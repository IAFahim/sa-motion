using System.Buffers.Binary;
using System.Text.Json;
using SA.Animation.Content;
using SA.Motion.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SA.Animation.Tests;

public class FamilyMintTests
{
    const int Bones = 56;

    readonly ITestOutputHelper output;

    public FamilyMintTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    static void LoadDescriptor(out string family, out uint version, out string[] names, out int[] parents, out Pose[] reference)
    {
        using var doc = JsonDocument.Parse(Fixture("humanoid-v1.safamily"));
        var root = doc.RootElement;
        family = root.GetProperty("family").GetString()!;
        version = root.GetProperty("familyVersion").GetUInt32();
        Assert.Equal("published", root.GetProperty("status").GetString());
        var bones = root.GetProperty("bones");
        Assert.Equal(Bones, bones.GetArrayLength());
        names = new string[Bones];
        parents = new int[Bones];
        reference = new Pose[Bones];
        var compiledParents = new int[Bones];
        var compiled = new Pose[Bones];
        Assert.Equal(AnimationError.None, CanonicalHumanoid.WriteReference(compiledParents, compiled));
        var index = 0;
        foreach (var bone in bones.EnumerateArray())
        {
            Assert.Equal(index, bone.GetProperty("index").GetInt32());
            names[index] = bone.GetProperty("name").GetString()!;
            parents[index] = bone.GetProperty("parent").GetInt32();
            var local = bone.GetProperty("referenceLocal");
            var p = local.GetProperty("position");
            var q = local.GetProperty("rotation");
            var s = local.GetProperty("scale");
            reference[index] = new Pose
            {
                Position = new Float3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
                Rotation = new Quaternion(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle()),
                Scale = new Float3(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle())
            };
            Assert.Equal(compiledParents[index], parents[index]);
            Assert.Equal(compiled[index].Position.X, reference[index].Position.X);
            Assert.Equal(compiled[index].Position.Y, reference[index].Position.Y);
            Assert.Equal(compiled[index].Position.Z, reference[index].Position.Z);
            Assert.Equal(compiled[index].Rotation.X, reference[index].Rotation.X);
            Assert.Equal(compiled[index].Rotation.Y, reference[index].Rotation.Y);
            Assert.Equal(compiled[index].Rotation.Z, reference[index].Rotation.Z);
            Assert.Equal(compiled[index].Rotation.W, reference[index].Rotation.W);
            Assert.Equal(compiled[index].Scale.X, reference[index].Scale.X);
            Assert.Equal(compiled[index].Scale.Y, reference[index].Scale.Y);
            Assert.Equal(compiled[index].Scale.Z, reference[index].Scale.Z);
            index++;
        }
    }

    static string Hex(AssetId id)
    {
        Span<byte> bytes = stackalloc byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, id.A);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(8), id.B);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(16), id.C);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(24), id.D);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [Fact]
    public void PublishedDescriptorMatchesCompiledReferenceExactly()
    {
        LoadDescriptor(out var family, out var version, out _, out _, out _);
        Assert.Equal("Humanoid", family);
        Assert.Equal(1u, version);
    }

    [Fact]
    public void FamilyIdIsStableAndScopedToNameAndVersion()
    {
        LoadDescriptor(out var family, out var version, out var names, out var parents, out var reference);
        Assert.Equal(AnimationError.None, ContentIdentity.Skeleton(family, version, names, parents, reference, out var first));
        Assert.False(first.IsEmpty);
        Assert.Equal(AnimationError.None, ContentIdentity.Skeleton(family, version, names, parents, reference, out var second));
        Assert.Equal(first, second);
        output.WriteLine("FamilyMint " + family + " v" + version + " " + Hex(first));
        Assert.Equal(AnimationError.None, ContentIdentity.Skeleton(family + "Other", version, names, parents, reference, out var renamed));
        Assert.NotEqual(first, renamed);
        Assert.Equal(AnimationError.None, ContentIdentity.Skeleton(family, version + 1, names, parents, reference, out var reversioned));
        Assert.NotEqual(first, reversioned);
    }
}

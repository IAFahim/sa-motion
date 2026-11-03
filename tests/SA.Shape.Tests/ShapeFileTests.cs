using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Shape.Tests;

public class ShapeFileTests
{
    internal static readonly string[] BoneNames = ["Root", "Hips", "LeftThigh", "LeftShin", "LeftFoot", "RightThigh", "RightShin", "RightFoot"];
    internal static readonly string[] MeshNames = ["leftCalf", "rightCalf"];
    static readonly int[] Parents = [-1, 0, 1, 2, 3, 1, 5, 6];
    const string TargetHex = "0500000000000000060000000000000007000000000000000800000000000000";
    const string GoldenHash = "f215f7ec40534784735bfcce78d59eec317357c8f57610b2740aa54b1d8e70f7";
    const string FreeHash = "e92fdb243bb7197e8d648c78e5f53950df2f19ac9b74189da7719de0ae5d4041";
    const string DocHead = "{\"schemaVersion\": 1, \"targetRigId\": \"" + TargetHex + "\", \"units\": \"metres\", \"up\": \"+Y\", \"grounding\": \"KeepBase\", \"supports\": [{\"bone\": 4, \"offset\": [0, -0.1, 0.05]}, {\"bone\": 7, \"offset\": [0, -0.1, 0.05]}]";

    internal static string FixturePath(string name)
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12; i++)
        {
            string candidate = Path.Combine(dir, "tests", "SA.Shape.Tests", "Fixtures", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            string? parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir)
            {
                break;
            }
            dir = parent;
        }
        return Path.Combine(AppContext.BaseDirectory, name);
    }

    internal static byte[] Utf8(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    internal static string HexOf(AssetId id)
    {
        byte[] bytes = new byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0), id.A);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), id.B);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), id.C);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), id.D);
        char[] chars = new char[64];
        const string digits = "0123456789abcdef";
        for (int i = 0; i < 32; i++)
        {
            chars[i * 2] = digits[bytes[i] >> 4];
            chars[i * 2 + 1] = digits[bytes[i] & 15];
        }
        return new string(chars);
    }

    internal static Float3[] BaseOffsets()
    {
        return [new Float3(0, 0, 0), new Float3(0, 1, 0), new Float3(-0.11f, -0.02f, 0), new Float3(0, -0.45f, 0),
            new Float3(0, -0.45f, 0), new Float3(0.11f, -0.02f, 0), new Float3(0, -0.45f, 0), new Float3(0, -0.45f, 0)];
    }

    static Pose[] Calibration()
    {
        Float3[] rest = BaseOffsets();
        Pose[] poses = new Pose[rest.Length];
        for (int i = 0; i < rest.Length; i++)
        {
            poses[i] = new Pose { Position = rest[i], Rotation = Quaternion.Identity, Scale = Float3.One };
        }
        return poses;
    }

    static void Compile(Pose[] calibration, int sourceHips, int targetHips, out RetargetBinding binding, out RetargetRow[] rows)
    {
        int[] map = new int[Parents.Length];
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = i;
        }
        rows = new RetargetRow[Parents.Length];
        Assert.Equal(AnimationError.None, RetargetTable.Compile(ShapeFixture.FamilyId, ShapeFixture.TargetId, Parents, calibration,
            Parents, calibration, map, sourceHips, targetHips, 1f, new RigidPose[Parents.Length * 2], rows, out binding));
    }

    static string Doc(string inner)
    {
        return DocHead + ", " + inner + "}";
    }

    [Fact]
    public void GoldenFileParsesAndHashMatchesCommittedValue()
    {
        byte[] json = File.ReadAllBytes(FixturePath("shape-golden.sashape"));
        Assert.Equal(AnimationError.None, ShapeFile.Load(json, BoneNames, MeshNames, ShapeFixture.TargetId, BaseOffsets(), out ShapeAsset? asset));
        ShapeAsset a = Assert.IsType<ShapeAsset>(asset);
        Assert.Equal(ShapeFixture.TargetId, a.Target);
        Assert.Equal(ShapeGrounding.KeepBase, a.Grounding);
        Assert.Equal(4, a.SupportA.Bone);
        Assert.Equal(new Float3(0, -0.1f, 0.05f), a.SupportA.Offset);
        Assert.Equal(7, a.SupportB.Bone);
        Assert.Equal(0.25f, a.MinimumLength);
        Assert.Equal(4f, a.MaximumLength);
        Assert.Equal(0.125f, a.MinimumSkinScale);
        Assert.Equal(8f, a.MaximumSkinScale);
        Assert.Equal(3, a.Channels.Length);
        Assert.Equal(1u, a.Channels[0].Id);
        Assert.Equal(2u, a.Channels[1].Id);
        Assert.Equal(3u, a.Channels[2].Id);
        Assert.Equal(6, a.Offsets.Length);
        Assert.Equal(6, a.Skin.Length);
        Assert.Equal(8, a.Frames.Length);
        Assert.Equal(2, a.Morphs.Length);
        Assert.Equal("leftCalf", a.MorphNames[0]);
        Assert.Equal(2, a.MorphEffects.Length);
        Assert.Equal(GoldenHash, HexOf(a.Definition));
        Assert.True(ShapeFile.CanonicalBytes(a, out byte[] first) == AnimationError.None);
        Assert.True(ShapeFile.CanonicalBytes(a, out byte[] second) == AnimationError.None);
        Assert.Equal(first, second);
        Assert.Equal(GoldenHash, HexOf(ShapeFile.DefinitionId(first)));
    }

    [Fact]
    public void GoldenNeutralRestoresBaseAndExtremaStayBounded()
    {
        byte[] json = File.ReadAllBytes(FixturePath("shape-golden.sashape"));
        Assert.Equal(AnimationError.None, ShapeFile.Load(json, BoneNames, MeshNames, ShapeFixture.TargetId, BaseOffsets(), out ShapeAsset? asset));
        ShapeAsset a = Assert.IsType<ShapeAsset>(asset);
        Compile(Calibration(), 1, 1, out RetargetBinding binding, out RetargetRow[] rows);
        Float3[] offsets = new Float3[8];
        float[] lengths = new float[8];
        float[] scales = new float[8];
        Float3[] skinScales = new Float3[8];
        Affine[] deformations = new Affine[8];
        RigidPose[] models = new RigidPose[8];
        ShapeWorkspace workspace = new ShapeWorkspace(offsets, lengths, scales, skinScales, deformations, models);
        float[] values = new float[3];
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(a.View(), binding, rows, values, 1, workspace, out ShapeGeometry geometry));
        Float3[] rest = BaseOffsets();
        for (int i = 0; i < rest.Length; i++)
        {
            Assert.Equal(rest[i], offsets[i]);
        }
        Assert.Equal(1f, geometry.Retarget.HeightRatio);
        float[][] corners = [[1, 1, 1], [-1, -1, -1], [0, 1, -1], [0.5f, -0.3f, 0.7f]];
        foreach (float[] corner in corners)
        {
            Assert.Equal(AnimationError.None, ShapeRig.Evaluate(a.View(), binding, rows, corner, 2, workspace, out _));
            for (int i = 0; i < rest.Length; i++)
            {
                Assert.InRange(lengths[i], 0.25f, 4f);
            }
        }
        float[] weights = new float[2];
        Assert.Equal(AnimationError.None, ShapeMorphs.Evaluate(a.Channels, values, a.Morphs, a.MorphEffects, weights));
        Assert.Equal(new float[] { 0f, 0f }, weights);
        Assert.Equal(AnimationError.None, ShapeMorphs.Evaluate(a.Channels, [0f, 1f, 0f], a.Morphs, a.MorphEffects, weights));
        Assert.Equal(new float[] { 0.5f, 0f }, weights);
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(a.View(), binding, rows, [1f, 0f, 0f], 3, workspace, out _));
        Assert.InRange(lengths[3], 1.249f, 1.251f);
    }

    [Fact]
    public void FreeGroundingFixtureLoadsWithoutFramesOrMorphs()
    {
        byte[] json = File.ReadAllBytes(FixturePath("shape-free.sashape"));
        Assert.Equal(AnimationError.None, ShapeFile.Load(json, BoneNames, [], ShapeFixture.TargetId, BaseOffsets(), out ShapeAsset? asset));
        ShapeAsset a = Assert.IsType<ShapeAsset>(asset);
        Assert.Equal(ShapeGrounding.Free, a.Grounding);
        Assert.Empty(a.Frames);
        Assert.Empty(a.Morphs);
        Assert.Empty(a.MorphEffects);
        Assert.Equal(FreeHash, HexOf(a.Definition));
        Compile(Calibration(), -1, -1, out RetargetBinding binding, out RetargetRow[] rows);
        Float3[] offsets = new Float3[8];
        float[] lengths = new float[8];
        float[] scales = new float[8];
        Float3[] skinScales = new Float3[8];
        Affine[] deformations = new Affine[8];
        RigidPose[] models = new RigidPose[8];
        ShapeWorkspace workspace = new ShapeWorkspace(offsets, lengths, scales, skinScales, deformations, models);
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(a.View(), binding, rows, [0f], 1, workspace, out _));
        Float3[] rest = BaseOffsets();
        for (int i = 0; i < rest.Length; i++)
        {
            Assert.Equal(rest[i], offsets[i]);
        }
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(a.View(), binding, rows, [1f], 2, workspace, out _));
        Assert.InRange(lengths[2], 1.499f, 1.501f);
    }

    [Fact]
    public void WhitespaceAndUnknownFieldsDoNotChangeCanonicalBytes()
    {
        string golden = File.ReadAllText(FixturePath("shape-golden.sashape"));
        Assert.Equal(AnimationError.None, ShapeFile.Parse(Utf8(golden), BoneNames, MeshNames, ShapeFixture.TargetId, BaseOffsets(), out ShapeAsset? first));
        string variant = "\n  " + golden.Replace("\"morphEffects\"", "\"provenance\": {\"author\": \"shape\", \"tags\": [\"a\", \"b\"]}, \"morphEffects\"") + "\n";
        Assert.Equal(AnimationError.None, ShapeFile.Parse(Utf8(variant), BoneNames, MeshNames, ShapeFixture.TargetId, BaseOffsets(), out ShapeAsset? second));
        ShapeAsset a = Assert.IsType<ShapeAsset>(first);
        ShapeAsset b = Assert.IsType<ShapeAsset>(second);
        Assert.True(ShapeFile.CanonicalBytes(a, out byte[] aBytes) == AnimationError.None);
        Assert.True(ShapeFile.CanonicalBytes(b, out byte[] bBytes) == AnimationError.None);
        Assert.Equal(aBytes, bBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"a\":}")]
    [InlineData("{\"schemaVersion\": 2}")]
    [InlineData("{\"schemaVersion\": 1, \"schemaVersion\": 1}")]
    [InlineData("{\"schemaVersion\": 1, \"targetRigId\": \"abc\"}")]
    [InlineData("{\"schemaVersion\": 1, \"targetRigId\": \"" + TargetHex + "\", \"units\": \"meters\"}")]
    [InlineData("{\"schemaVersion\": 1, \"targetRigId\": \"" + TargetHex + "\", \"units\": \"metres\", \"up\": \"-Y\"}")]
    [InlineData("{\"schemaVersion\": 1, \"targetRigId\": \"" + TargetHex + "\", \"units\": \"metres\", \"up\": \"+Y\", \"grounding\": \"Nope\"}")]
    [InlineData(DocHead + ", \"lengthRange\": [1]}")]
    [InlineData(DocHead + "} trailing")]
    [InlineData(DocHead + ", \"units\": \"metres\"}")]
    public void MalformedJsonReturnsInvalidFormat(string body)
    {
        Assert.Equal(AnimationError.InvalidFormat, ShapeFile.Parse(Utf8(body), BoneNames, [], default, BaseOffsets(), out _));
    }

    [Fact]
    public void InvalidUtf8AndDeepNestingReturnInvalidFormat()
    {
        Assert.Equal(AnimationError.InvalidFormat, ShapeFile.Parse(new byte[] { 255, 254, 0, 128 }, BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFormat, ShapeFile.Parse(Utf8("{\"k\": " + new string('[', 40)), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFormat, ShapeFile.Parse(Utf8("{\"k\": \"\\ud800\"}"), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFormat, ShapeFile.Parse(Utf8("{\"k\": \"a\u0001b\"}"), BoneNames, [], default, BaseOffsets(), out _));
    }

    [Fact]
    public void CorruptDefinitionIdReturnsCorruptData()
    {
        string golden = File.ReadAllText(FixturePath("shape-golden.sashape"));
        string flipped = golden.Replace(GoldenHash, GoldenHash.Substring(0, 63) + (GoldenHash[63] == '0' ? "1" : "0"));
        Assert.Equal(AnimationError.CorruptData, ShapeFile.Load(Utf8(flipped), BoneNames, MeshNames, ShapeFixture.TargetId, BaseOffsets(), out _));
        string stripped = golden.Replace("  \"definitionId\": \"" + GoldenHash + "\",\n", "");
        Assert.Equal(AnimationError.InvalidFormat, ShapeFile.Load(Utf8(stripped), BoneNames, MeshNames, ShapeFixture.TargetId, BaseOffsets(), out _));
    }

    [Fact]
    public void BadReferencesReturnInvalidTable()
    {
        string golden = File.ReadAllText(FixturePath("shape-golden.sashape"));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden.Replace("\"LeftFoot\"", "\"NoSuchBone\"")), BoneNames, MeshNames, default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden.Replace("\"bone\": 2", "\"bone\": 0")), BoneNames, MeshNames, default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden.Replace("\"channel\": 0", "\"channel\": 9")), BoneNames, MeshNames, default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden.Replace("\"morph\": \"leftCalf\"", "\"morph\": \"nope\"")), BoneNames, MeshNames, default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden.Replace("\"lengthRange\": [0.25, 4]", "\"lengthRange\": [2, 1]")), BoneNames, MeshNames, default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden.Replace("    { \"basis\": [0, 0, 0, -1], \"pivot\": [0, 0, 0] },\n", "")), BoneNames, MeshNames, default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden), BoneNames, MeshNames, new AssetId(1, 2, 3, 4), BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden), BoneNames, [], default, BaseOffsets(), out _));
    }

    [Fact]
    public void BadChannelsReturnInvalidFrame()
    {
        Assert.Equal(AnimationError.InvalidFrame, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 1, \"min\": -1, \"max\": 1}, {\"id\": 1, \"min\": -1, \"max\": 1}]")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFrame, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 1, \"min\": 1, \"max\": -1}]")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFrame, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 1, \"min\": -1, \"max\": 1, \"default\": 5}]")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFrame, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 1, \"min\": -1, \"max\": 1, \"halfLifeSeconds\": -2}]")), BoneNames, [], default, BaseOffsets(), out _));
    }

    [Fact]
    public void IntervalOverflowAndReversalAreRejected()
    {
        Assert.Equal(AnimationError.InvalidFrame, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 1, \"min\": -1, \"max\": 1}], \"offsetEffects\": [{\"channel\": 0, \"bone\": 2, \"logLength\": 100}]")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFrame, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 1, \"min\": -1, \"max\": 1}], \"skinEffects\": [{\"channel\": 0, \"bone\": 2, \"logScale\": [0, 100, 0]}]")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 1, \"min\": -1, \"max\": 1}], \"offsetEffects\": [{\"channel\": 0, \"bone\": 2, \"delta\": [50, 0, 0]}]")), BoneNames, [], default, BaseOffsets(), out _));
        StringBuilder many = new StringBuilder("[");
        for (int i = 0; i < 129; i++)
        {
            if (i != 0)
            {
                many.Append(", ");
            }
            many.Append("{\"id\": ").Append(i).Append(", \"min\": -1, \"max\": 1}");
        }
        many.Append("]");
        Assert.Equal(AnimationError.CapacityExceeded, ShapeFile.Parse(Utf8(Doc("\"channels\": " + many.ToString())), BoneNames, [], default, BaseOffsets(), out _));
    }

    [Fact]
    public void HostileInputNeverThrows()
    {
        Assert.NotEqual(AnimationError.None, ShapeFile.Parse(Array.Empty<byte>(), BoneNames, [], default, BaseOffsets(), out _));
        Assert.NotEqual(AnimationError.None, ShapeFile.Parse(new byte[] { 0, 1, 2, 255 }, BoneNames, [], default, BaseOffsets(), out _));
        Assert.NotEqual(AnimationError.None, ShapeFile.Parse(Utf8("{"), BoneNames, [], default, BaseOffsets(), out _));
        Assert.NotEqual(AnimationError.None, ShapeFile.Parse(Utf8("[1, 2, 3]"), BoneNames, [], default, BaseOffsets(), out _));
        Assert.NotEqual(AnimationError.None, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 1, \"min\": -1e999, \"max\": 1}]")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.NotEqual(AnimationError.None, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 1, \"min\": -1, \"max\": 1}]")), new string[0], [], default, BaseOffsets(), out _));
    }
}

using System.Buffers.Binary;
using System.Text.Json;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;
using SA.Soma;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: sa-soma-bake <humanoid-v1.safamily> <in.saib> <out.saclip>");
    return 2;
}

var family = LoadFamily(args[0], out var familyId, out var parents, out var reference);
if (!family) return 1;

var bytes = File.ReadAllBytes(args[1]);
if (!ReadMotion(bytes, out var frames, out var poses, out var contacts, out var fps)) return 1;

var error = SomaRetarget.Create(familyId, out var retarget);
if (error != AnimationError.None) return Fail("retarget " + error);

var samples = new Pose[frames * CanonicalHumanoid.BoneCount];
var root = new RigidPose[frames];
error = SomaClip.Convert(retarget!, poses, frames, SomaRig.SkeletonJointCount, SomaSpace.ModelYUp, samples, root);
if (error != AnimationError.None) return Fail("convert " + error);

var saContacts = new float[frames * SomaContacts.SaChannels];
for (var f = 0; f < frames; f++)
{
    error = SomaContacts.Merge(contacts.AsSpan(f * SomaContacts.SomaChannels, SomaContacts.SomaChannels), saContacts.AsSpan(f * SomaContacts.SaChannels, SomaContacts.SaChannels));
    if (error != AnimationError.None) return Fail("contacts " + error);
}

error = SomaClip.Encode(retarget!, samples, root, saContacts, frames, fps, out var clip, out var report);
if (error != AnimationError.None) return Fail("encode " + error);

error = SharedClipReader.Load(clip, familyId, reference, out var loaded);
if (error != AnimationError.None || loaded == null) return Fail("readback " + error);

File.WriteAllBytes(args[2], clip);
Console.WriteLine($"wrote {args[2]}: {frames} frames, {clip.Length} bytes, {report.Segments} segments");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine("error: " + message);
    return 1;
}

static bool LoadFamily(string path, out AssetId id, out int[] parents, out Pose[] reference)
{
    using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
    var rootElement = doc.RootElement;
    var name = rootElement.GetProperty("family").GetString()!;
    var version = rootElement.GetProperty("familyVersion").GetUInt32();
    var bones = rootElement.GetProperty("bones");
    var count = bones.GetArrayLength();
    var names = new string[count];
    parents = new int[count];
    reference = new Pose[count];
    var index = 0;
    foreach (var bone in bones.EnumerateArray())
    {
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
        index++;
    }

    var error = ContentIdentity.Skeleton(name, version, names, parents, reference, out id);
    if (error != AnimationError.None)
    {
        Console.Error.WriteLine("error: family identity " + error);
        return false;
    }

    return true;
}

static bool ReadMotion(byte[] bytes, out int frames, out float[] poses, out float[] contacts, out float fps)
{
    frames = 0;
    poses = Array.Empty<float>();
    contacts = Array.Empty<float>();
    fps = 0f;
    if (bytes.Length < 52 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x42494153u)
    {
        Console.Error.WriteLine("error: not a .saib file");
        return false;
    }

    var version = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
    var axes = bytes[6];
    var units = bytes[7];
    fps = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
    frames = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10));
    var joints = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12));
    if (version != 1 || axes != 0 || units != 0 || joints != SomaRig.SkeletonJointCount || frames < 2)
    {
        Console.Error.WriteLine("error: unsupported .saib (need version 1, Y-up, metres, 77 joints, 2+ frames)");
        return false;
    }

    var recipeLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48));
    var start = 52 + recipeLength;
    var poseFloats = frames * joints * 7;
    var expected = start + ((poseFloats + (frames * 7) + (frames * 4)) * sizeof(float));
    if (recipeLength < 0 || bytes.Length != expected)
    {
        Console.Error.WriteLine("error: .saib length does not match its header");
        return false;
    }

    poses = new float[poseFloats];
    Buffer.BlockCopy(bytes, start, poses, 0, poseFloats * sizeof(float));
    contacts = new float[frames * 4];
    Buffer.BlockCopy(bytes, start + ((poseFloats + (frames * 7)) * sizeof(float)), contacts, 0, contacts.Length * sizeof(float));
    return true;
}

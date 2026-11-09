using System.Buffers.Binary;
using System.Text.Json;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;
using SA.Soma;

if (args.Length > 0 && args[0] == "pose")
{
    var rest = args.ToList();
    var keepRoot = false;
    var at = rest.IndexOf("--root");
    if (at >= 0)
    {
        if (at + 1 >= rest.Count || rest[at + 1] != "last") return Fail("--root takes 'last'");
        keepRoot = true;
        rest.RemoveRange(at, 2);
    }

    if (rest.Count == 5) return ExtractPose(rest[1], rest[2], rest[3], rest[4], "", keepRoot);
    if (rest.Count == 6) return ExtractPose(rest[1], rest[2], rest[3], rest[4], rest[5], keepRoot);
}

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: sa-soma-bake <humanoid-v1.safamily> <in.saib> <out.saclip>");
    Console.Error.WriteLine("       sa-soma-bake pose <humanoid-v1.safamily> <in.saclip> <time[,time...]> <soma_rest.pose> <out.pose|out_{}.pose> [--root last]");
    return 2;
}

try
{
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
}
catch (IOException exception)
{
    return Fail(exception.Message);
}
catch (UnauthorizedAccessException exception)
{
    return Fail(exception.Message);
}
catch (JsonException exception)
{
    return Fail("family file: " + exception.Message);
}

static int ExtractPose(string familyPath, string clipPath, string time, string restPath, string outPath, bool keepRoot)
{
    try
    {
        if (outPath.Length == 0) return Fail("pose needs <out.pose>");
        if (!LoadFamily(familyPath, out var familyId, out _, out var reference)) return 1;
        var clipBytes = File.ReadAllBytes(clipPath);
        var error = SharedClipReader.Load(clipBytes, familyId, reference, out var clip);
        if (error != AnimationError.None || clip == null) return Fail("clip " + error);
        var tokens = time.Split(',');
        if (tokens.Length > 1 && !outPath.Contains("{}")) return Fail("several times need '{}' in <out.pose>");
        var times = new double[tokens.Length];
        var step = clip.SampleCount > 1 ? clip.Duration / (clip.SampleCount - 1) : 0.0;
        for (var i = 0; i < tokens.Length; i++)
            if (!ParseTime(tokens[i].Trim(), clip.Duration, step, out times[i])) return Fail("time must be seconds, s+Nf or e-Nf");

        var restBytes = File.ReadAllBytes(restPath);
        const int floats = SomaRig.SkeletonJointCount * SomaFrame.FloatsPerJoint;
        if (restBytes.Length != floats * sizeof(float)) return Fail("rest pose must be 539 float32 values");
        var rest = new float[floats];
        Buffer.BlockCopy(restBytes, 0, rest, 0, restBytes.Length);
        var skeletonReference = new Pose[SomaRig.SkeletonJointCount];
        for (var j = 0; j < skeletonReference.Length; j++)
        {
            var at = j * SomaFrame.FloatsPerJoint;
            skeletonReference[j] = new Pose
            {
                Position = new Float3(rest[at], rest[at + 1], rest[at + 2]),
                Rotation = Quaternion.Normalize(new Quaternion(rest[at + 3], rest[at + 4], rest[at + 5], rest[at + 6])),
                Scale = Float3.One
            };
        }

        error = SomaRetarget.Create(familyId, out var retarget);
        if (error != AnimationError.None) return Fail("retarget " + error);
        for (var i = 0; i < times.Length; i++)
        {
            var sa = new Pose[CanonicalHumanoid.BoneCount];
            error = clip.Sample(times[i], reference, sa);
            if (error != AnimationError.None) return Fail("sample " + error);
            var soma = new Pose[SomaRig.ModelJointCount];
            var saRoot = RigidPose.Identity;
            if (keepRoot && clip.Root.Length == clip.SampleCount && !RelativeRoot(clip, times[i], times[times.Length - 1], step, out saRoot)) return Fail("root track unusable");
            error = SomaUnretarget.ToSoma(retarget!, sa, saRoot, soma);
            if (error != AnimationError.None) return Fail("unretarget " + error);
            var skeleton = new Pose[SomaRig.SkeletonJointCount];
            error = SomaUnretarget.ToSkeleton(soma, skeletonReference, skeleton);
            if (error != AnimationError.None) return Fail("skeleton " + error);
            var output = new float[floats];
            error = SomaUnretarget.Write(skeleton, output);
            if (error != AnimationError.None) return Fail("write " + error);
            var bytes = new byte[output.Length * sizeof(float)];
            Buffer.BlockCopy(output, 0, bytes, 0, bytes.Length);
            var target = tokens.Length > 1 ? outPath.Replace("{}", i.ToString(System.Globalization.CultureInfo.InvariantCulture)) : outPath;
            File.WriteAllBytes(target, bytes);
            Console.WriteLine($"wrote {target}: t={times[i]:F3}s of {clip.Duration:F3}s");
        }

        return 0;
    }
    catch (IOException exception)
    {
        return Fail(exception.Message);
    }
    catch (UnauthorizedAccessException exception)
    {
        return Fail(exception.Message);
    }
    catch (JsonException exception)
    {
        return Fail("family file: " + exception.Message);
    }
}

static bool RelativeRoot(SharedClip clip, double seconds, double lastSeconds, double step, out RigidPose relative)
{
    relative = RigidPose.Identity;
    if (step <= 0.0) return true;
    var last = clip.Root[Math.Clamp((int)Math.Round(lastSeconds / step), 0, clip.SampleCount - 1)];
    var current = clip.Root[Math.Clamp((int)Math.Round(seconds / step), 0, clip.SampleCount - 1)];
    return RigidMath.TryInverse(last, out var inverse) && RigidMath.TryCompose(inverse, current, out relative);
}

static bool ParseTime(string token, double duration, double step, out double seconds)
{
    seconds = 0.0;
    var culture = System.Globalization.CultureInfo.InvariantCulture;
    if (token == "end" || token == "e") seconds = duration;
    else if (token == "start" || token == "s") seconds = 0.0;
    else if ((token.StartsWith("s+") || token.StartsWith("e-")) && token.EndsWith("f"))
    {
        if (!int.TryParse(token.AsSpan(2, token.Length - 3), System.Globalization.NumberStyles.None, culture, out var frames)) return false;
        seconds = token[0] == 's' ? frames * step : duration - frames * step;
    }
    else if (!double.TryParse(token, System.Globalization.NumberStyles.Float, culture, out seconds)) return false;
    seconds = Math.Clamp(seconds, 0.0, duration);
    return true;
}

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

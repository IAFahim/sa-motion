using System.Security.Cryptography;
using System.Text.Json;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;

var scalar = ScalarDecay.Create(1f, 0f, 1f).Evaluate(0.5f);
if (Math.Abs(scalar.Value - 0.5f) > 1e-5f) throw new Exception("Packed scalar kernel failed");
var source = Pose.Identity;
source.Position = new Float3(1f, 2f, 3f);
source.Rotation = Quaternion.AxisAngle(new Float3(0, 1, 0), 1f);
source.Scale = new Float3(2, 3, 4);
var history = new PoseHistory();
history.Reset(source);
var transition = PoseTransition.Create(history, Pose.Identity, 1f);
var output = PoseTransition.Apply(ref transition, Pose.Identity, 0.5f);
var library = typeof(ScalarDecay).Assembly.Location;
var result = new
{
    Version = typeof(ScalarDecay).Assembly.GetName().Version!.ToString(),
    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))).ToLowerInvariant(),
    Scalar = scalar.Value,
    Position = new[] { output.Position.X, output.Position.Y, output.Position.Z },
    Rotation = new[] { output.Rotation.X, output.Rotation.Y, output.Rotation.Z, output.Rotation.W },
    Scale = new[] { output.Scale.X, output.Scale.Y, output.Scale.Z },
    References = typeof(ScalarDecay).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray()
};
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

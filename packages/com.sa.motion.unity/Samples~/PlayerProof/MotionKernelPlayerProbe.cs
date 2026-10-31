using System;
using System.IO;
using System.Security.Cryptography;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using global::Unity.Burst;
using global::Unity.Collections;
using global::Unity.Jobs;
using global::UnityEngine;
using Pose = SA.Motion.Numerics.Pose;
using MotionQuaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Unity.Proof
{
    public class MotionKernelPlayerProbe : MonoBehaviour
    {
        public struct Input
        {
            public Pose Current;
            public Pose Previous;
            public Pose Destination;
            public float HistoryInterval;
            public float Duration;
            public float Elapsed;
            public float ScalarMagnitude;
            public float ScalarVelocity;
            public float ScalarDuration;
        }

        public struct Result
        {
            public Pose Pose;
            public DecaySample Scalar;
            public int Native;
        }

        [Serializable]
        public class Golden
        {
            public string Sha256;
            public float Scalar;
            public float[] Position;
            public float[] Rotation;
            public float[] Scale;
        }

        [Serializable]
        public class InputData
        {
            public float[] CurrentPosition;
            public float[] CurrentRotation;
            public float[] CurrentScale;
            public float HistoryInterval;
            public float Duration;
            public float Elapsed;
            public float ScalarMagnitude;
            public float ScalarVelocity;
            public float ScalarDuration;
        }

        [Serializable]
        public class BuildInput
        {
            public string VerifiedSha256;
            public string Framework;
        }

        [Serializable]
        public class Report
        {
            public bool Passed;
            public int NativeExecution;
            public int ManagedControl;
            public string ExpectedFixtureSha256;
            public string VerifiedBuildInputSha256;
            public string LoadedDllSha256;
            public bool RuntimeDllHashAvailable;
            public string UnityVersion;
            public string ScriptingBackend;
            public string Error;
            public float Tolerance;
            public float MaxNativeManagedError;
            public float[] ObservedPosition;
            public float[] ObservedRotation;
            public float[] ObservedScale;
            public float ObservedScalar;
            public float ObservedScalarVelocity;
            public float ObservedScalarAcceleration;
        }

        [BurstDiscard]
        public static void MarkManaged(ref int value) => value = 0;

        public static Result Evaluate(in Input input)
        {
            var history = new PoseHistory { Current = input.Current, Previous = input.Previous, Interval = input.HistoryInterval, Count = 2 };
            var transition = PoseTransition.Create(history, input.Destination, input.Duration);
            var native = 1;
            MarkManaged(ref native);
            return new Result
            {
                Pose = PoseTransition.Apply(ref transition, input.Destination, input.Elapsed),
                Scalar = ScalarDecay.Create(input.ScalarMagnitude, input.ScalarVelocity, input.ScalarDuration).Evaluate(input.Elapsed),
                Native = native
            };
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        public struct ProbeJob : IJob
        {
            [ReadOnly] public NativeArray<Input> Inputs;
            public NativeArray<Result> Output;
            public void Execute() => Output[0] = Evaluate(Inputs[0]);
        }

        public static string HashLoadedLibrary()
        {
            var path = typeof(ScalarDecay).Assembly.Location;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return "";
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        static float QuaternionError(MotionQuaternion a, MotionQuaternion b)
        {
            var same = Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), Math.Max(Math.Abs(a.Z - b.Z), Math.Abs(a.W - b.W)));
            var opposite = Math.Max(Math.Max(Math.Abs(a.X + b.X), Math.Abs(a.Y + b.Y)), Math.Max(Math.Abs(a.Z + b.Z), Math.Abs(a.W + b.W)));
            return Math.Min(same, opposite);
        }

        static float VectorError(Float3 a, Float3 b) => Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
        static Float3 Vector(float[] a) => new Float3(a[0], a[1], a[2]);
        static MotionQuaternion Rotation(float[] a) => new MotionQuaternion(a[0], a[1], a[2], a[3]);

        void Start()
        {
            var report = new Report { UnityVersion = Application.unityVersion, Tolerance = 2e-4f };
#if ENABLE_IL2CPP
            report.ScriptingBackend = "IL2CPP";
#elif ENABLE_CORECLR
            report.ScriptingBackend = "CoreCLR";
#elif ENABLE_MONO
            report.ScriptingBackend = "Mono";
#else
            report.ScriptingBackend = "Unknown";
#endif
            try
            {
                var golden = JsonUtility.FromJson<Golden>(Resources.Load<TextAsset>("SA.Motion.Player/PackageSmoke").text);
                var data = JsonUtility.FromJson<InputData>(Resources.Load<TextAsset>("SA.Motion.Player/Input").text);
                var build = JsonUtility.FromJson<BuildInput>(Resources.Load<TextAsset>("SA.Motion.Player/BuildInput").text);
                report.ExpectedFixtureSha256 = golden.Sha256;
                report.VerifiedBuildInputSha256 = build.VerifiedSha256;
                report.LoadedDllSha256 = HashLoadedLibrary();
                report.RuntimeDllHashAvailable = report.LoadedDllSha256.Length > 0;
                if (build.VerifiedSha256 != golden.Sha256 || (report.RuntimeDllHashAvailable && report.LoadedDllSha256 != golden.Sha256))
                    throw new InvalidOperationException("Fixture, verified build input and loaded DLL identities disagree");
                var current = new Pose { Position = Vector(data.CurrentPosition), Rotation = Rotation(data.CurrentRotation), Scale = Vector(data.CurrentScale) };
                var input = new Input
                {
                    Current = current, Previous = current, Destination = Pose.Identity,
                    HistoryInterval = data.HistoryInterval, Duration = data.Duration, Elapsed = data.Elapsed,
                    ScalarMagnitude = data.ScalarMagnitude, ScalarVelocity = data.ScalarVelocity, ScalarDuration = data.ScalarDuration
                };
                var managed = Evaluate(input);
                report.ManagedControl = managed.Native;
                using var inputs = new NativeArray<Input>(new[] { input }, Allocator.TempJob);
                using var output = new NativeArray<Result>(1, Allocator.TempJob);
                new ProbeJob { Inputs = inputs, Output = output }.Schedule().Complete();
                var result = output[0];
                report.NativeExecution = result.Native;
                report.ObservedPosition = new[] { result.Pose.Position.X, result.Pose.Position.Y, result.Pose.Position.Z };
                report.ObservedRotation = new[] { result.Pose.Rotation.X, result.Pose.Rotation.Y, result.Pose.Rotation.Z, result.Pose.Rotation.W };
                report.ObservedScale = new[] { result.Pose.Scale.X, result.Pose.Scale.Y, result.Pose.Scale.Z };
                report.ObservedScalar = result.Scalar.Value;
                report.ObservedScalarVelocity = result.Scalar.Velocity;
                report.ObservedScalarAcceleration = result.Scalar.Acceleration;
                report.MaxNativeManagedError = Math.Max(Math.Max(VectorError(managed.Pose.Position, result.Pose.Position), VectorError(managed.Pose.Scale, result.Pose.Scale)), QuaternionError(managed.Pose.Rotation, result.Pose.Rotation));
                report.MaxNativeManagedError = Math.Max(report.MaxNativeManagedError, Math.Max(Math.Abs(managed.Scalar.Value - result.Scalar.Value), Math.Max(Math.Abs(managed.Scalar.Velocity - result.Scalar.Velocity), Math.Abs(managed.Scalar.Acceleration - result.Scalar.Acceleration))));
                var fixtureError = Math.Max(Math.Abs(golden.Scalar - result.Scalar.Value), Math.Max(VectorError(Vector(golden.Position), result.Pose.Position), Math.Max(VectorError(Vector(golden.Scale), result.Pose.Scale), QuaternionError(Rotation(golden.Rotation), result.Pose.Rotation))));
                report.Passed = result.Native == 1 && managed.Native == 0 && report.MaxNativeManagedError <= report.Tolerance && fixtureError <= report.Tolerance;
                if (!report.Passed)
                    report.Error = $"Native/managed execution or numerical mismatch: fixtureError={fixtureError}, nativeManagedError={report.MaxNativeManagedError}";
            }
            catch (Exception error)
            {
                report.Error = error.ToString();
                report.Passed = false;
            }
            var arguments = Environment.GetCommandLineArgs();
            var path = Path.Combine(Application.persistentDataPath, "sa-motion-player-proof.json");
            for (var i = 0; i + 1 < arguments.Length; i++)
                if (arguments[i] == "-sa-motion-report")
                    path = arguments[i + 1];
            var exit = report.Passed ? 0 : 2;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
            }
            catch (Exception error)
            {
                Debug.LogError("SA Motion proof could not write its report: " + error.Message);
                exit = 3;
            }
            finally
            {
                Application.Quit(exit);
            }
        }
    }
}

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using global::Unity.Burst;
using global::Unity.Collections;
using global::Unity.Collections.LowLevel.Unsafe;
using global::Unity.Jobs;
using global::UnityEngine;
using Pose = SA.Motion.Numerics.Pose;
using MotionQuaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Unity.Tests
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ProbeInput
    {
        public Pose Current;
        public Pose Previous;
        public Pose Destination;
        public float HistoryInterval;
        public float Duration;
        public float Elapsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProbeOutput
    {
        public Pose Pose;
        public DecaySample Scalar;
        public int NativeExecution;
    }

    [Serializable]
    public class PackageGolden
    {
        public string Version;
        public string Sha256;
        public float Scalar;
        public float[] Position;
        public float[] Rotation;
        public float[] Scale;
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    public static class PortableProbe
    {
        [BurstDiscard]
        public static void MarkManaged(ref int value) => value = 0;

        public static ProbeOutput Evaluate(in ProbeInput input)
        {
            var history = new PoseHistory { Current = input.Current, Previous = input.Previous, Interval = input.HistoryInterval, Count = 2 };
            var transition = PoseTransition.Create(history, input.Destination, input.Duration);
            var native = 1;
            MarkManaged(ref native);
            return new ProbeOutput
            {
                Pose = PoseTransition.Apply(ref transition, input.Destination, input.Elapsed),
                Scalar = ScalarDecay.Create(1f, 0f, 1f).Evaluate(input.Elapsed),
                NativeExecution = native
            };
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate void Kernel(ProbeInput* input, ProbeOutput* output);

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        [AOT.MonoPInvokeCallback(typeof(Kernel))]
        public static unsafe void Execute(ProbeInput* input, ProbeOutput* output) => *output = Evaluate(*input);
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    public struct PortableJob : IJob
    {
        [ReadOnly] public NativeArray<ProbeInput> Inputs;
        public NativeArray<ProbeOutput> Outputs;
        public void Execute()
        {
            for (var i = 0; i < Inputs.Length; i++)
                Outputs[i] = PortableProbe.Evaluate(Inputs[i]);
        }
    }

    public class BurstKernelTests
    {
        static ProbeInput GoldenInput()
        {
            var source = Pose.Identity;
            source.Position = new Float3(1, 2, 3);
            source.Rotation = MotionQuaternion.AxisAngle(new Float3(0, 1, 0), 1f);
            source.Scale = new Float3(2, 3, 4);
            return new ProbeInput { Current = source, Previous = source, Destination = Pose.Identity, HistoryInterval = 0.02f, Duration = 1f, Elapsed = 0.5f };
        }

        static void Near(float expected, float actual) => Assert.LessOrEqual(Math.Abs(expected - actual), 2e-4f);
        static void Compare(in ProbeOutput expected, in ProbeOutput actual)
        {
            Near(expected.Pose.Position.X, actual.Pose.Position.X);
            Near(expected.Pose.Position.Y, actual.Pose.Position.Y);
            Near(expected.Pose.Position.Z, actual.Pose.Position.Z);
            Near(1f, Math.Abs(MotionQuaternion.Dot(expected.Pose.Rotation, actual.Pose.Rotation)));
            Near(expected.Pose.Scale.X, actual.Pose.Scale.X);
            Near(expected.Pose.Scale.Y, actual.Pose.Scale.Y);
            Near(expected.Pose.Scale.Z, actual.Pose.Scale.Z);
            Near(expected.Scalar.Value, actual.Scalar.Value);
            Near(expected.Scalar.Velocity, actual.Scalar.Velocity);
            Near(expected.Scalar.Acceleration, actual.Scalar.Acceleration);
        }

        [Test]
        public void PackedDllMatchesTheDotNetPackageConsumer()
        {
            var golden = JsonUtility.FromJson<PackageGolden>(Resources.Load<TextAsset>("SA.Motion/PackageSmoke").text);
            var input = GoldenInput();
            var actual = PortableProbe.Evaluate(input);
            Assert.AreEqual(0, actual.NativeExecution);
            Near(golden.Scalar, actual.Scalar.Value);
            Near(golden.Position[0], actual.Pose.Position.X);
            Near(golden.Position[1], actual.Pose.Position.Y);
            Near(golden.Position[2], actual.Pose.Position.Z);
            Near(golden.Rotation[1], actual.Pose.Rotation.Y);
            Near(golden.Rotation[3], actual.Pose.Rotation.W);
            Near(golden.Scale[0], actual.Pose.Scale.X);
            Near(golden.Scale[1], actual.Pose.Scale.Y);
            Near(golden.Scale[2], actual.Pose.Scale.Z);
            var hash = System.Security.Cryptography.SHA256.Create();
            var actualHash = BitConverter.ToString(hash.ComputeHash(System.IO.File.ReadAllBytes(typeof(ScalarDecay).Assembly.Location))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(golden.Sha256, actualHash);
            Assert.AreEqual(40, UnsafeUtility.SizeOf<Pose>());
            Assert.AreEqual(28, UnsafeUtility.SizeOf<ScalarDecay>());
            Assert.AreEqual(88, UnsafeUtility.SizeOf<PoseHistory>());
        }

        [Test]
        public void ScheduledBurstJobExecutesThePortableDllNatively()
        {
            using var inputs = new NativeArray<ProbeInput>(new[] { GoldenInput() }, Allocator.TempJob);
            using var outputs = new NativeArray<ProbeOutput>(1, Allocator.TempJob);
            var job = new PortableJob { Inputs = inputs, Outputs = outputs };
            job.Schedule().Complete();
            Assert.AreEqual(1, outputs[0].NativeExecution, "Managed fallback is not a successful Burst proof");
            Compare(PortableProbe.Evaluate(inputs[0]), outputs[0]);
        }

        [Test]
        public unsafe void CompiledFunctionPointerMatchesManagedEdgesAndProvesNativeExecution()
        {
            var pointer = BurstCompiler.CompileFunctionPointer<PortableProbe.Kernel>(PortableProbe.Execute);
            for (var i = 0; i < 32; i++)
            {
                var input = GoldenInput();
                input.Elapsed = i / 31f;
                input.Previous.Position = new Float3(1.1f, 2.2f, 3.3f);
                input.Previous.Rotation = i % 2 == 0 ? new MotionQuaternion(1, 0, 0, 0) : -input.Current.Rotation;
                input.Duration = i == 0 ? 0f : 0.6f;
                ProbeOutput output;
                pointer.Invoke(&input, &output);
                Assert.AreEqual(1, output.NativeExecution);
                Compare(PortableProbe.Evaluate(input), output);
            }
        }
    }
}

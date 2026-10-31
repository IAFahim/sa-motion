using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using global::Unity.Burst;
using global::Unity.Collections;
using global::Unity.Collections.LowLevel.Unsafe;
using global::Unity.Mathematics;
using global::UnityEngine;
using Pose = SA.Motion.Numerics.Pose;
using MotionQuaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Unity.CostProbe
{
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    public static unsafe class MotionCostProbe
    {
        public struct Input
        {
            public Pose Destination;
            public PoseTransition Transition;
            public float Elapsed;
            public float3 Vector;
            public quaternion Rotation;
        }

        public struct Result
        {
            public float Checksum;
            public int Native;
        }

        [Serializable]
        public class Report
        {
            public string UnityVersion;
            public string Cpu;
            public string Scope;
            public int Cases;
            public int IterationsPerSample;
            public int Samples;
            public int NativeExecution;
            public int ManagedControl;
            public float KernelChecksum;
            public float ConversionChecksum;
            public double ManagedKernelNs;
            public double BurstKernelNs;
            public double BurstConversionLoopNs;
            public double BurstBaselineLoopNs;
            public int Float3Bytes;
            public int QuaternionBytes;
            public int PoseBytes;
            public int HistoryBytes;
            public int TransitionBytes;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void Loop(Input* inputs, int count, int iterations, Result* result);

        [BurstDiscard]
        static void MarkManaged(ref int native) => native = 0;

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        [AOT.MonoPInvokeCallback(typeof(Loop))]
        public static void Kernel(Input* inputs, int count, int iterations, Result* result)
        {
            var checksum = 0f;
            var native = 1;
            MarkManaged(ref native);
            for (var i = 0; i < iterations; i++)
            {
                var input = inputs[i % count];
                var transition = input.Transition;
                var pose = PoseTransition.Apply(ref transition, input.Destination, input.Elapsed);
                checksum += pose.Position.X * 0.01f + pose.Position.Y * 0.02f + pose.Rotation.W * 0.03f + pose.Scale.Z * 0.04f;
            }
            *result = new Result { Checksum = checksum, Native = native };
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        [AOT.MonoPInvokeCallback(typeof(Loop))]
        public static void Convert(Input* inputs, int count, int iterations, Result* result)
        {
            var checksum = 0f;
            var native = 1;
            MarkManaged(ref native);
            for (var i = 0; i < iterations; i++)
            {
                var input = inputs[i % count];
                var vector = Conversions.ToUnity(Conversions.ToMotion(input.Vector));
                var rotation = Conversions.ToUnity(Conversions.ToMotion(input.Rotation));
                checksum += vector.x * 0.01f + vector.y * 0.02f + vector.z * 0.03f + rotation.value.w * 0.04f;
            }
            *result = new Result { Checksum = checksum, Native = native };
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        [AOT.MonoPInvokeCallback(typeof(Loop))]
        public static void Baseline(Input* inputs, int count, int iterations, Result* result)
        {
            var checksum = 0f;
            var native = 1;
            MarkManaged(ref native);
            for (var i = 0; i < iterations; i++)
            {
                var input = inputs[i % count];
                checksum += input.Vector.x * 0.01f + input.Vector.y * 0.02f + input.Vector.z * 0.03f + input.Rotation.value.w * 0.04f;
            }
            *result = new Result { Checksum = checksum, Native = native };
        }

        static double Measure(Loop loop, Input* inputs, int count, int iterations, out Result last)
        {
            Result sample;
            loop(inputs, count, 10000, &sample);
            var times = new double[5];
            for (var i = 0; i < times.Length; i++)
            {
                var start = Stopwatch.GetTimestamp();
                loop(inputs, count, iterations, &sample);
                times[i] = (Stopwatch.GetTimestamp() - start) * 1e9 / Stopwatch.Frequency / iterations;
                if (!float.IsFinite(sample.Checksum))
                    throw new InvalidOperationException("Nonfinite cost-probe checksum");
            }
            Array.Sort(times);
            last = sample;
            return times[times.Length / 2];
        }

        public static string Run(string output)
        {
            const int count = 64;
            const int iterations = 200000;
            using var inputs = new NativeArray<Input>(count, Allocator.Temp);
            for (var i = 0; i < count; i++)
            {
                var pose = Pose.Identity;
                pose.Position = new Float3(i * 0.03f + 1f, i * -0.01f, 0.3f);
                pose.Rotation = MotionQuaternion.AxisAngle(new Float3(0, 1, 0), i * 0.01f + 0.1f);
                pose.Scale = new Float3(1f + i * 0.01f, 1.2f, 1.5f);
                var history = new PoseHistory();
                history.Reset(pose);
                var value = new Input { Destination = Pose.Identity, Transition = PoseTransition.Create(history, Pose.Identity, 1f), Elapsed = i / 128f, Vector = new float3(i * 0.03f, i * 0.01f, 0.5f), Rotation = quaternion.RotateY(i * 0.01f) };
                UnsafeUtility.WriteArrayElement(inputs.GetUnsafePtr(), i, value);
            }
            var data = (Input*)inputs.GetUnsafeReadOnlyPtr();
            var kernel = BurstCompiler.CompileFunctionPointer<Loop>(Kernel);
            var convert = BurstCompiler.CompileFunctionPointer<Loop>(Convert);
            var baseline = BurstCompiler.CompileFunctionPointer<Loop>(Baseline);
            var managedNs = Measure(Kernel, data, count, iterations, out var managed);
            var burstNs = Measure(kernel.Invoke, data, count, iterations, out var native);
            var conversionNs = Measure(convert.Invoke, data, count, iterations, out var converted);
            var baselineNs = Measure(baseline.Invoke, data, count, iterations, out var direct);
            if (managed.Native != 0 || native.Native != 1 || converted.Native != 1 || direct.Native != 1)
                throw new InvalidOperationException("Native cost proof fell back to managed execution");
            if (Math.Abs(managed.Checksum - native.Checksum) > Math.Abs(managed.Checksum) * 1e-5f || converted.Checksum != direct.Checksum)
                throw new InvalidOperationException("Cost-probe output checksums disagree");
            var report = new Report
            {
                UnityVersion = Application.unityVersion, Cpu = SystemInfo.processorType,
                Scope = "Warmed Editor CoreCLR/Burst single-thread 64-case batches; loop/checksum and one delegate/function-pointer boundary included; not isolated conversion instruction cost, GPU, crowd or full-frame timing",
                Cases = count, IterationsPerSample = iterations, Samples = 5, NativeExecution = native.Native, ManagedControl = managed.Native,
                KernelChecksum = native.Checksum, ConversionChecksum = converted.Checksum,
                ManagedKernelNs = managedNs, BurstKernelNs = burstNs, BurstConversionLoopNs = conversionNs, BurstBaselineLoopNs = baselineNs,
                Float3Bytes = UnsafeUtility.SizeOf<Float3>(), QuaternionBytes = UnsafeUtility.SizeOf<MotionQuaternion>(), PoseBytes = UnsafeUtility.SizeOf<Pose>(), HistoryBytes = UnsafeUtility.SizeOf<PoseHistory>(), TransitionBytes = UnsafeUtility.SizeOf<PoseTransition>()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var json = JsonUtility.ToJson(report, true);
            File.WriteAllText(output, json);
            return json;
        }
    }
}

using System;
using SA.Motion.Inertialization;
using SA.Motion.Numerics;
using Xunit;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Motion.Tests;

public class TotalityTests
{
    static readonly float[] Palette = BuildPalette();
    static float[] BuildPalette()
    {
        var random = new Random(12345);
        var palette = new float[24];
        palette[0] = 0f;
        palette[1] = -0f;
        palette[2] = float.NaN;
        palette[3] = float.PositiveInfinity;
        palette[4] = float.NegativeInfinity;
        palette[5] = float.Epsilon;
        palette[6] = float.MaxValue;
        palette[7] = -float.MaxValue;
        palette[8] = 1e-30f;
        palette[9] = -1e-30f;
        palette[10] = 1e30f;
        palette[11] = -1e30f;
        for (var k = 0; k < 12; k++)
        {
            palette[12 + k] = (float)(random.NextDouble() * 20.0 - 10.0);
        }
        return palette;
    }
    static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
    static bool Finite(Float3 value)
    {
        return Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    }
    static bool Finite(Quaternion value)
    {
        return Finite(value.X) && Finite(value.Y) && Finite(value.Z) && Finite(value.W);
    }
    static bool Finite(DecaySample value)
    {
        return Finite(value.Value) && Finite(value.Velocity) && Finite(value.Acceleration);
    }
    static bool Finite(ScalarDecay value)
    {
        return Finite(value.Duration) && Finite(value.C0) && Finite(value.C1) && Finite(value.C2) && Finite(value.C3) && Finite(value.C4) && Finite(value.C5);
    }
    static bool Finite(VectorOffset value)
    {
        return Finite(value.Direction) && Finite(value.Decay);
    }
    static bool Finite(RotationOffset value)
    {
        return Finite(value.Axis) && Finite(value.Decay);
    }
    static bool Finite(Pose value)
    {
        return Finite(value.Position) && Finite(value.Rotation) && Finite(value.Scale);
    }
    static bool Finite(PoseHistory value)
    {
        return Finite(value.Current) && Finite(value.Previous) && Finite(value.Interval);
    }
    static string Show(float value)
    {
        return value.ToString("R");
    }
    static string Show(Float3 value)
    {
        return "(" + Show(value.X) + "," + Show(value.Y) + "," + Show(value.Z) + ")";
    }
    static string Show(Quaternion value)
    {
        return "(" + Show(value.X) + "," + Show(value.Y) + "," + Show(value.Z) + "," + Show(value.W) + ")";
    }
    static string Show(ScalarDecay value)
    {
        return "(D=" + Show(value.Duration) + " C0=" + Show(value.C0) + " C1=" + Show(value.C1) + " C2=" + Show(value.C2) + " C3=" + Show(value.C3) + " C4=" + Show(value.C4) + " C5=" + Show(value.C5) + ")";
    }
    static string Show(DecaySample value)
    {
        return "(V=" + Show(value.Value) + " Vel=" + Show(value.Velocity) + " Acc=" + Show(value.Acceleration) + ")";
    }
    static string Show(Pose value)
    {
        return "(P=" + Show(value.Position) + " R=" + Show(value.Rotation) + " S=" + Show(value.Scale) + ")";
    }

    [Fact]
    public void ScalarDecayTotality()
    {
        const int count = 200000;
        string? failure = null;
        var span = Palette.Length;
        for (var i = 0; i < count; i++)
        {
            try
            {
                var magnitude = Palette[i % span];
                var velocity = Palette[(i * 7 + 3) % span];
                var seconds = Palette[(i * 13 + 5) % span];
                var elapsed = Palette[(i * 17 + 11) % span];
                var decay = ScalarDecay.Create(magnitude, velocity, seconds);
                if (Finite(magnitude) && Finite(velocity) && Finite(seconds) && !Finite(decay))
                {
                    failure = "ScalarDecay.Create finite inputs gave non-finite output inputs(m=" + Show(magnitude) + " v=" + Show(velocity) + " s=" + Show(seconds) + ") output=" + Show(decay);
                    break;
                }
                var sample = decay.Evaluate(elapsed);
                if (Finite(decay) && Finite(elapsed) && !Finite(sample))
                {
                    failure = "ScalarDecay.Evaluate finite inputs gave non-finite output decay=" + Show(decay) + " elapsed=" + Show(elapsed) + " output=" + Show(sample);
                    break;
                }
                var hostile = new ScalarDecay
                {
                    Duration = Palette[(i * 19 + 1) % span],
                    C0 = Palette[(i * 23 + 2) % span],
                    C1 = Palette[(i * 29 + 4) % span],
                    C2 = Palette[(i * 31 + 6) % span],
                    C3 = Palette[(i * 37 + 8) % span],
                    C4 = Palette[(i * 41 + 10) % span],
                    C5 = Palette[(i * 43 + 12) % span]
                };
                var hostileSample = hostile.Evaluate(elapsed);
                if (Finite(hostile) && Finite(elapsed) && !Finite(hostileSample))
                {
                    failure = "ScalarDecay.Evaluate hostile finite inputs gave non-finite output decay=" + Show(hostile) + " elapsed=" + Show(elapsed) + " output=" + Show(hostileSample);
                    break;
                }
            }
            catch (Exception ex)
            {
                failure = "ScalarDecay threw " + ex.GetType().FullName + " at i=" + i + " " + ex.Message;
                break;
            }
        }
        Assert.True(failure == null, failure);
    }

    [Fact]
    public void VectorOffsetTotality()
    {
        const int count = 200000;
        string? failure = null;
        var span = Palette.Length;
        for (var i = 0; i < count; i++)
        {
            try
            {
                var current = new Float3(Palette[(i * 1) % span], Palette[(i * 5 + 1) % span], Palette[(i * 7 + 2) % span]);
                var previous = new Float3(Palette[(i * 11 + 3) % span], Palette[(i * 13 + 4) % span], Palette[(i * 17 + 5) % span]);
                var destination = new Float3(Palette[(i * 19 + 6) % span], Palette[(i * 23 + 7) % span], Palette[(i * 29 + 8) % span]);
                var interval = Palette[(i * 31 + 9) % span];
                var seconds = Palette[(i * 37 + 10) % span];
                var elapsed = Palette[(i * 41 + 11) % span];
                var offset = VectorOffset.Create(current, previous, destination, interval, seconds);
                if (Finite(current) && Finite(previous) && Finite(destination) && Finite(interval) && Finite(seconds) && !Finite(offset))
                {
                    failure = "VectorOffset.Create finite inputs gave non-finite output current=" + Show(current) + " previous=" + Show(previous) + " destination=" + Show(destination) + " interval=" + Show(interval) + " seconds=" + Show(seconds) + " outputDir=" + Show(offset.Direction) + " outputDecay=" + Show(offset.Decay);
                    break;
                }
                var applied = offset.Apply(destination, elapsed);
                if (Finite(offset) && Finite(destination) && Finite(elapsed) && !Finite(applied))
                {
                    failure = "VectorOffset.Apply finite inputs gave non-finite output offsetDir=" + Show(offset.Direction) + " offsetDecay=" + Show(offset.Decay) + " destination=" + Show(destination) + " elapsed=" + Show(elapsed) + " output=" + Show(applied);
                    break;
                }
                if (Finite(destination) && Finite(elapsed) && !Finite(offset.Apply(destination, elapsed)))
                {
                    var check = offset.Apply(destination, elapsed);
                    if (Finite(destination) && !Finite(check))
                    {
                        failure = "VectorOffset.Apply finite destination gave non-finite output offsetDir=" + Show(offset.Direction) + " offsetDecay=" + Show(offset.Decay) + " destination=" + Show(destination) + " elapsed=" + Show(elapsed) + " output=" + Show(check);
                        break;
                    }
                }
                var hostile = new VectorOffset
                {
                    Direction = new Float3(Palette[(i * 43 + 1) % span], Palette[(i * 47 + 2) % span], Palette[(i * 53 + 3) % span]),
                    Decay = new ScalarDecay
                    {
                        Duration = Palette[(i * 59 + 4) % span],
                        C0 = Palette[(i * 61 + 5) % span],
                        C1 = Palette[(i * 67 + 6) % span],
                        C2 = Palette[(i * 71 + 7) % span],
                        C3 = Palette[(i * 73 + 8) % span],
                        C4 = Palette[(i * 79 + 9) % span],
                        C5 = Palette[(i * 83 + 10) % span]
                    }
                };
                var hostileApplied = hostile.Apply(destination, elapsed);
                if (Finite(hostile.Direction) && Finite(hostile.Decay) && Finite(destination) && Finite(elapsed) && !Finite(hostileApplied))
                {
                    failure = "VectorOffset.Apply hostile finite inputs gave non-finite output offsetDir=" + Show(hostile.Direction) + " offsetDecay=" + Show(hostile.Decay) + " destination=" + Show(destination) + " elapsed=" + Show(elapsed) + " output=" + Show(hostileApplied);
                    break;
                }
                if (Finite(destination) && !Finite(hostile.Apply(destination, elapsed)))
                {
                    var extra = hostile.Apply(destination, elapsed);
                    if (Finite(destination) && !Finite(extra))
                    {
                        failure = "VectorOffset.Apply finite destination with hostile offset gave non-finite output offsetDir=" + Show(hostile.Direction) + " offsetDecay=" + Show(hostile.Decay) + " destination=" + Show(destination) + " elapsed=" + Show(elapsed) + " output=" + Show(extra);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                failure = "VectorOffset threw " + ex.GetType().FullName + " at i=" + i + " " + ex.Message;
                break;
            }
        }
        Assert.True(failure == null, failure);
    }

    [Fact]
    public void RotationOffsetTotality()
    {
        const int count = 200000;
        string? failure = null;
        var span = Palette.Length;
        for (var i = 0; i < count; i++)
        {
            try
            {
                var current = new Quaternion(Palette[(i * 1) % span], Palette[(i * 5 + 1) % span], Palette[(i * 7 + 2) % span], Palette[(i * 11 + 3) % span]);
                var previous = new Quaternion(Palette[(i * 13 + 4) % span], Palette[(i * 17 + 5) % span], Palette[(i * 19 + 6) % span], Palette[(i * 23 + 7) % span]);
                var destination = new Quaternion(Palette[(i * 29 + 8) % span], Palette[(i * 31 + 9) % span], Palette[(i * 37 + 10) % span], Palette[(i * 41 + 11) % span]);
                var interval = Palette[(i * 43 + 1) % span];
                var seconds = Palette[(i * 47 + 2) % span];
                var elapsed = Palette[(i * 53 + 3) % span];
                var offset = RotationOffset.Create(current, previous, destination, interval, seconds);
                if (Finite(current) && Finite(previous) && Finite(destination) && Finite(interval) && Finite(seconds) && !Finite(offset))
                {
                    failure = "RotationOffset.Create finite inputs gave non-finite output current=" + Show(current) + " previous=" + Show(previous) + " destination=" + Show(destination) + " interval=" + Show(interval) + " seconds=" + Show(seconds) + " outputAxis=" + Show(offset.Axis) + " outputDecay=" + Show(offset.Decay);
                    break;
                }
                var applied = offset.Apply(destination, elapsed);
                if (Finite(offset) && Finite(destination) && Finite(elapsed) && !Finite(applied))
                {
                    failure = "RotationOffset.Apply finite inputs gave non-finite output offsetAxis=" + Show(offset.Axis) + " offsetDecay=" + Show(offset.Decay) + " destination=" + Show(destination) + " elapsed=" + Show(elapsed) + " output=" + Show(applied);
                    break;
                }
                if (Finite(destination) && !Finite(offset.Apply(destination, elapsed)))
                {
                    var check = offset.Apply(destination, elapsed);
                    if (Finite(destination) && !Finite(check))
                    {
                        failure = "RotationOffset.Apply finite destination gave non-finite output offsetAxis=" + Show(offset.Axis) + " offsetDecay=" + Show(offset.Decay) + " destination=" + Show(destination) + " elapsed=" + Show(elapsed) + " output=" + Show(check);
                        break;
                    }
                }
                var hostile = new RotationOffset
                {
                    Axis = new Float3(Palette[(i * 59 + 4) % span], Palette[(i * 61 + 5) % span], Palette[(i * 67 + 6) % span]),
                    Decay = new ScalarDecay
                    {
                        Duration = Palette[(i * 71 + 7) % span],
                        C0 = Palette[(i * 73 + 8) % span],
                        C1 = Palette[(i * 79 + 9) % span],
                        C2 = Palette[(i * 83 + 10) % span],
                        C3 = Palette[(i * 89 + 11) % span],
                        C4 = Palette[(i * 97 + 12) % span],
                        C5 = Palette[(i * 101 + 13) % span]
                    }
                };
                var hostileApplied = hostile.Apply(destination, elapsed);
                if (Finite(hostile) && Finite(destination) && Finite(elapsed) && !Finite(hostileApplied))
                {
                    failure = "RotationOffset.Apply hostile finite inputs gave non-finite output offsetAxis=" + Show(hostile.Axis) + " offsetDecay=" + Show(hostile.Decay) + " destination=" + Show(destination) + " elapsed=" + Show(elapsed) + " output=" + Show(hostileApplied);
                    break;
                }
                if (Finite(destination) && !Finite(hostile.Apply(destination, elapsed)))
                {
                    var extra = hostile.Apply(destination, elapsed);
                    if (Finite(destination) && !Finite(extra))
                    {
                        failure = "RotationOffset.Apply finite destination with hostile offset gave non-finite output offsetAxis=" + Show(hostile.Axis) + " offsetDecay=" + Show(hostile.Decay) + " destination=" + Show(destination) + " elapsed=" + Show(elapsed) + " output=" + Show(extra);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                failure = "RotationOffset threw " + ex.GetType().FullName + " at i=" + i + " " + ex.Message;
                break;
            }
        }
        Assert.True(failure == null, failure);
    }

    [Fact]
    public void PoseHistoryTotality()
    {
        const int count = 200000;
        string? failure = null;
        var span = Palette.Length;
        for (var i = 0; i < count; i++)
        {
            try
            {
                var pose = new Pose
                {
                    Position = new Float3(Palette[(i * 1) % span], Palette[(i * 5 + 1) % span], Palette[(i * 7 + 2) % span]),
                    Rotation = new Quaternion(Palette[(i * 11 + 3) % span], Palette[(i * 13 + 4) % span], Palette[(i * 17 + 5) % span], Palette[(i * 19 + 6) % span]),
                    Scale = new Float3(Palette[(i * 23 + 7) % span], Palette[(i * 29 + 8) % span], Palette[(i * 31 + 9) % span])
                };
                var delta = Palette[(i * 37 + 10) % span];
                var history = new PoseHistory();
                history.Reset(pose);
                if (Finite(pose) && !Finite(history))
                {
                    failure = "PoseHistory.Reset finite inputs gave non-finite output pose=" + Show(pose) + " interval=" + Show(history.Interval) + " count=" + history.Count;
                    break;
                }
                var second = new Pose
                {
                    Position = new Float3(Palette[(i * 41 + 11) % span], Palette[(i * 43 + 12) % span], Palette[(i * 47 + 13) % span]),
                    Rotation = new Quaternion(Palette[(i * 53 + 1) % span], Palette[(i * 59 + 2) % span], Palette[(i * 61 + 3) % span], Palette[(i * 67 + 4) % span]),
                    Scale = new Float3(Palette[(i * 71 + 5) % span], Palette[(i * 73 + 6) % span], Palette[(i * 79 + 7) % span])
                };
                history.Capture(second, delta);
                if (Finite(pose) && Finite(second) && Finite(delta) && !Finite(history))
                {
                    failure = "PoseHistory.Capture finite inputs gave non-finite output first=" + Show(pose) + " second=" + Show(second) + " delta=" + Show(delta) + " interval=" + Show(history.Interval) + " count=" + history.Count;
                    break;
                }
            }
            catch (Exception ex)
            {
                failure = "PoseHistory threw " + ex.GetType().FullName + " at i=" + i + " " + ex.Message;
                break;
            }
        }
        Assert.True(failure == null, failure);
    }

    [Fact]
    public void PoseTransitionTotality()
    {
        const int count = 200000;
        string? failure = null;
        var span = Palette.Length;
        for (var i = 0; i < count; i++)
        {
            try
            {
                var currentPose = new Pose
                {
                    Position = new Float3(Palette[(i * 1) % span], Palette[(i * 5 + 1) % span], Palette[(i * 7 + 2) % span]),
                    Rotation = new Quaternion(Palette[(i * 11 + 3) % span], Palette[(i * 13 + 4) % span], Palette[(i * 17 + 5) % span], Palette[(i * 19 + 6) % span]),
                    Scale = new Float3(Palette[(i * 23 + 7) % span], Palette[(i * 29 + 8) % span], Palette[(i * 31 + 9) % span])
                };
                var previousPose = new Pose
                {
                    Position = new Float3(Palette[(i * 37 + 10) % span], Palette[(i * 41 + 11) % span], Palette[(i * 43 + 12) % span]),
                    Rotation = new Quaternion(Palette[(i * 47 + 13) % span], Palette[(i * 53 + 1) % span], Palette[(i * 59 + 2) % span], Palette[(i * 61 + 3) % span]),
                    Scale = new Float3(Palette[(i * 67 + 4) % span], Palette[(i * 71 + 5) % span], Palette[(i * 73 + 6) % span])
                };
                var destination = new Pose
                {
                    Position = new Float3(Palette[(i * 79 + 7) % span], Palette[(i * 83 + 8) % span], Palette[(i * 89 + 9) % span]),
                    Rotation = new Quaternion(Palette[(i * 97 + 10) % span], Palette[(i * 101 + 11) % span], Palette[(i * 103 + 12) % span], Palette[(i * 107 + 13) % span]),
                    Scale = new Float3(Palette[(i * 109 + 1) % span], Palette[(i * 113 + 2) % span], Palette[(i * 127 + 3) % span])
                };
                var interval = Palette[(i * 131 + 4) % span];
                var seconds = Palette[(i * 137 + 5) % span];
                var elapsed = Palette[(i * 139 + 6) % span];
                var history = new PoseHistory
                {
                    Current = currentPose,
                    Previous = previousPose,
                    Interval = interval,
                    Count = (uint)(i % 3)
                };
                var transition = PoseTransition.Create(history, destination, seconds);
                var transitionFinite = Finite(transition.Position) && Finite(transition.Rotation) && Finite(transition.LogScale);
                if (Finite(history) && Finite(destination) && Finite(seconds) && !transitionFinite)
                {
                    failure = "PoseTransition.Create finite inputs gave non-finite output historyCurrent=" + Show(currentPose) + " historyPrevious=" + Show(previousPose) + " interval=" + Show(interval) + " count=" + history.Count + " destination=" + Show(destination) + " seconds=" + Show(seconds);
                    break;
                }
                var applied = PoseTransition.Apply(ref transition, destination, elapsed);
                if (Finite(transition.Position) && Finite(transition.Rotation) && Finite(transition.LogScale) && Finite(destination) && Finite(elapsed) && !Finite(applied))
                {
                    failure = "PoseTransition.Apply finite inputs gave non-finite output destination=" + Show(destination) + " elapsed=" + Show(elapsed) + " output=" + Show(applied);
                    break;
                }
                if (Finite(destination) && destination.Scale.IsPositive && !Finite(PoseTransition.Apply(ref transition, destination, elapsed)))
                {
                    var extra = PoseTransition.Apply(ref transition, destination, elapsed);
                    if (Finite(destination) && destination.Scale.IsPositive && !Finite(extra))
                    {
                        failure = "PoseTransition.Apply finite positive-scale destination gave non-finite output destination=" + Show(destination) + " elapsed=" + Show(elapsed) + " output=" + Show(extra);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                failure = "PoseTransition threw " + ex.GetType().FullName + " at i=" + i + " " + ex.Message;
                break;
            }
        }
        Assert.True(failure == null, failure);
    }

    static bool BoundedBy1e18(Quaternion q) => Math.Abs(q.X) <= 1e18f && Math.Abs(q.Y) <= 1e18f && Math.Abs(q.Z) <= 1e18f && Math.Abs(q.W) <= 1e18f;

    [Fact]
    public void QuaternionTotality()
    {
        const int count = 200000;
        string? failure = null;
        var span = Palette.Length;
        for (var i = 0; i < count; i++)
        {
            try
            {
                var a = new Quaternion(Palette[(i * 1) % span], Palette[(i * 5 + 1) % span], Palette[(i * 7 + 2) % span], Palette[(i * 11 + 3) % span]);
                var b = new Quaternion(Palette[(i * 13 + 4) % span], Palette[(i * 17 + 5) % span], Palette[(i * 19 + 6) % span], Palette[(i * 23 + 7) % span]);
                var axis = new Float3(Palette[(i * 29 + 8) % span], Palette[(i * 31 + 9) % span], Palette[(i * 37 + 10) % span]);
                var angle = Palette[(i * 41 + 11) % span];
                var normalized = Quaternion.Normalize(a);
                if (Finite(a) && !Finite(normalized))
                {
                    failure = "Quaternion.Normalize finite inputs gave non-finite output input=" + Show(a) + " output=" + Show(normalized);
                    break;
                }
                var canonical = Quaternion.Canonical(a);
                if (Finite(a) && !Finite(canonical))
                {
                    failure = "Quaternion.Canonical finite inputs gave non-finite output input=" + Show(a) + " output=" + Show(canonical);
                    break;
                }
                var axisAngle = Quaternion.AxisAngle(axis, angle);
                if (Finite(axis) && Finite(angle) && !Finite(axisAngle))
                {
                    failure = "Quaternion.AxisAngle finite inputs gave non-finite output axis=" + Show(axis) + " angle=" + Show(angle) + " output=" + Show(axisAngle);
                    break;
                }
                var multiplied = Quaternion.Multiply(a, b);
                if (BoundedBy1e18(a) && BoundedBy1e18(b) && !Finite(multiplied))
                {
                    failure = "Quaternion.Multiply bounded inputs gave non-finite output a=" + Show(a) + " b=" + Show(b) + " output=" + Show(multiplied);
                    break;
                }
                var inverted = Quaternion.InverseUnit(a);
                if (Finite(a) && !Finite(inverted))
                {
                    failure = "Quaternion.InverseUnit finite inputs gave non-finite output input=" + Show(a) + " output=" + Show(inverted);
                    break;
                }
            }
            catch (Exception ex)
            {
                failure = "Quaternion threw " + ex.GetType().FullName + " at i=" + i + " " + ex.Message;
                break;
            }
        }
        Assert.True(failure == null, failure);
    }
}

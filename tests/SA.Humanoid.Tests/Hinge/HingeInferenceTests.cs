using System;
using SA.Humanoid.Hinge;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Humanoid.Tests.Hinge;

public class HingeInferenceTests
{
    const float AxisTolerance = 0.02f;
    const float RangeTolerance = 0.02f;

    static readonly float AxisAlignmentFloor = (float)Math.Cos(AxisTolerance);

    static Float3 Norm(Float3 v) => v / v.Length;

    static readonly Quaternion Rest = Quaternion.Normalize(
        Quaternion.AxisAngle(Norm(new Float3(0.3f, 0.5f, -0.2f)), 0.7f));

    static Quaternion[] HingeSamples(Float3 axis, float lo, float hi, int count)
    {
        var samples = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            float t = lo + (hi - lo) * i / (count - 1);
            samples[i] = Quaternion.Multiply(Quaternion.AxisAngle(axis, t), Rest);
        }
        return samples;
    }

    static void CheckHinge(Float3 axis, float lo, float hi)
    {
        var estimate = HingeInference.Estimate(Rest, HingeSamples(axis, lo, hi, 9));
        Assert.True(estimate.Valid);
        Assert.InRange(estimate.Fit, 0.8f, 1f);
        float alignment = Scalar.Abs(Float3.Dot(estimate.Axis, axis));
        Assert.True(alignment > AxisAlignmentFloor);
        float expectedMin = lo;
        float expectedMax = hi;
        if (Float3.Dot(estimate.Axis, axis) < 0f)
        {
            expectedMin = -hi;
            expectedMax = -lo;
        }
        Assert.InRange(estimate.Min, expectedMin - RangeTolerance, expectedMin + RangeTolerance);
        Assert.InRange(estimate.Max, expectedMax - RangeTolerance, expectedMax + RangeTolerance);
        Assert.True(estimate.Min <= estimate.Max);
        Assert.True(estimate.Axis.IsFinite);
    }

    [Fact]
    public void HingeAboutXRecoversAxisAndRange()
    {
        CheckHinge(new Float3(1f, 0f, 0f), -1.2f, 0.3f);
    }

    [Fact]
    public void HingeAboutYRecoversAxisAndRange()
    {
        CheckHinge(new Float3(0f, 1f, 0f), -0.4f, 1.1f);
    }

    [Fact]
    public void HingeAboutZRecoversAxisAndRange()
    {
        CheckHinge(new Float3(0f, 0f, 1f), 0.1f, 1.4f);
    }

    [Fact]
    public void HingeAboutObliqueAxisRecoversAxisAndRange()
    {
        CheckHinge(Norm(new Float3(1f, 2f, 3f)), -0.8f, 1.1f);
    }

    [Fact]
    public void BallMotionGivesLowFitAndInvalid()
    {
        var axisX = new Float3(1f, 0f, 0f);
        var axisY = new Float3(0f, 1f, 0f);
        var samples = new Quaternion[25];
        int k = 0;
        for (int i = 0; i < 5; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                var swing = Quaternion.Multiply(
                    Quaternion.AxisAngle(axisX, -1f + 0.5f * i),
                    Quaternion.AxisAngle(axisY, -1f + 0.5f * j));
                samples[k++] = Quaternion.Multiply(swing, Rest);
            }
        }
        var estimate = HingeInference.Estimate(Rest, samples);
        Assert.False(estimate.Valid);
        Assert.InRange(estimate.Fit, 0f, 0.8f);
        Assert.True(estimate.Axis.IsFinite);
        Assert.True(Scalar.IsFinite(estimate.Min));
        Assert.True(Scalar.IsFinite(estimate.Max));
        Assert.True(Scalar.IsFinite(estimate.Fit));
    }

    [Fact]
    public void SignFlippedSamplesGiveSameAnswer()
    {
        var axis = Norm(new Float3(1f, 2f, 3f));
        var samples = HingeSamples(axis, -0.8f, 1.1f, 9);
        var flipped = new Quaternion[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            flipped[i] = i % 2 == 0 ? -samples[i] : samples[i];
        var plain = HingeInference.Estimate(Rest, samples);
        var mixed = HingeInference.Estimate(Rest, flipped);
        Assert.Equal(plain.Valid, mixed.Valid);
        Assert.Equal(plain.Axis.X, mixed.Axis.X);
        Assert.Equal(plain.Axis.Y, mixed.Axis.Y);
        Assert.Equal(plain.Axis.Z, mixed.Axis.Z);
        Assert.Equal(plain.Min, mixed.Min);
        Assert.Equal(plain.Max, mixed.Max);
        Assert.Equal(plain.Fit, mixed.Fit);
    }

    [Fact]
    public void EstimateIsDeterministic()
    {
        var axis = Norm(new Float3(1f, 2f, 3f));
        var samples = HingeSamples(axis, -0.8f, 1.1f, 9);
        var first = HingeInference.Estimate(Rest, samples);
        var second = HingeInference.Estimate(Rest, samples);
        Assert.Equal(first.Valid, second.Valid);
        Assert.Equal(first.Axis.X, second.Axis.X);
        Assert.Equal(first.Axis.Y, second.Axis.Y);
        Assert.Equal(first.Axis.Z, second.Axis.Z);
        Assert.Equal(first.Min, second.Min);
        Assert.Equal(first.Max, second.Max);
        Assert.Equal(first.Fit, second.Fit);
    }

    [Fact]
    public void ConstantSamplesAreInvalidWithFiniteFields()
    {
        var samples = new Quaternion[5];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = Rest;
        var estimate = HingeInference.Estimate(Rest, samples);
        Assert.False(estimate.Valid);
        AssertFinite(estimate);
    }

    [Fact]
    public void SingleSampleIsInvalidWithFiniteFields()
    {
        var estimate = HingeInference.Estimate(Rest, HingeSamples(new Float3(0f, 0f, 1f), 0f, 1f, 1));
        Assert.False(estimate.Valid);
        AssertFinite(estimate);
    }

    [Fact]
    public void EmptySamplesAreInvalidWithFiniteFields()
    {
        var estimate = HingeInference.Estimate(Rest, Array.Empty<Quaternion>());
        Assert.False(estimate.Valid);
        AssertFinite(estimate);
    }

    [Fact]
    public void TinyMotionIsInvalidWithFiniteFields()
    {
        var estimate = HingeInference.Estimate(Rest, HingeSamples(new Float3(1f, 0f, 0f), 0f, 0.049f, 5));
        Assert.False(estimate.Valid);
        AssertFinite(estimate);
    }

    [Fact]
    public void MotionJustOverThresholdIsValid()
    {
        var estimate = HingeInference.Estimate(Rest, HingeSamples(new Float3(1f, 0f, 0f), 0f, 0.051f, 5));
        Assert.True(estimate.Valid);
        AssertFinite(estimate);
    }

    [Fact]
    public void NanSampleIsInvalidWithFiniteFields()
    {
        var samples = HingeSamples(new Float3(0f, 1f, 0f), -0.5f, 0.5f, 5);
        samples[2] = new Quaternion(float.NaN, 0f, 0f, 1f);
        var estimate = HingeInference.Estimate(Rest, samples);
        Assert.False(estimate.Valid);
        AssertFinite(estimate);
    }

    [Fact]
    public void InfiniteSampleIsInvalidWithFiniteFields()
    {
        var samples = HingeSamples(new Float3(0f, 1f, 0f), -0.5f, 0.5f, 5);
        samples[3] = new Quaternion(0f, 0f, float.PositiveInfinity, 1f);
        var estimate = HingeInference.Estimate(Rest, samples);
        Assert.False(estimate.Valid);
        AssertFinite(estimate);
    }

    [Fact]
    public void ZeroSampleIsInvalidWithFiniteFields()
    {
        var samples = HingeSamples(new Float3(0f, 1f, 0f), -0.5f, 0.5f, 5);
        samples[1] = default;
        var estimate = HingeInference.Estimate(Rest, samples);
        Assert.False(estimate.Valid);
        AssertFinite(estimate);
    }

    [Fact]
    public void BadRestIsInvalidWithFiniteFields()
    {
        var samples = HingeSamples(new Float3(1f, 0f, 0f), -0.5f, 0.5f, 5);
        var badRests = new Quaternion[]
        {
            default,
            new Quaternion(float.NaN, 0f, 0f, 1f),
            new Quaternion(0f, 0f, 0f, float.PositiveInfinity),
        };
        foreach (var rest in badRests)
        {
            var estimate = HingeInference.Estimate(rest, samples);
            Assert.False(estimate.Valid);
            AssertFinite(estimate);
        }
    }

    static void AssertFinite(HingeEstimate estimate)
    {
        Assert.True(estimate.Axis.IsFinite);
        Assert.True(Scalar.IsFinite(estimate.Min));
        Assert.True(Scalar.IsFinite(estimate.Max));
        Assert.True(Scalar.IsFinite(estimate.Fit));
        Assert.InRange(estimate.Fit, 0f, 1f);
        Assert.True(estimate.Min <= estimate.Max);
    }
}

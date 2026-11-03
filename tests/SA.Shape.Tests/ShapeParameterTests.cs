using SA.Animation;
using Xunit;

namespace SA.Shape.Tests;

public class ShapeParameterTests
{
    [Fact]
    public void StepConvergesExactlyToTheBoundedGoalAndCanReverseSmoothly()
    {
        ShapeChannel[] channels = [new(1, -1, 1)];
        ShapeParameter[] current = [new(0)];
        float[] request = [100];
        for (var tick = 0; tick < 15; tick++) Assert.Equal(AnimationError.None, ShapeParameters.Step(channels, request, current, .01f, current));
        var before = current[0];
        Assert.InRange(before.Value, 0.1f, .9f);
        Assert.True(before.Velocity > 0);
        request[0] = -100;
        Assert.Equal(AnimationError.None, ShapeParameters.Step(channels, request, current, .00001f, current));
        Assert.InRange(MathF.Abs(current[0].Value - before.Value), 0, .01f);
        Assert.InRange(MathF.Abs(current[0].Velocity - before.Velocity), 0, .01f);
        for (var tick = 0; tick < 2000; tick++)
        {
            Assert.Equal(AnimationError.None, ShapeParameters.Step(channels, request, current, .01f, current));
            Assert.InRange(current[0].Value, -1, 1);
        }
        Assert.Equal(new ShapeParameter(-1), current[0]);
        Assert.Equal(AnimationError.None, ShapeParameters.Reset(channels, current));
        Assert.Equal(new ShapeParameter(0), current[0]);
    }

    [Fact]
    public void PauseDoesNotAdvanceParametersAndZeroHalfLifeIsImmediate()
    {
        ShapeChannel[] channels = [new(1, 0, 1, .3f, 0)];
        ShapeParameter[] current = [new(.4f, .2f)];
        ShapeParameter[] output = [default];
        Assert.Equal(AnimationError.None, ShapeParameters.Step(channels, [1], current, 0, output));
        Assert.Equal(current[0], output[0]);
        Assert.Equal(AnimationError.None, ShapeParameters.Step(channels, [1], current, .01f, output));
        Assert.Equal(new ShapeParameter(1), output[0]);
    }

    [Fact]
    public void InvalidInputAndShiftedAliasingDoNotChangeParameterState()
    {
        ShapeChannel[] channels = [new(1, -1, 1), new(2, -1, 1)];
        ShapeParameter[] current = [new(.1f), new(.2f), new(.3f)];
        var saved = (ShapeParameter[])current.Clone();
        Assert.Equal(AnimationError.AliasedBuffers, ShapeParameters.Step(channels, [1, 1], current.AsSpan(0, 2), .01f, current.AsSpan(1)));
        Assert.Equal(saved, current);
        Assert.Equal(AnimationError.InvalidFrame, ShapeParameters.Step(channels, [1, float.NaN], current.AsSpan(0, 2), .01f, current));
        Assert.Equal(saved, current);
        Assert.Equal(AnimationError.InvalidFrame, ShapeParameters.Validate([channels[0], channels[0]]));
        Assert.Equal(AnimationError.InvalidFrame, ShapeParameters.Validate([new ShapeChannel(1, 1, -1)]));
        Assert.Equal(AnimationError.CapacityExceeded, ShapeParameters.Validate(new ShapeChannel[129]));
    }

    [Fact]
    public void MorphTargetsRebuildFromAuthoredBaseAndClampToRendererRanges()
    {
        ShapeChannel[] channels = [new(1, -1, 1), new(2, -1, 1)];
        ShapeMorph[] morphs = [new(0, 1, .2f), new(-1, 1, -.1f)];
        MorphEffect[] effects = [new(0, 0, .7f), new(1, 0, .5f), new(1, 1, -.4f)];
        float[] output = new float[2];
        for (var pass = 0; pass < 100; pass++) Assert.Equal(AnimationError.None, ShapeMorphs.Evaluate(channels, [1, 1], morphs, effects, output));
        Assert.Equal(1f, output[0]);
        Assert.Equal(-.5f, output[1]);
        Assert.Equal(AnimationError.None, ShapeMorphs.Evaluate(channels, [0, 0], morphs, effects, output));
        Assert.Equal(.2f, output[0]);
        Assert.Equal(-.1f, output[1]);
        Assert.Equal(AnimationError.InvalidTable, ShapeMorphs.Evaluate(channels, [0, 0], morphs, [new MorphEffect(0, 2, 1)], output));
        Assert.Equal(AnimationError.AliasedBuffers, ShapeMorphs.Evaluate(channels, output, morphs, effects, output));
    }
}

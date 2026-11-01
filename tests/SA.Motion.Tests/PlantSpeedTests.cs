using SA.Motion.Legs;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Motion.Tests;

public class PlantSpeedTests
{
    const float Frame = 1f / 60f;
    const float Sole = 0.09f;
    static readonly Float3 Up = new(0f, 1f, 0f);
    static readonly Float3 Forward = new(0f, 0f, 1f);
    static readonly FootShape Shape = new() { Heel = new Float3(0f, -Sole, -0.05f), Ball = new Float3(0f, -Sole, 0.14f) };

    sealed class Walker
    {
        public LegParams Params = LegParams.Gentle;
        LegRigState rig;
        readonly LegState[] states = new LegState[1];
        readonly LegPose[] poses = new LegPose[1];
        readonly FootShape[] shapes = { Shape };

        public bool Planted => states[0].Planted != 0;
        public bool Changed => states[0].Changed != 0;

        public void Step(float z, float height, Float3 groundVelocity = default)
        {
            var ankle = new Float3(0f, Sole + height, z);
            var input = new LegInput
            {
                Hip = ankle + new Float3(0f, 0.8f, 0f),
                Knee = ankle + new Float3(0f, 0.4f, 0.15f),
                Ankle = ankle,
                Upper = Quaternion.Identity,
                Lower = Quaternion.Identity,
                Foot = Quaternion.Identity,
                Ground = new GroundHit { Hit = 1, Point = new Float3(0f, 0f, z), Normal = Up },
                GroundVelocity = groundVelocity
            };
            LegRig.Step(Params, 1f, 1f, ref rig, states, new[] { input }, shapes, Float3.Zero, Up, Forward, Frame, poses);
        }
    }

    static int FirstPlant(Walker walker, float speed, int frames)
    {
        var z = 0f;
        for (var i = 0; i < 6; i++)
        {
            walker.Step(z, 0.4f);
            z += speed * Frame;
        }
        for (var i = 0; i < frames; i++)
        {
            walker.Step(z, 0f);
            if (walker.Planted)
                return i;
            z += speed * Frame;
        }
        return -1;
    }

    [Fact]
    public void AFootStillSlidingOverTheGroundDoesNotPlant()
    {
        Assert.Equal(-1, FirstPlant(new Walker(), 5f, 30));
    }

    [Fact]
    public void AFootBelowThePlantSpeedPlantsAtOnce()
    {
        Assert.Equal(0, FirstPlant(new Walker(), 0.5f, 30));
    }

    [Fact]
    public void ZeroPlantSpeedTurnsTheCheckOff()
    {
        var walker = new Walker();
        walker.Params.PlantSpeed = 0f;
        Assert.Equal(0, FirstPlant(walker, 5f, 30));
    }

    [Fact]
    public void AFootThatSlowsDownPlantsAsSoonAsItIsSlowEnough()
    {
        var walker = new Walker();
        var z = 0f;
        for (var i = 0; i < 6; i++)
        {
            walker.Step(z, 0.4f);
            z += 5f * Frame;
        }
        var speeds = new[] { 5f, 5f, 3f, 1.6f, 1.4f, 0.2f };
        var firstPlant = -1;
        for (var i = 0; i < speeds.Length; i++)
        {
            z += speeds[i] * Frame;
            walker.Step(z, 0f);
            if (walker.Planted && firstPlant < 0)
                firstPlant = i;
        }
        Assert.Equal(4, firstPlant);
    }

    [Fact]
    public void ASteadyFootOnAMovingGroundPlants()
    {
        var walker = new Walker();
        var z = 0f;
        var velocity = new Float3(0f, 0f, 5f);
        for (var i = 0; i < 6; i++)
        {
            walker.Step(z, 0.4f, velocity);
            z += 5f * Frame;
        }
        walker.Step(z, 0f, velocity);
        Assert.True(walker.Planted);
    }

    [Fact]
    public void AFootRunningOverMovingGroundIsNotMistakenForOneStandingOnIt()
    {
        var walker = new Walker();
        var z = 0f;
        var velocity = new Float3(0f, 0f, -5f);
        for (var i = 0; i < 6; i++)
        {
            walker.Step(z, 0.4f, velocity);
            z += 5f * Frame;
        }
        walker.Step(z, 0f, velocity);
        Assert.False(walker.Planted);
    }

    [Fact]
    public void ARunningTouchdownPlantsOncePerStrideInsteadOfTearingLoose()
    {
        var walker = new Walker();
        var z = 0f;
        for (var i = 0; i < 6; i++)
        {
            walker.Step(z, 0.4f);
            z += 8f * Frame;
        }
        var speeds = new[] { 8f, 8f, 8f, 3.3f, 0.8f, 0.6f, 0.5f, 0.3f, 0.3f, 0.3f };
        var plants = 0;
        foreach (var speed in speeds)
        {
            z += speed * Frame;
            walker.Step(z, 0f);
            if (walker.Changed && walker.Planted)
                plants++;
        }
        Assert.Equal(1, plants);
        Assert.True(walker.Planted);
    }

    [Fact]
    public void WithTheCheckOffThatSameTouchdownTearsLooseAndPlantsAgain()
    {
        var walker = new Walker();
        walker.Params.PlantSpeed = 0f;
        var z = 0f;
        for (var i = 0; i < 6; i++)
        {
            walker.Step(z, 0.4f);
            z += 8f * Frame;
        }
        var speeds = new[] { 8f, 8f, 8f, 3.3f, 0.8f, 0.6f, 0.5f, 0.3f, 0.3f, 0.3f };
        var plants = 0;
        foreach (var speed in speeds)
        {
            z += speed * Frame;
            walker.Step(z, 0f);
            if (walker.Changed && walker.Planted)
                plants++;
        }
        Assert.True(plants > 1, "plants=" + plants);
    }

    [Fact]
    public void DecideRefusesACorruptSpeed()
    {
        var p = LegParams.Standard;
        Assert.False(Planting.Decide(false, true, 0f, 0f, float.NaN, 0f, 0, 0f, p));
        Assert.False(Planting.Decide(false, true, 0f, 0f, float.PositiveInfinity, 0f, 0, 0f, p));
        Assert.True(Planting.Decide(false, true, 0f, 0f, 0f, 0f, 0, 0f, p));
    }

    [Fact]
    public void ThePlantSpeedStaysAfterBeingSanitisedAndScalesWithTheBody()
    {
        var p = LegParams.Standard;
        Assert.Equal(1.5f, LegParamsSpec.Sanitize(p).PlantSpeed);
        Assert.Equal(3f, LegParamsSpec.Prepared(p, 2f).PlantSpeed);
        p.PlantSpeed = float.NaN;
        Assert.Equal(1.5f, LegParamsSpec.Sanitize(p).PlantSpeed);
        p.PlantSpeed = 99f;
        Assert.Equal(20f, LegParamsSpec.Sanitize(p).PlantSpeed);
    }
}

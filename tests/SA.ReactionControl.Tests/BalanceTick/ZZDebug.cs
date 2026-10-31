using SA.Harness;
using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl;
using SA.ReactionControl.Balance;
using SA.ReactionControl.BalanceTick;
using Xunit;
using Xunit.Abstractions;
using TickDecision = SA.ReactionControl.BalanceTick.BalanceDecision;
using TickInput = SA.ReactionControl.BalanceTick.BalanceInput;

namespace SA.ReactionControl.Tests.BalanceTick;

public class ZZDebug
{
    readonly ITestOutputHelper output;

    public ZZDebug(ITestOutputHelper output)
    {
        this.output = output;
    }

    static float KneeBend(HarnessWorld world, bool left)
    {
        var thigh = left ? world.Bodies[(int)Role.ThighLeft].Position : world.Bodies[(int)Role.ThighRight].Position;
        var shin = left ? world.Bodies[(int)Role.ShinLeft].Position : world.Bodies[(int)Role.ShinRight].Position;
        var foot = left ? world.Bodies[(int)Role.FootLeft].Position : world.Bodies[(int)Role.FootRight].Position;
        var upper = shin - thigh;
        var lower = foot - shin;
        var ul = upper.Length;
        var ll = lower.Length;
        if (ul < 1e-6f || ll < 1e-6f)
            return 0f;
        var cosine = Float3.Dot(upper / ul, lower / ll);
        cosine = System.Math.Max(-1f, System.Math.Min(1f, cosine));
        return (float)System.Math.Acos(cosine) * 57.29578f;
    }

    [Fact]
    public void DebugStandTrace()
    {
        var world = HarnessWorld.Create();
        var p = BalanceParams.Standard;
        var input = TickInput.Standard;
        var state = BalanceControllerState.Initial(p.MaxStepDelay);
        var drives = new JointDrive[Roles.Count];
        var probes = new SupportProbe[2];
        var report = default(BalanceReport);
        for (var tick = 0; tick < 200; tick++)
        {
            probes[0] = world.ProbeGround(Role.FootLeft);
            probes[1] = world.ProbeGround(Role.FootRight);
            report = BalanceController.Tick(p, ref state, world.Bodies, probes, input, drives);
            world.ApplyDrives(drives);
            world.Step(1f / 60f);
            if (tick % 5 == 0 || report.Decision == TickDecision.Falling)
            {
                var pelvisY = world.Bodies[(int)Role.Pelvis].Position.Y;
                var torso = world.Bodies[(int)Role.Torso].Rotation;
                var axis = Quaternion.Rotate(torso, new Float3(0f, 1f, 0f));
                var dot = Float3.Dot(axis, new Float3(0f, 1f, 0f));
                var tilt = (float)System.Math.Acos(System.Math.Max(-1.0, System.Math.Min(1.0, dot))) * 57.29578f;
                output.WriteLine($"t {tick} pelvisY {pelvisY:F4} tilt {tilt:F2} kneeL {KneeBend(world, true):F1} kneeR {KneeBend(world, false):F1} step {state.Feet.SteppingLeg} planted {state.Feet.LeftPlanted}{state.Feet.RightPlanted} comH {report.ComHeight:F3} sup {report.Supported} dec {report.Decision}");
            }
            if (report.Decision == TickDecision.Falling)
                break;
        }
    }
}

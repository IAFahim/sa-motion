using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.Balance.Evaluation;

public sealed class BaselineHold : IBalanceController
{
    static readonly float[] Stiffness = { 2000f, 200f, 80f, 30f, 30f, 15f, 15f, 10f, 10f, 1000f, 1000f, 900f, 900f, 700f, 700f };
    static readonly float[] Damping = { 90f, 20f, 8f, 3f, 3f, 1.5f, 1.5f, 1f, 1f, 60f, 60f, 50f, 50f, 40f, 40f };

    public ControllerReport Tick(ReadOnlySpan<BodyState> bodies, ReadOnlySpan<FootContact> feet, ReadOnlySpan<Quaternion> targetWorld, float dt, Span<BodyDrive> drives)
    {
        var count = Math.Min(Math.Min(drives.Length, targetWorld.Length), Roles.Count);
        for (var i = 0; i < count; i++)
            drives[i] = new BodyDrive { TargetWorld = targetWorld[i], Stiffness = Stiffness[i], Damping = Damping[i], MaxTorque = Stiffness[i] * 0.5f };
        return new ControllerReport { Decision = ControllerDecision.Standing };
    }

    public void Reset()
    {
    }
}

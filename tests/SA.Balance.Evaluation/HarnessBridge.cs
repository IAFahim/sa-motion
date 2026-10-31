using SA.Balance.Contract;
using SA.Harness;
using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl;

namespace SA.Balance.Evaluation;

public static class HarnessBridge
{
    public static ControllerStep Step(IBalanceController controller, Action<ControllerReport>? observe = null)
    {
        var bodies = new BodyState[Roles.Count];
        var feet = new FootContact[2];
        var drives = new BodyDrive[Roles.Count];
        Quaternion[]? target = null;
        return (HarnessWorld world, float dt, Span<JointDrive> output) =>
        {
            var source = world.Bodies;
            if (target is null)
            {
                target = new Quaternion[Roles.Count];
                for (var i = 0; i < Roles.Count; i++)
                    target[i] = source[i].Rotation;
            }
            for (var i = 0; i < Roles.Count; i++)
                bodies[i] = new BodyState
                {
                    Position = source[i].Position,
                    Rotation = source[i].Rotation,
                    LinearVelocity = source[i].LinearVelocity,
                    AngularVelocity = source[i].AngularVelocity,
                    InverseMass = source[i].InverseMass
                };
            feet[0] = Contact(world.ProbeGround(Role.FootLeft));
            feet[1] = Contact(world.ProbeGround(Role.FootRight));
            for (var i = 0; i < Roles.Count; i++)
                drives[i] = BodyDrive.Passive;
            var report = controller.Tick(bodies, feet, target, dt, drives);
            observe?.Invoke(report);
            var count = Math.Min(output.Length, Roles.Count);
            for (var i = 0; i < count; i++)
                output[i] = new JointDrive
                {
                    Target = drives[i].TargetWorld,
                    TargetAngularVelocity = Float3.Zero,
                    Stiffness = drives[i].Stiffness,
                    Damping = drives[i].Damping,
                    MaxTorque = drives[i].MaxTorque
                };
        };
    }

    static FootContact Contact(SupportProbe probe) => new FootContact { Hit = probe.Hit, Point = probe.Point, Normal = probe.Normal };
}

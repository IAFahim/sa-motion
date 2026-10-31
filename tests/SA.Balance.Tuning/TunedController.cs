using ContractBody = SA.Balance.Contract.BodyState;
using ContractController = SA.Balance.Contract.IBalanceController;
using ContractDecision = SA.Balance.Contract.ControllerDecision;
using ContractDrive = SA.Balance.Contract.BodyDrive;
using ContractFoot = SA.Balance.Contract.FootContact;
using ContractReport = SA.Balance.Contract.ControllerReport;
using SA.Motion.Numerics;

namespace SA.Balance.Tuning;

public sealed class TunedController : ContractController
{
    BalanceParams parameters;
    BalanceState state;
    readonly BodyState[] bodies = new BodyState[15];
    readonly FootContact[] feet = new FootContact[2];
    readonly BodyDrive[] drives = new BodyDrive[15];
    readonly Quaternion[] targets = new Quaternion[15];

    public TunedController() : this(TunedParams.Tuned)
    {
    }

    public TunedController(BalanceParams parameters)
    {
        this.parameters = parameters;
    }

    public ContractReport Tick(ReadOnlySpan<ContractBody> source, ReadOnlySpan<ContractFoot> contacts, ReadOnlySpan<Quaternion> targetWorld, float dt, Span<ContractDrive> output)
    {
        var count = Math.Min(15, Math.Min(source.Length, Math.Min(targetWorld.Length, output.Length)));
        for (var i = 0; i < count; i++)
        {
            bodies[i] = new BodyState { Position = source[i].Position, Rotation = source[i].Rotation, LinearVelocity = source[i].LinearVelocity, AngularVelocity = source[i].AngularVelocity, InverseMass = source[i].InverseMass };
            targets[i] = targetWorld[i];
            drives[i] = BodyDrive.Passive;
        }
        for (var i = 0; i < 2 && i < contacts.Length; i++)
            feet[i] = new FootContact { Hit = contacts[i].Hit != 0, Point = contacts[i].Point, Normal = contacts[i].Normal };
        var input = new BalanceInput(targets, dt);
        var report = StandingController.Tick(in parameters, ref state, bodies, feet, in input, drives);
        for (var i = 0; i < count; i++)
            output[i] = new ContractDrive { TargetWorld = drives[i].TargetWorld, Stiffness = drives[i].Stiffness, Damping = drives[i].Damping, MaxTorque = drives[i].MaxTorque };
        return new ContractReport { Decision = (ContractDecision)(byte)report.Decision, Com = report.Com, CapturePoint = report.CapturePoint, Margin = report.Margin };
    }

    public void Reset()
    {
        state = new BalanceState();
    }
}

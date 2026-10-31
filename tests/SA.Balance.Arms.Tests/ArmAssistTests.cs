using ContractBody = SA.Balance.Contract.BodyState;
using ContractController = SA.Balance.Contract.IBalanceController;
using ContractDecision = SA.Balance.Contract.ControllerDecision;
using ContractDrive = SA.Balance.Contract.BodyDrive;
using ContractFoot = SA.Balance.Contract.FootContact;
using ContractReport = SA.Balance.Contract.ControllerReport;
using SA.Balance.Arms;
using SA.Balance.Evaluation;
using SA.Humanoid;
using SA.Motion.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SA.Balance.Arms.Tests;

sealed class InnerBaseline : ContractController
{
    SA.Balance.BalanceParams parameters = SA.Balance.BalanceParams.Standard;
    SA.Balance.BalanceState state;
    readonly SA.Balance.BodyState[] bodies = new SA.Balance.BodyState[15];
    readonly SA.Balance.FootContact[] feet = new SA.Balance.FootContact[2];
    readonly SA.Balance.BodyDrive[] drives = new SA.Balance.BodyDrive[15];
    readonly Quaternion[] targets = new Quaternion[15];

    public ContractReport Tick(ReadOnlySpan<ContractBody> source, ReadOnlySpan<ContractFoot> contacts, ReadOnlySpan<Quaternion> targetWorld, float dt, Span<ContractDrive> output)
    {
        var count = Math.Min(15, Math.Min(source.Length, Math.Min(targetWorld.Length, output.Length)));
        for (var i = 0; i < count; i++)
        {
            bodies[i] = new SA.Balance.BodyState { Position = source[i].Position, Rotation = source[i].Rotation, LinearVelocity = source[i].LinearVelocity, AngularVelocity = source[i].AngularVelocity, InverseMass = source[i].InverseMass };
            targets[i] = targetWorld[i];
            drives[i] = SA.Balance.BodyDrive.Passive;
        }
        for (var i = 0; i < 2 && i < contacts.Length; i++)
            feet[i] = new SA.Balance.FootContact { Hit = contacts[i].Hit != 0, Point = contacts[i].Point, Normal = contacts[i].Normal };
        var input = new SA.Balance.BalanceInput(targets, dt);
        var report = SA.Balance.StandingController.Tick(in parameters, ref state, bodies, feet, in input, drives);
        for (var i = 0; i < count; i++)
            output[i] = new ContractDrive { TargetWorld = drives[i].TargetWorld, Stiffness = drives[i].Stiffness, Damping = drives[i].Damping, MaxTorque = drives[i].MaxTorque };
        return new ContractReport { Decision = (ContractDecision)(byte)report.Decision, Com = report.Com, CapturePoint = report.CapturePoint, Margin = report.Margin };
    }

    public void Reset()
    {
        state = new SA.Balance.BalanceState();
    }
}

public class ArmAssistScoreTests
{
    readonly ITestOutputHelper output;

    public ArmAssistScoreTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TheArmAssistControllerPublishesItsScorecard()
    {
        var card = Evaluator.Run("arm-assist", () => new ArmAssistController());
        output.WriteLine(Evaluator.Table(card));
        Evaluator.Write(card, Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/balance"));
        Assert.True(card.StandPassed, Evaluator.Table(card));
        Assert.True(card.Deterministic);
        Assert.True(card.Score > 80, Evaluator.Table(card));
    }
}

public class ArmAssistUnitTests
{
    static ContractBody MovingBody(float x, float z)
    {
        return new ContractBody
        {
            Position = new Float3(x, 1f, z),
            Rotation = Quaternion.Identity,
            LinearVelocity = new Float3(x, 0f, z),
            AngularVelocity = Float3.Zero,
            InverseMass = 1f,
        };
    }

    [Fact]
    public void ShortSpansGivePassiveDrivesAndAFiniteReport()
    {
        var controller = new ArmAssistController();
        var drives = new ContractDrive[15];
        var report = controller.Tick(
            Array.Empty<ContractBody>(),
            Array.Empty<ContractFoot>(),
            Array.Empty<Quaternion>(),
            0.01666667f,
            drives);
        Assert.Equal(ContractDecision.Standing, report.Decision);
        Assert.True(Scalar.IsFinite(report.Margin));
        Assert.All(drives, drive => Assert.Equal(0f, drive.Stiffness));
    }

    [Fact]
    public void NanBodiesGiveFiniteDrivesAndAFiniteReport()
    {
        var controller = new ArmAssistController();
        var bodies = new ContractBody[15];
        var targets = new Quaternion[15];
        var drives = new ContractDrive[15];
        for (var i = 0; i < 15; i++)
        {
            bodies[i] = new ContractBody
            {
                Position = new Float3(float.NaN, float.NaN, float.NaN),
                Rotation = new Quaternion(float.NaN, 0f, 0f, 0f),
                LinearVelocity = new Float3(float.NaN, 0f, 0f),
                AngularVelocity = Float3.Zero,
                InverseMass = float.NaN,
            };
            targets[i] = Quaternion.Identity;
        }
        var feet = new ContractFoot[2];
        var report = controller.Tick(bodies, feet, targets, 0.01666667f, drives);
        Assert.True(Scalar.IsFinite(report.Margin));
        foreach (var drive in drives)
        {
            Assert.True(drive.TargetWorld.IsValidRotation);
            Assert.True(Scalar.IsFinite(drive.Stiffness));
            Assert.True(Scalar.IsFinite(drive.Damping));
            Assert.True(Scalar.IsFinite(drive.MaxTorque));
        }
    }

    [Fact]
    public void SwingMovesArmTargetsTowardCenterVelocity()
    {
        var controller = new ArmAssistController();
        var bodies = new ContractBody[15];
        var targets = new Quaternion[15];
        var drives = new ContractDrive[15];
        for (var i = 0; i < 15; i++)
        {
            bodies[i] = MovingBody(0f, 2f);
            targets[i] = Quaternion.Identity;
        }
        var feet = new ContractFoot[2];
        feet[0] = new ContractFoot { Hit = 1, Point = new Float3(-0.1f, 0f, 0f), Normal = new Float3(0f, 1f, 0f) };
        feet[1] = new ContractFoot { Hit = 1, Point = new Float3(0.1f, 0f, 0f), Normal = new Float3(0f, 1f, 0f) };
        controller.Tick(bodies, feet, targets, 0.01666667f, drives);
        Float3 armUp = Quaternion.Rotate(drives[(int)Role.UpperArmLeft].TargetWorld, new Float3(0f, -1f, 0f));
        Assert.True(armUp.Z > 0f);
    }

    [Fact]
    public void LegsAndPelvisMatchTheInnerController()
    {
        var bodies = new ContractBody[15];
        var targets = new Quaternion[15];
        var assistedDrives = new ContractDrive[15];
        var innerDrives = new ContractDrive[15];
        for (var i = 0; i < 15; i++)
        {
            bodies[i] = MovingBody(0f, 2f);
            targets[i] = Quaternion.Identity;
        }
        var feet = new ContractFoot[2];
        feet[0] = new ContractFoot { Hit = 1, Point = new Float3(-0.1f, 0f, 0f), Normal = new Float3(0f, 1f, 0f) };
        feet[1] = new ContractFoot { Hit = 1, Point = new Float3(0.1f, 0f, 0f), Normal = new Float3(0f, 1f, 0f) };
        new ArmAssistController().Tick(bodies, feet, targets, 0.01666667f, assistedDrives);
        new InnerBaseline().Tick(bodies, feet, targets, 0.01666667f, innerDrives);
        var protectedRoles = new Role[] { Role.Pelvis, Role.ThighLeft, Role.ThighRight, Role.ShinLeft, Role.ShinRight, Role.FootLeft, Role.FootRight };
        foreach (var role in protectedRoles)
        {
            Assert.Equal(innerDrives[(int)role].TargetWorld, assistedDrives[(int)role].TargetWorld);
            Assert.Equal(innerDrives[(int)role].Stiffness, assistedDrives[(int)role].Stiffness);
        }
    }

    [Fact]
    public void FastScoreDoesNotRegressTheInnerBaseline()
    {
        var assisted = Evaluator.RunFast(() => new ArmAssistController());
        var inner = Evaluator.RunFast(() => new InnerBaseline());
        Assert.True(assisted >= inner - 5f, $"assisted {assisted} inner {inner}");
    }
}

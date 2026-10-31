using System;
using SA.Balance.Contract;
using SA.Balance.Evaluation;
using SA.Humanoid;
using SA.Motion.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace SA.Balance.Tests
{
    sealed class ReachabilityController : IBalanceController
    {
        BalanceParams parameters;
        BalanceState state;
        readonly BodyState[] bodies = new BodyState[15];
        readonly FootContact[] feet = new FootContact[2];
        readonly BodyDrive[] drives = new BodyDrive[15];
        readonly Quaternion[] targets = new Quaternion[15];

        public ReachabilityController() : this(BalanceParams.Standard)
        {
        }

        public ReachabilityController(BalanceParams parameters)
        {
            this.parameters = parameters;
        }

        public ControllerReport Tick(ReadOnlySpan<Contract.BodyState> source, ReadOnlySpan<Contract.FootContact> contacts, ReadOnlySpan<Quaternion> targetWorld, float dt, Span<Contract.BodyDrive> output)
        {
            int count = Math.Min(15, Math.Min(source.Length, Math.Min(targetWorld.Length, output.Length)));
            for (int i = 0; i < count; i++)
            {
                bodies[i] = new BodyState { Position = source[i].Position, Rotation = source[i].Rotation, LinearVelocity = source[i].LinearVelocity, AngularVelocity = source[i].AngularVelocity, InverseMass = source[i].InverseMass };
                targets[i] = targetWorld[i];
                drives[i] = BodyDrive.Passive;
            }
            for (int i = 0; i < 2 && i < contacts.Length; i++)
                feet[i] = new FootContact { Hit = contacts[i].Hit != 0, Point = contacts[i].Point, Normal = contacts[i].Normal };
            var input = new BalanceInput(targets, dt);
            BalanceReport report = StandingController.Tick(in parameters, ref state, bodies, feet, in input, drives);
            for (int i = 0; i < count; i++)
            {
                output[i] = new Contract.BodyDrive { TargetWorld = drives[i].TargetWorld, Stiffness = drives[i].Stiffness, Damping = drives[i].Damping, MaxTorque = drives[i].MaxTorque };
            }
            return new ControllerReport { Decision = (ControllerDecision)(byte)report.Decision, Com = report.Com, CapturePoint = report.CapturePoint, Margin = report.Margin };
        }

        public void Reset()
        {
            state = new BalanceState();
        }
    }

    public class ReachabilityScoreTests
    {
        readonly ITestOutputHelper output;

        public ReachabilityScoreTests(ITestOutputHelper output) => this.output = output;

        [Fact]
        public void ReachabilityControllerBeatsEighty()
        {
            Scorecard card = Evaluator.Run("c2-reachability", () => new ReachabilityController());
            output.WriteLine(Evaluator.Table(card));
            Evaluator.Write(card, Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/balance"));
            Assert.True(card.StandPassed, Evaluator.Table(card));
            Assert.True(card.Deterministic, Evaluator.Table(card));
            Assert.True(card.Score > 80, Evaluator.Table(card));
        }
    }
}

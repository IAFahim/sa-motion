using System;
using SA.Humanoid;
using SA.Harness;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Balance.Tests
{
    public class FallTests
    {
        static void FillBodies(BodyState[] bodies, Float3 position, Float3 velocity)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = new BodyState();
                body.Position = position;
                body.Rotation = Quaternion.Identity;
                body.LinearVelocity = velocity;
                body.AngularVelocity = Float3.Zero;
                body.InverseMass = 1f;
                bodies[i] = body;
            }
        }

        static void FillTargets(Quaternion[] targets)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = Quaternion.Identity;
            }
        }

        static void FillContacts(FootContact[] feet)
        {
            var left = new FootContact();
            left.Hit = true;
            left.Point = new Float3(-0.1f, 0f, 0f);
            left.Normal = new Float3(0f, 1f, 0f);
            var right = new FootContact();
            right.Hit = true;
            right.Point = new Float3(0.1f, 0f, 0f);
            right.Normal = new Float3(0f, 1f, 0f);
            feet[0] = left;
            feet[1] = right;
        }

        static BalanceReport DriveTick(BalanceParams p, ref BalanceState state, BodyState[] bodies, FootContact[] feet, Quaternion[] targets, BodyDrive[] drives)
        {
            var input = new BalanceInput(targets, 0.01666667f);
            return StandingController.Tick(in p, ref state, bodies, feet, in input, drives);
        }

        [Fact]
        public void UnreachableCaptureLatchesFallingWithoutSteps()
        {
            var bodies = new BodyState[15];
            FillBodies(bodies, new Float3(0f, 0.9f, 0f), new Float3(5f, 0f, 0f));
            var feet = new FootContact[2];
            FillContacts(feet);
            var targets = new Quaternion[15];
            FillTargets(targets);
            var drives = new BodyDrive[15];
            var state = new BalanceState();
            BalanceReport report = DriveTick(BalanceParams.Standard, ref state, bodies, feet, targets, drives);
            Assert.Equal(Decision.Falling, report.Decision);
            Assert.True(state.FallingLatched);
            Assert.Equal(-1, state.SwingFoot);
            BalanceReport again = DriveTick(BalanceParams.Standard, ref state, bodies, feet, targets, drives);
            Assert.Equal(Decision.Falling, again.Decision);
        }

        [Fact]
        public void CoverableCaptureStepsInsteadOfLatching()
        {
            var bodies = new BodyState[15];
            FillBodies(bodies, new Float3(0f, 0.9f, 0f), new Float3(2.6f, 0f, 0f));
            var feet = new FootContact[2];
            FillContacts(feet);
            var targets = new Quaternion[15];
            FillTargets(targets);
            var drives = new BodyDrive[15];
            var state = new BalanceState();
            BalanceReport report = DriveTick(BalanceParams.Standard, ref state, bodies, feet, targets, drives);
            Assert.Equal(Decision.Stepping, report.Decision);
            Assert.False(state.FallingLatched);
            Assert.True(state.SwingFoot == 0 || state.SwingFoot == 1);
        }

        [Fact]
        public void ExtremePushesAreDeterministic()
        {
            var first = new BalanceRunner(BalanceParams.Standard);
            var second = new BalanceRunner(BalanceParams.Standard);
            var push = new PushSpec(Role.Torso, new Float3(1f, 0f, 0f), 450f, 0.5f);
            ScenarioResult a = Scenarios.Run(first.Step, push, 5f);
            ScenarioResult b = Scenarios.Run(second.Step, push, 5f);
            Assert.Equal(a.Fell, b.Fell);
            Assert.Equal(a.FinalPelvisHeight, b.FinalPelvisHeight);
            Assert.Equal(first.LastReport.Decision, second.LastReport.Decision);
        }
    }
}

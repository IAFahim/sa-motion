using System;
using SA.Humanoid;
using SA.Harness;
using SA.Motion.Numerics;
using SA.ReactionControl;

namespace SA.Balance.Tests
{
    sealed class BalanceRunner
    {
        BalanceParams _params;
        BalanceState _state;
        Quaternion[]? _targets;
        readonly BodyState[] _bodies = new BodyState[15];
        readonly FootContact[] _feet = new FootContact[2];
        readonly BodyDrive[] _drives = new BodyDrive[15];

        public BalanceReport LastReport;
        public bool SawStepping;

        public BalanceRunner(BalanceParams p)
        {
            _params = p;
            _state = new BalanceState();
            LastReport = default;
            SawStepping = false;
        }

        public ControllerStep Step
        {
            get { return Run; }
        }

        void Run(HarnessWorld world, float dt, Span<JointDrive> drives)
        {
            if (_targets is null)
            {
                _targets = new Quaternion[15];
                for (int i = 0; i < 15; i++)
                {
                    _targets[i] = world.Bodies[i].Rotation;
                }
            }
            for (int i = 0; i < 15; i++)
            {
                var source = world.Bodies[i];
                var body = new BodyState();
                body.Position = source.Position;
                body.Rotation = source.Rotation;
                body.LinearVelocity = source.LinearVelocity;
                body.AngularVelocity = source.AngularVelocity;
                body.InverseMass = source.InverseMass;
                _bodies[i] = body;
            }
            var left = world.ProbeGround(Role.FootLeft);
            var right = world.ProbeGround(Role.FootRight);
            var contactLeft = new FootContact();
            contactLeft.Hit = left.Hit != 0;
            contactLeft.Point = left.Point;
            contactLeft.Normal = left.Normal;
            var contactRight = new FootContact();
            contactRight.Hit = right.Hit != 0;
            contactRight.Point = right.Point;
            contactRight.Normal = right.Normal;
            _feet[0] = contactLeft;
            _feet[1] = contactRight;
            var input = new BalanceInput(_targets, dt);
            LastReport = StandingController.Tick(in _params, ref _state, _bodies, _feet, in input, _drives);
            if (LastReport.Decision == Decision.Stepping)
            {
                SawStepping = true;
            }
            int count = drives.Length < 15 ? drives.Length : 15;
            for (int i = 0; i < count; i++)
            {
                var drive = _drives[i];
                drives[i] = new JointDrive
                {
                    Target = drive.TargetWorld,
                    TargetAngularVelocity = Float3.Zero,
                    Stiffness = drive.Stiffness,
                    Damping = drive.Damping,
                    MaxTorque = drive.MaxTorque
                };
            }
            for (int i = count; i < drives.Length; i++)
            {
                drives[i] = JointDrive.Passive;
            }
        }
    }
}

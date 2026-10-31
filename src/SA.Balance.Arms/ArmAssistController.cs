using System;
using SA.Humanoid;
using SA.Humanoid.Profile;
using SA.Motion.Numerics;
using BalanceBody = SA.Balance.BodyState;
using BalanceDrive = SA.Balance.BodyDrive;
using BalanceFoot = SA.Balance.FootContact;
using ContractBody = SA.Balance.Contract.BodyState;
using ContractController = SA.Balance.Contract.IBalanceController;
using ContractDecision = SA.Balance.Contract.ControllerDecision;
using ContractDrive = SA.Balance.Contract.BodyDrive;
using ContractFoot = SA.Balance.Contract.FootContact;
using ContractReport = SA.Balance.Contract.ControllerReport;

namespace SA.Balance.Arms
{
    public sealed class ArmAssistController : ContractController
    {
        BalanceParams parameters;
        BalanceState state;
        readonly BalanceBody[] bodies = new BalanceBody[Roles.Count];
        readonly BalanceFoot[] feet = new BalanceFoot[2];
        readonly BalanceDrive[] drives = new BalanceDrive[Roles.Count];
        readonly Quaternion[] targets = new Quaternion[Roles.Count];

        public ArmAssistController()
            : this(ArmAssistParams.Standard)
        {
        }

        public ArmAssistController(BalanceParams parameters)
        {
            this.parameters = parameters;
        }

        public ContractReport Tick(ReadOnlySpan<ContractBody> source, ReadOnlySpan<ContractFoot> contacts, ReadOnlySpan<Quaternion> targetWorld, float dt, Span<ContractDrive> output)
        {
            if (source.Length < Roles.Count || targetWorld.Length < Roles.Count || output.Length < Roles.Count)
            {
                for (int i = 0; i < output.Length; i++)
                {
                    output[i] = ContractDrive.Passive;
                }
                return new ContractReport { Decision = ContractDecision.Standing, Com = Float3.Zero, CapturePoint = Float3.Zero, Margin = 0f };
            }
            for (int i = 0; i < Roles.Count; i++)
            {
                bodies[i] = new BalanceBody
                {
                    Position = SanitizeVector(source[i].Position),
                    Rotation = SanitizeRotation(source[i].Rotation),
                    LinearVelocity = SanitizeVector(source[i].LinearVelocity),
                    AngularVelocity = SanitizeVector(source[i].AngularVelocity),
                    InverseMass = source[i].InverseMass,
                };
                targets[i] = SanitizeRotation(targetWorld[i]);
                drives[i] = BalanceDrive.Passive;
            }
            for (int i = 0; i < 2; i++)
            {
                if (i < contacts.Length)
                {
                    feet[i] = new BalanceFoot
                    {
                        Hit = contacts[i].Hit != 0,
                        Point = SanitizeVector(contacts[i].Point),
                        Normal = SanitizeVector(contacts[i].Normal),
                    };
                }
                else
                {
                    feet[i] = new BalanceFoot { Hit = false, Point = Float3.Zero, Normal = new Float3(0f, 1f, 0f) };
                }
            }
            var input = new BalanceInput(targets, dt);
            BalanceReport report = StandingController.Tick(in parameters, ref state, bodies, feet, in input, drives);
            if (report.Decision == Decision.Falling)
            {
                ApplyCushion();
            }
            else
            {
                ApplyFlywheel();
            }
            for (int i = 0; i < Roles.Count; i++)
            {
                output[i] = new ContractDrive
                {
                    TargetWorld = drives[i].TargetWorld,
                    Stiffness = drives[i].Stiffness,
                    Damping = drives[i].Damping,
                    MaxTorque = drives[i].MaxTorque,
                };
            }
            for (int i = Roles.Count; i < output.Length; i++)
            {
                output[i] = ContractDrive.Passive;
            }
            return new ContractReport { Decision = (ContractDecision)(byte)report.Decision, Com = report.Com, CapturePoint = report.CapturePoint, Margin = report.Margin };
        }

        public void Reset()
        {
            state = new BalanceState();
        }

        void ApplyFlywheel()
        {
            Float3 velocity = CenterVelocity();
            float pitch = Clamp(-velocity.Z * ArmAssistParams.ArmPitchGain, -ArmAssistParams.ArmMax, ArmAssistParams.ArmMax);
            float roll = Clamp(velocity.X * ArmAssistParams.ArmRollGain, -ArmAssistParams.ArmMax, ArmAssistParams.ArmMax);
            SwingArm(Role.UpperArmLeft, pitch, roll, 1f);
            SwingArm(Role.UpperArmRight, pitch, roll, 1f);
            SwingArm(Role.ForearmLeft, pitch, roll, ArmAssistParams.ForearmShare);
            SwingArm(Role.ForearmRight, pitch, roll, ArmAssistParams.ForearmShare);
            SwingArm(Role.HandLeft, pitch, roll, ArmAssistParams.HandShare);
            SwingArm(Role.HandRight, pitch, roll, ArmAssistParams.HandShare);
            float torsoPitch = Clamp(-velocity.Z * ArmAssistParams.TorsoRateGain, -ArmAssistParams.TorsoRateMax, ArmAssistParams.TorsoRateMax);
            float torsoRoll = Clamp(velocity.X * ArmAssistParams.TorsoRateGain, -ArmAssistParams.TorsoRateMax, ArmAssistParams.TorsoRateMax);
            Quaternion nudge = SwingQuaternion(torsoPitch, torsoRoll);
            BalanceDrive torso = drives[(int)Role.Torso];
            torso.TargetWorld = Quaternion.Normalize(Quaternion.Multiply(nudge, torso.TargetWorld));
            drives[(int)Role.Torso] = torso;
        }

        void SwingArm(Role role, float pitch, float roll, float share)
        {
            Quaternion swing = SwingQuaternion(pitch * share, roll * share);
            BalanceDrive drive = drives[(int)role];
            drive.TargetWorld = Quaternion.Normalize(Quaternion.Multiply(swing, drive.TargetWorld));
            float stiffness = drive.Stiffness * ArmAssistParams.ArmStiffnessBoost;
            drive.Stiffness = stiffness;
            drive.Damping = drive.Damping * 1.41421356f;
            float scale = parameters.TorqueScale;
            if (!Scalar.IsFinite(scale) || !(scale > 0f))
            {
                scale = 0.5f;
            }
            drive.MaxTorque = stiffness * scale;
            drives[(int)role] = drive;
        }

        void ApplyCushion()
        {
            CushionArm(Role.UpperArmLeft, ArmAssistParams.CushionRaise, -ArmAssistParams.CushionSpread);
            CushionArm(Role.UpperArmRight, ArmAssistParams.CushionRaise, ArmAssistParams.CushionSpread);
            CushionArm(Role.ForearmLeft, ArmAssistParams.CushionElbow, -ArmAssistParams.CushionSpread);
            CushionArm(Role.ForearmRight, ArmAssistParams.CushionElbow, ArmAssistParams.CushionSpread);
            CushionArm(Role.HandLeft, ArmAssistParams.CushionElbow, -ArmAssistParams.CushionSpread);
            CushionArm(Role.HandRight, ArmAssistParams.CushionElbow, ArmAssistParams.CushionSpread);
        }

        void CushionArm(Role role, float raise, float spread)
        {
            Quaternion lift = Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -raise);
            Quaternion wide = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), spread);
            Quaternion pose = Quaternion.Normalize(Quaternion.Multiply(wide, lift));
            BalanceDrive drive = drives[(int)role];
            drive.TargetWorld = Quaternion.Normalize(Quaternion.Multiply(pose, drive.TargetWorld));
            drives[(int)role] = drive;
        }

        Float3 CenterVelocity()
        {
            Float3 total = Float3.Zero;
            float mass = 0f;
            for (int i = 0; i < Roles.Count; i++)
            {
                float fraction = HumanProfile.Of((Role)i).MassFraction;
                if (!(fraction > 0f) || !Scalar.IsFinite(fraction))
                {
                    continue;
                }
                Float3 linear = SanitizeVector(bodies[i].LinearVelocity);
                total = total + linear * fraction;
                mass += fraction;
            }
            if (!(mass > 1e-6f))
            {
                return Float3.Zero;
            }
            Float3 center = total / mass;
            if (!center.IsFinite)
            {
                return Float3.Zero;
            }
            return new Float3(Clamp(center.X, -10f, 10f), 0f, Clamp(center.Z, -10f, 10f));
        }

        static Quaternion SwingQuaternion(float pitch, float roll)
        {
            if (pitch == 0f && roll == 0f)
            {
                return Quaternion.Identity;
            }
            Quaternion aboutX = Quaternion.AxisAngle(new Float3(1f, 0f, 0f), pitch);
            Quaternion aboutZ = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), roll);
            return Quaternion.Normalize(Quaternion.Multiply(aboutZ, aboutX));
        }

        static Float3 SanitizeVector(Float3 value)
        {
            if (!value.IsFinite)
            {
                return Float3.Zero;
            }
            return value;
        }

        static Quaternion SanitizeRotation(Quaternion value)
        {
            if (!value.IsValidRotation)
            {
                return Quaternion.Identity;
            }
            return value;
        }

        static float Clamp(float value, float minimum, float maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }
            if (value > maximum)
            {
                return maximum;
            }
            return value;
        }
    }
}

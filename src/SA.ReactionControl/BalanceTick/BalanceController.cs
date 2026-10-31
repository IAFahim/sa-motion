using System;
using SA.Humanoid;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;
using SA.ReactionControl.Balance;

namespace SA.ReactionControl.BalanceTick
{
    public static class BalanceController
    {
        public const float WideStepFactor = 1.5f;
        public const float AbortFallback = 1.2f;
        public const float PelvisStrength = 2000f;
        public const float ServoHipStrength = 1000f;
        public const float ServoHipDamping = 10f;
        public const float ServoKneeStrength = 900f;
        public const float ServoKneeDamping = 8f;
        public const float ServoAnkleStrength = 700f;
        public const float ServoAnkleDamping = 3f;
        public const float ServoSpineStrength = 200f;
        public const float ServoSpineDamping = 5f;
        public const float ServoMaxTorque = 500f;
        public const float PelvisDamping = 30f;
        public const float StepDeadband = 0.09f;
        public const float MaxHeightCorrection = 0.1f;
        public const float TaperBendReference = 0.2f;
        public const float TaperRate = 2.0f;
        public const float TaperMinimum = 0.5f;

        public static BalanceReport Tick(
            in BalanceParams parameters,
            ref BalanceControllerState state,
            ReadOnlySpan<RoleBody> bodies,
            ReadOnlySpan<SupportProbe> feet,
            in BalanceInput input,
            Span<JointDrive> drives)
        {
            if (drives.Length >= Roles.Count)
            {
                for (var i = 0; i < Roles.Count; i++)
                    drives[i] = JointDrive.Passive;
            }
            else
            {
                for (var i = 0; i < drives.Length; i++)
                    drives[i] = JointDrive.Passive;
                return new BalanceReport
                {
                    Supported = false,
                    Decision = BalanceDecision.Falling,
                    ComHeight = GuardHeight(state.LastComHeight)
                };
            }
            if (state.FallingLatched)
            {
                return new BalanceReport
                {
                    Supported = false,
                    Decision = BalanceDecision.Falling,
                    ComHeight = GuardHeight(state.LastComHeight)
                };
            }
            if (!FiniteInputs(bodies, feet))
            {
                return new BalanceReport
                {
                    Supported = false,
                    Decision = BalanceDecision.Falling,
                    ComHeight = GuardHeight(state.LastComHeight)
                };
            }
            var tick = parameters;
            tick.DeltaTime = GuardStep(input.Timestep);
            var clamp = Scalar.IsFinite(parameters.StepClamp) && parameters.StepClamp > 0f ? parameters.StepClamp : 0.6f;
            tick.StepClamp = input.WideStep ? clamp * WideStepFactor : clamp;
            if (Scalar.IsFinite(input.DesiredHeight) && input.DesiredHeight > 0f)
                tick.PelvisHeight = input.DesiredHeight;
            var servo = Scalar.IsFinite(parameters.LegStiffness) && parameters.LegStiffness >= 0f ? parameters.LegStiffness : 1f;
            tick.HipStrength = ServoHipStrength * servo;
            tick.HipDamping = ServoHipDamping * servo;
            tick.KneeStrength = ServoKneeStrength * servo;
            tick.KneeDamping = ServoKneeDamping * servo;
            tick.AnkleStrength = ServoAnkleStrength * servo;
            tick.AnkleDamping = ServoAnkleDamping * servo;
            tick.SpineStrength = ServoSpineStrength * servo;
            tick.SpineDamping = ServoSpineDamping * servo;
            tick.MaxTorque = ServoMaxTorque;
            var up = BalanceMath.SanitizeUnit(parameters.Up, new Float3(0f, 1f, 0f));
            var leftProbe = feet[0];
            var rightProbe = feet[1];
            leftProbe.Point = BalanceMath.Sanitize(leftProbe.Point);
            rightProbe.Point = BalanceMath.Sanitize(rightProbe.Point);
            var snapshot = default(BalanceSnapshot);
            if (!SnapshotBuilder.TryBuild(tick, bodies, leftProbe, rightProbe, ref snapshot))
            {
                return new BalanceReport
                {
                    Supported = false,
                    Decision = BalanceDecision.Falling,
                    ComHeight = GuardHeight(state.LastComHeight)
                };
            }
            var abort = Scalar.IsFinite(parameters.AbortThreshold) && parameters.AbortThreshold > 0f
                ? parameters.AbortThreshold
                : AbortFallback;
            if (!Holding(tick, snapshot, up, abort, state.Feet.Countdown, out var comHeight))
            {
                state.FallingLatched = true;
                state.LastComHeight = comHeight;
                return new BalanceReport
                {
                    Supported = false,
                    Decision = BalanceDecision.Falling,
                    ComHeight = comHeight
                };
            }
            if (!state.Seeded)
            {
                state.Pelvis.Centre = (BalanceMath.Sanitize(snapshot.Left.Foot.Frame.Position) + BalanceMath.Sanitize(snapshot.Right.Foot.Frame.Position)) * 0.5f;
                state.Pelvis.BlendVelocity = snapshot.HorizontalVelocity;
                state.AnkleClearance = SeedClearance(snapshot, leftProbe, rightProbe, up);
                state.Seeded = true;
            }
            tick.AnkleClearance = Scalar.IsFinite(state.AnkleClearance) && state.AnkleClearance > 0f ? state.AnkleClearance : 0f;
            state.Pelvis.Centre = state.Feet.Centre;
            if (state.Pelvis.FixedVelocity)
                state.Pelvis.BlendVelocity = BalanceMath.Sanitize(state.Pelvis.BlendVelocity);
            else
                state.Pelvis.BlendVelocity = snapshot.HorizontalVelocity;
            var steppingBefore = state.Feet.SteppingLeg;
            var feetBefore = state.Feet;
            FootPlacement.Update(tick, snapshot, ref state.Feet);
            if (steppingBefore == 0 && state.Feet.SteppingLeg != 0)
            {
                var want = state.Feet.SteppingLeg == 1 ? state.Feet.DistLeft : state.Feet.DistRight;
                if (!(want > StepDeadband))
                    state.Feet = feetBefore;
            }
            if (state.Feet.LeftPlanted)
            {
                if (!feetBefore.LeftPlanted)
                    state.PlantLeft = state.Feet.FootLeft;
                var holdLeft = state.Feet.FootLeft;
                holdLeft.X = state.PlantLeft.X;
                holdLeft.Z = state.PlantLeft.Z;
                state.Feet.FootLeft = holdLeft;
            }
            if (state.Feet.RightPlanted)
            {
                if (!feetBefore.RightPlanted)
                    state.PlantRight = state.Feet.FootRight;
                var holdRight = state.Feet.FootRight;
                holdRight.X = state.PlantRight.X;
                holdRight.Z = state.PlantRight.Z;
                state.Feet.FootRight = holdRight;
            }
            var pelvisY = Float3.Dot(BalanceMath.Sanitize(bodies[(int)Role.Pelvis].Position), up);
            var leftY = Float3.Dot(state.Feet.FootLeft, up);
            var rightY = Float3.Dot(state.Feet.FootRight, up);
            var lowY = leftY < rightY ? leftY : rightY;
            var correction = (pelvisY - lowY) - tick.PelvisHeight;
            if (!Scalar.IsFinite(correction))
                correction = 0f;
            if (correction > MaxHeightCorrection)
                correction = MaxHeightCorrection;
            if (correction < -MaxHeightCorrection)
                correction = -MaxHeightCorrection;
            if (state.Feet.LeftPlanted)
                state.Feet.FootLeft = state.Feet.FootLeft + up * correction;
            if (state.Feet.RightPlanted)
                state.Feet.FootRight = state.Feet.FootRight + up * correction;
            PelvisControl.Update(tick, snapshot, ref state.Pelvis, state.Feet.LeftPlanted, state.Feet.RightPlanted);
            BalanceSolve.Solve(tick, ref snapshot, ref state.Feet, ref state.Pelvis, drives);
            state.Pelvis.Lift = Scalar.IsFinite(snapshot.HeightTarget) ? snapshot.HeightTarget : 0f;
            WritePelvis(bodies[(int)Role.Pelvis].Rotation, up, tick.MaxTorque, drives);
            WriteTorso(bodies[(int)Role.Torso].Rotation, snapshot.Spine.Desired.Y, up, tick.SpineStrength, tick.SpineDamping, tick.MaxTorque, drives);
            if (tick.Taper)
            {
                ScaleKnee(snapshot.Left, Role.ShinLeft, drives);
                ScaleKnee(snapshot.Right, Role.ShinRight, drives);
            }
            MaskOwnership(drives);
            state.LastComHeight = comHeight;
            var stepping = state.Feet.SteppingLeg;
            var distance = stepping == 1 ? state.Feet.DistLeft : stepping == 2 ? state.Feet.DistRight : 0f;
            if (!Scalar.IsFinite(distance) || distance < 0f)
                distance = 0f;
            return new BalanceReport
            {
                Supported = snapshot.SupportLeft.Contact || snapshot.SupportRight.Contact,
                SteppingLeg = stepping,
                StepInProgress = stepping != 0,
                StepDistance = distance,
                Decision = stepping != 0 ? BalanceDecision.Stepping : BalanceDecision.Standing,
                ComHeight = comHeight
            };
        }

        static bool Holding(
            in BalanceParams tick,
            in BalanceSnapshot snapshot,
            Float3 up,
            float abort,
            int countdown,
            out float comHeight)
        {
            var com = BalanceMath.Sanitize(snapshot.Com);
            var left = BalanceMath.Sanitize(snapshot.Left.Foot.Frame.Position);
            var right = BalanceMath.Sanitize(snapshot.Right.Foot.Frame.Position);
            var low = Float3.Dot(left, up) < Float3.Dot(right, up) ? Float3.Dot(left, up) : Float3.Dot(right, up);
            var height = Float3.Dot(com, up) - low;
            comHeight = height;
            var middle = (left + right) * 0.5f;
            var near = BalanceMath.Horizontal(com - left, up).Length;
            var far = BalanceMath.Horizontal(com - right, up).Length;
            var mid = BalanceMath.Horizontal(com - middle, up).Length;
            var off = near < far ? near : far;
            if (mid < off)
                off = mid;
            if (off > abort * height)
                return false;
            if (tick.LowAbort && height < 0.5f)
                return false;
            if (countdown == -1)
                return false;
            return true;
        }

        static void WritePelvis(Quaternion rotation, Float3 up, float ceiling, Span<JointDrive> drives)
        {
            var clean = BalanceMath.SanitizeRotation(rotation);
            var tilted = Quaternion.Rotate(clean, new Float3(0f, 1f, 0f));
            var correction = BalanceMath.Arc(tilted, up, 1e-6f);
            var target = Quaternion.Normalize(Quaternion.Multiply(correction, clean));
            if (!target.IsValidRotation || !(ceiling > 0f) || !Scalar.IsFinite(ceiling))
            {
                drives[(int)Role.Pelvis] = JointDrive.Passive;
                return;
            }
            drives[(int)Role.Pelvis] = new JointDrive
            {
                Target = target,
                TargetAngularVelocity = Float3.Zero,
                Stiffness = PelvisStrength,
                Damping = PelvisDamping,
                MaxTorque = ceiling
            };
        }
        static void WriteTorso(Quaternion rotation, float lean, Float3 up, float stiffness, float damping, float ceiling, Span<JointDrive> drives)
        {
            var clean = BalanceMath.SanitizeRotation(rotation);
            var tilted = Quaternion.Rotate(clean, new Float3(0f, 1f, 0f));
            var anchor = Quaternion.Normalize(Quaternion.Multiply(BalanceMath.Arc(tilted, up, 1e-6f), clean));
            if (!anchor.IsValidRotation)
            {
                drives[(int)Role.Torso] = JointDrive.Passive;
                return;
            }
            var bend = Scalar.IsFinite(lean) ? lean : 0f;
            if (bend > 0.5f)
                bend = 0.5f;
            if (bend < -0.5f)
                bend = -0.5f;
            var target = Quaternion.Normalize(Quaternion.Multiply(anchor, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), bend)));
            if (!target.IsValidRotation || !(stiffness > 0f) || !Scalar.IsFinite(stiffness) || !(damping >= 0f) || !Scalar.IsFinite(damping) || !(ceiling > 0f) || !Scalar.IsFinite(ceiling))
            {
                drives[(int)Role.Torso] = JointDrive.Passive;
                return;
            }
            drives[(int)Role.Torso] = new JointDrive
            {
                Target = target,
                TargetAngularVelocity = Float3.Zero,
                Stiffness = stiffness,
                Damping = damping,
                MaxTorque = ceiling
            };
        }


        static void ScaleKnee(in BalanceLeg leg, Role kneeRole, Span<JointDrive> drives)
        {
            var actual = Scalar.IsFinite(leg.Knee.Actual) ? leg.Knee.Actual : 0f;
            var factor = 1f + TaperRate * (actual + TaperBendReference);
            if (factor > 1f)
                factor = 1f;
            if (factor < TaperMinimum)
                factor = TaperMinimum;
            var drive = drives[(int)kneeRole];
            drive.Stiffness *= factor * factor;
            drives[(int)kneeRole] = drive;
        }

        static void MaskOwnership(Span<JointDrive> drives)
        {
            for (var i = 0; i < Roles.Count; i++)
            {
                var role = (Role)i;
                if (role != Role.Pelvis && role != Role.Torso &&
                    role != Role.ThighLeft && role != Role.ThighRight &&
                    role != Role.ShinLeft && role != Role.ShinRight &&
                    role != Role.FootLeft && role != Role.FootRight)
                {
                    drives[i] = JointDrive.Passive;
                }
            }
        }

        static bool FiniteInputs(ReadOnlySpan<RoleBody> bodies, ReadOnlySpan<SupportProbe> feet)
        {
            if (bodies.Length < Roles.Count || feet.Length < 2)
                return false;
            for (var i = 0; i < Roles.Count; i++)
            {
                var body = bodies[i];
                if (!body.Position.IsFinite || !body.LinearVelocity.IsFinite || !body.AngularVelocity.IsFinite)
                    return false;
                if (!body.Rotation.IsValidRotation)
                    return false;
                if (!Scalar.IsFinite(body.InverseMass))
                    return false;
            }
            for (var i = 0; i < 2; i++)
            {
                var probe = feet[i];
                if (!probe.Point.IsFinite || !probe.Normal.IsFinite || !probe.PointVelocity.IsFinite)
                    return false;
                if (!Scalar.IsFinite(probe.Distance))
                    return false;
            }
            return true;
        }

        static float SeedClearance(in BalanceSnapshot snapshot, SupportProbe left, SupportProbe right, Float3 up)
        {
            var ankle = (BalanceMath.Sanitize(snapshot.Left.Ankle.Frame.Position) + BalanceMath.Sanitize(snapshot.Right.Ankle.Frame.Position)) * 0.5f;
            var ground = (BalanceMath.Sanitize(left.Point) + BalanceMath.Sanitize(right.Point)) * 0.5f;
            var clearance = Float3.Dot(ankle - ground, BalanceMath.SanitizeUnit(up, new Float3(0f, 1f, 0f)));
            if (!Scalar.IsFinite(clearance) || clearance < 0f)
                return 0f;
            if (clearance > 0.5f)
                return 0.5f;
            return clearance;
        }

        static float GuardStep(float step)
        {
            if (!Scalar.IsFinite(step) || step <= 0f)
                return 0.0166667f;
            if (step > 0.0333333f)
                return 0.0333333f;
            return step;
        }

        static float GuardHeight(float height)
        {
            return Scalar.IsFinite(height) ? height : 0f;
        }
    }
}

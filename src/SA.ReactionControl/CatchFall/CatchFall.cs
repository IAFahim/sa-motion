using System;
using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.ReactionControl.CatchFall
{
    public static class CatchFall
    {
        const float PositionEpsilon = 1e-4f;
        const float SideThreshold = 0.01f;
        const float DriveDamping = 1.0f;
        const float MaxTimestep = 0.1f;

        public static CatchFallState Start(in CatchFallParams p)
        {
            float elbow = Sanitize(p.ElbowRest, 0.35f);
            var arm = new CatchFallArmState
            {
                ContactVelocity = Float3.Zero,
                StiffnessScale = 1.0f,
                ClavLean1 = 0.0f,
                ClavLean2 = 0.0f,
                Elbow = elbow
            };
            return new CatchFallState
            {
                Active = 1,
                Touched = 0,
                Dir = new Float3(0.0f, -1.0f, 0.0f),
                Forward = 0.0f,
                Gain = 1.0f,
                Upright = 1.0f,
                LegLeft = 0.75f,
                LegRight = 0.75f,
                Lean2 = 0.0f,
                ArmLeft = arm,
                ArmRight = arm
            };
        }

        public static void Deactivate(ref CatchFallState state)
        {
            state.Active = 0;
        }

        public static CatchFallResult Tick(
            ref CatchFallState state,
            in CatchFallParams p,
            ReadOnlySpan<RoleBody> bodies,
            in CatchFallProbes probes,
            Float3 up,
            float dt,
            Span<JointDrive> drives)
        {
            up = SanitizeDirection(up, new Float3(0.0f, 1.0f, 0.0f));
            float step = Scalar.IsFinite(dt) && dt > 0.0f ? Scalar.Min(dt, MaxTimestep) : 0.0f;
            for (int i = 0; i < drives.Length; i++)
                drives[i] = JointDrive.Passive;
            var neutral = new CatchFallResult(Float3.Zero, Float3.Zero, Float3.Zero, 0.0f, Float3.Zero, state.Touched);
            if (state.Active == 0)
                return neutral;
            if (bodies.Length < Roles.Count || drives.Length < Roles.Count)
                return neutral;
            CatchFallParams q = SanitizeParams(p);

            RoleBody torso = SanitizeBody(bodies[(int)Role.Torso]);
            RoleBody head = SanitizeBody(bodies[(int)Role.Head]);
            float mass = torso.InverseMass > 0.0f && Scalar.IsFinite(torso.InverseMass)
                ? 1.0f / torso.InverseMass : 0.0f;
            Float3 spin = SpinTorque(up, torso.AngularVelocity, mass, q.SpinReductionGain, q.SpinTorqueScale, q.SpinTorqueMax);

            float speed = torso.LinearVelocity.Length;
            if (!Scalar.IsFinite(speed))
                speed = 0.0f;
            float gain = state.Gain;
            if (!Scalar.IsFinite(gain))
                gain = 1.0f;
            gain = Clamp(gain + (2.0f * speed - gain) * step * q.GainRate, q.GainFloor, 1.0f);
            state.Gain = gain;

            Float3 under = (SanitizeVector(state.ArmLeft.ContactVelocity) + SanitizeVector(state.ArmRight.ContactVelocity)) * 0.5f;
            Float3 fall = torso.LinearVelocity - under;
            Float3 dir = NormalizeOr(fall - up * q.FallBias, up * -1.0f);
            state.Dir = dir;

            Float3 chestForward = Quaternion.Rotate(torso.Rotation, new Float3(0.0f, 0.0f, 1.0f));
            Float3 chestLateral = Quaternion.Rotate(torso.Rotation, new Float3(1.0f, 0.0f, 0.0f));
            Float3 chestUp = Quaternion.Rotate(torso.Rotation, new Float3(0.0f, 1.0f, 0.0f));
            float forward = -Float3.Dot(chestForward, dir);
            state.Forward = Scalar.IsFinite(forward) ? forward : 0.0f;
            float upright = Clamp01(Float3.Dot(chestUp, up));
            state.Upright = upright;

            Float3 look = head.Position + dir;
            Float3 headTarget = head.Position;
            float twist = 0.0f;
            if (q.UseHeadLook != 0)
            {
                headTarget = look;
                twist = Clamp(SignedAngleAboutUp(chestForward, look - torso.Position, up), -q.SpineTwistLimit, q.SpineTwistLimit);
            }

            byte touched = state.Touched;
            Float3 handTargetLeft = TickArm(
                ref state.ArmLeft, q, SanitizeBody(bodies[(int)Role.UpperArmLeft]),
                SanitizeBody(bodies[(int)Role.ForearmLeft]), SanitizeBody(bodies[(int)Role.HandLeft]),
                probes.Left, dir, up, chestForward, chestLateral, torso.Position,
                state.Forward, upright, gain, step, drives,
                (int)Role.UpperArmLeft, (int)Role.ForearmLeft, (int)Role.HandLeft, ref touched);
            Float3 handTargetRight = TickArm(
                ref state.ArmRight, q, SanitizeBody(bodies[(int)Role.UpperArmRight]),
                SanitizeBody(bodies[(int)Role.ForearmRight]), SanitizeBody(bodies[(int)Role.HandRight]),
                probes.Right, dir, up, chestForward, chestLateral, torso.Position,
                state.Forward, upright, gain, step, drives,
                (int)Role.UpperArmRight, (int)Role.ForearmRight, (int)Role.HandRight, ref touched);
            state.Touched = touched;

            return new CatchFallResult(handTargetLeft, handTargetRight, headTarget, twist, spin, touched);
        }

        public static Float3 SpinTorque(Float3 up, Float3 angularVelocity, float mass, float gain, float scale, float cap)
        {
            up = SanitizeDirection(up, new Float3(0.0f, 1.0f, 0.0f));
            angularVelocity = SanitizeVector(angularVelocity);
            if (!Scalar.IsFinite(mass) || mass < 0.0f)
                mass = 0.0f;
            if (!Scalar.IsFinite(gain) || !Scalar.IsFinite(scale) || !Scalar.IsFinite(cap) || cap < 0.0f)
                return Float3.Zero;
            float along = Float3.Dot(up, angularVelocity);
            if (!Scalar.IsFinite(along))
                return Float3.Zero;
            Float3 torque = up * (mass * Clamp01(gain) * along * -scale);
            if (!torque.IsFinite)
                return Float3.Zero;
            float magnitude = torque.Length;
            if (magnitude > cap && magnitude > 0.0f)
                torque = torque * (cap / magnitude);
            return torque;
        }

        static Float3 TickArm(
            ref CatchFallArmState arm,
            in CatchFallParams q,
            RoleBody upper,
            RoleBody forearm,
            RoleBody hand,
            in CatchFallArmProbe probe,
            Float3 dir,
            Float3 up,
            Float3 chestForward,
            Float3 chestLateral,
            Float3 torsoPosition,
            float forward,
            float upright,
            float gain,
            float step,
            Span<JointDrive> drives,
            int upperIndex,
            int forearmIndex,
            int handIndex,
            ref byte touched)
        {
            byte hit = (byte)((probe.Probe.Hit != 0
                && probe.Probe.Point.IsFinite
                && probe.Probe.Normal.IsFinite
                && Scalar.IsFinite(probe.Probe.Distance)
                && probe.Probe.Distance >= 0.0f
                && probe.Probe.Distance <= q.ProbeLength + q.Reach) ? 1 : 0);
            Float3 contactVelocity = Float3.Zero;
            if (hit != 0 && probe.Probe.PointVelocity.IsFinite)
                contactVelocity = probe.Probe.PointVelocity;
            arm.ContactVelocity = contactVelocity;
            byte touching = (byte)(probe.Touching != 0 ? 1 : 0);

            float scale = Scalar.IsFinite(arm.StiffnessScale) ? arm.StiffnessScale : 1.0f;
            if (touching != 0)
                scale = Scalar.Max(gain, scale - q.RelaxRate * step);
            else
                scale = Scalar.Min(scale + q.RecoverRate * step, Scalar.Min(q.ArmCapScale * gain, 1.0f));
            arm.StiffnessScale = Clamp01(scale);

            float clampedForward = Clamp(forward, q.BackwardsMinArmOffset, q.ForwardMaxArmOffset);
            Float3 from;
            if (forward > 0.0f)
            {
                Float3 side = Float3.Cross(chestLateral, up);
                if (side.LengthSquared > SideThreshold && side.IsFinite)
                    side = side / side.Length;
                Float3 outDir = NormalizeOr(chestForward + Float3.Cross(chestForward, side), chestForward);
                from = upper.Position + outDir * ((upright * q.Reach + q.MinExtension) * clampedForward);
            }
            else
            {
                from = upper.Position + chestForward * clampedForward;
            }
            Float3 to = from + dir * q.ProbeLength;

            Float3 at = hit != 0 ? probe.Probe.Point : to;
            Float3 aim = touching != 0 ? (at + hand.Position) * 0.5f : at;
            Float3 offset = aim - upper.Position;
            float length = offset.IsFinite ? offset.Length : 0.0f;
            if (length > q.Reach && length > 0.0f)
                offset = offset * (q.Reach / length);
            else if (length < q.MinExtension)
                offset = offset * (q.MinExtension / (length + PositionEpsilon));
            Float3 target = upper.Position + SanitizeVector(offset);
            Float3 wantDir = NormalizeOr(offset, up * -1.0f);

            Float3 limbDown = new Float3(0.0f, -1.0f, 0.0f);
            Float3 currentDir = Quaternion.Rotate(upper.Rotation, limbDown);
            Quaternion aimError = RotationFromTo(currentDir, wantDir);
            float aimAngle = AngleBetween(currentDir, wantDir);
            Float3 foldAxis = NormalizeOr(Float3.Cross(currentDir, wantDir), chestForward);
            float foldAngle = Clamp(q.ClavFollowRatio, 0.0f, 1.0f) * aimAngle;
            Quaternion upperTarget = Quaternion.Normalize(Quaternion.Multiply(
                Quaternion.AxisAngle(foldAxis, foldAngle),
                Quaternion.Multiply(aimError, upper.Rotation)));
            float armStiffness = q.ArmsStiffness * arm.StiffnessScale;
            drives[upperIndex] = BuildDrive(upperTarget, armStiffness, q.TorqueLimit);

            Float3 wantFore = NormalizeOr(target - forearm.Position, wantDir);
            Float3 currentFore = Quaternion.Rotate(forearm.Rotation, limbDown);
            Quaternion foreBase = Quaternion.Multiply(RotationFromTo(currentFore, wantFore), forearm.Rotation);
            Quaternion foreTarget;
            if (forward >= -q.StiffnessFloor)
            {
                Float3 bendAxis = NormalizeOr(Float3.Cross(wantFore, up), chestLateral);
                foreTarget = Quaternion.Normalize(Quaternion.Multiply(
                    Quaternion.AxisAngle(bendAxis, q.ElbowRest * 0.5f), foreBase));
            }
            else
            {
                foreTarget = Quaternion.Normalize(foreBase);
            }
            drives[forearmIndex] = BuildDrive(foreTarget, armStiffness, q.TorqueLimit);

            if (hit != 0)
            {
                Float3 wantHand = NormalizeOr(at - hand.Position, wantDir);
                Float3 currentHand = Quaternion.Rotate(hand.Rotation, limbDown);
                Quaternion handTarget = Quaternion.Normalize(Quaternion.Multiply(
                    RotationFromTo(currentHand, wantHand), hand.Rotation));
                drives[handIndex] = BuildDrive(handTarget, armStiffness, q.TorqueLimit);
            }
            else
            {
                Quaternion hold = Quaternion.Normalize(hand.Rotation);
                drives[handIndex] = BuildDrive(hold, q.SafeMissStiffness, q.TorqueLimit);
            }

            if (touching != 0)
                touched = 1;
            return target;
        }

        static JointDrive BuildDrive(Quaternion target, float stiffness, float torqueLimit)
        {
            if (!Scalar.IsFinite(stiffness) || stiffness < 0.0f)
                stiffness = 0.0f;
            if (!Scalar.IsFinite(torqueLimit) || torqueLimit < 0.0f)
                torqueLimit = 0.0f;
            return new JointDrive
            {
                Target = target,
                TargetAngularVelocity = Float3.Zero,
                Stiffness = stiffness,
                Damping = stiffness > 0.0f ? DriveDamping : 0.0f,
                MaxTorque = torqueLimit
            };
        }

        static CatchFallParams SanitizeParams(in CatchFallParams p)
        {
            return new CatchFallParams(
                Sanitize(p.LegsStiffness, 6.0f),
                Sanitize(p.TorsoStiffness, 8.0f),
                Sanitize(p.ArmsStiffness, 12.0f),
                Sanitize(p.ForwardMaxArmOffset, 0.35f),
                Sanitize(p.BackwardsMinArmOffset, -0.25f),
                Clamp01(Sanitize(p.SpinReductionGain, 0.0f)),
                p.UseHeadLook,
                Scalar.Max(Sanitize(p.Reach, 0.70f), PositionEpsilon),
                Scalar.Max(Sanitize(p.ProbeLength, 2.0f), 0.0f),
                Scalar.Max(Sanitize(p.MinExtension, 0.15f), 0.0f),
                Clamp01(Sanitize(p.StiffnessFloor, 0.15f)),
                Clamp01(Sanitize(p.GainFloor, 0.20f)),
                Scalar.Max(Sanitize(p.RelaxRate, 0.5f), 0.0f),
                Scalar.Max(Sanitize(p.RecoverRate, 2.0f), 0.0f),
                Scalar.Max(Sanitize(p.GainRate, 1.5f), 0.0f),
                Clamp01(Sanitize(p.ArmCapScale, 1.0f)),
                Sanitize(p.FallBias, 0.5f),
                Sanitize(p.SpinTorqueScale, 2.0f),
                Scalar.Max(Sanitize(p.SpinTorqueMax, 60.0f), 0.0f),
                Clamp01(Sanitize(p.ClavFollowRatio, 0.5f)),
                Sanitize(p.ElbowRest, 0.35f),
                Scalar.Max(Sanitize(p.TorqueLimit, 80.0f), 0.0f),
                Scalar.Max(Sanitize(p.SafeMissStiffness, 1.0f), 0.0f),
                Scalar.Max(Sanitize(p.SpineTwistLimit, 1.0f), 0.0f));
        }

        static RoleBody SanitizeBody(RoleBody body)
        {
            if (!body.Position.IsFinite)
                body.Position = Float3.Zero;
            if (!body.Rotation.IsValidRotation)
                body.Rotation = Quaternion.Identity;
            else
                body.Rotation = Quaternion.Normalize(body.Rotation);
            if (!body.LinearVelocity.IsFinite)
                body.LinearVelocity = Float3.Zero;
            if (!body.AngularVelocity.IsFinite)
                body.AngularVelocity = Float3.Zero;
            if (!Scalar.IsFinite(body.InverseMass) || body.InverseMass < 0.0f)
                body.InverseMass = 0.0f;
            return body;
        }

        static float Sanitize(float value, float fallback)
        {
            return Scalar.IsFinite(value) ? value : fallback;
        }

        static Float3 SanitizeVector(Float3 value)
        {
            return value.IsFinite ? value : Float3.Zero;
        }

        static Float3 SanitizeDirection(Float3 value, Float3 fallback)
        {
            if (!value.IsFinite || value.LengthSquared < 1e-12f)
                return fallback;
            return value / value.Length;
        }

        static Float3 NormalizeOr(Float3 value, Float3 fallback)
        {
            if (!value.IsFinite)
                return fallback;
            float length = value.Length;
            if (!Scalar.IsFinite(length) || length < 1e-6f)
                return fallback;
            return value / length;
        }

        static Quaternion RotationFromTo(Float3 from, Float3 to)
        {
            if (!from.IsFinite || !to.IsFinite)
                return Quaternion.Identity;
            float fromLength = from.Length;
            float toLength = to.Length;
            if (!Scalar.IsFinite(fromLength) || !Scalar.IsFinite(toLength) || fromLength < 1e-6f || toLength < 1e-6f)
                return Quaternion.Identity;
            Float3 a = from / fromLength;
            Float3 b = to / toLength;
            float d = Clamp(Float3.Dot(a, b), -1.0f, 1.0f);
            if (d >= 1.0f - 1e-6f)
                return Quaternion.Identity;
            if (d <= -1.0f + 1e-6f)
            {
                Float3 axis = Float3.Cross(a, new Float3(1.0f, 0.0f, 0.0f));
                if (axis.LengthSquared < 1e-6f)
                    axis = Float3.Cross(a, new Float3(0.0f, 0.0f, 1.0f));
                if (axis.LengthSquared < 1e-6f)
                    return Quaternion.Identity;
                axis = axis / axis.Length;
                return Quaternion.AxisAngle(axis, Scalar.Pi);
            }
            Float3 cross = Float3.Cross(a, b);
            float w = 1.0f + d;
            float length = (float)Math.Sqrt(cross.LengthSquared + w * w);
            if (!Scalar.IsFinite(length) || length < 1e-6f)
                return Quaternion.Identity;
            return new Quaternion(cross.X / length, cross.Y / length, cross.Z / length, w / length);
        }

        static float AngleBetween(Float3 a, Float3 b)
        {
            if (!a.IsFinite || !b.IsFinite)
                return 0.0f;
            float la = a.Length;
            float lb = b.Length;
            if (!Scalar.IsFinite(la) || !Scalar.IsFinite(lb) || la < 1e-6f || lb < 1e-6f)
                return 0.0f;
            float d = Clamp(Float3.Dot(a / la, b / lb), -1.0f, 1.0f);
            return (float)Math.Acos(d);
        }

        static float SignedAngleAboutUp(Float3 a, Float3 b, Float3 up)
        {
            if (!a.IsFinite || !b.IsFinite)
                return 0.0f;
            Float3 flatA = a - up * Float3.Dot(a, up);
            Float3 flatB = b - up * Float3.Dot(b, up);
            if (!flatA.IsFinite || !flatB.IsFinite)
                return 0.0f;
            float la = flatA.Length;
            float lb = flatB.Length;
            if (!Scalar.IsFinite(la) || !Scalar.IsFinite(lb) || la < 1e-6f || lb < 1e-6f)
                return 0.0f;
            flatA = flatA / la;
            flatB = flatB / lb;
            float y = Float3.Dot(up, Float3.Cross(flatA, flatB));
            float x = Float3.Dot(flatA, flatB);
            return (float)Math.Atan2(y, x);
        }

        static float Clamp(float value, float low, float high)
        {
            if (!Scalar.IsFinite(value))
                return low;
            if (value < low)
                return low;
            if (value > high)
                return high;
            return value;
        }

        static float Clamp01(float value)
        {
            if (!Scalar.IsFinite(value))
                return 0.0f;
            if (value < 0.0f)
                return 0.0f;
            if (value > 1.0f)
                return 1.0f;
            return value;
        }
    }
}

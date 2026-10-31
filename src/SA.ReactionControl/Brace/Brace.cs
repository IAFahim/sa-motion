using System;
using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Brace
{
    public static class Brace
    {
        const float SmoothRate = 2f;
        const float LeanSmoothRate = 3f;
        const float MaxSwing = 2.1f;
        const float MaxElbow = 1.2f;
        const float FarDistance = 100f;
        const float Epsilon = 1e-6f;
        const float MinSegment = 1e-4f;

        static readonly Float3 Up = new Float3(0f, 1f, 0f);
        static readonly Float3 Forward = new Float3(0f, 0f, 1f);
        static readonly Float3 LeftFallback = new Float3(-1f, 0f, 0f);
        static readonly Float3 RightFallback = new Float3(1f, 0f, 0f);

        public static void Activate(ref BraceState state)
        {
            state = default;
        }

        public static BraceResult Tick(
            in BraceParams p,
            ref BraceState state,
            ReadOnlySpan<RoleBody> bodies,
            in SupportProbe wall,
            in SupportProbe touchLeft,
            in SupportProbe touchRight,
            in BalanceHint hint,
            float dt,
            Span<JointDrive> drives)
        {
            if (bodies.Length < Roles.Count || drives.Length < Roles.Count || !Scalar.IsFinite(dt) || dt <= 0f)
            {
                PassiveAll(drives);
                state = default;
                return IdleResult();
            }
            RoleBody pelvis = bodies[(int)Role.Pelvis];
            RoleBody torso = bodies[(int)Role.Torso];
            RoleBody upperLeft = bodies[(int)Role.UpperArmLeft];
            RoleBody upperRight = bodies[(int)Role.UpperArmRight];
            RoleBody foreLeft = bodies[(int)Role.ForearmLeft];
            RoleBody foreRight = bodies[(int)Role.ForearmRight];
            RoleBody handLeft = bodies[(int)Role.HandLeft];
            RoleBody handRight = bodies[(int)Role.HandRight];
            if (!pelvis.Position.IsFinite || !torso.Position.IsFinite
                || !upperLeft.Position.IsFinite || !upperRight.Position.IsFinite
                || !foreLeft.Position.IsFinite || !foreRight.Position.IsFinite
                || !handLeft.Position.IsFinite || !handRight.Position.IsFinite)
            {
                PassiveAll(drives);
                state = default;
                return IdleResult();
            }
            SanitizeState(ref state);
            PassiveAll(drives);
            bool wallHere = wall.Hit != 0 && wall.Point.IsFinite;
            bool contactLeft = touchLeft.Hit != 0 && touchLeft.Point.IsFinite;
            bool contactRight = touchRight.Hit != 0 && touchRight.Point.IsFinite;
            if (contactLeft)
            {
                state.ContactLeft = touchLeft.Point;
                state.NormalLeft = FiniteUnit(touchLeft.Normal, Up);
            }
            if (contactRight)
            {
                state.ContactRight = touchRight.Point;
                state.NormalRight = FiniteUnit(touchRight.Normal, Up);
            }
            float holdTime = GainOr(p.ContactHoldTime, 0.4f);
            if (holdTime < 0f)
            {
                holdTime = 0f;
            }
            if (contactLeft || contactRight)
            {
                state.HoldTimer = holdTime;
                state.HasContact = 1;
            }
            else if (state.HoldTimer > 0f)
            {
                state.HoldTimer -= dt;
                if (state.HoldTimer <= 0f)
                {
                    state.HoldTimer = 0f;
                    state.HasContact = 0;
                }
            }
            else
            {
                state.HasContact = 0;
            }
            Float3 chest = torso.Position;
            float measured = wallHere ? (wall.Point - chest).Length : FarDistance;
            if (!Scalar.IsFinite(measured))
            {
                measured = FarDistance;
            }
            if (!(state.SmoothedDistance > 0f))
            {
                state.SmoothedDistance = measured;
            }
            float speedLimit = GainOr(p.TargetSpeedLimit, 3f);
            if (speedLimit < 0f)
            {
                speedLimit = 0f;
            }
            float closing = wallHere ? (state.SmoothedDistance - measured) / dt : 0f;
            closing = Clamp(closing, -speedLimit, speedLimit);
            float floor = GainOr(p.ApproachFloor, 0.05f);
            if (floor < 0f)
            {
                floor = 0f;
            }
            float prediction = wallHere ? measured - closing * GainOr(p.PredictionTime, 0.35f) : FarDistance;
            if (prediction < floor)
            {
                prediction = floor;
            }
            float blend = dt * SmoothRate;
            if (blend > 1f)
            {
                blend = 1f;
            }
            state.SmoothedDistance += (prediction - state.SmoothedDistance) * blend;
            bool wasBraced = state.Phase == BracePhase.Braced;
            float band = GainOr(p.HysteresisBand, 0.05f);
            if (band < 0f)
            {
                band = 0f;
            }
            float engageAt = GainOr(p.BraceDistance, 0.9f) + (wasBraced ? band : 0f);
            bool braced = wallHere && state.SmoothedDistance < engageAt;
            bool holding = state.HasContact != 0;
            if (braced || (holding && (wallHere || state.HoldTimer > 0f)))
            {
                state.Phase = BracePhase.Braced;
            }
            else if (wallHere)
            {
                state.Phase = BracePhase.Reaching;
            }
            else
            {
                state.Phase = BracePhase.Idle;
                state.ArmMask = 0;
                state.StaggerLeft = 0f;
                state.StaggerRight = 0f;
            }
            float support = hint.Support;
            float hintLean = hint.Lean;
            float reach = GainOr(p.MaxReach, 0.75f);
            if (reach < 0f)
            {
                reach = 0f;
            }
            if (state.Phase != BracePhase.Idle && wallHere && !(holding && !contactLeft && !contactRight && state.HoldTimer > 0f))
            {
                UpdateArms(p, ref state, torso, upperLeft, upperRight, wall.Point, reach, dt);
            }
            if (state.StaggerLeft > 0f)
            {
                state.StaggerLeft -= dt;
                if (state.StaggerLeft < 0f)
                {
                    state.StaggerLeft = 0f;
                }
            }
            if (state.StaggerRight > 0f)
            {
                state.StaggerRight -= dt;
                if (state.StaggerRight < 0f)
                {
                    state.StaggerRight = 0f;
                }
            }
            bool reachLeft = (state.ArmMask & 1) != 0 && state.StaggerLeft <= 0f;
            bool reachRight = (state.ArmMask & 2) != 0 && state.StaggerRight <= 0f;
            float spread = GainOr(p.SideSpread, 0.12f);
            if (spread < 0f)
            {
                spread = 0f;
            }
            if (wallHere)
            {
                Float3 sideDir = SideDirection(chest, wall.Point);
                if (reachLeft)
                {
                    state.ReachLeft = ClampReach(upperLeft.Position, AimPoint(wall.Point, sideDir, -spread, contactLeft, touchLeft.Point), reach);
                }
                if (reachRight)
                {
                    state.ReachRight = ClampReach(upperRight.Position, AimPoint(wall.Point, sideDir, spread, contactRight, touchRight.Point), reach);
                }
            }
            if (state.Phase == BracePhase.Idle)
            {
                return new BraceResult(BracePhase.Idle, 0, 0, state.SmoothedDistance, 0f, 0f, state.ReachLeft, state.ReachRight);
            }
            bool braceLeft = contactLeft || (holding && !wallHere);
            Float3 normalLeft = braceLeft ? state.NormalLeft : Up;
            if (reachLeft)
            {
                ReachArm(p, upperLeft, foreLeft, handLeft, chest, state.ReachLeft, normalLeft, braceLeft, (int)Role.UpperArmLeft, (int)Role.ForearmLeft, (int)Role.HandLeft, LeftFallback, drives);
            }
            else
            {
                GuardPose(p, upperLeft, foreLeft, handLeft, support, (int)Role.UpperArmLeft, (int)Role.ForearmLeft, (int)Role.HandLeft, drives);
            }
            bool braceRight = contactRight || (holding && !wallHere);
            Float3 normalRight = braceRight ? state.NormalRight : Up;
            if (reachRight)
            {
                ReachArm(p, upperRight, foreRight, handRight, chest, state.ReachRight, normalRight, braceRight, (int)Role.UpperArmRight, (int)Role.ForearmRight, (int)Role.HandRight, RightFallback, drives);
            }
            else
            {
                GuardPose(p, upperRight, foreRight, handRight, support, (int)Role.UpperArmRight, (int)Role.ForearmRight, (int)Role.HandRight, drives);
            }
            float leanGain = GainOr(p.LeanGain, 0.5f);
            Float3 toWall = wallHere ? wall.Point - chest : Float3.Zero;
            float wallDist = toWall.Length;
            Float3 facing = ForwardOf(torso.Rotation);
            float align = wallHere && Scalar.IsFinite(wallDist) && wallDist > Epsilon
                ? Clamp(Float3.Dot(toWall / wallDist, facing), -1f, 1f)
                : 0f;
            float over = wallHere ? chest.Y - wall.Point.Y : 0f;
            if (!Scalar.IsFinite(over))
            {
                over = 0f;
            }
            float raw = wallHere ? Clamp(over * leanGain, -0.5f, 0.5f) * (0.5f + 0.5f * align) + hintLean * 0.25f : 0f;
            float leanBlend = dt * LeanSmoothRate;
            if (leanBlend > 1f)
            {
                leanBlend = 1f;
            }
            state.Lean += (raw - state.Lean) * leanBlend;
            float crouch = state.Phase == BracePhase.Braced ? GainOr(p.CrouchAmount, 0.12f) : 0f;
            if (crouch < 0f)
            {
                crouch = 0f;
            }
            byte contactFlag = holding ? (byte)1 : (byte)0;
            return new BraceResult(state.Phase, state.ArmMask, contactFlag, state.SmoothedDistance, state.Lean, crouch, state.ReachLeft, state.ReachRight);
        }

        static void UpdateArms(
            in BraceParams p,
            ref BraceState state,
            RoleBody torso,
            RoleBody upperLeft,
            RoleBody upperRight,
            Float3 target,
            float reach,
            float dt)
        {
            float margin = GainOr(p.ReleaseMargin, 0.08f);
            if (margin < 0f)
            {
                margin = 0f;
            }
            Float3 facing = ForwardOf(torso.Rotation);
            bool wantLeft = WantArm(upperLeft.Position, target, facing, reach + (((state.ArmMask & 1) != 0) ? margin : 0f));
            bool wantRight = WantArm(upperRight.Position, target, facing, reach + (((state.ArmMask & 2) != 0) ? margin : 0f));
            byte want = (byte)((wantLeft ? 1 : 0) | (wantRight ? 2 : 0));
            if (want == 3 && (state.StaggerLeft <= 0f && state.StaggerRight <= 0f))
            {
                bool freshLeft = (state.ArmMask & 1) == 0;
                bool freshRight = (state.ArmMask & 2) == 0;
                if (freshLeft || freshRight)
                {
                    float distLeft = (target - upperLeft.Position).Length;
                    float distRight = (target - upperRight.Position).Length;
                    if (!Scalar.IsFinite(distLeft))
                    {
                        distLeft = FarDistance;
                    }
                    if (!Scalar.IsFinite(distRight))
                    {
                        distRight = FarDistance;
                    }
                    float stagger = GainOr(p.StaggerTime, 0.15f);
                    if (stagger < 0f)
                    {
                        stagger = 0f;
                    }
                    if (distLeft >= distRight)
                    {
                        state.StaggerLeft = stagger;
                        state.FartherSide = 0;
                    }
                    else
                    {
                        state.StaggerRight = stagger;
                        state.FartherSide = 1;
                    }
                }
            }
            if (want != 3)
            {
                state.StaggerLeft = 0f;
                state.StaggerRight = 0f;
            }
            state.ArmMask = want;
        }

        static bool WantArm(Float3 shoulder, Float3 target, Float3 facing, float range)
        {
            Float3 offset = target - shoulder;
            float dist = offset.Length;
            if (!Scalar.IsFinite(dist) || dist <= Epsilon || dist >= range)
            {
                return false;
            }
            return Float3.Dot(offset / dist, facing) > 0f;
        }

        static Float3 AimPoint(Float3 wallPoint, Float3 sideDir, float lateral, bool snap, Float3 snapPoint)
        {
            Float3 aim = snap ? snapPoint : wallPoint;
            return aim + sideDir * lateral;
        }

        static Float3 SideDirection(Float3 chest, Float3 target)
        {
            Float3 along = target - chest;
            float len = along.Length;
            Float3 dir = Scalar.IsFinite(len) && len > Epsilon ? along / len : Forward;
            Float3 side = Float3.Cross(Up, dir);
            float sideLen = side.Length;
            if (Scalar.IsFinite(sideLen) && sideLen > Epsilon)
            {
                return side / sideLen;
            }
            return new Float3(1f, 0f, 0f);
        }

        static Float3 ClampReach(Float3 shoulder, Float3 aim, float reach)
        {
            Float3 offset = aim - shoulder;
            float len = offset.Length;
            if (!Scalar.IsFinite(len) || len <= Epsilon)
            {
                return shoulder;
            }
            if (len > reach)
            {
                return shoulder + offset * (reach / len);
            }
            return aim;
        }

        static void ReachArm(
            in BraceParams p,
            RoleBody upper,
            RoleBody fore,
            RoleBody hand,
            Float3 chest,
            Float3 target,
            Float3 normal,
            bool contact,
            int shoulderIndex,
            int elbowIndex,
            int wristIndex,
            Float3 fallback,
            Span<JointDrive> drives)
        {
            float stiffness = GainOr(p.BodyStiffness, 40f);
            float damping = GainOr(p.ArmDamping, 6f);
            float torque = GainOr(p.MaxTorque, 200f);
            Float3 shoulder = upper.Position;
            Float3 elbow = fore.Position;
            Float3 palm = hand.Position;
            Float3 upperDir = NormChain(elbow - shoulder, chest - shoulder, fallback);
            Float3 aimDir = NormOr(target - shoulder, upperDir);
            Quaternion swing = SwingClamped(upperDir, aimDir, MaxSwing);
            drives[shoulderIndex] = MakeDrive(Quaternion.Normalize(Quaternion.Multiply(swing, NormalizeOrIdentity(upper.Rotation))), stiffness, damping, torque);
            float upperLen = SegmentLength(elbow - shoulder);
            float foreLen = SegmentLength(palm - elbow);
            float span = upperLen + foreLen - MinSegment;
            float want = (target - shoulder).Length;
            if (!Scalar.IsFinite(want))
            {
                want = span;
            }
            if (want > span)
            {
                want = span;
            }
            float desired = InteriorAngle(upperLen, foreLen, want);
            float have = (palm - shoulder).Length;
            if (!Scalar.IsFinite(have))
            {
                have = span;
            }
            if (have > span)
            {
                have = span;
            }
            float current = InteriorAngle(upperLen, foreLen, have);
            float delta = Clamp(desired - current, -MaxElbow, MaxElbow);
            Float3 foreDir = NormChain(palm - elbow, aimDir, upperDir);
            Float3 axis = NormChain(Float3.Cross(upperDir, foreDir), Float3.Cross(upperDir, Up), fallback);
            Quaternion bend = Quaternion.AxisAngle(axis, delta);
            drives[elbowIndex] = MakeDrive(Quaternion.Normalize(Quaternion.Multiply(bend, NormalizeOrIdentity(fore.Rotation))), stiffness, damping, torque);
            Float3 align = contact ? normal * -1f : NormOr(target - palm, aimDir);
            Float3 handAxis = Quaternion.Rotate(NormalizeOrIdentity(hand.Rotation), Forward);
            Quaternion wristSwing = FromTo(handAxis, align);
            float wristStiffness = GainOr(p.WristStiffness, 30f);
            float wristDamping = GainOr(p.WristDamping, 5f);
            drives[wristIndex] = MakeDrive(Quaternion.Normalize(Quaternion.Multiply(wristSwing, NormalizeOrIdentity(hand.Rotation))), wristStiffness, wristDamping, torque);
        }

        static void GuardPose(
            in BraceParams p,
            RoleBody upper,
            RoleBody fore,
            RoleBody hand,
            float support,
            int shoulderIndex,
            int elbowIndex,
            int wristIndex,
            Span<JointDrive> drives)
        {
            float stiffness = GainOr(p.GuardStiffness, 8f) * (0.25f + 0.75f * support);
            float damping = GainOr(p.GuardDamping, 2f);
            float torque = GainOr(p.MaxTorque, 200f);
            drives[shoulderIndex] = MakeDrive(NormalizeOrIdentity(upper.Rotation), stiffness, damping, torque);
            drives[elbowIndex] = MakeDrive(NormalizeOrIdentity(fore.Rotation), stiffness, damping, torque);
            drives[wristIndex] = MakeDrive(NormalizeOrIdentity(hand.Rotation), stiffness, damping, torque);
        }

        static JointDrive MakeDrive(Quaternion target, float stiffness, float damping, float torque)
        {
            return new JointDrive
            {
                Target = target,
                TargetAngularVelocity = Float3.Zero,
                Stiffness = stiffness,
                Damping = damping,
                MaxTorque = torque
            };
        }

        static void PassiveAll(Span<JointDrive> drives)
        {
            for (int i = 0; i < drives.Length; i++)
            {
                drives[i] = JointDrive.Passive;
            }
        }

        static void SanitizeState(ref BraceState state)
        {
            if (state.Phase != BracePhase.Idle && state.Phase != BracePhase.Reaching && state.Phase != BracePhase.Braced)
            {
                state.Phase = BracePhase.Idle;
            }
            state.ArmMask &= 3;
            if (state.FartherSide > 1)
            {
                state.FartherSide = 0;
            }
            if (state.HasContact > 1)
            {
                state.HasContact = 0;
            }
            if (!Scalar.IsFinite(state.SmoothedDistance) || state.SmoothedDistance < 0f)
            {
                state.SmoothedDistance = 0f;
            }
            if (!Scalar.IsFinite(state.HoldTimer) || state.HoldTimer < 0f)
            {
                state.HoldTimer = 0f;
            }
            if (!Scalar.IsFinite(state.StaggerLeft) || state.StaggerLeft < 0f)
            {
                state.StaggerLeft = 0f;
            }
            if (!Scalar.IsFinite(state.StaggerRight) || state.StaggerRight < 0f)
            {
                state.StaggerRight = 0f;
            }
            if (!Scalar.IsFinite(state.Lean))
            {
                state.Lean = 0f;
            }
            state.ReachLeft = FiniteOrZero(state.ReachLeft);
            state.ReachRight = FiniteOrZero(state.ReachRight);
            state.ContactLeft = FiniteOrZero(state.ContactLeft);
            state.ContactRight = FiniteOrZero(state.ContactRight);
            state.NormalLeft = FiniteUnit(state.NormalLeft, Up);
            state.NormalRight = FiniteUnit(state.NormalRight, Up);
        }

        static BraceResult IdleResult()
        {
            return new BraceResult(BracePhase.Idle, 0, 0, 0f, 0f, 0f, Float3.Zero, Float3.Zero);
        }

        static Float3 FiniteOrZero(Float3 value)
        {
            return value.IsFinite ? value : Float3.Zero;
        }

        static Float3 FiniteUnit(Float3 value, Float3 fallback)
        {
            if (!value.IsFinite)
            {
                return fallback;
            }
            float len = value.Length;
            if (!(len > Epsilon))
            {
                return fallback;
            }
            return value / len;
        }

        static float GainOr(float value, float fallback)
        {
            if (!Scalar.IsFinite(value) || value < 0f)
            {
                return fallback;
            }
            return value;
        }

        static float Clamp(float value, float low, float high)
        {
            return value < low ? low : (value > high ? high : value);
        }

        static Float3 NormOr(Float3 value, Float3 fallback)
        {
            float len = value.Length;
            if (!Scalar.IsFinite(len) || !(len > Epsilon))
            {
                return fallback;
            }
            return value / len;
        }

        static Float3 NormChain(Float3 first, Float3 second, Float3 fallback)
        {
            float len = first.Length;
            if (Scalar.IsFinite(len) && len > Epsilon)
            {
                return first / len;
            }
            return NormOr(second, fallback);
        }

        static float SegmentLength(Float3 span)
        {
            float len = span.Length;
            if (!Scalar.IsFinite(len) || len < MinSegment)
            {
                return MinSegment;
            }
            return len;
        }

        static float InteriorAngle(float a, float b, float opposite)
        {
            float denom = 2f * a * b;
            if (!(denom > 0f))
            {
                return 0f;
            }
            float cos = (a * a + b * b - opposite * opposite) / denom;
            cos = Clamp(cos, -1f, 1f);
            return (float)Math.Acos(cos);
        }

        static Quaternion NormalizeOrIdentity(Quaternion value)
        {
            if (!value.IsValidRotation)
            {
                return Quaternion.Identity;
            }
            return Quaternion.Normalize(value);
        }

        static Float3 ForwardOf(Quaternion rotation)
        {
            return Quaternion.Rotate(NormalizeOrIdentity(rotation), Forward);
        }

        static Quaternion FromTo(Float3 from, Float3 to)
        {
            Float3 a = NormOr(from, Forward);
            Float3 b = NormOr(to, Forward);
            float dot = Clamp(Float3.Dot(a, b), -1f, 1f);
            if (dot >= 1f - Epsilon)
            {
                return Quaternion.Identity;
            }
            if (dot <= -1f + Epsilon)
            {
                Float3 axis = Float3.Cross(a, Up);
                if (!(axis.LengthSquared > Epsilon * Epsilon))
                {
                    axis = Float3.Cross(a, new Float3(1f, 0f, 0f));
                }
                axis = NormOr(axis, Up);
                return Quaternion.AxisAngle(axis, Scalar.Pi);
            }
            Float3 cross = Float3.Cross(a, b);
            return Quaternion.Normalize(new Quaternion(cross.X, cross.Y, cross.Z, 1f + dot));
        }

        static Quaternion SwingClamped(Float3 from, Float3 to, float limit)
        {
            Quaternion swing = FromTo(from, to);
            float w = Clamp(swing.W, -1f, 1f);
            float angle = 2f * (float)Math.Acos(w);
            if (!(angle > Epsilon) || !(angle > limit))
            {
                return swing;
            }
            Float3 axis = NormOr(swing.Vector, Forward);
            return Quaternion.AxisAngle(axis, limit);
        }
    }
}

using System;
using SA.Humanoid;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Balance
{
    public static class BalanceSolve
    {
        public static void Solve(
            in BalanceParams p,
            ref BalanceSnapshot s,
            ref BalanceFeet f,
            ref BalancePelvisState pv,
            Span<JointDrive> drives)
        {
            for (var i = 0; i < drives.Length && i < Roles.Count; i++)
                drives[i] = JointDrive.Passive;
            if (drives.Length < Roles.Count)
                return;
            var up = BalanceMath.Sanitize(p.Up);
            if (!(up.Length > 1e-6f))
                up = new Float3(0f, 1f, 0f);
            else
                up = up / up.Length;
            var hipShift = Scalar.IsFinite(pv.HipShift) ? BalanceMath.Clamp(pv.HipShift, -1f, 1f) : 0f;
            var anklePitch = Scalar.IsFinite(pv.AnklePitch) ? BalanceMath.Clamp(pv.AnklePitch, -1f, 1f) : 0f;
            var yawLeft = Scalar.IsFinite(pv.YawLeft) ? pv.YawLeft : 0f;
            var yawRight = Scalar.IsFinite(pv.YawRight) ? pv.YawRight : 0f;
            var pelvisHeight = Scalar.IsFinite(p.PelvisHeight) && p.PelvisHeight > 0f ? p.PelvisHeight : 0.95f;
            var legScale = Scalar.IsFinite(p.LegLength) && p.LegLength > 0f ? p.LegLength : 1f;
            var centre = BalanceMath.Sanitize(f.Centre);
            var t0 = BalanceMath.Sanitize(f.FootLeft) + up * (hipShift * p.HipSpread);
            var t1 = BalanceMath.Sanitize(f.FootRight) + up * (-(hipShift * p.HipSpread));
            if (!Scalar.IsFinite(p.HipSpread))
            {
                t0 = BalanceMath.Sanitize(f.FootLeft);
                t1 = BalanceMath.Sanitize(f.FootRight);
            }
            var lean = BalanceMath.Clamp(hipShift, -p.LeanClamp, p.LeanClamp);
            var spine = s.Spine;
            spine.Desired = new Float3(spine.Desired.X, lean, spine.Desired.Z);
            s.Spine = spine;
            var lowLeft = Float3.Dot(BalanceMath.Sanitize(f.FootLeft), up) - Drop(BalanceMath.Sanitize(f.FootLeft), centre, up, pelvisHeight, p);
            var lowRight = BalanceMath.Sanitize(f.FootRight);
            var lowRightValue = Float3.Dot(lowRight, up) - Drop(lowRight, centre, up, pelvisHeight, p);
            var low = lowLeft < lowRightValue ? lowLeft : lowRightValue;
            var both = f.LeftPlanted && f.RightPlanted;
            s.HeightTarget = pelvisHeight * legScale - (Float3.Dot(centre, up) - low) + (both ? 0f : p.SingleSupportSink);
            if (!Scalar.IsFinite(s.HeightTarget))
                s.HeightTarget = pelvisHeight;
            var targetLeft = t0;
            var targetRight = t1;
            var normalLeft = BalanceMath.Sanitize(s.SupportLeft.Normal);
            var normalRight = BalanceMath.Sanitize(s.SupportRight.Normal);
            if (f.LeftPlanted)
            {
                var placed = FootPlacement.Reach(t0, up, s.Pelvis.Frame, p.StepClamp * s.Left.Hip.HalfLean1, p.StepClamp * s.Left.Hip.HalfLean2, p);
                targetLeft = placed.Position;
            }
            if (f.RightPlanted)
            {
                var placed = FootPlacement.Reach(t1, up, s.Pelvis.Frame, p.StepClamp * s.Right.Hip.HalfLean1, p.StepClamp * s.Right.Hip.HalfLean2, p);
                targetRight = placed.Position;
            }
            SolveLeg(p, up, targetLeft, yawLeft, 1f, !f.LeftPlanted, p.HipStrength, p.HipDamping, ref s, true);
            SolveLeg(p, up, targetRight, yawRight, -1f, !f.RightPlanted, p.HipStrength, p.HipDamping, ref s, false);
            SolveAnkle(p, up, targetLeft, normalLeft, anklePitch + lean, yawLeft * 0.5f, f.LeftPlanted, p.AnkleStrength, p.AnkleDamping, ref s, true);
            SolveAnkle(p, up, targetRight, normalRight, anklePitch - lean, yawRight * -0.5f, f.RightPlanted, p.AnkleStrength, p.AnkleDamping, ref s, false);
            RedistributeOverTravel(ref s);
            WriteDrives(p, s, f, up, drives);
        }

        static float Drop(Float3 foot, Float3 centre, Float3 up, float pelvisHeight, in BalanceParams p)
        {
            if (!(pelvisHeight > 1e-6f))
                return 0f;
            var spread = Scalar.IsFinite(p.HipSpread) && p.HipSpread >= 0f ? p.HipSpread : 0.11f;
            var gain = Scalar.IsFinite(p.DropGain) ? p.DropGain : 2f;
            var cap = Scalar.IsFinite(p.DropCapFraction) && p.DropCapFraction >= 0f ? p.DropCapFraction : 0.25f;
            var horizontal = BalanceMath.Horizontal(foot - centre, up).Length;
            if (!Scalar.IsFinite(horizontal))
                return 0f;
            var excess = (horizontal - spread) * gain;
            var energy = excess * excess;
            var limit = pelvisHeight * pelvisHeight * cap;
            if (energy > limit)
                energy = limit;
            var remaining = pelvisHeight * pelvisHeight - energy;
            if (!(remaining > 0f))
                return pelvisHeight;
            return pelvisHeight - (float)Math.Sqrt(remaining);
        }


        static void SolveLeg(
            in BalanceParams p,
            Float3 up,
            Float3 target,
            float yaw,
            float side,
            bool swing,
            float hipStrength,
            float hipDamping,
            ref BalanceSnapshot s,
            bool left)
        {
            var leg = left ? s.Left : s.Right;
            var goal = BalanceMath.Sanitize(target);
            var clearance = Scalar.IsFinite(p.AnkleClearance) && p.AnkleClearance > 0f ? p.AnkleClearance : 0f;
            if (clearance > 0f)
                goal = goal + up * clearance;
            var lead = swing ? p.SwingLeadGain : 0f;
            if (lead > 0f && Scalar.IsFinite(lead))
                goal = LeadTarget(goal, lead, hipStrength, hipDamping, p.MinLeadDistance, leg.Hip, s.Pelvis, s.Velocity);
            var lift = 0f;
            var blend = 0f;
            if (swing)
            {
                var footPos = leg.Ankle.Frame.Position;
                var distance = BalanceMath.Sanitize(goal) - footPos;
                var ratio = 0f;
                if (hipStrength > 1e-6f && Scalar.IsFinite(hipStrength) && Scalar.IsFinite(hipDamping) && hipDamping >= 0f)
                    ratio = hipDamping / hipStrength;
                var reach = Scalar.IsFinite(p.SwingReach) && p.SwingReach > 0f ? p.SwingReach : 1f;
                var span = distance.Length;
                if (!Scalar.IsFinite(span))
                    span = 0f;
                var capped = span < ratio * reach ? span : ratio * reach;
                var shaped = capped + p.SwingMinDistance;
                var open = 1f - p.SwingMinDistance / (shaped > 1e-9f ? shaped : 1e-9f);
                if (open < 0f)
                    open = 0f;
                if (!Scalar.IsFinite(open))
                    open = 0f;
                var ground = left ? s.SupportLeft.GroundVelocity : s.SupportRight.GroundVelocity;
                var ahead = goal - (leg.Foot.Velocity - ground) * p.SwingAheadHorizon;
                lift = BalanceMath.Clamp(
                    Float3.Dot(up, ahead) * p.SwingLiftGain + p.SwingLiftBase,
                    p.SwingLiftMin,
                    p.SwingLiftMax);
                var mix = lift > open ? (lift + open) * 0.5f : lift;
                var direction = BalanceMath.NormalizeOrZero(distance, 1e-6f);
                goal = footPos + direction * (shaped * (1f - mix));
                blend = 1f / (p.SwingBlendBase - mix);
                if (!Scalar.IsFinite(blend))
                    blend = 0f;
            }
            var limbC = BalanceMath.AxisZ(leg.Limb);
            var limbD = leg.Limb.Position;
            var childLocal = BalanceMath.ToLocal(leg.Knee.ChildFrame, leg.Ankle.Frame.Position - leg.Knee.ChildFrame.Position);
            var kneeVector = BalanceMath.ToWorld(leg.Limb, childLocal);
            var thighLength = leg.ThighLength;
            if (!(thighLength > 1e-6f))
                thighLength = 0.45f;
            var shinLength = kneeVector.Length;
            if (!Scalar.IsFinite(shinLength) || !(shinLength > 1e-6f))
                shinLength = 0.45f;
            var spanProduct = shinLength * thighLength;
            var off = 0f;
            if (spanProduct > 1e-9f)
            {
                var sine = Float3.Dot(limbC, Float3.Cross(kneeVector, limbD)) / spanProduct;
                off = BalanceMath.Asin(BalanceMath.Clamp(sine, -1f, 1f));
            }
            var relative = BalanceMath.ToLocal(leg.Hip.Frame, goal - leg.Hip.Position);
            var relativeLength = relative.Length;
            if (!Scalar.IsFinite(relativeLength))
            {
                relative = Float3.Zero;
                relativeLength = 0f;
            }
            var total = shinLength + thighLength;
            if (relativeLength > total && relativeLength > 1e-9f)
                relative = relative * (total / relativeLength);
            var cosine = 0f;
            var denominator = 2f * shinLength * thighLength;
            if (denominator > 1e-9f)
                cosine = (shinLength * shinLength + thighLength * thighLength - Float3.Dot(relative, relative)) / denominator;
            var bend = (Scalar.Pi - BalanceMath.Acos(BalanceMath.Clamp(cosine, -1f, 1f))) * -1f + off;
            if (!Scalar.IsFinite(bend))
                bend = 0f;
            var kneePoint = Quaternion.Rotate(BalanceMath.RotVecToQuat(limbC * bend), kneeVector) + limbD;
            var aim = yaw * side;
            if (!Scalar.IsFinite(aim))
                aim = 0f;
            var axis = BalanceMath.NormalizeOrZero(kneePoint, 1e-6f);
            var decomposed = BalanceMath.QMul(
                BalanceMath.Arc(kneePoint, relative, p.Epsilon),
                BalanceMath.RotVecToQuat(axis * aim));
            BalanceMath.SplitRotation(decomposed, out var lean1, out var lean2, out var twist);
            var halfTwist = leg.Hip.HalfTwist > 1e-6f ? leg.Hip.HalfTwist : 0.6f;
            if (twist > halfTwist || twist < -halfTwist)
            {
                var excess = twist - BalanceMath.Clamp(twist, -halfTwist, halfTwist);
                var narrowed = BalanceMath.QMul(
                    BalanceMath.Arc(kneePoint, relative, p.Epsilon),
                    BalanceMath.RotVecToQuat(axis * (aim - excess)));
                BalanceMath.SplitRotation(narrowed, out lean1, out lean2, out _);
            }
            var half1 = leg.Hip.HalfLean1 > 1e-6f ? leg.Hip.HalfLean1 : 0.7f;
            var half2 = leg.Hip.HalfLean2 > 1e-6f ? leg.Hip.HalfLean2 : 0.7f;
            var ellipse = (lean2 * lean2) / (half2 * half2) + (lean1 * lean1) / (half1 * half1);
            if (ellipse > 1f && Scalar.IsFinite(ellipse))
            {
                var pull = (1f - 1f / (float)Math.Sqrt(ellipse)) * (float)Math.Sqrt(lean1 * lean1 + lean2 * lean2) * -1f;
                bend += BalanceMath.Clamp(pull, -0.5f, 0.5f);
            }
            var knee = leg.Knee;
            knee.Desired = bend;
            var hip = leg.Hip;
            hip.Desired = new Float3(
                hip.MidLean1 + lean1,
                side * lean2 + hip.MidLean2,
                side * twist + hip.MidTwist);
            if (swing)
            {
                var hold = 1f - blend;
                hip.Desired = new Float3(
                    hip.Actual.X * hold + blend * hip.Desired.X,
                    hip.Actual.Y * hold + blend * hip.Desired.Y,
                    hip.Actual.Z * hold + blend * hip.Desired.Z);
                knee.Desired = knee.Actual * hold + knee.Desired * blend;
            }
            if (!hip.Desired.IsFinite)
                hip.Desired = new Float3(hip.MidLean1, hip.MidLean2, hip.MidTwist);
            if (!Scalar.IsFinite(knee.Desired))
                knee.Desired = knee.Mid;
            leg.Hip = hip;
            leg.Knee = knee;
            if (left)
                s.Left = leg;
            else
                s.Right = leg;
        }

        static Float3 LeadTarget(
            Float3 target,
            float lead,
            float strength,
            float damping,
            float minDistance,
            in BalanceJoint3 hip,
            in BalancePart pelvis,
            Float3 velocity)
        {
            var clean = BalanceMath.Sanitize(target);
            var range = clean - hip.Position;
            var ratio = 0f;
            if (strength > 1e-6f && Scalar.IsFinite(strength) && Scalar.IsFinite(damping) && damping >= 0f)
                ratio = damping / strength;
            var gain = ratio * lead;
            var floor = Scalar.IsFinite(minDistance) && minDistance > 0f ? minDistance : 0.07f;
            var swirl = Float3.Cross(range, BalanceMath.Sanitize(pelvis.AngularVelocity)) * gain;
            var turned = range + swirl;
            var before = range.Length;
            var after = turned.Length;
            if (!Scalar.IsFinite(before))
                before = 0f;
            if (!Scalar.IsFinite(after))
                after = 0f;
            var correction = BalanceMath.NormalizeOrZero(turned, 1e-6f) * (before - after) + swirl;
            var stepped = clean + correction - (BalanceMath.Sanitize(pelvis.Velocity) - BalanceMath.Sanitize(velocity)) * gain;
            var direction = BalanceMath.NormalizeOrZero(range, 1e-6f);
            var along = Float3.Dot(direction, stepped - hip.Position);
            if (Scalar.IsFinite(along) && along < floor)
                stepped = direction * (floor - along) + stepped;
            return stepped;
        }

        static void SolveAnkle(
            in BalanceParams p,
            Float3 up,
            Float3 target,
            Float3 normal,
            float pitch,
            float yaw,
            bool planted,
            float strength,
            float damping,
            ref BalanceSnapshot s,
            bool left)
        {
            var leg = left ? s.Left : s.Right;
            var ankle = leg.Ankle;
            var cleanNormal = BalanceMath.Sanitize(normal);
            if (!(cleanNormal.Length > 1e-6f))
                cleanNormal = up;
            else
                cleanNormal = cleanNormal / cleanNormal.Length;
            float flex;
            if (!planted)
            {
                var shin = leg.Shin;
                var shinSide = BalanceMath.AxisX(shin.Frame);
                var shinForward = BalanceMath.AxisZ(shin.Frame);
                var ratio = 0f;
                if (strength > 1e-6f && Scalar.IsFinite(strength) && Scalar.IsFinite(damping) && damping >= 0f)
                    ratio = damping / strength;
                var align = -Float3.Dot(BalanceMath.Sanitize(shin.AngularVelocity), shinSide) * ratio
                    + Float3.Dot(shinForward, cleanNormal);
                var footSide = BalanceMath.AxisX(leg.Foot.Frame);
                var toe = Float3.Dot(cleanNormal, BalanceMath.NormalizeOrZero(Float3.Cross(footSide, up), 1e-6f)) * p.ToeAlignGain
                    + align * p.ShinAlignGain;
                var forward = BalanceMath.AxisY(s.Root);
                if (Float3.Dot(BalanceMath.Sanitize(leg.Foot.Velocity), forward) < 0f)
                    toe -= p.AnkleBackstepBias;
                flex = toe + p.AnkleSwingOffset;
            }
            else
            {
                var kneePoint = PredictKnee(leg, left ? 1f : -1f);
                var rise = BalanceMath.NormalizeOrZero(kneePoint - BalanceMath.Sanitize(target), 1e-6f);
                var flat = BalanceMath.NormalizeOrZero(Float3.Cross(rise, cleanNormal), 1e-6f);
                var kneeAxis = BalanceMath.AxisZ(leg.Knee.Frame);
                flex = BalanceMath.Asin(BalanceMath.Clamp(Float3.Dot(kneeAxis, flat), -1f, 1f));
            }
            if (!Scalar.IsFinite(flex))
                flex = 0f;
            if (!Scalar.IsFinite(pitch))
                pitch = 0f;
            if (!Scalar.IsFinite(yaw))
                yaw = 0f;
            ankle.Desired = new Float3(planted ? flex + pitch : flex, yaw, ankle.Desired.Z);
            if (!ankle.Desired.IsFinite)
                ankle.Desired = Float3.Zero;
            leg.Ankle = ankle;
            if (left)
                s.Left = leg;
            else
                s.Right = leg;
        }

        static Float3 PredictKnee(in BalanceLeg leg, float side)
        {
            var clean = 1f;
            if (Scalar.IsFinite(side) && side < 0f)
                clean = -1f;
            var twist = (Scalar.IsFinite(leg.Hip.Desired.Z) ? leg.Hip.Desired.Z : leg.Hip.MidTwist) - leg.Hip.MidTwist;
            var lean1 = (Scalar.IsFinite(leg.Hip.Desired.X) ? leg.Hip.Desired.X : leg.Hip.MidLean1) - leg.Hip.MidLean1;
            var lean2 = (Scalar.IsFinite(leg.Hip.Desired.Y) ? leg.Hip.Desired.Y : leg.Hip.MidLean2) - leg.Hip.MidLean2;
            var relative = new Float3(twist * clean, lean1, lean2 * clean);
            var length = leg.ThighLength;
            if (!(length > 1e-6f))
                length = 0.45f;
            var local = Quaternion.Rotate(
                BalanceMath.JointRotation(relative.Y, relative.Z, relative.X),
                new Float3(0f, 0f, length));
            return Frames.TransformPoint(leg.Hip.Frame, local);
        }

        static float OverTravel(in BalanceJoint3 hip)
        {
            var half1 = hip.HalfLean1;
            var half2 = hip.HalfLean2;
            if (!(half1 > 1e-6f) || !(half2 > 1e-6f))
                return 0f;
            var lean1 = (Scalar.IsFinite(hip.Desired.X) ? hip.Desired.X : hip.MidLean1) - hip.MidLean1;
            var lean2 = (Scalar.IsFinite(hip.Desired.Y) ? hip.Desired.Y : hip.MidLean2) - hip.MidLean2;
            var scaled1 = lean1 / half1;
            var scaled2 = lean2 / half2;
            var radius = (float)Math.Sqrt(scaled1 * scaled1 + scaled2 * scaled2);
            if (!(radius > 1f) || !Scalar.IsFinite(radius))
                return 0f;
            return (radius - 1f) / (radius + 1e-6f) * lean2;
        }

        static void RedistributeOverTravel(ref BalanceSnapshot s)
        {
            var overLeft = OverTravel(s.Left.Hip);
            var overRight = OverTravel(s.Right.Hip);
            if (!Scalar.IsFinite(overLeft))
                overLeft = 0f;
            if (!Scalar.IsFinite(overRight))
                overRight = 0f;
            var left = s.Left;
            var right = s.Right;
            var spine = s.Spine;
            var pushLeft = overLeft > 0f && overRight <= 0f ? overLeft : 0f;
            var pushRight = overRight > 0f && overLeft <= 0f ? overRight : 0f;
            if (pushLeft > 0f)
            {
                var hip = right.Hip;
                hip.Desired = new Float3(hip.Desired.X, hip.Desired.Y + pushLeft, hip.Desired.Z);
                right.Hip = hip;
                if (s.SupportLeft.Contact)
                {
                    var stance = left.Hip;
                    stance.Desired = new Float3(stance.Desired.X, stance.Desired.Y - pushLeft, stance.Desired.Z);
                    left.Hip = stance;
                }
                spine.Desired = new Float3(spine.Desired.X, spine.Desired.Y - pushLeft, spine.Desired.Z);
            }
            if (pushRight > 0f)
            {
                var hip = left.Hip;
                hip.Desired = new Float3(hip.Desired.X, hip.Desired.Y + pushRight, hip.Desired.Z);
                left.Hip = hip;
                if (s.SupportRight.Contact)
                {
                    var stance = right.Hip;
                    stance.Desired = new Float3(stance.Desired.X, stance.Desired.Y - pushRight, stance.Desired.Z);
                    right.Hip = stance;
                }
                spine.Desired = new Float3(spine.Desired.X, spine.Desired.Y - pushRight, spine.Desired.Z);
            }
            s.Left = left;
            s.Right = right;
            s.Spine = spine;
        }

        static void WriteDrives(in BalanceParams p, in BalanceSnapshot s, in BalanceFeet f, Float3 up, Span<JointDrive> drives)
        {
            var ceiling = Scalar.IsFinite(p.MaxTorque) && p.MaxTorque > 0f ? p.MaxTorque : 0f;
            WriteLegDrives(p, s.Left, 1f, Role.ThighLeft, Role.ShinLeft, Role.FootLeft, ceiling, f.LeftPlanted, up, drives);
            WriteLegDrives(p, s.Right, -1f, Role.ThighRight, Role.ShinRight, Role.FootRight, ceiling, f.RightPlanted, up, drives);
            var spineLean = Scalar.IsFinite(s.Spine.Desired.Y) ? s.Spine.Desired.Y : 0f;
            var spineTarget = Quaternion.Multiply(
                BalanceMath.SanitizeRotation(s.Spine.Frame.Rotation),
                Quaternion.AxisAngle(new Float3(0f, 0f, 1f), spineLean));
            drives[(int)Role.Torso] = MakeDrive(spineTarget, p.SpineStrength, p.SpineDamping, ceiling);
        }

        static Quaternion FlatAnchor(Float3 up, Frame foot)
        {
            var rotation = BalanceMath.SanitizeRotation(foot.Rotation);
            var axis = BalanceMath.Sanitize(up);
            if (!(axis.Length > 1e-6f))
                return rotation;
            var sole = Quaternion.Rotate(rotation, new Float3(0f, 0f, 1f));
            var anchor = BalanceMath.Arc(sole, axis * -1f, 1e-6f);
            var flat = Quaternion.Normalize(Quaternion.Multiply(anchor, rotation));
            if (!flat.IsValidRotation)
                return rotation;
            return flat;
        }

        static void WriteLegDrives(
            in BalanceParams p,
            in BalanceLeg leg,
            float side,
            Role hipRole,
            Role kneeRole,
            Role ankleRole,
            float ceiling,
            bool planted,
            Float3 up,
            Span<JointDrive> drives)
        {
            var clean = side >= 0f ? 1f : -1f;
            var desiredTwist = Scalar.IsFinite(leg.Hip.Desired.Z) ? leg.Hip.Desired.Z : 0f;
            var desiredLean1 = Scalar.IsFinite(leg.Hip.Desired.X) ? leg.Hip.Desired.X : 0f;
            var desiredLean2 = Scalar.IsFinite(leg.Hip.Desired.Y) ? leg.Hip.Desired.Y : 0f;
            var actualTwist = Scalar.IsFinite(leg.Hip.Actual.Z) ? leg.Hip.Actual.Z : 0f;
            var desiredHip = BalanceMath.JointRotation(desiredLean1, desiredLean2 * clean, desiredTwist * clean);
            var actualHip = BalanceMath.JointRotation(0f, 0f, actualTwist * clean);
            var hipCorrection = Quaternion.Multiply(desiredHip, Quaternion.InverseUnit(actualHip));
            var hipTarget = Quaternion.Multiply(
                BalanceMath.SanitizeRotation(leg.Hip.Frame.Rotation),
                Quaternion.Multiply(hipCorrection, Frames.Relative(leg.Hip.Frame, leg.Thigh.Frame).Rotation));
            drives[(int)hipRole] = MakeDrive(hipTarget, p.HipStrength, p.HipDamping, ceiling);
            var desiredKnee = Scalar.IsFinite(leg.Knee.Desired) ? leg.Knee.Desired : 0f;
            var actualKnee = Scalar.IsFinite(leg.Knee.Actual) ? leg.Knee.Actual : 0f;
            var kneeTarget = Quaternion.Multiply(
                BalanceMath.SanitizeRotation(leg.Knee.Frame.Rotation),
                Quaternion.Multiply(
                    Quaternion.AxisAngle(new Float3(1f, 0f, 0f), actualKnee - desiredKnee),
                    Frames.Relative(leg.Knee.Frame, leg.Shin.Frame).Rotation));
            drives[(int)kneeRole] = MakeDrive(kneeTarget, p.KneeStrength, p.KneeDamping, ceiling);
            var desiredPitch = Scalar.IsFinite(leg.Ankle.Desired.X) ? leg.Ankle.Desired.X : 0f;
            var desiredYaw = Scalar.IsFinite(leg.Ankle.Desired.Y) ? leg.Ankle.Desired.Y : 0f;
            var desiredAnkle = Quaternion.Multiply(
                Quaternion.AxisAngle(new Float3(1f, 0f, 0f), desiredPitch),
                Quaternion.AxisAngle(new Float3(0f, 1f, 0f), desiredYaw));
            var anchor = planted ? FlatAnchor(up, leg.Ankle.Frame) : BalanceMath.SanitizeRotation(leg.Ankle.Frame.Rotation);
            var ankleTarget = Quaternion.Multiply(
                anchor,
                Quaternion.Multiply(desiredAnkle, Frames.Relative(leg.Ankle.Frame, leg.Foot.Frame).Rotation));
            drives[(int)ankleRole] = MakeDrive(ankleTarget, p.AnkleStrength, p.AnkleDamping, ceiling);
        }

        static JointDrive MakeDrive(Quaternion target, float stiffness, float damping, float ceiling)
        {
            if (!target.IsValidRotation)
                return JointDrive.Passive;
            if (!Scalar.IsFinite(stiffness) || stiffness < 0f)
                stiffness = 0f;
            if (!Scalar.IsFinite(damping) || damping < 0f)
                damping = 0f;
            if (!(ceiling > 0f))
                return JointDrive.Passive;
            if (stiffness <= 0f && damping <= 0f)
                return JointDrive.Passive;
            return new JointDrive
            {
                Target = Quaternion.Normalize(target),
                TargetAngularVelocity = Float3.Zero,
                Stiffness = stiffness,
                Damping = damping,
                MaxTorque = ceiling
            };
        }
    }
}

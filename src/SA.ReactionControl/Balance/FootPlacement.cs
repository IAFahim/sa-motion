using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Balance
{
    public static class FootPlacement
    {
        public static Frame Reach(Float3 target, Float3 up, Frame pelvis, float first, float second, in BalanceParams p)
        {
            var clean = BalanceMath.Sanitize(target);
            var axis = BalanceMath.Sanitize(up);
            if (!(axis.Length > 1e-6f))
                axis = new Float3(0f, 1f, 0f);
            else
                axis = axis / axis.Length;
            var pelvisForward = BalanceMath.AxisZ(pelvis);
            var along = Float3.Dot(axis, pelvisForward);
            var forward = BalanceMath.NormalizeOrZero(pelvisForward - axis * along, 1e-6f);
            var side = Float3.Cross(forward, axis);
            var pelvisSide = BalanceMath.AxisX(pelvis);
            var pelvisPosition = BalanceMath.Sanitize(pelvis.Position);
            var bias = Scalar.IsFinite(p.ReachBias) ? p.ReachBias : 0f;
            var centre = pelvisPosition
                + side * Float3.Dot(axis, pelvisSide) * bias
                + forward * along * bias;
            centre = BalanceMath.SetHeight(centre, axis, Float3.Dot(clean, axis));
            var relative = clean - centre;
            var epsilon = Scalar.IsFinite(p.Epsilon) && p.Epsilon > 0f ? p.Epsilon : 1e-6f;
            var forwardScale = first * p.ReachForwardScale;
            var lateralScale = second * p.ReachLateralScale;
            float scale;
            if (!(forwardScale > 1e-6f) || !(lateralScale > 1e-6f))
            {
                scale = 0f;
            }
            else
            {
                var x = Float3.Dot(relative, forward) / forwardScale;
                var y = Float3.Dot(relative, side) / lateralScale;
                if (!Scalar.IsFinite(x) || !Scalar.IsFinite(y))
                {
                    scale = 0f;
                }
                else
                {
                    var radius = (float)System.Math.Sqrt(x * x + y * y);
                    scale = 1f / (radius + epsilon);
                    if (scale > 1f || !Scalar.IsFinite(scale))
                        scale = 1f;
                }
            }
            var flatForward = Float3.Dot(relative, forward);
            var flatSide = Float3.Dot(relative, side);
            if (!Scalar.IsFinite(flatForward))
                flatForward = 0f;
            if (!Scalar.IsFinite(flatSide))
                flatSide = 0f;
            return new Frame(
                centre + forward * (flatForward * scale) + side * (flatSide * scale),
                pelvis.Rotation);
        }

        public static void HipLeans(
            in BalanceJoint3 hip,
            Float3 target,
            float side,
            out float lean1,
            out float lean2)
        {
            var clean = BalanceMath.Sanitize(target);
            var origin = BalanceMath.Sanitize(hip.Position);
            var mid1 = Scalar.IsFinite(hip.MidLean1) ? hip.MidLean1 : 0f;
            var mid2 = Scalar.IsFinite(hip.MidLean2) ? hip.MidLean2 : 0f;
            var direction = BalanceMath.ToLocal(hip.Frame, clean - origin);
            var unit = BalanceMath.NormalizeOrZero(direction, 1e-6f);
            var tilt = BalanceMath.Acos(BalanceMath.Clamp(unit.Z, -1f, 1f));
            var radius = (float)System.Math.Sqrt(unit.X * unit.X + unit.Y * unit.Y);
            var gain = 1f / (radius + 1e-6f);
            if (!Scalar.IsFinite(side))
                side = 1f;
            lean1 = unit.X * gain * tilt + mid1;
            lean2 = unit.Y * gain * tilt * side + mid2;
            if (!Scalar.IsFinite(lean1))
                lean1 = mid1;
            if (!Scalar.IsFinite(lean2))
                lean2 = mid2;
        }

        public static float HipRoom(in BalanceJoint3 hip, float used1, float used2, float start1, float start2, float epsilon)
        {
            if (!Scalar.IsFinite(epsilon) || !(epsilon > 0f))
                epsilon = 1e-6f;
            var span1 = hip.HalfLean1;
            var span2 = hip.HalfLean2;
            if (!(span1 > epsilon) || !(span2 > epsilon))
                return 0f;
            if (!Scalar.IsFinite(used1))
                used1 = hip.MidLean1;
            if (!Scalar.IsFinite(used2))
                used2 = hip.MidLean2;
            if (!Scalar.IsFinite(start1))
                start1 = hip.MidLean1;
            if (!Scalar.IsFinite(start2))
                start2 = hip.MidLean2;
            var inverse1 = 1f / span1;
            var actual1 = Scalar.IsFinite(hip.Actual.X) ? hip.Actual.X : hip.MidLean1;
            var actual2 = Scalar.IsFinite(hip.Actual.Y) ? hip.Actual.Y : hip.MidLean2;
            var inverse2 = 1f / span2;
            var c1 = ((actual1 + start1) * 0.5f - hip.MidLean1) * inverse1;
            var c2 = ((actual2 + start2) * 0.5f - hip.MidLean2) * inverse2;
            var d1 = (used1 - hip.MidLean1) * inverse1 - (start1 - hip.MidLean1) * inverse1;
            var d2 = (used2 - hip.MidLean2) * inverse2 - (start2 - hip.MidLean2) * inverse2;
            var numerator = d2 * c2 + d1 * c1;
            var denominator = numerator - (d2 + c2) * d2 - (d1 + c1) * d1;
            float t;
            if (!(denominator > epsilon) && !(denominator < -epsilon))
                t = 0f;
            else
                t = numerator / denominator;
            if (!Scalar.IsFinite(t))
                t = 0f;
            var p1 = t * d1 + c1;
            var p2 = t * d2 + c2;
            var axisLength = (float)System.Math.Sqrt(d1 * d1 + d2 * d2);
            var axisGain = 1f / (axisLength + epsilon);
            var u1 = axisGain * d1;
            var u2 = axisGain * d2;
            var off = System.Math.Abs(p1 * u2 - p2 * u1);
            float e1;
            float e2;
            if (off >= 1.0)
            {
                e1 = actual1;
                e2 = actual2;
            }
            else
            {
                var height = (float)System.Math.Sqrt(1.0 - off * off);
                e1 = (height * u1 + p1) * span1 + hip.MidLean1;
                e2 = (height * u2 + p2) * span2 + hip.MidLean2;
            }
            var g1 = used1 - start1;
            var g2 = used2 - start2;
            var goalLength = (float)System.Math.Sqrt(g1 * g1 + g2 * g2);
            var goalGain = 1f / (goalLength + epsilon);
            var room = goalGain * g2 * (e2 - actual2) + goalGain * g1 * (e1 - actual1);
            if (!Scalar.IsFinite(room))
                return 0f;
            return BalanceMath.Clamp(room, -1f, 1f);
        }

        public static void Decide(in BalanceSnapshot s, ref BalanceFeet f, int maxStepDelay)
        {
            if (f.SteppingLeg != 0)
                return;
            var leftContact = s.SupportLeft.Contact;
            var rightContact = s.SupportRight.Contact;
            var free = !f.InsideSupport;
            var leftFarther = f.DistLeft > f.DistRight && free;
            var rightFarther = f.DistRight > f.DistLeft && free;
            bool wantLeft;
            bool wantRight;
            if (rightContact)
                wantLeft = leftFarther || !leftContact;
            else
                wantLeft = !leftContact && rightFarther;
            if (leftContact)
                wantRight = rightFarther || !rightContact;
            else
                wantRight = !rightContact && leftFarther;
            if (wantLeft || wantRight)
            {
                if (f.Countdown >= 0)
                    f.Countdown--;
                if (f.Countdown <= 0)
                {
                    wantLeft = false;
                    wantRight = false;
                }
            }
            if (wantLeft)
            {
                f.SteppingLeg = 1;
                f.WasAlone = rightContact && !leftContact;
                f.WasFar = f.DistLeft < f.DistRight;
                f.Start = f.FootLeft;
                f.Countdown = maxStepDelay;
                return;
            }
            if (wantRight)
            {
                f.SteppingLeg = 2;
                f.WasAlone = leftContact && !rightContact;
                f.WasFar = f.DistRight < f.DistLeft;
                f.Start = f.FootRight;
                f.Countdown = maxStepDelay;
            }
        }

        public static void Update(in BalanceParams p, in BalanceSnapshot s, ref BalanceFeet f)
        {
            var up = BalanceMath.Sanitize(p.Up);
            if (!(up.Length > 1e-6f))
                up = new Float3(0f, 1f, 0f);
            else
                up = up / up.Length;
            f.HeightLeft = Float3.Dot(BalanceMath.Sanitize(s.SupportLeft.Target), up);
            f.HeightRight = Float3.Dot(BalanceMath.Sanitize(s.SupportRight.Target), up);
            var left = BalanceMath.SetHeight(s.Left.Foot.Frame.Position, up, f.HeightLeft);
            var right = BalanceMath.SetHeight(s.Right.Foot.Frame.Position, up, f.HeightRight);
            if (!s.SupportLeft.Contact || !s.SupportRight.Contact)
                BalanceMath.Separate(ref left, ref right, p.FootWidth);
            f.FootLeft = left;
            f.FootRight = right;
            f.Centre = (left + right) * 0.5f;
            var predicted = BalanceMath.SetHeight(
                s.Com + s.HorizontalVelocity * p.StepLead,
                up,
                Float3.Dot(f.Centre, up));
            var inside = Support.Measure(
                predicted,
                s.Left.Foot.Frame,
                s.Right.Foot.Frame,
                p.FootSupportHalfWidth,
                p.FootSupportHalfLength,
                up,
                out _);
            f.InsideSupport = inside > p.SupportInsideMargin;
            var stance = BalanceMath.Horizontal(right - left, up).Length;
            if (f.InsideSupport && stance > p.FootWidth + p.WideStanceRelease)
                f.InsideSupport = false;
            var rootForward = BalanceMath.AxisY(s.Root);
            var rootSide = BalanceMath.AxisX(s.Root);
            var toRight = BalanceMath.NormalizeOrZero(BalanceMath.Horizontal(predicted - right, up), 1e-6f);
            var awayRight = rootForward * Float3.Dot(rootForward, toRight) * Float3.Dot(rootSide, toRight);
            var targetLeft = predicted - rootSide * p.StepOffset - awayRight * p.StepOffset;
            var toLeft = BalanceMath.NormalizeOrZero(BalanceMath.Horizontal(predicted - left, up), 1e-6f);
            var awayLeft = rootForward * Float3.Dot(rootForward, toLeft) * Float3.Dot(rootSide, toLeft);
            var targetRight = predicted + rootSide * p.StepOffset + awayLeft * p.StepOffset;
            f.DistLeft = BalanceMath.Horizontal(targetLeft - left, up).Length;
            f.DistRight = BalanceMath.Horizontal(targetRight - right, up).Length;
            if (!Scalar.IsFinite(f.DistLeft))
                f.DistLeft = 0f;
            if (!Scalar.IsFinite(f.DistRight))
                f.DistRight = 0f;
            Decide(s, ref f, p.MaxStepDelay);
            if (f.SteppingLeg == 0)
            {
                f.LeftPlanted = s.SupportLeft.Contact;
                f.RightPlanted = s.SupportRight.Contact;
                return;
            }
            var steppingRight = f.SteppingLeg == 2;
            var side = steppingRight ? -1f : 1f;
            var target = steppingRight ? targetRight : targetLeft;
            var contact = steppingRight ? s.SupportRight.Contact : s.SupportLeft.Contact;
            var stanceFoot = steppingRight ? f.FootLeft : f.FootRight;
            var leg = steppingRight ? s.Right : s.Left;
            var footPos = steppingRight ? f.FootRight : f.FootLeft;
            f.NearFootStep = steppingRight ? f.DistLeft > f.DistRight : f.DistLeft < f.DistRight;
            f.NoContactStep = !contact;
            f.Centre = s.Com + s.HorizontalVelocity * p.CentrePreviewTime;
            var reachFrame = Reach(target, up, s.Pelvis.Frame, p.StepClamp * leg.Hip.HalfLean1, p.StepClamp * leg.Hip.HalfLean2, p);
            target = reachFrame.Position;
            var footWorld = leg.Foot.Frame.Position;
            var travel = BalanceMath.NormalizeOrZero(BalanceMath.Horizontal(target - f.Start, up), 1e-6f);
            var ahead = Float3.Dot(target, travel) - Float3.Dot(footWorld, travel);
            var closing = Float3.Dot(s.Velocity, travel) - Float3.Dot(leg.Foot.Velocity, travel);
            var toTarget = BalanceMath.Horizontal(target - footWorld, up).Length;
            var fromStart = BalanceMath.Horizontal(f.Start - footWorld, up).Length;
            var span = fromStart + p.Epsilon;
            var spanFraction = (!(span > 1e-9f) || !Scalar.IsFinite(toTarget)) ? 0f : toTarget / span;
            var sole = footWorld - BalanceMath.AxisY(leg.Foot.Frame) * p.SoleOffset * 0.5f;
            var passed = BalanceMath.Horizontal(sole - f.PrevTarget, up).Length
                > BalanceMath.Horizontal(f.PrevSole - target, up).Length;
            var closingTerm = closing < 0f ? closing : 0f;
            var lift = (closingTerm * p.LiftGain + ahead) * 0.5f;
            lift = BalanceMath.Clamp(lift, -p.StepHeight, p.StepHeight);
            var speed = 0f;
            if (Scalar.IsFinite(p.DeltaTime) && p.DeltaTime > 1e-6f)
            {
                var before = BalanceMath.Horizontal(f.PrevSole - f.PrevTarget, up).Length;
                var after = BalanceMath.Horizontal(sole - target, up).Length;
                if (Scalar.IsFinite(before) && Scalar.IsFinite(after))
                    speed = (before - after) / p.DeltaTime;
            }
            f.PrevTarget = target;
            f.PrevSole = sole;
            var lifted = BalanceMath.SetHeight(target, up, Float3.Dot(footPos, up));
            HipLeans(leg.Hip, lifted, side, out var use1, out var use2);
            HipLeans(leg.Hip, f.Start, side, out var from1, out var from2);
            var normal = steppingRight ? s.SupportRight.Normal : s.SupportLeft.Normal;
            var spin = Float3.Cross(leg.Foot.AngularVelocity, BalanceMath.AxisY(leg.Foot.Frame)) * p.ToeSpinFactor
                + BalanceMath.AxisY(leg.Foot.Frame);
            var tilt = p.ReachForwardScale / (Float3.Dot(normal, up) + 0.5f);
            if (!Scalar.IsFinite(tilt))
                tilt = 1f;
            var toe = Float3.Dot(normal, spin);
            var room = HipRoom(leg.Hip, use1, use2, from1, from2, p.Epsilon);
            var liftHeight = BalanceMath.Clamp(toe < 0f ? -toe : toe, 0f, 1f) * p.LiftGain + lift;
            if (liftHeight > room)
                liftHeight = room;
            var ground = steppingRight ? s.SupportRight.Target : s.SupportLeft.Target;
            if (!f.NoContactStep)
            {
                liftHeight += p.AirborneLift;
                var pull = Float3.Dot(p.GravityUp, f.Start - ground);
                if (pull < 0f)
                    pull = 0f;
                if (!Scalar.IsFinite(pull))
                    pull = 0f;
                ground = ground + BalanceMath.Sanitize(p.GravityUp) * pull;
            }
            var liftWorld = ground.X * up.X + ground.Z * up.Z + tilt * (liftHeight + ground.Y * up.Y);
            if (!Scalar.IsFinite(liftWorld))
                liftWorld = Float3.Dot(footPos, up);
            if (toe < 0f || passed)
                speed = -1f;
            var placed = BalanceMath.SetHeight(lifted, up, liftWorld);
            var correction = BalanceMath.ClampLength(BalanceMath.Horizontal(placed - footPos, up), 0f, p.MaxStepCorrection);
            var settled = Reach(placed + correction, up, s.Pelvis.Frame, p.StepClamp * leg.Hip.HalfLean1, p.StepClamp * leg.Hip.HalfLean2, p);
            if (steppingRight)
                f.FootRight = settled.Position;
            else
                f.FootLeft = settled.Position;
            var middle = ((steppingRight ? f.FootRight : f.FootLeft) + stanceFoot) * 0.5f;
            f.Centre = f.Centre + up * (Float3.Dot(middle, up) - Float3.Dot(f.Centre, up));
            if (steppingRight)
            {
                f.LeftPlanted = s.SupportLeft.Contact;
                f.RightPlanted = false;
            }
            else
            {
                f.LeftPlanted = false;
                f.RightPlanted = s.SupportRight.Contact;
            }
            var otherContact = steppingRight ? s.SupportLeft.Contact : s.SupportRight.Contact;
            var landed = contact && speed < 0f && (spanFraction < 0.5f || (f.NearFootStep && f.NoContactStep));
            if (landed || (contact && !otherContact))
            {
                f.LeftPlanted = s.SupportLeft.Contact;
                f.RightPlanted = s.SupportRight.Contact;
                f.SteppingLeg = 0;
                Decide(s, ref f, p.MaxStepDelay);
            }
        }
    }
}

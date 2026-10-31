using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Balance
{
    public static class PelvisControl
    {
        public static float Yaw(float gain, Frame pelvis, Float3 velocity, bool toward, bool predict, Float3 angularVelocity, in BalanceParams p)
        {
            var clean = BalanceMath.Sanitize(velocity);
            var spin = BalanceMath.Sanitize(angularVelocity);
            var look = clean;
            if (predict)
                look = Quaternion.Rotate(BalanceMath.RotVecToQuat(spin * -p.YawPredictHorizon), clean);
            var back = BalanceMath.AxisZ(pelvis);
            var right = BalanceMath.AxisX(pelvis);
            var angle = BalanceMath.Atan2(-Float3.Dot(back, look), -Float3.Dot(right, look) * gain);
            var half = Scalar.Pi * 0.5f;
            if (toward)
            {
                if (angle >= 0f)
                    angle -= half;
                else
                    angle += half;
            }
            else
            {
                angle -= half;
                if (angle < -Scalar.Pi)
                    angle += Scalar.TwoPi;
                if (!(angle >= -p.YawValidRange && angle <= p.YawValidRange))
                    angle = 0f;
            }
            if (predict)
            {
                var speed = clean.Length;
                if (speed > 1f)
                    speed = 1f;
                if (!Scalar.IsFinite(speed))
                    speed = 0f;
                angle = speed * angle * p.YawPredictGain;
            }
            if (!Scalar.IsFinite(angle))
                return 0f;
            return angle;
        }

        public static void Update(
            in BalanceParams p,
            in BalanceSnapshot s,
            ref BalancePelvisState pv,
            bool leftPlanted,
            bool rightPlanted)
        {
            var up = BalanceMath.Sanitize(p.Up);
            if (!(up.Length > 1e-6f))
                up = new Float3(0f, 1f, 0f);
            else
                up = up / up.Length;
            pv.Offset = BalanceMath.Horizontal(s.Pelvis.Frame.Position - s.Com, up);
            var flow = pv.FixedVelocity ? BalanceMath.Sanitize(pv.BlendVelocity) : BalanceMath.Sanitize(s.HorizontalVelocity);
            var toward = !pv.FixedVelocity;
            var pelvis = s.Pelvis.Frame;
            if (leftPlanted && !rightPlanted)
            {
                pv.YawLeft = Blend(Yaw(1f, pelvis, flow, toward, false, s.AngularVelocity, p), pv.YawLeft, flow, p);
                pv.YawRight = pv.YawLeft * 0.5f;
            }
            else if (rightPlanted && !leftPlanted)
            {
                pv.YawRight = Blend(Yaw(-1f, pelvis, flow, toward, false, s.AngularVelocity, p), pv.YawRight, flow, p);
                pv.YawLeft = pv.YawRight * 0.5f;
            }
            else if (pv.FixedVelocity)
            {
                var yaw = Yaw(1f, pelvis, flow, toward, true, s.AngularVelocity, p);
                pv.YawLeft = BalanceMath.Clamp(yaw + (pv.YawLeft - pv.YawRight) * 0.5f, -p.YawLimit, p.YawLimit);
                pv.YawRight = -pv.YawLeft;
            }
            else
            {
                var step = Scalar.IsFinite(p.DeltaTime) && p.DeltaTime > 0f ? p.DeltaTime * 0.5f : 0.008f;
                var goalLeft = p.YawHalfSpan + p.YawCenter;
                var goalRight = -p.YawHalfSpan + p.YawCenter;
                pv.YawLeft = Approach(pv.YawLeft, goalLeft, step);
                pv.YawRight = Approach(pv.YawRight, goalRight, step);
            }
            var flatForward = BalanceMath.NormalizeOrZero(BalanceMath.Horizontal(BalanceMath.AxisZ(s.Root), up), 1e-6f);
            var flatSide = BalanceMath.NormalizeOrZero(BalanceMath.Horizontal(BalanceMath.AxisX(s.Root) * -1f, up), 1e-6f);
            var drift = BalanceMath.Sanitize(s.Com) - BalanceMath.Sanitize(pv.Centre);
            var pushForward = BalanceMath.Clamp(-Float3.Dot(drift, flatForward), -p.PelvisLeanForwardLimit, p.PelvisLeanBackLimit);
            var pushSide = -Float3.Dot(drift, flatSide);
            var slideForward = -Float3.Dot(BalanceMath.Sanitize(s.HorizontalVelocity), flatForward);
            var slideSide = -Float3.Dot(BalanceMath.Sanitize(s.HorizontalVelocity), flatSide);
            var preview = p.StepLead * p.PelvisLeadGain;
            if (leftPlanted && rightPlanted)
            {
                if (Scalar.IsFinite(p.DeltaTime) && Scalar.IsFinite(pushForward))
                    pv.ForwardLean = BalanceMath.Clamp(pv.ForwardLean + p.DeltaTime * pushForward, p.LeanBias - p.ForwardLeanRange, p.LeanBias + p.ForwardLeanRange);
                if (Scalar.IsFinite(p.DeltaTime) && Scalar.IsFinite(pushSide))
                    pv.LateralLean = BalanceMath.Clamp(pv.LateralLean + p.DeltaTime * pushSide, -p.LateralLeanRange, p.LateralLeanRange);
            }
            pv.HipShift = (pushSide * p.LateralRespond + (preview * 0.5f * slideSide + pv.LateralLean * p.LateralLeanGain)) * p.LateralScale;
            if (leftPlanted && rightPlanted)
            {
                pv.AnklePitch = (pushForward * p.ForwardRespond + slideForward * preview + pv.ForwardLean * p.ForwardLeanGain) * -p.ForwardScale;
            }
            else
            {
                pv.AnklePitch = 0f;
                var bleed = BalanceMath.Clamp(-p.LeanAccumulatorRate * slideForward, 0f, 0.5f);
                if (Scalar.IsFinite(bleed))
                    pv.LeanAccumulator -= bleed;
            }
            if (!Scalar.IsFinite(pv.HipShift))
                pv.HipShift = 0f;
            if (!Scalar.IsFinite(pv.AnklePitch))
                pv.AnklePitch = 0f;
            if (!Scalar.IsFinite(pv.LateralLean))
                pv.LateralLean = 0f;
            if (!Scalar.IsFinite(pv.ForwardLean))
                pv.ForwardLean = p.LeanBias;
            var toeSum = -Float3.Dot(s.SupportRight.Normal, BalanceMath.AxisZ(s.Right.Foot.Frame))
                + -Float3.Dot(s.SupportLeft.Normal, BalanceMath.AxisZ(s.Left.Foot.Frame));
            if (Scalar.IsFinite(toeSum) && toeSum > p.ToeUprightBand)
            {
                var upright = (toeSum - p.ToeUprightBand) * p.ToeUprightGain;
                if (upright > 1f)
                    upright = 1f;
                pv.AnklePitch += (0f - pv.AnklePitch) * upright;
            }
        }

        static float Blend(float yaw, float current, Float3 flow, in BalanceParams p)
        {
            var target = BalanceMath.Clamp(yaw, -p.YawLimit, p.YawLimit);
            if (!Scalar.IsFinite(current))
                current = 0f;
            var speed = flow.Length * 2f;
            if (speed > 1f)
                speed = 1f;
            if (!(speed >= 0f))
                speed = 0f;
            return (target - current) * speed * p.YawBlendRate + current;
        }

        static float Approach(float current, float goal, float step)
        {
            if (!Scalar.IsFinite(current))
                current = 0f;
            if (!Scalar.IsFinite(goal))
                return current;
            if (!(step > 0f))
                return current;
            if (current > goal)
            {
                current -= step;
                if (current < goal)
                    current = goal;
            }
            else if (current < goal)
            {
                current += step;
                if (current > goal)
                    current = goal;
            }
            return current;
        }
    }
}

using System;
using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.ReactionControl
{
    public static class BodyBalance
    {
        public static uint NextRng(ref uint state)
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            if (x == 0)
            {
                x = 0x9E3779B9u;
            }
            state = x;
            return x;
        }

        public static float Unit01(ref uint state)
        {
            return (NextRng(ref state) >> 8) * (1f / 16777216f);
        }

        public static void OwnFrame(Quaternion orientation, out Float3 x, out Float3 y, out Float3 z)
        {
            Quaternion q = orientation.IsValidRotation ? Quaternion.Normalize(orientation) : Quaternion.Identity;
            x = Quaternion.Rotate(q, new Float3(1f, 0f, 0f));
            y = Quaternion.Rotate(q, new Float3(0f, -1f, 0f));
            z = Quaternion.Rotate(q, new Float3(0f, 0f, 1f));
        }

        public static Float3 LeanAngles(Float3 fx, Float3 fy, Float3 fz)
        {
            if (!fx.IsFinite || !fy.IsFinite || !fz.IsFinite)
            {
                return Float3.Zero;
            }
            float yaw = AsinClamped(fz.Y);
            float guard = Abs((float)Math.Cos(yaw));
            if (guard < 1e-3f)
            {
                guard = 1e-3f;
            }
            float k = 1f / guard;
            return new Float3(AsinClamped(-fy.Y * k), yaw, AsinClamped(-fz.Z * k));
        }

        public static BalanceOutput Tick(in BodyBalanceParams p, ref BodyBalanceState s, ReadOnlySpan<RoleBody> bodies, ReadOnlySpan<SupportProbe> obstacles, in BalanceInput input, Span<JointDrive> drives)
        {
            BalanceOutput output = new BalanceOutput { StepBudget = p.MaxSteps };
            if (bodies.Length != Roles.Count)
            {
                PassiveFill(drives);
                output.Decision = BalanceDecision.Falling;
                return output;
            }
            if (InputsFinite(bodies, obstacles, in input) == 0)
            {
                PassiveFill(drives);
                output.Decision = BalanceDecision.Falling;
                return output;
            }
            if (drives.Length != Roles.Count)
            {
                output.Decision = BalanceDecision.Falling;
                return output;
            }
            float dt = input.DeltaTime;
            if (!Scalar.IsFinite(dt) || dt <= 0f)
            {
                dt = 1f / 60f;
            }
            if (dt > 0.25f)
            {
                dt = 0.25f;
            }
            Quaternion torsoQ = Quaternion.Normalize(bodies[(int)Role.Torso].Rotation);
            Quaternion pelvisQ = Quaternion.Normalize(bodies[(int)Role.Pelvis].Rotation);
            Quaternion headQ = Quaternion.Normalize(bodies[(int)Role.Head].Rotation);
            Float3 comPos = bodies[(int)Role.Pelvis].Position;
            Float3 comVel = bodies[(int)Role.Pelvis].LinearVelocity;
            Float3 headPos = bodies[(int)Role.Head].Position;
            Float3 chestPos = bodies[(int)Role.Torso].Position;
            float speed = comVel.Length;
            float f19 = Clamp(speed, p.SpeedFloor, 1f);
            if (input.Failed != 0)
            {
                if (input.Catching == 0)
                {
                    output.HasCatchFall = 1;
                    output.CatchFallGains = new Float3(p.BodyStiffness * p.CatchGainX, p.BodyStiffness * p.CatchGainY, p.BodyStiffness * p.CatchGainZ);
                    output.HasStabilise = 0;
                }
                else
                {
                    output.HasStabilise = 1;
                    output.StabiliseStiffness = 2f;
                    output.StabiliseDamping = p.StabiliseDamping;
                }
                s.Decision = BalanceDecision.Falling;
                s.HoldTimer = p.FallHoldTime;
                output.Decision = BalanceDecision.Falling;
                PassiveOwned(drives);
                return output;
            }
            if (input.Stepping != 0 && s.Stepping != input.Stepping)
            {
                s.Stepping = input.Stepping;
                s.Cut = Unit01(ref s.Rng) * p.CutSpan;
                if (1f > p.StepClampLo && 1f < p.StepClampHi)
                {
                    output.HasStepClamp = 1;
                    output.StepClamp = Unit01(ref s.Rng) * p.LeanRateThreshold + p.TurnToVelocityProb;
                }
            }
            Quaternion baseQ = p.UsePelvisFrame != 0 ? pelvisQ : torsoQ;
            OwnFrame(baseQ, out Float3 fx, out Float3 fy, out Float3 fz);
            Float3 now = LeanAngles(fx, fy, fz);
            float inv = 1f / dt;
            Float3 rate = new Float3(-((now.X - s.PrevEuler.X) * inv), -((now.Y - s.PrevEuler.Y) * inv), -((now.Z - s.PrevEuler.Z) * inv));
            s.PrevEuler = now;
            s.FrameX = fx;
            s.FrameY = fy;
            s.FrameZ = fz;
            float leanPitch = AsinClamped(-fz.Y);
            float leanRoll = AsinClamped(fx.Y);
            float tilt = Over(leanPitch, p.SomersaultThreshold, p.CutSpan);
            float sideTilt = Over(leanRoll, p.SomersaultThreshold, p.CutSpan);
            float f5 = rate.Y * leanPitch < 0f ? 0f : 1f;
            float f4 = rate.X * leanRoll < 0f ? 1f : 0f;
            float spinY = Over(rate.Y, p.LeanRateThreshold, p.RateCap);
            float spinZ = Over(rate.Z, p.LeanRateThresholdY, p.RateCap);
            float spinX = Over(rate.X, p.LeanRateThreshold, p.RateCap);
            float response = (-(1f * f4) * sideTilt) - ((1f * f5) * tilt);
            response += (-(spinY * p.RateGainXZ)) * f5;
            response += -spinZ;
            response += (-(spinX * p.RateGainXZ)) * f4;
            const float bendTime = 0.5f;
            if (s.ArmsOutTimer > 0f)
            {
                s.ArmsOutTimer -= dt;
            }
            if (s.BendTimer > 0f)
            {
                s.BendTimer -= dt;
            }
            if (input.ContactUpper != 0)
            {
                s.BendTimer = bendTime;
            }
            float twistSum = 0f;
            float twProxy = WrapPi(YawOf(torsoQ) - YawOf(pelvisQ));
            byte grounded = input.Grounded;
            float authority = 1f - Clamp(((1f - f19) * p.CutSpan) + p.TurnToTargetProb, 0f, 1f);
            float elbowK = p.BodyStiffness * p.ElbowResponseGain;
            float upperStiffness = Min(p.BodyStiffness * p.BodyStiffness, p.MaxDrive) * authority;
            float upperDamping = Min(2f * p.BodyStiffness * p.Damping, p.MaxDrive) * authority;
            float foreStiffness = Min(elbowK * elbowK, p.MaxDrive) * authority;
            float foreDamping = Min(p.Damping * p.CutSpan * elbowK * 2f, p.MaxDrive) * authority;
            for (int side = 0; side < 2; side++)
            {
                if (grounded == 0)
                {
                    continue;
                }
                int upper = side == 0 ? (int)Role.UpperArmLeft : (int)Role.UpperArmRight;
                int fore = side == 0 ? (int)Role.ForearmLeft : (int)Role.ForearmRight;
                float otherHip = (side == 0 ? -0.5f : 0.5f) * twProxy;
                float ownHip = -otherHip;
                float shoulderTwist = (-otherHip * p.LeanRateThreshold) + ((-otherHip * p.ShoulderHipRate) + (ownHip * p.TurnIntervalSpan));
                twistSum += shoulderTwist;
                float bendCut = 0f;
                float elbowCorr = -otherHip * p.ElbowGain;
                if (s.BendTimer > 0f)
                {
                    float fwd = Float3.Dot(fz, comVel);
                    float lat = Float3.Dot(fy, comVel);
                    byte toward = (side == 0 ? lat > 0f : lat < 0f) ? (byte)1 : (byte)0;
                    if ((fwd > 0f && toward != 0) || fwd > 0.5f)
                    {
                        elbowCorr = Max(elbowCorr, p.ElbowContactAngle);
                    }
                    else
                    {
                        bendCut = s.Cut;
                    }
                }
                elbowCorr = Clamp(elbowCorr, p.Damping, p.ElbowMax);
                float lateralBase = (leanRoll * p.TurnToVelocityProb) + 0.5f;
                float lateral = lateralBase;
                if (p.UseArmsOut != 0)
                {
                    float maxLateral = lateralBase - bendCut;
                    if (maxLateral < p.ArmsOutMinLean)
                    {
                        maxLateral = p.ArmsOutMinLean;
                    }
                    lateral = Clamp(lateralBase + response, p.ArmsOutMinLean, maxLateral);
                }
                Quaternion upperCurrent = Quaternion.Normalize(bodies[upper].Rotation);
                Quaternion foreCurrent = Quaternion.Normalize(bodies[fore].Rotation);
                Quaternion twistCorr = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), Clamp(shoulderTwist, -1f, 1f) * p.TwistCorrect * 0.5f);
                Quaternion lateralCorr = Quaternion.AxisAngle(fz, (lateral - 0.5f) - (p.TwistGainA * leanRoll));
                drives[upper] = new JointDrive
                {
                    Target = Quaternion.Normalize(Quaternion.Multiply(Quaternion.Multiply(twistCorr, lateralCorr), upperCurrent)),
                    TargetAngularVelocity = Float3.Zero,
                    Stiffness = upperStiffness,
                    Damping = upperDamping,
                    MaxTorque = 0f,
                };
                Quaternion elbowAxis = Quaternion.AxisAngle(new Float3(1f, 0f, 0f), elbowCorr);
                drives[fore] = new JointDrive
                {
                    Target = Quaternion.Normalize(Quaternion.Multiply(elbowAxis, foreCurrent)),
                    TargetAngularVelocity = Float3.Zero,
                    Stiffness = foreStiffness,
                    Damping = foreDamping,
                    MaxTorque = 0f,
                };
            }
            Float3 upT = Quaternion.Rotate(torsoQ, new Float3(0f, 1f, 0f));
            Float3 worldUp = new Float3(0f, 1f, 0f);
            Float3 tiltAxis = Float3.Cross(upT, worldUp);
            float sinA = tiltAxis.Length;
            float pitchCorr = (-leanPitch * p.UprightCorrect) + p.TorsoPitchBias;
            float yawCorr = Clamp(twistSum * 0.5f, -1f, 1f) * p.TwistCorrect;
            float rollCorr = -leanRoll * p.UprightCorrect;
            Quaternion upright = Quaternion.Identity;
            if (sinA > 1e-6f)
            {
                float cosA = Float3.Dot(upT, worldUp);
                if (cosA < -1f)
                {
                    cosA = -1f;
                }
                if (cosA > 1f)
                {
                    cosA = 1f;
                }
                upright = Quaternion.AxisAngle(tiltAxis / sinA, (float)Math.Atan2(sinA, cosA) * p.UprightCorrect);
            }
            Quaternion yawQ = Quaternion.AxisAngle(worldUp, yawCorr);
            Quaternion biasQ = Quaternion.AxisAngle(fx, p.TorsoPitchBias);
            drives[(int)Role.Torso] = new JointDrive
            {
                Target = Quaternion.Normalize(Quaternion.Multiply(Quaternion.Multiply(Quaternion.Multiply(upright, yawQ), biasQ), torsoQ)),
                TargetAngularVelocity = Float3.Zero,
                Stiffness = Min(p.SpineMuscle + (p.BodyStiffness * p.BodyStiffness), p.MaxDrive),
                Damping = Min(2f * p.BodyStiffness * p.Damping, p.MaxDrive),
                MaxTorque = 0f,
            };
            drives[(int)Role.Head] = new JointDrive
            {
                Target = headQ,
                TargetAngularVelocity = Float3.Zero,
                Stiffness = Min(p.BodyStiffness * p.BodyStiffness, p.MaxDrive),
                Damping = Min(2f * p.BodyStiffness, p.MaxDrive),
                MaxTorque = 0f,
            };
            drives[(int)Role.HandLeft] = JointDrive.Passive;
            drives[(int)Role.HandRight] = JointDrive.Passive;
            output.SpineLean = new Float3(pitchCorr, yawCorr, rollCorr);
            if (p.UseHeadLook != 0)
            {
                output.HeadLookStiffness = p.UpperStiffness + 1f;
                byte hasLook = 0;
                Float3 target = Float3.Zero;
                Float3 velocity = Float3.Zero;
                float best = p.LookProbeRange;
                for (int i = 0; i < obstacles.Length; i++)
                {
                    SupportProbe probe = obstacles[i];
                    if (probe.Hit == 0 || probe.Distance > best)
                    {
                        continue;
                    }
                    best = probe.Distance;
                    target = probe.Point;
                    velocity = probe.PointVelocity;
                    hasLook = 1;
                }
                if (hasLook == 0 && input.HasLookTarget != 0)
                {
                    target = input.LookTarget;
                    velocity = Float3.Zero;
                    hasLook = 1;
                }
                if (hasLook == 0 && input.HasPushSource != 0)
                {
                    target = input.PushSource;
                    velocity = Float3.Zero;
                    hasLook = 1;
                }
                if (input.Stepping != 0)
                {
                    s.LookTime -= dt;
                    if (s.LookTime < 0f)
                    {
                        s.LookTime = (Unit01(ref s.Rng) * p.TurnToTargetProb) + p.TurnToTargetProb;
                        if (s.LookVelProb < -p.SpeedFloor)
                        {
                            s.LookVelProb = s.LookRand;
                        }
                        s.LookVel = s.LookVelProb > Unit01(ref s.Rng) ? (byte)1 : (byte)0;
                    }
                    if (s.LookVel != 0 && speed > 1e-3f)
                    {
                        float lead = speed < 1f ? speed : 1f;
                        target = headPos + ((comVel / speed) * (lead * p.LookVelLead));
                        velocity = chestPos;
                        hasLook = 1;
                    }
                }
                else
                {
                    s.LookTime = 0f;
                }
                output.HasHeadLook = hasLook;
                output.HeadLookTarget = target;
                output.HeadLookVelocity = velocity;
            }
            if (p.UseBodyTurn != 0)
            {
                s.TurnTime -= dt;
                if (s.TurnTime < 0f)
                {
                    s.TurnTime = (Unit01(ref s.Rng) * p.TurnIntervalSpan) + p.TurnIntervalBase;
                    s.TurnRand = Unit01(ref s.Rng);
                }
                float sum = (((((p.TurnOffProb + p.TurnToTargetProb) + p.TurnToVelocityProb) + p.TurnAwayProb) + p.TurnSideProb) + p.TurnSideProb);
                if (sum > p.TurnProbFloor)
                {
                    float norm = 1f / sum;
                    float edgeVel = (p.TurnOffProb + p.TurnToTargetProb + p.TurnToVelocityProb) * norm;
                    float r = s.TurnRand;
                    Float3 right = Quaternion.Rotate(pelvisQ, new Float3(1f, 0f, 0f));
                    Float3 anchor = output.HasHeadLook != 0 ? output.HeadLookTarget : (input.HasPushSource != 0 ? input.PushSource : comPos);
                    Float3 away = comPos - anchor;
                    Float3 dir = Float3.Zero;
                    byte picked = 0;
                    float edgeTarget = (p.TurnOffProb + p.TurnToTargetProb) * norm;
                    float edgeOff = p.TurnOffProb * norm;
                    if (r > edgeOff && r <= edgeTarget)
                    {
                        dir = anchor - comPos;
                        picked = 1;
                    }
                    else if (r > edgeTarget && r <= edgeVel)
                    {
                        dir = comVel;
                        picked = 1;
                    }
                    else if (r > edgeVel)
                    {
                        float edgeAway = edgeVel + (p.TurnAwayProb * norm);
                        float edgeLeft = edgeAway + (p.TurnSideProb * norm);
                        if (r <= edgeAway)
                        {
                            dir = away;
                        }
                        else if (r <= edgeLeft)
                        {
                            dir = right;
                        }
                        else
                        {
                            dir = Float3.Zero - right;
                        }
                        picked = 1;
                    }
                    if (picked != 0 && r < edgeVel && dir.LengthSquared > 1e-12f)
                    {
                        output.HasTurn = 1;
                        output.TurnIntent = dir / dir.Length;
                    }
                }
                if (f19 < p.TurnSpeedGate)
                {
                    output.HasTurn = 0;
                    output.TurnIntent = Float3.Zero;
                }
            }
            output.HasStabilise = 1;
            output.StabiliseStiffness = 2f;
            output.StabiliseDamping = p.StabiliseDamping;
            float metric = tilt + sideTilt + tilt + sideTilt + ((spinY * p.RateGainXZ) + spinZ + (spinX * p.RateGainXZ));
            output.LeanResponse = metric;
            if (metric >= p.DecisionFallLean)
            {
                s.Decision = BalanceDecision.Falling;
                s.HoldTimer = p.FallHoldTime;
            }
            else if (s.Decision == BalanceDecision.Falling)
            {
                if (s.HoldTimer > 0f)
                {
                    s.HoldTimer -= dt;
                    s.Decision = BalanceDecision.Falling;
                }
                else if (metric >= p.DecisionFallLean * p.DecisionExitFraction)
                {
                    s.Decision = BalanceDecision.Falling;
                }
                else if (metric >= p.DecisionLoseLean * p.DecisionExitFraction)
                {
                    s.Decision = BalanceDecision.LosingBalance;
                }
                else
                {
                    s.Decision = BalanceDecision.Recovering;
                }
            }
            else if (s.Decision == BalanceDecision.LosingBalance)
            {
                if (metric < p.DecisionLoseLean * p.DecisionExitFraction)
                {
                    s.Decision = BalanceDecision.Recovering;
                }
            }
            else if (metric >= p.DecisionLoseLean)
            {
                s.Decision = BalanceDecision.LosingBalance;
            }
            output.Decision = s.Decision;
            return output;
        }

        private static void PassiveFill(Span<JointDrive> drives)
        {
            for (int i = 0; i < drives.Length; i++)
            {
                drives[i] = JointDrive.Passive;
            }
        }

        private static void PassiveOwned(Span<JointDrive> drives)
        {
            drives[(int)Role.Torso] = JointDrive.Passive;
            drives[(int)Role.Head] = JointDrive.Passive;
            drives[(int)Role.UpperArmLeft] = JointDrive.Passive;
            drives[(int)Role.UpperArmRight] = JointDrive.Passive;
            drives[(int)Role.ForearmLeft] = JointDrive.Passive;
            drives[(int)Role.ForearmRight] = JointDrive.Passive;
            drives[(int)Role.HandLeft] = JointDrive.Passive;
            drives[(int)Role.HandRight] = JointDrive.Passive;
        }

        private static byte InputsFinite(ReadOnlySpan<RoleBody> bodies, ReadOnlySpan<SupportProbe> obstacles, in BalanceInput input)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                RoleBody b = bodies[i];
                if (!b.Position.IsFinite || !b.LinearVelocity.IsFinite || !b.AngularVelocity.IsFinite || !Scalar.IsFinite(b.InverseMass) || !Scalar.IsFinite(b.Rotation.X) || !Scalar.IsFinite(b.Rotation.Y) || !Scalar.IsFinite(b.Rotation.Z) || !Scalar.IsFinite(b.Rotation.W))
                {
                    return 0;
                }
            }
            for (int i = 0; i < obstacles.Length; i++)
            {
                SupportProbe probe = obstacles[i];
                if (probe.Hit == 0)
                {
                    continue;
                }
                if (!probe.Point.IsFinite || !probe.Normal.IsFinite || !probe.PointVelocity.IsFinite || !Scalar.IsFinite(probe.Distance))
                {
                    return 0;
                }
            }
            if (input.HasPushSource != 0 && !input.PushSource.IsFinite)
            {
                return 0;
            }
            if (input.HasLookTarget != 0 && !input.LookTarget.IsFinite)
            {
                return 0;
            }
            return 1;
        }

        private static float YawOf(Quaternion q)
        {
            Float3 fwd = Quaternion.Rotate(q, new Float3(0f, 0f, 1f));
            return (float)Math.Atan2(fwd.X, fwd.Z);
        }

        private static float WrapPi(float a)
        {
            while (a > (float)Math.PI)
            {
                a -= 2f * (float)Math.PI;
            }
            while (a < -(float)Math.PI)
            {
                a += 2f * (float)Math.PI;
            }
            return a;
        }

        private static float Over(float x, float threshold, float cap)
        {
            float m = Abs(x);
            if (m < threshold)
            {
                m = threshold;
            }
            if (m > cap)
            {
                m = cap;
            }
            return m - threshold;
        }

        private static float AsinClamped(float x)
        {
            if (x < -1f)
            {
                x = -1f;
            }
            if (x > 1f)
            {
                x = 1f;
            }
            return (float)Math.Asin(x);
        }

        private static float Abs(float v)
        {
            return v < 0f ? -v : v;
        }

        private static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        private static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo)
            {
                return lo;
            }
            if (v > hi)
            {
                return hi;
            }
            return v;
        }
    }
}

using System;
using SA.Humanoid;
using SA.Motion.Numerics;

namespace SA.Balance
{
    public static class StandingController
    {
        public static BalanceReport Tick(
            in BalanceParams p,
            ref BalanceState state,
            ReadOnlySpan<BodyState> bodies,
            ReadOnlySpan<FootContact> feet,
            in BalanceInput input,
            Span<BodyDrive> drives)
        {
            BalanceParams q = BalanceMath.SanitizeParams(p);
            int count = drives.Length < Roles.Count ? drives.Length : Roles.Count;
            if (bodies.Length < Roles.Count || count <= 0)
            {
                FillPassive(drives);
                return NeutralReport();
            }
            float dt = input.Timestep;
            if (!Scalar.IsFinite(dt) || dt <= 0f)
            {
                dt = 0.01666667f;
            }
            if (dt > 0.03333334f)
            {
                dt = 0.03333334f;
            }
            BodyState pelvis = SanitizeBody(bodies[(int)Role.Pelvis]);
            BodyState torso = SanitizeBody(bodies[(int)Role.Torso]);
            if (!state.HasStart)
            {
                state.HasStart = true;
                state.StartPelvis = pelvis.Position;
                state.SwingFoot = -1;
                state.SwingTime = 0f;
                state.FallingLatched = false;
            }
            BalanceMath.CenterOfMass(bodies, out Float3 com, out Float3 comVelocity);
            Float3 capture = BalanceMath.CapturePoint(com, comVelocity, q);
            Float3 fallback = new Float3(pelvis.Position.X, 0f, pelvis.Position.Z);
            Float3 support = BalanceMath.SupportCenter(feet, fallback);
            float reach = DistanceXZ(capture, support);
            float margin = reach - q.SupportRadius;
            UpdateSwing(bodies, capture, support, comVelocity, reach, margin, dt, q, ref state);
            if (state.FallingLatched)
            {
                WriteFallTone(bodies, q, drives, count);
                FinishTick(capture, ref state);
                return new BalanceReport(Decision.Falling, com, capture, margin, -1);
            }
            float heightError = state.StartPelvis.Y - pelvis.Position.Y;
            float flex = BalanceMath.Clamp(heightError * q.HeightGain, -q.HeightMax, q.HeightMax);
            Float3 upright = UprightOffset(torso, q);
            Float3 ankle = AnkleOffset(capture, support, comVelocity, q);
            Float3 hip = HipOffset(capture, support, comVelocity, q);
            Float3 torsoLean = upright + hip;
            Float3 pelvisRoll = new Float3(0f, 0f, hip.Z * 0.5f);
            Quaternion swingThigh = Quaternion.Identity;
            Quaternion swingShin = Quaternion.Identity;
            bool hasSwing = SwingTargets(bodies, q, ref state, out swingThigh, out swingShin);
            for (int i = 0; i < count; i++)
            {
                var role = (Role)i;
                BodyState body = SanitizeBody(bodies[i]);
                Quaternion target = BaseTarget(input.TargetWorld, i, body.Rotation);
                target = ApplyCorrection(target, role, torsoLean, pelvisRoll, ankle, flex, q);
                if (hasSwing)
                {
                    target = OverrideSwing(target, role, state.SwingFoot, swingThigh, swingShin);
                }
                float stiffness = BalanceMath.StiffnessFor(role, q);
                float damping = BalanceMath.DampingFor(role, q);
                if (hasSwing && IsStanceLeg(role, state.SwingFoot))
                {
                    stiffness *= q.StanceBoost;
                }
                var drive = new BodyDrive();
                drive.TargetWorld = target;
                drive.Stiffness = stiffness;
                drive.Damping = damping;
                drive.MaxTorque = stiffness * q.TorqueScale;
                drives[i] = drive;
            }
            for (int i = count; i < drives.Length; i++)
            {
                drives[i] = BodyDrive.Passive;
            }
            FinishTick(capture, ref state);
            if (hasSwing)
            {
                return new BalanceReport(Decision.Stepping, com, capture, margin, state.SwingFoot);
            }
            if (margin > 0f)
            {
                return new BalanceReport(Decision.Recovering, com, capture, margin, -1);
            }
            return new BalanceReport(Decision.Standing, com, capture, margin, -1);
        }

        static void FillPassive(Span<BodyDrive> drives)
        {
            for (int i = 0; i < drives.Length; i++)
            {
                drives[i] = BodyDrive.Passive;
            }
        }

        static BalanceReport NeutralReport()
        {
            return new BalanceReport(Decision.Standing, Float3.Zero, Float3.Zero, 0f, -1);
        }

        static BodyState SanitizeBody(BodyState body)
        {
            body.Position = BalanceMath.SanitizeVector(body.Position, Float3.Zero);
            if (!body.Rotation.IsValidRotation)
            {
                body.Rotation = Quaternion.Identity;
            }
            body.Rotation = Quaternion.Normalize(body.Rotation);
            body.LinearVelocity = BalanceMath.SanitizeVector(body.LinearVelocity, Float3.Zero);
            body.AngularVelocity = BalanceMath.SanitizeVector(body.AngularVelocity, Float3.Zero);
            if (!Scalar.IsFinite(body.InverseMass) || body.InverseMass < 0f)
            {
                body.InverseMass = 0f;
            }
            return body;
        }

        static Quaternion BaseTarget(Quaternion[]? targets, int index, Quaternion fallback)
        {
            if (targets is null || index < 0 || index >= targets.Length)
            {
                return BalanceMath.SanitizeOrientation(fallback, Quaternion.Identity);
            }
            return BalanceMath.SanitizeOrientation(targets[index], fallback);
        }

        static Float3 UprightOffset(BodyState torso, BalanceParams q)
        {
            Float3 up = Quaternion.Rotate(torso.Rotation, new Float3(0f, 1f, 0f));
            if (!up.IsFinite)
            {
                return Float3.Zero;
            }
            Float3 tilt = new Float3(up.X, 0f, up.Z);
            Float3 spin = torso.AngularVelocity;
            if (!spin.IsFinite)
            {
                spin = Float3.Zero;
            }
            Float3 rate = new Float3(-spin.Z, 0f, spin.X);
            return tilt * -q.UprightGain + rate * -q.UprightDamp;
        }

        static Float3 AnkleOffset(Float3 capture, Float3 support, Float3 velocity, BalanceParams q)
        {
            float errorZ = capture.Z - support.Z;
            float pitched = errorZ * q.AnkleGain + velocity.Z * q.AnkleVelGain;
            pitched = BalanceMath.Clamp(pitched, -q.LeanMax, q.LeanMax);
            return new Float3(pitched, 0f, 0f);
        }

        static Float3 HipOffset(Float3 capture, Float3 support, Float3 velocity, BalanceParams q)
        {
            float errorX = capture.X - support.X;
            float errorZ = capture.Z - support.Z;
            float pitched = errorZ * q.HipGain + velocity.Z * q.HipVelGain;
            float rolled = errorX * q.HipRollGain + velocity.X * q.HipRollVelGain;
            pitched = BalanceMath.Clamp(pitched, -q.LeanMax, q.LeanMax);
            rolled = BalanceMath.Clamp(rolled, -q.LeanMax, q.LeanMax);
            return new Float3(pitched, 0f, rolled);
        }

        static Quaternion ApplyCorrection(Quaternion target, Role role, Float3 torsoLean, Float3 pelvisRoll, Float3 ankle, float flex, BalanceParams q)
        {
            switch (role)
            {
                case Role.Torso:
                    return BalanceMath.OffsetByVector(target, torsoLean, q.UprightMax);
                case Role.Pelvis:
                    return BalanceMath.OffsetByVector(target, pelvisRoll, q.UprightMax);
                case Role.FootLeft:
                case Role.FootRight:
                    return BalanceMath.OffsetByVector(target, ankle, q.LeanMax);
                case Role.ThighLeft:
                case Role.ThighRight:
                    return BalanceMath.OffsetAboutAxis(target, new Float3(1f, 0f, 0f), flex * 0.5f);
                case Role.ShinLeft:
                case Role.ShinRight:
                    return BalanceMath.OffsetAboutAxis(target, new Float3(1f, 0f, 0f), -flex);
                default:
                    return target;
            }
        }
        static bool IsStanceLeg(Role role, int swingFoot)
        {
            if (swingFoot == 0)
            {
                return role == Role.ThighRight || role == Role.ShinRight || role == Role.FootRight;
            }
            if (swingFoot == 1)
            {
                return role == Role.ThighLeft || role == Role.ShinLeft || role == Role.FootLeft;
            }
            return false;
        }

        static void UpdateSwing(ReadOnlySpan<BodyState> bodies, Float3 capture, Float3 support, Float3 velocity, float reach, float margin, float dt, BalanceParams q, ref BalanceState state)
        {
            float twoStep = q.MaxReach + q.MaxReach + q.SupportRadius;
            float limit = twoStep < q.FallReach ? twoStep : q.FallReach;
            if (state.SwingFoot == 0 || state.SwingFoot == 1)
            {
                state.SwingTime += dt;
                if (state.SwingTime >= q.StepTime)
                {
                    state.SwingFoot = -1;
                    state.SwingTime = 0f;
                    return;
                }
                else
                {
                    if (state.FallingLatched)
                    {
                        return;
                    }
                    if (reach > limit)
                    {
                        state.SwingFoot = -1;
                        state.SwingTime = 0f;
                        state.FallingLatched = true;
                        return;
                    }
                    if (margin > q.StepMargin && reach > 1e-6f && state.SwingTime >= q.StepTime * q.ChainPhase && DistanceXZ(capture, state.SwingTo) > q.StepMargin + q.StepMargin)
                    {
                        BeginStep(bodies, capture, support, velocity, reach, 1 - state.SwingFoot, q, ref state);
                    }
                    return;
                }
            }
            else
            {
                state.SwingFoot = -1;
                state.SwingTime = 0f;
            }
            if (state.FallingLatched)
            {
                return;
            }
            if (!(margin > q.StepMargin))
            {
                return;
            }
            if (!(reach > 1e-6f))
            {
                return;
            }
            if (reach > limit)
            {
                state.FallingLatched = true;
                return;
            }
            BodyState left = SanitizeBody(bodies[(int)Role.FootLeft]);
            BodyState right = SanitizeBody(bodies[(int)Role.FootRight]);
            float nearLeft = DistanceXZ(capture, left.Position);
            float nearRight = DistanceXZ(capture, right.Position);
            int foot = nearLeft <= nearRight ? 0 : 1;
            BeginStep(bodies, capture, support, velocity, reach, foot, q, ref state);
        }

        static void BeginStep(ReadOnlySpan<BodyState> bodies, Float3 capture, Float3 support, Float3 velocity, float reach, int foot, BalanceParams q, ref BalanceState state)
        {
            Float3 direction = (capture - support) / reach;
            Float3 lead = new Float3(velocity.X, 0f, velocity.Z) * (q.StepTime * q.StepLead);
            Float3 goal = capture + lead + direction * q.StepMargin;
            Float3 offset = goal - support;
            float length = DistanceXZ(goal, support);
            if (length > q.MaxReach)
            {
                goal = support + offset * (q.MaxReach / length);
            }
            int chosen = foot == 0 ? 0 : 1;
            BodyState stepping = chosen == 0 ? SanitizeBody(bodies[(int)Role.FootLeft]) : SanitizeBody(bodies[(int)Role.FootRight]);
            state.SwingFoot = chosen;
            state.SwingTime = 0f;
            state.SwingFrom = new Float3(stepping.Position.X, 0f, stepping.Position.Z);
            state.SwingTo = new Float3(goal.X, 0f, goal.Z);
        }

        static bool SwingTargets(ReadOnlySpan<BodyState> bodies, BalanceParams q, ref BalanceState state, out Quaternion thigh, out Quaternion shin)
        {
            thigh = Quaternion.Identity;
            shin = Quaternion.Identity;
            if (!(state.SwingFoot == 0 || state.SwingFoot == 1))
            {
                return false;
            }
            float alpha = state.SwingTime / q.StepTime;
            alpha = BalanceMath.Clamp(alpha, 0f, 1f);
            Float3 top = state.SwingTo - state.SwingFrom;
            Float3 path = state.SwingFrom + top * alpha;
            float lift = (float)Math.Sin(alpha * Scalar.Pi) * q.StepHeight;
            Float3 sole = new Float3(path.X, lift, path.Z);
            Float3 ankleGoal = sole + new Float3(0f, q.StepAnkleHeight, 0f);
            BodyState pelvis = SanitizeBody(bodies[(int)Role.Pelvis]);
            int side = state.SwingFoot == 0 ? -1 : 1;
            Float3 hip = BalanceMath.HipJoint(pelvis, side);
            Float3 knee;
            BalanceMath.SolveKnee(hip, ankleGoal, q.StepThighLength, q.StepShinLength, out knee);
            thigh = BalanceMath.MapAxisY(hip - knee);
            shin = BalanceMath.MapAxisY(knee - ankleGoal);
            if (!thigh.IsValidRotation)
            {
                thigh = Quaternion.Identity;
            }
            if (!shin.IsValidRotation)
            {
                shin = Quaternion.Identity;
            }
            return true;
        }

        static Quaternion OverrideSwing(Quaternion target, Role role, int swingFoot, Quaternion thigh, Quaternion shin)
        {
            if (swingFoot == 0)
            {
                if (role == Role.ThighLeft)
                {
                    return thigh;
                }
                if (role == Role.ShinLeft)
                {
                    return shin;
                }
                return target;
            }
            if (swingFoot == 1)
            {
                if (role == Role.ThighRight)
                {
                    return thigh;
                }
                if (role == Role.ShinRight)
                {
                    return shin;
                }
                return target;
            }
            return target;
        }

        static void WriteFallTone(ReadOnlySpan<BodyState> bodies, BalanceParams q, Span<BodyDrive> drives, int count)
        {
            for (int i = 0; i < count; i++)
            {
                BodyState body = SanitizeBody(bodies[i]);
                var drive = new BodyDrive();
                drive.TargetWorld = body.Rotation;
                drive.Stiffness = q.FallToneStiffness;
                drive.Damping = q.FallToneDamping;
                drive.MaxTorque = q.FallToneTorque;
                drives[i] = drive;
            }
            for (int i = count; i < drives.Length; i++)
            {
                drives[i] = BodyDrive.Passive;
            }
        }

        static void FinishTick(Float3 capture, ref BalanceState state)
        {
            if (state.Ticks < int.MaxValue)
            {
                state.Ticks++;
            }
            state.LastCapture = capture;
        }

        static float DistanceXZ(Float3 a, Float3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}

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
            float margin = DistanceXZ(capture, support) - q.SupportRadius;
            float heightError = state.StartPelvis.Y - pelvis.Position.Y;
            float flex = BalanceMath.Clamp(heightError * q.HeightGain, -q.HeightMax, q.HeightMax);
            Float3 upright = UprightOffset(torso, q);
            Float3 ankle = AnkleOffset(capture, support, comVelocity, q);
            Float3 hip = HipOffset(capture, support, comVelocity, q);
            Float3 torsoLean = upright + hip;
            for (int i = 0; i < count; i++)
            {
                var role = (Role)i;
                BodyState body = SanitizeBody(bodies[i]);
                Quaternion target = BaseTarget(input.TargetWorld, i, body.Rotation);
                target = ApplyCorrection(target, role, torsoLean, ankle, flex, q);
                float stiffness = BalanceMath.StiffnessFor(role, q);
                float damping = BalanceMath.DampingFor(role, q);
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
            if (state.Ticks < int.MaxValue)
            {
                state.Ticks++;
            }
            state.LastCapture = capture;
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
            float error = capture.Z - support.Z;
            float push = velocity.Z;
            float pitched = error * q.AnkleGain + push * q.AnkleVelGain;
            pitched = BalanceMath.Clamp(pitched, -q.LeanMax, q.LeanMax);
            return new Float3(pitched, 0f, 0f);
        }

        static Float3 HipOffset(Float3 capture, Float3 support, Float3 velocity, BalanceParams q)
        {
            float errorX = capture.X - support.X;
            float errorZ = capture.Z - support.Z;
            float pitched = errorZ * q.HipGain + velocity.Z * q.HipVelGain;
            float rolled = errorX * q.HipGain + velocity.X * q.HipVelGain;
            pitched = BalanceMath.Clamp(pitched, -q.LeanMax, q.LeanMax);
            rolled = BalanceMath.Clamp(rolled, -q.LeanMax, q.LeanMax);
            return new Float3(pitched, 0f, -rolled);
        }

        static Quaternion ApplyCorrection(Quaternion target, Role role, Float3 upright, Float3 ankle, float flex, BalanceParams q)
        {
            switch (role)
            {
                case Role.Torso:
                    return BalanceMath.OffsetByVector(target, upright, q.UprightMax);
                case Role.Pelvis:
                    return target;
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

        static float DistanceXZ(Float3 a, Float3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}

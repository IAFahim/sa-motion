using System;
using SA.Humanoid;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Balance
{
    public static class SnapshotBuilder
    {
        public static bool TryBuild(
            in BalanceParams p,
            ReadOnlySpan<RoleBody> bodies,
            in SupportProbe leftProbe,
            in SupportProbe rightProbe,
            ref BalanceSnapshot s)
        {
            s = default;
            if (bodies.Length < Roles.Count)
                return false;
            var up = BalanceMath.Sanitize(p.Up);
            if (!(up.Length > 1e-6f))
                up = new Float3(0f, 1f, 0f);
            else
                up = up / up.Length;
            var com = Float3.Zero;
            var velocity = Float3.Zero;
            var angular = Float3.Zero;
            var mass = 0f;
            for (var i = 0; i < Roles.Count; i++)
            {
                var body = bodies[i];
                if (!body.Position.IsFinite || !body.LinearVelocity.IsFinite || !body.AngularVelocity.IsFinite)
                    continue;
                if (!Scalar.IsFinite(body.InverseMass) || !(body.InverseMass > 0f))
                    continue;
                var weight = body.InverseMass > 10000f ? 10000f : body.InverseMass;
                com += BalanceMath.Sanitize(body.Position) * weight;
                velocity += BalanceMath.Sanitize(body.LinearVelocity) * weight;
                angular += BalanceMath.Sanitize(body.AngularVelocity) * weight;
                mass += weight;
            }
            if (!(mass > 0f))
                return false;
            s.Com = com / mass;
            s.Velocity = BalanceMath.Sanitize(velocity / mass);
            s.AngularVelocity = BalanceMath.Sanitize(angular / mass);
            s.HorizontalVelocity = BalanceMath.Horizontal(s.Velocity, up);
            var pelvisBody = bodies[(int)Role.Pelvis];
            var pelvisRotation = BalanceMath.SanitizeRotation(pelvisBody.Rotation);
            var quarter = new SA.Motion.Numerics.Quaternion(0.7071068f, 0f, 0f, 0.7071068f);
            var rootRotation = SA.Motion.Numerics.Quaternion.Normalize(SA.Motion.Numerics.Quaternion.Multiply(pelvisRotation, quarter));
            s.Root = new Frame(s.Com, rootRotation);
            s.Left = BuildLeg(p, bodies, true);
            s.Right = BuildLeg(p, bodies, false);
            s.Spine = BuildSpine(p, bodies);
            s.Pelvis = new BalancePart
            {
                Frame = new Frame(BalanceMath.Sanitize(pelvisBody.Position), pelvisRotation),
                Velocity = BalanceMath.Sanitize(pelvisBody.LinearVelocity),
                AngularVelocity = BalanceMath.Sanitize(pelvisBody.AngularVelocity)
            };
            s.SupportLeft = BuildSupport(leftProbe, up);
            s.SupportRight = BuildSupport(rightProbe, up);
            s.HeightTarget = 0f;
            return true;
        }

        static BalanceSupport BuildSupport(in SupportProbe probe, Float3 up)
        {
            var contact = probe.Hit != 0;
            var target = BalanceMath.Sanitize(probe.Point);
            var normal = BalanceMath.SanitizeUnit(probe.Normal, up);
            var groundVelocity = BalanceMath.Sanitize(probe.PointVelocity);
            return new BalanceSupport
            {
                Contact = contact,
                Target = target,
                Normal = normal,
                GroundVelocity = groundVelocity
            };
        }

        static BalanceLeg BuildLeg(in BalanceParams p, ReadOnlySpan<RoleBody> bodies, bool left)
        {
            var thighRole = left ? Role.ThighLeft : Role.ThighRight;
            var shinRole = left ? Role.ShinLeft : Role.ShinRight;
            var footRole = left ? Role.FootLeft : Role.FootRight;
            var thigh = bodies[(int)thighRole];
            var shin = bodies[(int)shinRole];
            var foot = bodies[(int)footRole];
            var thighPosition = BalanceMath.Sanitize(thigh.Position);
            var kneePosition = BalanceMath.Sanitize(shin.Position);
            var anklePosition = BalanceMath.Sanitize(foot.Position);
            var thighDirection = BalanceMath.NormalizeOrZero(kneePosition - thighPosition, 1e-6f);
            if (!(thighDirection.Length > 0f))
                thighDirection = new Float3(0f, -1f, 0f);
            var shinDirection = BalanceMath.NormalizeOrZero(anklePosition - kneePosition, 1e-6f);
            if (!(shinDirection.Length > 0f))
                shinDirection = new Float3(0f, -1f, 0f);
            var halfSegment = (thighPosition - kneePosition) * 0.5f;
            var hipJoint = thighPosition + halfSegment;
            var kneeJoint = (thighPosition + kneePosition) * 0.5f;
            var ankleJoint = kneePosition - halfSegment;
            var hipFrame = new Frame(hipJoint, BalanceMath.Arc(new Float3(0f, 0f, 1f), thighDirection, 1e-6f));
            var kneeFrame = new Frame(kneeJoint, BalanceMath.Arc(new Float3(0f, 0f, 1f), shinDirection, 1e-6f));
            var ankleFrame = new Frame(ankleJoint, BalanceMath.SanitizeRotation(foot.Rotation));
            var footFrame = new Frame(anklePosition, BalanceMath.SanitizeRotation(foot.Rotation));
            var thighFrame = new Frame(thighPosition, BalanceMath.SanitizeRotation(thigh.Rotation));
            var shinFrame = new Frame(kneePosition, BalanceMath.SanitizeRotation(shin.Rotation));
            var limb = Frames.Relative(hipFrame, kneeFrame);
            var leg = new BalanceLeg
            {
                Hip = new BalanceJoint3
                {
                    Frame = hipFrame,
                    Position = hipFrame.Position,
                    HalfTwist = p.HipHalfTwist,
                    HalfLean1 = p.HipHalfLean1,
                    HalfLean2 = p.HipHalfLean2,
                    MidTwist = p.HipMidTwist,
                    MidLean1 = p.HipMidLean1,
                    MidLean2 = p.HipMidLean2,
                    Actual = new Float3(0f, 0f, HipTwist(hipFrame, thighFrame))
                },
                Thigh = new BalancePart
                {
                    Frame = thighFrame,
                    Velocity = BalanceMath.Sanitize(thigh.LinearVelocity),
                    AngularVelocity = BalanceMath.Sanitize(thigh.AngularVelocity)
                },
                Knee = new BalanceJoint1
                {
                    Frame = kneeFrame,
                    ChildFrame = kneeFrame,
                    Position = kneeFrame.Position,
                    HalfRange = p.KneeHalfRange,
                    Mid = p.KneeMid,
                    Actual = KneeFlexion(thighDirection, shinDirection)
                },
                Shin = new BalancePart
                {
                    Frame = shinFrame,
                    Velocity = BalanceMath.Sanitize(shin.LinearVelocity),
                    AngularVelocity = BalanceMath.Sanitize(shin.AngularVelocity)
                },
                Ankle = new BalanceJoint3
                {
                    Frame = ankleFrame,
                    Position = ankleFrame.Position,
                    HalfTwist = 0.1f,
                    HalfLean1 = p.AnkleHalfPitch,
                    HalfLean2 = p.AnkleHalfYaw,
                    MidTwist = 0f,
                    MidLean1 = p.AnkleMidPitch,
                    MidLean2 = p.AnkleMidYaw,
                    Actual = Float3.Zero
                },
                Foot = new BalancePart
                {
                    Frame = footFrame,
                    Velocity = BalanceMath.Sanitize(foot.LinearVelocity),
                    AngularVelocity = BalanceMath.Sanitize(foot.AngularVelocity)
                },
                Limb = limb,
                ThighLength = BalanceMath.SegmentLength(hipJoint, kneeJoint),
                ShinLength = BalanceMath.SegmentLength(kneeJoint, ankleJoint)
            };
            return leg;
        }

        static float HipTwist(Frame hip, Frame thigh)
        {
            BalanceMath.SplitRotation(Frames.Relative(hip, thigh).Rotation, out _, out _, out var twist);
            if (!Scalar.IsFinite(twist))
                return 0f;
            return twist;
        }

        static float KneeFlexion(Float3 thighDirection, Float3 shinDirection)
        {
            var cosine = BalanceMath.Clamp(Float3.Dot(thighDirection, shinDirection), -1f, 1f);
            return -BalanceMath.Acos(cosine);
        }

        static BalanceJoint3 BuildSpine(in BalanceParams p, ReadOnlySpan<RoleBody> bodies)
        {
            var torso = bodies[(int)Role.Torso];
            var frame = new Frame(
                BalanceMath.Sanitize(torso.Position),
                BalanceMath.SanitizeRotation(torso.Rotation));
            return new BalanceJoint3
            {
                Frame = frame,
                Position = frame.Position,
                HalfTwist = p.SpineHalfLean,
                HalfLean1 = p.SpineHalfLean,
                HalfLean2 = p.SpineHalfLean,
                MidTwist = p.SpineMidLean,
                MidLean1 = p.SpineMidLean,
                MidLean2 = p.SpineMidLean
            };
        }
    }
}

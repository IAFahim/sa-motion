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
                com += body.Position * weight;
                velocity += body.LinearVelocity * weight;
                angular += body.AngularVelocity * weight;
                mass += weight;
            }
            if (!(mass > 0f))
                return false;
            s.Com = com / mass;
            s.Velocity = BalanceMath.Sanitize(velocity / mass);
            s.AngularVelocity = BalanceMath.Sanitize(angular / mass);
            s.HorizontalVelocity = BalanceMath.Horizontal(s.Velocity, up);
            var pelvisBody = bodies[(int)Role.Pelvis];
            var rootRotation = BalanceMath.SanitizeRotation(pelvisBody.Rotation);
            s.Root = new Frame(s.Com, rootRotation);
            s.Left = BuildLeg(p, bodies, true);
            s.Right = BuildLeg(p, bodies, false);
            s.Spine = BuildSpine(p, bodies);
            var pelvisFrame = new Frame(
                BalanceMath.Sanitize(pelvisBody.Position),
                BalanceMath.SanitizeRotation(pelvisBody.Rotation));
            s.Pelvis = new BalancePart
            {
                Frame = pelvisFrame,
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
            var hipFrame = new Frame(
                BalanceMath.Sanitize(thigh.Position),
                BalanceMath.SanitizeRotation(thigh.Rotation));
            var kneeFrame = new Frame(
                BalanceMath.Sanitize(shin.Position),
                BalanceMath.SanitizeRotation(shin.Rotation));
            var ankleFrame = new Frame(
                BalanceMath.Sanitize(foot.Position),
                BalanceMath.SanitizeRotation(foot.Rotation));
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
                    MidLean2 = p.HipMidLean2
                },
                Thigh = new BalancePart
                {
                    Frame = hipFrame,
                    Velocity = BalanceMath.Sanitize(thigh.LinearVelocity),
                    AngularVelocity = BalanceMath.Sanitize(thigh.AngularVelocity)
                },
                Knee = new BalanceJoint1
                {
                    Frame = kneeFrame,
                    ChildFrame = kneeFrame,
                    Position = kneeFrame.Position,
                    HalfRange = p.KneeHalfRange,
                    Mid = p.KneeMid
                },
                Shin = new BalancePart
                {
                    Frame = kneeFrame,
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
                    MidLean2 = p.AnkleMidYaw
                },
                Foot = new BalancePart
                {
                    Frame = ankleFrame,
                    Velocity = BalanceMath.Sanitize(foot.LinearVelocity),
                    AngularVelocity = BalanceMath.Sanitize(foot.AngularVelocity)
                },
                Limb = limb,
                ThighLength = BalanceMath.SegmentLength(hipFrame.Position, kneeFrame.Position),
                ShinLength = BalanceMath.SegmentLength(kneeFrame.Position, ankleFrame.Position)
            };
            return leg;
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

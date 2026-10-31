using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using Xunit;

namespace SA.ReactionControl.Tests
{
    public class GetUpTests
    {
        static RoleBody[] MakeBodies(Float3 pelvisPosition, Quaternion pelvisRotation, Quaternion torsoRotation, Float3 headPosition)
        {
            var bodies = new RoleBody[Roles.Count];
            bodies[(int)Role.Pelvis] = new RoleBody { Position = pelvisPosition, Rotation = pelvisRotation };
            bodies[(int)Role.Torso] = new RoleBody { Position = new Float3(pelvisPosition.X, pelvisPosition.Y + 0.1f, pelvisPosition.Z), Rotation = torsoRotation };
            bodies[(int)Role.Head] = new RoleBody { Position = headPosition, Rotation = Quaternion.Identity };
            return bodies;
        }

        static RoleBody[] LyingBodies(Quaternion torsoRotation)
        {
            var pelvis = new Float3(0f, 0.4f, 0f);
            return MakeBodies(pelvis, Quaternion.Identity, torsoRotation, new Float3(0f, 0.45f, 0.8f));
        }

        static Quaternion Yaw(float angle)
        {
            return Quaternion.Normalize(Quaternion.AxisAngle(new Float3(0f, 1f, 0f), angle));
        }

        [Fact]
        public void SupineTorsoReadsAsSupine()
        {
            var pose = GetUp.Classify(LyingBodies(Quaternion.Identity), GetUpParams.Standard);
            Assert.Equal(RestingPose.Supine, pose);
        }

        [Fact]
        public void SupineWithYawStillReadsAsSupine()
        {
            var bodies = LyingBodies(Quaternion.Normalize(Quaternion.Multiply(Yaw((float)Math.PI), Quaternion.Identity)));
            Assert.Equal(RestingPose.Supine, GetUp.Classify(bodies, GetUpParams.Standard));
        }

        [Fact]
        public void ProneTorsoReadsAsProne()
        {
            var prone = Quaternion.Normalize(Quaternion.AxisAngle(new Float3(1f, 0f, 0f), (float)Math.PI));
            Assert.Equal(RestingPose.Prone, GetUp.Classify(LyingBodies(prone), GetUpParams.Standard));
        }

        [Fact]
        public void ProneWithYawStillReadsAsProne()
        {
            var prone = Quaternion.Normalize(Quaternion.AxisAngle(new Float3(1f, 0f, 0f), (float)Math.PI));
            var bodies = LyingBodies(Quaternion.Normalize(Quaternion.Multiply(Yaw((float)Math.PI), prone)));
            Assert.Equal(RestingPose.Prone, GetUp.Classify(bodies, GetUpParams.Standard));
        }

        [Fact]
        public void RolledTorsoReadsAsSide()
        {
            var side = Quaternion.Normalize(Quaternion.AxisAngle(new Float3(0f, 0f, 1f), (float)Math.PI * 0.5f));
            Assert.Equal(RestingPose.Side, GetUp.Classify(LyingBodies(side), GetUpParams.Standard));
        }

        [Fact]
        public void RolledTorsoWithYawStillReadsAsSide()
        {
            var side = Quaternion.Normalize(Quaternion.AxisAngle(new Float3(0f, 0f, 1f), (float)Math.PI * 0.5f));
            var bodies = LyingBodies(Quaternion.Normalize(Quaternion.Multiply(Yaw((float)Math.PI), side)));
            Assert.Equal(RestingPose.Side, GetUp.Classify(bodies, GetUpParams.Standard));
        }

        [Fact]
        public void StandingTorsoReadsAsUpright()
        {
            var pelvis = new Float3(0f, 1f, 0f);
            var bodies = MakeBodies(pelvis, Quaternion.Identity, Quaternion.Identity, new Float3(0f, 1.6f, 0f));
            Assert.Equal(RestingPose.Upright, GetUp.Classify(bodies, GetUpParams.Standard));
        }

        [Fact]
        public void BadDataReadsAsUnknown()
        {
            Assert.Equal(RestingPose.Unknown, GetUp.Classify(Array.Empty<RoleBody>(), GetUpParams.Standard));
            var bodies = LyingBodies(Quaternion.Identity);
            bodies[(int)Role.Torso] = new RoleBody { Position = new Float3(float.NaN, 0f, 0f), Rotation = Quaternion.Identity };
            Assert.Equal(RestingPose.Unknown, GetUp.Classify(bodies, GetUpParams.Standard));
            bodies = LyingBodies(new Quaternion(float.NaN, 0f, 0f, 0f));
            Assert.Equal(RestingPose.Unknown, GetUp.Classify(bodies, GetUpParams.Standard));
        }

        [Fact]
        public void MatchingClipWinsAndMissingClipIsExplicit()
        {
            var clips = new GetUpClipEntry[]
            {
                new GetUpClipEntry { ClipId = 7, StartPose = RestingPose.Supine, RootOffset = Float3.Zero },
                new GetUpClipEntry { ClipId = 3, StartPose = RestingPose.Prone, RootOffset = Float3.Zero }
            };
            var prone = GetUp.ChooseClip(RestingPose.Prone, clips);
            Assert.Equal(1, prone.HasClip);
            Assert.Equal(3, prone.ClipId);
            var supine = GetUp.ChooseClip(RestingPose.Supine, clips);
            Assert.Equal(7, supine.ClipId);
            var missing = GetUp.ChooseClip(RestingPose.Side, clips);
            Assert.Equal(0, missing.HasClip);
            Assert.Equal(-1, missing.ClipId);
            var empty = GetUp.ChooseClip(RestingPose.Supine, Array.Empty<GetUpClipEntry>());
            Assert.Equal(0, empty.HasClip);
        }

        [Fact]
        public void AlignmentFacesPelvisForwardOnGround()
        {
            var yaw = 0.7f;
            var pelvis = new Float3(1f, 0.5f, 2f);
            var bodies = MakeBodies(pelvis, Yaw(yaw), Quaternion.Identity, new Float3(1f, 0.55f, 2.8f));
            var alignment = GetUp.ComputeAlignment(bodies, GetUpParams.Standard);
            Assert.Equal(1, alignment.Valid);
            Assert.True(Scalar.IsFinite(alignment.Yaw));
            Assert.Equal(yaw, alignment.Yaw, 4);
            Assert.Equal(1f, alignment.Position.X, 4);
            Assert.Equal(0f, alignment.Position.Y, 4);
            Assert.Equal(2f, alignment.Position.Z, 4);
        }

        [Fact]
        public void AlignmentClampsWildInputInsideBounds()
        {
            var wild = new Float3(1e30f, 0.5f, -1e30f);
            var bodies = MakeBodies(wild, Yaw(0.3f), Quaternion.Identity, new Float3(0f, 0.55f, 0.8f));
            var alignment = GetUp.ComputeAlignment(bodies, GetUpParams.Standard);
            Assert.True(Scalar.IsFinite(alignment.Position.X));
            Assert.True(Scalar.IsFinite(alignment.Position.Z));
            Assert.True(Scalar.Abs(alignment.Position.X) <= GetUpParams.Standard.MaxAlignOffset);
            Assert.True(Scalar.Abs(alignment.Position.Z) <= GetUpParams.Standard.MaxAlignOffset);
            Assert.True(Scalar.IsFinite(alignment.Yaw));
        }

        [Fact]
        public void DegenerateForwardIsInvalidButFinite()
        {
            var pitched = Quaternion.Normalize(Quaternion.AxisAngle(new Float3(1f, 0f, 0f), (float)Math.PI * 0.5f));
            var bodies = MakeBodies(new Float3(0f, 0.4f, 0f), pitched, Quaternion.Identity, new Float3(0f, 0.45f, 0.8f));
            var alignment = GetUp.ComputeAlignment(bodies, GetUpParams.Standard);
            Assert.Equal(0, alignment.Valid);
            Assert.True(alignment.Position.IsFinite);
            Assert.True(Scalar.IsFinite(alignment.Yaw));
        }

        [Fact]
        public void BlockedClearanceBlocksAndMissesGo()
        {
            var blocked = new SupportProbe[] { new SupportProbe { Hit = 1, Distance = 0.5f } };
            Assert.Equal(GetUpClearance.Blocked, GetUp.CheckClearance(blocked, GetUpParams.Standard));
            Assert.Equal(GetUpClearance.Go, GetUp.CheckClearance(Array.Empty<SupportProbe>(), GetUpParams.Standard));
            var miss = new SupportProbe[] { new SupportProbe { Hit = 0, Distance = 0.1f } };
            Assert.Equal(GetUpClearance.Go, GetUp.CheckClearance(miss, GetUpParams.Standard));
            var far = new SupportProbe[] { new SupportProbe { Hit = 1, Distance = 10f } };
            Assert.Equal(GetUpClearance.Go, GetUp.CheckClearance(far, GetUpParams.Standard));
        }

        [Fact]
        public void HostileInputsStayFiniteAndTotal()
        {
            var parameters = new GetUpParams
            {
                LyingHeadDrop = float.NaN,
                UprightHeadRise = float.PositiveInfinity,
                PoseDot = float.NaN,
                MaxAlignOffset = float.NaN,
                ClearDistance = float.NaN
            };
            var bodies = new RoleBody[Roles.Count];
            for (var i = 0; i < bodies.Length; i++)
                bodies[i] = new RoleBody
                {
                    Position = new Float3(float.NaN, float.PositiveInfinity, float.NegativeInfinity),
                    Rotation = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN)
                };
            Assert.Equal(RestingPose.Unknown, GetUp.Classify(bodies, parameters));
            var alignment = GetUp.ComputeAlignment(bodies, parameters);
            Assert.True(alignment.Position.IsFinite);
            Assert.True(Scalar.IsFinite(alignment.Yaw));
            var probes = new SupportProbe[] { new SupportProbe { Hit = 1, Distance = float.NaN } };
            Assert.Equal(GetUpClearance.Go, GetUp.CheckClearance(probes, parameters));
            var choice = GetUp.ChooseClip(RestingPose.Unknown, Array.Empty<GetUpClipEntry>());
            Assert.Equal(0, choice.HasClip);
        }
    }
}

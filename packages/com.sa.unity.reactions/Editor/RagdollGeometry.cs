using System.Collections.Generic;
using SA.Humanoid;
using SA.Humanoid.Mapping;
using SA.Humanoid.Profile;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using BoxCollider = Unity.Physics.BoxCollider;
using CapsuleCollider = Unity.Physics.CapsuleCollider;
using Collider = Unity.Physics.Collider;
using Material = Unity.Physics.Material;
using SkeletonBone = SA.Humanoid.Mapping.SkeletonBone;
using SphereCollider = Unity.Physics.SphereCollider;

namespace SA.Reactions.Editor
{
    struct RoleGeometry
    {
        public int Role;
        public int ParentRole;
        public BlobAssetReference<Collider> Shape;
        public float Mass;
        public float3 AnchorInParent;
        public quaternion JointFrameInParent;
        public quaternion JointFrameInChild;
        public float ConeAngle;
        public float2 TwistRange;
        public float Frequency;
        public float MaxTorque;
        public float TrackGain;
    }

    static class RagdollGeometry
    {
        static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };

        static readonly float[] Radius =
        {
            0f, 0f, 0f, 0.15f, 0.15f, 0.13f, 0.13f, 0f, 0f, 0.17f, 0.17f, 0.13f, 0.13f, 0f, 0f
        };

        static readonly float[] Frequency =
        {
            0f, 14f, 18f, 14f, 14f, 14f, 14f, 18f, 18f, 16f, 16f, 18f, 18f, 26f, 26f
        };

        static readonly float[] Track =
        {
            0.06f, 0.08f, 0.06f, 0.06f, 0.06f, 0.055f, 0.055f, 0.05f, 0.05f, 0.09f, 0.09f, 0.09f, 0.09f, 0.14f, 0.14f
        };

        static readonly float[] Torque =
        {
            0f, 450f, 70f, 140f, 140f, 80f, 80f, 30f, 30f, 520f, 520f, 380f, 380f, 260f, 260f
        };

        public static Transform[] Resolve(Transform root, Animator animator, Transform[] overrides, List<string> errors)
        {
            var result = new Transform[Roles.Count];
            var human = animator != null && animator.avatar != null && animator.avatar.isHuman;
            var all = new List<Transform>();
            Collect(root, all);
            var suggested = new int[Roles.Count];
            if (!human)
            {
                var skeleton = new SkeletonBone[all.Count];
                for (var i = 0; i < all.Count; i++)
                {
                    var parent = all[i] == root ? -1 : all.IndexOf(all[i].parent);
                    skeleton[i] = new SkeletonBone(all[i].name, parent, new SA.Motion.Numerics.Pose
                    {
                        Position = new SA.Motion.Numerics.Float3(all[i].localPosition.x, all[i].localPosition.y, all[i].localPosition.z),
                        Rotation = new SA.Motion.Numerics.Quaternion(all[i].localRotation.x, all[i].localRotation.y, all[i].localRotation.z, all[i].localRotation.w),
                        Scale = new SA.Motion.Numerics.Float3(1f, 1f, 1f)
                    });
                }
                suggested = HumanoidMapping.Suggest(skeleton);
                for (var role = 0; role < Roles.Count; role++)
                    if (overrides != null && role < overrides.Length && overrides[role] != null)
                        suggested[role] = all.IndexOf(overrides[role]);
                var report = HumanoidMapping.Map(skeleton, suggested);
                foreach (var diagnostic in report.Diagnostics)
                    errors.Add($"{diagnostic.Code} for role {diagnostic.Role}");
            }
            for (var role = 0; role < Roles.Count; role++)
            {
                if (overrides != null && role < overrides.Length && overrides[role] != null)
                    result[role] = overrides[role];
                else if (human)
                    result[role] = animator.GetBoneTransform(Bones[role]);
                else if (suggested[role] >= 0 && suggested[role] < all.Count)
                    result[role] = all[suggested[role]];
                if (result[role] == null && human && role == (int)Role.Torso)
                    result[role] = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
                if (result[role] == null)
                    errors.Add($"missing bone for role {(Role)role}");
            }
            return result;
        }

        static void Collect(Transform node, List<Transform> into)
        {
            into.Add(node);
            foreach (Transform child in node)
                Collect(child, into);
        }

        static Transform Toe(Transform foot)
        {
            foreach (Transform child in foot)
                if (child.name.ToLowerInvariant().Contains("toe"))
                    return child;
            return null;
        }

        static float3 Local(Transform bone, float3 world) => math.rotate(math.inverse((quaternion)bone.rotation), world - (float3)bone.position);

        static quaternion Basis(float3 x, float3 y)
        {
            var z = math.normalize(math.cross(x, y));
            return new quaternion(new float3x3(x, math.cross(z, x), z));
        }

        static Material Surface => new Material { Friction = 0.9f, Restitution = 0f, CollisionResponse = CollisionResponsePolicy.Collide };

        public static List<RoleGeometry> Build(Transform root, Transform[] bone, float totalMass, float gainScale, int group, List<string> errors)
        {
            var result = new List<RoleGeometry>();
            var filter = new CollisionFilter { BelongsTo = 2u, CollidesWith = 0xFFFFFFFFu, GroupIndex = group };
            var scale = math.max(0.3f, (bone[(int)Role.Head].position.y - root.position.y) / 1.7f);
            var up = new float3(0f, 1f, 0f);
            var position = new float3[Roles.Count];
            var rotation = new quaternion[Roles.Count];
            for (var role = 0; role < Roles.Count; role++)
            {
                position[role] = bone[role].position;
                rotation[role] = bone[role].rotation;
            }
            var lateral = math.normalizesafe(position[(int)Role.UpperArmRight] - position[(int)Role.UpperArmLeft], new float3(1f, 0f, 0f));
            var spineUp = math.normalizesafe(position[(int)Role.Head] - position[(int)Role.Pelvis], up);
            var forward = math.normalizesafe(math.cross(lateral, spineUp), new float3(0f, 0f, 1f));
            var toes = new float3[2];
            for (var side = 0; side < 2; side++)
            {
                var foot = position[side == 0 ? (int)Role.FootLeft : (int)Role.FootRight];
                var toeBone = Toe(bone[side == 0 ? (int)Role.FootLeft : (int)Role.FootRight]);
                toes[side] = toeBone != null ? (float3)toeBone.position : foot + forward * 0.15f * scale;
            }
            var end = new float3[Roles.Count];
            end[(int)Role.Pelvis] = position[(int)Role.Torso];
            end[(int)Role.Torso] = position[(int)Role.Head];
            end[(int)Role.Head] = position[(int)Role.Head] + spineUp * 0.22f * scale;
            for (var side = 0; side < 2; side++)
            {
                var upperArm = side == 0 ? Role.UpperArmLeft : Role.UpperArmRight;
                var forearm = side == 0 ? Role.ForearmLeft : Role.ForearmRight;
                var hand = side == 0 ? Role.HandLeft : Role.HandRight;
                var thigh = side == 0 ? Role.ThighLeft : Role.ThighRight;
                var shin = side == 0 ? Role.ShinLeft : Role.ShinRight;
                var foot = side == 0 ? Role.FootLeft : Role.FootRight;
                end[(int)upperArm] = position[(int)forearm];
                end[(int)forearm] = position[(int)hand];
                end[(int)hand] = position[(int)hand] + math.normalizesafe(position[(int)hand] - position[(int)forearm], lateral) * 0.09f * scale;
                end[(int)thigh] = position[(int)shin];
                end[(int)shin] = position[(int)foot];
                end[(int)foot] = toes[side];
            }
            var groundY = root.position.y;
            for (var role = 0; role < Roles.Count; role++)
            {
                var r = (Role)role;
                var transform = bone[role];
                var shape = default(BlobAssetReference<Collider>);
                var origin = position[role];
                var length = math.max(0.02f, math.length(end[role] - origin));
                if (r == Role.Pelvis || r == Role.Torso)
                {
                    var width = r == Role.Pelvis
                        ? math.distance(position[(int)Role.ThighLeft], position[(int)Role.ThighRight]) * 1.6f
                        : math.distance(position[(int)Role.UpperArmLeft], position[(int)Role.UpperArmRight]) * 0.95f;
                    var depth = (r == Role.Pelvis ? 0.2f : 0.22f) * scale;
                    var center = (origin + end[role]) * 0.5f;
                    var axis = math.normalizesafe(end[role] - origin, spineUp);
                    var basis = Basis(lateral, axis);
                    var worldBasis = basis;
                    shape = BoxCollider.Create(new BoxGeometry
                    {
                        Center = Local(transform, center),
                        Orientation = math.mul(math.inverse(rotation[role]), worldBasis),
                        Size = new float3(width, length + 0.04f * scale, depth),
                        BevelRadius = 0.01f * scale
                    }, filter, Surface);
                }
                else if (r == Role.Head)
                {
                    var center = origin + spineUp * 0.1f * scale;
                    shape = SphereCollider.Create(new SphereGeometry { Center = Local(transform, center), Radius = 0.11f * scale }, filter, Surface);
                }
                else if (r == Role.FootLeft || r == Role.FootRight)
                {
                    var flat = math.normalizesafe(new float3(end[role].x - origin.x, 0f, end[role].z - origin.z), forward);
                    var footLength = 0.26f * scale;
                    var footWidth = 0.1f * scale;
                    var height = math.max(0.05f, origin.y + 0.015f * scale - groundY);
                    var centerXZ = new float2(origin.x, origin.z) + new float2(flat.x, flat.z) * (0.5f * footLength - 0.08f * scale);
                    var center = new float3(centerXZ.x, groundY + height * 0.5f, centerXZ.y);
                    var worldBasis = Basis(flat, up);
                    shape = BoxCollider.Create(new BoxGeometry
                    {
                        Center = Local(transform, center),
                        Orientation = math.mul(math.inverse(rotation[role]), worldBasis),
                        Size = new float3(footLength, height, footWidth),
                        BevelRadius = 0.012f * scale
                    }, filter, Surface);
                }
                else
                {
                    var radius = r == Role.HandLeft || r == Role.HandRight ? 0.04f * scale : Radius[role] * length;
                    shape = CapsuleCollider.Create(new CapsuleGeometry { Vertex0 = float3.zero, Vertex1 = Local(transform, end[role]), Radius = radius }, filter, Surface);
                }
                var parent = r == Role.Pelvis ? -1 : (int)Roles.Parent(r);
                var item = new RoleGeometry
                {
                    Role = role,
                    ParentRole = parent,
                    Shape = shape,
                    Mass = HumanProfile.Mass(r, totalMass),
                    Frequency = Frequency[role] * gainScale,
                    MaxTorque = Torque[role] * gainScale * totalMass / 70f,
                    TrackGain = Track[role]
                };
                if (parent >= 0)
                {
                    var direction = math.normalizesafe(end[role] - origin, spineUp);
                    var helper = math.abs(direction.y) < 0.9f ? up : lateral;
                    var frame = Basis(direction, helper);
                    item.JointFrameInParent = math.mul(math.inverse(rotation[parent]), frame);
                    item.JointFrameInChild = math.mul(math.inverse(rotation[role]), frame);
                    item.AnchorInParent = math.rotate(math.inverse(rotation[parent]), origin - position[parent]);
                    var limit = HumanProfile.Of(r).Limit;
                    var hinge = r == Role.ForearmLeft || r == Role.ForearmRight || r == Role.ShinLeft || r == Role.ShinRight;
                    item.ConeAngle = hinge ? 1.0f : math.clamp(math.max(limit.Swing1Max, limit.Swing2Max), 0.35f, 1.1f);
                    item.TwistRange = hinge ? new float2(-0.4f, 0.4f) : new float2(math.clamp(limit.TwistMin, -0.9f, -0.1f), math.clamp(limit.TwistMax, 0.1f, 1.2f));
                }
                result.Add(item);
            }
            return result;
        }
    }
}

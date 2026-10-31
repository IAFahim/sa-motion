using System;
using SA.Humanoid.Mapping;
using SA.Humanoid.Profile;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.Humanoid.Build
{
    public static class RagdollBuilder
    {
        const float ZeroLengthThreshold = 0.001f;
        const float UniformScaleTolerance = 1e-4f;
        const float UpSwitchThreshold = 0.99f;

        public static RagdollLayout Build(SkeletonBone[]? skeleton, int[]? boneOfRole, float totalMass)
        {
            MappingReport report = HumanoidMapping.Map(skeleton, boneOfRole);
            SkeletonBone[] source = skeleton ?? Array.Empty<SkeletonBone>();
            int bones = source.Length;
            int[] map = report.BoneOfRole;
            BuildParams parameters = BuildParams.Standard;

            int[] attributed = new int[bones];
            for (int i = 0; i < bones; i++)
            {
                attributed[i] = -1;
            }
            for (int r = 0; r < Roles.Count; r++)
            {
                int b = map[r];
                if (b < 0 || b >= bones)
                {
                    continue;
                }
                int current = b;
                for (int step = 0; step <= bones; step++)
                {
                    if (current < 0 || current >= bones)
                    {
                        break;
                    }
                    if (attributed[current] < 0)
                    {
                        attributed[current] = r;
                    }
                    int parent = source[current].Parent;
                    if (parent == -1)
                    {
                        break;
                    }
                    if (parent < -1 || parent >= bones)
                    {
                        break;
                    }
                    current = parent;
                }
            }

            Frame[] world = new Frame[bones];
            float[] worldScale = new float[bones];
            bool[] settled = new bool[bones];
            bool[] scaleBad = new bool[bones];
            bool[] restBad = new bool[bones];
            for (int i = 0; i < bones; i++)
            {
                world[i] = Frame.Identity;
                worldScale[i] = 1f;
                scaleBad[i] = !TryUniformScale(source[i].Rest.Scale, out _);
                restBad[i] = !source[i].Rest.Position.IsFinite || !source[i].Rest.Rotation.IsValidRotation;
            }
            int[] climb = new int[bones];
            for (int i = 0; i < bones; i++)
            {
                if (settled[i])
                {
                    continue;
                }
                int depth = 0;
                int current = i;
                while (current >= 0 && current < bones && !settled[current] && depth < bones)
                {
                    climb[depth] = current;
                    depth++;
                    int parent = source[current].Parent;
                    if (parent == -1)
                    {
                        current = -1;
                        break;
                    }
                    if (parent < -1 || parent >= bones)
                    {
                        current = -1;
                        break;
                    }
                    current = parent;
                }
                Frame baseFrame = Frame.Identity;
                float baseScale = 1f;
                if (current >= 0 && current < bones && settled[current])
                {
                    baseFrame = world[current];
                    baseScale = worldScale[current];
                }
                for (int k = depth - 1; k >= 0; k--)
                {
                    int b = climb[k];
                    Pose rest = source[b].Rest;
                    Float3 localPosition = rest.Position.IsFinite ? rest.Position : Float3.Zero;
                    Quaternion localRotation = rest.Rotation.IsValidRotation ? Quaternion.Normalize(rest.Rotation) : Quaternion.Identity;
                    TryUniformScale(rest.Scale, out float uniform);
                    float composed = baseScale * uniform;
                    if (!Scalar.IsFinite(composed) || composed <= 0f)
                    {
                        composed = 1f;
                    }
                    Float3 position = baseFrame.Position + Quaternion.Rotate(baseFrame.Rotation, localPosition * baseScale);
                    if (!position.IsFinite)
                    {
                        position = baseFrame.Position;
                    }
                    Quaternion rotation = Quaternion.Normalize(Quaternion.Multiply(baseFrame.Rotation, localRotation));
                    world[b] = new Frame(position, rotation);
                    worldScale[b] = composed;
                    settled[b] = true;
                    baseFrame = world[b];
                    baseScale = composed;
                }
            }

            bool[] jointOk = new bool[Roles.Count];
            Float3[] jointPosition = new Float3[Roles.Count];
            for (int r = 0; r < Roles.Count; r++)
            {
                int b = map[r];
                if (b < 0 || b >= bones)
                {
                    jointPosition[r] = Float3.Zero;
                    jointOk[r] = false;
                    continue;
                }
                jointPosition[r] = world[b].Position;
                jointOk[r] = true;
            }

            BodyLayout[] bodies = new BodyLayout[Roles.Count];
            for (int r = 0; r < Roles.Count; r++)
            {
                bodies[r] = BuildBody((Role)r, jointOk, jointPosition, parameters, totalMass);
            }

            JointLayout[] joints = new JointLayout[Roles.Count - 1];
            for (int r = 1; r < Roles.Count; r++)
            {
                Role child = (Role)r;
                Role parent = Roles.Parent(child);
                int p = (int)parent;
                JointLimit limit = HumanProfile.Of(child).Limit;
                if (!jointOk[r] || p < 0 || p >= Roles.Count || !jointOk[p])
                {
                    joints[r - 1] = new JointLayout(child, parent, Float3.Zero, Float3.Zero, Quaternion.Identity, Quaternion.Identity, Quaternion.Identity, limit);
                    continue;
                }
                Float3 childJoint = jointPosition[r];
                Quaternion parentRotation = bodies[p].WorldFromBody.Rotation;
                Quaternion childRotation = bodies[r].WorldFromBody.Rotation;
                Quaternion inverseParent = Quaternion.InverseUnit(parentRotation);
                Quaternion inverseChild = Quaternion.InverseUnit(childRotation);
                Float3 anchorInParent = Quaternion.Rotate(inverseParent, childJoint - jointPosition[p]);
                Float3 anchorInChild = Quaternion.Rotate(inverseChild, childJoint - bodies[r].WorldFromBody.Position);
                Quaternion restRelative = Quaternion.Normalize(Quaternion.Multiply(inverseParent, childRotation));
                joints[r - 1] = new JointLayout(child, parent, anchorInParent, anchorInChild, restRelative, Quaternion.Identity, restRelative, limit);
            }

            Diagnostic[] scratch = new Diagnostic[report.Diagnostics.Length + 2 * bones + Roles.Count + 1];
            int diagnostics = 0;
            for (int i = 0; i < report.Diagnostics.Length; i++)
            {
                scratch[diagnostics] = report.Diagnostics[i];
                diagnostics++;
            }
            if (!Scalar.IsFinite(totalMass) || totalMass <= 0f)
            {
                scratch[diagnostics] = new Diagnostic(BuildDiagnostics.BadTotalMass, Role.Pelvis, -1);
                diagnostics++;
            }
            for (int i = 0; i < bones; i++)
            {
                if (attributed[i] < 0)
                {
                    continue;
                }
                if (scaleBad[i])
                {
                    scratch[diagnostics] = new Diagnostic(BuildDiagnostics.BadBoneScale, (Role)attributed[i], i);
                    diagnostics++;
                }
                if (restBad[i])
                {
                    scratch[diagnostics] = new Diagnostic(BuildDiagnostics.BadRestValue, (Role)attributed[i], i);
                    diagnostics++;
                }
            }
            for (int r = 0; r < Roles.Count; r++)
            {
                float extent = SegmentExtent((Role)r, jointOk, jointPosition, out bool valid);
                if (valid && extent < ZeroLengthThreshold)
                {
                    scratch[diagnostics] = new Diagnostic(BuildDiagnostics.ZeroLengthSegment, (Role)r, map[r]);
                    diagnostics++;
                }
            }
            for (int i = 1; i < diagnostics; i++)
            {
                Diagnostic key = scratch[i];
                int j = i - 1;
                while (j >= 0 && IsBefore(key, scratch[j]))
                {
                    scratch[j + 1] = scratch[j];
                    j--;
                }
                scratch[j + 1] = key;
            }
            Diagnostic[] ordered = new Diagnostic[diagnostics];
            for (int i = 0; i < diagnostics; i++)
            {
                ordered[i] = scratch[i];
            }
            return new RagdollLayout(bodies, joints, ordered, diagnostics == 0);
        }

        static BodyLayout BuildBody(Role role, bool[] jointOk, Float3[] jointPosition, BuildParams parameters, float totalMass)
        {
            int r = (int)role;
            bool self = r >= 0 && r < Roles.Count && jointOk[r];
            Float3 origin = self ? jointPosition[r] : Float3.Zero;
            float extent = SegmentExtent(role, jointOk, jointPosition, out _);

            Float3 axis = new Float3(1f, 0f, 0f);
            if (self)
            {
                bool found = false;
                for (int ci = 0; ci < ChildCount(role); ci++)
                {
                    Role child = ChildAt(role, ci);
                    int c = (int)child;
                    if (c < 0 || c >= Roles.Count || !jointOk[c])
                    {
                        continue;
                    }
                    Float3 delta = jointPosition[c] - origin;
                    float length = delta.Length;
                    if (length >= ZeroLengthThreshold && Scalar.IsFinite(length))
                    {
                        axis = delta / length;
                        found = true;
                        break;
                    }
                }
                if (!found && !Roles.IsRoot(role))
                {
                    Role parent = Roles.Parent(role);
                    int p = (int)parent;
                    if (p >= 0 && p < Roles.Count && jointOk[p])
                    {
                        Float3 delta = origin - jointPosition[p];
                        float length = delta.Length;
                        if (length >= ZeroLengthThreshold && Scalar.IsFinite(length))
                        {
                            axis = delta / length;
                        }
                    }
                }
            }

            Frame frame = new Frame(origin, BasisRotation(axis));
            float mass = HumanProfile.Mass(role, totalMass);
            Float3 center = new Float3(extent * 0.5f, 0f, 0f);
            if (BuildParams.IsBox(role))
            {
                Float3 half = new Float3(extent * 0.5f, parameters.HalfWidthFor(role) * extent, parameters.HalfDepthFor(role) * extent);
                return new BodyLayout(role, frame, Float3.Zero, Float3.Zero, 0f, half, mass, center);
            }
            float radius = parameters.RadiusFor(role) * extent;
            return new BodyLayout(role, frame, Float3.Zero, new Float3(extent, 0f, 0f), radius, Float3.Zero, mass, center);
        }

        static float SegmentExtent(Role role, bool[] jointOk, Float3[] jointPosition, out bool valid)
        {
            valid = false;
            int r = (int)role;
            if (r < 0 || r >= Roles.Count || !jointOk[r])
            {
                return 0f;
            }
            Float3 origin = jointPosition[r];
            Role? primary = PrimaryChild(role);
            if (primary.HasValue)
            {
                int c = (int)primary.Value;
                if (c < 0 || c >= Roles.Count || !jointOk[c])
                {
                    return 0f;
                }
                float primaryLength = Distance(origin, jointPosition[c]);
                if (!Scalar.IsFinite(primaryLength))
                {
                    return 0f;
                }
                valid = true;
                return primaryLength;
            }
            if (ChildCount(role) == 0)
            {
                if (Roles.IsRoot(role))
                {
                    return 0f;
                }
                int p = (int)Roles.Parent(role);
                if (p < 0 || p >= Roles.Count || !jointOk[p])
                {
                    return 0f;
                }
                float parentLength = Distance(origin, jointPosition[p]);
                if (!Scalar.IsFinite(parentLength))
                {
                    return 0f;
                }
                valid = true;
                return parentLength;
            }
            for (int ci = 0; ci < ChildCount(role); ci++)
            {
                int c = (int)ChildAt(role, ci);
                if (c < 0 || c >= Roles.Count || !jointOk[c])
                {
                    continue;
                }
                float length = Distance(origin, jointPosition[c]);
                if (!Scalar.IsFinite(length))
                {
                    continue;
                }
                valid = true;
                return length;
            }
            return 0f;
        }

        static Role? PrimaryChild(Role role)
        {
            switch (role)
            {
                case Role.Pelvis: return Role.Torso;
                case Role.Torso: return Role.Head;
                case Role.UpperArmLeft: return Role.ForearmLeft;
                case Role.UpperArmRight: return Role.ForearmRight;
                case Role.ForearmLeft: return Role.HandLeft;
                case Role.ForearmRight: return Role.HandRight;
                case Role.ThighLeft: return Role.ShinLeft;
                case Role.ThighRight: return Role.ShinRight;
                case Role.ShinLeft: return Role.FootLeft;
                case Role.ShinRight: return Role.FootRight;
                default: return null;
            }
        }

        static int ChildCount(Role role)
        {
            switch (role)
            {
                case Role.Pelvis: return 3;
                case Role.Torso: return 3;
                case Role.UpperArmLeft:
                case Role.UpperArmRight:
                case Role.ForearmLeft:
                case Role.ForearmRight:
                case Role.ThighLeft:
                case Role.ThighRight:
                case Role.ShinLeft:
                case Role.ShinRight:
                    return 1;
                default:
                    return 0;
            }
        }

        static Role ChildAt(Role role, int index)
        {
            switch (role)
            {
                case Role.Pelvis:
                    switch (index)
                    {
                        case 0: return Role.Torso;
                        case 1: return Role.ThighLeft;
                        default: return Role.ThighRight;
                    }
                case Role.Torso:
                    switch (index)
                    {
                        case 0: return Role.Head;
                        case 1: return Role.UpperArmLeft;
                        default: return Role.UpperArmRight;
                    }
                case Role.UpperArmLeft: return Role.ForearmLeft;
                case Role.UpperArmRight: return Role.ForearmRight;
                case Role.ForearmLeft: return Role.HandLeft;
                case Role.ForearmRight: return Role.HandRight;
                case Role.ThighLeft: return Role.ShinLeft;
                case Role.ThighRight: return Role.ShinRight;
                case Role.ShinLeft: return Role.FootLeft;
                case Role.ShinRight: return Role.FootRight;
                default: return Role.Pelvis;
            }
        }

        static float Distance(Float3 a, Float3 b) => (a - b).Length;

        static bool TryUniformScale(Float3 scale, out float uniform)
        {
            uniform = 1f;
            if (!scale.IsFinite)
            {
                return false;
            }
            if (scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f)
            {
                return false;
            }
            float max = scale.X;
            if (scale.Y > max)
            {
                max = scale.Y;
            }
            if (scale.Z > max)
            {
                max = scale.Z;
            }
            float min = scale.X;
            if (scale.Y < min)
            {
                min = scale.Y;
            }
            if (scale.Z < min)
            {
                min = scale.Z;
            }
            if (max - min > UniformScaleTolerance * max)
            {
                return false;
            }
            uniform = (scale.X + scale.Y + scale.Z) / 3f;
            return true;
        }

        static Quaternion BasisRotation(Float3 axis)
        {
            Float3 x = new Float3(1f, 0f, 0f);
            if (axis.IsFinite && axis.LengthSquared >= 0.25f)
            {
                x = axis / axis.Length;
            }
            Float3 reference = Scalar.Abs(x.Y) > UpSwitchThreshold ? new Float3(0f, 0f, 1f) : new Float3(0f, 1f, 0f);
            Float3 z = Float3.Cross(x, reference);
            float zLength = z.Length;
            if (!Scalar.IsFinite(zLength) || zLength <= 1e-6f)
            {
                z = Float3.Cross(x, new Float3(0f, 0f, 1f));
                zLength = z.Length;
                if (!Scalar.IsFinite(zLength) || zLength <= 1e-6f)
                {
                    return Quaternion.Identity;
                }
            }
            z = z / zLength;
            Float3 y = Float3.Cross(z, x);
            return BasisToQuaternion(x, y, z);
        }

        static Quaternion BasisToQuaternion(Float3 x, Float3 y, Float3 z)
        {
            float m00 = x.X;
            float m01 = y.X;
            float m02 = z.X;
            float m10 = x.Y;
            float m11 = y.Y;
            float m12 = z.Y;
            float m20 = x.Z;
            float m21 = y.Z;
            float m22 = z.Z;
            float trace = m00 + m11 + m22;
            float qx;
            float qy;
            float qz;
            float qw;
            if (trace > 0f)
            {
                float s = (float)Math.Sqrt(trace + 1f) * 2f;
                qw = 0.25f * s;
                qx = (m21 - m12) / s;
                qy = (m02 - m20) / s;
                qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                qw = (m21 - m12) / s;
                qx = 0.25f * s;
                qy = (m01 + m10) / s;
                qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                qw = (m02 - m20) / s;
                qx = (m01 + m10) / s;
                qy = 0.25f * s;
                qz = (m12 + m21) / s;
            }
            else
            {
                float s = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                qw = (m10 - m01) / s;
                qx = (m02 + m20) / s;
                qy = (m12 + m21) / s;
                qz = 0.25f * s;
            }
            return Quaternion.Normalize(new Quaternion(qx, qy, qz, qw));
        }

        static bool IsBefore(Diagnostic a, Diagnostic b)
        {
            if (a.Role != b.Role)
            {
                return (int)a.Role < (int)b.Role;
            }
            return (int)a.Code < (int)b.Code;
        }
    }
}

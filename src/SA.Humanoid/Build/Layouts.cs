using System.Runtime.InteropServices;
using SA.Humanoid.Mapping;
using SA.Humanoid.Profile;
using SA.Motion.Numerics;
using SA.Motion.Mechanics;

namespace SA.Humanoid.Build
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BodyLayout
    {
        public readonly Role Role;
        public readonly Frame WorldFromBody;
        public readonly Float3 CapsuleStart;
        public readonly Float3 CapsuleEnd;
        public readonly float Radius;
        public readonly Float3 BoxHalfExtents;
        public readonly float Mass;
        public readonly Float3 CenterOfMass;

        public BodyLayout(Role role, Frame worldFromBody, Float3 capsuleStart, Float3 capsuleEnd, float radius, Float3 boxHalfExtents, float mass, Float3 centerOfMass)
        {
            Role = role;
            WorldFromBody = worldFromBody;
            CapsuleStart = capsuleStart;
            CapsuleEnd = capsuleEnd;
            Radius = radius;
            BoxHalfExtents = boxHalfExtents;
            Mass = mass;
            CenterOfMass = centerOfMass;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct JointLayout
    {
        public readonly Role Child;
        public readonly Role Parent;
        public readonly Float3 AnchorInParent;
        public readonly Float3 AnchorInChild;
        public readonly Quaternion JointFrameInParent;
        public readonly Quaternion JointFrameInChild;
        public readonly Quaternion RestRelative;
        public readonly JointLimit Limit;

        public JointLayout(Role child, Role parent, Float3 anchorInParent, Float3 anchorInChild, Quaternion jointFrameInParent, Quaternion jointFrameInChild, Quaternion restRelative, JointLimit limit)
        {
            Child = child;
            Parent = parent;
            AnchorInParent = anchorInParent;
            AnchorInChild = anchorInChild;
            JointFrameInParent = jointFrameInParent;
            JointFrameInChild = jointFrameInChild;
            RestRelative = restRelative;
            Limit = limit;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RagdollLayout
    {
        public readonly BodyLayout[] Bodies;
        public readonly JointLayout[] Joints;
        public readonly Diagnostic[] Diagnostics;
        public readonly bool IsValid;

        public RagdollLayout(BodyLayout[]? bodies, JointLayout[]? joints, Diagnostic[]? diagnostics, bool isValid)
        {
            Bodies = bodies ?? System.Array.Empty<BodyLayout>();
            Joints = joints ?? System.Array.Empty<JointLayout>();
            Diagnostics = diagnostics ?? System.Array.Empty<Diagnostic>();
            IsValid = isValid;
        }
    }
}

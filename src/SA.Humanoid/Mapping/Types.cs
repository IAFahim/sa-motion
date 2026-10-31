using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Humanoid.Mapping
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SkeletonBone
    {
        public readonly string Name;
        public readonly int Parent;
        public readonly Pose Rest;

        public SkeletonBone(string? name, int parent, Pose rest)
        {
            Name = name ?? string.Empty;
            Parent = parent;
            Rest = rest;
        }
    }

    public enum DiagnosticCode : byte
    {
        RoleMissing = 0,
        BoneUsedByTwoRoles = 1,
        ParentIndexOutOfRange = 2,
        ParentCycle = 3,
        MapIndexOutOfRange = 4,
        RoleNotBelowItsParentRole = 5,
        WrongMapLength = 6
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Diagnostic
    {
        public readonly DiagnosticCode Code;
        public readonly Role Role;
        public readonly int Bone;

        public Diagnostic(DiagnosticCode code, Role role, int bone)
        {
            Code = code;
            Role = role;
            Bone = bone;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MappingReport
    {
        public readonly int[] BoneOfRole;
        public readonly Diagnostic[] Diagnostics;
        public readonly bool IsValid;

        public MappingReport(int[]? boneOfRole, Diagnostic[]? diagnostics, bool isValid)
        {
            BoneOfRole = boneOfRole ?? Array.Empty<int>();
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
            IsValid = isValid;
        }
    }
}

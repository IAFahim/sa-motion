using System;
using System.Runtime.InteropServices;
using SA.Motion.Kinematics;
using SA.Motion.Numerics;

namespace SA.Animation.Content
{
    public sealed class MappingAssignmentResult
    {
        public int[] BoneOfRole { get; }
        public byte[] Ambiguous { get; }
        public long Score { get; }
        internal MappingAssignmentResult(int[] boneOfRole, byte[] ambiguous, long score) { BoneOfRole = boneOfRole; Ambiguous = ambiguous; Score = score; }
    }

    public static class MappingAssignment
    {
        public static AnimationError Solve(ReadOnlySpan<int> scores, int roles, int bones, int ambiguityGap, out MappingAssignmentResult? result)
        {
            result = null;
            if (roles <= 0 || roles > 128 || bones <= 0 || bones > SampleLocator.MaxBones || scores.Length != (long)roles * bones || ambiguityGap < 0)
                return AnimationError.InvalidFrame;
            for (var i = 0; i < scores.Length; i++)
                if (scores[i] > 1000000) return AnimationError.InvalidFrame;
            var mapping = new int[roles];
            var ambiguous = new byte[roles];
            var score = Assign(scores, roles, bones, -1, -1, mapping);
            var alternative = new int[roles];
            for (var i = 0; i < roles; i++)
                if (mapping[i] >= 0 && score - Assign(scores, roles, bones, i, mapping[i], alternative) < ambiguityGap)
                    ambiguous[i] = 1;
            result = new MappingAssignmentResult(mapping, ambiguous, score);
            return AnimationError.None;
        }

        static long Assign(ReadOnlySpan<int> scores, int roles, int bones, int forbiddenRole, int forbiddenBone, Span<int> mapping)
        {
            var columns = bones + roles;
            const long infinity = 1000000000000L;
            var u = new long[roles + 1];
            var v = new long[columns + 1];
            var owner = new int[columns + 1];
            var previous = new int[columns + 1];
            var minimum = new long[columns + 1];
            var used = new bool[columns + 1];
            for (var role = 1; role <= roles; role++)
            {
                owner[0] = role;
                var column = 0;
                for (var j = 0; j <= columns; j++) { minimum[j] = infinity; used[j] = false; }
                do
                {
                    used[column] = true;
                    var currentRole = owner[column];
                    var delta = infinity;
                    var next = 0;
                    for (var j = 1; j <= columns; j++)
                    {
                        if (used[j]) continue;
                        var candidate = j <= bones ? scores[(currentRole - 1) * bones + j - 1] : 0;
                        var cost = j > bones ? 0 : candidate <= 0 || currentRole - 1 == forbiddenRole && j - 1 == forbiddenBone ? infinity / 2 : -(long)candidate;
                        var reduced = cost - u[currentRole] - v[j];
                        if (reduced < minimum[j]) { minimum[j] = reduced; previous[j] = column; }
                        if (minimum[j] < delta) { delta = minimum[j]; next = j; }
                    }
                    for (var j = 0; j <= columns; j++)
                        if (used[j]) { u[owner[j]] += delta; v[j] -= delta; }
                        else minimum[j] -= delta;
                    column = next;
                }
                while (owner[column] != 0);
                do
                {
                    var next = previous[column];
                    owner[column] = owner[next];
                    column = next;
                }
                while (column != 0);
            }
            for (var i = 0; i < roles; i++) mapping[i] = -1;
            var total = 0L;
            for (var j = 1; j <= bones; j++)
                if (owner[j] != 0)
                {
                    var role = owner[j] - 1;
                    mapping[role] = j - 1;
                    total += scores[role * bones + j - 1];
                }
            return total;
        }
    }

    public static class CalibrationFrame
    {
        public static bool TryRotation(Float3 localPrimary, Float3 localSecondary, Float3 desiredPrimary, Float3 desiredSecondary, out Quaternion rotation)
        {
            rotation = Quaternion.Identity;
            if (!Unit(localPrimary, out var local) || !Unit(desiredPrimary, out var desired) || !localSecondary.IsFinite || !desiredSecondary.IsFinite
                || !Unit(localSecondary - local * Float3.Dot(localSecondary, local), out var localSide)
                || !Unit(desiredSecondary - desired * Float3.Dot(desiredSecondary, desired), out var desiredSide))
                return false;
            var swing = TwoBoneIK.FromTo(local, desired);
            var side = Quaternion.Rotate(swing, localSide);
            var angle = (float)Math.Atan2(Float3.Dot(desired, Float3.Cross(side, desiredSide)), Float3.Dot(side, desiredSide));
            rotation = Quaternion.Normalize(Quaternion.Multiply(RotationVector.Exp(desired * angle), swing));
            return true;
        }

        static bool Unit(Float3 value, out Float3 unit)
        {
            unit = Float3.Zero;
            var length = value.Length;
            if (!value.IsFinite || !Scalar.IsFinite(length) || length <= 1e-5f) return false;
            unit = value / length;
            return true;
        }
    }
}

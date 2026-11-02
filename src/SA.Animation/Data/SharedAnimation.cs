using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public enum AnimationError : byte
    {
        None, InvalidFrame, InvalidHandle, FamilyMismatch, NeedsData, CapacityExceeded,
        BudgetExceeded, TimeOutOfRange, TickConflict, ContinuityMisuse, UnsupportedScale,
        UnsupportedTranslationChannel, InvalidTable, InvalidFormat, CorruptData, AliasedBuffers
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct AssetId : IEquatable<AssetId>
    {
        public readonly ulong A;
        public readonly ulong B;
        public readonly ulong C;
        public readonly ulong D;
        public AssetId(ulong a, ulong b, ulong c, ulong d) { A = a; B = b; C = c; D = d; }
        public bool IsEmpty => (A | B | C | D) == 0;
        public bool Equals(AssetId other) => A == other.A && B == other.B && C == other.C && D == other.D;
        public override bool Equals(object? other) => other is AssetId id && Equals(id);
        public override int GetHashCode() => unchecked((int)(A ^ B ^ C ^ D));
        public static bool operator ==(AssetId a, AssetId b) => a.Equals(b);
        public static bool operator !=(AssetId a, AssetId b) => !a.Equals(b);
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RigidPose
    {
        public readonly Float3 Position;
        public readonly Quaternion Rotation;
        public RigidPose(Float3 position, Quaternion rotation) { Position = position; Rotation = rotation; }
        public static RigidPose Identity => new RigidPose(Float3.Zero, Quaternion.Identity);
        public bool IsValid => Position.IsFinite && Rotation.IsValidRotation;
        public Pose ToPose() => new Pose { Position = Position, Rotation = Rotation, Scale = Float3.One };
    }

    public static class RigidMath
    {
        public static bool UnitPose(in Pose pose) => pose.Position.IsFinite && UnitRotation(pose.Rotation)
            && pose.Scale.X == 1f && pose.Scale.Y == 1f && pose.Scale.Z == 1f;

        public static bool UnitRotation(Quaternion q) => q.IsValidRotation && Math.Abs(Quaternion.Dot(q, q) - 1f) <= 1e-3f;

        public static bool TryCompose(RigidPose a, RigidPose b, out RigidPose result)
        {
            result = RigidPose.Identity;
            if (!a.IsValid || !b.IsValid)
                return false;
            var aq = Quaternion.Normalize(a.Rotation);
            var position = a.Position + Quaternion.Rotate(aq, b.Position);
            if (!position.IsFinite)
                return false;
            result = new RigidPose(position, Quaternion.Normalize(Quaternion.Multiply(aq, Quaternion.Normalize(b.Rotation))));
            return true;
        }

        public static bool TryInverse(RigidPose pose, out RigidPose result)
        {
            result = RigidPose.Identity;
            if (!pose.IsValid)
                return false;
            var rotation = Quaternion.InverseUnit(Quaternion.Normalize(pose.Rotation));
            var position = Quaternion.Rotate(rotation, pose.Position * -1f);
            if (!position.IsFinite)
                return false;
            result = new RigidPose(position, rotation);
            return true;
        }

        public static AnimationError Model(ReadOnlySpan<int> parents, ReadOnlySpan<Pose> local, Span<RigidPose> model)
        {
            if (parents.Length == 0 || parents.Length != local.Length || model.Length < local.Length)
                return AnimationError.InvalidTable;
            for (var i = 0; i < local.Length; i++)
                if (parents[i] < -1 || parents[i] >= i || !UnitPose(local[i]))
                    return AnimationError.InvalidTable;
            for (var i = 0; i < local.Length; i++)
            {
                var pose = new RigidPose(local[i].Position, Quaternion.Normalize(local[i].Rotation));
                if (parents[i] >= 0 && !TryCompose(model[parents[i]], pose, out pose))
                    return AnimationError.InvalidTable;
                model[i] = pose;
            }
            return AnimationError.None;
        }
    }
}

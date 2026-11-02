using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SA.Motion.Numerics;

namespace SA.Animation.Content
{
    public static class ContentIdentity
    {
        public static AnimationError Skeleton(string familyName, uint version, ReadOnlySpan<string> names, ReadOnlySpan<int> parents, ReadOnlySpan<Pose> reference, out AssetId id)
        {
            id = default;
            if (string.IsNullOrWhiteSpace(familyName) || version == 0 || names.Length != parents.Length || parents.Length == 0 || parents.Length > SampleLocator.MaxBones || reference.Length != parents.Length)
                return AnimationError.InvalidTable;
            var models = new RigidPose[parents.Length];
            if (parents[0] != -1 || !SharedClipReader.RootIdentity(new RigidPose(reference[0].Position, reference[0].Rotation))
                || RigidMath.Model(parents, reference, models) != AnimationError.None) return AnimationError.InvalidTable;
            for (var i = 1; i < parents.Length; i++) if (parents[i] < 0) return AnimationError.InvalidTable;
            for (var i = 0; i < names.Length; i++)
            {
                if (!Text(names[i])) return AnimationError.InvalidTable;
                for (var j = 0; j < i; j++) if (string.Equals(names[i].Normalize(NormalizationForm.FormC), names[j].Normalize(NormalizationForm.FormC), StringComparison.Ordinal)) return AnimationError.InvalidTable;
            }
            if (!Text(familyName)) return AnimationError.InvalidTable;
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
            String(writer, "SA.Family.1");
            String(writer, familyName);
            writer.Write(version);
            writer.Write(parents.Length);
            for (var i = 0; i < parents.Length; i++) { String(writer, names[i]); writer.Write(parents[i]); Pose(writer, reference[i]); }
            writer.Flush();
            id = Hash(bytes.ToArray());
            return AnimationError.None;
        }

        public static AnimationError Motion(AssetId family, ReadOnlySpan<Pose> samples, int bones, int sampleCount, double duration, bool loop, RootPolicy rootPolicy,
            ReadOnlySpan<RigidPose> root, ReadOnlySpan<Pose> reference, ReadOnlySpan<float> contacts, out AssetId id)
        {
            id = default;
            if (family.IsEmpty || bones <= 0 || bones > SampleLocator.MaxBones || samples.Length != (long)bones * sampleCount || root.Length != sampleCount
                || SampleLocator.Locate(duration, sampleCount, 0, false, out _) != AnimationError.None || rootPolicy > RootPolicy.FullRigid
                || (reference.Length != 0 && reference.Length != bones) || (contacts.Length != 0 && contacts.Length != (long)sampleCount * 4))
                return AnimationError.InvalidFrame;
            for (var i = 0; i < samples.Length; i++) if (!RigidMath.UnitPose(samples[i]) || (i % bones == 0 && !SharedClipReader.RootIdentity(new RigidPose(samples[i].Position, samples[i].Rotation)))) return AnimationError.InvalidFrame;
            for (var i = 0; i < reference.Length; i++) if (!RigidMath.UnitPose(reference[i])) return AnimationError.InvalidFrame;
            for (var i = 0; i < root.Length; i++) if (!SharedClipReader.ValidRoot(root[i], rootPolicy) || (i == 0 && !SharedClipReader.RootIdentity(root[i]))) return AnimationError.InvalidFrame;
            for (var i = 0; i < contacts.Length; i++) if (!Scalar.IsFinite(contacts[i]) || contacts[i] < 0 || contacts[i] > 1) return AnimationError.InvalidFrame;
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
            String(writer, "SA.MotionPayload.1");
            Id(writer, family);
            writer.Write(bones);
            writer.Write(sampleCount);
            writer.Write(duration == 0 ? 0d : duration);
            writer.Write(loop ? 1u : 0u);
            writer.Write((uint)rootPolicy);
            for (var i = 0; i < samples.Length; i++) Pose(writer, samples[i]);
            for (var i = 0; i < root.Length; i++) { Vector(writer, root[i].Position); Rotation(writer, root[i].Rotation); }
            writer.Write(reference.Length);
            for (var i = 0; i < reference.Length; i++) Pose(writer, reference[i]);
            writer.Write(contacts.Length);
            for (var i = 0; i < contacts.Length; i++) Number(writer, contacts[i]);
            writer.Flush();
            id = Hash(bytes.ToArray());
            return AnimationError.None;
        }

        public static AnimationError BakeKey(AssetId sourceDependency, long sourceClipId, AssetId sourceAvatar, AssetId family, AssetId profile, string fullEditorVersion, out AssetId id)
        {
            id = default;
            if (sourceDependency.IsEmpty || family.IsEmpty || profile.IsEmpty || !Text(fullEditorVersion)) return AnimationError.InvalidFrame;
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
            String(writer, "SA.BakeRequest.1");
            Id(writer, sourceDependency);
            writer.Write(sourceClipId);
            Id(writer, sourceAvatar);
            Id(writer, family);
            Id(writer, profile);
            String(writer, fullEditorVersion);
            writer.Flush();
            id = Hash(bytes.ToArray());
            return AnimationError.None;
        }

        public static AssetId Hash(ReadOnlySpan<byte> data)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(data);
            return ContentWire.Id(hash.GetHashAndReset());
        }

        static bool Text(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (var i = 0; i < text.Length; i++)
                if (char.IsSurrogate(text[i]))
                {
                    if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                    i++;
                }
            return true;
        }

        static void String(BinaryWriter writer, string text) { var bytes = Encoding.UTF8.GetBytes(text.Normalize(NormalizationForm.FormC)); writer.Write(bytes.Length); writer.Write(bytes); }
        static void Id(BinaryWriter writer, AssetId id) { writer.Write(id.A); writer.Write(id.B); writer.Write(id.C); writer.Write(id.D); }
        static void Number(BinaryWriter writer, float value) => writer.Write(value == 0f ? 0f : value);
        static void Vector(BinaryWriter writer, Float3 value) { Number(writer, value.X); Number(writer, value.Y); Number(writer, value.Z); }
        static void Rotation(BinaryWriter writer, Quaternion value) { var q = Quaternion.Canonical(value); Number(writer, q.X); Number(writer, q.Y); Number(writer, q.Z); Number(writer, q.W); }
        static void Pose(BinaryWriter writer, in Pose pose) { Vector(writer, pose.Position); Rotation(writer, pose.Rotation); }
    }
}

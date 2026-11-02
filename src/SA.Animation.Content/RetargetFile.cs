using System;
using System.Buffers.Binary;
using SA.Motion.Numerics;

namespace SA.Animation.Content
{
    public static class RetargetFile
    {
        public static AnimationError Encode(in RetargetBinding binding, ReadOnlySpan<int> canonicalParents, ReadOnlySpan<Pose> canonicalReference,
            ReadOnlySpan<RetargetRow> rows, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            var error = Recompile(binding.Family, binding.Target, canonicalParents, canonicalReference, rows, binding.SourceHips, binding.TargetHips, binding.HeightRatio, out var compiled, out var verified);
            if (error != AnimationError.None || !Equivalent(binding.SourceHipReference, verified.SourceHipReference) || !Equivalent(binding.TargetHipReference, verified.TargetHipReference))
                return error == AnimationError.None ? AnimationError.InvalidTable : error;
            var meta = new byte[64];
            BinaryPrimitives.WriteUInt32LittleEndian(meta, (uint)rows.Length);
            ContentWire.Single(meta.AsSpan(8), binding.HeightRatio);
            ContentWire.Id(meta.AsSpan(16), binding.Target);
            var bones = new byte[rows.Length * 64];
            var corrections = new byte[rows.Length * 16];
            var pre = new byte[rows.Length * 16];
            for (var i = 0; i < rows.Length; i++)
            {
                var row = compiled[i];
                var data = bones.AsSpan(i * 64, 64);
                BinaryPrimitives.WriteInt32LittleEndian(data, row.Parent);
                BinaryPrimitives.WriteInt32LittleEndian(data.Slice(4), row.Source);
                BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(8), (uint)row.Mode);
                BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(12), i == binding.TargetHips ? 1u : 0u);
                ContentWire.Vector(data.Slice(16), row.Calibration.Position);
                ContentWire.Rotation(data.Slice(28), Quaternion.Canonical(row.Calibration.Rotation));
                ContentWire.Rotation(corrections.AsSpan(i * 16), Quaternion.Canonical(row.Correction));
                ContentWire.Rotation(pre.AsSpan(i * 16), Quaternion.Canonical(row.PreRotation));
            }
            return SacaFile.Create(SacaKind.Retarget, binding.Family, new[]
            {
                new ChunkPayload(SacaContainer.Meta, meta, 1, 64),
                new ChunkPayload(SacaContainer.Bones, bones, (uint)rows.Length, 64),
                new ChunkPayload(SacaContainer.Corrections, corrections, (uint)rows.Length, 16),
                new ChunkPayload(SacaContainer.PreRotations, pre, (uint)rows.Length, 16)
            }, out bytes);
        }

        public static AnimationError Load(ReadOnlySpan<byte> bytes, AssetId expectedFamily, AssetId expectedTarget,
            ReadOnlySpan<int> canonicalParents, ReadOnlySpan<Pose> canonicalReference, out RetargetBinding binding, out RetargetRow[] rows)
        {
            binding = default;
            rows = Array.Empty<RetargetRow>();
            Span<SacaChunk> directory = stackalloc SacaChunk[64];
            var error = SacaFile.Verify(bytes, directory, out var chunksCount, out var header);
            if (error != AnimationError.None) return error;
            if (header.Family != expectedFamily) return AnimationError.FamilyMismatch;
            var chunks = directory.Slice(0, chunksCount);
            if (header.Kind != SacaKind.Retarget || !SharedClipReader.Required(chunks, SacaContainer.Meta, 1, 64, out var metaChunk))
                return AnimationError.InvalidFormat;
            var meta = bytes.Slice(metaChunk.Offset, 64);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(meta);
            var ratio = ContentWire.Single(meta.Slice(8));
            var target = ContentWire.Id(meta.Slice(16));
            if (target.IsEmpty || target != expectedTarget) return AnimationError.InvalidTable;
            if (count == 0 || count > SampleLocator.MaxBones || BinaryPrimitives.ReadUInt32LittleEndian(meta.Slice(4)) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(meta.Slice(12)) != 0 || !ContentWire.Zero(meta.Slice(48))
                || !Scalar.IsFinite(ratio) || ratio <= 0
                || !SharedClipReader.Required(chunks, SacaContainer.Bones, count, 64, out var boneChunk)
                || !SharedClipReader.Required(chunks, SacaContainer.Corrections, count, 16, out var correctionChunk)
                || !SharedClipReader.Required(chunks, SacaContainer.PreRotations, count, 16, out var preChunk))
                return AnimationError.InvalidFormat;
            var decoded = new RetargetRow[count];
            var targetHips = -1;
            var sourceHips = -1;
            for (var i = 0; i < decoded.Length; i++)
            {
                var data = bytes.Slice(boneChunk.Offset + i * 64, 64);
                var mode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8));
                var flags = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12));
                if (mode > 2 || flags > 1 || !ContentWire.Zero(data.Slice(44))) return AnimationError.InvalidFormat;
                var source = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4));
                if (flags == 1)
                {
                    if (targetHips >= 0 || i == 0) return AnimationError.InvalidTable;
                    targetHips = i;
                    sourceHips = source;
                }
                var calibration = new Pose { Position = ContentWire.Vector(data.Slice(16)), Rotation = ContentWire.Rotation(data.Slice(28)), Scale = Float3.One };
                decoded[i] = new RetargetRow(BinaryPrimitives.ReadInt32LittleEndian(data), source, (RetargetRowMode)mode, calibration,
                    ContentWire.Rotation(bytes.Slice(correctionChunk.Offset + i * 16)), ContentWire.Rotation(bytes.Slice(preChunk.Offset + i * 16)));
            }
            return Recompile(expectedFamily, target, canonicalParents, canonicalReference, decoded, sourceHips, targetHips, ratio, out rows, out binding);
        }

        static AnimationError Recompile(AssetId family, AssetId target, ReadOnlySpan<int> canonicalParents, ReadOnlySpan<Pose> canonicalReference,
            ReadOnlySpan<RetargetRow> rows, int sourceHips, int targetHips, float ratio, out RetargetRow[] compiled, out RetargetBinding binding)
        {
            compiled = Array.Empty<RetargetRow>();
            binding = default;
            if (rows.Length == 0 || rows.Length > SampleLocator.MaxBones || canonicalParents.Length > SampleLocator.MaxBones)
                return AnimationError.InvalidTable;
            var parents = new int[rows.Length];
            var calibration = new Pose[rows.Length];
            var map = new int[rows.Length];
            for (var i = 0; i < rows.Length; i++) { parents[i] = rows[i].Parent; calibration[i] = rows[i].Calibration; map[i] = rows[i].Source; }
            var result = new RetargetRow[rows.Length];
            var scratch = new RigidPose[canonicalParents.Length + rows.Length];
            var error = RetargetTable.Compile(family, target, canonicalParents, canonicalReference, parents, calibration, map, sourceHips, targetHips, ratio, scratch, result, out var verified);
            if (error != AnimationError.None) return error;
            for (var i = 0; i < result.Length; i++)
            {
                var general = rows[i].Mode == RetargetRowMode.ModelMapped && result[i].Mode == RetargetRowMode.FastLocal;
                if ((!general && result[i].Mode != rows[i].Mode) || !Equivalent(result[i].Correction, rows[i].Correction)
                    || !Equivalent(general ? Quaternion.Identity : result[i].PreRotation, rows[i].PreRotation))
                    return AnimationError.InvalidTable;
                if (general) result[i] = new RetargetRow(result[i].Parent, result[i].Source, RetargetRowMode.ModelMapped, result[i].Calibration, result[i].Correction, Quaternion.Identity);
            }
            compiled = result;
            binding = verified;
            return AnimationError.None;
        }

        static bool Equivalent(Float3 a, Float3 b) => a.IsFinite && b.IsFinite && (a - b).Length <= 1e-5f;
        static bool Equivalent(Quaternion a, Quaternion b) => RigidMath.UnitRotation(a) && RigidMath.UnitRotation(b)
            && RotationVector.Log(Quaternion.Multiply(a, Quaternion.InverseUnit(b))).Length <= 1e-5f;
    }
}

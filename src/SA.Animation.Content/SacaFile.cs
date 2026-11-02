using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation.Content
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ChunkPayload
    {
        public readonly uint Tag;
        public readonly uint Flags;
        public readonly byte[] Bytes;
        public readonly uint Count;
        public readonly uint Stride;
        public ChunkPayload(uint tag, byte[] bytes, uint count = 0, uint stride = 0, bool required = true)
        { Tag = tag; Flags = required ? 1u : 0u; Bytes = bytes; Count = count; Stride = stride; }
    }

    public static class SacaFile
    {
        public static AnimationError Create(SacaKind kind, AssetId family, ReadOnlySpan<ChunkPayload> chunks, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (family.IsEmpty || (kind != SacaKind.Clip && kind != SacaKind.Retarget) || chunks.Length == 0 || chunks.Length > SacaContainer.MaxChunks)
                return AnimationError.InvalidFormat;
            var cursor = 128L + chunks.Length * 32;
            for (var i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].Bytes == null || (chunks[i].Stride == 0 ? chunks[i].Count != 0 : chunks[i].Count * (ulong)chunks[i].Stride != (ulong)chunks[i].Bytes.Length))
                    return AnimationError.InvalidFormat;
                cursor = Align(cursor) + chunks[i].Bytes.Length;
                if (cursor > SacaContainer.MaxFileBytes)
                    return AnimationError.CapacityExceeded;
            }
            var result = new byte[(int)cursor];
            BinaryPrimitives.WriteUInt32LittleEndian(result, 0x41434153);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)kind);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), 128);
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16), (ulong)result.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(24), 128);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(32), (uint)chunks.Length);
            ContentWire.Id(result.AsSpan(72), family);
            cursor = 128 + chunks.Length * 32;
            for (var i = 0; i < chunks.Length; i++)
            {
                cursor = Align(cursor);
                var entry = result.AsSpan(128 + i * 32, 32);
                var chunk = chunks[i];
                BinaryPrimitives.WriteUInt32LittleEndian(entry, chunk.Tag);
                BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(4), chunk.Flags);
                BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(8), (ulong)cursor);
                BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(16), (ulong)chunk.Bytes.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(24), chunk.Count);
                BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(28), chunk.Stride);
                chunk.Bytes.CopyTo(result.AsSpan((int)cursor));
                cursor += chunk.Bytes.Length;
            }
            Span<SacaChunk> directory = stackalloc SacaChunk[SacaContainer.MaxChunks];
            var error = SacaContainer.ReadDirectory(result, directory, out _, out _);
            if (error != AnimationError.None)
                return error;
            var digest = Digest(result);
            ContentWire.Id(result.AsSpan(40), digest);
            bytes = result;
            return AnimationError.None;
        }

        public static AssetId Digest(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 128)
                return default;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> zero = stackalloc byte[32];
            zero.Clear();
            hash.AppendData(bytes.Slice(0, 40));
            hash.AppendData(zero);
            hash.AppendData(bytes.Slice(72));
            return ContentWire.Id(hash.GetHashAndReset());
        }

        public static AnimationError Verify(ReadOnlySpan<byte> bytes, Span<SacaChunk> directory, out int count, out SacaHeader header)
        {
            var error = SacaContainer.ReadDirectory(bytes, directory, out count, out header);
            if (error != AnimationError.None)
                return error;
            if (header.Digest.IsEmpty || Digest(bytes) != header.Digest)
            {
                count = 0;
                header = default;
                return AnimationError.CorruptData;
            }
            return AnimationError.None;
        }

        static long Align(long position) => (position + 15) & ~15L;
    }

    internal static class ContentWire
    {
        internal static float Single(ReadOnlySpan<byte> bytes) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
        internal static void Single(Span<byte> bytes, float value) => BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(value == 0 ? 0 : value));
        internal static double Double(ReadOnlySpan<byte> bytes) => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes));
        internal static void Double(Span<byte> bytes, double value) => BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(value == 0 ? 0 : value));
        internal static Float3 Vector(ReadOnlySpan<byte> bytes) => new Float3(Single(bytes), Single(bytes.Slice(4)), Single(bytes.Slice(8)));
        internal static void Vector(Span<byte> bytes, Float3 value) { Single(bytes, value.X); Single(bytes.Slice(4), value.Y); Single(bytes.Slice(8), value.Z); }
        internal static Quaternion Rotation(ReadOnlySpan<byte> bytes) => new Quaternion(Single(bytes), Single(bytes.Slice(4)), Single(bytes.Slice(8)), Single(bytes.Slice(12)));
        internal static void Rotation(Span<byte> bytes, Quaternion value) { Single(bytes, value.X); Single(bytes.Slice(4), value.Y); Single(bytes.Slice(8), value.Z); Single(bytes.Slice(12), value.W); }
        internal static AssetId Id(ReadOnlySpan<byte> bytes) => new AssetId(BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8)), BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16)), BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24)));
        internal static void Id(Span<byte> bytes, AssetId value) { BinaryPrimitives.WriteUInt64LittleEndian(bytes, value.A); BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(8), value.B); BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(16), value.C); BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(24), value.D); }
        internal static bool Zero(ReadOnlySpan<byte> bytes) { for (var i = 0; i < bytes.Length; i++) if (bytes[i] != 0) return false; return true; }
    }
}

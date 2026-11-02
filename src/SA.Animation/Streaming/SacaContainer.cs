using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SA.Animation
{
    public enum SacaKind : uint { Clip = 1, Retarget = 2 }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SacaHeader
    {
        public readonly SacaKind Kind;
        public readonly AssetId Digest;
        public readonly AssetId Family;
        public SacaHeader(SacaKind kind, AssetId digest, AssetId family) { Kind = kind; Digest = digest; Family = family; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SacaChunk
    {
        public readonly uint Tag;
        public readonly uint Flags;
        public readonly int Offset;
        public readonly int Length;
        public readonly uint Count;
        public readonly uint Stride;
        public SacaChunk(uint tag, uint flags, int offset, int length, uint count, uint stride)
        { Tag = tag; Flags = flags; Offset = offset; Length = length; Count = count; Stride = stride; }
    }

    public static class SacaContainer
    {
        public const int HeaderBytes = 128;
        public const int MaxFileBytes = 256 * 1024 * 1024;
        public const int MaxChunks = 64;
        public const uint Meta = 0x4154454d;
        public const uint Tracks = 0x534b5254;
        public const uint Segments = 0x49474553;
        public const uint Data = 0x41544144;
        public const uint Root = 0x544f4f52;
        public const uint Reference = 0x50464552;
        public const uint Contacts = 0x544e4f43;
        public const uint Info = 0x4f464e49;
        public const uint Bones = 0x454e4f42;
        public const uint Corrections = 0x51584946;
        public const uint PreRotations = 0x51455250;

        public static AnimationError ReadDirectory(ReadOnlySpan<byte> bytes, Span<SacaChunk> chunks, out int count, out SacaHeader header)
        {
            count = 0;
            header = default;
            if (bytes.Length < HeaderBytes || bytes.Length > MaxFileBytes || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x41434153
                || BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4)) != 1 || BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6)) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12)) != HeaderBytes || BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16)) != (ulong)bytes.Length
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(36)) != 0)
                return AnimationError.InvalidFormat;
            var kind = (SacaKind)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8));
            var directory = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24));
            var entries = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(32));
            if ((kind != SacaKind.Clip && kind != SacaKind.Retarget) || entries == 0 || entries > MaxChunks || directory < HeaderBytes
                || directory % 16 != 0 || directory > (ulong)bytes.Length || entries * 32UL > (ulong)bytes.Length - directory)
                return AnimationError.InvalidFormat;
            if (chunks.Length < entries)
                return AnimationError.CapacityExceeded;
            for (var i = 104; i < HeaderBytes; i++)
                if (bytes[i] != 0)
                    return AnimationError.InvalidFormat;
            var family = Wire.Id(bytes.Slice(72));
            if (family.IsEmpty)
                return AnimationError.InvalidFormat;
            for (var i = 0; i < entries; i++)
            {
                var record = bytes.Slice((int)directory + i * 32, 32);
                var tag = BinaryPrimitives.ReadUInt32LittleEndian(record);
                var flags = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(4));
                var offset = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(8));
                var length = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(16));
                var records = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(24));
                var stride = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(28));
                if (flags > 1 || offset < HeaderBytes || offset % 16 != 0 || offset > (ulong)bytes.Length || length > (ulong)bytes.Length - offset
                    || (stride == 0 ? records != 0 : records * (ulong)stride != length)
                    || Overlap(offset, length, directory, entries * 32UL) || (flags == 1 && !Known(kind, tag)))
                    return AnimationError.InvalidFormat;
                for (var previous = 0; previous < i; previous++)
                    if (chunks[previous].Tag == tag || Overlap(offset, length, (ulong)chunks[previous].Offset, (ulong)chunks[previous].Length))
                        return AnimationError.InvalidFormat;
                chunks[i] = new SacaChunk(tag, flags, (int)offset, (int)length, records, stride);
            }
            count = (int)entries;
            header = new SacaHeader(kind, Wire.Id(bytes.Slice(40)), family);
            return AnimationError.None;
        }

        public static bool Find(ReadOnlySpan<SacaChunk> chunks, uint tag, out SacaChunk chunk)
        {
            for (var i = 0; i < chunks.Length; i++)
                if (chunks[i].Tag == tag) { chunk = chunks[i]; return true; }
            chunk = default;
            return false;
        }

        static bool Overlap(ulong a, ulong al, ulong b, ulong bl) => al != 0 && bl != 0 && a < b + bl && b < a + al;
        static bool Known(SacaKind kind, uint tag) => tag == Meta || tag == Info || (kind == SacaKind.Clip
            ? tag == Tracks || tag == Segments || tag == Data || tag == Root || tag == Reference || tag == Contacts
            : tag == Bones || tag == Corrections || tag == PreRotations);
    }

    public static class AnimationCrc32
    {
        public static uint Of(ReadOnlySpan<byte> bytes)
        {
            var crc = uint.MaxValue;
            for (var i = 0; i < bytes.Length; i++)
            {
                crc ^= bytes[i];
                for (var bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
            }
            return ~crc;
        }
    }
}

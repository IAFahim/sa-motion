using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SA.Animation;
using SA.Motion.Numerics;
using SA.Shape;

namespace SA.Animation.Content
{
    public sealed class ShapeAsset
    {
        public AssetId Target { get; }
        public AssetId Definition { get; }
        public ShapeGrounding Grounding { get; }
        public ShapeMarker SupportA { get; }
        public ShapeMarker SupportB { get; }
        public float MinimumLength { get; }
        public float MaximumLength { get; }
        public float MinimumSkinScale { get; }
        public float MaximumSkinScale { get; }
        public ShapeChannel[] Channels { get; }
        public OffsetEffect[] Offsets { get; }
        public SkinEffect[] Skin { get; }
        public SkinFrame[] Frames { get; }
        public ShapeMorph[] Morphs { get; }
        public string[] MorphNames { get; }
        public MorphEffect[] MorphEffects { get; }

        internal ShapeAsset(AssetId target, AssetId definition, ShapeGrounding grounding, ShapeMarker supportA, ShapeMarker supportB,
            float minimumLength, float maximumLength, float minimumSkinScale, float maximumSkinScale,
            ShapeChannel[] channels, OffsetEffect[] offsets, SkinEffect[] skin, SkinFrame[] frames,
            ShapeMorph[] morphs, string[] morphNames, MorphEffect[] morphEffects)
        {
            Target = target;
            Definition = definition;
            Grounding = grounding;
            SupportA = supportA;
            SupportB = supportB;
            MinimumLength = minimumLength;
            MaximumLength = maximumLength;
            MinimumSkinScale = minimumSkinScale;
            MaximumSkinScale = maximumSkinScale;
            Channels = channels;
            Offsets = offsets;
            Skin = skin;
            Frames = frames;
            Morphs = morphs;
            MorphNames = morphNames;
            MorphEffects = morphEffects;
        }

        public ShapeProfile Profile
        {
            get { return new ShapeProfile(Definition, Target, SupportA, SupportB, Grounding, MinimumLength, MaximumLength, MinimumSkinScale, MaximumSkinScale); }
        }

        public ShapeDefinition View()
        {
            return new ShapeDefinition(Profile, Channels, Offsets, Skin, Frames);
        }
    }

    public static class ShapeFile
    {
        public static AnimationError Parse(ReadOnlySpan<byte> json, ReadOnlySpan<string> targetBones, ReadOnlySpan<string> meshMorphs, AssetId expectedTarget, ReadOnlySpan<Float3> baseOffsets, out ShapeAsset? asset)
        {
            return ParseCore(json, targetBones, meshMorphs, expectedTarget, baseOffsets, false, out asset);
        }

        public static AnimationError Load(ReadOnlySpan<byte> json, ReadOnlySpan<string> targetBones, ReadOnlySpan<string> meshMorphs, AssetId expectedTarget, ReadOnlySpan<Float3> baseOffsets, out ShapeAsset? asset)
        {
            return ParseCore(json, targetBones, meshMorphs, expectedTarget, baseOffsets, true, out asset);
        }

        public static AnimationError CanonicalBytes(ShapeAsset asset, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (asset == null)
            {
                return AnimationError.InvalidFormat;
            }
            if (asset.Channels == null || asset.Offsets == null || asset.Skin == null || asset.Frames == null
                || asset.Morphs == null || asset.MorphNames == null || asset.MorphEffects == null
                || asset.MorphNames.Length != asset.Morphs.Length)
            {
                return AnimationError.InvalidFormat;
            }
            for (int i = 0; i < asset.MorphNames.Length; i++)
            {
                if (asset.MorphNames[i] == null)
                {
                    return AnimationError.InvalidFormat;
                }
            }
            return BuildBytes(asset.Target, asset.Grounding, asset.SupportA, asset.SupportB,
                asset.MinimumLength, asset.MaximumLength, asset.MinimumSkinScale, asset.MaximumSkinScale,
                asset.Channels, asset.Offsets, asset.Skin, asset.Frames, asset.MorphNames, asset.Morphs, asset.MorphEffects, out bytes);
        }

        public static AssetId DefinitionId(ReadOnlySpan<byte> canonical)
        {
            return ContentIdentity.Hash(canonical);
        }

        static AnimationError ParseCore(ReadOnlySpan<byte> json, ReadOnlySpan<string> targetBones, ReadOnlySpan<string> meshMorphs, AssetId expectedTarget, ReadOnlySpan<Float3> baseOffsets, bool verify, out ShapeAsset? asset)
        {
            asset = null;
            if (targetBones.Length == 0 || targetBones.Length > SampleLocator.MaxBones)
            {
                return AnimationError.InvalidTable;
            }
            string[] bones = new string[targetBones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                string candidate = targetBones[i];
                if (string.IsNullOrEmpty(candidate))
                {
                    return AnimationError.InvalidTable;
                }
                for (int j = 0; j < i; j++)
                {
                    if (bones[j].Equals(candidate, StringComparison.Ordinal))
                    {
                        return AnimationError.InvalidTable;
                    }
                }
                bones[i] = candidate;
            }
            string[] mesh = new string[meshMorphs.Length];
            for (int i = 0; i < mesh.Length; i++)
            {
                if (meshMorphs[i] == null)
                {
                    return AnimationError.InvalidTable;
                }
                mesh[i] = meshMorphs[i];
            }
            if (json.Length > 16777216)
            {
                return AnimationError.InvalidFormat;
            }
            byte[] copy = new byte[json.Length];
            json.CopyTo(copy);
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(copy);
            }
            catch (Exception)
            {
                return AnimationError.InvalidFormat;
            }
            Reader reader = new Reader(text, bones, mesh);
            AnimationError error = reader.ParseRoot();
            if (error != AnimationError.None)
            {
                return error;
            }
            if (!reader.SeenSchema || !reader.SeenTarget || !reader.SeenUnits || !reader.SeenUp || !reader.SeenGrounding || !reader.SeenSupports)
            {
                return AnimationError.InvalidFormat;
            }
            if (reader.Target.IsEmpty)
            {
                return AnimationError.InvalidTable;
            }
            if (!expectedTarget.IsEmpty && expectedTarget != reader.Target)
            {
                return AnimationError.InvalidTable;
            }
            if (!ScaleRange(reader.MinLength, reader.MaxLength) || !ScaleRange(reader.MinSkin, reader.MaxSkin))
            {
                return AnimationError.InvalidTable;
            }
            ShapeChannel[] channels = reader.Channels.ToArray();
            error = ShapeParameters.Validate(channels);
            if (error != AnimationError.None)
            {
                return error;
            }
            if (reader.OffsetRaws.Count > ShapeRig.MaxEffects || reader.SkinRaws.Count > ShapeRig.MaxEffects
                || reader.MorphRaws.Count > ShapeRig.MaxEffects || reader.MorphFxRaws.Count > ShapeRig.MaxEffects)
            {
                return AnimationError.CapacityExceeded;
            }
            if (baseOffsets.Length != 0 && baseOffsets.Length != bones.Length)
            {
                return AnimationError.InvalidTable;
            }
            OffsetEffect[] offsets = new OffsetEffect[reader.OffsetRaws.Count];
            for (int i = 0; i < offsets.Length; i++)
            {
                OffsetRaw raw = reader.OffsetRaws[i];
                if (raw.Channel < 0 || raw.Channel >= channels.Length || raw.Bone <= 0 || raw.Bone >= bones.Length
                    || !raw.Delta.IsFinite || !Scalar.IsFinite(raw.LogLength))
                {
                    return AnimationError.InvalidTable;
                }
                offsets[i] = new OffsetEffect(raw.Channel, raw.Bone, raw.Delta, raw.LogLength);
            }
            SkinEffect[] skin = new SkinEffect[reader.SkinRaws.Count];
            for (int i = 0; i < skin.Length; i++)
            {
                SkinRaw raw = reader.SkinRaws[i];
                if (raw.Channel < 0 || raw.Channel >= channels.Length || raw.Bone <= 0 || raw.Bone >= bones.Length || !raw.LogScale.IsFinite)
                {
                    return AnimationError.InvalidTable;
                }
                skin[i] = new SkinEffect(raw.Channel, raw.Bone, raw.LogScale);
            }
            if (reader.Frames.Count != 0 && reader.Frames.Count != bones.Length)
            {
                return AnimationError.InvalidTable;
            }
            SkinFrame[] frames = reader.Frames.ToArray();
            for (int i = 0; i < frames.Length; i++)
            {
                if (!RigidMath.UnitRotation(frames[i].Basis) || !frames[i].Pivot.IsFinite)
                {
                    return AnimationError.InvalidTable;
                }
            }
            if (!reader.SupportA.Offset.IsFinite || !reader.SupportB.Offset.IsFinite)
            {
                return AnimationError.InvalidTable;
            }
            ShapeMorph[] morphs = new ShapeMorph[reader.MorphRaws.Count];
            string[] morphNames = new string[reader.MorphRaws.Count];
            for (int i = 0; i < morphs.Length; i++)
            {
                MorphRaw raw = reader.MorphRaws[i];
                if (!Scalar.IsFinite(raw.Minimum) || !Scalar.IsFinite(raw.Maximum) || !Scalar.IsFinite(raw.Base)
                    || raw.Minimum > raw.Base || raw.Maximum < raw.Base)
                {
                    return AnimationError.InvalidTable;
                }
                morphs[i] = new ShapeMorph(raw.Minimum, raw.Maximum, raw.Base);
                morphNames[i] = raw.Name;
            }
            for (int i = 0; i < morphNames.Length; i++)
            {
                if (!ContainsName(reader.NormMesh, morphNames[i]))
                {
                    return AnimationError.InvalidTable;
                }
            }
            MorphEffect[] morphEffects = new MorphEffect[reader.MorphFxRaws.Count];
            for (int i = 0; i < morphEffects.Length; i++)
            {
                MorphFxRaw raw = reader.MorphFxRaws[i];
                int morph = raw.Morph;
                if (raw.MorphName.Length != 0)
                {
                    morph = IndexOfName(morphNames, raw.MorphName);
                    if (morph < 0)
                    {
                        return AnimationError.InvalidTable;
                    }
                }
                if (raw.Channel < 0 || raw.Channel >= channels.Length || morph < 0 || morph >= morphs.Length || !Scalar.IsFinite(raw.Delta))
                {
                    return AnimationError.InvalidTable;
                }
                morphEffects[i] = new MorphEffect(raw.Channel, morph, raw.Delta);
            }
            byte[] canonical;
            error = BuildBytes(reader.Target, reader.Grounding, reader.SupportA, reader.SupportB,
                reader.MinLength, reader.MaxLength, reader.MinSkin, reader.MaxSkin,
                channels, offsets, skin, frames, morphNames, morphs, morphEffects, out canonical);
            if (error != AnimationError.None)
            {
                return error;
            }
            AssetId computed = ContentIdentity.Hash(canonical);
            if (verify)
            {
                if (!reader.SeenAuthored)
                {
                    return AnimationError.InvalidFormat;
                }
                AssetId authored;
                if (ParseHex(reader.Authored, out authored) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                if (authored != computed)
                {
                    return AnimationError.CorruptData;
                }
            }
            error = ValidateIntervals(channels, offsets, skin, reader.MinLength, reader.MaxLength, reader.MinSkin, reader.MaxSkin, baseOffsets, bones.Length);
            if (error != AnimationError.None)
            {
                return error;
            }
            asset = new ShapeAsset(reader.Target, computed, reader.Grounding, reader.SupportA, reader.SupportB,
                reader.MinLength, reader.MaxLength, reader.MinSkin, reader.MaxSkin,
                channels, offsets, skin, frames, morphs, morphNames, morphEffects);
            return AnimationError.None;
        }

        static bool ScaleRange(float minimum, float maximum)
        {
            return Scalar.IsFinite(minimum) && Scalar.IsFinite(maximum) && minimum > 0f && minimum <= 1f && maximum >= 1f;
        }

        static bool ContainsName(string[] list, string name)
        {
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].Equals(name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        static int IndexOfName(string[] list, string name)
        {
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].Equals(name, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }
            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }
            return -1;
        }

        static AnimationError ParseHex(string text, out AssetId id)
        {
            id = default;
            if (text.Length != 64)
            {
                return AnimationError.InvalidFormat;
            }
            Span<byte> bytes = stackalloc byte[32];
            for (int i = 0; i < 32; i++)
            {
                int hi = HexValue(text[i * 2]);
                int lo = HexValue(text[i * 2 + 1]);
                if (hi < 0 || lo < 0)
                {
                    return AnimationError.InvalidFormat;
                }
                bytes[i] = (byte)(hi * 16 + lo);
            }
            id = new AssetId(
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(0, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24, 8)));
            return AnimationError.None;
        }

        static void WriteFloat(Span<byte> destination, float value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value == 0f ? 0f : value));
        }

        static void WriteSupport(byte[] buffer, ref int cursor, ShapeMarker marker)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(cursor), marker.Bone);
            cursor += 4;
            WriteFloat(buffer.AsSpan(cursor), marker.Offset.X);
            cursor += 4;
            WriteFloat(buffer.AsSpan(cursor), marker.Offset.Y);
            cursor += 4;
            WriteFloat(buffer.AsSpan(cursor), marker.Offset.Z);
            cursor += 4;
        }

        static AnimationError BuildBytes(AssetId target, ShapeGrounding grounding, ShapeMarker supportA, ShapeMarker supportB,
            float minimumLength, float maximumLength, float minimumSkinScale, float maximumSkinScale,
            ReadOnlySpan<ShapeChannel> channels, ReadOnlySpan<OffsetEffect> offsets, ReadOnlySpan<SkinEffect> skin,
            ReadOnlySpan<SkinFrame> frames, ReadOnlySpan<string> morphNames, ReadOnlySpan<ShapeMorph> morphs,
            ReadOnlySpan<MorphEffect> morphEffects, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            byte[][] names = new byte[morphNames.Length][];
            long size = 93;
            size += 4 + (long)channels.Length * 20;
            size += 4 + (long)offsets.Length * 24;
            size += 4 + (long)skin.Length * 20;
            size += 4 + (long)frames.Length * 28;
            size += 4;
            for (int i = 0; i < morphNames.Length; i++)
            {
                if (morphNames[i] == null)
                {
                    return AnimationError.InvalidFormat;
                }
                names[i] = Encoding.UTF8.GetBytes(morphNames[i].Normalize(NormalizationForm.FormC));
                size += 16 + names[i].Length;
            }
            size += 4 + (long)morphEffects.Length * 12;
            if (size > int.MaxValue)
            {
                return AnimationError.InvalidFormat;
            }
            byte[] buffer = new byte[(int)size];
            byte[] tag = Encoding.ASCII.GetBytes("SA.SHAPE");
            tag.CopyTo(buffer, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), 1u);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(12), target.A);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(20), target.B);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(28), target.C);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(36), target.D);
            buffer[44] = (byte)grounding;
            int cursor = 45;
            WriteSupport(buffer, ref cursor, supportA);
            WriteSupport(buffer, ref cursor, supportB);
            WriteFloat(buffer.AsSpan(cursor), minimumLength);
            cursor += 4;
            WriteFloat(buffer.AsSpan(cursor), maximumLength);
            cursor += 4;
            WriteFloat(buffer.AsSpan(cursor), minimumSkinScale);
            cursor += 4;
            WriteFloat(buffer.AsSpan(cursor), maximumSkinScale);
            cursor += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(cursor), (uint)channels.Length);
            cursor += 4;
            for (int i = 0; i < channels.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(cursor), channels[i].Id);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), channels[i].Minimum);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), channels[i].Maximum);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), channels[i].Default);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), channels[i].HalfLife);
                cursor += 4;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(cursor), (uint)offsets.Length);
            cursor += 4;
            for (int i = 0; i < offsets.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(cursor), offsets[i].Channel);
                cursor += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(cursor), offsets[i].Bone);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), offsets[i].Delta.X);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), offsets[i].Delta.Y);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), offsets[i].Delta.Z);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), offsets[i].LogLength);
                cursor += 4;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(cursor), (uint)skin.Length);
            cursor += 4;
            for (int i = 0; i < skin.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(cursor), skin[i].Channel);
                cursor += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(cursor), skin[i].Bone);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), skin[i].LogScale.X);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), skin[i].LogScale.Y);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), skin[i].LogScale.Z);
                cursor += 4;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(cursor), (uint)frames.Length);
            cursor += 4;
            for (int i = 0; i < frames.Length; i++)
            {
                Quaternion basis = Quaternion.Canonical(frames[i].Basis);
                WriteFloat(buffer.AsSpan(cursor), basis.X);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), basis.Y);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), basis.Z);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), basis.W);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), frames[i].Pivot.X);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), frames[i].Pivot.Y);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), frames[i].Pivot.Z);
                cursor += 4;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(cursor), (uint)morphs.Length);
            cursor += 4;
            for (int i = 0; i < morphs.Length; i++)
            {
                WriteFloat(buffer.AsSpan(cursor), morphs[i].Minimum);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), morphs[i].Maximum);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), morphs[i].Base);
                cursor += 4;
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(cursor), (uint)names[i].Length);
                cursor += 4;
                names[i].CopyTo(buffer, cursor);
                cursor += names[i].Length;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(cursor), (uint)morphEffects.Length);
            cursor += 4;
            for (int i = 0; i < morphEffects.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(cursor), morphEffects[i].Channel);
                cursor += 4;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(cursor), morphEffects[i].Morph);
                cursor += 4;
                WriteFloat(buffer.AsSpan(cursor), morphEffects[i].Delta);
                cursor += 4;
            }
            bytes = buffer;
            return AnimationError.None;
        }

        static AnimationError ValidateIntervals(ReadOnlySpan<ShapeChannel> channels, ReadOnlySpan<OffsetEffect> offsets, ReadOnlySpan<SkinEffect> skin,
            float minimumLength, float maximumLength, float minimumSkinScale, float maximumSkinScale,
            ReadOnlySpan<Float3> bases, int bones)
        {
            int count = channels.Length;
            double[] low = new double[count];
            double[] high = new double[count];
            for (int i = 0; i < count; i++)
            {
                low[i] = (double)channels[i].Minimum - (double)channels[i].Default;
                high[i] = (double)channels[i].Maximum - (double)channels[i].Default;
            }
            double[] sum = new double[count];
            double[] sumX = new double[count];
            double[] sumY = new double[count];
            double[] sumZ = new double[count];
            for (int bone = 0; bone < bones; bone++)
            {
                Array.Clear(sum, 0, count);
                for (int i = 0; i < offsets.Length; i++)
                {
                    if (offsets[i].Bone == bone)
                    {
                        sum[offsets[i].Channel] += (double)offsets[i].LogLength;
                    }
                }
                double lengthLo = 0.0;
                double lengthHi = 0.0;
                for (int c = 0; c < count; c++)
                {
                    double k = sum[c];
                    if (k == 0.0)
                    {
                        continue;
                    }
                    double a = k * low[c];
                    double b = k * high[c];
                    lengthLo += a < b ? a : b;
                    lengthHi += a > b ? a : b;
                }
                double shortest = Math.Exp(lengthLo);
                double longest = Math.Exp(lengthHi);
                if (double.IsNaN(shortest) || double.IsInfinity(shortest) || double.IsNaN(longest) || double.IsInfinity(longest))
                {
                    return AnimationError.InvalidFrame;
                }
                if (shortest < (double)minimumLength || longest > (double)maximumLength)
                {
                    return AnimationError.InvalidFrame;
                }
                for (int axis = 0; axis < 3; axis++)
                {
                    Array.Clear(sum, 0, count);
                    for (int i = 0; i < skin.Length; i++)
                    {
                        if (skin[i].Bone == bone)
                        {
                            Float3 v = skin[i].LogScale;
                            sum[skin[i].Channel] += axis == 0 ? (double)v.X : axis == 1 ? (double)v.Y : (double)v.Z;
                        }
                    }
                    double axisLo = 0.0;
                    double axisHi = 0.0;
                    for (int c = 0; c < count; c++)
                    {
                        double k = sum[c];
                        if (k == 0.0)
                        {
                            continue;
                        }
                        double a = k * low[c];
                        double b = k * high[c];
                        axisLo += a < b ? a : b;
                        axisHi += a > b ? a : b;
                    }
                    double thin = Math.Exp(axisLo);
                    double thick = Math.Exp(axisHi);
                    if (double.IsNaN(thin) || double.IsInfinity(thin) || double.IsNaN(thick) || double.IsInfinity(thick))
                    {
                        return AnimationError.InvalidFrame;
                    }
                    if (thin < (double)minimumSkinScale || thick > (double)maximumSkinScale)
                    {
                        return AnimationError.InvalidFrame;
                    }
                }
                Array.Clear(sumX, 0, count);
                Array.Clear(sumY, 0, count);
                Array.Clear(sumZ, 0, count);
                for (int i = 0; i < offsets.Length; i++)
                {
                    if (offsets[i].Bone == bone)
                    {
                        sumX[offsets[i].Channel] += (double)offsets[i].Delta.X;
                        sumY[offsets[i].Channel] += (double)offsets[i].Delta.Y;
                        sumZ[offsets[i].Channel] += (double)offsets[i].Delta.Z;
                    }
                }
                double offsetLoX = 0.0;
                double offsetHiX = 0.0;
                double offsetLoY = 0.0;
                double offsetHiY = 0.0;
                double offsetLoZ = 0.0;
                double offsetHiZ = 0.0;
                for (int c = 0; c < count; c++)
                {
                    double ax = sumX[c] * low[c];
                    double bx = sumX[c] * high[c];
                    offsetLoX += ax < bx ? ax : bx;
                    offsetHiX += ax > bx ? ax : bx;
                    double ay = sumY[c] * low[c];
                    double by = sumY[c] * high[c];
                    offsetLoY += ay < by ? ay : by;
                    offsetHiY += ay > by ? ay : by;
                    double az = sumZ[c] * low[c];
                    double bz = sumZ[c] * high[c];
                    offsetLoZ += az < bz ? az : bz;
                    offsetHiZ += az > bz ? az : bz;
                }
                if (double.IsNaN(offsetLoX) || double.IsInfinity(offsetLoX) || double.IsNaN(offsetHiX) || double.IsInfinity(offsetHiX)
                    || double.IsNaN(offsetLoY) || double.IsInfinity(offsetLoY) || double.IsNaN(offsetHiY) || double.IsInfinity(offsetHiY)
                    || double.IsNaN(offsetLoZ) || double.IsInfinity(offsetLoZ) || double.IsNaN(offsetHiZ) || double.IsInfinity(offsetHiZ))
                {
                    return AnimationError.InvalidTable;
                }
                if (bases.Length != 0)
                {
                    Float3 rest = bases[bone];
                    double px = (double)rest.X;
                    double py = (double)rest.Y;
                    double pz = (double)rest.Z;
                    if (px * px + py * py + pz * pz > 1e-12)
                    {
                        double shapedLoX = (px >= 0.0 ? px * shortest : px * longest) + offsetLoX;
                        double shapedHiX = (px >= 0.0 ? px * longest : px * shortest) + offsetHiX;
                        double shapedLoY = (py >= 0.0 ? py * shortest : py * longest) + offsetLoY;
                        double shapedHiY = (py >= 0.0 ? py * longest : py * shortest) + offsetHiY;
                        double shapedLoZ = (pz >= 0.0 ? pz * shortest : pz * longest) + offsetLoZ;
                        double shapedHiZ = (pz >= 0.0 ? pz * longest : pz * shortest) + offsetHiZ;
                        if (shapedLoX <= 0.0 && 0.0 <= shapedHiX && shapedLoY <= 0.0 && 0.0 <= shapedHiY && shapedLoZ <= 0.0 && 0.0 <= shapedHiZ)
                        {
                            return AnimationError.InvalidTable;
                        }
                        double dot = (px >= 0.0 ? px * shapedLoX : px * shapedHiX)
                            + (py >= 0.0 ? py * shapedLoY : py * shapedHiY)
                            + (pz >= 0.0 ? pz * shapedLoZ : pz * shapedHiZ);
                        if (dot < 0.0)
                        {
                            return AnimationError.InvalidTable;
                        }
                    }
                }
            }
            return AnimationError.None;
        }

        struct OffsetRaw
        {
            public int Channel;
            public int Bone;
            public Float3 Delta;
            public float LogLength;
        }

        struct SkinRaw
        {
            public int Channel;
            public int Bone;
            public Float3 LogScale;
        }

        struct MorphRaw
        {
            public string Name;
            public float Minimum;
            public float Maximum;
            public float Base;
        }

        struct MorphFxRaw
        {
            public int Channel;
            public int Morph;
            public string MorphName;
            public float Delta;
        }

        sealed class Reader
        {
            public string Text;
            public int Pos;
            public string[] Bones;
            public string[] NormBones;
            public string[] NormMesh;
            public bool SeenSchema;
            public AssetId Target;
            public bool SeenTarget;
            public string Authored = "";
            public bool SeenAuthored;
            public bool SeenUnits;
            public bool SeenUp;
            public ShapeGrounding Grounding;
            public bool SeenGrounding;
            public ShapeMarker SupportA;
            public ShapeMarker SupportB;
            public bool SeenSupports;
            public float MinLength = 0.25f;
            public float MaxLength = 4f;
            public float MinSkin = 0.125f;
            public float MaxSkin = 8f;
            public List<ShapeChannel> Channels = new List<ShapeChannel>();
            public List<OffsetRaw> OffsetRaws = new List<OffsetRaw>();
            public List<SkinRaw> SkinRaws = new List<SkinRaw>();
            public List<SkinFrame> Frames = new List<SkinFrame>();
            public List<MorphRaw> MorphRaws = new List<MorphRaw>();
            public List<MorphFxRaw> MorphFxRaws = new List<MorphFxRaw>();
            bool seenLength;
            bool seenSkinRange;
            bool seenChannels;
            bool seenOffsets;
            bool seenSkin;
            bool seenFrames;
            bool seenMorphs;
            bool seenMorphFx;

            public Reader(string text, string[] bones, string[] mesh)
            {
                Text = text;
                Bones = bones;
                NormBones = new string[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    NormBones[i] = bones[i].Normalize(NormalizationForm.FormC);
                }
                NormMesh = new string[mesh.Length];
                for (int i = 0; i < mesh.Length; i++)
                {
                    NormMesh[i] = mesh[i].Normalize(NormalizationForm.FormC);
                }
            }

            public AnimationError ParseRoot()
            {
                SkipWhite();
                if (!Consume('{'))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (Consume('}'))
                {
                    return AnimationError.InvalidFormat;
                }
                while (true)
                {
                    SkipWhite();
                    string key;
                    if (ReadString(out key) != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (!Consume(':'))
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    AnimationError error = ParseKey(key);
                    if (error != AnimationError.None)
                    {
                        return error;
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume('}'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (Pos != Text.Length)
                {
                    return AnimationError.InvalidFormat;
                }
                return AnimationError.None;
            }

            AnimationError ParseKey(string key)
            {
                switch (key)
                {
                    case "schemaVersion":
                        if (SeenSchema)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        SeenSchema = true;
                        return ReadSchema();
                    case "targetRigId":
                        if (SeenTarget)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        SeenTarget = true;
                        return ReadTarget();
                    case "definitionId":
                        if (SeenAuthored)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        SeenAuthored = true;
                        return ReadAuthored();
                    case "units":
                        if (SeenUnits)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        SeenUnits = true;
                        return ReadUnits();
                    case "up":
                        if (SeenUp)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        SeenUp = true;
                        return ReadUp();
                    case "grounding":
                        if (SeenGrounding)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        SeenGrounding = true;
                        return ReadGrounding();
                    case "supports":
                        if (SeenSupports)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        SeenSupports = true;
                        return ReadSupports();
                    case "lengthRange":
                        if (seenLength)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenLength = true;
                        return ReadLengthRange();
                    case "skinScaleRange":
                        if (seenSkinRange)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenSkinRange = true;
                        return ReadSkinRange();
                    case "channels":
                        if (seenChannels)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenChannels = true;
                        return ReadChannels();
                    case "offsetEffects":
                        if (seenOffsets)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenOffsets = true;
                        return ReadOffsetEffects();
                    case "skinEffects":
                        if (seenSkin)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenSkin = true;
                        return ReadSkinEffects();
                    case "skinFrames":
                        if (seenFrames)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenFrames = true;
                        return ReadSkinFrames();
                    case "morphs":
                        if (seenMorphs)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenMorphs = true;
                        return ReadMorphs();
                    case "morphEffects":
                        if (seenMorphFx)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenMorphFx = true;
                        return ReadMorphEffects();
                    default:
                        return SkipValue(0);
                }
            }

            void SkipWhite()
            {
                while (Pos < Text.Length)
                {
                    char c = Text[Pos];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    {
                        break;
                    }
                    Pos++;
                }
            }

            bool Consume(char c)
            {
                if (Pos < Text.Length && Text[Pos] == c)
                {
                    Pos++;
                    return true;
                }
                return false;
            }

            AnimationError ReadString(out string value)
            {
                value = "";
                if (!Consume('"'))
                {
                    return AnimationError.InvalidFormat;
                }
                StringBuilder builder = new StringBuilder();
                while (true)
                {
                    if (Pos >= Text.Length)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    char c = Text[Pos++];
                    if (c == '"')
                    {
                        value = builder.ToString();
                        return AnimationError.None;
                    }
                    if (c == '\\')
                    {
                        if (Pos >= Text.Length)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        char e = Text[Pos++];
                        if (e == '"')
                        {
                            builder.Append('"');
                        }
                        else if (e == '\\')
                        {
                            builder.Append('\\');
                        }
                        else if (e == '/')
                        {
                            builder.Append('/');
                        }
                        else if (e == 'b')
                        {
                            builder.Append('\b');
                        }
                        else if (e == 'f')
                        {
                            builder.Append('\f');
                        }
                        else if (e == 'n')
                        {
                            builder.Append('\n');
                        }
                        else if (e == 'r')
                        {
                            builder.Append('\r');
                        }
                        else if (e == 't')
                        {
                            builder.Append('\t');
                        }
                        else if (e == 'u')
                        {
                            int unit;
                            if (ReadHex4(out unit) != AnimationError.None)
                            {
                                return AnimationError.InvalidFormat;
                            }
                            if (unit >= 55296 && unit <= 56319)
                            {
                                if (Pos + 6 > Text.Length || Text[Pos] != '\\' || Text[Pos + 1] != 'u')
                                {
                                    return AnimationError.InvalidFormat;
                                }
                                Pos += 2;
                                int low;
                                if (ReadHex4(out low) != AnimationError.None)
                                {
                                    return AnimationError.InvalidFormat;
                                }
                                if (low < 56320 || low > 57343)
                                {
                                    return AnimationError.InvalidFormat;
                                }
                                builder.Append((char)unit);
                                builder.Append((char)low);
                            }
                            else if (unit >= 56320 && unit <= 57343)
                            {
                                return AnimationError.InvalidFormat;
                            }
                            else
                            {
                                builder.Append((char)unit);
                            }
                        }
                        else
                        {
                            return AnimationError.InvalidFormat;
                        }
                    }
                    else if (c < ' ')
                    {
                        return AnimationError.InvalidFormat;
                    }
                    else
                    {
                        builder.Append(c);
                    }
                }
            }

            AnimationError ReadHex4(out int unit)
            {
                unit = 0;
                if (Pos + 4 > Text.Length)
                {
                    return AnimationError.InvalidFormat;
                }
                int h = 0;
                for (int i = 0; i < 4; i++)
                {
                    char c = Text[Pos + i];
                    int v;
                    if (c >= '0' && c <= '9')
                    {
                        v = c - '0';
                    }
                    else if (c >= 'a' && c <= 'f')
                    {
                        v = c - 'a' + 10;
                    }
                    else if (c >= 'A' && c <= 'F')
                    {
                        v = c - 'A' + 10;
                    }
                    else
                    {
                        return AnimationError.InvalidFormat;
                    }
                    h = h * 16 + v;
                }
                Pos += 4;
                unit = h;
                return AnimationError.None;
            }

            AnimationError ReadRawNumber(out double value)
            {
                value = 0;
                if (Pos >= Text.Length)
                {
                    return AnimationError.InvalidFormat;
                }
                int start = Pos;
                if (Text[Pos] == '-')
                {
                    Pos++;
                }
                if (Pos >= Text.Length)
                {
                    return AnimationError.InvalidFormat;
                }
                if (Text[Pos] == '0')
                {
                    Pos++;
                }
                else if (Text[Pos] >= '1' && Text[Pos] <= '9')
                {
                    while (Pos < Text.Length && Text[Pos] >= '0' && Text[Pos] <= '9')
                    {
                        Pos++;
                    }
                }
                else
                {
                    return AnimationError.InvalidFormat;
                }
                if (Pos < Text.Length && Text[Pos] == '.')
                {
                    Pos++;
                    int first = Pos;
                    while (Pos < Text.Length && Text[Pos] >= '0' && Text[Pos] <= '9')
                    {
                        Pos++;
                    }
                    if (Pos == first)
                    {
                        return AnimationError.InvalidFormat;
                    }
                }
                if (Pos < Text.Length && (Text[Pos] == 'e' || Text[Pos] == 'E'))
                {
                    Pos++;
                    if (Pos < Text.Length && (Text[Pos] == '+' || Text[Pos] == '-'))
                    {
                        Pos++;
                    }
                    int first = Pos;
                    while (Pos < Text.Length && Text[Pos] >= '0' && Text[Pos] <= '9')
                    {
                        Pos++;
                    }
                    if (Pos == first)
                    {
                        return AnimationError.InvalidFormat;
                    }
                }
                value = double.Parse(Text.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                return AnimationError.None;
            }

            AnimationError ReadFloat(out float value)
            {
                value = 0f;
                double raw;
                if (ReadRawNumber(out raw) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                value = (float)raw;
                return AnimationError.None;
            }

            AnimationError ReadIndex(out long value)
            {
                value = 0;
                if (Pos >= Text.Length)
                {
                    return AnimationError.InvalidFormat;
                }
                bool negative = false;
                if (Text[Pos] == '-')
                {
                    negative = true;
                    Pos++;
                }
                int first = Pos;
                long total = 0;
                while (Pos < Text.Length && Text[Pos] >= '0' && Text[Pos] <= '9')
                {
                    total = total * 10 + (Text[Pos] - '0');
                    if (total > 9007199254740991L)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    Pos++;
                }
                if (Pos == first)
                {
                    return AnimationError.InvalidFormat;
                }
                if (Pos < Text.Length && (Text[Pos] == '.' || Text[Pos] == 'e' || Text[Pos] == 'E'))
                {
                    return AnimationError.InvalidFormat;
                }
                value = negative ? -total : total;
                return AnimationError.None;
            }

            AnimationError ReadUint(out uint value)
            {
                value = 0;
                if (Pos >= Text.Length || Text[Pos] < '0' || Text[Pos] > '9')
                {
                    return AnimationError.InvalidFormat;
                }
                long total = 0;
                while (Pos < Text.Length && Text[Pos] >= '0' && Text[Pos] <= '9')
                {
                    total = total * 10 + (Text[Pos] - '0');
                    if (total > 4294967295L)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    Pos++;
                }
                if (Pos < Text.Length && (Text[Pos] == '.' || Text[Pos] == 'e' || Text[Pos] == 'E'))
                {
                    return AnimationError.InvalidFormat;
                }
                value = (uint)total;
                return AnimationError.None;
            }

            AnimationError ReadFloat3(out Float3 value)
            {
                value = default;
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                float x;
                float y;
                float z;
                if (ReadFloat(out x) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(','))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (ReadFloat(out y) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(','))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (ReadFloat(out z) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(']'))
                {
                    return AnimationError.InvalidFormat;
                }
                value = new Float3(x, y, z);
                return AnimationError.None;
            }

            AnimationError ReadBasis(out Quaternion value)
            {
                value = default;
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                float x;
                float y;
                float z;
                float w;
                if (ReadFloat(out x) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(','))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (ReadFloat(out y) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(','))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (ReadFloat(out z) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(','))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (ReadFloat(out w) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(']'))
                {
                    return AnimationError.InvalidFormat;
                }
                value = new Quaternion(x, y, z, w);
                return AnimationError.None;
            }

            AnimationError ReadBone(out int bone)
            {
                bone = -1;
                if (Pos >= Text.Length)
                {
                    return AnimationError.InvalidFormat;
                }
                if (Text[Pos] == '"')
                {
                    string name;
                    if (ReadString(out name) != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    bone = IndexOfName(NormBones, name.Normalize(NormalizationForm.FormC));
                    if (bone <= 0 || bone >= Bones.Length)
                    {
                        return AnimationError.InvalidTable;
                    }
                    return AnimationError.None;
                }
                if (Text[Pos] == '-' || (Text[Pos] >= '0' && Text[Pos] <= '9'))
                {
                    long raw;
                    if (ReadIndex(out raw) != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    if (raw <= 0 || raw >= Bones.Length)
                    {
                        return AnimationError.InvalidTable;
                    }
                    bone = (int)raw;
                    return AnimationError.None;
                }
                return AnimationError.InvalidFormat;
            }

            AnimationError ReadChannelRef(out int channel)
            {
                channel = -1;
                if (Pos >= Text.Length || (Text[Pos] != '-' && (Text[Pos] < '0' || Text[Pos] > '9')))
                {
                    return AnimationError.InvalidFormat;
                }
                long raw;
                if (ReadIndex(out raw) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                if (raw < 0 || raw > 2147483647L)
                {
                    return AnimationError.InvalidTable;
                }
                channel = (int)raw;
                return AnimationError.None;
            }

            AnimationError SkipLiteral(string word)
            {
                if (Pos + word.Length > Text.Length)
                {
                    return AnimationError.InvalidFormat;
                }
                for (int i = 0; i < word.Length; i++)
                {
                    if (Text[Pos + i] != word[i])
                    {
                        return AnimationError.InvalidFormat;
                    }
                }
                Pos += word.Length;
                return AnimationError.None;
            }

            AnimationError SkipValue(int depth)
            {
                if (depth > 32)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (Pos >= Text.Length)
                {
                    return AnimationError.InvalidFormat;
                }
                char c = Text[Pos];
                if (c == '{')
                {
                    Pos++;
                    SkipWhite();
                    if (Consume('}'))
                    {
                        return AnimationError.None;
                    }
                    while (true)
                    {
                        SkipWhite();
                        string key;
                        if (ReadString(out key) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        SkipWhite();
                        if (!Consume(':'))
                        {
                            return AnimationError.InvalidFormat;
                        }
                        if (SkipValue(depth + 1) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        SkipWhite();
                        if (Consume(','))
                        {
                            continue;
                        }
                        if (Consume('}'))
                        {
                            break;
                        }
                        return AnimationError.InvalidFormat;
                    }
                    return AnimationError.None;
                }
                if (c == '[')
                {
                    Pos++;
                    SkipWhite();
                    if (Consume(']'))
                    {
                        return AnimationError.None;
                    }
                    while (true)
                    {
                        SkipWhite();
                        if (SkipValue(depth + 1) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        SkipWhite();
                        if (Consume(','))
                        {
                            continue;
                        }
                        if (Consume(']'))
                        {
                            break;
                        }
                        return AnimationError.InvalidFormat;
                    }
                    return AnimationError.None;
                }
                if (c == '"')
                {
                    string skipped;
                    return ReadString(out skipped);
                }
                if (c == 't')
                {
                    return SkipLiteral("true");
                }
                if (c == 'f')
                {
                    return SkipLiteral("false");
                }
                if (c == 'n')
                {
                    return SkipLiteral("null");
                }
                double number;
                return ReadRawNumber(out number);
            }

            AnimationError ReadSchema()
            {
                uint version;
                if (ReadUint(out version) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                if (version != 1u)
                {
                    return AnimationError.InvalidFormat;
                }
                return AnimationError.None;
            }

            AnimationError ReadTarget()
            {
                string hex;
                if (ReadString(out hex) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                AssetId id;
                if (ParseHex(hex, out id) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                Target = id;
                return AnimationError.None;
            }

            AnimationError ReadAuthored()
            {
                string hex;
                if (ReadString(out hex) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                Authored = hex;
                return AnimationError.None;
            }

            AnimationError ReadUnits()
            {
                string units;
                if (ReadString(out units) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                if (!units.Equals("metres", StringComparison.Ordinal))
                {
                    return AnimationError.InvalidFormat;
                }
                return AnimationError.None;
            }

            AnimationError ReadUp()
            {
                string up;
                if (ReadString(out up) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                if (!up.Equals("+Y", StringComparison.Ordinal))
                {
                    return AnimationError.InvalidFormat;
                }
                return AnimationError.None;
            }

            AnimationError ReadGrounding()
            {
                string mode;
                if (ReadString(out mode) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                if (mode.Equals("KeepBase", StringComparison.Ordinal))
                {
                    Grounding = ShapeGrounding.KeepBase;
                    return AnimationError.None;
                }
                if (mode.Equals("Free", StringComparison.Ordinal))
                {
                    Grounding = ShapeGrounding.Free;
                    return AnimationError.None;
                }
                return AnimationError.InvalidFormat;
            }

            AnimationError ReadSupports()
            {
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                ShapeMarker first;
                AnimationError firstError = ReadSupport(out first);
                if (firstError != AnimationError.None)
                {
                    return firstError;
                }
                SkipWhite();
                if (!Consume(','))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                ShapeMarker second;
                AnimationError secondError = ReadSupport(out second);
                if (secondError != AnimationError.None)
                {
                    return secondError;
                }
                SkipWhite();
                if (!Consume(']'))
                {
                    return AnimationError.InvalidFormat;
                }
                SupportA = first;
                SupportB = second;
                return AnimationError.None;
            }

            AnimationError ReadSupport(out ShapeMarker marker)
            {
                marker = default;
                if (!Consume('{'))
                {
                    return AnimationError.InvalidFormat;
                }
                bool seenBone = false;
                bool seenOffset = false;
                int bone = -1;
                Float3 offset = default;
                SkipWhite();
                if (Consume('}'))
                {
                    return AnimationError.InvalidFormat;
                }
                while (true)
                {
                    SkipWhite();
                    string key;
                    if (ReadString(out key) != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (!Consume(':'))
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (key.Equals("bone", StringComparison.Ordinal))
                    {
                        if (seenBone)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenBone = true;
                        int resolved;
                        AnimationError error = ReadBone(out resolved);
                        if (error != AnimationError.None)
                        {
                            return error;
                        }
                        bone = resolved;
                    }
                    else if (key.Equals("offset", StringComparison.Ordinal))
                    {
                        if (seenOffset)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenOffset = true;
                        Float3 point;
                        if (ReadFloat3(out point) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        offset = point;
                    }
                    else
                    {
                        if (SkipValue(0) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume('}'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                if (!seenBone || !seenOffset)
                {
                    return AnimationError.InvalidFormat;
                }
                marker = new ShapeMarker(bone, offset);
                return AnimationError.None;
            }

            AnimationError ReadLengthRange()
            {
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                float minimum;
                float maximum;
                if (ReadFloat(out minimum) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(','))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (ReadFloat(out maximum) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(']'))
                {
                    return AnimationError.InvalidFormat;
                }
                MinLength = minimum;
                MaxLength = maximum;
                return AnimationError.None;
            }

            AnimationError ReadSkinRange()
            {
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                float minimum;
                float maximum;
                if (ReadFloat(out minimum) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(','))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (ReadFloat(out maximum) != AnimationError.None)
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (!Consume(']'))
                {
                    return AnimationError.InvalidFormat;
                }
                MinSkin = minimum;
                MaxSkin = maximum;
                return AnimationError.None;
            }

            AnimationError ReadChannels()
            {
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (Consume(']'))
                {
                    return AnimationError.None;
                }
                while (true)
                {
                    SkipWhite();
                    if (Channels.Count >= ShapeParameters.MaxChannels)
                    {
                        return AnimationError.CapacityExceeded;
                    }
                    if (ReadChannel() != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume(']'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                return AnimationError.None;
            }

            AnimationError ReadChannel()
            {
                if (!Consume('{'))
                {
                    return AnimationError.InvalidFormat;
                }
                bool seenId = false;
                bool seenName = false;
                bool seenMin = false;
                bool seenMax = false;
                bool seenDefault = false;
                bool seenHalfLife = false;
                uint id = 0;
                float minimum = 0f;
                float maximum = 0f;
                float initial = 0f;
                float halfLife = 0.1f;
                SkipWhite();
                if (Consume('}'))
                {
                    return AnimationError.InvalidFormat;
                }
                while (true)
                {
                    SkipWhite();
                    string key;
                    if (ReadString(out key) != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (!Consume(':'))
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (key.Equals("id", StringComparison.Ordinal))
                    {
                        if (seenId)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenId = true;
                        uint parsed;
                        if (ReadUint(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        id = parsed;
                    }
                    else if (key.Equals("name", StringComparison.Ordinal))
                    {
                        if (seenName)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenName = true;
                        string skipped;
                        if (ReadString(out skipped) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                    }
                    else if (key.Equals("min", StringComparison.Ordinal))
                    {
                        if (seenMin)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenMin = true;
                        float parsed;
                        if (ReadFloat(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        minimum = parsed;
                    }
                    else if (key.Equals("max", StringComparison.Ordinal))
                    {
                        if (seenMax)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenMax = true;
                        float parsed;
                        if (ReadFloat(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        maximum = parsed;
                    }
                    else if (key.Equals("default", StringComparison.Ordinal))
                    {
                        if (seenDefault)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenDefault = true;
                        float parsed;
                        if (ReadFloat(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        initial = parsed;
                    }
                    else if (key.Equals("halfLifeSeconds", StringComparison.Ordinal))
                    {
                        if (seenHalfLife)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenHalfLife = true;
                        float parsed;
                        if (ReadFloat(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        halfLife = parsed;
                    }
                    else
                    {
                        if (SkipValue(0) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume('}'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                if (!seenId || !seenMin || !seenMax)
                {
                    return AnimationError.InvalidFormat;
                }
                Channels.Add(new ShapeChannel(id, minimum, maximum, initial, halfLife));
                return AnimationError.None;
            }

            AnimationError ReadOffsetEffects()
            {
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (Consume(']'))
                {
                    return AnimationError.None;
                }
                while (true)
                {
                    SkipWhite();
                    if (OffsetRaws.Count >= ShapeRig.MaxEffects)
                    {
                        return AnimationError.CapacityExceeded;
                    }
                    AnimationError error = ReadOffsetEffect();
                    if (error != AnimationError.None)
                    {
                        return error;
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume(']'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                return AnimationError.None;
            }

            AnimationError ReadOffsetEffect()
            {
                if (!Consume('{'))
                {
                    return AnimationError.InvalidFormat;
                }
                bool seenChannel = false;
                bool seenBone = false;
                bool seenDelta = false;
                bool seenLog = false;
                int channel = -1;
                int bone = -1;
                Float3 delta = default;
                float logLength = 0f;
                SkipWhite();
                if (Consume('}'))
                {
                    return AnimationError.InvalidFormat;
                }
                while (true)
                {
                    SkipWhite();
                    string key;
                    if (ReadString(out key) != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (!Consume(':'))
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (key.Equals("channel", StringComparison.Ordinal))
                    {
                        if (seenChannel)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenChannel = true;
                        int parsed;
                        AnimationError error = ReadChannelRef(out parsed);
                        if (error != AnimationError.None)
                        {
                            return error;
                        }
                        channel = parsed;
                    }
                    else if (key.Equals("bone", StringComparison.Ordinal))
                    {
                        if (seenBone)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenBone = true;
                        int parsed;
                        AnimationError error = ReadBone(out parsed);
                        if (error != AnimationError.None)
                        {
                            return error;
                        }
                        bone = parsed;
                    }
                    else if (key.Equals("delta", StringComparison.Ordinal))
                    {
                        if (seenDelta)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenDelta = true;
                        Float3 parsed;
                        if (ReadFloat3(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        delta = parsed;
                    }
                    else if (key.Equals("logLength", StringComparison.Ordinal))
                    {
                        if (seenLog)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenLog = true;
                        float parsed;
                        if (ReadFloat(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        logLength = parsed;
                    }
                    else
                    {
                        if (SkipValue(0) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume('}'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                if (!seenChannel || !seenBone)
                {
                    return AnimationError.InvalidFormat;
                }
                OffsetRaw raw;
                raw.Channel = channel;
                raw.Bone = bone;
                raw.Delta = delta;
                raw.LogLength = logLength;
                OffsetRaws.Add(raw);
                return AnimationError.None;
            }

            AnimationError ReadSkinEffects()
            {
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (Consume(']'))
                {
                    return AnimationError.None;
                }
                while (true)
                {
                    SkipWhite();
                    if (SkinRaws.Count >= ShapeRig.MaxEffects)
                    {
                        return AnimationError.CapacityExceeded;
                    }
                    AnimationError error = ReadSkinEffect();
                    if (error != AnimationError.None)
                    {
                        return error;
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume(']'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                return AnimationError.None;
            }

            AnimationError ReadSkinEffect()
            {
                if (!Consume('{'))
                {
                    return AnimationError.InvalidFormat;
                }
                bool seenChannel = false;
                bool seenBone = false;
                bool seenScale = false;
                int channel = -1;
                int bone = -1;
                Float3 logScale = default;
                SkipWhite();
                if (Consume('}'))
                {
                    return AnimationError.InvalidFormat;
                }
                while (true)
                {
                    SkipWhite();
                    string key;
                    if (ReadString(out key) != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (!Consume(':'))
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (key.Equals("channel", StringComparison.Ordinal))
                    {
                        if (seenChannel)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenChannel = true;
                        int parsed;
                        AnimationError error = ReadChannelRef(out parsed);
                        if (error != AnimationError.None)
                        {
                            return error;
                        }
                        channel = parsed;
                    }
                    else if (key.Equals("bone", StringComparison.Ordinal))
                    {
                        if (seenBone)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenBone = true;
                        int parsed;
                        AnimationError error = ReadBone(out parsed);
                        if (error != AnimationError.None)
                        {
                            return error;
                        }
                        bone = parsed;
                    }
                    else if (key.Equals("logScale", StringComparison.Ordinal))
                    {
                        if (seenScale)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenScale = true;
                        Float3 parsed;
                        if (ReadFloat3(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        logScale = parsed;
                    }
                    else
                    {
                        if (SkipValue(0) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume('}'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                if (!seenChannel || !seenBone)
                {
                    return AnimationError.InvalidFormat;
                }
                SkinRaw raw;
                raw.Channel = channel;
                raw.Bone = bone;
                raw.LogScale = logScale;
                SkinRaws.Add(raw);
                return AnimationError.None;
            }

            AnimationError ReadSkinFrames()
            {
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (Consume(']'))
                {
                    return AnimationError.None;
                }
                while (true)
                {
                    SkipWhite();
                    if (Frames.Count >= Bones.Length)
                    {
                        return AnimationError.InvalidTable;
                    }
                    if (ReadSkinFrame() != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume(']'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                return AnimationError.None;
            }

            AnimationError ReadSkinFrame()
            {
                if (!Consume('{'))
                {
                    return AnimationError.InvalidFormat;
                }
                bool seenBasis = false;
                bool seenPivot = false;
                Quaternion basis = Quaternion.Identity;
                Float3 pivot = default;
                SkipWhite();
                if (Consume('}'))
                {
                    return AnimationError.InvalidFormat;
                }
                while (true)
                {
                    SkipWhite();
                    string key;
                    if (ReadString(out key) != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (!Consume(':'))
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (key.Equals("basis", StringComparison.Ordinal))
                    {
                        if (seenBasis)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenBasis = true;
                        Quaternion parsed;
                        if (ReadBasis(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        basis = parsed;
                    }
                    else if (key.Equals("pivot", StringComparison.Ordinal))
                    {
                        if (seenPivot)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenPivot = true;
                        Float3 parsed;
                        if (ReadFloat3(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        pivot = parsed;
                    }
                    else
                    {
                        if (SkipValue(0) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume('}'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                if (!seenBasis || !seenPivot)
                {
                    return AnimationError.InvalidFormat;
                }
                Frames.Add(new SkinFrame(basis, pivot));
                return AnimationError.None;
            }

            AnimationError ReadMorphs()
            {
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (Consume(']'))
                {
                    return AnimationError.None;
                }
                while (true)
                {
                    SkipWhite();
                    if (MorphRaws.Count >= ShapeRig.MaxEffects)
                    {
                        return AnimationError.CapacityExceeded;
                    }
                    AnimationError error = ReadMorph();
                    if (error != AnimationError.None)
                    {
                        return error;
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume(']'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                return AnimationError.None;
            }

            AnimationError ReadMorph()
            {
                if (!Consume('{'))
                {
                    return AnimationError.InvalidFormat;
                }
                bool seenName = false;
                bool seenMin = false;
                bool seenMax = false;
                bool seenBase = false;
                string name = "";
                float minimum = 0f;
                float maximum = 0f;
                float baseValue = 0f;
                SkipWhite();
                if (Consume('}'))
                {
                    return AnimationError.InvalidFormat;
                }
                while (true)
                {
                    SkipWhite();
                    string key;
                    if (ReadString(out key) != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (!Consume(':'))
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (key.Equals("name", StringComparison.Ordinal))
                    {
                        if (seenName)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenName = true;
                        string parsed;
                        if (ReadString(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        name = parsed;
                    }
                    else if (key.Equals("min", StringComparison.Ordinal))
                    {
                        if (seenMin)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenMin = true;
                        float parsed;
                        if (ReadFloat(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        minimum = parsed;
                    }
                    else if (key.Equals("max", StringComparison.Ordinal))
                    {
                        if (seenMax)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenMax = true;
                        float parsed;
                        if (ReadFloat(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        maximum = parsed;
                    }
                    else if (key.Equals("base", StringComparison.Ordinal))
                    {
                        if (seenBase)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenBase = true;
                        float parsed;
                        if (ReadFloat(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        baseValue = parsed;
                    }
                    else
                    {
                        if (SkipValue(0) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume('}'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                if (!seenName || !seenMin || !seenMax)
                {
                    return AnimationError.InvalidFormat;
                }
                string normalized = name.Normalize(NormalizationForm.FormC);
                if (normalized.Length == 0)
                {
                    return AnimationError.InvalidTable;
                }
                for (int i = 0; i < MorphRaws.Count; i++)
                {
                    if (MorphRaws[i].Name.Equals(normalized, StringComparison.Ordinal))
                    {
                        return AnimationError.InvalidTable;
                    }
                }
                MorphRaw raw;
                raw.Name = normalized;
                raw.Minimum = minimum;
                raw.Maximum = maximum;
                raw.Base = baseValue;
                MorphRaws.Add(raw);
                return AnimationError.None;
            }

            AnimationError ReadMorphEffects()
            {
                if (!Consume('['))
                {
                    return AnimationError.InvalidFormat;
                }
                SkipWhite();
                if (Consume(']'))
                {
                    return AnimationError.None;
                }
                while (true)
                {
                    SkipWhite();
                    if (MorphFxRaws.Count >= ShapeRig.MaxEffects)
                    {
                        return AnimationError.CapacityExceeded;
                    }
                    AnimationError error = ReadMorphEffect();
                    if (error != AnimationError.None)
                    {
                        return error;
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume(']'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                return AnimationError.None;
            }

            AnimationError ReadMorphEffect()
            {
                if (!Consume('{'))
                {
                    return AnimationError.InvalidFormat;
                }
                bool seenChannel = false;
                bool seenMorph = false;
                bool seenDelta = false;
                int channel = -1;
                int morph = -1;
                string morphName = "";
                float delta = 0f;
                SkipWhite();
                if (Consume('}'))
                {
                    return AnimationError.InvalidFormat;
                }
                while (true)
                {
                    SkipWhite();
                    string key;
                    if (ReadString(out key) != AnimationError.None)
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (!Consume(':'))
                    {
                        return AnimationError.InvalidFormat;
                    }
                    SkipWhite();
                    if (key.Equals("channel", StringComparison.Ordinal))
                    {
                        if (seenChannel)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenChannel = true;
                        int parsed;
                        AnimationError error = ReadChannelRef(out parsed);
                        if (error != AnimationError.None)
                        {
                            return error;
                        }
                        channel = parsed;
                    }
                    else if (key.Equals("morph", StringComparison.Ordinal))
                    {
                        if (seenMorph)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenMorph = true;
                        if (Pos < Text.Length && Text[Pos] == '"')
                        {
                            string parsed;
                            if (ReadString(out parsed) != AnimationError.None)
                            {
                                return AnimationError.InvalidFormat;
                            }
                            morphName = parsed.Normalize(NormalizationForm.FormC);
                        }
                        else if (Pos < Text.Length && (Text[Pos] == '-' || (Text[Pos] >= '0' && Text[Pos] <= '9')))
                        {
                            long raw;
                            if (ReadIndex(out raw) != AnimationError.None)
                            {
                                return AnimationError.InvalidFormat;
                            }
                            if (raw < 0 || raw > 2147483647L)
                            {
                                return AnimationError.InvalidTable;
                            }
                            morph = (int)raw;
                        }
                        else
                        {
                            return AnimationError.InvalidFormat;
                        }
                    }
                    else if (key.Equals("delta", StringComparison.Ordinal))
                    {
                        if (seenDelta)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        seenDelta = true;
                        float parsed;
                        if (ReadFloat(out parsed) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                        delta = parsed;
                    }
                    else
                    {
                        if (SkipValue(0) != AnimationError.None)
                        {
                            return AnimationError.InvalidFormat;
                        }
                    }
                    SkipWhite();
                    if (Consume(','))
                    {
                        continue;
                    }
                    if (Consume('}'))
                    {
                        break;
                    }
                    return AnimationError.InvalidFormat;
                }
                if (!seenChannel || !seenMorph)
                {
                    return AnimationError.InvalidFormat;
                }
                MorphFxRaw rawEffect;
                rawEffect.Channel = channel;
                rawEffect.Morph = morph;
                rawEffect.MorphName = morphName;
                rawEffect.Delta = delta;
                MorphFxRaws.Add(rawEffect);
                return AnimationError.None;
            }
        }
    }
}

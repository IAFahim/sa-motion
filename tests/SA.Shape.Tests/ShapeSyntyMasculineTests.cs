using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Shape.Tests;

public class ShapeSyntyMasculineTests
{
    internal static readonly string[] BoneNames = [
        "Root", "Hips", "Spine_01", "Spine_02", "Spine_03",
        "Clavicle_L", "Shoulder_L", "Elbow_L", "Hand_L",
        "Finger_01", "Finger_02", "Finger_03", "Finger_04",
        "IndexFinger_01", "IndexFinger_02", "IndexFinger_03", "IndexFinger_04",
        "Prop_L", "Thumb_01", "Thumb_02", "Thumb_03",
        "Clavicle_R", "Shoulder_R", "Elbow_R", "Hand_R",
        "Finger_01_1", "Finger_02_1", "Finger_03_1", "Finger_04_1",
        "IndexFinger_01_1", "IndexFinger_02_1", "IndexFinger_03_1", "IndexFinger_04_1",
        "Prop_R", "Thumb_01_1", "Thumb_02_1", "Thumb_03_1",
        "Neck", "Head", "Eyebrows", "Eyes", "Jaw",
        "UpperLeg_L", "LowerLeg_L", "Ankle_L", "Ball_L", "Toes_L",
        "UpperLeg_R", "LowerLeg_R", "Ankle_R", "Ball_R", "Toes_R"
    ];
    static readonly int[] Parents = [-1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 8, 13, 14, 15, 8, 8, 18, 19, 4, 21, 22, 23, 24, 25, 26, 27, 24, 29, 30, 31, 24, 24, 34, 35, 4, 37, 38, 38, 38, 1, 42, 43, 44, 45, 1, 47, 48, 49, 50];
    const string TargetHex = "3c37466dee941d1626c4b3d7c89e065061f38e3019bf389e5d5e2ac2eb345cb2";
    const string DefinitionHex = "4846c2549aa21add1a51a715e3248c8b20be377f6c543745f8533e82a098712e";
    const string DocHead = "{\"schemaVersion\": 1, \"targetRigId\": \"" + TargetHex + "\", \"units\": \"metres\", \"up\": \"+Y\", \"grounding\": \"KeepBase\", \"supports\": [{\"bone\": \"Toes_L\", \"offset\": [0, -0.08, 0.12]}, {\"bone\": \"Toes_R\", \"offset\": [0, -0.08, 0.12]}]";

    internal static string FixturePath(string name)
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12; i++)
        {
            string candidate = Path.Combine(dir, "tests", "SA.Shape.Tests", "Fixtures", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            string? candidateParent = Path.GetDirectoryName(dir);
            if (candidateParent == null || candidateParent == dir)
            {
                break;
            }
            dir = candidateParent;
        }
        return Path.Combine(AppContext.BaseDirectory, name);
    }

    internal static byte[] Utf8(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    internal static string HexOf(AssetId id)
    {
        byte[] bytes = new byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0), id.A);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), id.B);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), id.C);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), id.D);
        char[] chars = new char[64];
        const string digits = "0123456789abcdef";
        for (int i = 0; i < 32; i++)
        {
            chars[i * 2] = digits[bytes[i] >> 4];
            chars[i * 2 + 1] = digits[bytes[i] & 15];
        }
        return new string(chars);
    }

    internal static Float3[] BaseOffsets()
    {
        return [
            new Float3(0f, 0f, 0f),
            new Float3(6.0766633e-26f, 0.8762761f, -1.9721522e-33f),
            new Float3(-0.00052054384f, 0.10388745f, 0.0030201653f),
            new Float3(3.521303e-05f, 0.18148641f, -0.003029207f),
            new Float3(2.5267314e-05f, 0.179006f, -0.0033441796f),
            new Float3(-0.07293239f, 0.060095485f, 0.041684438f),
            new Float3(-0.0015302919f, 0.13196069f, 0.0015548666f),
            new Float3(-0.008642869f, 0.33936706f, -0.0014362641f),
            new Float3(0.0011719065f, 0.27114275f, -0.00038136425f),
            new Float3(-0.00084363244f, 0.09982842f, -0.0328334f),
            new Float3(-0.0008881993f, 0.042478856f, 5.5511148e-18f),
            new Float3(-0.00018553007f, 0.035132784f, 1.2256862e-15f),
            new Float3(-0.030032393f, -1.9895197e-15f, 3.6770585e-15f),
            new Float3(0.004316187f, 0.103952475f, 0.026726495f),
            new Float3(0.0001083448f, 0.040936172f, -5.32907e-16f),
            new Float3(-0.0007850012f, 0.037903752f, 2.5401902e-15f),
            new Float3(-0.0006347594f, 0.03779261f, 5.2935434e-15f),
            new Float3(-0.034580193f, 0f, -3.28626e-15f),
            new Float3(-0.011595635f, 0.035177354f, 0.045055393f),
            new Float3(-0.000111722875f, 0.063699976f, 0.00051302114f),
            new Float3(4.0739913e-05f, 0.05665453f, -0.00036141623f),
            new Float3(0.07595525f, 0.056041416f, 0.04193737f),
            new Float3(0.1319786f, 6.4733913e-06f, 4.7353424e-08f),
            new Float3(-0.0102176275f, 0.33932388f, 0.0014362636f),
            new Float3(0.008239318f, 0.27101967f, 0.00038136088f),
            new Float3(-0.0009030714f, 0.09982765f, 0.032833397f),
            new Float3(-0.0009212346f, 0.042478103f, -1.9789931e-08f),
            new Float3(-0.00064628496f, 0.0351273f, 1.4853608e-08f),
            new Float3(8.118405e-05f, 0.03003102f, 3.591099e-07f),
            new Float3(0.004257569f, 0.10395538f, -0.026726501f),
            new Float3(-0.0009464423f, 0.040924966f, -4.956748e-08f),
            new Float3(-0.00087888323f, 0.037901938f, -4.5452925e-08f),
            new Float3(-0.000867626f, 0.037783366f, -1.550738e-07f),
            new Float3(-0.034581333f, -2.348031e-11f, -1.4608388e-09f),
            new Float3(-0.01161952f, 0.03517002f, -0.045055397f),
            new Float3(-0.0020193702f, 0.06363735f, 0.0020211332f),
            new Float3(-4.810849e-06f, 0.05664875f, -0.0008467391f),
            new Float3(0.003048571f, 0.11184796f, -0.00019923091f),
            new Float3(-2.7363978e-05f, 0.12157096f, -0.0042248624f),
            new Float3(4.489649e-16f, 0.1276692f, 0.12272215f),
            new Float3(6.1420966e-17f, 0.09447606f, 0.12272215f),
            new Float3(-4.5919798e-05f, 0.06247511f, 0.02164003f),
            new Float3(-0.09932242f, -0.040918678f, -0.025959484f),
            new Float3(0.0003177549f, 0.39930907f, -3.6326495e-14f),
            new Float3(0.0002042169f, 0.3771205f, -0.0014286892f),
            new Float3(-0.00016336289f, 0.11278386f, 0.003565653f),
            new Float3(-0.0011773192f, 0.07294723f, -0.00033563236f),
            new Float3(0.09856232f, -0.039802644f, -0.03023863f),
            new Float3(0.010702381f, 0.39916554f, 0.0002548783f),
            new Float3(-0.0013460641f, 0.3770066f, -0.009293252f),
            new Float3(2.638574e-05f, 0.11273842f, 0.0047949436f),
            new Float3(0.00085089286f, 0.072920196f, -0.0021743176f)
        ];
    }

    internal static Quaternion[] BaseRotations()
    {
        return [
            new Quaternion(0f, 0f, 0f, 1f),
            new Quaternion(-0.08936619f, -0.011019031f, -0.0018417095f, 0.9959362f),
            new Quaternion(0.12236893f, 0.016841816f, 0.0021861256f, 0.9923394f),
            new Quaternion(-0.09700823f, -0.002024661f, -0.0009303108f, 0.9952811f),
            new Quaternion(0.10263826f, -0.0043107844f, 0.014025991f, 0.99461055f),
            new Quaternion(-0.19795883f, -0.15226193f, 0.6841742f, 0.6852257f),
            new Quaternion(0.24288492f, 0.0013261441f, 0.026976056f, 0.96967906f),
            new Quaternion(0.04076235f, -0.0013400238f, -0.02851253f, 0.99876106f),
            new Quaternion(-0.035830513f, 0.00013413433f, -0.0005781422f, 0.9993577f),
            new Quaternion(-0.035583567f, 0.00038872837f, 0.05146945f, 0.9980404f),
            new Quaternion(0.019524885f, -0.002381834f, 0.029078018f, 0.9993836f),
            new Quaternion(-0.06327691f, -0.03787702f, -0.63469684f, 0.7692343f),
            new Quaternion(0.028479932f, -0.02622546f, 0.70719844f, 0.7059543f),
            new Quaternion(0.08155007f, -0.0035584988f, 0.0053418023f, 0.9966486f),
            new Quaternion(-0.010751619f, 0.0010569453f, 0.086703725f, 0.9961756f),
            new Quaternion(-0.05057772f, 0.0025546495f, 0.054213107f, 0.99724436f),
            new Quaternion(0.010008598f, -0.013127802f, -0.013274175f, 0.99977565f),
            new Quaternion(-0.49920812f, 0.50079066f, 0.50079066f, -0.49920812f),
            new Quaternion(0.17747258f, -0.59642255f, -0.14795116f, 0.76869637f),
            new Quaternion(0.28024364f, 0.021980722f, 0.11261144f, 0.9530473f),
            new Quaternion(0.011819729f, 0.012863658f, 0.0061585675f, 0.99982846f),
            new Quaternion(0.969375f, -0.02894694f, -0.24119917f, 0.03601538f),
            new Quaternion(-0.161729f, 0.1734206f, -0.67174023f, 0.70180774f),
            new Quaternion(-0.04074566f, 0.0017766021f, -0.013171476f, 0.9990812f),
            new Quaternion(0.0358259f, -0.0005905641f, -0.013899653f, 0.99926126f),
            new Quaternion(0.035583828f, -0.0003644826f, 0.051379528f, 0.998045f),
            new Quaternion(-0.03891436f, 0.0017450516f, 0.017679958f, 0.99908465f),
            new Quaternion(0.14099719f, -0.0111482795f, 0.08911783f, 0.98592776f),
            new Quaternion(-0.0057477625f, 0.0019679351f, -0.014419821f, 0.99987763f),
            new Quaternion(-0.06579386f, -0.0003947136f, -0.008554249f, 0.99779654f),
            new Quaternion(0.021211151f, 0.0007877451f, 0.098599166f, 0.9949009f),
            new Quaternion(0.0258702f, -0.006316372f, 0.057002403f, 0.99801886f),
            new Quaternion(-0.015948143f, -0.009884943f, 0.022371532f, 0.99957365f),
            new Quaternion(-0.500643f, -0.49935615f, 0.49935615f, 0.500643f),
            new Quaternion(-0.13571893f, -0.6623168f, 0.23517862f, 0.6982893f),
            new Quaternion(0.31123874f, -0.07066126f, -0.05106248f, 0.9463245f),
            new Quaternion(0.016929606f, -0.0138513055f, 0.0068213423f, 0.9997375f),
            new Quaternion(0.05833261f, 0.010388687f, -0.013379393f, 0.9981535f),
            new Quaternion(-0.096971296f, -0.012015258f, -0.0006379704f, 0.99521446f),
            new Quaternion(1.7763568e-15f, 2.7755576e-16f, 4.18144e-09f, 1f),
            new Quaternion(1.7763568e-15f, 2.7755576e-16f, 4.18144e-09f, 1f),
            new Quaternion(1.7763568e-15f, 2.7755576e-16f, 4.18144e-09f, 1f),
            new Quaternion(0.01240067f, -0.08918496f, 0.99584544f, 0.013570825f),
            new Quaternion(-0.019704385f, -0.013604648f, 0.012342069f, 0.9996371f),
            new Quaternion(0.49336958f, 0.003990463f, -0.010034023f, 0.86975276f),
            new Quaternion(0.28727654f, 0.013302653f, -0.009919861f, 0.95770395f),
            new Quaternion(-0.022539295f, -0.01635351f, 0.008433997f, 0.9995766f),
            new Quaternion(-0.0034534498f, -0.09001314f, 0.9954848f, -0.029928034f),
            new Quaternion(-0.008797099f, 0.0010323166f, -0.027879696f, 0.9995721f),
            new Quaternion(0.47960764f, -0.014286102f, 0.003323918f, 0.87736046f),
            new Quaternion(0.3047057f, 0.011314889f, 0.0077266153f, 0.95234805f),
            new Quaternion(-0.021681461f, -0.006950418f, -0.005826311f, 0.9997238f)
        ];
    }

    static Pose[] Calibration()
    {
        Float3[] rest = BaseOffsets();
        Quaternion[] rots = BaseRotations();
        Pose[] poses = new Pose[rest.Length];
        for (int i = 0; i < rest.Length; i++)
        {
            poses[i] = new Pose { Position = rest[i], Rotation = rots[i], Scale = Float3.One };
        }
        return poses;
    }

    static readonly AssetId TargetId = new AssetId(1593593594915469116UL, 5766470958344029222UL, 11401072571598173025UL, 12852205623788068445UL);
    static void Compile(Pose[] calibration, int sourceHips, int targetHips, out RetargetBinding binding, out RetargetRow[] rows)
    {
        int[] map = new int[Parents.Length];
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = i;
        }
        rows = new RetargetRow[Parents.Length];
        Assert.Equal(AnimationError.None, RetargetTable.Compile(ShapeFixture.FamilyId, TargetId, Parents, calibration,
            Parents, calibration, map, sourceHips, targetHips, 1f, new RigidPose[Parents.Length * 2], rows, out binding));
    }

    static string Doc(string inner)
    {
        return DocHead + ", " + inner + "}";
    }

    [Fact]
    public void SyntyFileParsesAndHashMatchesCommittedValue()
    {
        byte[] json = File.ReadAllBytes(FixturePath("shape-synty-masculine.sashape"));
        Assert.Equal(AnimationError.None, ShapeFile.Load(json, BoneNames, [], default, BaseOffsets(), out ShapeAsset? asset));
        ShapeAsset a = Assert.IsType<ShapeAsset>(asset);
        Assert.Equal(TargetHex, HexOf(a.Target));
        Assert.Equal(ShapeGrounding.KeepBase, a.Grounding);
        Assert.Equal(46, a.SupportA.Bone);
        Assert.Equal(51, a.SupportB.Bone);
        Assert.Equal(new Float3(0f, -0.08f, 0.12f), a.SupportA.Offset);
        Assert.Equal(new Float3(0f, -0.08f, 0.12f), a.SupportB.Offset);
        Assert.Equal(0.25f, a.MinimumLength);
        Assert.Equal(4f, a.MaximumLength);
        Assert.Equal(0.125f, a.MinimumSkinScale);
        Assert.Equal(8f, a.MaximumSkinScale);
        Assert.Equal(3, a.Channels.Length);
        Assert.Equal(11u, a.Channels[0].Id);
        Assert.Equal(12u, a.Channels[1].Id);
        Assert.Equal(13u, a.Channels[2].Id);
        Assert.Equal(11, a.Offsets.Length);
        Assert.Equal(11, a.Skin.Length);
        Assert.Empty(a.Frames);
        Assert.Empty(a.Morphs);
        Assert.Empty(a.MorphEffects);
        Assert.Equal(DefinitionHex, HexOf(a.Definition));
        Assert.True(ShapeFile.CanonicalBytes(a, out byte[] first) == AnimationError.None);
        Assert.True(ShapeFile.CanonicalBytes(a, out byte[] second) == AnimationError.None);
        Assert.Equal(first, second);
        Assert.Equal(DefinitionHex, HexOf(ShapeFile.DefinitionId(first)));
    }

    [Fact]
    public void SyntyNeutralRestoresBaseAndExtremaStayBounded()
    {
        byte[] json = File.ReadAllBytes(FixturePath("shape-synty-masculine.sashape"));
        Assert.Equal(AnimationError.None, ShapeFile.Load(json, BoneNames, [], default, BaseOffsets(), out ShapeAsset? asset2));
        ShapeAsset a = Assert.IsType<ShapeAsset>(asset2);
        Compile(Calibration(), 1, 1, out RetargetBinding binding, out RetargetRow[] rows);
        Float3[] offsets = new Float3[BoneNames.Length];
        float[] lengths = new float[BoneNames.Length];
        float[] scales = new float[BoneNames.Length];
        Float3[] skinScales = new Float3[BoneNames.Length];
        Affine[] deformations = new Affine[BoneNames.Length];
        RigidPose[] models = new RigidPose[BoneNames.Length];
        ShapeWorkspace workspace = new ShapeWorkspace(offsets, lengths, scales, skinScales, deformations, models);
        float[] values = new float[3];
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(a.View(), binding, rows, values, 1, workspace, out ShapeGeometry geometry));
        Float3[] rest = BaseOffsets();
        for (int i = 0; i < rest.Length; i++)
        {
            Assert.Equal(rest[i], offsets[i]);
        }
        Assert.Equal(1f, geometry.Retarget.HeightRatio);
        float[][] corners = [[1, 1, 1], [-1, -1, -1], [1, -1, 1], [-1, 1, -1], [0.5f, -0.3f, 0.7f]];
        foreach (float[] corner in corners)
        {
            Assert.Equal(AnimationError.None, ShapeRig.Evaluate(a.View(), binding, rows, corner, 2, workspace, out _));
            for (int i = 0; i < rest.Length; i++)
            {
                Assert.InRange(lengths[i], 0.25f, 4f);
                Assert.InRange(skinScales[i].X, 0.125f, 8f);
                Assert.InRange(skinScales[i].Y, 0.125f, 8f);
                Assert.InRange(skinScales[i].Z, 0.125f, 8f);
            }
        }
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(a.View(), binding, rows, [1f, 0f, 0f], 3, workspace, out _));
        Assert.InRange(lengths[43], 1.249f, 1.251f);
        Assert.InRange(lengths[44], 1.249f, 1.251f);
        Assert.InRange(skinScales[42].Y, 1.249f, 1.251f);
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(a.View(), binding, rows, [0f, 1f, 0f], 4, workspace, out _));
        Assert.InRange(lengths[7], 1.199f, 1.201f);
        Assert.InRange(skinScales[6].Y, 1.199f, 1.201f);
        Assert.Equal(AnimationError.None, ShapeRig.Evaluate(a.View(), binding, rows, [0f, 0f, 1f], 5, workspace, out _));
        Assert.InRange(lengths[4], 1.149f, 1.151f);
        Assert.InRange(skinScales[2].Y, 1.149f, 1.151f);
    }

    [Fact]
    public void SyntyMalformedDiagnosticsStayVisible()
    {
        string golden = File.ReadAllText(FixturePath("shape-synty-masculine.sashape"));
        Assert.Equal(AnimationError.InvalidFormat, ShapeFile.Parse(Utf8(""), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFormat, ShapeFile.Parse(Utf8("{"), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFrame, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 11, \"min\": -1, \"max\": 1}, {\"id\": 11, \"min\": -1, \"max\": 1}]")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden.Replace("\"Toes_L\"", "\"NoSuchBone\"")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden.Replace("\"bone\": \"LowerLeg_L\"", "\"bone\": 0")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidTable, ShapeFile.Parse(Utf8(golden.Replace("\"lengthRange\": [0.25, 4]", "\"lengthRange\": [2, 1]")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFrame, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 11, \"min\": -1, \"max\": 1}], \"offsetEffects\": [{\"channel\": 0, \"bone\": 2, \"logLength\": 100}]")), BoneNames, [], default, BaseOffsets(), out _));
        Assert.Equal(AnimationError.InvalidFrame, ShapeFile.Parse(Utf8(Doc("\"channels\": [{\"id\": 11, \"min\": -1, \"max\": 1}], \"skinEffects\": [{\"channel\": 0, \"bone\": 2, \"logScale\": [0, 100, 0]}]")), BoneNames, [], default, BaseOffsets(), out _));
        string flipped = golden.Replace(DefinitionHex, DefinitionHex.Substring(0, 63) + (DefinitionHex[63] == '0' ? "1" : "0"));
        Assert.Equal(AnimationError.CorruptData, ShapeFile.Load(Utf8(flipped), BoneNames, [], default, BaseOffsets(), out _));
        Assert.NotEqual(AnimationError.None, ShapeFile.Parse(new byte[] { 255, 254, 0, 128 }, BoneNames, [], default, BaseOffsets(), out _));
    }
}

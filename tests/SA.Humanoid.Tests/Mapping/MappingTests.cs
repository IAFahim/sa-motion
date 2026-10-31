using SA.Humanoid.Mapping;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Humanoid.Tests.Mapping;

public class MappingTests
{
    static SkeletonBone Bone(string name, int parent) => new SkeletonBone(name, parent, Pose.Identity);

    static SkeletonBone[] Mixamo()
    {
        return new SkeletonBone[]
        {
            Bone("Hips", -1),
            Bone("Spine", 0),
            Bone("Spine1", 1),
            Bone("Spine2", 2),
            Bone("Neck", 3),
            Bone("Head", 4),
            Bone("LeftShoulder", 3),
            Bone("LeftArm", 6),
            Bone("LeftForeArm", 7),
            Bone("LeftHand", 8),
            Bone("RightShoulder", 3),
            Bone("RightArm", 10),
            Bone("RightForeArm", 11),
            Bone("RightHand", 12),
            Bone("LeftUpLeg", 0),
            Bone("LeftLeg", 14),
            Bone("LeftFoot", 15),
            Bone("LeftToeBase", 16),
            Bone("RightUpLeg", 0),
            Bone("RightLeg", 18),
            Bone("RightFoot", 19),
            Bone("RightToeBase", 20),
        };
    }

    static SkeletonBone[] Generic()
    {
        return new SkeletonBone[]
        {
            Bone("foot_R", 7),
            Bone("l_hand", 9),
            Bone("spine_01", 11),
            Bone("upperarm_r", 6),
            Bone("head", 5),
            Bone("neck", 6),
            Bone("chest", 2),
            Bone("shin_R", 8),
            Bone("upleg_R", 11),
            Bone("l_forearm", 10),
            Bone("l_upperarm", 6),
            Bone("pelvis_jnt", -1),
            Bone("calf.l", 13),
            Bone("thigh_l", 11),
            Bone("L_foot", 12),
            Bone("forearm_r", 3),
            Bone("hand_r", 15),
        };
    }

    static void EqualMaps(int[] expected, int[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }

    static void EqualReports(MappingReport expected, MappingReport actual)
    {
        Assert.Equal(expected.IsValid, actual.IsValid);
        EqualMaps(expected.BoneOfRole, actual.BoneOfRole);
        Assert.Equal(expected.Diagnostics.Length, actual.Diagnostics.Length);
        for (int i = 0; i < expected.Diagnostics.Length; i++)
        {
            Assert.Equal(expected.Diagnostics[i].Code, actual.Diagnostics[i].Code);
            Assert.Equal(expected.Diagnostics[i].Role, actual.Diagnostics[i].Role);
            Assert.Equal(expected.Diagnostics[i].Bone, actual.Diagnostics[i].Bone);
        }
    }

    [Fact]
    public void MixamoSuggestThenMapIsValid()
    {
        int[] expected = new int[] { 0, 3, 5, 7, 11, 8, 12, 9, 13, 14, 18, 15, 19, 16, 20 };
        SkeletonBone[] skeleton = Mixamo();
        int[] suggested = HumanoidMapping.Suggest(skeleton);
        EqualMaps(expected, suggested);
        MappingReport report = HumanoidMapping.Map(skeleton, suggested);
        Assert.True(report.IsValid);
        Assert.Empty(report.Diagnostics);
        EqualMaps(expected, report.BoneOfRole);
    }

    [Fact]
    public void GenericSuggestThenMapIsValid()
    {
        int[] expected = new int[] { 11, 6, 4, 10, 3, 9, 15, 1, 16, 13, 8, 12, 7, 14, 0 };
        SkeletonBone[] skeleton = Generic();
        int[] suggested = HumanoidMapping.Suggest(skeleton);
        EqualMaps(expected, suggested);
        MappingReport report = HumanoidMapping.Map(skeleton, suggested);
        Assert.True(report.IsValid);
        Assert.Empty(report.Diagnostics);
        EqualMaps(expected, report.BoneOfRole);
    }

    [Fact]
    public void RepeatedCallsGiveEqualResults()
    {
        SkeletonBone[] skeleton = Mixamo();
        int[] first = HumanoidMapping.Suggest(skeleton);
        int[] second = HumanoidMapping.Suggest(skeleton);
        EqualMaps(first, second);
        MappingReport firstReport = HumanoidMapping.Map(skeleton, first);
        MappingReport secondReport = HumanoidMapping.Map(skeleton, second);
        EqualReports(firstReport, secondReport);
        int[] broken = (int[])first.Clone();
        broken[(int)Role.ShinLeft] = -1;
        EqualReports(HumanoidMapping.Map(skeleton, broken), HumanoidMapping.Map(skeleton, broken));
    }

    [Fact]
    public void NullAndEmptyInputsStayTotal()
    {
        int[] none = new int[] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        EqualMaps(none, HumanoidMapping.Suggest(null));
        EqualMaps(none, HumanoidMapping.Suggest(Array.Empty<SkeletonBone>()));
        MappingReport nullReport = HumanoidMapping.Map(null, null);
        Assert.False(nullReport.IsValid);
        EqualMaps(none, nullReport.BoneOfRole);
        MappingReport emptyMap = HumanoidMapping.Map(Mixamo(), none);
        Assert.False(emptyMap.IsValid);
        Assert.Equal(Roles.Count, emptyMap.Diagnostics.Length);
        foreach (Diagnostic diagnostic in emptyMap.Diagnostics)
        {
            Assert.Equal(DiagnosticCode.RoleMissing, diagnostic.Code);
        }
    }
}

using SA.Humanoid.Mapping;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Humanoid.Tests.Mapping;

public class MappingFailureTests
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

    static void Contains(MappingReport report, DiagnosticCode code, Role role)
    {
        foreach (Diagnostic diagnostic in report.Diagnostics)
        {
            if (diagnostic.Code == code && diagnostic.Role == role)
            {
                return;
            }
        }
        Assert.Fail("Missing diagnostic " + code + " for role " + role);
    }

    static void OrderedByRoleThenCode(MappingReport report)
    {
        for (int i = 1; i < report.Diagnostics.Length; i++)
        {
            Diagnostic before = report.Diagnostics[i - 1];
            Diagnostic after = report.Diagnostics[i];
            Assert.True((int)before.Role < (int)after.Role || ((int)before.Role == (int)after.Role && (int)before.Code <= (int)after.Code));
        }
    }

    [Fact]
    public void MissingLegReportsRoleMissing()
    {
        SkeletonBone[] skeleton = Mixamo();
        int[] map = HumanoidMapping.Suggest(skeleton);
        map[(int)Role.ShinLeft] = -1;
        MappingReport report = HumanoidMapping.Map(skeleton, map);
        Assert.False(report.IsValid);
        Contains(report, DiagnosticCode.RoleMissing, Role.ShinLeft);
        OrderedByRoleThenCode(report);
    }

    [Fact]
    public void BoneUsedByTwoRolesReportsLaterRole()
    {
        SkeletonBone[] skeleton = Mixamo();
        int[] map = HumanoidMapping.Suggest(skeleton);
        map[(int)Role.ThighRight] = map[(int)Role.ThighLeft];
        MappingReport report = HumanoidMapping.Map(skeleton, map);
        Assert.False(report.IsValid);
        Contains(report, DiagnosticCode.BoneUsedByTwoRoles, Role.ThighRight);
        OrderedByRoleThenCode(report);
    }

    [Fact]
    public void DoubleUsedBoneAlsoFailsTheDescendantCheckInCodeOrder()
    {
        SkeletonBone[] skeleton = Mixamo();
        int[] map = HumanoidMapping.Suggest(skeleton);
        map[(int)Role.ForearmLeft] = map[(int)Role.UpperArmLeft];
        MappingReport report = HumanoidMapping.Map(skeleton, map);
        Assert.False(report.IsValid);
        Assert.Equal(2, report.Diagnostics.Length);
        Assert.Equal(DiagnosticCode.BoneUsedByTwoRoles, report.Diagnostics[0].Code);
        Assert.Equal(Role.ForearmLeft, report.Diagnostics[0].Role);
        Assert.Equal(DiagnosticCode.RoleNotBelowItsParentRole, report.Diagnostics[1].Code);
        Assert.Equal(Role.ForearmLeft, report.Diagnostics[1].Role);
    }

    [Fact]
    public void CycleReportsParentCycle()
    {
        SkeletonBone[] skeleton = new SkeletonBone[]
        {
            Bone("hips", -1),
            Bone("spine", 2),
            Bone("torso", 1),
            Bone("head", 2),
        };
        int[] map = new int[] { 0, 2, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        MappingReport report = HumanoidMapping.Map(skeleton, map);
        Assert.False(report.IsValid);
        Contains(report, DiagnosticCode.ParentCycle, Role.Head);
        Contains(report, DiagnosticCode.ParentCycle, Role.Torso);
        OrderedByRoleThenCode(report);
    }

    [Fact]
    public void OutOfRangeParentReportsParentIndexOutOfRange()
    {
        SkeletonBone[] skeleton = new SkeletonBone[]
        {
            Bone("hips", -1),
            Bone("spine", 0),
            Bone("head", 99),
        };
        int[] map = new int[] { 0, 1, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        MappingReport report = HumanoidMapping.Map(skeleton, map);
        Assert.False(report.IsValid);
        Contains(report, DiagnosticCode.ParentIndexOutOfRange, Role.Head);
        OrderedByRoleThenCode(report);
    }

    [Fact]
    public void ForearmOutsideItsUpperArmSubtreeReportsRoleNotBelowItsParentRole()
    {
        SkeletonBone[] skeleton = Mixamo();
        int[] map = HumanoidMapping.Suggest(skeleton);
        map[(int)Role.ForearmLeft] = 17;
        MappingReport report = HumanoidMapping.Map(skeleton, map);
        Assert.False(report.IsValid);
        Contains(report, DiagnosticCode.RoleNotBelowItsParentRole, Role.ForearmLeft);
        OrderedByRoleThenCode(report);
    }

    [Fact]
    public void WrongMapLengthReportsWrongMapLength()
    {
        MappingReport report = HumanoidMapping.Map(Mixamo(), new int[] { 0, 1, 2 });
        Assert.False(report.IsValid);
        Assert.Single(report.Diagnostics);
        Assert.Equal(DiagnosticCode.WrongMapLength, report.Diagnostics[0].Code);
        Assert.Equal(Role.Pelvis, report.Diagnostics[0].Role);
    }

    [Fact]
    public void OutOfRangeMapIndexReportsMapIndexOutOfRange()
    {
        SkeletonBone[] skeleton = Mixamo();
        int[] map = HumanoidMapping.Suggest(skeleton);
        map[(int)Role.Head] = 999;
        MappingReport report = HumanoidMapping.Map(skeleton, map);
        Assert.False(report.IsValid);
        Contains(report, DiagnosticCode.MapIndexOutOfRange, Role.Head);
        OrderedByRoleThenCode(report);
    }
}

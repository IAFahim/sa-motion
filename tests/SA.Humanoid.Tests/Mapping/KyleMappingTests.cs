using SA.Humanoid;
using SA.Humanoid.Mapping;
using SA.Motion.Numerics;
using Xunit;

namespace SA.Humanoid.Tests.Mapping;

public class KyleMappingTests
{
    static SkeletonBone[] Kyle()
    {
        var paths = new List<string>
        {
            "Root", "Root/Hip",
            "Root/Hip/Left_Thigh_Joint_01", "Root/Hip/Left_Thigh_Joint_01/Left_Knee_Joint_01", "Root/Hip/Left_Thigh_Joint_01/Left_Knee_Joint_01/Left_Ankle_Joint_01", "Root/Hip/Left_Thigh_Joint_01/Left_Knee_Joint_01/Left_Ankle_Joint_01/Left_Toe_Joint_01",
            "Root/Hip/Right_Thigh_Joint_01", "Root/Hip/Right_Thigh_Joint_01/Right_Knee_Joint_01", "Root/Hip/Right_Thigh_Joint_01/Right_Knee_Joint_01/Right_Ankle_Joint_01", "Root/Hip/Right_Thigh_Joint_01/Right_Knee_Joint_01/Right_Ankle_Joint_01/Right_Toe_Joint_01",
            "Root/Ribs"
        };
        foreach (var side in new[] { "Left", "Right" })
        {
            var shoulder = $"Root/Ribs/{side}_Shoulder_Joint_01";
            paths.Add(shoulder);
            paths.Add($"{shoulder}/{side}_Upper_Arm_Joint_01");
            paths.Add($"{shoulder}/{side}_Upper_Arm_Joint_01/{side}_Forearm_Joint_01");
            paths.Add($"{shoulder}/{side}_Upper_Arm_Joint_01/{side}_Forearm_Joint_01/{side}_Wrist_Joint_01");
            paths.Add($"{shoulder}/{side}_Upper_Arm_Joint_01/{side}_Forearm_Joint_01/{side}_Wrist_Joint_01/{side}_Index_Finger_Joint_01a");
        }
        paths.Add("Root/Ribs/Neck");
        paths.Add("Root/Ribs/Neck/Head");
        var bones = new SkeletonBone[paths.Count];
        for (var i = 0; i < paths.Count; i++)
        {
            var slash = paths[i].LastIndexOf('/');
            var parent = slash < 0 ? -1 : paths.IndexOf(paths[i][..slash]);
            bones[i] = new SkeletonBone(paths[i][(slash + 1)..], parent, Pose.Identity);
        }
        return bones;
    }

    [Fact]
    public void SuggestionsForRobotKyleValidateAndNameTheRightBones()
    {
        var skeleton = Kyle();
        var report = HumanoidMapping.Map(skeleton, HumanoidMapping.Suggest(skeleton));
        Assert.True(report.IsValid, string.Join(", ", report.Diagnostics.Select(d => $"{d.Code}:{d.Role}")));
        string Name(Role role) => skeleton[report.BoneOfRole[(int)role]].Name;
        Assert.Equal("Hip", Name(Role.Pelvis));
        Assert.Equal("Ribs", Name(Role.Torso));
        Assert.Equal("Head", Name(Role.Head));
        Assert.Equal("Left_Upper_Arm_Joint_01", Name(Role.UpperArmLeft));
        Assert.Equal("Right_Forearm_Joint_01", Name(Role.ForearmRight));
        Assert.Equal("Left_Wrist_Joint_01", Name(Role.HandLeft));
        Assert.Equal("Right_Thigh_Joint_01", Name(Role.ThighRight));
        Assert.Equal("Left_Knee_Joint_01", Name(Role.ShinLeft));
        Assert.Equal("Right_Ankle_Joint_01", Name(Role.FootRight));
    }
}

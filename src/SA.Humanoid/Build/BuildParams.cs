using System.Runtime.InteropServices;

namespace SA.Humanoid.Build
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BuildParams
    {
        public readonly float LimbRadiusRatio;
        public readonly float HeadRadiusRatio;
        public readonly float HandRadiusRatio;
        public readonly float FootRadiusRatio;
        public readonly float PelvisHalfWidthRatio;
        public readonly float PelvisHalfDepthRatio;
        public readonly float TorsoHalfWidthRatio;
        public readonly float TorsoHalfDepthRatio;

        public BuildParams(float limbRadiusRatio, float headRadiusRatio, float handRadiusRatio, float footRadiusRatio, float pelvisHalfWidthRatio, float pelvisHalfDepthRatio, float torsoHalfWidthRatio, float torsoHalfDepthRatio)
        {
            LimbRadiusRatio = limbRadiusRatio;
            HeadRadiusRatio = headRadiusRatio;
            HandRadiusRatio = handRadiusRatio;
            FootRadiusRatio = footRadiusRatio;
            PelvisHalfWidthRatio = pelvisHalfWidthRatio;
            PelvisHalfDepthRatio = pelvisHalfDepthRatio;
            TorsoHalfWidthRatio = torsoHalfWidthRatio;
            TorsoHalfDepthRatio = torsoHalfDepthRatio;
        }

        public static BuildParams Standard => new BuildParams(
            limbRadiusRatio: 0.12f,
            headRadiusRatio: 0.28f,
            handRadiusRatio: 0.16f,
            footRadiusRatio: 0.16f,
            pelvisHalfWidthRatio: 0.80f,
            pelvisHalfDepthRatio: 0.50f,
            torsoHalfWidthRatio: 0.40f,
            torsoHalfDepthRatio: 0.28f);

        public float RadiusFor(Role role)
        {
            switch (role)
            {
                case Role.Head:
                    return HeadRadiusRatio;
                case Role.HandLeft:
                case Role.HandRight:
                    return HandRadiusRatio;
                case Role.FootLeft:
                case Role.FootRight:
                    return FootRadiusRatio;
                default:
                    return LimbRadiusRatio;
            }
        }

        public static bool IsBox(Role role) => role == Role.Pelvis || role == Role.Torso;

        public float HalfWidthFor(Role role) => role == Role.Pelvis ? PelvisHalfWidthRatio : TorsoHalfWidthRatio;

        public float HalfDepthFor(Role role) => role == Role.Pelvis ? PelvisHalfDepthRatio : TorsoHalfDepthRatio;
    }
}

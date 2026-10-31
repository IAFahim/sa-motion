namespace SA.Humanoid
{
    public enum Role : byte
    {
        Pelvis,
        Torso,
        Head,
        UpperArmLeft,
        UpperArmRight,
        ForearmLeft,
        ForearmRight,
        HandLeft,
        HandRight,
        ThighLeft,
        ThighRight,
        ShinLeft,
        ShinRight,
        FootLeft,
        FootRight
    }

    public static class Roles
    {
        public const int Count = 15;

        public static bool IsValid(Role role) => (byte)role < Count;

        public static bool IsRoot(Role role) => role == Role.Pelvis;

        public static Role Parent(Role role) => role switch
        {
            Role.Torso => Role.Pelvis,
            Role.Head => Role.Torso,
            Role.UpperArmLeft => Role.Torso,
            Role.UpperArmRight => Role.Torso,
            Role.ForearmLeft => Role.UpperArmLeft,
            Role.ForearmRight => Role.UpperArmRight,
            Role.HandLeft => Role.ForearmLeft,
            Role.HandRight => Role.ForearmRight,
            Role.ThighLeft => Role.Pelvis,
            Role.ThighRight => Role.Pelvis,
            Role.ShinLeft => Role.ThighLeft,
            Role.ShinRight => Role.ThighRight,
            Role.FootLeft => Role.ShinLeft,
            Role.FootRight => Role.ShinRight,
            _ => Role.Pelvis
        };
    }
}

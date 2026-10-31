namespace SA.Humanoid.Profile
{
    public static class HumanProfile
    {
        public static RoleSpec Of(Role role) => role switch
        {
            Role.Pelvis => new RoleSpec(0.1117f, 1f, default),
            Role.Torso => new RoleSpec(0.3229f, 1f, new JointLimit(-0.5236f, 0.5236f, 0.7854f, 0.5236f)),
            Role.Head => new RoleSpec(0.0694f, 1f, new JointLimit(-1.2217f, 1.2217f, 1.0472f, 0.7854f)),
            Role.UpperArmLeft => new RoleSpec(0.0271f, 1f, new JointLimit(-1.5708f, 1.5708f, 2.9671f, 1.5708f)),
            Role.UpperArmRight => new RoleSpec(0.0271f, 1f, new JointLimit(-1.5708f, 1.5708f, 2.9671f, 1.5708f)),
            Role.ForearmLeft => new RoleSpec(0.0162f, 1f, new JointLimit(0f, 2.5307f, 0.05f, 0.05f)),
            Role.ForearmRight => new RoleSpec(0.0162f, 1f, new JointLimit(-2.5307f, 0f, 0.05f, 0.05f)),
            Role.HandLeft => new RoleSpec(0.0061f, 1f, new JointLimit(-0.7854f, 0.7854f, 0.8727f, 0.5236f)),
            Role.HandRight => new RoleSpec(0.0061f, 1f, new JointLimit(-0.7854f, 0.7854f, 0.8727f, 0.5236f)),
            Role.ThighLeft => new RoleSpec(0.1416f, 1f, new JointLimit(-0.7854f, 0.7854f, 2.0944f, 1.0472f)),
            Role.ThighRight => new RoleSpec(0.1416f, 1f, new JointLimit(-0.7854f, 0.7854f, 2.0944f, 1.0472f)),
            Role.ShinLeft => new RoleSpec(0.0433f, 1f, new JointLimit(0f, 2.618f, 0.05f, 0.05f)),
            Role.ShinRight => new RoleSpec(0.0433f, 1f, new JointLimit(-2.618f, 0f, 0.05f, 0.05f)),
            Role.FootLeft => new RoleSpec(0.0137f, 1f, new JointLimit(-0.2618f, 0.2618f, 0.8727f, 0.5236f)),
            Role.FootRight => new RoleSpec(0.0137f, 1f, new JointLimit(-0.2618f, 0.2618f, 0.8727f, 0.5236f)),
            _ => default,
        };

        public static float Mass(Role role, float totalMass)
        {
            if (float.IsNaN(totalMass) || float.IsInfinity(totalMass) || totalMass <= 0f)
            {
                return 0f;
            }
            return Of(role).MassFraction * totalMass;
        }
    }
}

using SA.Motion.Legs;

namespace SA.Reactions
{
    public enum LegIKCause : byte
    {
        Working,
        WeightZero,
        ClipsUnticked,
        BadFootShape,
        NoGround
    }

    public static class LegIKAdvice
    {
        public const float Patience = 3f;

        public static LegIKCause Diagnose(float weight, float clipWeight, float footReach, float legLength, bool anyFootPlanted, float secondsWithoutPlant)
        {
            if (anyFootPlanted || !(secondsWithoutPlant >= Patience))
                return LegIKCause.Working;
            if (!(weight > 0f))
                return LegIKCause.WeightZero;
            if (!(clipWeight > 0f))
                return LegIKCause.ClipsUnticked;
            if (!FootHeight.PlausibleReach(footReach, legLength))
                return LegIKCause.BadFootShape;
            return LegIKCause.NoGround;
        }

        public static string Message(LegIKCause cause) => cause switch
        {
            LegIKCause.WeightZero => "Weight is 0, so feet are not planted. Raise Weight on Leg IK Authoring, or write LegIK.Weight in code.",
            LegIKCause.ClipsUnticked => "The clip that is playing has Foot IK unticked. Tick Foot IK for it under Rig Authoring > Resolved animations.",
            LegIKCause.BadFootShape => "The heel and ball baked for this character are far from its foot bone, so no foot can ever count as near the ground. The skeleton was probably posed or moved when the scene was baked: look for saved bone overrides on the character inside the SubScene, or set heel and ball by hand in Leg IK Authoring.",
            LegIKCause.NoGround => "No foot has touched ground for 3 seconds. The floor needs a collider inside a SubScene, on a layer ticked in Leg IK Authoring > Ground. A character in the air is normal and is not reported.",
            _ => string.Empty
        };
    }
}

using System;
using System.Runtime.InteropServices;

namespace SA.Motion.Legs
{
    public enum LegPreset : byte
    {
        Standard,
        Grounded,
        Agile,
        Strict,
        Gentle,
        Contact
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct LegParams
    {
        public const float ReferenceLegLength = 0.85f;

        public float Weight;
        public float WeightHalfLife;
        public float PlantHeight;
        public float ReleaseHeight;
        public float PlantRise;
        public float ReleaseRise;
        public float PlantSpeed;
        public float PinRadius;
        public float ReleaseDistance;
        public float BlendSeconds;
        public float TurnPivot;
        public float HeelRoll;
        public float BallRoll;
        public float RollBand;
        public float SlopeAlign;
        public float MaxSlopeAlign;
        public float MaxWalkableSlope;
        public float TerrainFollow;
        public float TerrainHalfLife;
        public float MaxStepHeight;
        public float Softness;
        public float MaxStretch;
        public float KneeFollow;
        public float PelvisWeight;
        public float PelvisDrop;
        public float PelvisRaise;
        public float PelvisHalfLife;

        public static LegParams Standard => new LegParams
        {
            Weight = 1f,
            WeightHalfLife = 0.12f,
            PlantHeight = 0.035f,
            ReleaseHeight = 0.09f,
            PlantRise = 0.1f,
            ReleaseRise = 0.6f,
            PlantSpeed = 1.5f,
            PinRadius = 0.15f,
            ReleaseDistance = 0.35f,
            BlendSeconds = 0.12f,
            TurnPivot = 0.7f,
            HeelRoll = 1f,
            BallRoll = 1f,
            RollBand = 0.07f,
            SlopeAlign = 1f,
            MaxSlopeAlign = 0.6f,
            MaxWalkableSlope = 0.9f,
            TerrainFollow = 1f,
            TerrainHalfLife = 0.05f,
            MaxStepHeight = 0.45f,
            Softness = 0.04f,
            MaxStretch = 1f,
            KneeFollow = 1f,
            PelvisWeight = 1f,
            PelvisDrop = 0.3f,
            PelvisRaise = 0.05f,
            PelvisHalfLife = 0.08f
        };

        public static LegParams Grounded
        {
            get
            {
                var p = Standard;
                p.PlantHeight = 0.05f;
                p.ReleaseHeight = 0.12f;
                p.PlantRise = 0.15f;
                p.ReleaseRise = 0.8f;
                p.PlantSpeed = 2f;
                p.PinRadius = 0.2f;
                p.ReleaseDistance = 0.45f;
                p.BlendSeconds = 0.18f;
                p.Softness = 0.05f;
                p.TerrainHalfLife = 0.07f;
                p.PelvisHalfLife = 0.12f;
                return p;
            }
        }

        public static LegParams Agile
        {
            get
            {
                var p = Standard;
                p.PlantHeight = 0.025f;
                p.ReleaseHeight = 0.06f;
                p.PlantRise = 0.05f;
                p.ReleaseRise = 0.4f;
                p.PlantSpeed = 1f;
                p.PinRadius = 0.06f;
                p.ReleaseDistance = 0.22f;
                p.BlendSeconds = 0.08f;
                p.Softness = 0.03f;
                p.TerrainHalfLife = 0.035f;
                p.PelvisHalfLife = 0.05f;
                return p;
            }
        }

        public static LegParams Strict
        {
            get
            {
                var p = Standard;
                p.PlantHeight = 0.04f;
                p.ReleaseHeight = 0.08f;
                p.PinRadius = 0.5f;
                p.ReleaseDistance = 0.5f;
                p.BlendSeconds = 0.1f;
                p.RollBand = 0.04f;
                return p;
            }
        }

        public static LegParams Gentle
        {
            get
            {
                var p = Standard;
                p.PlantRise = 1f;
                p.ReleaseRise = 1.5f;
                p.PinRadius = 0.06f;
                p.ReleaseDistance = 0.18f;
                p.BlendSeconds = 0.16f;
                p.SlopeAlign = 0.6f;
                p.MaxSlopeAlign = 0.25f;
                p.MaxStepHeight = 0.12f;
                p.Softness = 0.05f;
                p.PelvisDrop = 0.06f;
                p.PelvisRaise = 0.02f;
                p.PelvisHalfLife = 0.12f;
                return p;
            }
        }

        public static LegParams Contact
        {
            get
            {
                var p = Gentle;
                p.PlantSpeed = 0f;
                p.ReleaseDistance = 0.5f;
                return p;
            }
        }

        public static LegParams Preset(LegPreset preset) => preset switch
        {
            LegPreset.Contact => Contact,
            LegPreset.Gentle => Gentle,
            LegPreset.Grounded => Grounded,
            LegPreset.Agile => Agile,
            LegPreset.Strict => Strict,
            _ => Standard
        };
    }
}

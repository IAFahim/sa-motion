using SA.Motion.Numerics;

namespace SA.Motion.Legs
{
    public static class Planting
    {
        public static bool Decide(bool planted, bool ground, float height, float rise, float drift, byte contactAuthored, float contact, in LegParams p)
        {
            if (!ground)
                return false;
            if (contactAuthored != 0)
                return Scalar.IsFinite(contact) && contact >= 0.5f;
            if (!Scalar.IsFinite(height) || !Scalar.IsFinite(rise) || !Scalar.IsFinite(drift))
                return false;
            if (planted)
                return height <= p.ReleaseHeight && rise <= p.ReleaseRise && drift <= p.ReleaseDistance;
            return height <= p.PlantHeight && rise <= p.PlantRise;
        }

        public static bool Walkable(in GroundHit ground, Float3 up, float maxSlope) =>
            WalkableWithin(ground, up, SlopeLimit(maxSlope));

        internal static float SlopeLimit(float maxSlope) => (float)System.Math.Cos(Scalar.IsFinite(maxSlope) ? maxSlope : 0f);

        internal static bool WalkableWithin(in GroundHit ground, Float3 up, float slopeLimit)
        {
            if (ground.Hit == 0 || !ground.Point.IsFinite || !ground.Normal.IsFinite)
                return false;
            var length = LegMath.Length(ground.Normal);
            if (!(length > Scalar.MagnitudeEpsilon))
                return false;
            var cosine = Float3.Dot(ground.Normal / length, up);
            return cosine >= slopeLimit && cosine > 0f;
        }
    }
}

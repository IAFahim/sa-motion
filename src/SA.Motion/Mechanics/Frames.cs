using SA.Motion.Numerics;

namespace SA.Motion.Mechanics
{
    public static class Frames
    {
        public static Frame Compose(Frame outer, Frame inner)
        {
            var outerRotation = SanitizeRotation(outer.Rotation);
            var innerRotation = SanitizeRotation(inner.Rotation);
            return new Frame(
                SanitizePosition(outer.Position) + Quaternion.Rotate(outerRotation, SanitizePosition(inner.Position)),
                Quaternion.Normalize(Quaternion.Multiply(outerRotation, innerRotation)));
        }

        public static Frame Inverse(Frame frame)
        {
            var inverse = Quaternion.InverseUnit(SanitizeRotation(frame.Rotation));
            return new Frame(Float3.Zero - Quaternion.Rotate(inverse, SanitizePosition(frame.Position)), inverse);
        }

        public static Frame Relative(Frame parent, Frame child) => Compose(Inverse(parent), child);

        public static Float3 TransformPoint(Frame frame, Float3 point) =>
            Quaternion.Rotate(SanitizeRotation(frame.Rotation), SanitizePosition(point)) + SanitizePosition(frame.Position);

        public static Float3 TransformDirection(Frame frame, Float3 direction) =>
            Quaternion.Rotate(SanitizeRotation(frame.Rotation), SanitizePosition(direction));

        public static Frame Mirror(Frame frame, Axis normal)
        {
            var position = SanitizePosition(frame.Position);
            var rotation = SanitizeRotation(frame.Rotation);
            if (normal == Axis.X)
                return new Frame(
                    new Float3(-position.X, position.Y, position.Z),
                    new Quaternion(rotation.X, -rotation.Y, -rotation.Z, rotation.W));
            if (normal == Axis.Y)
                return new Frame(
                    new Float3(position.X, -position.Y, position.Z),
                    new Quaternion(-rotation.X, rotation.Y, -rotation.Z, rotation.W));
            return new Frame(
                new Float3(position.X, position.Y, -position.Z),
                new Quaternion(-rotation.X, -rotation.Y, rotation.Z, rotation.W));
        }

        static Quaternion SanitizeRotation(Quaternion rotation) =>
            rotation.IsValidRotation ? Quaternion.Normalize(rotation) : Quaternion.Identity;

        static Float3 SanitizePosition(Float3 position) => position.IsFinite ? position : Float3.Zero;
    }
}

using System;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public static class SkinPalette
    {
        public static AnimationError Build(ReadOnlySpan<Affine> model, ReadOnlySpan<int> paletteToRig, ReadOnlySpan<Affine> originalBindMatrices,
            in Affine meshFromModel, Span<Affine> output)
        {
            if (paletteToRig.Length != originalBindMatrices.Length || output.Length < paletteToRig.Length || !Finite(meshFromModel))
                return AnimationError.InvalidTable;
            if (model.Overlaps(output) || originalBindMatrices.Overlaps(output))
                return AnimationError.AliasedBuffers;
            for (var i = 0; i < paletteToRig.Length; i++)
            {
                var bone = paletteToRig[i];
                if (bone < 0 || bone >= model.Length || !Finite(model[bone]) || !Finite(originalBindMatrices[i]))
                    return AnimationError.InvalidTable;
            }
            for (var i = 0; i < paletteToRig.Length; i++)
            {
                var first = Multiply(meshFromModel, model[paletteToRig[i]]);
                var palette = Multiply(first, originalBindMatrices[i]);
                if (!Finite(first) || !Finite(palette)) return AnimationError.InvalidTable;
                output[i] = palette;
            }
            return AnimationError.None;
        }

        static bool Finite(in Affine a) => a.C0.IsFinite && a.C1.IsFinite && a.C2.IsFinite && a.C3.IsFinite;
        static Float3 Vector(in Affine a, Float3 b) => a.C0 * b.X + a.C1 * b.Y + a.C2 * b.Z;
        static Affine Multiply(in Affine a, in Affine b) => new Affine(Vector(a, b.C0), Vector(a, b.C1), Vector(a, b.C2), Vector(a, b.C3) + a.C3);
    }
}

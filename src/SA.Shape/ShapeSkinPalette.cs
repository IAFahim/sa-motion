using System;
using SA.Animation;
using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.Shape
{
    public static class ShapeSkinPalette
    {
        public static AnimationError Build(ReadOnlySpan<Affine> model, ReadOnlySpan<Affine> deformations, ReadOnlySpan<int> paletteToRig,
            ReadOnlySpan<Affine> originalBindMatrices, in Affine meshFromModel, Span<Affine> positions, Span<Float3x3> normals)
        {
            if (model.Length != deformations.Length || paletteToRig.Length != originalBindMatrices.Length || positions.Length < paletteToRig.Length
                || normals.Length < paletteToRig.Length || !ShapeMath.Finite(meshFromModel))
                return AnimationError.InvalidTable;
            if (model.Overlaps(positions) || deformations.Overlaps(positions) || originalBindMatrices.Overlaps(positions)) return AnimationError.AliasedBuffers;
            for (var i = 0; i < paletteToRig.Length; i++)
            {
                var bone = paletteToRig[i];
                if (bone < 0 || bone >= model.Length || !ShapeMath.Finite(model[bone]) || !ShapeMath.Finite(deformations[bone]) || !ShapeMath.Finite(originalBindMatrices[i]))
                    return AnimationError.InvalidTable;
            }
            for (var i = 0; i < paletteToRig.Length; i++)
            {
                var bone = paletteToRig[i];
                var first = ShapeMath.Multiply(meshFromModel, model[bone]);
                if (!ShapeMath.Identity(deformations[bone])) first = ShapeMath.Multiply(first, deformations[bone]);
                var matrix = ShapeMath.Multiply(first, originalBindMatrices[i]);
                if (!ShapeMath.Finite(first) || !ShapeMath.Finite(matrix) || !ShapeMath.Normal(matrix, out var normal)) return AnimationError.InvalidTable;
                positions[i] = matrix;
                normals[i] = normal;
            }
            return AnimationError.None;
        }

        public static AnimationError Blend(ReadOnlySpan<Affine> palette, ReadOnlySpan<int> influences, ReadOnlySpan<float> weights,
            out Affine position, out Float3x3 normal)
        {
            position = default;
            normal = default;
            if (influences.Length == 0 || influences.Length != weights.Length) return AnimationError.InvalidTable;
            var total = 0d;
            for (var i = 0; i < influences.Length; i++)
            {
                var bone = influences[i];
                if (bone < 0 || bone >= palette.Length || !ShapeMath.Finite(palette[bone]) || !Scalar.IsFinite(weights[i]) || weights[i] < 0f)
                    return AnimationError.InvalidTable;
                total += weights[i];
            }
            if (total <= 0d) return AnimationError.InvalidTable;
            var candidate = default(Affine);
            for (var i = 0; i < influences.Length; i++)
            {
                var matrix = palette[influences[i]];
                var weight = (float)(weights[i] / total);
                candidate.C0 += matrix.C0 * weight;
                candidate.C1 += matrix.C1 * weight;
                candidate.C2 += matrix.C2 * weight;
                candidate.C3 += matrix.C3 * weight;
            }
            if (!ShapeMath.Finite(candidate) || !ShapeMath.Normal(candidate, out normal)) return AnimationError.UnsupportedScale;
            position = candidate;
            return AnimationError.None;
        }
    }
}

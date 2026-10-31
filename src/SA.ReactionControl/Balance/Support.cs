using SA.Motion.Mechanics;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Balance
{
    public static class Support
    {
        public static float Measure(
            Float3 predicted,
            Frame leftFoot,
            Frame rightFoot,
            float halfWidth,
            float halfLength,
            Float3 up,
            out Float3 closest)
        {
            var axis = BalanceMath.Sanitize(up);
            if (!(halfWidth > 0f))
                halfWidth = 0.09f;
            if (!(halfLength > 0f))
                halfLength = 0.16f;
            var point = BalanceMath.Sanitize(predicted);
            var leftSide = BalanceMath.AxisX(leftFoot);
            var leftForward = BalanceMath.AxisZ(leftFoot);
            var rightSide = BalanceMath.AxisX(rightFoot);
            var rightForward = BalanceMath.AxisZ(rightFoot);
            var c0 = leftFoot.Position - leftForward * halfLength - leftSide * halfWidth;
            var c1 = rightFoot.Position - rightForward * halfLength + rightSide * halfWidth;
            var c2 = rightFoot.Position + rightForward * halfLength + rightSide * halfWidth;
            var c3 = leftFoot.Position + leftForward * halfLength - leftSide * halfWidth;
            var n0 = EdgeNormal(c1 - c0, axis);
            var n1 = EdgeNormal(c2 - c1, axis);
            var n2 = EdgeNormal(c3 - c2, axis);
            var n3 = EdgeNormal(c0 - c3, axis);
            var d3 = point - c3;
            var d1 = point - c1;
            var a = Float3.Dot(n2, d3);
            var b = Float3.Dot(n3, d3);
            var c = Float3.Dot(n1, d1);
            var e = Float3.Dot(n0, d1);
            var distance = a;
            if (b < distance)
                distance = b;
            if (c < distance)
                distance = c;
            if (e < distance)
                distance = e;
            var mean = (leftFoot.Position + rightFoot.Position) * 0.5f;
            var projected = axis * Float3.Dot(axis, mean - point) + point;
            if (!(distance < 0f))
            {
                closest = projected;
                return distance;
            }
            if (b < 0f && b < e && b < a)
                closest = ClosestOnSegment(projected, c3, c0);
            else if (c < 0f && c < e && c < a)
                closest = ClosestOnSegment(projected, c2, c1);
            else if (e < 0f)
                closest = ClosestOnSegment(projected, c0, c1);
            else if (a < 0f)
                closest = ClosestOnSegment(projected, c3, c2);
            else
                closest = projected;
            return distance;
        }

        static Float3 EdgeNormal(Float3 edge, Float3 up)
        {
            return BalanceMath.NormalizeOrZero(Float3.Cross(edge, up), 1e-6f);
        }

        static Float3 ClosestOnSegment(Float3 point, Float3 u, Float3 v)
        {
            var span = v - u;
            var squared = Float3.Dot(span, span);
            if (!(squared > 1e-12f))
                return u;
            var t = Float3.Dot(point - u, span) / squared;
            t = BalanceMath.Clamp(t, 0f, 1f);
            return span * t + u;
        }
    }
}

using System;
using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.ReactionControl.Coordination
{
    public enum ArbitrationReason : byte
    {
        NoneActive = 0,
        HighestPriority = 1,
        HysteresisHold = 2,
        Retained = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ModeRequest
    {
        public ReactionMode Mode;
        public bool Active;
        public float Priority;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ArbitrationResult
    {
        public ReactionMode Owner;
        public ArbitrationReason Reason;
        public float Priority;
    }

    public static class Coordinator
    {
        public static ArbitrationResult Arbitrate(ReadOnlySpan<ModeRequest> requests, ReactionMode current, float hysteresis)
        {
            float margin = Scalar.IsFinite(hysteresis) && hysteresis > 0f ? hysteresis : 0f;
            int top = -1;
            float topPriority = float.NegativeInfinity;
            float currentPriority = float.NegativeInfinity;
            bool currentActive = false;
            for (int i = 0; i < requests.Length; i++)
            {
                ModeRequest request = requests[i];
                if (!request.Active || (byte)request.Mode > (byte)ReactionMode.Recovering)
                    continue;
                float priority = Scalar.IsFinite(request.Priority) ? request.Priority : float.NegativeInfinity;
                if (priority > topPriority)
                {
                    topPriority = priority;
                    top = i;
                }
                if (request.Mode == current && priority > currentPriority)
                {
                    currentPriority = priority;
                    currentActive = true;
                }
            }
            if (top < 0 || float.IsNegativeInfinity(topPriority))
                return new ArbitrationResult { Owner = ReactionMode.Animated, Reason = ArbitrationReason.NoneActive, Priority = 0f };
            ReactionMode winner = requests[top].Mode;
            if (winner == current)
                return new ArbitrationResult { Owner = current, Reason = ArbitrationReason.Retained, Priority = topPriority };
            if (currentActive && topPriority <= currentPriority + margin)
                return new ArbitrationResult { Owner = current, Reason = ArbitrationReason.HysteresisHold, Priority = currentPriority };
            return new ArbitrationResult { Owner = winner, Reason = ArbitrationReason.HighestPriority, Priority = topPriority };
        }
    }
}

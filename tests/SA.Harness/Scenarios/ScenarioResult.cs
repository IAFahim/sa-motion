using System.Runtime.InteropServices;

namespace SA.Harness
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ScenarioResult
    {
        public readonly bool Fell;
        public readonly float PeakPelvisDisplacement;
        public readonly float PeakTorsoLean;
        public readonly float RecoverySeconds;
        public readonly float MaxFootStep;
        public readonly int Steps;
        public readonly float FinalPelvisHeight;

        public ScenarioResult(
            bool fell,
            float peakPelvisDisplacement,
            float peakTorsoLean,
            float recoverySeconds,
            float maxFootStep,
            int steps,
            float finalPelvisHeight)
        {
            Fell = fell;
            PeakPelvisDisplacement = peakPelvisDisplacement;
            PeakTorsoLean = peakTorsoLean;
            RecoverySeconds = recoverySeconds;
            MaxFootStep = maxFootStep;
            Steps = steps;
            FinalPelvisHeight = finalPelvisHeight;
        }
    }
}

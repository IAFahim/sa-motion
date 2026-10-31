using System;
using SA.Humanoid;
using SA.Motion.Numerics;
using SA.ReactionControl;

namespace SA.Harness
{
    public static class Scenarios
    {
        public const float StepSeconds = 0.01666667f;
        public const float MaxSeconds = 60f;
        public const float SweepPushSeconds = 0.5f;
        public const float FellPelvisHeight = 0.5f;
        public const float FellLeanDegrees = 60f;
        public const float RecoverHeightTolerance = 0.1f;
        public const float RecoverLeanDegrees = 15f;
        public const float StepLiftThreshold = 0.05f;
        public const int MaxDirections = 360;

        public static ScenarioResult Run(ControllerStep? controller, in PushSpec push, float seconds)
        {
            bool hostile = controller is null;
            var world = HarnessWorld.Create();
            Float3 startPelvis = world.Bodies[(int)Role.Pelvis].Position;
            if (!Scalar.IsFinite(seconds) || seconds <= 0f)
            {
                hostile = true;
                seconds = 0f;
            }
            if (seconds > MaxSeconds)
            {
                seconds = MaxSeconds;
            }
            float atSeconds = push.AtSeconds;
            if (!Scalar.IsFinite(atSeconds))
            {
                hostile = true;
                atSeconds = 0f;
            }
            if (atSeconds < 0f)
            {
                atSeconds = 0f;
            }
            if (atSeconds > seconds)
            {
                atSeconds = seconds;
            }
            Float3 direction = push.Direction;
            if (!direction.IsFinite || direction.LengthSquared < 1e-12f)
            {
                hostile = true;
                direction = new Float3(1f, 0f, 0f);
            }
            float impulse = push.Impulse;
            if (!Scalar.IsFinite(impulse))
            {
                hostile = true;
                impulse = 0f;
            }
            if (!Roles.IsValid(push.Role))
            {
                hostile = true;
            }
            int total = (int)(seconds / StepSeconds + 0.5f);
            var heights = new float[total + 1];
            var leans = new float[total + 1];
            heights[0] = startPelvis.Y;
            leans[0] = LeanDegrees(world);
            float peakDisplacement = 0f;
            float peakLean = leans[0];
            float maxFoot = 0f;
            int steps = 0;
            bool leftLifted = false;
            bool rightLifted = false;
            SampleFeet(world, ref maxFoot, ref steps, ref leftLifted, ref rightLifted);
            var drives = new JointDrive[HarnessWorld.BodyCount];
            var span = new Span<JointDrive>(drives);
            bool armed = impulse != 0f && Roles.IsValid(push.Role);
            float pushLength = direction.Length;
            int pushStep = 0;
            for (int step = 1; step <= total; step++)
            {
                float elapsed = (step - 1) * StepSeconds;
                try
                {
                    if (controller is null)
                    {
                        FillPassive(span);
                    }
                    else
                    {
                        controller(world, StepSeconds, span);
                    }
                }
                catch
                {
                    hostile = true;
                    FillPassive(span);
                }
                SanitizeDrives(world, span);
                world.ApplyDrives(span);
                if (armed && elapsed >= atSeconds)
                {
                    world.ApplyImpulse(push.Role, direction * (impulse / pushLength), world.Bodies[(int)push.Role].Position);
                    armed = false;
                    pushStep = step - 1;
                }
                world.Step(StepSeconds);
                RoleBody pelvis = world.Bodies[(int)Role.Pelvis];
                heights[step] = pelvis.Position.Y;
                leans[step] = LeanDegrees(world);
                float displacement = (pelvis.Position - startPelvis).Length;
                if (displacement > peakDisplacement)
                {
                    peakDisplacement = displacement;
                }
                if (leans[step] > peakLean)
                {
                    peakLean = leans[step];
                }
                SampleFeet(world, ref maxFoot, ref steps, ref leftLifted, ref rightLifted);
            }
            if (!Scalar.IsFinite(peakDisplacement))
            {
                peakDisplacement = 0f;
            }
            if (!Scalar.IsFinite(peakLean))
            {
                peakLean = 0f;
            }
            if (!Scalar.IsFinite(maxFoot))
            {
                maxFoot = 0f;
            }
            float finalHeight = heights[total];
            if (!Scalar.IsFinite(finalHeight))
            {
                finalHeight = 0f;
            }
            bool fell = hostile || finalHeight < FellPelvisHeight || leans[total] > FellLeanDegrees;
            float recovery = RecoverySeconds(heights, leans, startPelvis.Y, pushStep, total);
            return new ScenarioResult(fell, peakDisplacement, peakLean, recovery, maxFoot, steps, finalHeight);
        }

        public static ScenarioResult[] Sweep(ControllerStep? controller, ReadOnlySpan<float> impulses, int directions, float seconds)
        {
            if (directions < 1 || impulses.Length == 0)
            {
                return Array.Empty<ScenarioResult>();
            }
            if (directions > MaxDirections)
            {
                directions = MaxDirections;
            }
            if ((long)impulses.Length * directions > 65536L)
            {
                return Array.Empty<ScenarioResult>();
            }
            var results = new ScenarioResult[impulses.Length * directions];
            for (int i = 0; i < impulses.Length; i++)
            {
                for (int k = 0; k < directions; k++)
                {
                    var spec = new PushSpec(Role.Torso, SweepDirection(k, directions), impulses[i], SweepPushSeconds);
                    results[i * directions + k] = Run(controller, in spec, seconds);
                }
            }
            return results;
        }

        public static Float3 SweepDirection(int index, int directions)
        {
            if (directions < 1)
            {
                return new Float3(1f, 0f, 0f);
            }
            int wrapped = index % directions;
            if (wrapped < 0)
            {
                wrapped += directions;
            }
            float angle = 2f * Scalar.Pi * wrapped / directions;
            return new Float3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
        }

        static void FillPassive(Span<JointDrive> drives)
        {
            for (int i = 0; i < drives.Length; i++)
            {
                drives[i] = JointDrive.Passive;
            }
        }

        static void SanitizeDrives(HarnessWorld world, Span<JointDrive> drives)
        {
            int count = drives.Length < HarnessWorld.BodyCount ? drives.Length : HarnessWorld.BodyCount;
            for (int i = 0; i < count; i++)
            {
                JointDrive drive = drives[i];
                if (!drive.Target.IsValidRotation)
                {
                    drive.Target = world.Bodies[i].Rotation;
                }
                if (!Scalar.IsFinite(drive.Stiffness))
                {
                    drive.Stiffness = 0f;
                }
                if (!Scalar.IsFinite(drive.Damping))
                {
                    drive.Damping = 0f;
                }
                if (!Scalar.IsFinite(drive.MaxTorque))
                {
                    drive.MaxTorque = 0f;
                }
                drives[i] = drive;
            }
            for (int i = count; i < drives.Length; i++)
            {
                drives[i] = JointDrive.Passive;
            }
        }

        static float LeanDegrees(HarnessWorld world)
        {
            Float3 up = Quaternion.Rotate(world.Bodies[(int)Role.Torso].Rotation, new Float3(0f, 1f, 0f));
            if (!up.IsFinite || up.LengthSquared < 1e-12f)
            {
                return 0f;
            }
            float cosine = up.Y / up.Length;
            if (cosine > 1f)
            {
                cosine = 1f;
            }
            if (cosine < -1f)
            {
                cosine = -1f;
            }
            float degrees = (float)Math.Acos(cosine) * 180f / Scalar.Pi;
            return Scalar.IsFinite(degrees) ? degrees : 0f;
        }

        static void SampleFeet(HarnessWorld world, ref float maxFoot, ref int steps, ref bool leftLifted, ref bool rightLifted)
        {
            float left = world.ProbeGround(Role.FootLeft).Distance;
            float right = world.ProbeGround(Role.FootRight).Distance;
            if (Scalar.IsFinite(left))
            {
                if (left > maxFoot)
                {
                    maxFoot = left;
                }
                bool lifted = left > StepLiftThreshold;
                if (lifted && !leftLifted)
                {
                    steps++;
                }
                leftLifted = lifted;
            }
            if (Scalar.IsFinite(right))
            {
                if (right > maxFoot)
                {
                    maxFoot = right;
                }
                bool lifted = right > StepLiftThreshold;
                if (lifted && !rightLifted)
                {
                    steps++;
                }
                rightLifted = lifted;
            }
        }

        static float RecoverySeconds(float[] heights, float[] leans, float startHeight, int pushStep, int total)
        {
            int first = total + 1;
            for (int step = total; step >= pushStep; step--)
            {
                if (Scalar.Abs(heights[step] - startHeight) > RecoverHeightTolerance || leans[step] > RecoverLeanDegrees)
                {
                    break;
                }
                first = step;
            }
            if (first > total)
            {
                return (total - pushStep) * StepSeconds;
            }
            return (first - pushStep) * StepSeconds;
        }
    }
}

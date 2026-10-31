using System;
using System.Text;

namespace SA.Humanoid.Mapping
{
    public static partial class HumanoidMapping
    {
        public static int[] Suggest(SkeletonBone[]? skeleton)
        {
            int count = Roles.Count;
            int[] result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = -1;
            }
            if (skeleton == null || skeleton.Length == 0)
            {
                return result;
            }
            int n = skeleton.Length;
            string[] cores = new string[n];
            int[] sides = new int[n];
            int[] depths = new int[n];
            for (int i = 0; i < n; i++)
            {
                string name = skeleton[i].Name;
                cores[i] = Core(name);
                sides[i] = Side(name);
                depths[i] = Depth(skeleton, i);
            }
            result[(int)Role.Pelvis] = FindShallowestCenter(cores, sides, depths, Role.Pelvis);
            result[(int)Role.Head] = FindShallowestCenter(cores, sides, depths, Role.Head);
            result[(int)Role.Torso] = FindTorso(cores, sides, depths, skeleton, result[(int)Role.Head]);
            result[(int)Role.UpperArmLeft] = FindSide(cores, sides, depths, Role.UpperArmLeft, 1);
            result[(int)Role.UpperArmRight] = FindSide(cores, sides, depths, Role.UpperArmRight, 2);
            result[(int)Role.ForearmLeft] = FindSide(cores, sides, depths, Role.ForearmLeft, 1);
            result[(int)Role.ForearmRight] = FindSide(cores, sides, depths, Role.ForearmRight, 2);
            result[(int)Role.HandLeft] = FindSide(cores, sides, depths, Role.HandLeft, 1);
            result[(int)Role.HandRight] = FindSide(cores, sides, depths, Role.HandRight, 2);
            result[(int)Role.ThighLeft] = FindSide(cores, sides, depths, Role.ThighLeft, 1);
            result[(int)Role.ThighRight] = FindSide(cores, sides, depths, Role.ThighRight, 2);
            result[(int)Role.ShinLeft] = FindSide(cores, sides, depths, Role.ShinLeft, 1);
            result[(int)Role.ShinRight] = FindSide(cores, sides, depths, Role.ShinRight, 2);
            result[(int)Role.FootLeft] = FindSide(cores, sides, depths, Role.FootLeft, 1);
            result[(int)Role.FootRight] = FindSide(cores, sides, depths, Role.FootRight, 2);
            return result;
        }

        private static string Core(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            string lower = name.ToLowerInvariant();
            StringBuilder kept = new StringBuilder(lower.Length);
            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
                if (c == '_' || c == '-' || c == '.' || c == ' ' || c == ':' || c == '/' || c == '\\' || c == '|')
                {
                    continue;
                }
                kept.Append(c);
            }
            return kept.ToString();
        }

        private static int Side(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }
            string lower = name.ToLowerInvariant();
            if (lower.Contains("left"))
            {
                return 1;
            }
            if (lower.Contains("right"))
            {
                return 2;
            }
            char[] separators = new char[] { '_', '-', '.', ' ', ':', '/', '\\', '|' };
            string[] parts = lower.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "l")
                {
                    return 1;
                }
                if (parts[i] == "r")
                {
                    return 2;
                }
            }
            return 0;
        }

        private static int Depth(SkeletonBone[] skeleton, int bone)
        {
            int n = skeleton.Length;
            int depth = 0;
            int current = skeleton[bone].Parent;
            for (int step = 0; step <= n; step++)
            {
                if (current == -1)
                {
                    return depth;
                }
                if (current < -1 || current >= n)
                {
                    return depth;
                }
                depth++;
                if (current == bone)
                {
                    return depth;
                }
                current = skeleton[current].Parent;
            }
            return depth;
        }

        private static bool IsPelvisCore(string core)
        {
            return core.Contains("pelvis") || core.Contains("hips");
        }

        private static bool IsSpineCore(string core)
        {
            return core.Contains("spine") || core.Contains("chest") || core.Contains("torso");
        }

        private static bool IsHeadCore(string core)
        {
            return core.Contains("head");
        }

        private static bool IsUpperArmCore(string core)
        {
            return core.Contains("arm") && !core.Contains("fore");
        }

        private static bool IsShoulderCore(string core)
        {
            return core.Contains("shoulder");
        }

        private static bool IsForearmCore(string core)
        {
            return (core.Contains("fore") || core.Contains("lower")) && core.Contains("arm");
        }

        private static bool IsHandCore(string core)
        {
            return core.Contains("hand");
        }

        private static bool IsThighCore(string core)
        {
            return core.Contains("thigh") || core.Contains("upperleg") || core.Contains("upleg") || (core.Contains("upper") && core.Contains("leg"));
        }

        private static bool IsShinCore(string core)
        {
            if (core.Contains("shin") || core.Contains("calf") || core.Contains("lowerleg"))
            {
                return true;
            }
            if (core.Contains("lower") && core.Contains("leg"))
            {
                return true;
            }
            return core.Contains("leg") && !IsThighCore(core);
        }

        private static bool IsFootCore(string core)
        {
            return core.Contains("foot");
        }

        private static bool CoreMatches(Role role, string core)
        {
            if (role == Role.Pelvis)
            {
                return IsPelvisCore(core);
            }
            if (role == Role.Torso)
            {
                return IsSpineCore(core);
            }
            if (role == Role.Head)
            {
                return IsHeadCore(core);
            }
            if (role == Role.UpperArmLeft || role == Role.UpperArmRight)
            {
                return IsUpperArmCore(core);
            }
            if (role == Role.ForearmLeft || role == Role.ForearmRight)
            {
                return IsForearmCore(core);
            }
            if (role == Role.HandLeft || role == Role.HandRight)
            {
                return IsHandCore(core);
            }
            if (role == Role.ThighLeft || role == Role.ThighRight)
            {
                return IsThighCore(core);
            }
            if (role == Role.ShinLeft || role == Role.ShinRight)
            {
                return IsShinCore(core);
            }
            if (role == Role.FootLeft || role == Role.FootRight)
            {
                return IsFootCore(core);
            }
            return false;
        }

        private static int FindShallowestCenter(string[] cores, int[] sides, int[] depths, Role role)
        {
            int best = -1;
            for (int i = 0; i < cores.Length; i++)
            {
                if (sides[i] != 0)
                {
                    continue;
                }
                if (!CoreMatches(role, cores[i]))
                {
                    continue;
                }
                if (best < 0 || depths[i] < depths[best])
                {
                    best = i;
                }
            }
            return best;
        }

        private static int FindSide(string[] cores, int[] sides, int[] depths, Role role, int side)
        {
            int best = FindSideWith(cores, sides, depths, role, side, false);
            if (best < 0 && (role == Role.UpperArmLeft || role == Role.UpperArmRight))
            {
                best = FindSideWith(cores, sides, depths, role, side, true);
            }
            return best;
        }

        private static int FindSideWith(string[] cores, int[] sides, int[] depths, Role role, int side, bool shoulderFallback)
        {
            int best = -1;
            for (int i = 0; i < cores.Length; i++)
            {
                if (sides[i] != side)
                {
                    continue;
                }
                bool hit = shoulderFallback ? IsShoulderCore(cores[i]) : CoreMatches(role, cores[i]);
                if (!hit)
                {
                    continue;
                }
                if (best < 0 || depths[i] < depths[best])
                {
                    best = i;
                }
            }
            return best;
        }

        private static int FindTorso(string[] cores, int[] sides, int[] depths, SkeletonBone[] skeleton, int head)
        {
            int n = cores.Length;
            bool[] ancestorOfHead = new bool[n];
            if (head >= 0 && head < n)
            {
                int current = head;
                for (int step = 0; step <= n; step++)
                {
                    if (current < 0 || current >= n)
                    {
                        break;
                    }
                    ancestorOfHead[current] = true;
                    current = skeleton[current].Parent;
                }
            }
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                if (sides[i] != 0)
                {
                    continue;
                }
                if (!IsSpineCore(cores[i]))
                {
                    continue;
                }
                if (!ancestorOfHead[i])
                {
                    continue;
                }
                if (best < 0 || depths[i] > depths[best])
                {
                    best = i;
                }
            }
            if (best >= 0)
            {
                return best;
            }
            for (int i = 0; i < n; i++)
            {
                if (sides[i] != 0)
                {
                    continue;
                }
                if (!IsSpineCore(cores[i]))
                {
                    continue;
                }
                if (best < 0 || depths[i] > depths[best])
                {
                    best = i;
                }
            }
            return best;
        }
    }
}

using System;
using System.Collections.Generic;

namespace SA.Humanoid.Mapping
{
    public static partial class HumanoidMapping
    {
        public static MappingReport Map(SkeletonBone[]? skeleton, int[]? boneOfRole)
        {
            int count = Roles.Count;
            int bones = skeleton == null ? 0 : skeleton.Length;
            int[] copy = new int[count];
            for (int i = 0; i < count; i++)
            {
                copy[i] = -1;
            }
            if (boneOfRole == null || boneOfRole.Length != count)
            {
                Diagnostic[] single = new Diagnostic[1];
                single[0] = new Diagnostic(DiagnosticCode.WrongMapLength, Role.Pelvis, -1);
                return new MappingReport(copy, single, false);
            }
            for (int i = 0; i < count; i++)
            {
                copy[i] = boneOfRole[i];
            }
            List<Diagnostic> found = new List<Diagnostic>();
            bool[] valid = new bool[count];
            for (int r = 0; r < count; r++)
            {
                int v = copy[r];
                if (v == -1)
                {
                    found.Add(new Diagnostic(DiagnosticCode.RoleMissing, (Role)r, -1));
                    valid[r] = false;
                }
                else if (v < -1 || v >= bones)
                {
                    found.Add(new Diagnostic(DiagnosticCode.MapIndexOutOfRange, (Role)r, v));
                    valid[r] = false;
                }
                else
                {
                    valid[r] = true;
                }
            }
            if (bones > 0)
            {
                int[] first = new int[bones];
                for (int i = 0; i < bones; i++)
                {
                    first[i] = -1;
                }
                for (int r = 0; r < count; r++)
                {
                    if (!valid[r])
                    {
                        continue;
                    }
                    int v = copy[r];
                    if (first[v] < 0)
                    {
                        first[v] = r;
                    }
                    else
                    {
                        found.Add(new Diagnostic(DiagnosticCode.BoneUsedByTwoRoles, (Role)r, v));
                    }
                }
            }
            bool[] chainOk = new bool[count];
            for (int r = 0; r < count; r++)
            {
                chainOk[r] = true;
            }
            if (skeleton != null)
            {
                for (int r = 0; r < count; r++)
                {
                    if (!valid[r])
                    {
                        continue;
                    }
                    int issue = ChainIssue(skeleton, copy[r]);
                    if (issue == 1)
                    {
                        found.Add(new Diagnostic(DiagnosticCode.ParentIndexOutOfRange, (Role)r, copy[r]));
                        chainOk[r] = false;
                    }
                    else if (issue == 2)
                    {
                        found.Add(new Diagnostic(DiagnosticCode.ParentCycle, (Role)r, copy[r]));
                        chainOk[r] = false;
                    }
                }
            }
            for (int r = 0; r < count; r++)
            {
                if (!valid[r] || !chainOk[r])
                {
                    continue;
                }
                Role role = (Role)r;
                if (Roles.IsRoot(role))
                {
                    continue;
                }
                int p = (int)Roles.Parent(role);
                if (!valid[p] || !chainOk[p])
                {
                    continue;
                }
                int b = copy[r];
                int parentBone = copy[p];
                if (b == parentBone || IsProperDescendant(skeleton, bones, parentBone, b))
                {
                    found.Add(new Diagnostic(DiagnosticCode.RoleAboveItsParentRole, role, b));
                }
            }
            Diagnostic[] all = found.ToArray();
            SortByRoleThenCode(all);
            return new MappingReport(copy, all, all.Length == 0);
        }

        private static int ChainIssue(SkeletonBone[] skeleton, int bone)
        {
            int n = skeleton.Length;
            if (bone < 0 || bone >= n)
            {
                return 1;
            }
            bool[] seen = new bool[n];
            int current = bone;
            for (int step = 0; step <= n; step++)
            {
                if (current == -1)
                {
                    return 0;
                }
                if (current < -1 || current >= n)
                {
                    return 1;
                }
                if (seen[current])
                {
                    return 2;
                }
                seen[current] = true;
                int parent = skeleton[current].Parent;
                if (parent == -1)
                {
                    return 0;
                }
                if (parent < -1 || parent >= n)
                {
                    return 1;
                }
                current = parent;
            }
            return 2;
        }

        private static bool IsProperDescendant(SkeletonBone[]? skeleton, int bones, int bone, int ancestor)
        {
            if (skeleton == null)
            {
                return false;
            }
            if (bone < 0 || bone >= bones)
            {
                return false;
            }
            if (ancestor < 0 || ancestor >= bones)
            {
                return false;
            }
            if (bone == ancestor)
            {
                return false;
            }
            bool[] seen = new bool[bones];
            int current = skeleton[bone].Parent;
            for (int step = 0; step <= bones; step++)
            {
                if (current == ancestor)
                {
                    return true;
                }
                if (current == -1)
                {
                    return false;
                }
                if (current < -1 || current >= bones)
                {
                    return false;
                }
                if (seen[current])
                {
                    return false;
                }
                seen[current] = true;
                current = skeleton[current].Parent;
            }
            return false;
        }

        private static void SortByRoleThenCode(Diagnostic[] items)
        {
            for (int i = 1; i < items.Length; i++)
            {
                Diagnostic key = items[i];
                int j = i - 1;
                while (j >= 0 && Before(key, items[j]))
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = key;
            }
        }

        private static bool Before(Diagnostic a, Diagnostic b)
        {
            if (a.Role != b.Role)
            {
                return (int)a.Role < (int)b.Role;
            }
            return (int)a.Code < (int)b.Code;
        }
    }
}

using System;
using System.Runtime.InteropServices;

namespace SA.Animation
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ResidencySlot
    {
        public AssetId Artifact;
        public int Segment;
        public int Bytes;
        public uint Generation;
        public uint Pins;
        public ulong LastUse;
        public byte Occupied;
        public byte Retired;
    }

    public static class Residency
    {
        public static AnimationError Publish(Span<ResidencySlot> slots, int index, AssetId artifact, int segment, int bytes, ulong tick, out ClipHandle handle)
        {
            handle = default;
            if (index < 0 || index >= slots.Length || artifact.IsEmpty || segment < 0 || bytes < 0)
                return AnimationError.InvalidHandle;
            ref var slot = ref slots[index];
            if (slot.Occupied != 0 || slot.Retired != 0 || slot.Pins != 0)
                return AnimationError.BudgetExceeded;
            var generation = slot.Generation == 0 ? 1u : slot.Generation;
            slot = new ResidencySlot { Artifact = artifact, Segment = segment, Bytes = bytes, Generation = generation, LastUse = tick, Occupied = 1 };
            handle = new ClipHandle((uint)index, generation);
            return AnimationError.None;
        }

        public static AnimationError Pin(Span<ResidencySlot> slots, ClipHandle handle, ulong tick)
        {
            if (!Valid(slots, handle) || slots[(int)handle.Slot].Pins == uint.MaxValue)
                return AnimationError.InvalidHandle;
            ref var slot = ref slots[(int)handle.Slot];
            slot.Pins++;
            slot.LastUse = Math.Max(slot.LastUse, tick);
            return AnimationError.None;
        }

        public static AnimationError Release(Span<ResidencySlot> slots, ClipHandle handle)
        {
            if (!Valid(slots, handle) || slots[(int)handle.Slot].Pins == 0)
                return AnimationError.InvalidHandle;
            slots[(int)handle.Slot].Pins--;
            return AnimationError.None;
        }

        public static AnimationError Evict(Span<ResidencySlot> slots, int index, out ResidencySlot payload)
        {
            payload = default;
            if (index < 0 || index >= slots.Length || slots[index].Occupied == 0)
                return AnimationError.InvalidHandle;
            ref var slot = ref slots[index];
            if (slot.Pins != 0)
                return AnimationError.BudgetExceeded;
            payload = slot;
            slot = payload.Generation == uint.MaxValue ? new ResidencySlot { Generation = uint.MaxValue, Retired = 1 }
                : new ResidencySlot { Generation = payload.Generation + 1 };
            return AnimationError.None;
        }

        public static int EvictionCandidate(ReadOnlySpan<ResidencySlot> slots)
        {
            var candidate = -1;
            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.Occupied == 0 || slot.Pins != 0) continue;
                if (candidate < 0 || Before(slot, slots[candidate])) candidate = i;
            }
            return candidate;
        }

        public static bool Valid(ReadOnlySpan<ResidencySlot> slots, ClipHandle handle) => handle.Generation != 0 && handle.Slot < (uint)slots.Length
            && slots[(int)handle.Slot].Occupied != 0 && slots[(int)handle.Slot].Retired == 0 && slots[(int)handle.Slot].Generation == handle.Generation;

        static bool Before(in ResidencySlot a, in ResidencySlot b)
        {
            if (a.LastUse != b.LastUse) return a.LastUse < b.LastUse;
            if (a.Artifact.A != b.Artifact.A) return a.Artifact.A < b.Artifact.A;
            if (a.Artifact.B != b.Artifact.B) return a.Artifact.B < b.Artifact.B;
            if (a.Artifact.C != b.Artifact.C) return a.Artifact.C < b.Artifact.C;
            if (a.Artifact.D != b.Artifact.D) return a.Artifact.D < b.Artifact.D;
            return a.Segment < b.Segment;
        }
    }
}

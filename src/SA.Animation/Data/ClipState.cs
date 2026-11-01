using System.Runtime.InteropServices;
using SA.Motion.Numerics;

namespace SA.Animation
{
    public enum ClipMode : byte
    {
        Override,
        Additive
    }

    public enum Wrap : byte
    {
        Loop,
        Clamp,
        PingPong
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ClipIndex
    {
        public readonly ushort Value;

        public ClipIndex(ushort value) => Value = value;

        public static ClipIndex None => new ClipIndex(ushort.MaxValue);
        public bool IsNone => Value == ushort.MaxValue;
        public bool Is(ClipIndex other) => Value == other.Value;
        public static ClipIndex At(int index, int count) => index >= 0 && index < count && index < ushort.MaxValue ? new ClipIndex((ushort)index) : None;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct MaskIndex
    {
        public readonly ushort Value;

        public MaskIndex(ushort value) => Value = value;

        public static MaskIndex Full => new MaskIndex(0);
        public bool Is(MaskIndex other) => Value == other.Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClipState
    {
        public ClipIndex Clip;
        public byte Layer;
        public ClipMode Mode;
        public MaskIndex Mask;
        public Wrap Wrap;
        public byte Synced;
        public float Phase;
        public float Speed;
        public float Weight;
        public float Fade;
        public float FadeRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PlayOptions
    {
        public readonly byte Layer;
        public readonly ClipMode Mode;
        public readonly MaskIndex Mask;
        public readonly Wrap Wrap;
        public readonly float Speed;
        public readonly bool Restart;

        public PlayOptions(byte layer, ClipMode mode, MaskIndex mask, Wrap wrap, float speed, bool restart)
        {
            Layer = layer;
            Mode = mode;
            Mask = mask;
            Wrap = wrap;
            Speed = speed;
            Restart = restart;
        }

        public static PlayOptions Default => new PlayOptions(0, ClipMode.Override, MaskIndex.Full, Wrap.Loop, 1f, false);

        public PlayOptions WithLayer(byte layer) => new PlayOptions(layer, Mode, Mask, Wrap, Speed, Restart);
        public PlayOptions WithMode(ClipMode mode) => new PlayOptions(Layer, mode, Mask, Wrap, Speed, Restart);
        public PlayOptions WithMask(MaskIndex mask) => new PlayOptions(Layer, Mode, mask, Wrap, Speed, Restart);
        public PlayOptions WithWrap(Wrap wrap) => new PlayOptions(Layer, Mode, Mask, wrap, Speed, Restart);
        public PlayOptions WithSpeed(float speed) => new PlayOptions(Layer, Mode, Mask, Wrap, speed, Restart);
        public PlayOptions WithRestart(bool restart) => new PlayOptions(Layer, Mode, Mask, Wrap, Speed, restart);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PoseSum
    {
        public Float3 Position;
        public Quaternion Rotation;
        public Float3 Scale;
        public float Weight;
    }
}

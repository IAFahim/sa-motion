using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

namespace SA.Reactions
{
    public struct HitReactionState : IComponentData
    {
        public uint ClipName;
        public byte Layer;
        public float Weight;
        public float FadeSeconds;
        public ClipId ActiveClip;
        public bool Active;
    }

    public static class HitReactions
    {
        public static bool Play(EntityManager manager, Entity rig, float strength = 1f)
        {
            if (!manager.HasComponent<HitReactionState>(rig) || !manager.HasBuffer<ClipAsset>(rig) || !manager.HasBuffer<ClipState>(rig))
                return false;
            var reaction = manager.GetComponentData<HitReactionState>(rig);
            var clip = Names.Clip(manager.GetBuffer<ClipAsset>(rig, true).AsNativeArray().AsReadOnlySpan(), reaction.ClipName);
            if (clip.IsNone)
                return false;
            var mask = MaskId.Full;
            if (manager.HasBuffer<MaskAsset>(rig))
                mask = Names.Mask(manager.GetBuffer<MaskAsset>(rig, true), new FixedString64Bytes("UpperBody"));
            var states = manager.GetBuffer<ClipState>(rig);
            Playback.CrossFade(ref states, clip, reaction.FadeSeconds,
                PlayOptions.Default.WithLayer(reaction.Layer).WithMask(mask).WithWrap(Wrap.Clamp).WithRestart(true));
            var index = Playback.Find(states, reaction.Layer, clip);
            if (index >= 0)
            {
                var state = states[index];
                state.Weight = math.saturate(Playback.Finite(strength) * reaction.Weight);
                states[index] = state;
            }
            reaction.ActiveClip = clip;
            reaction.Active = true;
            manager.SetComponentData(rig, reaction);
            return true;
        }
    }

    [UpdateInGroup(typeof(AnimationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(PlaybackSystem))]
    public partial struct HitReactionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state) => state.Dependency = new EndJob().ScheduleParallel(state.Dependency);

        [BurstCompile]
        partial struct EndJob : IJobEntity
        {
            void Execute(ref HitReactionState reaction, ref DynamicBuffer<ClipState> states)
            {
                if (reaction.Active && Playback.HasFinished(states, reaction.Layer, reaction.ActiveClip))
                {
                    Playback.Stop(ref states, reaction.Layer, reaction.FadeSeconds);
                    reaction.Active = false;
                }
            }
        }
    }
}

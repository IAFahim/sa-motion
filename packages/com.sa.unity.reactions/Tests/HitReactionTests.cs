using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;

namespace SA.Reactions.Tests
{
    public class HitReactionTests
    {
        static BlobAssetReference<Clip> Clip(uint name)
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var clip = ref builder.ConstructRoot<Clip>();
            clip.NameHash = name;
            clip.Duration = 1f;
            clip.FrameCount = 2;
            return builder.CreateBlobAssetReference<Clip>(Allocator.Persistent);
        }

        [Test]
        public void FlinchRestartsOnHitAndFadesOutWithoutReplacingLocomotion()
        {
            using var idle = Clip(10);
            using var hit = Clip(20);
            using var world = new World("SA.Flinch");
            var manager = world.EntityManager;
            var rig = manager.CreateEntity();
            var assets = manager.AddBuffer<ClipAsset>(rig);
            assets.Add(new ClipAsset { Value = idle });
            assets.Add(new ClipAsset { Value = hit });
            manager.AddBuffer<ClipState>(rig);
            manager.AddComponentData(rig, new HitReactionState { ClipName = 20, Layer = 2, Weight = 0.8f, FadeSeconds = 0.08f });
            assets = manager.GetBuffer<ClipAsset>(rig);
            var states = manager.GetBuffer<ClipState>(rig);
            var idleId = Names.Clip(assets.AsNativeArray().AsReadOnlySpan(), 10);
            var hitId = Names.Clip(assets.AsNativeArray().AsReadOnlySpan(), 20);
            Playback.Play(ref states, idleId, PlayOptions.Default);
            Playback.Step(ref states, assets.AsNativeArray().AsReadOnlySpan(), 0.3f);
            Assert.IsTrue(HitReactions.Play(manager, rig));
            states = manager.GetBuffer<ClipState>(rig);
            Assert.AreEqual(0.3f, Playback.NormalizedTime(states, 0, idleId), 1e-5f);
            Playback.Step(ref states, assets.AsNativeArray().AsReadOnlySpan(), 0.2f);
            Assert.IsTrue(HitReactions.Play(manager, rig));
            states = manager.GetBuffer<ClipState>(rig);
            Assert.AreEqual(0f, Playback.NormalizedTime(states, 2, hitId));
            Playback.Step(ref states, assets.AsNativeArray().AsReadOnlySpan(), 1.1f);
            var system = world.GetOrCreateSystem<HitReactionSystem>();
            world.SetTime(new TimeData(1.6, 0.02f));
            system.Update(world.Unmanaged);
            manager.CompleteAllTrackedJobs();
            Assert.IsFalse(manager.GetComponentData<HitReactionState>(rig).Active);
            states = manager.GetBuffer<ClipState>(rig);
            assets = manager.GetBuffer<ClipAsset>(rig);
            Playback.Step(ref states, assets.AsNativeArray().AsReadOnlySpan(), 0.2f);
            Assert.IsTrue(Playback.IsPlaying(states, 0, idleId));
            Assert.IsFalse(Playback.IsPlaying(states, 2, hitId));
        }
    }
}

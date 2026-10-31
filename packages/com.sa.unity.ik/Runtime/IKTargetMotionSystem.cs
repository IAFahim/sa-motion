using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace SA.IK
{
    public struct IKTargetMotion : IComponentData
    {
        public float3 Origin;
        public float3 Range;
        public float Frequency;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(AnimationSystemGroup))]
    public partial struct IKTargetMotionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state) => state.Dependency = new MoveJob { Time = (float)SystemAPI.Time.ElapsedTime }.ScheduleParallel(state.Dependency);

        [BurstCompile]
        partial struct MoveJob : IJobEntity
        {
            public float Time;
            void Execute(ref LocalTransform transform, in IKTargetMotion motion)
            {
                var phase = Time * motion.Frequency * 2f * math.PI;
                transform.Position = motion.Origin + motion.Range * new float3(math.sin(phase), math.sin(phase * 0.7f), math.sin(phase * 0.5f));
            }
        }
    }
}

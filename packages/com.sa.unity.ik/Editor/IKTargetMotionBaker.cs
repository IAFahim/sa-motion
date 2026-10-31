using Unity.Entities;

namespace SA.IK.Editor
{
    class IKTargetMotionBaker : Baker<IKTargetMotionAuthoring>
    {
        public override void Bake(IKTargetMotionAuthoring authoring)
        {
            AddComponent(GetEntity(TransformUsageFlags.Dynamic), new IKTargetMotion
            { Origin = authoring.transform.localPosition, Range = authoring.Range, Frequency = authoring.Frequency });
        }
    }
}

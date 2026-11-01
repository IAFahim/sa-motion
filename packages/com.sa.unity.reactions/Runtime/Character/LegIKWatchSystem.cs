#if UNITY_EDITOR
using System.Collections.Generic;
using SA.Motion.Legs;
using SA.Motion.Unity;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace SA.Reactions
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LegIKWatchSystem : SystemBase
    {
        public const string ContactsKey = "SA.LegIK.Contacts";
        const float Gap = 0.5f;
        const float MarkerSize = 0.06f;

        enum Told : byte
        {
            Nothing,
            Warned,
            Working
        }

        struct Watch
        {
            public float Since;
            public float Seen;
            public Told Told;
        }

        readonly Dictionary<Entity, Watch> watches = new Dictionary<Entity, Watch>();

        protected override void OnUpdate()
        {
            var now = (float)SystemAPI.Time.ElapsedTime;
            var contacts = EditorPrefs.GetBool(ContactsKey, true);
            foreach (var (ik, feet, entity) in SystemAPI.Query<RefRO<LegIK>, DynamicBuffer<LegIKFoot>>().WithEntityAccess())
            {
                var planted = false;
                for (var i = 0; i < feet.Length; i++)
                {
                    if (feet[i].State.Planted == 0)
                        continue;
                    planted = true;
                    if (contacts)
                        Mark((Vector3)Conversions.ToUnity(feet[i].State.Anchor));
                }
                watches.TryGetValue(entity, out var watch);
                if (now - watch.Seen > Gap)
                    watch.Since = now;
                watch.Seen = now;
                if (planted)
                {
                    watch.Since = now;
                    if (watch.Told != Told.Working)
                        Debug.Log("SA Leg IK on '" + Name(entity) + "': feet are planted. Green crosses in the Scene view mark each planted foot (Tools > SA > Show Leg IK Contacts turns them off).");
                    watch.Told = Told.Working;
                }
                else if (watch.Told == Told.Nothing)
                {
                    var cause = LegIKAdvice.Diagnose(ik.ValueRO.Weight, ClipWeight(entity), Reach(feet), LegLength(entity, ik.ValueRO), false, now - watch.Since);
                    if (cause != LegIKCause.Working)
                    {
                        Debug.LogWarning("SA Leg IK on '" + Name(entity) + "': " + LegIKAdvice.Message(cause));
                        watch.Told = Told.Warned;
                    }
                }
                watches[entity] = watch;
            }
        }

        float ClipWeight(Entity entity)
        {
            if (!EntityManager.HasBuffer<ClipAsset>(entity) || !EntityManager.HasBuffer<ClipState>(entity))
                return 1f;
            return LegIKSystem.ClipWeight(
                EntityManager.GetBuffer<ClipAsset>(entity, true).AsNativeArray().AsReadOnlySpan(),
                EntityManager.GetBuffer<ClipState>(entity, true).AsNativeArray().AsReadOnlySpan());
        }

        float Reach(DynamicBuffer<LegIKFoot> feet)
        {
            var reach = 0f;
            for (var i = 0; i < feet.Length; i++)
            {
                var scale = math.length(EntityManager.GetComponentData<LocalToWorld>(feet[i].Foot).Value.c0.xyz);
                var shape = math.max(math.length(Conversions.ToUnity(feet[i].Shape.Heel)), math.length(Conversions.ToUnity(feet[i].Shape.Ball)));
                reach = math.max(reach, scale * shape);
            }
            return reach;
        }

        float LegLength(Entity entity, in LegIK ik) =>
            ik.BodyScale * LegParams.ReferenceLegLength * math.length(EntityManager.GetComponentData<LocalToWorld>(entity).Value.c1.xyz);

        string Name(Entity entity)
        {
            var name = EntityManager.GetName(entity);
            return string.IsNullOrEmpty(name) ? "a character" : name;
        }

        static void Mark(Vector3 point)
        {
            Debug.DrawLine(point - Vector3.right * MarkerSize, point + Vector3.right * MarkerSize, Color.green);
            Debug.DrawLine(point - Vector3.forward * MarkerSize, point + Vector3.forward * MarkerSize, Color.green);
            Debug.DrawLine(point, point + Vector3.up * MarkerSize * 2f, Color.green);
        }
    }
}
#endif

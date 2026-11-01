using System.Globalization;
using System.IO;
using System.Text;
using SA;
using SA.Reactions;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class LegIKProbe : MonoBehaviour
{
    const string ModeFile = "Temp/sa/leg_probe_mode.txt";
    const string OutputFile = "Temp/sa/leg_probe.csv";
    const string SpeedsFile = "Temp/sa/leg_probe_speeds.txt";
    float[] speeds;
    bool placed;

    EntityManager manager;
    EntityQuery rigs;
    StringBuilder rows;
    quaternion[] last;
    float clock;
    bool enabledIK = true;

    void Start()
    {
        manager = World.DefaultGameObjectInjectionWorld.EntityManager;
        speeds = File.Exists(SpeedsFile) ? System.Array.ConvertAll(File.ReadAllText(SpeedsFile).Trim().Split(' '), t => float.Parse(t, CultureInfo.InvariantCulture)) : new float[3];
        enabledIK = !File.Exists(ModeFile) || File.ReadAllText(ModeFile).Trim() != "off";
        rigs = new EntityQueryBuilder(Allocator.Temp).WithAll<LegIK, LegIKFoot>().WithNone<Ragdoll>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build(manager);
        rows = new StringBuilder("time,leg_ik,foot,planted,x,y,z,step_deg,weight,pelvis_shift,segment,step_upper,step_lower,step_foot,reach,anchor_y,anchor_z\n");
    }

    void Update()
    {
        clock += Time.deltaTime;
        using var entities = rigs.ToEntityArray(Allocator.Temp);
        foreach (var rig in entities)
        {
            Travel(rig);
            if (manager.IsComponentEnabled<LegIK>(rig) != enabledIK)
                manager.SetComponentEnabled<LegIK>(rig, enabledIK);
            var feet = manager.GetBuffer<LegIKFoot>(rig, true);
            var ik = manager.GetComponentData<LegIK>(rig);
            if (last == null)
                last = new quaternion[feet.Length * 3];
            for (var f = 0; f < feet.Length; f++)
            {
                var leg = feet[f];
                var bones = new[] { leg.Upper, leg.Lower, leg.Foot };
                var step = 0f;
                var each = new float[3];
                for (var b = 0; b < 3; b++)
                {
                    var rotation = manager.GetComponentData<LocalTransform>(bones[b]).Rotation;
                    var slot = f * 3 + b;
                    if (clock > 0.5f && !last[slot].Equals(default(quaternion)))
                        each[b] = math.degrees(2f * math.acos(math.min(1f, math.abs(math.dot(rotation.value, last[slot].value)))));
                    step = math.max(step, each[b]);
                    last[slot] = rotation;
                }
                var position = manager.GetComponentData<LocalToWorld>(leg.Foot).Position;
                var hip = manager.GetComponentData<LocalToWorld>(leg.Upper).Position;
                var knee = manager.GetComponentData<LocalToWorld>(leg.Lower).Position;
                var reach = math.distance(hip, position) / (math.distance(hip, knee) + math.distance(knee, position));
                rows.Append(string.Format(CultureInfo.InvariantCulture, "{0:F3},{1},{2},{3},{4:F4},{5:F4},{6:F4},{7:F2},{8:F3},{9:F4},{10},{11:F1},{12:F1},{13:F1},{14:F4},{15:F4},{16:F4}\n",
                    clock, enabledIK ? 1 : 0, f, leg.State.Planted, position.x, position.y, position.z, step, ik.State.Weight.Value, ik.State.Pelvis.Value, (int)(clock % 28f / 4f), each[0], each[1], each[2], reach, leg.State.Anchor.Y, leg.State.Anchor.Z));
            }
        }
    }

    void Travel(Entity rig)
    {
        var transform = manager.GetComponentData<LocalTransform>(rig);
        if (!placed)
        {
            transform.Position.z = -29f;
            placed = true;
        }
        var segment = (int)(clock % 28f / 4f);
        var speed = segment switch { 1 => speeds[0], 2 => speeds[1], 3 => speeds[2], 4 => speeds[1], 5 => speeds[0], _ => 0f };
        transform.Position.z += speed * Time.deltaTime;
        manager.SetComponentData(rig, transform);
    }

    void OnDestroy()
    {
        if (rows == null)
            return;
        Directory.CreateDirectory("Temp/sa");
        File.WriteAllText(OutputFile, rows.ToString());
    }
}

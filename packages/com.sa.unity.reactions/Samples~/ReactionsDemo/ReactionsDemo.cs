using System.IO;
using System.Text;
using SA;
using SA.Humanoid;
using SA.Reactions;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using BoxCollider = Unity.Physics.BoxCollider;
using Collider = Unity.Physics.Collider;

public class ReactionsDemo : MonoBehaviour
{
    public float CycleSeconds = 28f;
    public bool Scripted = true;
    public bool WriteTelemetry = true;
    public bool HighRatePhysics;

    BlobAssetReference<Collider> ground;
    EntityManager manager;
    EntityQuery ragdolls;
    EntityQuery inertials;
    float clock;
    float lastHit = -100f;
    string lastHitLabel = "";
    StringBuilder telemetry;
    float nextSample;

    void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        manager = World.DefaultGameObjectInjectionWorld.EntityManager;
        if (HighRatePhysics)
            World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<FixedStepSimulationSystemGroup>().Timestep = 1f / 480f;
        var step = PhysicsStep.Default;
        step.SubstepCount = HighRatePhysics ? 1 : 4;
        step.SolverIterationCount = HighRatePhysics ? 8 : 16;
        manager.CreateSingleton(step);
        ground = BoxCollider.Create(new BoxGeometry { Center = float3.zero, Orientation = quaternion.identity, Size = new float3(60f, 1f, 60f), BevelRadius = 0f }, CollisionFilter.Default, new Unity.Physics.Material { Friction = 0.9f, Restitution = 0f, CollisionResponse = CollisionResponsePolicy.Collide });
        var floor = manager.CreateEntity(typeof(LocalTransform), typeof(PhysicsCollider), typeof(PhysicsWorldIndex));
        manager.SetComponentData(floor, LocalTransform.FromPosition(new float3(0f, -0.5f, 0f)));
        manager.SetComponentData(floor, new PhysicsCollider { Value = ground });
        ragdolls = manager.CreateEntityQuery(typeof(Ragdoll));
        inertials = manager.CreateEntityQuery(ComponentType.ReadWrite<TransitionIntent>(), ComponentType.ReadOnly<Rig>(), ComponentType.Exclude<Ragdoll>());
        telemetry = new StringBuilder("time,mode,strength,pelvis_y,head_y,head_x,head_z,event,decision,feet,margin,distL,distR\n");
    }

    void OnDestroy()
    {
        if (ground.IsCreated)
            ground.Dispose();
        if (WriteTelemetry && telemetry != null)
        {
            Directory.CreateDirectory("Temp/sa");
            File.WriteAllText("Temp/sa/demo_telemetry.csv", telemetry.ToString());
        }
    }

    void Update()
    {
        clock += Time.deltaTime;
        var cycle = clock % CycleSeconds;
        DriveInertial(cycle);
        DriveRagdoll(cycle);
        if (Scripted)
            ScriptedHits(cycle);
        ManualHits();
        Sample();
    }

    void DriveInertial(float cycle)
    {
        using var rigs = inertials.ToEntityArray(Allocator.Temp);
        foreach (var rig in rigs)
        {
            var clips = manager.GetBuffer<ClipAsset>(rig, true);
            var masks = manager.GetBuffer<MaskAsset>(rig, true);
            var inPlace = Names.Mask(masks, "InPlace");
            var segment = (int)(cycle / 4f);
            var name = segment switch { 1 => "Kyle_Walk", 2 => "Kyle_Run", 3 => "Kyle_Sprint", 4 => "Kyle_Run", 5 => "Kyle_Walk", _ => "Idle" };
            var states = manager.GetBuffer<ClipState>(rig);
            var intent = manager.GetComponentData<TransitionIntent>(rig);
            TransitionPlayback.Play(ref states, ref intent, (uint)(segment + 1), Names.Clip(clips, name), 0.4f, PlayOptions.Default.WithMask(inPlace));
            manager.SetComponentData(rig, intent);
        }
    }

    void DriveRagdoll(float cycle)
    {
        using var rigs = ragdolls.ToEntityArray(Allocator.Temp);
        foreach (var rig in rigs)
        {
            var clips = manager.GetBuffer<ClipAsset>(rig, true);
            var masks = manager.GetBuffer<MaskAsset>(rig, true);
            var states = manager.GetBuffer<ClipState>(rig);
            Playback.Play(ref states, Names.Clip(clips, "Idle"), PlayOptions.Default);
            if (cycle > 14f && cycle < 18f)
                Playback.CrossFade(ref states, Names.Clip(clips, "Kyle_Clap"), 0.3f, PlayOptions.Default.WithLayer(1).WithMode(ClipMode.Additive).WithMask(Names.Mask(masks, "UpperBody")));
            else
                Playback.Stop(ref states, 1, 0.3f);
        }
    }

    readonly bool[] done = new bool[4];
    int cycleIndex;

    void ScriptedHits(float cycle)
    {
        var index = (int)(clock / CycleSeconds);
        if (index != cycleIndex)
        {
            cycleIndex = index;
            System.Array.Clear(done, 0, done.Length);
        }
        Fire(cycle, 0, 5f, Role.Torso, new float3(-1f, 0.1f, 0f), 150f, "light shove from the right");
        Fire(cycle, 1, 9f, Role.Torso, new float3(0f, 0.1f, -1f), 240f, "shove from the front");
        Fire(cycle, 2, 12f, Role.Head, new float3(1f, 0.2f, 0.3f), 160f, "hit to the head");
        Fire(cycle, 3, 20f, Role.Torso, new float3(-0.3f, 0.25f, -1f), 330f, "knockdown");
    }

    void Fire(float cycle, int id, float at, Role role, float3 direction, float impulse, string label)
    {
        if (cycle < at || done[id])
            return;
        done[id] = true;
        Hit(role, direction, impulse, label);
    }

    void ManualHits()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;
        if (keyboard.digit1Key.wasPressedThisFrame) Hit(Role.Torso, new float3(-1f, 0.1f, 0f), 150f, "light shove");
        if (keyboard.digit2Key.wasPressedThisFrame) Hit(Role.Torso, new float3(0f, 0.1f, -1f), 240f, "shove from the front");
        if (keyboard.digit3Key.wasPressedThisFrame) Hit(Role.Head, new float3(1f, 0.2f, 0.3f), 160f, "hit to the head");
        if (keyboard.digit4Key.wasPressedThisFrame) Hit(Role.Torso, new float3(-0.3f, 0.25f, -1f), 330f, "knockdown");
    }

    public void Hit(Role role, float3 direction, float impulse, string label)
    {
        using var rigs = ragdolls.ToEntityArray(Allocator.Temp);
        foreach (var rig in rigs)
        {
            var bodies = manager.GetBuffer<RagdollBody>(rig, true);
            if (bodies.Length <= (int)role)
                continue;
            var transform = manager.GetComponentData<LocalTransform>(bodies[(int)role].Body);
            manager.GetBuffer<RagdollHit>(rig).Add(new RagdollHit { Role = (int)role, Impulse = math.normalize(direction) * impulse, Point = transform.Position });
        }
        lastHit = clock;
        lastHitLabel = label;
    }

    void Sample()
    {
        if (!WriteTelemetry || clock < nextSample)
            return;
        nextSample = clock + 0.1f;
        using var rigs = ragdolls.ToEntityArray(Allocator.Temp);
        foreach (var rig in rigs)
        {
            var ragdoll = manager.GetComponentData<Ragdoll>(rig);
            var bodies = manager.GetBuffer<RagdollBody>(rig, true);
            if (ragdoll.Spawned != 1 || bodies.Length < Roles.Count)
                continue;
            var pelvis = manager.GetComponentData<LocalTransform>(bodies[(int)Role.Pelvis].Body).Position;
            var head = manager.GetComponentData<LocalTransform>(bodies[(int)Role.Head].Body).Position;
            telemetry.Append($"{clock:F2},{ragdoll.Mode},{ragdoll.Strength:F2},{pelvis.y:F3},{head.y:F3},{head.x - pelvis.x:F3},{head.z - pelvis.z:F3},{(clock - lastHit < 0.15f ? lastHitLabel : "")},{(manager.HasComponent<BalanceControl>(rig) ? manager.GetComponentData<BalanceControl>(rig).Decision : -1)},{(manager.HasComponent<BalanceControl>(rig) ? manager.GetComponentData<BalanceControl>(rig).FootContacts : -1)},{(manager.HasComponent<BalanceControl>(rig) ? manager.GetComponentData<BalanceControl>(rig).Margin : 0f):F2},{(manager.HasComponent<BalanceControl>(rig) ? manager.GetComponentData<BalanceControl>(rig).DistanceLeft : 0f):F3},{(manager.HasComponent<BalanceControl>(rig) ? manager.GetComponentData<BalanceControl>(rig).DistanceRight : 0f):F3}\n");
        }
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(20, 14, 900, 30), "SA: inertialized transitions (right)  |  automatic active ragdoll (left)", style);
        var small = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        GUI.Label(new Rect(20, 44, 900, 24), "keys 1 shove   2 front shove   3 head hit   4 knockdown", small);
        if (clock - lastHit < 2f)
            GUI.Label(new Rect(20, 70, 900, 28), "hit: " + lastHitLabel, style);
    }
}

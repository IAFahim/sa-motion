using System.Collections.Generic;
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
    public string IdleName = "Idle";
    public string[] LocomotionNames = { "Kyle_Walk", "Kyle_Run", "Kyle_Sprint" };
    public string AdditiveName = "Kyle_Clap";
    public string InPlaceMaskName = "InPlace";
    public string UpperBodyMaskName = "UpperBody";
    public bool UseMasks = true;
    [Tooltip("Stop Play mode by itself after this many seconds of game time. 0 means never.")]
    public float StopAfterSeconds;
    [Tooltip("The visible floor. The physics ground is built from its position, tilt and size, so characters stand on what you see. Found by the name Floor when empty.")]
    public Transform Floor;

    BlobAssetReference<Collider> ground;
    float3 groundPoint;
    float3 groundUp;
    readonly HashSet<Entity> placed = new HashSet<Entity>();
    EntityManager manager;
    EntityQuery ragdolls;
    EntityQuery inertials;
    float clock;
    float lastHit = -100f;
    string lastHitLabel = "";
    StringBuilder telemetry;
    float nextSample;

    public string[] DisabledSystems = { "ShowcaseSystem", "HeadLookSystem" };

    void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        manager = World.DefaultGameObjectInjectionWorld.EntityManager;
        DisableSystems(World.DefaultGameObjectInjectionWorld);
        if (HighRatePhysics)
            World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<FixedStepSimulationSystemGroup>().Timestep = 1f / 480f;
        var step = PhysicsStep.Default;
        step.SubstepCount = HighRatePhysics ? 1 : 4;
        step.SolverIterationCount = HighRatePhysics ? 8 : 16;
        manager.CreateSingleton(step);
        var visual = Floor != null ? Floor : GameObject.Find("Floor")?.transform;
        var size = visual != null ? new float3(10f * math.abs(visual.lossyScale.x), 1f, 10f * math.abs(visual.lossyScale.z)) : new float3(60f, 1f, 60f);
        groundPoint = visual != null ? (float3)visual.position : float3.zero;
        groundUp = visual != null ? math.normalizesafe((float3)(visual.rotation * Vector3.up), new float3(0f, 1f, 0f)) : new float3(0f, 1f, 0f);
        ground = BoxCollider.Create(new BoxGeometry { Center = new float3(0f, -0.5f, 0f), Orientation = quaternion.identity, Size = size, BevelRadius = 0f }, CollisionFilter.Default, new Unity.Physics.Material { Friction = 0.9f, Restitution = 0f, CollisionResponse = CollisionResponsePolicy.Collide });
        var floor = manager.CreateEntity(typeof(LocalTransform), typeof(PhysicsCollider), typeof(PhysicsWorldIndex));
        manager.SetComponentData(floor, LocalTransform.FromPositionRotation(groundPoint, visual != null ? (quaternion)visual.rotation : quaternion.identity));
        manager.SetComponentData(floor, new PhysicsCollider { Value = ground });
        ragdolls = manager.CreateEntityQuery(typeof(Ragdoll));
        inertials = manager.CreateEntityQuery(ComponentType.ReadWrite<TransitionIntent>(), ComponentType.ReadOnly<Rig>(), ComponentType.Exclude<Ragdoll>());
        telemetry = new StringBuilder("time,mode,strength,pelvis_y,head_y,head_x,head_z,event,decision,feet,margin,distL,distR,jump,blend\n");
    }

    static void StopPlaying()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    void DisableSystems(World world)
    {
        foreach (var name in DisabledSystems)
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(name);
                if (type == null)
                    continue;
                var handle = world.Unmanaged.GetExistingUnmanagedSystem(type);
                if (handle != SystemHandle.Null)
                    world.Unmanaged.ResolveSystemStateRef(handle).Enabled = false;
            }
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
        if (StopAfterSeconds > 0f && clock >= StopAfterSeconds)
            StopPlaying();
        var cycle = clock % CycleSeconds;
        PlaceOnGround();
        DriveInertial(cycle);
        DriveRagdoll(cycle);
        if (Scripted)
            ScriptedHits(cycle);
        ManualHits();
        TrackJump();
        Sample();
    }

    void PlaceOnGround()
    {
        PlaceOnGround(inertials);
        PlaceOnGround(ragdolls);
    }

    void PlaceOnGround(EntityQuery query)
    {
        using var rigs = query.ToEntityArray(Allocator.Temp);
        foreach (var rig in rigs)
        {
            if (!placed.Add(rig) || !manager.HasComponent<LocalTransform>(rig))
                continue;
            var transform = manager.GetComponentData<LocalTransform>(rig);
            transform.Position.y += GroundHeight(transform.Position.x, transform.Position.z);
            manager.SetComponentData(rig, transform);
        }
    }

    float GroundHeight(float x, float z) =>
        groundUp.y > 0.01f ? groundPoint.y - (groundUp.x * (x - groundPoint.x) + groundUp.z * (z - groundPoint.z)) / groundUp.y : 0f;

    void DriveInertial(float cycle)
    {
        using var rigs = inertials.ToEntityArray(Allocator.Temp);
        foreach (var rig in rigs)
        {
            var clips = manager.GetBuffer<ClipAsset>(rig, true);
            var masks = manager.GetBuffer<MaskAsset>(rig, true);
            var options = UseMasks ? PlayOptions.Default.WithMask(Names.Mask(masks, InPlaceMaskName)) : PlayOptions.Default;
            var segment = (int)(cycle / 4f);
            var name = segment switch { 1 => LocomotionNames[0], 2 => LocomotionNames[1], 3 => LocomotionNames[2], 4 => LocomotionNames[1], 5 => LocomotionNames[0], _ => IdleName };
            var states = manager.GetBuffer<ClipState>(rig);
            var intent = manager.GetComponentData<TransitionIntent>(rig);
            TransitionPlayback.PlaySynced(ref states, ref intent, (uint)(segment + 1), Names.Clip(clips, name), 0.4f, options);
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
            Playback.Play(ref states, Names.Clip(clips, IdleName), PlayOptions.Default);
            if (!string.IsNullOrEmpty(AdditiveName) && cycle > 14f && cycle < 18f)
                Playback.CrossFade(ref states, Names.Clip(clips, AdditiveName), 0.3f, PlayOptions.Default.WithLayer(1).WithMode(ClipMode.Additive).WithMask(UseMasks ? Names.Mask(masks, UpperBodyMaskName) : MaskId.Full));
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

    quaternion[] lastBones;
    float jumpMax;

    void TrackJump()
    {
        using var rigs = ragdolls.ToEntityArray(Allocator.Temp);
        foreach (var rig in rigs)
        {
            var bones = manager.GetBuffer<BoneEntity>(rig, true);
            var fresh = lastBones == null || lastBones.Length != bones.Length;
            if (fresh)
                lastBones = new quaternion[bones.Length];
            for (var b = 0; b < bones.Length; b++)
            {
                var rotation = manager.GetComponentData<LocalTransform>(bones[b].Value).Rotation;
                if (!fresh)
                    jumpMax = math.max(jumpMax, math.degrees(2f * math.acos(math.min(1f, math.abs(math.dot(rotation.value, lastBones[b].value))))));
                lastBones[b] = rotation;
            }
        }
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
            telemetry.Append($"{clock:F2},{ragdoll.Mode},{ragdoll.Strength:F2},{pelvis.y:F3},{head.y:F3},{head.x - pelvis.x:F3},{head.z - pelvis.z:F3},{(clock - lastHit < 0.15f ? lastHitLabel : "")},{(manager.HasComponent<BalanceControl>(rig) ? manager.GetComponentData<BalanceControl>(rig).Decision : -1)},{(manager.HasComponent<BalanceControl>(rig) ? manager.GetComponentData<BalanceControl>(rig).FootContacts : -1)},{(manager.HasComponent<BalanceControl>(rig) ? manager.GetComponentData<BalanceControl>(rig).Margin : 0f):F2},{(manager.HasComponent<BalanceControl>(rig) ? manager.GetComponentData<BalanceControl>(rig).DistanceLeft : 0f):F3},{(manager.HasComponent<BalanceControl>(rig) ? manager.GetComponentData<BalanceControl>(rig).DistanceRight : 0f):F3},{jumpMax:F1},{ragdoll.BlendLeft:F2}\n");
            jumpMax = 0f;
        }
    }

    static void Label(Rect rect, string text, GUIStyle style)
    {
        var shadow = new GUIStyle(style);
        shadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, shadow);
        GUI.Label(rect, text, style);
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        Label(new Rect(20, 14, 900, 30), "SA: inertialized transitions (left)  |  automatic active ragdoll (right)", style);
        var small = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        Label(new Rect(20, 44, 900, 24), "keys 1 shove   2 front shove   3 head hit   4 knockdown", small);
        if (StopAfterSeconds > 0f)
            Label(new Rect(20, 100, 1100, 24), "Play mode will stop by itself at " + StopAfterSeconds.ToString("F0") + " s. Set Stop After Seconds on Reactions Demo to 0 to turn this off.", small);
        if (clock - lastHit < 2f)
            Label(new Rect(20, 70, 900, 28), "hit: " + lastHitLabel, style);
    }
}

using System;
using System.Collections.Generic;
using SA;
using SA.Editor;
using SA.Reactions;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ReactionsDemoBuilder
{
    const string Directory = "Assets/ReactionsDemo/";
    const string Source = "Assets/SA Demo/Kyle SA Cpu.prefab";
    const string UpperBodyMask = "Assets/SA Demo/UpperBody.mask";
    const string UalModel = "Assets/UAL/UAL1_Standard.fbx";
    static readonly string[] UalClips = { "Armature|Idle_Loop", "Armature|Walk_Loop", "Armature|Jog_Fwd_Loop", "Armature|Sprint_Loop", "Armature|Hit_Chest", "Armature|Hit_Head" };

    [MenuItem("Tools/SA/Build Reactions Demo")]
    public static string Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        var upper = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMask);
        if (source == null || upper == null)
            return $"Missing {Source} or {UpperBodyMask}. Import the SA Showcase sample and build its Kyle prefabs first.";
        var ragdoll = MakePrefab(source, "Kyle Ragdoll", upper, true);
        var inertial = MakePrefab(source, "Kyle Inertial", upper, false);
        return Compose("ReactionsDemo", ragdoll, inertial, _ => { });
    }

    [MenuItem("Tools/SA/Build Reactions Demo (UAL)")]
    public static string BuildUal()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(UalModel);
        if (model == null)
            return $"Missing {UalModel}. Import the Quaternius Universal Animation Library with Humanoid rig first.";
        var ragdoll = MakeUalPrefab(model, "UAL Ragdoll", true);
        var inertial = MakeUalPrefab(model, "UAL Inertial", false);
        return Compose("ReactionsDemoUAL", ragdoll, inertial, demo =>
        {
            demo.IdleName = "Armature|Idle_Loop";
            demo.LocomotionNames = new[] { "Armature|Walk_Loop", "Armature|Jog_Fwd_Loop", "Armature|Sprint_Loop" };
            demo.AdditiveName = "";
            demo.UseMasks = false;
        });
    }

    static string Compose(string name, GameObject ragdoll, GameObject inertial, Action<ReactionsDemo> configure)
    {
        var subScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Place(ragdoll, subScene, new Vector3(-1f, 0f, 0f));
        Place(inertial, subScene, new Vector3(1.4f, 0f, 0f));
        EditorSceneManager.SaveScene(subScene, Directory + name + "Sub.unity");
        var main = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        foreach (var root in main.GetRootGameObjects())
        {
            if (root.TryGetComponent(out Light light) && light.type == LightType.Directional)
                root.transform.rotation = Quaternion.Euler(48f, 155f, 0f);
            if (root.name == "Main Camera")
            {
                root.transform.position = new Vector3(0.2f, 1.25f, 5.4f);
                root.transform.rotation = Quaternion.LookRotation(new Vector3(0.2f, 0.95f, 0f) - root.transform.position);
                root.GetComponent<Camera>().fieldOfView = 38f;
            }
        }
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(2f, 1f, 2f);
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.33f, 0.36f, 0.4f));
        AssetDatabase.DeleteAsset(Directory + "Floor.mat");
        AssetDatabase.CreateAsset(material, Directory + "Floor.mat");
        floor.GetComponent<MeshRenderer>().sharedMaterial = material;
        var holder = new GameObject("Subscene");
        var sub = holder.AddComponent<SubScene>();
        sub.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(Directory + name + "Sub.unity");
        sub.AutoLoadScene = true;
        var demo = new GameObject("Reactions Demo").AddComponent<ReactionsDemo>();
        configure(demo);
        EditorSceneManager.SaveScene(main, Directory + name + ".unity");
        return "built " + main.path;
    }

    static GameObject MakeUalPrefab(GameObject model, string name, bool ragdoll)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = name;
        var rig = CharacterSetup.Configure(instance);
        var extra = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UAL/UAL2_Standard.fbx");
        if (extra != null)
            rig.AnimationLibraries.Add(extra);
        rig.Engine = Engine.Cpu;
        rig.Autoplay = AnimationDiscovery.Clips(rig).Find(clip => clip.name == UalClips[0]);
        rig.Inertialize = true;
        rig.FootIK = !ragdoll;
        if (ragdoll)
            instance.AddComponent<RagdollAuthoring>();
        var saved = PrefabUtility.SaveAsPrefabAsset(instance, Directory + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(instance);
        return saved;
    }

    static GameObject MakePrefab(GameObject source, string name, AvatarMask upper, bool ragdoll)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = name;
        var rig = CharacterSetup.Configure(instance);
        rig.Masks = new List<AvatarMask> { upper };
        rig.InPlace = true;
        rig.Inertialize = true;
        rig.FootIK = !ragdoll;
        if (ragdoll)
            instance.AddComponent<RagdollAuthoring>();
        var path = Directory + name + ".prefab";
        var asset = PrefabUtility.SaveAsPrefabAsset(instance, path);
        UnityEngine.Object.DestroyImmediate(instance);
        return asset;
    }

    static void Place(GameObject prefab, UnityEngine.SceneManagement.Scene scene, Vector3 position)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.transform.position = position;
        if (go.GetComponent<RagdollAuthoring>() == null)
        {
            var ik = go.AddComponent<SA.IK.IKAuthoring>();
            ik.HeadTarget = Target(go.name + " Head Target", position + new Vector3(0f, 1.7f, 2f), new Vector3(0.6f, 0.1f, 0f));
            ik.HeadAngle = 45f;
            ik.ReachTargets = new List<SA.IK.LimbTarget>
            {
                new SA.IK.LimbTarget { Limb = SA.IK.Limb.LeftArm, Target = Target(go.name + " Hand Target", position + new Vector3(-0.6f, 1.1f, 0.5f), new Vector3(0.1f, 0.15f, 0.1f)), Weight = 0.8f }
            };
        }
    }

    static Transform Target(string name, Vector3 position, Vector3 range)
    {
        var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        target.name = name;
        target.transform.position = position;
        target.transform.localScale = Vector3.one * 0.06f;
        UnityEngine.Object.DestroyImmediate(target.GetComponent<Collider>());
        var motion = target.AddComponent<SA.IK.IKTargetMotionAuthoring>();
        motion.Range = range;
        return target.transform;
    }
}

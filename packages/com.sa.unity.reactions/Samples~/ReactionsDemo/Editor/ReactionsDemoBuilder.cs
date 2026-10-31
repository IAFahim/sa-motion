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
    const string KyleMaterial = "Assets/SA Demo/Kyle SA.mat";
    const string UalModel = "Assets/UAL/UAL1_Standard.fbx";
    static readonly string[] UalClips = { "Armature|Idle_Loop", "Armature|Walk_Loop", "Armature|Jog_Fwd_Loop", "Armature|Sprint_Loop", "Armature|Hit_Chest", "Armature|Hit_Head" };

    [MenuItem("Tools/SA/Build Reactions Demo")]
    public static string Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        var upper = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMask);
        if (source == null || upper == null)
            return $"Missing {Source} or {UpperBodyMask}. Import the SA Showcase sample and build its Kyle prefabs first.";
        var inPlace = InPlaceMask();
        var ragdoll = MakePrefab(source, "Kyle Ragdoll", upper, inPlace, true);
        var inertial = MakePrefab(source, "Kyle Inertial", upper, inPlace, false);
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

    static Material SaMaterial(Material source)
    {
        var path = Directory + "UAL SA " + source.name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(KyleMaterial));
        material.SetTexture("_BaseMap", source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture);
        material.SetColor("_BaseColor", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static GameObject MakeUalPrefab(GameObject model, string name, bool ragdoll)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = name;
        var rig = instance.AddComponent<RigAuthoring>();
        rig.Engine = Engine.Cpu;
        rig.Clips = new List<AnimationClip>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(UalModel))
            if (asset is AnimationClip clip && Array.IndexOf(UalClips, clip.name) >= 0)
                rig.Clips.Add(clip);
        rig.Autoplay = rig.Clips.Find(clip => clip.name == UalClips[0]);
        rig.Inertialize = true;
        SkinnedMeshConversion.Convert(instance);
        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.sharedMaterials = Array.ConvertAll(renderer.sharedMaterials, SaMaterial);
        }
        if (ragdoll)
            instance.AddComponent<RagdollAuthoring>();
        var saved = PrefabUtility.SaveAsPrefabAsset(instance, Directory + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(instance);
        return saved;
    }

    static AvatarMask InPlaceMask()
    {
        var path = Directory + "InPlace.mask";
        AssetDatabase.DeleteAsset(path);
        var mask = new AvatarMask { name = "InPlace", transformCount = 3 };
        mask.SetTransformPath(0, "Root");
        mask.SetTransformActive(0, false);
        mask.SetTransformPath(1, "Root/Hip");
        mask.SetTransformActive(1, true);
        mask.SetTransformPath(2, "Root/Ribs");
        mask.SetTransformActive(2, true);
        AssetDatabase.CreateAsset(mask, path);
        return mask;
    }

    static GameObject MakePrefab(GameObject source, string name, AvatarMask upper, AvatarMask inPlace, bool ragdoll)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = name;
        var rig = instance.GetComponent<RigAuthoring>();
        rig.Masks = new List<AvatarMask> { upper, inPlace };
        rig.Inertialize = true;
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
    }
}

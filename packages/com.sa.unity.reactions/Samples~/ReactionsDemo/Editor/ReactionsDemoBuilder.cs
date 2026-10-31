using System.Collections.Generic;
using SA;
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
        var subScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Place(ragdoll, subScene, new Vector3(-1f, 0f, 0f));
        Place(inertial, subScene, new Vector3(1.4f, 0f, 0f));
        EditorSceneManager.SaveScene(subScene, Directory + "ReactionsDemoSub.unity");
        var main = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        foreach (var root in main.GetRootGameObjects())
            if (root.name == "Main Camera")
            {
                root.transform.position = new Vector3(0.2f, 1.25f, 5.4f);
                root.transform.rotation = Quaternion.LookRotation(new Vector3(0.2f, 0.95f, 0f) - root.transform.position);
                root.GetComponent<Camera>().fieldOfView = 38f;
            }
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(2f, 1f, 2f);
        Object.DestroyImmediate(floor.GetComponent<Collider>());
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.33f, 0.36f, 0.4f));
        AssetDatabase.DeleteAsset(Directory + "Floor.mat");
        AssetDatabase.CreateAsset(material, Directory + "Floor.mat");
        floor.GetComponent<MeshRenderer>().sharedMaterial = material;
        var holder = new GameObject("Subscene");
        var sub = holder.AddComponent<SubScene>();
        sub.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(Directory + "ReactionsDemoSub.unity");
        sub.AutoLoadScene = true;
        new GameObject("Reactions Demo").AddComponent<ReactionsDemo>();
        EditorSceneManager.SaveScene(main, Directory + "ReactionsDemo.unity");
        return "built " + main.path;
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
        rig.Inertialize = !ragdoll;
        if (ragdoll)
            instance.AddComponent<RagdollAuthoring>();
        var path = Directory + name + ".prefab";
        var asset = PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);
        return asset;
    }

    static void Place(GameObject prefab, UnityEngine.SceneManagement.Scene scene, Vector3 position)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.transform.position = position;
    }
}

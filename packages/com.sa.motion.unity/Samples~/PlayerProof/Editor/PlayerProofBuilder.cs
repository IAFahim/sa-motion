using System.IO;
using global::UnityEditor;
using global::UnityEditor.Build;
using global::UnityEditor.Build.Reporting;
using global::UnityEditor.SceneManagement;
using global::UnityEngine;
using global::UnityEngine.SceneManagement;

namespace SA.Motion.Unity.Proof.Editor
{
    public static class PlayerProofBuilder
    {
        [System.Serializable]
        public class Artifact
        {
            public string sha256;
            public string framework;
        }

        public static string Build(string output)
        {
            const string owned = "Assets/SA.Motion.Proof/.sa-motion-proof-owned";
            if (!File.Exists(owned))
                throw new System.InvalidOperationException("Player proof scene directory is not task-owned");
            var goldenAsset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/SA.Motion.Proof/Resources/SA.Motion.Player/PackageSmoke.json");
            var golden = JsonUtility.FromJson<MotionKernelPlayerProbe.Golden>(goldenAsset.text);
            var loadedHash = MotionKernelPlayerProbe.HashLoadedLibrary();
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.sa.motion.unity");
            var manifestText = File.ReadAllText(Path.Combine(package.resolvedPath, "artifact.json"));
            var manifest = JsonUtility.FromJson<Artifact>(manifestText);
            if (loadedHash.Length == 0 || loadedHash != golden.Sha256 || loadedHash != manifest.sha256 || manifest.framework != "netstandard2.1")
                throw new System.InvalidOperationException("Loaded, packed-manifest and golden DLL identities disagree before build");
            const string buildInput = "Assets/SA.Motion.Proof/Resources/SA.Motion.Player/BuildInput.json";
            File.WriteAllText(buildInput, JsonUtility.ToJson(new MotionKernelPlayerProbe.BuildInput { VerifiedSha256 = loadedHash, Framework = manifest.framework }, true));
            AssetDatabase.ImportAsset(buildInput, ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = new GameObject("SA Motion Player Proof");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<MotionKernelPlayerProbe>();
                const string path = "Assets/SA.Motion.Proof/PlayerProof.unity";
                if (!EditorSceneManager.SaveScene(scene, path))
                    throw new System.InvalidOperationException("Could not save the task-owned proof scene");
                var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { path },
                    locationPathName = output,
                    target = BuildTarget.StandaloneLinux64,
                    options = BuildOptions.Development
                });
                var summary = $"result={report.summary.result}; errors={report.summary.totalErrors}; warnings={report.summary.totalWarnings}; backend={backend}; bytes={report.summary.totalSize}";
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-summary.txt"), summary);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new System.InvalidOperationException(summary);
                return summary;
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
            }
        }
    }
}

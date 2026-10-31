var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.sa.motion.unity");
var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(package.resolvedPath, "../.."));
return SA.Motion.Unity.Proof.Editor.PlayerProofBuilder.Build(System.IO.Path.Combine(root, "artifacts/player/SA.Motion.Proof.x86_64"));

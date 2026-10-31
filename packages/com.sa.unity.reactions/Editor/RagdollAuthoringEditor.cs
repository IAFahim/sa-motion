using SA.Editor;
using UnityEditor;
using UnityEngine;

namespace SA.Reactions.Editor
{
    [CustomEditor(typeof(RagdollAuthoring))]
    public class RagdollAuthoringEditor : UnityEditor.Editor
    {
        bool overrides;

        [MenuItem("Tools/SA/Set Up Ragdoll")]
        static void SetUpSelection() => CharacterSetup.SetUpSelection(ConfigureRagdoll);

        static void ConfigureRagdoll(GameObject root)
        {
            if (!root.TryGetComponent<RagdollAuthoring>(out _))
                Undo.AddComponent<RagdollAuthoring>(root);
            var rig = root.GetComponent<RigAuthoring>();
            rig.Engine = Engine.Cpu;
            rig.Inertialize = true;
            EditorUtility.SetDirty(rig);
        }

        public override void OnInspectorGUI()
        {
            var authoring = (RagdollAuthoring)target;
            if (GUILayout.Button("Set Up Character"))
            {
                CharacterSetup.Configure(authoring.gameObject);
                ConfigureRagdoll(authoring.gameObject);
            }
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "Overrides");
            overrides = EditorGUILayout.Foldout(overrides, "Bone overrides (optional)", true);
            if (overrides)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Overrides"), true);
            if (serializedObject.ApplyModifiedProperties() && authoring.Flinch != null)
            {
                var rig = authoring.GetComponent<RigAuthoring>();
                if (!rig.AnimationLibraries.Contains(authoring.Flinch))
                    rig.AnimationLibraries.Add(authoring.Flinch);
                EditorUtility.SetDirty(rig);
            }
            EditorGUILayout.HelpBox("The Avatar or humanoid bone names select all 15 body roles automatically. Override individual entries only for a custom skeleton.", MessageType.Info);
        }
    }
}

using System.Collections.Generic;
using SA.Motion.Legs;
using SA.Motion.Tuning;
using SA.Motion.Unity;
using UnityEditor;
using UnityEngine;

namespace SA.Reactions.Editor
{
    [CustomEditor(typeof(LegIKAuthoring))]
    class LegIKAuthoringEditor : UnityEditor.Editor
    {
        readonly Dictionary<string, bool> open = new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            var ik = (LegIKAuthoring)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LegIKAuthoring.StartEnabled)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LegIKAuthoring.Weight)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LegIKAuthoring.Ground)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LegIKAuthoring.Preset)));
            var customize = serializedObject.FindProperty(nameof(LegIKAuthoring.Customize));
            EditorGUILayout.PropertyField(customize, new GUIContent("Customize", "Edit every setting instead of using the preset."));
            if (customize.boolValue)
            {
                if (GUILayout.Button("Copy " + ik.Preset + " preset into the custom settings"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Undo.RecordObject(ik, "Copy Leg IK Preset");
                    ik.Custom = LegParams.Preset(ik.Preset);
                    EditorUtility.SetDirty(ik);
                    serializedObject.Update();
                }
                Fields(serializedObject.FindProperty(nameof(LegIKAuthoring.Custom)), true);
                var clamped = Clamped(ik.Custom);
                if (clamped.Length > 0)
                    EditorGUILayout.HelpBox("These settings are out of range or contradict each other and will be corrected when baked: " + clamped, MessageType.Warning);
            }
            else
                Fields(null, false);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LegIKAuthoring.LeftFoot)), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LegIKAuthoring.RightFoot)), true);
            if (GUILayout.Button(new GUIContent("Find heel and ball from the skeleton", "Fill both foot overrides from the rest pose so you can adjust them. The orange gizmos show the result.")))
                FillFeet(ik);
            EditorGUILayout.HelpBox("Untick Foot IK per clip in Rig Authoring > Resolved animations for flips, jumps and anything airborne. Gameplay and timelines set LegIK.Weight or toggle the LegIK component at runtime.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }

        void Fields(SerializedProperty custom, bool editable)
        {
            var preset = LegParams.Preset(((LegIKAuthoring)target).Preset);
            string group = null;
            var visible = true;
            for (var i = 0; i < LegParamsSpec.Count; i++)
            {
                var field = (LegParam)i;
                var text = LegParamsSpec.Text(field);
                var bounds = LegParamsSpec.Bounds(field);
                if (text.Group != group)
                {
                    group = text.Group;
                    open.TryGetValue(group, out visible);
                    visible = EditorGUILayout.Foldout(visible, group, true);
                    open[group] = visible;
                }
                if (!visible)
                    continue;
                var degrees = bounds.Unit == ParamUnit.Radians;
                var factor = degrees ? Mathf.Rad2Deg : 1f;
                var label = new GUIContent(ObjectNames.NicifyVariableName(text.Name) + Suffix(bounds.Unit), text.Description);
                using (new EditorGUI.IndentLevelScope())
                {
                    if (!editable)
                    {
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.FloatField(label, LegParamsSpec.Get(preset, field) * factor);
                        continue;
                    }
                    var property = custom.FindPropertyRelative(text.Name);
                    var value = EditorGUILayout.Slider(label, property.floatValue * factor, bounds.Minimum * factor, bounds.Maximum * factor);
                    property.floatValue = value / factor;
                }
            }
        }

        static string Suffix(ParamUnit unit) => unit switch
        {
            ParamUnit.Meters => " (m)",
            ParamUnit.MetersPerSecond => " (m/s)",
            ParamUnit.Radians => " (°)",
            ParamUnit.Seconds => " (s)",
            _ => ""
        };

        static string Clamped(LegParams settings)
        {
            var sanitized = LegParamsSpec.Sanitize(settings);
            var names = new List<string>();
            for (var i = 0; i < LegParamsSpec.Count; i++)
                if (!LegParamsSpec.Get(sanitized, (LegParam)i).Equals(LegParamsSpec.Get(settings, (LegParam)i)))
                    names.Add(ObjectNames.NicifyVariableName(LegParamsSpec.Text((LegParam)i).Name));
            return string.Join(", ", names);
        }

        static void FillFeet(LegIKAuthoring ik)
        {
            var animator = ik.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning("SA: finding the heel and ball needs a humanoid Avatar. Enter them by hand instead.", ik);
                return;
            }
            Undo.RecordObject(ik, "Find Heel And Ball");
            ik.LeftFoot = Fill(ik.transform, animator, true);
            ik.RightFoot = Fill(ik.transform, animator, false);
            EditorUtility.SetDirty(ik);
        }

        static FootShapeOverride Fill(Transform root, Animator animator, bool left)
        {
            var foot = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            var shape = FootShapes.Auto(root, foot, FootShapes.Toe(animator, foot, left));
            return new FootShapeOverride { Override = true, Heel = (Vector3)Conversions.ToUnity(shape.Heel), Ball = (Vector3)Conversions.ToUnity(shape.Ball) };
        }
    }
}

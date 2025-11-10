#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.SOAP.Editor
{
    /// <summary>
    /// Custom inspector that shows serialized members from BaseAsset with a toggle per member to enable/disable override.
    /// When toggled on, the payload field becomes editable; when off, the inherited (base) value is shown as read-only.
    /// </summary>
    [CustomEditor(typeof(ScriptableObjectVariant))]
    public sealed class ScriptableObjectVariantEditor : UnityEditor.Editor
    {
        private ScriptableObjectVariant _variant;
        private SerializedObject _baseSO;
        private SerializedObject _payloadSO;

        private void OnEnable()
        {
            _variant = (ScriptableObjectVariant)target;

            if (_variant.BaseAsset != null)
                _baseSO = new SerializedObject(_variant.BaseAsset);

            // Ensure payload exists but DO NOT reinitialize (preserve user's edits)
            _variant.__Editor_EnsurePayload(false);

            if (_variant.Payload != null)
                _payloadSO = new SerializedObject(_variant.Payload);
        }

        public override void OnInspectorGUI()
        {
            // Base assignment field
            EditorGUI.BeginChangeCheck();
            ScriptableObject newBase = (ScriptableObject)EditorGUILayout.ObjectField(
                new GUIContent("Base Asset"), _variant.BaseAsset, typeof(ScriptableObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_variant, "Change Base Asset");
                _variant.BaseAsset = newBase;
                _baseSO = _variant.BaseAsset != null ? new SerializedObject(_variant.BaseAsset) : null;

                // If type changes, payload will be recreated and initialized; if same type, it won't overwrite.
                _variant.__Editor_EnsurePayload(true);

                _payloadSO = _variant.Payload != null ? new SerializedObject(_variant.Payload) : null;
            }

            if (_variant.BaseAsset == null)
            {
                EditorGUILayout.HelpBox("Assign a Base Asset to edit overrides.", MessageType.Info);
                return;
            }

            if (_payloadSO == null || _baseSO == null)
            {
                EditorGUILayout.HelpBox("Internal payload is missing or out of sync. Click 'Repair' to recreate.",
                    MessageType.Warning);
                if (GUILayout.Button("Repair"))
                {
                    // When repairing, recreate/retype payload and reinitialize from base
                    _variant.__Editor_EnsurePayload(true);
                    _payloadSO = new SerializedObject(_variant.Payload);
                    _baseSO = new SerializedObject(_variant.BaseAsset);
                }

                return;
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Enable All"))
                    EnableDisableAll(true);

                if (GUILayout.Button("Disable All"))
                    EnableDisableAll(false);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Overrides", EditorStyles.boldLabel);

            _baseSO.Update();
            _payloadSO.Update();

            SerializedProperty baseIter = _baseSO.GetIterator();
            bool enterChildren = true;

            while (baseIter.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (baseIter.propertyPath == "m_Script")
                    continue;

                SerializedProperty payloadProp = _payloadSO.FindProperty(baseIter.propertyPath);
                if (payloadProp == null)
                    continue;

                DrawPropertyWithToggle(baseIter.Copy(), payloadProp.Copy());
            }

            if (_payloadSO.ApplyModifiedProperties())
                EditorUtility.SetDirty(_variant.Payload);

            if (GUI.changed)
                EditorUtility.SetDirty(_variant);
        }

        private void EnableDisableAll(bool enable)
        {
            List<string> paths = new();
            SerializedProperty it = _baseSO.GetIterator();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.propertyPath == "m_Script")
                    continue;
                paths.Add(it.propertyPath);
            }

            Undo.RecordObject(_variant, enable ? "Enable All Overrides" : "Disable All Overrides");
            foreach (string p in paths)
            {
                if (enable) _variant.Overrides.Add(p);
                else _variant.Overrides.Remove(p);
            }

            EditorUtility.SetDirty(_variant);
        }

        private void DrawPropertyWithToggle(SerializedProperty baseProp, SerializedProperty payloadProp)
        {
            bool isOverridden = _variant.Overrides.Contains(baseProp.propertyPath);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Toggle on the same row as the editable field
                    bool newOverride = EditorGUILayout.ToggleLeft(
                        new GUIContent(ObjectNames.NicifyVariableName(baseProp.displayName), baseProp.propertyPath),
                        isOverridden,
                        GUILayout.Width(EditorGUIUtility.labelWidth - 4));

                    if (newOverride != isOverridden)
                    {
                        Undo.RecordObject(_variant, "Toggle Override");

                        if (newOverride)
                        {
                            _variant.Overrides.Add(baseProp.propertyPath);

                            // Initialize payload with inherited value for this path for better UX.
                            SerializedProperty baseCopy = _baseSO.FindProperty(baseProp.propertyPath);
                            SerializedProperty payloadCopy = _payloadSO.FindProperty(baseProp.propertyPath);
                            if (baseCopy != null && payloadCopy != null)
                            {
                                SerializedPropertyUtilities.CopyValue(baseCopy, payloadCopy);
                                _payloadSO.ApplyModifiedProperties();
                                EditorUtility.SetDirty(_variant.Payload);
                            }
                        }
                        else
                        {
                            _variant.Overrides.Remove(baseProp.propertyPath);
                        }

                        EditorUtility.SetDirty(_variant);
                        isOverridden = newOverride;
                    }

                    using (new EditorGUI.DisabledScope(!isOverridden))
                    {
                        EditorGUILayout.PropertyField(payloadProp, GUIContent.none, true);
                    }
                }

                if (!isOverridden)
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(baseProp, new GUIContent("Inherited"), true);
                        EditorGUI.indentLevel--;
                    }
            }
        }
    }
}
#endif
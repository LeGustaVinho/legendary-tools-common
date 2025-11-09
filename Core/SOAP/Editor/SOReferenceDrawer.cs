#if UNITY_EDITOR
using LegendaryTools.SOAP.Variables.Scopes;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.SOAP.Variables.Editor
{
    /// <summary>
    /// Compact and collapsible drawer for SOReferenceBase and descendants.
    /// - Foldout on the label line.
    /// - Row 1: Global (Value | Reference) + field.
    /// - Row 2: Scope popup (VariableScope). If scope != Global, draws its value inline.
    /// </summary>
    [CustomPropertyDrawer(typeof(SOReferenceBase), true)]
    public class SOReferenceDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;
        private const float PopupWidthGlobal = 90f; // "Value" / "Reference"
        private const float PopupWidthScope = 120f; // "Global/Session/Scene/Prefab"

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty useConstant = property.FindPropertyRelative("UseConstant");
            SerializedProperty constant = property.FindPropertyRelative("ConstantValue");
            SerializedProperty variable = property.FindPropertyRelative("Variable");

            if (useConstant == null || constant == null || variable == null)
                return EditorGUIUtility.singleLineHeight;

            float line = EditorGUIUtility.singleLineHeight;

            if (!property.isExpanded)
                return line;

            float height = line /*foldout*/ + Spacing + line /*global*/;

            SerializedProperty useScoped = property.FindPropertyRelative("UseScoped");
            SerializedProperty usePrefab = property.FindPropertyRelative("UsePrefabOverride");
            SerializedProperty useScene = property.FindPropertyRelative("UseSceneOverride");
            SerializedProperty useSession = property.FindPropertyRelative("UseSessionOverride");

            if (useScoped != null && usePrefab != null && useScene != null && useSession != null)
                height += Spacing + line; // scope row

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty useConstant = property.FindPropertyRelative("UseConstant");
            SerializedProperty constant = property.FindPropertyRelative("ConstantValue");
            SerializedProperty variable = property.FindPropertyRelative("Variable");

            SerializedProperty useScoped = property.FindPropertyRelative("UseScoped");
            SerializedProperty usePrefab = property.FindPropertyRelative("UsePrefabOverride");
            SerializedProperty prefabValue = property.FindPropertyRelative("PrefabValue");
            SerializedProperty useScene = property.FindPropertyRelative("UseSceneOverride");
            SerializedProperty sceneValue = property.FindPropertyRelative("SceneValue");
            SerializedProperty useSession = property.FindPropertyRelative("UseSessionOverride");
            SerializedProperty sessionValue = property.FindPropertyRelative("SessionValue");

            float line = EditorGUIUtility.singleLineHeight;

            // Foldout header
            Rect r = new(position.x, position.y, position.width, line);
            property.isExpanded = EditorGUI.Foldout(r, property.isExpanded, label, true);

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            if (useConstant == null || constant == null || variable == null)
            {
                r.y += line + Spacing;
                EditorGUI.LabelField(r, "(Invalid SOReference)");
                EditorGUI.EndProperty();
                return;
            }

            r.y += line + Spacing;

            // ---------- Global Row ----------
            {
                float labelWidth = Mathf.Max(60f, EditorGUIUtility.labelWidth * 0.6f);
                Rect leftLabel = new(r.x, r.y, labelWidth, line);
                Rect popup = new(leftLabel.xMax + 2f, r.y, PopupWidthGlobal, line);
                Rect field = new(popup.xMax + 4f, r.y, r.width - (popup.xMax - r.x) - 4f, line);

                EditorGUI.LabelField(leftLabel, "Global");

                int mode = useConstant.boolValue ? 0 : 1;
                mode = EditorGUI.Popup(popup, mode, new[] { "Value", "Reference" });
                useConstant.boolValue = mode == 0;

                EditorGUI.PropertyField(field, useConstant.boolValue ? constant : variable, GUIContent.none, true);
            }

            r.y += line + Spacing;

            // ---------- Scope Row ----------
            if (useScoped != null && usePrefab != null && useScene != null && useSession != null)
            {
                float labelWidth = Mathf.Max(60f, EditorGUIUtility.labelWidth * 0.6f);
                Rect leftLabel = new(r.x, r.y, labelWidth, line);
                Rect popup = new(leftLabel.xMax + 2f, r.y, PopupWidthScope, line);
                Rect field = new(popup.xMax + 4f, r.y, r.width - (popup.xMax - r.x) - 4f, line);

                EditorGUI.LabelField(leftLabel, "Scope");

                // Determine current scope from flags
                VariableScope current = VariableScope.Global;
                if (useScoped.boolValue)
                {
                    if (usePrefab.boolValue) current = VariableScope.Prefab;
                    else if (useScene.boolValue) current = VariableScope.Scene;
                    else if (useSession.boolValue) current = VariableScope.Session;
                }

                // Draw scope popup
                string[] scopeNames = { "Global", "Session", "Scene", "Prefab" };
                int selected = EditorGUI.Popup(popup, (int)current, scopeNames);
                VariableScope selectedScope = (VariableScope)selected;

                // Update flags
                if (selectedScope == VariableScope.Global)
                {
                    useScoped.boolValue = false;
                    usePrefab.boolValue = false;
                    useScene.boolValue = false;
                    useSession.boolValue = false;
                }
                else
                {
                    useScoped.boolValue = true;
                    usePrefab.boolValue = selectedScope == VariableScope.Prefab;
                    useScene.boolValue = selectedScope == VariableScope.Scene;
                    useSession.boolValue = selectedScope == VariableScope.Session;
                }

                // Inline scope value field when != Global (with label inside PropertyField)
                if (selectedScope != VariableScope.Global)
                {
                    SerializedProperty scopeValueProp = selectedScope switch
                    {
                        VariableScope.Prefab => prefabValue,
                        VariableScope.Scene => sceneValue,
                        VariableScope.Session => sessionValue,
                        _ => null
                    };

                    if (scopeValueProp != null) EditorGUI.PropertyField(field, scopeValueProp, GUIContent.none, true);
                }
            }

            EditorGUI.EndProperty();
        }
    }
}
#endif
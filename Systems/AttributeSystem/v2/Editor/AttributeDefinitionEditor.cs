#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2.Editor
{
    /// <summary>
    /// Custom inspector for AttributeDefinition.
    /// Groups fields and shows debug information, including GUID.
    /// </summary>
    [CustomEditor(typeof(AttributeDefinition))]
    public class AttributeDefinitionEditor : UnityEditor.Editor
    {
        private SerializedProperty _idProp;

        private SerializedProperty _displayNameProp;
        private SerializedProperty _kindProp;

        private SerializedProperty _categoryNameProp;
        private SerializedProperty _visibilityProp;

        private SerializedProperty _clampModeProp;

        private SerializedProperty _baseIntProp;
        private SerializedProperty _baseFloatProp;
        private SerializedProperty _baseFlagsProp;

        private SerializedProperty _minModeProp;
        private SerializedProperty _maxModeProp;

        private SerializedProperty _minIntProp;
        private SerializedProperty _maxIntProp;

        private SerializedProperty _minFloatProp;
        private SerializedProperty _maxFloatProp;

        private SerializedProperty _minFlagsProp;
        private SerializedProperty _maxFlagsProp;

        private SerializedProperty _minRefProp;
        private SerializedProperty _maxRefProp;

        private SerializedProperty _flagNamesProp;

        private bool _showBaseValues = true;
        private bool _showLimits = true;
        private bool _showFlags = true;
        private bool _showDebug = true;

        private void OnEnable()
        {
            _idProp = serializedObject.FindProperty("_id");

            _displayNameProp = serializedObject.FindProperty("displayName");
            _kindProp = serializedObject.FindProperty("kind");

            _categoryNameProp = serializedObject.FindProperty("categoryName");
            _visibilityProp = serializedObject.FindProperty("visibility");

            _clampModeProp = serializedObject.FindProperty("clampMode");

            _baseIntProp = serializedObject.FindProperty("baseInteger");
            _baseFloatProp = serializedObject.FindProperty("baseFloat");
            _baseFlagsProp = serializedObject.FindProperty("baseFlags");

            _minModeProp = serializedObject.FindProperty("minMode");
            _maxModeProp = serializedObject.FindProperty("maxMode");

            _minIntProp = serializedObject.FindProperty("minInteger");
            _maxIntProp = serializedObject.FindProperty("maxInteger");

            _minFloatProp = serializedObject.FindProperty("minFloat");
            _maxFloatProp = serializedObject.FindProperty("maxFloat");

            _minFlagsProp = serializedObject.FindProperty("minFlags");
            _maxFlagsProp = serializedObject.FindProperty("maxFlags");

            _minRefProp = serializedObject.FindProperty("minReference");
            _maxRefProp = serializedObject.FindProperty("maxReference");

            _flagNamesProp = serializedObject.FindProperty("flagNames");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawIdSection();
            EditorGUILayout.Space();

            DrawMetaSection();
            EditorGUILayout.Space();

            DrawBaseValuesSection();
            EditorGUILayout.Space();

            DrawLimitsSection();
            EditorGUILayout.Space();

            DrawFlagsSection();
            EditorGUILayout.Space();

            DrawDebugSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawIdSection()
        {
            EditorGUILayout.LabelField("ID", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(_idProp, new GUIContent("GUID"));
            }
        }

        private void DrawMetaSection()
        {
            EditorGUILayout.LabelField("Meta", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_displayNameProp);
            EditorGUILayout.PropertyField(_kindProp);

            EditorGUILayout.PropertyField(_categoryNameProp);
            EditorGUILayout.PropertyField(_visibilityProp);
            EditorGUILayout.PropertyField(_clampModeProp);
        }

        private void DrawBaseValuesSection()
        {
            _showBaseValues = EditorGUILayout.Foldout(_showBaseValues, "Base Values", true);
            if (!_showBaseValues)
                return;

            EditorGUI.indentLevel++;

            AttributeKind kind = (AttributeKind)_kindProp.enumValueIndex;
            switch (kind)
            {
                case AttributeKind.Integer:
                    EditorGUILayout.PropertyField(_baseIntProp, new GUIContent("Base Integer"));
                    break;

                case AttributeKind.Float:
                    EditorGUILayout.PropertyField(_baseFloatProp, new GUIContent("Base Float"));
                    break;

                case AttributeKind.Flags:
                    EditorGUILayout.PropertyField(_baseFlagsProp, new GUIContent("Base Flags (Raw Bitmask)"));
                    break;

                default:
                    EditorGUILayout.HelpBox("Unsupported attribute kind.", MessageType.Warning);
                    break;
            }

            EditorGUI.indentLevel--;
        }

        private void DrawLimitsSection()
        {
            _showLimits = EditorGUILayout.Foldout(_showLimits, "Limits", true);
            if (!_showLimits)
                return;

            EditorGUI.indentLevel++;

            AttributeKind kind = (AttributeKind)_kindProp.enumValueIndex;

            EditorGUILayout.PropertyField(_minModeProp, new GUIContent("Min Mode"));
            if ((AttributeLimitMode)_minModeProp.enumValueIndex == AttributeLimitMode.FixedValue)
                switch (kind)
                {
                    case AttributeKind.Integer:
                        EditorGUILayout.PropertyField(_minIntProp, new GUIContent("Min Integer"));
                        break;
                    case AttributeKind.Float:
                        EditorGUILayout.PropertyField(_minFloatProp, new GUIContent("Min Float"));
                        break;
                    case AttributeKind.Flags:
                        EditorGUILayout.PropertyField(_minFlagsProp, new GUIContent("Min Flags Bitmask"));
                        break;
                }
            else if ((AttributeLimitMode)_minModeProp.enumValueIndex == AttributeLimitMode.ReferenceAttribute)
                EditorGUILayout.PropertyField(_minRefProp, new GUIContent("Min Reference"));

            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(_maxModeProp, new GUIContent("Max Mode"));
            if ((AttributeLimitMode)_maxModeProp.enumValueIndex == AttributeLimitMode.FixedValue)
                switch (kind)
                {
                    case AttributeKind.Integer:
                        EditorGUILayout.PropertyField(_maxIntProp, new GUIContent("Max Integer"));
                        break;
                    case AttributeKind.Float:
                        EditorGUILayout.PropertyField(_maxFloatProp, new GUIContent("Max Float"));
                        break;
                    case AttributeKind.Flags:
                        EditorGUILayout.PropertyField(_maxFlagsProp, new GUIContent("Max Flags Bitmask"));
                        break;
                }
            else if ((AttributeLimitMode)_maxModeProp.enumValueIndex == AttributeLimitMode.ReferenceAttribute)
                EditorGUILayout.PropertyField(_maxRefProp, new GUIContent("Max Reference"));

            EditorGUI.indentLevel--;
        }

        private void DrawFlagsSection()
        {
            AttributeKind kind = (AttributeKind)_kindProp.enumValueIndex;
            bool isFlags = kind == AttributeKind.Flags;

            using (new EditorGUI.DisabledScope(!isFlags))
            {
                _showFlags = EditorGUILayout.Foldout(_showFlags, "Flags (Names)", true);
                if (!_showFlags)
                    return;

                EditorGUI.indentLevel++;

                if (!isFlags) EditorGUILayout.HelpBox("Flags are only used when kind = Flags.", MessageType.Info);

                EditorGUILayout.PropertyField(_flagNamesProp, true);

                EditorGUI.indentLevel--;
            }
        }

        private void DrawDebugSection()
        {
            _showDebug = EditorGUILayout.Foldout(_showDebug, "Debug Preview", true);
            if (!_showDebug)
                return;

            EditorGUI.indentLevel++;

            AttributeDefinition def = (AttributeDefinition)target;
            if (def == null)
            {
                EditorGUILayout.HelpBox("Definition reference is null.", MessageType.Warning);
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.LabelField("Category", def.categoryName);
            EditorGUILayout.LabelField("Visibility", def.visibility.ToString());

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Default Value (by kind):", EditorStyles.boldLabel);
            switch (def.kind)
            {
                case AttributeKind.Integer:
                    EditorGUILayout.LabelField("Integer", def.baseInteger.ToString());
                    break;
                case AttributeKind.Float:
                    EditorGUILayout.LabelField("Float", def.baseFloat.ToString());
                    break;
                case AttributeKind.Flags:
                    EditorGUILayout.LabelField("Flags (Bitmask)", $"0x{def.baseFlags:X16}");
                    if (def.flagNames != null && def.flagNames.Length > 0)
                    {
                        EditorGUILayout.LabelField("Active Flags:");
                        EditorGUI.indentLevel++;
                        for (int i = 0; i < def.flagNames.Length; i++)
                        {
                            string name = def.flagNames[i];
                            if (string.IsNullOrEmpty(name))
                                continue;

                            bool on = (def.baseFlags & (1UL << i)) != 0;
                            if (on) EditorGUILayout.LabelField($"[{i}] {name}");
                        }

                        EditorGUI.indentLevel--;
                    }

                    break;
            }

            EditorGUI.indentLevel--;
        }
    }
}
#endif
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using LegendaryTools.Inspector;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// PropertyDrawer that shows an Object reference and, when expanded,
    /// renders the referenced object's serializable members inline.
    /// </summary>
    [CustomPropertyDrawer(typeof(InlineEditorAttribute), true)]
    public sealed class InlineEditorDrawer : PropertyDrawer
    {
        // Cache editors to avoid recreating them every repaint.
        private readonly Dictionary<int, UnityEditor.Editor> _editorCache = new();

        // Cache measured heights for GetPropertyHeight efficiency.
        private readonly Dictionary<int, float> _heightCache = new();

        // Tracks which properties had their initial expanded state applied this session.
        private static readonly HashSet<string> _initExpand = new();

        private const float BoxPadding = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Validate owner type (MonoBehaviour or ScriptableObject).
            UnityEngine.Object owner = property.serializedObject.targetObject;
            bool ownerOk = owner is MonoBehaviour || owner is ScriptableObject;

            // Validate field type (must be an object reference to MB/SO).
            bool isObjRef = property.propertyType == SerializedPropertyType.ObjectReference;
            Type fieldType = fieldInfo.FieldType;

            bool refTypeOk = typeof(ScriptableObject).IsAssignableFrom(fieldType) ||
                             typeof(MonoBehaviour).IsAssignableFrom(fieldType);

            // Draw field and error state if invalid usage.
            if (!ownerOk || !isObjRef || !refTypeOk)
            {
                Rect line = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(line, property, label, true);

                line.y += line.height + EditorGUIUtility.standardVerticalSpacing;

                Rect helpRect = new(line.x, line.y, position.width, EditorGUIUtility.singleLineHeight * 2f);
                string msg = !ownerOk
                    ? "InlineEditor can only be used inside a MonoBehaviour or ScriptableObject."
                    : !isObjRef
                        ? "InlineEditor targets ObjectReference fields only (lists/arrays are not supported)."
                        : "InlineEditor field must be a ScriptableObject or MonoBehaviour reference.";
                EditorGUI.HelpBox(helpRect, msg, MessageType.Error);

                EditorGUI.EndProperty();
                return;
            }

            // Draw the object reference field.
            Rect refLine = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginChangeCheck();
            UnityEngine.Object newRef =
                EditorGUI.ObjectField(refLine, label, property.objectReferenceValue, fieldType, true);
            if (EditorGUI.EndChangeCheck())
            {
                property.objectReferenceValue = newRef;
                property.serializedObject.ApplyModifiedProperties();
                ClearCaches();
            }

            // If no reference, finish here.
            if (property.objectReferenceValue == null)
            {
                EditorGUI.EndProperty();
                return;
            }

            // Initialize expanded state once per session + property path.
            string key = GetInitKey(property);
            if (!_initExpand.Contains(key))
            {
                InlineEditorAttribute attr = (InlineEditorAttribute)attribute;
                property.isExpanded = attr.ExpandedByDefault;
                _initExpand.Add(key);
            }

            // Foldout line.
            Rect foldoutLine = new(
                position.x,
                refLine.y + refLine.height + EditorGUIUtility.standardVerticalSpacing,
                position.width,
                EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(foldoutLine, property.isExpanded, "Inline", true);

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            // Compute rect for the box area based on cached height.
            float innerHeight = GetOrComputeInspectorHeight(property.objectReferenceValue);
            Rect boxRect = new(
                position.x,
                foldoutLine.y + foldoutLine.height + EditorGUIUtility.standardVerticalSpacing,
                position.width,
                innerHeight + BoxPadding * 2f);

            GUI.Box(boxRect, GUIContent.none, EditorStyles.helpBox);

            // Content rect inside the box.
            Rect content = new(
                boxRect.x + BoxPadding,
                boxRect.y + BoxPadding,
                boxRect.width - BoxPadding * 2f,
                boxRect.height - BoxPadding * 2f);

            // Indent content to visually nest it.
            int prevIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;

            using (new GUILayout.AreaScope(content))
            {
                int id = property.objectReferenceValue.GetInstanceID();

                if (!_editorCache.TryGetValue(id, out UnityEditor.Editor editor) || editor == null)
                {
                    UnityEditor.Editor.CreateCachedEditor(property.objectReferenceValue, null, ref editor);
                    _editorCache[id] = editor;
                }

                if (editor != null)
                {
                    EditorGUI.BeginChangeCheck();
                    DrawDefaultInspectorWithoutScript(editor);
                    if (EditorGUI.EndChangeCheck())
                    {
                        editor.serializedObject.ApplyModifiedProperties();
                        // Re-measure height on next layout because content changed.
                        _heightCache.Remove(id);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Failed to create editor for the referenced object.", MessageType.Warning);
                }
            }

            EditorGUI.indentLevel = prevIndent;

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Always at least the object reference field.
            float h = EditorGUIUtility.singleLineHeight;

            // If there's no reference, do NOT reserve space for foldout/box.
            if (property.objectReferenceValue == null)
                return h;

            // When there IS a reference, we always draw a foldout line (collapsed or expanded).
            h += EditorGUIUtility.standardVerticalSpacing; // spacing before foldout
            h += EditorGUIUtility.singleLineHeight; // foldout line

            if (property.isExpanded)
            {
                h += EditorGUIUtility.standardVerticalSpacing;
                float inner = GetOrComputeInspectorHeight(property.objectReferenceValue);
                h += inner + BoxPadding * 2f; // boxed content
            }

            return h;
        }

        /// <summary>
        /// Computes and caches the height needed to draw the referenced object's default inspector.
        /// </summary>
        private float GetOrComputeInspectorHeight(UnityEngine.Object obj)
        {
            int id = obj.GetInstanceID();
            if (_heightCache.TryGetValue(id, out float cached))
                return cached;

            float height = 0f;

            try
            {
                SerializedObject so = new(obj);
                SerializedProperty it = so.GetIterator();
                bool enterChildren = true;

                while (it.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    // Hide m_Script row to match default inspector behavior for MB/SO.
                    if (it.propertyPath == "m_Script")
                        continue;

                    float ph = EditorGUI.GetPropertyHeight(it, true);
                    height += ph + EditorGUIUtility.standardVerticalSpacing;
                }
            }
            catch
            {
                // Fallback height if anything goes wrong.
                height = EditorGUIUtility.singleLineHeight * 3f;
            }

            height = Mathf.Max(height, EditorGUIUtility.singleLineHeight);
            _heightCache[id] = height;
            return height;
        }

        /// <summary>
        /// Draws the default inspector, excluding the m_Script field for MB/SO.
        /// </summary>
        private static void DrawDefaultInspectorWithoutScript(UnityEditor.Editor editor)
        {
            SerializedObject so = editor.serializedObject;
            so.UpdateIfRequiredOrScript();

            SerializedProperty prop = so.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyPath == "m_Script")
                    continue;

                EditorGUILayout.PropertyField(prop, true);
            }
        }

        private void ClearCaches()
        {
            foreach (KeyValuePair<int, UnityEditor.Editor> kv in _editorCache)
            {
                if (kv.Value != null)
                    UnityEngine.Object.DestroyImmediate(kv.Value);
            }

            _editorCache.Clear();
            _heightCache.Clear();
        }

        private static string GetInitKey(SerializedProperty property)
        {
            // Unique-ish key per property path to initialize foldout only once per session.
            return
                $"InlineEditor_Init_{property.serializedObject.targetObject.GetInstanceID()}_{property.propertyPath}";
        }
    }
}
#endif
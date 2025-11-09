#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using LegendaryTools.Inspector;

/// <summary>
/// PropertyDrawer that shows an Object reference and, when expanded,
/// renders the referenced object's serializable members inline (no GUILayout).
/// </summary>
[CustomPropertyDrawer(typeof(InlineEditorAttribute), true)]
public sealed class InlineEditorDrawer : PropertyDrawer
{
    // Cache editors to avoid recreating them every repaint.
    private readonly Dictionary<int, Editor> _editorCache = new();

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

        // Y cursor for manual layout (no GUILayout).
        float y = position.y;

        // Draw field and error state if invalid usage.
        if (!ownerOk || !isObjRef || !refTypeOk)
        {
            Rect line = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(line, property, label, true);
            y += line.height + EditorGUIUtility.standardVerticalSpacing;

            Rect helpRect = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight * 2f);
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
        Rect refLine = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.BeginChangeCheck();
        UnityEngine.Object newRef =
            EditorGUI.ObjectField(refLine, label, property.objectReferenceValue, fieldType, true);
        if (EditorGUI.EndChangeCheck())
        {
            property.objectReferenceValue = newRef;
            property.serializedObject.ApplyModifiedProperties();
            ClearEditorCache();
        }

        y += refLine.height;

        // If no reference, finish here.
        if (property.objectReferenceValue == null)
        {
            EditorGUI.EndProperty();
            return;
        }

        y += EditorGUIUtility.standardVerticalSpacing;

        // Initialize expanded state once per session + property path.
        string key = GetInitKey(property);
        if (!_initExpand.Contains(key))
        {
            InlineEditorAttribute attr = (InlineEditorAttribute)attribute;
            property.isExpanded = attr.ExpandedByDefault;
            _initExpand.Add(key);
        }

        // Foldout line.
        Rect foldoutLine = new(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutLine, property.isExpanded, "Inline", true);
        y += foldoutLine.height;

        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        y += EditorGUIUtility.standardVerticalSpacing;

        // Compute inner height (manual, Rect-based) and draw a box.
        float innerHeight = ComputeInspectorHeight(property.objectReferenceValue);
        Rect boxRect = new(position.x, y, position.width, innerHeight + BoxPadding * 2f);
        GUI.Box(boxRect, GUIContent.none, EditorStyles.helpBox);

        // Content rect inside the box and indent it visually.
        Rect content = new(
            boxRect.x + BoxPadding,
            boxRect.y + BoxPadding,
            boxRect.width - BoxPadding * 2f,
            boxRect.height - BoxPadding * 2f);

        int prevIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel++;

        // Draw child object properties using Rect-based controls only.
        DrawInspectorRectBased(content, property.objectReferenceValue);

        EditorGUI.indentLevel = prevIndent;

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Start with the object reference field.
        float h = EditorGUIUtility.singleLineHeight;

        // If there's no reference, do NOT reserve space for foldout/box.
        if (property.objectReferenceValue == null)
            return h;

        // Always draw a foldout line (collapsed or expanded).
        h += EditorGUIUtility.standardVerticalSpacing; // spacing before foldout
        h += EditorGUIUtility.singleLineHeight; // foldout line

        if (property.isExpanded)
        {
            h += EditorGUIUtility.standardVerticalSpacing;
            float inner = ComputeInspectorHeight(property.objectReferenceValue);
            h += inner + BoxPadding * 2f; // boxed content
        }

        return h;
    }

    /// <summary>
    /// Draws the referenced object's default inspector (excluding m_Script) using Rect-based layout.
    /// </summary>
    private void DrawInspectorRectBased(Rect contentRect, UnityEngine.Object obj)
    {
        // Respect indent for child content.
        Rect r = EditorGUI.IndentedRect(contentRect);

        // Ensure a minimum row height to avoid clipping.
        float y = r.y;

        SerializedObject so = new(obj);
        so.UpdateIfRequiredOrScript();

        SerializedProperty it = so.GetIterator();
        bool enter = true;

        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.propertyPath == "m_Script")
                continue;

            float ph = EditorGUI.GetPropertyHeight(it, true);

            Rect row = new(r.x, y, r.width, ph);
            EditorGUI.PropertyField(row, it, true);

            y += ph + EditorGUIUtility.standardVerticalSpacing;
        }

        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Computes the total height needed to draw the referenced object's inspector using Rect-based logic.
    /// </summary>
    private float ComputeInspectorHeight(UnityEngine.Object obj)
    {
        float height = 0f;

        try
        {
            SerializedObject so = new(obj);
            SerializedProperty it = so.GetIterator();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (it.propertyPath == "m_Script")
                    continue;

                float ph = EditorGUI.GetPropertyHeight(it, true);
                height += ph + EditorGUIUtility.standardVerticalSpacing;
            }
        }
        catch
        {
            height = EditorGUIUtility.singleLineHeight * 3f;
        }

        // Remove the last added spacing if there was any content.
        if (height > 0f)
            height -= EditorGUIUtility.standardVerticalSpacing;

        return Mathf.Max(height, EditorGUIUtility.singleLineHeight);
    }

    private void ClearEditorCache()
    {
        foreach (KeyValuePair<int, Editor> kv in _editorCache)
        {
            if (kv.Value != null)
                UnityEngine.Object.DestroyImmediate(kv.Value);
        }

        _editorCache.Clear();
    }

    private static string GetInitKey(SerializedProperty property)
    {
        // Unique-ish key per property path to initialize foldout once per session.
        return $"InlineEditor_Init_{property.serializedObject.targetObject.GetInstanceID()}_{property.propertyPath}";
    }
}
#endif
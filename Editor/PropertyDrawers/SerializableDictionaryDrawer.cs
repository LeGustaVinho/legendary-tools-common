using UnityEditor;
using UnityEngine;
using System.Linq;
using LegendaryTools;
using Object = UnityEngine.Object;

// <summary>
// Custom drawer for SerializableDictionary<,> that supports foldout, pagination and validation.
// Highlights duplicate keys and keeps UX responsive for collections with many elements.
// </summary>
[CustomPropertyDrawer(typeof(SerializableDictionary<,>))]
public class SerializableDictionaryDrawer : PropertyDrawer
{
    // Cached layout metrics read from EditorGUIUtility. Safe to cache for runtime session.
    private readonly float spacing = EditorGUIUtility.standardVerticalSpacing;
    private readonly float lineHeight = EditorGUIUtility.singleLineHeight;

    // Session keys to persist foldout and pagination state per property path.
    private const string FoldoutKey = "SerializableDictionary_Foldout_";
    private const string PageKey = "SerializableDictionary_Page_";

    // Pagination configuration.
    private const int ElementsPerPage = 20;

    /// <summary>
    /// Draws the property GUI for a SerializableDictionary instance.
    /// </summary>
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Retrieve serialized 'keys' and 'values' arrays from the target object.
        SerializedProperty keys = property.FindPropertyRelative("keys");
        SerializedProperty values = property.FindPropertyRelative("values");

        // Compose unique state keys for this specific property path.
        string foldoutKey = FoldoutKey + property.propertyPath;
        string pageKey = PageKey + property.propertyPath;

        // Load previous session state.
        bool isExpanded = SessionState.GetBool(foldoutKey, false);
        int currentPage = SessionState.GetInt(pageKey, 0);

        // Draw foldout header.
        Rect foldoutRect = new(position.x, position.y, position.width, lineHeight);
        position.y += lineHeight + spacing;

        EditorGUI.BeginChangeCheck();
        isExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, label, true, EditorStyles.foldoutHeader);
        if (EditorGUI.EndChangeCheck())
            // Persist current foldout state per property path.
            SessionState.SetBool(foldoutKey, isExpanded);

        if (!isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        // Draw a subtle background for content area.
        Rect contentRect = new(position.x, position.y, position.width, GetContentHeight(keys, values, currentPage));
        EditorGUI.DrawRect(contentRect, new Color(0.1f, 0.1f, 0.1f, 0.1f));

        // Indent and shrink content region slightly for visual structure.
        EditorGUI.indentLevel++;
        position.x += 10;
        position.width -= 10;

        // Begin change check to apply modified properties at the end.
        EditorGUI.BeginChangeCheck();

        // Compute pagination metrics.
        int totalElements = Mathf.Min(keys.arraySize, values.arraySize);
        int totalPages = Mathf.CeilToInt((float)totalElements / ElementsPerPage);
        currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, totalPages - 1));

        int startIndex = currentPage * ElementsPerPage;
        int endIndex = Mathf.Min(startIndex + ElementsPerPage, totalElements);

        if (totalElements == 0)
        {
            // Empty dictionary hint.
            Rect emptyRect = new(position.x, position.y, position.width, lineHeight);
            EditorGUI.LabelField(emptyRect, "Dictionary is empty", EditorStyles.helpBox);
            position.y += lineHeight + spacing;
        }
        else
        {
            // Pagination UI (when needed).
            if (totalPages > 1)
            {
                Rect paginationRect = new(position.x, position.y, position.width, lineHeight);
                Rect prevRect = new(position.x, position.y, 60, lineHeight);
                Rect pageLabelRect = new(position.x + 65, position.y, position.width - 130, lineHeight);
                Rect nextRect = new(position.x + position.width - 60, position.y, 60, lineHeight);

                // Previous button
                GUI.enabled = currentPage > 0;
                if (GUI.Button(prevRect, "Previous"))
                {
                    currentPage--;
                    SessionState.SetInt(pageKey, currentPage);
                }

                GUI.enabled = true;

                // Page indicator
                EditorGUI.LabelField(pageLabelRect, $"Page {currentPage + 1} of {totalPages}",
                    EditorStyles.centeredGreyMiniLabel);

                // Next button
                GUI.enabled = currentPage < totalPages - 1;
                if (GUI.Button(nextRect, "Next"))
                {
                    currentPage++;
                    SessionState.SetInt(pageKey, currentPage);
                }

                GUI.enabled = true;

                position.y += lineHeight + spacing;
            }

            // Draw key/value rows for the current page.
            int? indexToRemove = null;
            bool[] invalidKeys = new bool[endIndex - startIndex];
            string[] invalidKeyMessages = new string[endIndex - startIndex];

            for (int i = startIndex; i < endIndex; i++)
            {
                SerializedProperty keyProp = keys.GetArrayElementAtIndex(i);
                SerializedProperty valueProp = values.GetArrayElementAtIndex(i);

                // Calculate height per row based on expanded child properties.
                float keyHeight = EditorGUI.GetPropertyHeight(keyProp, GUIContent.none, true);
                float valueHeight = EditorGUI.GetPropertyHeight(valueProp, GUIContent.none, true);
                float rowHeight = Mathf.Max(keyHeight, valueHeight);

                // Layout: key (40%), value (50%), remove button (10%).
                Rect keyRect = new(position.x, position.y, position.width * 0.4f - 5, rowHeight);
                Rect valueRect = new(position.x + position.width * 0.4f, position.y, position.width * 0.5f - 5,
                    rowHeight);
                Rect removeRect = new(position.x + position.width * 0.9f, position.y, position.width * 0.1f,
                    lineHeight);

                // Save old key value to revert if user introduces a duplicate.
                object oldKey = GetPropertyValue(keyProp);
                object currentKey = GetPropertyValue(keyProp);

                // Pre-compute duplicate state for this index.
                invalidKeys[i - startIndex] = Enumerable.Range(0, keys.arraySize)
                    .Where(j => j != i)
                    .Any(j => ArePropertiesEqual(GetPropertyValue(keys.GetArrayElementAtIndex(j)), currentKey));

                invalidKeyMessages[i - startIndex] =
                    invalidKeys[i - startIndex] ? $"Duplicate key detected for index {i}" : null;

                // Draw key field with a temporary red tint when invalid.
                EditorGUI.BeginChangeCheck();
                Color prevColor = GUI.color;
                if (invalidKeys[i - startIndex]) GUI.color = Color.red;

                EditorGUI.PropertyField(keyRect, keyProp, GUIContent.none, true);

                // Restore GUI color.
                GUI.color = prevColor;
                bool keyChanged = EditorGUI.EndChangeCheck();

                // If the key changed and is now a duplicate, revert to the old value and flag an error hint.
                if (keyChanged && !invalidKeys[i - startIndex])
                {
                    object newKey = GetPropertyValue(keyProp);
                    bool keyExists = Enumerable.Range(0, keys.arraySize)
                        .Where(j => j != i)
                        .Any(j => ArePropertiesEqual(GetPropertyValue(keys.GetArrayElementAtIndex(j)), newKey));

                    if (keyExists)
                    {
                        Debug.LogWarning($"Key at index {i} is a duplicate. Reverting to previous value.");
                        SetPropertyValue(keyProp, oldKey);
                        invalidKeys[i - startIndex] = true;
                        invalidKeyMessages[i - startIndex] = $"Duplicate key detected for index {i}";
                    }
                }

                // Draw value field.
                EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none, true);

                // Draw remove button with emphasis.
                GUIStyle removeButtonStyle = new(GUI.skin.button) { fontStyle = FontStyle.Bold };
                if (GUI.Button(removeRect, "X", removeButtonStyle)) indexToRemove = i;

                // If key is invalid, reserve an extra help box line right below this row.
                if (invalidKeys[i - startIndex])
                {
                    position.y += rowHeight + spacing;
                    Rect errorRect = new(position.x, position.y, position.width, lineHeight);
                    EditorGUI.HelpBox(errorRect, invalidKeyMessages[i - startIndex], MessageType.Error);
                    rowHeight += lineHeight + spacing;
                }

                position.y += rowHeight + spacing;
            }

            // Remove requested entry (if any).
            if (indexToRemove.HasValue)
            {
                keys.DeleteArrayElementAtIndex(indexToRemove.Value);
                values.DeleteArrayElementAtIndex(indexToRemove.Value);
            }
        }

        // Draw Add/Clear buttons in a single row.
        Rect buttonRowRect = new(position.x, position.y, position.width, lineHeight);
        Rect addRect = new(position.x, position.y, position.width / 2f - 2.5f, lineHeight);
        Rect clearRect = new(position.x + position.width / 2f + 2.5f, position.y, position.width / 2f - 2.5f,
            lineHeight);

        if (GUI.Button(addRect, "Add Entry"))
        {
            // Create a new entry with a generated key and default value.
            object newKey = GenerateUniqueKey(keys);

            keys.arraySize++;
            values.arraySize++;

            SetPropertyValue(keys.GetArrayElementAtIndex(keys.arraySize - 1), newKey);
            SetPropertyValue(values.GetArrayElementAtIndex(values.arraySize - 1),
                GetDefaultValue(values.GetArrayElementAtIndex(values.arraySize - 1)));

            // Recalculate pagination AFTER insertion so the new element becomes visible on the last page.
            int totalAfter = Mathf.Min(keys.arraySize, values.arraySize);
            int pagesAfter = Mathf.CeilToInt((float)totalAfter / ElementsPerPage);
            int newCurrentPage = Mathf.Max(0, pagesAfter - 1);
            SessionState.SetInt(PageKey + property.propertyPath, newCurrentPage);
        }

        if (GUI.Button(clearRect, "Clear Dictionary"))
            if (EditorUtility.DisplayDialog("Clear Dictionary",
                    "Are you sure you want to clear all entries in the dictionary?", "Yes", "No"))
            {
                keys.arraySize = 0;
                values.arraySize = 0;
                SessionState.SetInt(PageKey + property.propertyPath, 0);
            }

        EditorGUI.indentLevel--;

        // Apply changes if any property was modified.
        if (EditorGUI.EndChangeCheck()) property.serializedObject.ApplyModifiedProperties();

        EditorGUI.EndProperty();
    }

    /// <summary>
    /// Returns the height for the entire drawer given current foldout/pagination state.
    /// </summary>
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        bool isExpanded = SessionState.GetBool(FoldoutKey + property.propertyPath, false);
        if (!isExpanded) return lineHeight;

        SerializedProperty keys = property.FindPropertyRelative("keys");
        SerializedProperty values = property.FindPropertyRelative("values");

        return lineHeight + spacing +
               GetContentHeight(keys, values, SessionState.GetInt(PageKey + property.propertyPath, 0));
    }

    /// <summary>
    /// Computes the total height of the content region (rows + pagination + buttons).
    /// </summary>
    private float GetContentHeight(SerializedProperty keys, SerializedProperty values, int currentPage)
    {
        int totalElements = Mathf.Min(keys.arraySize, values.arraySize);
        int totalPages = Mathf.CeilToInt((float)totalElements / ElementsPerPage);
        currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, totalPages - 1));

        int elementsInPage = totalElements == 0
            ? 0
            : Mathf.Min(totalElements - currentPage * ElementsPerPage, ElementsPerPage);

        float height = totalElements == 0 ? lineHeight : 0f;

        if (totalElements > 0)
        {
            int startIndex = currentPage * ElementsPerPage;
            int endIndex = Mathf.Min(startIndex + ElementsPerPage, totalElements);

            for (int i = startIndex; i < endIndex; i++)
            {
                float keyHeight = EditorGUI.GetPropertyHeight(keys.GetArrayElementAtIndex(i), GUIContent.none, true);
                float valueHeight =
                    EditorGUI.GetPropertyHeight(values.GetArrayElementAtIndex(i), GUIContent.none, true);
                float rowHeight = Mathf.Max(keyHeight, valueHeight);

                // If duplicate, include HelpBox height line.
                bool isInvalid = Enumerable.Range(0, keys.arraySize)
                    .Where(j => j != i)
                    .Any(j => ArePropertiesEqual(GetPropertyValue(keys.GetArrayElementAtIndex(j)),
                        GetPropertyValue(keys.GetArrayElementAtIndex(i))));

                if (isInvalid) rowHeight += lineHeight + spacing;

                height += rowHeight + spacing;
            }
        }

        if (totalPages > 1) height += lineHeight + spacing; // Pagination line

        height += lineHeight + spacing; // Buttons line
        return height;
    }

    /// <summary>
    /// Gets a boxed value of a SerializedProperty in a small set of supported property types.
    /// For Generic types, returns a copy of the SerializedProperty itself for structural comparison.
    /// </summary>
    private object GetPropertyValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.String: return property.stringValue;
            case SerializedPropertyType.Integer: return property.intValue;
            case SerializedPropertyType.Float: return property.floatValue;
            case SerializedPropertyType.Boolean: return property.boolValue;
            case SerializedPropertyType.ObjectReference: return property.objectReferenceValue;
            case SerializedPropertyType.Enum: return property.enumValueIndex;
            case SerializedPropertyType.Generic: return property.Copy(); // Return a copy for deep comparison.
            default:
                Debug.LogWarning($"Unsupported key type: {property.propertyType}");
                return null;
        }
    }

    /// <summary>
    /// Sets a SerializedProperty value from a boxed object. Supports a small set of common types.
    /// For Generic: expects a SerializedProperty to copy from.
    /// </summary>
    private void SetPropertyValue(SerializedProperty property, object value)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.String:
                property.stringValue = value as string ?? string.Empty;
                break;
            case SerializedPropertyType.Integer:
                property.intValue = value is int i ? i : 0;
                break;
            case SerializedPropertyType.Float:
                property.floatValue = value is float f ? f : 0f;
                break;
            case SerializedPropertyType.Boolean:
                property.boolValue = value is bool b && b;
                break;
            case SerializedPropertyType.ObjectReference:
                property.objectReferenceValue = value as Object;
                break;
            case SerializedPropertyType.Enum:
                property.enumValueIndex = value is int ei ? ei : 0;
                break;
            case SerializedPropertyType.Generic:
                if (value is SerializedProperty sourceProp)
                {
                    property.CopyFrom(sourceProp); // Copy fields recursively where types match.
                    property.serializedObject.ApplyModifiedProperties();
                }
                else
                {
                    Debug.LogWarning($"Cannot set Generic property with value: {value}");
                }

                break;
            default:
                Debug.LogWarning($"Unsupported key type: {property.propertyType}");
                break;
        }
    }

    /// <summary>
    /// Generates a unique key depending on the element type used by the 'keys' array.
    /// For complex types, falls back to default and lets the user edit manually.
    /// </summary>
    private object GenerateUniqueKey(SerializedProperty keys)
    {
        switch (keys.arrayElementType)
        {
            case "string":
            {
                string baseKey = "NewKey";
                int suffix = 1;
                string newKey = baseKey;
                while (Enumerable.Range(0, keys.arraySize)
                       .Any(i => keys.GetArrayElementAtIndex(i).stringValue == newKey))
                {
                    newKey = $"{baseKey}{suffix++}";
                }

                return newKey;
            }
            case "int":
            {
                int newKey = keys.arraySize > 0
                    ? Enumerable.Range(0, keys.arraySize).Max(i => keys.GetArrayElementAtIndex(i).intValue) + 1
                    : 0;
                return newKey;
            }
            case "float":
            {
                float newKey = keys.arraySize > 0
                    ? Enumerable.Range(0, keys.arraySize).Max(i => keys.GetArrayElementAtIndex(i).floatValue) + 1f
                    : 0f;
                return newKey;
            }
            case "bool":
                Debug.LogWarning("Boolean keys are not recommended due to limited unique values.");
                return false;
            case "PPtr<$Object>": // Unity object reference type.
                return null; // Default is null reference.
            default:
                // Fallback: complex/unknown key types are initialized to default.
                // This avoids brittle reflection with Type.GetType on Unity's internal type strings.
                Debug.LogWarning(
                    $"Auto-generating unique keys for '{keys.arrayElementType}' is not supported. Using default.");
                return null; // default ref-type; for value types Unity initializes appropriately.
        }
    }

    /// <summary>
    /// Returns a sensible default for a value property.
    /// </summary>
    private object GetDefaultValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.String: return "";
            case SerializedPropertyType.Integer: return 0;
            case SerializedPropertyType.Float: return 0f;
            case SerializedPropertyType.Boolean: return false;
            case SerializedPropertyType.ObjectReference: return null;
            case SerializedPropertyType.Enum: return 0;
            case SerializedPropertyType.Generic: return null; // Unity initializes nested fields automatically.
            default: return null;
        }
    }

    /// <summary>
    /// Compares two boxed values obtained from SerializedProperty for equality.
    /// Supports deep compare for Generic via CompareSerializedProperties.
    /// </summary>
    private bool ArePropertiesEqual(object a, object b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;

        if (a is SerializedProperty propA && b is SerializedProperty propB)
        {
            if (propA.propertyType != propB.propertyType) return false;
            switch (propA.propertyType)
            {
                case SerializedPropertyType.Generic:
                    return CompareSerializedProperties(propA, propB);
                default:
                    // For non-generic types, equality was already boxed appropriately.
                    return Equals(GetBoxed(propA), GetBoxed(propB));
            }
        }

        return Equals(a, b);
    }

    /// <summary>
    /// Helper to box a simple SerializedProperty value for equality comparison.
    /// </summary>
    private object GetBoxed(SerializedProperty prop)
    {
        switch (prop.propertyType)
        {
            case SerializedPropertyType.String: return prop.stringValue;
            case SerializedPropertyType.Integer: return prop.intValue;
            case SerializedPropertyType.Float: return prop.floatValue;
            case SerializedPropertyType.Boolean: return prop.boolValue;
            case SerializedPropertyType.ObjectReference: return prop.objectReferenceValue;
            case SerializedPropertyType.Enum: return prop.enumValueIndex;
            default: return null;
        }
    }

    /// <summary>
    /// Deep comparison of two Generic SerializedProperties by walking their visible children.
    /// NOTE: This implementation uses NextVisible across the same depth. For heavily nested data,
    /// consider switching to an iterator bounded by GetEndProperty() to strictly limit scope.
    /// </summary>
    private bool CompareSerializedProperties(SerializedProperty propA, SerializedProperty propB)
    {
        SerializedProperty aIt = propA.Copy();
        SerializedProperty bIt = propB.Copy();
        bool aEntered = false;
        bool bEntered = false;

        while (aIt.NextVisible(aEntered) && bIt.NextVisible(bEntered))
        {
            aEntered = true;
            bEntered = true;

            if (aIt.propertyType != bIt.propertyType) return false;

            switch (aIt.propertyType)
            {
                case SerializedPropertyType.String:
                    if (aIt.stringValue != bIt.stringValue) return false;
                    break;
                case SerializedPropertyType.Integer:
                    if (aIt.intValue != bIt.intValue) return false;
                    break;
                case SerializedPropertyType.Float:
                    if (!Mathf.Approximately(aIt.floatValue, bIt.floatValue)) return false;
                    break;
                case SerializedPropertyType.Boolean:
                    if (aIt.boolValue != bIt.boolValue) return false;
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (aIt.objectReferenceValue != bIt.objectReferenceValue) return false;
                    break;
                case SerializedPropertyType.Enum:
                    if (aIt.enumValueIndex != bIt.enumValueIndex) return false;
                    break;
                default:
                    // Other property types continue iteration.
                    break;
            }
        }

        return true;
    }
}

/// <summary>
/// Extensions for SerializedProperty to copy values field-by-field where types match.
/// </summary>
public static class SerializedPropertyExtensions
{
    /// <summary>
    /// Copies values from a source SerializedProperty to a target SerializedProperty recursively where possible.
    /// </summary>
    /// <param name="target">Destination property.</param>
    /// <param name="source">Source property.</param>
    public static void CopyFrom(this SerializedProperty target, SerializedProperty source)
    {
        SerializedProperty sourceIt = source.Copy();
        SerializedProperty targetIt = target.Copy();
        bool sourceEntered = false;
        bool targetEntered = false;

        // Walk both properties in parallel where fields are visible and types match.
        while (sourceIt.NextVisible(sourceEntered) && targetIt.NextVisible(targetEntered))
        {
            sourceEntered = true;
            targetEntered = true;

            if (sourceIt.propertyType == targetIt.propertyType)
                switch (sourceIt.propertyType)
                {
                    case SerializedPropertyType.String:
                        targetIt.stringValue = sourceIt.stringValue; break;
                    case SerializedPropertyType.Integer:
                        targetIt.intValue = sourceIt.intValue; break;
                    case SerializedPropertyType.Float:
                        targetIt.floatValue = sourceIt.floatValue; break;
                    case SerializedPropertyType.Boolean:
                        targetIt.boolValue = sourceIt.boolValue; break;
                    case SerializedPropertyType.ObjectReference:
                        targetIt.objectReferenceValue = sourceIt.objectReferenceValue; break;
                    case SerializedPropertyType.Enum:
                        targetIt.enumValueIndex = sourceIt.enumValueIndex; break;
                    default:
                        // For complex types, continue traversing children.
                        break;
                }
        }

        // Apply modifications to the serialized object backing the property.
        target.serializedObject.ApplyModifiedProperties();
    }
}
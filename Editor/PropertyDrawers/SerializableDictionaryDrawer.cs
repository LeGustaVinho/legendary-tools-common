using UnityEditor;
using UnityEngine;
using System.Linq;
using System;
using LegendaryTools;
using Object = UnityEngine.Object;

[CustomPropertyDrawer(typeof(SerializableDictionary<,>))]
public class SerializableDictionaryDrawer : PropertyDrawer
{
    private readonly float spacing = EditorGUIUtility.standardVerticalSpacing;
    private readonly float lineHeight = EditorGUIUtility.singleLineHeight;
    private const string FoldoutKey = "SerializableDictionary_Foldout_";
    private const string PageKey = "SerializableDictionary_Page_";
    private const int ElementsPerPage = 20;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Find the lists of keys and values
        SerializedProperty keys = property.FindPropertyRelative("keys");
        SerializedProperty values = property.FindPropertyRelative("values");

        // Foldout and page state, persistent per property
        string foldoutKey = FoldoutKey + property.propertyPath;
        string pageKey = PageKey + property.propertyPath;
        bool isExpanded = SessionState.GetBool(foldoutKey, false);
        int currentPage = SessionState.GetInt(pageKey, 0);

        // Calculate the initial position
        Rect foldoutRect = new Rect(position.x, position.y, position.width, lineHeight);
        position.y += lineHeight + spacing;

        // Draw the foldout
        EditorGUI.BeginChangeCheck();
        isExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, label, true, EditorStyles.foldoutHeader);
        if (EditorGUI.EndChangeCheck())
        {
            SessionState.SetBool(foldoutKey, isExpanded);
        }

        if (!isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        // Add a colored background for the dictionary content
        Rect contentRect = new Rect(position.x, position.y, position.width, GetContentHeight(keys, values, currentPage));
        EditorGUI.DrawRect(contentRect, new Color(0.1f, 0.1f, 0.1f, 0.1f));

        // Indent the content
        EditorGUI.indentLevel++;
        position.x += 10;
        position.width -= 10;

        // Begin checking for changes
        EditorGUI.BeginChangeCheck();

        // Calculate the indices of elements on the current page
        int totalElements = Mathf.Min(keys.arraySize, values.arraySize);
        int totalPages = Mathf.CeilToInt((float)totalElements / ElementsPerPage);
        currentPage = Mathf.Clamp(currentPage, 0, totalPages - 1);
        int startIndex = currentPage * ElementsPerPage;
        int endIndex = Mathf.Min(startIndex + ElementsPerPage, totalElements);

        // Check if the dictionary is empty
        if (totalElements == 0)
        {
            Rect emptyRect = new Rect(position.x, position.y, position.width, lineHeight);
            EditorGUI.LabelField(emptyRect, "Dictionary is empty", EditorStyles.helpBox);
            position.y += lineHeight + spacing;
        }
        else
        {
            // Display pagination controls, if necessary
            if (totalPages > 1)
            {
                Rect paginationRect = new Rect(position.x, position.y, position.width, lineHeight);
                Rect prevRect = new Rect(position.x, position.y, 60, lineHeight);
                Rect pageLabelRect = new Rect(position.x + 65, position.y, position.width - 130, lineHeight);
                Rect nextRect = new Rect(position.x + position.width - 60, position.y, 60, lineHeight);

                GUI.enabled = currentPage > 0;
                if (GUI.Button(prevRect, "Previous"))
                {
                    currentPage--;
                    SessionState.SetInt(pageKey, currentPage);
                }
                GUI.enabled = true;

                EditorGUI.LabelField(pageLabelRect, $"Page {currentPage + 1} of {totalPages}", EditorStyles.centeredGreyMiniLabel);

                GUI.enabled = currentPage < totalPages - 1;
                if (GUI.Button(nextRect, "Next"))
                {
                    currentPage++;
                    SessionState.SetInt(pageKey, currentPage);
                }
                GUI.enabled = true;

                position.y += lineHeight + spacing;
            }

            // Display and edit the key-value pairs on the current page
            int? indexToRemove = null;
            bool[] invalidKeys = new bool[endIndex - startIndex];
            string[] invalidKeyMessages = new string[endIndex - startIndex];

            for (int i = startIndex; i < endIndex; i++)
            {
                SerializedProperty keyProp = keys.GetArrayElementAtIndex(i);
                SerializedProperty valueProp = values.GetArrayElementAtIndex(i);

                // Calculate the height of fields (considering expansion)
                float keyHeight = EditorGUI.GetPropertyHeight(keyProp, GUIContent.none, true);
                float valueHeight = EditorGUI.GetPropertyHeight(valueProp, GUIContent.none, true);
                float rowHeight = Mathf.Max(keyHeight, valueHeight);

                // Define the areas for key, value, and remove button
                Rect keyRect = new Rect(position.x, position.y, position.width * 0.4f - 5, rowHeight);
                Rect valueRect = new Rect(position.x + position.width * 0.4f, position.y, position.width * 0.5f - 5, rowHeight);
                Rect removeRect = new Rect(position.x + position.width * 0.9f, position.y, position.width * 0.1f, lineHeight);

                // Save the current key value
                object oldKey = GetPropertyValue(keyProp);

                // Check if the key is duplicated
                object currentKey = GetPropertyValue(keyProp);
                invalidKeys[i - startIndex] = Enumerable.Range(0, keys.arraySize)
                    .Where(j => j != i)
                    .Any(j => ArePropertiesEqual(GetPropertyValue(keys.GetArrayElementAtIndex(j)), currentKey));
                invalidKeyMessages[i - startIndex] = invalidKeys[i - startIndex] ? $"Duplicate key detected for index {i}" : null;

                // Draw the key field with red text if invalid
                EditorGUI.BeginChangeCheck();
                GUIStyle keyStyle = new GUIStyle(EditorStyles.textField);
                if (invalidKeys[i - startIndex])
                {
                    keyStyle.normal.textColor = Color.red;
                    keyStyle.focused.textColor = Color.red;
                }
                EditorGUI.PropertyField(keyRect, keyProp, GUIContent.none, true); // Include children for expansion
                bool keyChanged = EditorGUI.EndChangeCheck();

                // Validate the key only after editing is complete
                if (keyChanged && !invalidKeys[i - startIndex])
                {
                    object newKey = GetPropertyValue(keyProp);
                    if (!ArePropertiesEqual(oldKey, newKey))
                    {
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
                }

                // Draw the value field
                EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none, true); // Include children for expansion

                // Remove button with style
                GUIStyle removeButtonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, normal = { textColor = Color.red } };
                if (GUI.Button(removeRect, "X", removeButtonStyle))
                {
                    indexToRemove = i;
                }

                // Display error message for invalid keys
                if (invalidKeys[i - startIndex])
                {
                    position.y += rowHeight + spacing;
                    Rect errorRect = new Rect(position.x, position.y, position.width, lineHeight);
                    EditorGUI.HelpBox(errorRect, invalidKeyMessages[i - startIndex], MessageType.Error);
                    rowHeight += lineHeight + spacing;
                }

                position.y += rowHeight + spacing;
            }

            // Remove the marked entry, if any
            if (indexToRemove.HasValue)
            {
                keys.DeleteArrayElementAtIndex(indexToRemove.Value);
                values.DeleteArrayElementAtIndex(indexToRemove.Value);
            }
        }

        // Buttons for adding and clearing
        Rect buttonRowRect = new Rect(position.x, position.y, position.width, lineHeight);
        Rect addRect = new Rect(position.x, position.y, position.width / 2 - 2.5f, lineHeight);
        Rect clearRect = new Rect(position.x + position.width / 2 + 2.5f, position.y, position.width / 2 - 2.5f, lineHeight);

        GUIStyle addButtonStyle = new GUIStyle(GUI.skin.button) { normal = { textColor = Color.green } };
        if (GUI.Button(addRect, "Add Entry", addButtonStyle))
        {
            // Generate a unique key
            object newKey = GenerateUniqueKey(keys);
            keys.arraySize++;
            values.arraySize++;
            SetPropertyValue(keys.GetArrayElementAtIndex(keys.arraySize - 1), newKey);
            SetPropertyValue(values.GetArrayElementAtIndex(values.arraySize - 1), GetDefaultValue(values.GetArrayElementAtIndex(values.arraySize - 1)));
            // Adjust the page to show the new entry
            currentPage = totalPages;
            SessionState.SetInt(pageKey, currentPage);
        }

        GUIStyle clearButtonStyle = new GUIStyle(GUI.skin.button) { normal = { textColor = Color.red } };
        if (GUI.Button(clearRect, "Clear Dictionary", clearButtonStyle))
        {
            if (EditorUtility.DisplayDialog("Clear Dictionary", "Are you sure you want to clear all entries in the dictionary?", "Yes", "No"))
            {
                keys.arraySize = 0;
                values.arraySize = 0;
                currentPage = 0;
                SessionState.SetInt(pageKey, currentPage);
            }
        }

        EditorGUI.indentLevel--;

        // Apply modifications
        if (EditorGUI.EndChangeCheck())
        {
            property.serializedObject.ApplyModifiedProperties();
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        bool isExpanded = SessionState.GetBool(FoldoutKey + property.propertyPath, false);
        if (!isExpanded)
        {
            return lineHeight;
        }

        SerializedProperty keys = property.FindPropertyRelative("keys");
        SerializedProperty values = property.FindPropertyRelative("values");
        return lineHeight + spacing + GetContentHeight(keys, values, SessionState.GetInt(PageKey + property.propertyPath, 0));
    }

    private float GetContentHeight(SerializedProperty keys, SerializedProperty values, int currentPage)
    {
        int totalElements = Mathf.Min(keys.arraySize, values.arraySize);
        int totalPages = Mathf.CeilToInt((float)totalElements / ElementsPerPage);
        currentPage = Mathf.Clamp(currentPage, 0, totalPages - 1);
        int elementsInPage = totalElements == 0 ? 0 : Mathf.Min(totalElements - currentPage * ElementsPerPage, ElementsPerPage);

        float height = totalElements == 0 ? lineHeight : 0;
        if (totalElements > 0)
        {
            int startIndex = currentPage * ElementsPerPage;
            int endIndex = Mathf.Min(startIndex + ElementsPerPage, totalElements);
            for (int i = startIndex; i < endIndex; i++)
            {
                float keyHeight = EditorGUI.GetPropertyHeight(keys.GetArrayElementAtIndex(i), GUIContent.none, true);
                float valueHeight = EditorGUI.GetPropertyHeight(values.GetArrayElementAtIndex(i), GUIContent.none, true);
                float rowHeight = Mathf.Max(keyHeight, valueHeight);
                // Check if the key is duplicated to add extra height for HelpBox
                bool isInvalid = Enumerable.Range(0, keys.arraySize)
                    .Where(j => j != i)
                    .Any(j => ArePropertiesEqual(GetPropertyValue(keys.GetArrayElementAtIndex(j)), GetPropertyValue(keys.GetArrayElementAtIndex(i))));
                if (isInvalid)
                {
                    rowHeight += lineHeight + spacing;
                }
                height += rowHeight + spacing;
            }
        }

        if (totalPages > 1)
        {
            height += lineHeight + spacing; // Space for pagination controls
        }
        height += lineHeight + spacing; // Space for Add/Clear buttons
        return height;
    }

    // Gets the value of a property generically
    private object GetPropertyValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.String:
                return property.stringValue;
            case SerializedPropertyType.Integer:
                return property.intValue;
            case SerializedPropertyType.Float:
                return property.floatValue;
            case SerializedPropertyType.Boolean:
                return property.boolValue;
            case SerializedPropertyType.ObjectReference:
                return property.objectReferenceValue;
            case SerializedPropertyType.Enum:
                return property.enumValueIndex;
            case SerializedPropertyType.Generic: // For classes and structs
                return property.Copy(); // Returns a copy of the SerializedProperty
            default:
                Debug.LogWarning($"Unsupported key type: {property.propertyType}");
                return null;
        }
    }

    // Sets the value of a property generically
    private void SetPropertyValue(SerializedProperty property, object value)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.String:
                property.stringValue = (string)value;
                break;
            case SerializedPropertyType.Integer:
                property.intValue = (int)value;
                break;
            case SerializedPropertyType.Float:
                property.floatValue = (float)value;
                break;
            case SerializedPropertyType.Boolean:
                property.boolValue = (bool)value;
                break;
            case SerializedPropertyType.ObjectReference:
                property.objectReferenceValue = (Object)value;
                break;
            case SerializedPropertyType.Enum:
                property.enumValueIndex = (int)value;
                break;
            case SerializedPropertyType.Generic: // For classes and structs
                if (value is SerializedProperty sourceProp)
                {
                    property.CopyFrom(sourceProp); // Copies the values from the SerializedProperty
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

    // Generates a unique key for the property type
    private object GenerateUniqueKey(SerializedProperty keys)
    {
        switch (keys.arrayElementType)
        {
            case "string":
            {
                string baseKey = "NewKey";
                int suffix = 1;
                string newKey = baseKey;
                while (Enumerable.Range(0, keys.arraySize).Any(i => keys.GetArrayElementAtIndex(i).stringValue == newKey))
                {
                    newKey = $"{baseKey}{suffix++}";
                }
                return newKey;
            }
            case "int":
            {
                int newKey = keys.arraySize > 0 ? Enumerable.Range(0, keys.arraySize).Max(i => keys.GetArrayElementAtIndex(i).intValue) + 1 : 0;
                return newKey;
            }
            case "float":
            {
                float newKey = keys.arraySize > 0 ? Enumerable.Range(0, keys.arraySize).Max(i => keys.GetArrayElementAtIndex(i).floatValue) + 1f : 0f;
                return newKey;
            }
            case "bool":
                Debug.LogWarning("Boolean keys are not recommended due to limited unique values.");
                return false;
            case "PPtr<$Object>": // For ObjectReference
                return null; // Null reference as default
            default: // For classes and structs
                Debug.LogWarning($"Generating unique keys for complex types ({keys.arrayElementType}) is not fully supported. Using default instance.");
                try
                {
                    Type keyType = Type.GetType(keys.arrayElementType) ?? typeof(object);
                    object newInstance = Activator.CreateInstance(keyType);
                    // Try to create a distinct key by modifying a field (if possible)
                    if (newInstance != null)
                    {
                        var idField = keyType.GetField("id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (idField != null && idField.FieldType == typeof(int))
                        {
                            int newId = keys.arraySize > 0 ? Enumerable.Range(0, keys.arraySize).Max(i =>
                            {
                                var prop = keys.GetArrayElementAtIndex(i);
                                if (prop.propertyType == SerializedPropertyType.Generic)
                                {
                                    var idProp = prop.FindPropertyRelative("id");
                                    return idProp?.intValue ?? 0;
                                }
                                return 0;
                            }) + 1 : 1;
                            idField.SetValue(newInstance, newId);
                        }
                    }
                    return newInstance;
                }
                catch
                {
                    return null;
                }
        }
    }

    // Gets a default value for the value type
    private object GetDefaultValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.String:
                return "";
            case SerializedPropertyType.Integer:
                return 0;
            case SerializedPropertyType.Float:
                return 0f;
            case SerializedPropertyType.Boolean:
                return false;
            case SerializedPropertyType.ObjectReference:
                return null;
            case SerializedPropertyType.Enum:
                return 0;
            case SerializedPropertyType.Generic: // For classes and structs
                return null; // Unity initializes with default values
            default:
                return null;
        }
    }

    // Compares two property values to check for equality
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
                    return Equals(a, b);
            }
        }
        return Equals(a, b);
    }

    // Compares two SerializedProperty instances for complex types
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
                    if (!Mathf.Approximately(aIt.floatValue, bIt.floatValue)) return false; // Fixed typo
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
                    // Continue for nested types
                    break;
            }
        }
        return true;
    }
}

// Extension for copying values from a SerializedProperty
public static class SerializedPropertyExtensions
{
    public static void CopyFrom(this SerializedProperty target, SerializedProperty source)
    {
        SerializedProperty sourceIt = source.Copy();
        SerializedProperty targetIt = target.Copy();
        bool sourceEntered = false;
        bool targetEntered = false;

        while (sourceIt.NextVisible(sourceEntered) && targetIt.NextVisible(targetEntered))
        {
            sourceEntered = true;
            targetEntered = true;
            if (sourceIt.propertyType == targetIt.propertyType)
            {
                switch (sourceIt.propertyType)
                {
                    case SerializedPropertyType.String:
                        targetIt.stringValue = sourceIt.stringValue;
                        break;
                    case SerializedPropertyType.Integer:
                        targetIt.intValue = sourceIt.intValue;
                        break;
                    case SerializedPropertyType.Float:
                        targetIt.floatValue = sourceIt.floatValue;
                        break;
                    case SerializedPropertyType.Boolean:
                        targetIt.boolValue = sourceIt.boolValue;
                        break;
                    case SerializedPropertyType.ObjectReference:
                        targetIt.objectReferenceValue = sourceIt.objectReferenceValue;
                        break;
                    case SerializedPropertyType.Enum:
                        targetIt.enumValueIndex = sourceIt.enumValueIndex;
                        break;
                    default:
                        // For complex types, continue iteration
                        break;
                }
            }
        }
        target.serializedObject.ApplyModifiedProperties();
    }
}
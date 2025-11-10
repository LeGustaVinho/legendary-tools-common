#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Custom property drawer for Observable<T> that routes Inspector edits through the Value property.
    /// This ensures change events are fired when the serialized 'value' field changes in the Inspector.
    /// </summary>
    [CustomPropertyDrawer(typeof(Observable<>), true)]
    public class ObservableDrawer : PropertyDrawer
    {
        /// <summary>
        /// Gets the required height for drawing the property, delegating to the inner 'value' field.
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty valueProp = property.FindPropertyRelative("value");
            return EditorGUI.GetPropertyHeight(valueProp, label, true);
        }

        /// <summary>
        /// Draws the property and triggers OnChanged by assigning through the Value setter when modified.
        /// Supports multi-object editing by applying the changed value to all selected targets.
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty valueProp = property.FindPropertyRelative("value");

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();

            // Draw the inner 'value' normally (Unity handles the field UI).
            EditorGUI.PropertyField(position, valueProp, label, true);

            if (EditorGUI.EndChangeCheck())
            {
                // Read the edited value from the SerializedProperty.
                object newValueObj = ReadManagedValue(valueProp);

                // Apply the change through the Value setter to every selected target.
                UnityEngine.Object[] targets = property.serializedObject.targetObjects;
                string path = property.propertyPath;

                foreach (Object target in targets)
                {
                    object observableObj = GetTargetObjectOfProperty(target, path);
                    if (observableObj == null)
                        continue;

                    Type observableType = observableObj.GetType();
                    Type valueType = observableType.GetField("value",
                                             BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                         ?.FieldType
                                     ?? InferGenericArgument(observableType);

                    if (valueType == null)
                        continue;

                    object converted;
                    try
                    {
                        converted = ConvertTo(newValueObj, valueType);
                    }
                    catch
                    {
                        converted = newValueObj; // Best-effort fallback
                    }

                    PropertyInfo valuePi =
                        observableType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                    if (valuePi != null && valuePi.CanWrite)
                    {
                        Undo.RecordObject((UnityEngine.Object)target, "Change Observable Value");

                        // Assign via the Value property so that OnChanged is invoked.
                        valuePi.SetValue(observableObj, converted);

                        EditorUtility.SetDirty((UnityEngine.Object)target);
                    }
                }

                // Commit changes to the serialized representation.
                property.serializedObject.ApplyModifiedProperties();
            }

            EditorGUI.EndProperty();
        }

        // -------- Helpers --------

        /// <summary>
        /// Reads a boxed managed representation from a SerializedProperty for common Unity types.
        /// </summary>
        private static object ReadManagedValue(SerializedProperty sp)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer:
#if UNITY_2021_2_OR_NEWER
                    return sp.longValue;
#else
                    return (long)sp.intValue;
#endif

                case SerializedPropertyType.Float:
#if UNITY_2020_1_OR_NEWER
                    return sp.doubleValue;
#else
                    return (double)sp.floatValue;
#endif

                case SerializedPropertyType.Boolean:
                    return sp.boolValue;

                case SerializedPropertyType.String:
                    return sp.stringValue;

                // For enums, use the underlying integer value (not enumValueIndex).
                case SerializedPropertyType.Enum:
                    return sp.intValue;

                case SerializedPropertyType.ObjectReference:
                    return sp.objectReferenceValue;

                case SerializedPropertyType.Color:
                    return sp.colorValue;

                case SerializedPropertyType.Vector2:
                    return sp.vector2Value;

                case SerializedPropertyType.Vector3:
                    return sp.vector3Value;

#if UNITY_2022_1_OR_NEWER
                case SerializedPropertyType.Vector2Int:
                    return sp.vector2IntValue;

                case SerializedPropertyType.Vector3Int:
                    return sp.vector3IntValue;
#endif

                case SerializedPropertyType.Quaternion:
                    return sp.quaternionValue;

                default:
                    // Fallback: attempt to resolve the managed object for this property path.
                    return GetTargetObjectOfProperty(sp.serializedObject.targetObject, sp.propertyPath);
            }
        }

        /// <summary>
        /// Converts a boxed value to a target type, handling enums and IConvertible cases.
        /// </summary>
        private static object ConvertTo(object value, Type targetType)
        {
            if (value == null) return null;

            // If it's already assignable, return as-is.
            if (targetType.IsInstanceOfType(value))
                return value;

            // Enum conversion from underlying integral value.
            if (targetType.IsEnum)
                if (value is IConvertible)
                {
                    int intVal = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    return Enum.ToObject(targetType, intVal);
                }

            // Numeric/string convertible types.
            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);

            return value;
        }

        /// <summary>
        /// Infers the generic argument T from Observable<T> type.
        /// </summary>
        private static Type InferGenericArgument(Type observableType)
        {
            if (observableType.IsGenericType)
                return observableType.GetGenericArguments()[0];
            return null;
        }

        /// <summary>
        /// Resolves the managed object instance for a SerializedProperty path starting at a specific root object.
        /// Supports field, property, and array/list navigation.
        /// </summary>
        private static object GetTargetObjectOfProperty(object root, string propertyPath)
        {
            if (root == null || string.IsNullOrEmpty(propertyPath))
                return null;

            string path = propertyPath.Replace(".Array.data[", "[");
            string[] elements = path.Split('.');

            object obj = root;
            foreach (string element in elements)
            {
                if (obj == null) return null;

                if (element.Contains("["))
                {
                    string elementName = element.Substring(0, element.IndexOf("[", StringComparison.Ordinal));
                    int index = Convert.ToInt32(
                        element.Substring(element.IndexOf("[", StringComparison.Ordinal))
                            .Replace("[", string.Empty)
                            .Replace("]", string.Empty),
                        CultureInfo.InvariantCulture);

                    obj = GetValueByName(obj, elementName);
                    obj = GetIndexedValue(obj, index);
                }
                else
                {
                    obj = GetValueByName(obj, element);
                }
            }

            return obj;
        }

        /// <summary>
        /// Gets a field or property value by name using reflection.
        /// </summary>
        private static object GetValueByName(object source, string name)
        {
            if (source == null) return null;
            Type type = source.GetType();

            // Search up the inheritance chain.
            while (type != null)
            {
                FieldInfo f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f.GetValue(source);

                PropertyInfo p = type.GetProperty(name,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p != null) return p.GetValue(source, null);

                type = type.BaseType;
            }

            return null;
        }

        /// <summary>
        /// Gets an element at the specified index from an IEnumerable or IList instance.
        /// </summary>
        private static object GetIndexedValue(object source, int index)
        {
            if (source == null) return null;

            if (source is IList list)
            {
                if (index >= 0 && index < list.Count)
                    return list[index];
                return null;
            }

            if (source is IEnumerable enumerable)
            {
                IEnumerator enm = enumerable.GetEnumerator();
                for (int i = 0; i <= index; i++)
                {
                    if (!enm.MoveNext()) return null;
                }

                return enm.Current;
            }

            return null;
        }
    }
}
#endif
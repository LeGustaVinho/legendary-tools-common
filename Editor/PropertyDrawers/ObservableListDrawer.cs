#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Custom drawer for ObservableList<T> that routes all Inspector edits (add/remove/reorder/edit)
    /// through the ObservableList API (indexer/Add/Insert/Remove/Clear), ensuring change events are fired.
    /// </summary>
    [CustomPropertyDrawer(typeof(ObservableList<>), true)]
    public class ObservableListDrawer : PropertyDrawer
    {
        private readonly Dictionary<string, ReorderableList> _lists = new();

        /// <summary>
        /// Gets the required height by delegating to the ReorderableList.
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            ReorderableList list = GetOrCreateList(property, label);
            return list != null ? list.GetHeight() : EditorGUIUtility.singleLineHeight;
        }

        /// <summary>
        /// Draws the list using a ReorderableList and triggers events by calling the ObservableList API.
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            ReorderableList list = GetOrCreateList(property, label);
            if (list == null)
            {
                EditorGUI.LabelField(position, label, new GUIContent("Unsupported list type"));
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            list.DoList(position);
            EditorGUI.EndProperty();
        }

        // ===================== ReorderableList factory & callbacks =====================

        private ReorderableList GetOrCreateList(SerializedProperty property, GUIContent label)
        {
            if (property == null) return null;

            string key = property.serializedObject.targetObject.GetInstanceID() + "|" + property.propertyPath;
            if (_lists.TryGetValue(key, out ReorderableList existing))
                return existing;

            // The ObservableList<T> has a private serialized List<T> field named "collection"
            SerializedProperty collectionProp = property.FindPropertyRelative("collection");
            if (collectionProp == null || !collectionProp.isArray)
                return null;

            ReorderableList rl = new(property.serializedObject, collectionProp, true, true, true, true);

            // Header
            rl.drawHeaderCallback = rect => { EditorGUI.LabelField(rect, label); };

            // Dynamic element height to support complex types
            rl.elementHeightCallback = index =>
            {
                SerializedProperty element = collectionProp.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(element, GUIContent.none, true) + 2f;
            };

            // Element draw + update via indexer (fires OnUpdate)
            rl.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                SerializedProperty elementProp = collectionProp.GetArrayElementAtIndex(index);
                rect.height = EditorGUI.GetPropertyHeight(elementProp, GUIContent.none, true);
                rect.y += 1f; // small padding

                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(rect, elementProp, GUIContent.none, true);
                if (EditorGUI.EndChangeCheck())
                {
                    // Read edited value from the serialized property (do not Apply; we'll sync from managed)
                    object newValueObj = ReadManagedValue(elementProp);

                    // Push the changed element to all selected targets via indexer setter -> triggers OnUpdate
                    ApplyToAllTargets(property, (observableListObj, listElementType) =>
                    {
                        object converted = ConvertTo(newValueObj, listElementType);

                        // indexer set => triggers OnUpdate
                        PropertyInfo setItem = observableListObj.GetType()
                            .GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
                        if (setItem != null && setItem.CanWrite)
                        {
                            RecordUndo(property.serializedObject.targetObjects, "Modify ObservableList Element");
                            setItem.SetValue(observableListObj, converted, new object[] { index });
                            MarkDirty(property.serializedObject.targetObjects);
                        }
                    });

                    // Refresh serialized view from managed
                    property.serializedObject.Update();
                }
            };

            // Add => call Add(default(T)) on every target (fires OnAdd)
            rl.onAddCallback = list =>
            {
                ApplyToAllTargets(property, (observableListObj, listElementType) =>
                {
                    object defaultVal = GetDefault(listElementType);
                    MethodInfo add = observableListObj.GetType()
                        .GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
                    if (add != null)
                    {
                        RecordUndo(property.serializedObject.targetObjects, "Add ObservableList Element");
                        add.Invoke(observableListObj, new[] { defaultVal });
                        MarkDirty(property.serializedObject.targetObjects);
                    }
                });
                property.serializedObject.Update();
            };

            // Remove => call RemoveAt(index) (fires OnRemove)
            rl.onRemoveCallback = list =>
            {
                int index = list.index;
                if (index < 0 || index >= list.count) return;

                ApplyToAllTargets(property, (observableListObj, _) =>
                {
                    MethodInfo removeAt = observableListObj.GetType()
                        .GetMethod("RemoveAt", BindingFlags.Instance | BindingFlags.Public);
                    if (removeAt != null)
                    {
                        RecordUndo(property.serializedObject.targetObjects, "Remove ObservableList Element");
                        removeAt.Invoke(observableListObj, new object[] { index });
                        MarkDirty(property.serializedObject.targetObjects);
                    }
                });
                property.serializedObject.Update();
            };

            // Reorder => emulate move via RemoveAt + Insert (fires OnRemove then OnAdd)
            rl.onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
            {
                if (oldIndex == newIndex) return;

                ApplyToAllTargets(property, (observableListObj, _) =>
                {
                    IList rawList = (IList)GetCollectionList(observableListObj);
                    if (rawList == null) return;

                    // Grab current value
                    object moved = rawList[oldIndex];

                    // Adjust target index after removal if moving down
                    int insertIndex = newIndex;
                    if (insertIndex > oldIndex) insertIndex--;

                    MethodInfo removeAt = observableListObj.GetType()
                        .GetMethod("RemoveAt", BindingFlags.Instance | BindingFlags.Public);
                    MethodInfo insert = observableListObj.GetType()
                        .GetMethod("Insert", BindingFlags.Instance | BindingFlags.Public);

                    if (removeAt != null && insert != null)
                    {
                        RecordUndo(property.serializedObject.targetObjects, "Reorder ObservableList Elements");
                        removeAt.Invoke(observableListObj, new object[] { oldIndex });
                        insert.Invoke(observableListObj, new object[] { insertIndex, moved });
                        MarkDirty(property.serializedObject.targetObjects);
                    }
                });

                property.serializedObject.Update();
            };

            // Context menu "Clear" => call Clear() (fires OnClear)
            rl.onCanRemoveCallback = list => list.count > 0;
            rl.onAddDropdownCallback = (rect, list) =>
            {
                GenericMenu menu = new();
                menu.AddItem(new GUIContent("Add Default"), false, () => rl.onAddCallback?.Invoke(rl));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Clear All"), false, () =>
                {
                    ApplyToAllTargets(property, (observableListObj, _) =>
                    {
                        MethodInfo clear = observableListObj.GetType()
                            .GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public);
                        if (clear != null)
                        {
                            RecordUndo(property.serializedObject.targetObjects, "Clear ObservableList");
                            clear.Invoke(observableListObj, null);
                            MarkDirty(property.serializedObject.targetObjects);
                        }
                    });
                    property.serializedObject.Update();
                });
                menu.ShowAsContext();
            };

            _lists[key] = rl;
            return rl;
        }

        // ===================== Helpers =====================

        private static void ApplyToAllTargets(SerializedProperty property, Action<object, Type> apply)
        {
            Object[] targets = property.serializedObject.targetObjects;
            string path = property.propertyPath; // path to ObservableList<T>
            foreach (Object t in targets)
            {
                object observableListObj = GetTargetObjectOfProperty(t, path);
                if (observableListObj == null) continue;

                // Find element type T from private List<T> 'collection'
                FieldInfo collectionField = observableListObj.GetType().GetField("collection",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (collectionField == null) continue;
                Type listType = collectionField.FieldType; // List<T>
                Type elemType = listType.IsGenericType ? listType.GetGenericArguments()[0] : typeof(object);

                apply(observableListObj, elemType);
            }
        }

        private static object GetCollectionList(object observableListObj)
        {
            FieldInfo f = observableListObj.GetType().GetField("collection",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return f?.GetValue(observableListObj);
        }

        private static void RecordUndo(UnityEngine.Object[] targets, string label)
        {
            foreach (Object t in targets)
            {
                Undo.RecordObject(t, label);
            }
        }

        private static void MarkDirty(UnityEngine.Object[] targets)
        {
            foreach (Object t in targets)
            {
                EditorUtility.SetDirty(t);
            }
        }

        private static object GetDefault(Type t)
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }

        /// <summary>
        /// Reads a boxed managed value from a SerializedProperty for common Unity types.
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
                case SerializedPropertyType.Enum:
                    return sp.intValue; // underlying enum numeric value
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
                    return GetBoxedValue(sp);
            }
        }

        /// <summary>
        /// Converts a boxed value to a target type, handling enums and IConvertible cases.
        /// </summary>
        private static object ConvertTo(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;

            if (targetType.IsEnum && value is IConvertible)
            {
                int intVal = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return Enum.ToObject(targetType, intVal);
            }

            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);

            return value;
        }

        // -------- Reflection path resolver --------

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

        /// <summary>
        /// Attempts to resolve the boxed managed value represented by a SerializedProperty.
        /// </summary>
        private static object GetBoxedValue(SerializedProperty prop)
        {
            return GetTargetObjectOfProperty(prop.serializedObject.targetObject, prop.propertyPath);
        }
    }
}
#endif
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace LegendaryTools.EditorTools
{
    /// <summary>
    /// Custom drawer for ObservableDictionary<T> that routes Inspector edits
    /// through the ObservableDictionary API (Add/Remove/Clear/indexer) to ensure change events fire.
    /// Supports two backings:
    /// - Without ODIN: SerializableDictionary<TKey,TValue> (keys/values lists).
    /// - With ODIN:     C# Dictionary<TKey,TValue> (edited via a snapshot + diff apply).
    /// </summary>
    [CustomPropertyDrawer(typeof(LegendaryTools.ObservableDictionary<,>), true)]
    public class ObservableDictionaryDrawer : PropertyDrawer
    {
        private readonly Dictionary<string, ReorderableList> _rlCache = new();
        private const float CellPadding = 2f;

        /// <summary>
        /// Gets the required height for the property drawing.
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
#if ODIN_INSPECTOR
            if (UsesOdin(property))
            {
                // Header + buttons + rows (approximate: 2 lines per row)
                int rows = GetOdinSnapshot(property).Count;
                return (EditorGUIUtility.singleLineHeight * (2 + rows * 2)) + 8f;
            }
#endif
            var rl = GetOrCreateList(property, label);
            return rl != null ? rl.GetHeight() : EditorGUIUtility.singleLineHeight;
        }

        /// <summary>
        /// Draws the property in the Inspector and applies changes through the public API to fire events.
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
#if ODIN_INSPECTOR
            if (UsesOdin(property))
            {
                DrawOdinDictionary(position, property, label);
                EditorGUI.EndProperty();
                return;
            }
#endif
            var rl = GetOrCreateList(property, label);
            if (rl == null)
            {
                EditorGUI.LabelField(position, label, new GUIContent("Unsupported dictionary type"));
                EditorGUI.EndProperty();
                return;
            }

            rl.DoList(position);
            EditorGUI.EndProperty();
        }

        // ========================= NON-ODIN (SerializableDictionary) =========================

        /// <summary>
        /// Creates (or reuses) a ReorderableList bound to SerializableDictionary keys/values arrays.
        /// </summary>
        private ReorderableList GetOrCreateList(SerializedProperty property, GUIContent label)
        {
#if ODIN_INSPECTOR
            if (UsesOdin(property)) return null;
#endif
            if (property == null) return null;

            string key = property.serializedObject.targetObject.GetInstanceID() + "|" + property.propertyPath;
            if (_rlCache.TryGetValue(key, out var existing)) return existing;

            var dictProp = property.FindPropertyRelative("dictionary");
            if (dictProp == null) return null;

            var keysProp = dictProp.FindPropertyRelative("keys");
            var valuesProp = dictProp.FindPropertyRelative("values");
            if (keysProp == null || valuesProp == null || !keysProp.isArray || !valuesProp.isArray)
                return null;

            // Dictionary is unordered; disable dragging to avoid misleading semantics.
            var rl = new ReorderableList(property.serializedObject, keysProp, false, true, true, true);

            rl.drawHeaderCallback = rect =>
            {
                float colWidth = rect.width / 2f;
                EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), label);
                rect.y += EditorGUIUtility.singleLineHeight;

                EditorGUI.LabelField(new Rect(rect.x, rect.y, colWidth - CellPadding, EditorGUIUtility.singleLineHeight), "Key");
                EditorGUI.LabelField(new Rect(rect.x + colWidth, rect.y, colWidth - CellPadding, EditorGUIUtility.singleLineHeight), "Value");
            };

            rl.elementHeightCallback = idx =>
            {
                var keyEl = keysProp.GetArrayElementAtIndex(idx);
                var valEl = valuesProp.GetArrayElementAtIndex(idx);
                float hk = EditorGUI.GetPropertyHeight(keyEl, GUIContent.none, true);
                float hv = EditorGUI.GetPropertyHeight(valEl, GUIContent.none, true);
                return hk + hv + 6f;
            };

            rl.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var keyEl = keysProp.GetArrayElementAtIndex(index);
                var valEl = valuesProp.GetArrayElementAtIndex(index);

                float colWidth = rect.width / 2f;
                var keyRect = new Rect(rect.x, rect.y + 2f, colWidth - CellPadding, EditorGUI.GetPropertyHeight(keyEl, GUIContent.none, true));
                var valRect = new Rect(rect.x + colWidth, rect.y + 2f, colWidth - CellPadding, EditorGUI.GetPropertyHeight(valEl, GUIContent.none, true));

                object oldKeyObj = ReadManagedValue(keyEl);
                object oldValObj = ReadManagedValue(valEl);

                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(keyRect, keyEl, GUIContent.none, true);
                EditorGUI.PropertyField(valRect, valEl, GUIContent.none, true);
                if (EditorGUI.EndChangeCheck())
                {
                    object newKeyObj = ReadManagedValue(keyEl);
                    object newValObj = ReadManagedValue(valEl);

                    ApplyToAllTargets(property, (obsDictObj, tKey, tValue) =>
                    {
                        object convertedOldKey = ConvertTo(oldKeyObj, tKey);
                        object convertedNewKey = ConvertTo(newKeyObj, tKey);
                        object convertedNewVal = ConvertTo(newValObj, tValue);

                        RecordUndo(property.serializedObject.targetObjects, "Modify ObservableDictionary Entry");

                        if (!Equals(convertedOldKey, convertedNewKey))
                        {
                            // Key change: Remove(oldKey) then Add(newKey, newVal) => fires OnRemove + OnAdd
                            InvokeRemoveByKey(obsDictObj, tKey, convertedOldKey);
                            InvokeAdd(obsDictObj, tKey, tValue, convertedNewKey, convertedNewVal);
                        }
                        else
                        {
                            // Value change: indexer set => fires OnUpdate
                            SetIndexer(obsDictObj, convertedNewKey, convertedNewVal);
                        }

                        MarkDirty(property.serializedObject.targetObjects);
                    });

                    property.serializedObject.Update();
                }
            };

            rl.onAddCallback = list =>
            {
                ApplyToAllTargets(property, (obsDictObj, tKey, tValue) =>
                {
                    object defKey = GetDefault(tKey);
                    object defVal = GetDefault(tValue);

                    RecordUndo(property.serializedObject.targetObjects, "Add ObservableDictionary Entry");
                    InvokeAdd(obsDictObj, tKey, tValue, defKey, defVal);
                    MarkDirty(property.serializedObject.targetObjects);
                });
                property.serializedObject.Update();
            };

            rl.onRemoveCallback = list =>
            {
                int index = list.index;
                if (index < 0 || index >= keysProp.arraySize) return;

                var keyEl = keysProp.GetArrayElementAtIndex(index);
                object keyObj = ReadManagedValue(keyEl);

                ApplyToAllTargets(property, (obsDictObj, tKey, _) =>
                {
                    object convertedKey = ConvertTo(keyObj, tKey);

                    RecordUndo(property.serializedObject.targetObjects, "Remove ObservableDictionary Entry");
                    InvokeRemoveByKey(obsDictObj, tKey, convertedKey);
                    MarkDirty(property.serializedObject.targetObjects);
                });
                property.serializedObject.Update();
            };

            rl.onAddDropdownCallback = (rect, list) =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Add Default"), false, () => rl.onAddCallback?.Invoke(rl));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Clear All"), false, () =>
                {
                    ApplyToAllTargets(property, (obsDictObj, _, __) =>
                    {
                        RecordUndo(property.serializedObject.targetObjects, "Clear ObservableDictionary");
                        InvokeClear(obsDictObj);
                        MarkDirty(property.serializedObject.targetObjects);
                    });
                    property.serializedObject.Update();
                });
                menu.ShowAsContext();
            };

            _rlCache[key] = rl;
            return rl;
        }

        // ========================= ODIN (C# Dictionary backing) =========================
#if ODIN_INSPECTOR
        /// <summary>
        /// Determines whether the property uses Odin path (i.e., backing field is a C# Dictionary).
        /// Uses reflection to avoid relying on SerializedProperty for unmanaged dictionaries.
        /// </summary>
        private bool UsesOdin(SerializedProperty property)
        {
            var obs = GetTargetObjectOfProperty(property.serializedObject.targetObject, property.propertyPath);
            if (obs == null) return false;

            var f = obs.GetType().GetField("dictionary", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) return false;

            var t = f.FieldType;
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>);
        }

        /// <summary>
        /// Takes a managed snapshot of the current dictionary entries.
        /// </summary>
        private List<KeyValuePair<object, object>> GetOdinSnapshot(SerializedProperty property)
        {
            var snapshot = new List<KeyValuePair<object, object>>();

            var obs = GetTargetObjectOfProperty(property.serializedObject.targetObject, property.propertyPath);
            if (obs == null) return snapshot;

            var fDict = obs.GetType().GetField("dictionary", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fDict == null) return snapshot;

            var dictObj = fDict.GetValue(obs);
            if (dictObj is IDictionary dict)
            {
                foreach (DictionaryEntry e in dict)
                    snapshot.Add(new KeyValuePair<object, object>(e.Key, e.Value));
            }
            else if (dictObj != null)
            {
                var enumerable = (IEnumerable)dictObj;
                foreach (var kv in enumerable)
                {
                    var t = kv.GetType();
                    var k = t.GetProperty("Key")?.GetValue(kv);
                    var v = t.GetProperty("Value")?.GetValue(kv);
                    snapshot.Add(new KeyValuePair<object, object>(k, v));
                }
            }

            return snapshot;
        }

        /// <summary>
        /// Draws an editable table for the C# Dictionary snapshot and applies a diff via ObservableDictionary API.
        /// </summary>
        private void DrawOdinDictionary(Rect position, SerializedProperty property, GUIContent label)
        {
            var before = GetOdinSnapshot(property);
            ResolveKeyValueTypes(property, out var tKey, out var tValue);

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(line, label);
            line.y += line.height + 4f;

            var btnRect = new Rect(line.x, line.y, position.width, line.height);
            float half = btnRect.width / 2f;
            var addRect = new Rect(btnRect.x, btnRect.y, half - 2f, btnRect.height);
            var clearRect = new Rect(btnRect.x + half + 2f, btnRect.y, half - 2f, btnRect.height);

            bool addPressed = GUI.Button(addRect, "Add Default");
            bool clearPressed = GUI.Button(clearRect, "Clear All");
            line.y += line.height + 4f;

            var list = new List<KeyValuePair<object, object>>(before);
            for (int i = 0; i < list.Count; i++)
            {
                var kv = list[i];

                var keyRect = new Rect(line.x, line.y, position.width - 86f, line.height);
                object newKey = DrawGenericField(keyRect, "Key", kv.Key, tKey);
                var rmRect = new Rect(position.xMax - 80f, keyRect.y, 80f, keyRect.height);
                if (GUI.Button(rmRect, "Remove"))
                {
                    list.RemoveAt(i);
                    i--;
                    continue;
                }
                line.y += line.height + 2f;

                var valRect = new Rect(line.x, line.y, position.width, line.height);
                object newVal = DrawGenericField(valRect, "Value", kv.Value, tValue);
                line.y += line.height + 6f;

                if (!Equals(newKey, kv.Key) || !Equals(newVal, kv.Value))
                    list[i] = new KeyValuePair<object, object>(newKey, newVal);
            }

            if (addPressed) list.Add(new KeyValuePair<object, object>(GetDefault(tKey), GetDefault(tValue)));
            if (clearPressed) list.Clear();

            bool changed = !SequenceEqual(before, list, (a, b) => Equals(a.Key, b.Key) && Equals(a.Value, b.Value));
            if (changed)
            {
                ApplyOdinDiff(property, before, list, tKey, tValue);
                property.serializedObject.Update();
            }
        }

        private static bool SequenceEqual(
            List<KeyValuePair<object, object>> a,
            List<KeyValuePair<object, object>> b,
            Func<KeyValuePair<object, object>, KeyValuePair<object, object>, bool> eq)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!eq(a[i], b[i])) return false;
            }
            return true;
        }

        private void ApplyOdinDiff(
            SerializedProperty property,
            List<KeyValuePair<object, object>> before,
            List<KeyValuePair<object, object>> after,
            Type tKey,
            Type tValue)
        {
            var beforeDict = new Dictionary<object, object>(new RefEq());
            foreach (var kv in before) beforeDict[kv.Key] = kv.Value;

            var afterDict = new Dictionary<object, object>(new RefEq());
            foreach (var kv in after) afterDict[kv.Key] = kv.Value;

            ApplyToAllTargets(property, (obsDictObj, _, __) =>
            {
                RecordUndo(property.serializedObject.targetObjects, "Edit ObservableDictionary");

                // Remove keys no longer present.
                foreach (var k in beforeDict.Keys.Where(k => !afterDict.ContainsKey(k)).ToList())
                    InvokeRemoveByKey(obsDictObj, tKey, ConvertTo(k, tKey));

                // Add / update
                foreach (var kv in afterDict)
                {
                    var k = ConvertTo(kv.Key, tKey);
                    var v = ConvertTo(kv.Value, tValue);

                    if (!beforeDict.ContainsKey(kv.Key))
                    {
                        // New entry
                        InvokeAdd(obsDictObj, tKey, tValue, k, v);
                    }
                    else
                    {
                        // Possible update
                        var oldV = ConvertTo(beforeDict[kv.Key], tValue);
                        if (!Equals(v, oldV))
                            SetIndexer(obsDictObj, k, v);
                    }
                }

                MarkDirty(property.serializedObject.targetObjects);
            });
        }

        private sealed class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) => object.Equals(x, y);
            public int GetHashCode(object obj) => obj == null ? 0 : obj.GetHashCode();
        }

        /// <summary>
        /// Draws a generic field for common types. Uses EnumPopup to support any enum underlying type.
        /// </summary>
        private object DrawGenericField(Rect rect, string label, object value, Type type)
        {
            var content = new GUIContent(label);

            if (type == typeof(string))
                return EditorGUI.TextField(rect, content, value as string ?? string.Empty);

            if (type == typeof(int))
                return EditorGUI.IntField(rect, content, value is int i ? i : 0);

            if (type == typeof(long))
            {
                long v = value is long l ? l : 0L;
                string s = EditorGUI.DelayedTextField(rect, content, v.ToString(CultureInfo.InvariantCulture));
                return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : v;
            }

            if (type == typeof(float))
                return EditorGUI.FloatField(rect, content, value is float f ? f : 0f);

            if (type == typeof(double))
            {
                double v = value is double d ? d : 0d;
                string s = EditorGUI.DelayedTextField(rect, content, v.ToString(CultureInfo.InvariantCulture));
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : v;
            }

            if (type == typeof(bool))
                return EditorGUI.Toggle(rect, content, value is bool b && b);

            if (type.IsEnum)
            {
                var current = value as Enum ?? (Enum)Enum.ToObject(type, 0);
                var newEnum = EditorGUI.EnumPopup(rect, content, current);
                return newEnum; // boxed enum of correct declared type
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return EditorGUI.ObjectField(rect, content, value as UnityEngine.Object, type, true);

            // Fallback: display ToString()
            EditorGUI.LabelField(rect, content, new GUIContent(value != null ? value.ToString() : "null"));
            return value;
        }

        private void ResolveKeyValueTypes(SerializedProperty property, out Type tKey, out Type tValue)
        {
            var obs = GetTargetObjectOfProperty(property.serializedObject.targetObject, property.propertyPath);
            var obsType = obs?.GetType();
            var genArgs = obsType?.GetGenericArguments();
            tKey = genArgs != null && genArgs.Length == 2 ? genArgs[0] : typeof(object);
            tValue = genArgs != null && genArgs.Length == 2 ? genArgs[1] : typeof(object);
        }
#endif

        // ========================= Shared helpers =========================

        /// <summary>
        /// Applies an action to all selected targets, resolving generic key/value types per instance.
        /// </summary>
        private static void ApplyToAllTargets(SerializedProperty property, Action<object, Type, Type> apply)
        {
            var targets = property.serializedObject.targetObjects;
            string path = property.propertyPath;
            foreach (var t in targets)
            {
                var obsDictObj = GetTargetObjectOfProperty(t, path);
                if (obsDictObj == null) continue;

                var genArgs = obsDictObj.GetType().GetGenericArguments();
                var tKey = genArgs.Length > 0 ? genArgs[0] : typeof(object);
                var tValue = genArgs.Length > 1 ? genArgs[1] : typeof(object);

                apply(obsDictObj, tKey, tValue);
            }
        }

        /// <summary>
        /// Sets an entry via the indexer (this[key] = value), wrapped with exception safety.
        /// </summary>
        private static void SetIndexer(object instance, object key, object value)
        {
            try
            {
                var idx = instance.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
                if (idx != null && idx.CanWrite)
                    idx.SetValue(instance, value, new[] { key });
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogError($"ObservableDictionary indexer set failed: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"ObservableDictionary indexer set failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Invoke Add(TKey, TValue) with exact signature to avoid ambiguous overloads.
        /// </summary>
        private static void InvokeAdd(object instance, Type tKey, Type tValue, object key, object value)
        {
            try
            {
                var mi = instance.GetType().GetMethod(
                    "Add",
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    types: new[] { tKey, tValue },
                    modifiers: null);

                if (mi == null)
                    throw new MissingMethodException($"{instance.GetType().Name}.Add({tKey.Name}, {tValue.Name}) not found.");

                mi.Invoke(instance, new[] { key, value });
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogError($"ObservableDictionary.Add failed: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"ObservableDictionary.Add failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Invoke Remove(TKey) with exact signature to avoid ambiguous overloads.
        /// </summary>
        private static void InvokeRemoveByKey(object instance, Type tKey, object key)
        {
            try
            {
                var mi = instance.GetType().GetMethod(
                    "Remove",
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    types: new[] { tKey },
                    modifiers: null);

                if (mi == null)
                    throw new MissingMethodException($"{instance.GetType().Name}.Remove({tKey.Name}) not found.");

                mi.Invoke(instance, new[] { key });
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogError($"ObservableDictionary.Remove failed: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"ObservableDictionary.Remove failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Safely invoke Clear().
        /// </summary>
        private static void InvokeClear(object instance)
        {
            try
            {
                var mi = instance.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public);
                mi?.Invoke(instance, null);
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogError($"ObservableDictionary.Clear failed: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"ObservableDictionary.Clear failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Records Undo for all targets.
        /// </summary>
        private static void RecordUndo(UnityEngine.Object[] targets, string label)
        {
            foreach (var t in targets) Undo.RecordObject(t, label);
        }

        /// <summary>
        /// Marks all targets dirty for Editor refresh.
        /// </summary>
        private static void MarkDirty(UnityEngine.Object[] targets)
        {
            foreach (var t in targets) EditorUtility.SetDirty(t);
        }

        /// <summary>
        /// Gets the default value for a given type (value type default or null).
        /// </summary>
        private static object GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

        /// <summary>
        /// Reads a boxed value from a SerializedProperty for common Unity types.
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
                    return sp.intValue; // underlying numeric value
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
        /// Converts a boxed value to a target type, including enums and IConvertible.
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
            {
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }

            return value;
        }

        /// <summary>
        /// Resolves the managed object for a SerializedProperty path starting at a root object.
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

                PropertyInfo p = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
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
                if (index >= 0 && index < list.Count) return list[index];
                return null;
            }

            if (source is IEnumerable enumerable)
            {
                var enm = enumerable.GetEnumerator();
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

#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.SOAP.Editor
{
    /// <summary>
    /// Inspector for SOEvent (0 args) and SOEvent<T1..T6>.
    /// - Manual Raise(..) UI with typed payload inputs
    /// - Lists all current subscribers (persistent list and one-shot list)
    /// - Ping target and Remove listener actions
    /// </summary>
    [CustomEditor(typeof(SOEventBase), true)]
    public class SOEventInspector : UnityEditor.Editor
    {
        // ---------- Cache for signature / inputs ----------
        private Type[] _cachedArgTypes;
        private MethodInfo _cachedRaise;
        private readonly Dictionary<int, object> _argInputs = new();
        private readonly Dictionary<int, Vector3> _quatEulerInputs = new();

        // ---------- Reflection targets for subscribers ----------
        private FieldInfo _fiListeners; // private readonly List<Action<...>> _listeners
        private FieldInfo _fiOnce; // private readonly List<Action<...>> _once
        private Type _delegateType; // Action / Action<T...>
        private MethodInfo _miRemove; // RemoveListener(Action<...>)

        // ---------- UI state ----------
        private string _search = string.Empty;
        private Vector2 _scrollSubs;

        public override void OnInspectorGUI()
        {
            // Draw default serialized fields (notes, etc.)
            base.OnInspectorGUI();
            EditorGUILayout.Space();

            SOEventBase evt = (SOEventBase)target;

            // ---- Runtime info / controls ----
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Event Tester", EditorStyles.boldLabel);
                DrawRuntimeInfo(evt);
                ResolveSignatureAndBind(evt);

                if (_cachedArgTypes == null)
                {
                    EditorGUILayout.HelpBox(
                        "Unsupported event base type. Ensure your event inherits SOEvent or SOEvent<T...>.",
                        MessageType.Error);
                }
                else
                {
                    DrawPayloadInputs(_cachedArgTypes);
                    EditorGUILayout.Space();
                    using (new EditorGUI.DisabledScope(_cachedRaise == null))
                    {
                        string btn = _cachedArgTypes.Length == 0
                            ? "Raise()"
                            : $"Raise({string.Join(", ", _cachedArgTypes.Select(NiceTypeName))})";
                        if (GUILayout.Button(btn)) TryRaise(_cachedRaise, _cachedArgTypes);
                    }
                }
            }

            // ---- Subscribers list ----
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Subscribers", EditorStyles.boldLabel);

                // Search/filter
                using (new EditorGUILayout.HorizontalScope())
                {
                    _search = EditorGUILayout.TextField(new GUIContent("Filter"), _search);
                    if (GUILayout.Button("Refresh", GUILayout.Width(90)))
                        // Just repaint: subscribers are runtime lists
                        Repaint();
                }

                // Resolve list fields once per frame
                ResolveSubscriberFields(evt);

                // Draw both lists
                DrawSubscribersSection(evt, false);
                EditorGUILayout.Space(6);
                DrawSubscribersSection(evt, true);
            }
        }

        // ========== Runtime counters / controls ==========
        private static void DrawRuntimeInfo(SOEventBase evt)
        {
            if (evt == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Listeners", evt.ListenerCount.ToString(), GUILayout.MaxWidth(180));
                EditorGUILayout.LabelField("Raised", evt.RaiseCount.ToString(), GUILayout.MaxWidth(180));
                EditorGUILayout.LabelField("Is Raising", evt.IsRaising ? "Yes" : "No", GUILayout.MaxWidth(180));
                GUILayout.FlexibleSpace();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Remove All Listeners"))
                {
                    Undo.RecordObject(evt, "Remove All Event Listeners");
                    evt.RemoveAllListeners();
                    EditorUtility.SetDirty(evt);
                }
            }
        }

        // ========== Payload UI ==========
        private void DrawPayloadInputs(Type[] argTypes)
        {
            bool allSupported = true;
            for (int i = 0; i < argTypes.Length; i++)
            {
                allSupported &= DrawArgField(i, argTypes[i]);
            }

            if (!allSupported)
                EditorGUILayout.HelpBox(
                    "One or more argument types are not supported by the tester UI. You may still raise with defaults.",
                    MessageType.Info);
        }

        private bool DrawArgField(int index, Type t)
        {
            GUIContent label = new($"Arg {index + 1} ({NiceTypeName(t)})");

            // Initialize default value if missing
            if (!_argInputs.ContainsKey(index))
            {
                _argInputs[index] = GetDefaultForType(t);
                if (t == typeof(Quaternion))
                    _quatEulerInputs[index] = Vector3.zero;
            }

            // Supported editors
            if (t == typeof(int))
            {
                int value = _argInputs[index] is int i ? i : 0;
                value = EditorGUILayout.IntField(label, value);
                _argInputs[index] = value;
                return true;
            }

            if (t == typeof(float))
            {
                float value = _argInputs[index] is float f ? f : 0f;
                value = EditorGUILayout.FloatField(label, value);
                _argInputs[index] = value;
                return true;
            }

            if (t == typeof(double))
            {
                double value = _argInputs[index] is double d ? d : 0d;
                string txt = EditorGUILayout.TextField(label, value.ToString("R", CultureInfo.InvariantCulture));
                if (double.TryParse(txt, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture,
                        out double d2))
                    _argInputs[index] = d2;
                return true;
            }

            if (t == typeof(long))
            {
                long value = _argInputs[index] is long l ? l : 0L;
                string txt = EditorGUILayout.TextField(label, value.ToString(CultureInfo.InvariantCulture));
                if (long.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l2))
                    _argInputs[index] = l2;
                return true;
            }

            if (t == typeof(bool))
            {
                bool value = _argInputs[index] is bool b && b;
                value = EditorGUILayout.Toggle(label, value);
                _argInputs[index] = value;
                return true;
            }

            if (t == typeof(string))
            {
                string value = _argInputs[index] as string ?? string.Empty;
                value = EditorGUILayout.TextField(label, value);
                _argInputs[index] = value;
                return true;
            }

            if (t == typeof(short))
            {
                short value = _argInputs[index] is short s ? s : (short)0;
                int temp = EditorGUILayout.IntField(label, value);
                _argInputs[index] = (short)Mathf.Clamp(temp, short.MinValue, short.MaxValue);
                return true;
            }

            if (t == typeof(byte))
            {
                byte value = _argInputs[index] is byte by ? by : (byte)0;
                int temp = EditorGUILayout.IntSlider(label, value, byte.MinValue, byte.MaxValue);
                _argInputs[index] = (byte)temp;
                return true;
            }

            if (t == typeof(uint))
            {
                uint value = _argInputs[index] is uint ui ? ui : 0u;
                string txt = EditorGUILayout.TextField(label, value.ToString(CultureInfo.InvariantCulture));
                if (uint.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint ui2))
                    _argInputs[index] = ui2;
                return true;
            }

            if (t == typeof(ulong))
            {
                ulong value = _argInputs[index] is ulong ul ? ul : 0ul;
                string txt = EditorGUILayout.TextField(label, value.ToString(CultureInfo.InvariantCulture));
                if (ulong.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong ul2))
                    _argInputs[index] = ul2;
                return true;
            }

            if (t == typeof(Vector2))
            {
                Vector2 v = _argInputs[index] is Vector2 v2 ? v2 : Vector2.zero;
                v = EditorGUILayout.Vector2Field(label, v);
                _argInputs[index] = v;
                return true;
            }

            if (t == typeof(Vector3))
            {
                Vector3 v = _argInputs[index] is Vector3 v3 ? v3 : Vector3.zero;
                v = EditorGUILayout.Vector3Field(label, v);
                _argInputs[index] = v;
                return true;
            }

            if (t == typeof(Color))
            {
                Color c = _argInputs[index] is Color col ? col : Color.white;
                c = EditorGUILayout.ColorField(label, c);
                _argInputs[index] = c;
                return true;
            }

            if (t == typeof(Quaternion))
            {
                Vector3 e = _quatEulerInputs.TryGetValue(index, out Vector3 eul) ? eul : Vector3.zero;
                e = EditorGUILayout.Vector3Field(new GUIContent($"{label.text} (Euler)"), e);
                _quatEulerInputs[index] = e;
                _argInputs[index] = Quaternion.Euler(e);
                return true;
            }

            if (t.IsEnum)
            {
                Enum current = _argInputs[index] as Enum ?? (Enum)Enum.GetValues(t).GetValue(0);
                Enum next = EditorGUILayout.EnumPopup(label, current);
                _argInputs[index] = next;
                return true;
            }

            if (typeof(Object).IsAssignableFrom(t))
            {
                Object current = _argInputs[index] as Object;
                Object next = EditorGUILayout.ObjectField(label, current, t, false);
                _argInputs[index] = next;
                return true;
            }

            EditorGUILayout.HelpBox($"Arg {index + 1} type '{t.FullName}' not supported by tester UI.",
                MessageType.Warning);
            return false;
        }

        private void TryRaise(MethodInfo raiseMethod, Type[] argTypes)
        {
            try
            {
                object[] args;
                if (argTypes.Length == 0)
                {
                    args = Array.Empty<object>();
                }
                else
                {
                    args = new object[argTypes.Length];
                    for (int i = 0; i < argTypes.Length; i++)
                    {
                        object v = _argInputs.TryGetValue(i, out object val) ? val : GetDefaultForType(argTypes[i]);
                        args[i] = CoerceValue(v, argTypes[i]);
                    }
                }

                raiseMethod.Invoke(target, args);
            }
            catch (TargetInvocationException tex)
            {
                Debug.LogException(tex.InnerException ?? tex);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        // ========== Subscribers listing ==========

        private void ResolveSubscriberFields(SOEventBase evt)
        {
            Type t = evt.GetType();

            // Walk inheritance to find event generic base
            Type evType = null;
            for (Type cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                if (cur == typeof(SOEvent))
                {
                    evType = cur;
                    break;
                }

                if (cur.IsGenericType)
                {
                    Type def = cur.GetGenericTypeDefinition();
                    if (def == typeof(SOEvent<>) ||
                        def == typeof(SOEvent<,>) ||
                        def == typeof(SOEvent<,,>) ||
                        def == typeof(SOEvent<,,,>) ||
                        def == typeof(SOEvent<,,,,>) ||
                        def == typeof(SOEvent<,,,,,>))
                    {
                        evType = cur;
                        break;
                    }
                }
            }

            if (evType == null)
            {
                _fiListeners = null;
                _fiOnce = null;
                _delegateType = null;
                _miRemove = null;
                return;
            }

            // _listeners / _once are defined on that concrete type
            _fiListeners = evType.GetField("_listeners", BindingFlags.Instance | BindingFlags.NonPublic);
            _fiOnce = evType.GetField("_once", BindingFlags.Instance | BindingFlags.NonPublic);

            // Determine delegate type from field generic args: List<Action<...>>
            if (_fiListeners != null)
            {
                Type listType = _fiListeners.FieldType; // List<Action<...>>
                if (listType.IsGenericType)
                {
                    Type listArg = listType.GetGenericArguments().FirstOrDefault(); // Action<...>
                    _delegateType = listArg;
                }
            }
            else
            {
                _delegateType = typeof(Action); // fallback
            }

            // RemoveListener signature: RemoveListener(Action<...>)
            _miRemove = t.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                    m.Name == "RemoveListener" &&
                    m.GetParameters().Length == 1 &&
                    _delegateType != null &&
                    m.GetParameters()[0].ParameterType == _delegateType);
        }

        private void DrawSubscribersSection(SOEventBase evt, bool isOnce)
        {
            IList delegates = GetDelegateList(evt, isOnce);
            string header = isOnce ? "One-shot Listeners" : "Persistent Listeners";

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                EditorGUILayout.LabelField($"{header} ({(delegates != null ? delegates.Count : 0)})",
                    EditorStyles.boldLabel);

                if (delegates == null || delegates.Count == 0)
                {
                    EditorGUILayout.LabelField("— none —");
                    return;
                }

                _scrollSubs = EditorGUILayout.BeginScrollView(_scrollSubs, GUILayout.MinHeight(120));

                for (int i = 0; i < delegates.Count; i++)
                {
                    Delegate dlg = delegates[i] as Delegate;
                    if (dlg == null) continue;

                    string row = FormatDelegateRow(dlg, out Object pingObj);
                    if (!string.IsNullOrEmpty(_search))
                    {
                        if (!row.IndexOf(_search, StringComparison.OrdinalIgnoreCase).Equals(-1))
                        {
                            // pass
                        }
                        else
                        {
                            continue; // filtered out
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(row, GUILayout.ExpandWidth(true));

                        if (pingObj != null && GUILayout.Button("Ping", GUILayout.Width(50)))
                        {
                            EditorGUIUtility.PingObject(pingObj);
                            Selection.activeObject = pingObj;
                        }

                        using (new EditorGUI.DisabledScope(_miRemove == null))
                        {
                            if (GUILayout.Button("Remove", GUILayout.Width(70))) TryRemoveListener(evt, dlg);
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private IList GetDelegateList(SOEventBase evt, bool once)
        {
            try
            {
                FieldInfo fi = once ? _fiOnce : _fiListeners;
                if (fi == null) return null;
                return fi.GetValue(evt) as IList;
            }
            catch
            {
                return null;
            }
        }

        private string FormatDelegateRow(Delegate d, out Object ping)
        {
            ping = null;
            MethodInfo method = d.Method;
            object tgt = d.Target;

            string targetStr;
            if (tgt is Object uo && uo != null)
            {
                ping = uo;
                targetStr = $"{uo.name} : {uo.GetType().Name}";
            }
            else if (tgt != null)
            {
                targetStr = $"{tgt.GetType().Name}";
            }
            else
            {
                targetStr = "(static)";
            }

            string typeStr = $"{method.DeclaringType?.Name}.{method.Name}";
            return $"{targetStr}  →  {typeStr}";
        }

        private void TryRemoveListener(SOEventBase evt, Delegate d)
        {
            try
            {
                if (_miRemove == null) return;
                Undo.RecordObject(evt, "Remove Listener");
                _miRemove.Invoke(evt, new object[] { d });
                EditorUtility.SetDirty(evt);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        // ========== Signature resolution (from previous version) ==========
        private void ResolveSignatureAndBind(SOEventBase evt)
        {
            Type concreteType = evt.GetType();

            // 0-args concrete SOEvent
            if (typeof(SOEvent).IsAssignableFrom(concreteType) && concreteType == typeof(SOEvent))
            {
                _cachedArgTypes = Array.Empty<Type>();
                _cachedRaise = concreteType.GetMethod("Raise", BindingFlags.Instance | BindingFlags.Public);
                return;
            }

            // Walk inheritance for SOEvent<T...>
            for (Type t = concreteType; t != null && t != typeof(object); t = t.BaseType)
            {
                if (!t.IsGenericType) continue;

                Type def = t.GetGenericTypeDefinition();
                if (def == typeof(SOEvent<>) ||
                    def == typeof(SOEvent<,>) ||
                    def == typeof(SOEvent<,,>) ||
                    def == typeof(SOEvent<,,,>) ||
                    def == typeof(SOEvent<,,,,>) ||
                    def == typeof(SOEvent<,,,,,>))
                {
                    Type[] args = t.GetGenericArguments();

                    // Find exact Raise signature on concrete type
                    MethodInfo m = concreteType.GetMethod(
                        "Raise",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        args,
                        null);

                    _cachedArgTypes = args;
                    _cachedRaise = m;
                    return;
                }
            }

            // Could be a concrete subclass of SOEvent (0 args)
            if (typeof(SOEvent).IsAssignableFrom(concreteType))
            {
                _cachedArgTypes = Array.Empty<Type>();
                _cachedRaise = concreteType.GetMethod("Raise", BindingFlags.Instance | BindingFlags.Public, null,
                    Type.EmptyTypes, null);
                return;
            }

            _cachedArgTypes = null;
            _cachedRaise = null;
        }

        // ========== Small helpers (same as before) ==========
        private static object GetDefaultForType(Type t)
        {
            if (t == typeof(string)) return string.Empty;
            if (t == typeof(bool)) return false;
            if (t == typeof(Vector2)) return Vector2.zero;
            if (t == typeof(Vector3)) return Vector3.zero;
            if (t == typeof(Color)) return Color.white;
            if (t == typeof(Quaternion)) return Quaternion.identity;
            if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
            if (typeof(Object).IsAssignableFrom(t)) return null;

            try
            {
                return Activator.CreateInstance(t);
            }
            catch
            {
                return null;
            }
        }

        private static string NiceTypeName(Type t)
        {
            if (t == typeof(int)) return "int";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(long)) return "long";
            if (t == typeof(uint)) return "uint";
            if (t == typeof(ulong)) return "ulong";
            if (t == typeof(short)) return "short";
            if (t == typeof(byte)) return "byte";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t == typeof(Vector2)) return "Vector2";
            if (t == typeof(Vector3)) return "Vector3";
            if (t == typeof(Color)) return "Color";
            if (t == typeof(Quaternion)) return "Quaternion";
            if (t.IsEnum) return t.Name;
            if (typeof(Object).IsAssignableFrom(t)) return t.Name;
            return t.Name;
        }

        private static object CoerceValue(object value, Type targetType)
        {
            if (value == null) return GetNullSafeDefault(targetType);

            try
            {
                if (targetType.IsInstanceOfType(value)) return value;

                // Strings to numerics
                if (value is string s)
                {
                    if (targetType == typeof(int) &&
                        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i;
                    if (targetType == typeof(float) && float.TryParse(s,
                            NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture,
                            out float f)) return f;
                    if (targetType == typeof(double) && double.TryParse(s,
                            NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture,
                            out double d)) return d;
                    if (targetType == typeof(long) &&
                        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
                    if (targetType == typeof(uint) && uint.TryParse(s, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out uint ui)) return ui;
                    if (targetType == typeof(ulong) && ulong.TryParse(s, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out ulong ul)) return ul;
                    if (targetType == typeof(short) && short.TryParse(s, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out short sh)) return sh;
                    if (targetType == typeof(byte) && byte.TryParse(s, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out byte by)) return by;
                }

                // Enum from string
                if (targetType.IsEnum)
                    if (value is string es && Enum.IsDefined(targetType, es))
                        return Enum.Parse(targetType, es);

                // Last resort
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                return GetNullSafeDefault(targetType);
            }
        }

        private static object GetNullSafeDefault(Type t)
        {
            if (t == typeof(string)) return string.Empty;
            if (t == typeof(bool)) return false;
            if (t == typeof(Vector2)) return Vector2.zero;
            if (t == typeof(Vector3)) return Vector3.zero;
            if (t == typeof(Color)) return Color.white;
            if (t == typeof(Quaternion)) return Quaternion.identity;
            if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
            if (typeof(Object).IsAssignableFrom(t)) return null;

            try
            {
                return Activator.CreateInstance(t);
            }
            catch
            {
                return null;
            }
        }
    }
}
#endif
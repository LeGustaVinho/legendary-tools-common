using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LegendaryTools.FixedPoint.Editor
{
    [CustomPropertyDrawer(typeof(SerializedFixedVector2))]
    public sealed class FixedVector2Drawer : FixedVectorDrawer { protected override int Dimensions => 2; }
    [CustomPropertyDrawer(typeof(SerializedFixedVector3))]
    public sealed class FixedVector3Drawer : FixedVectorDrawer { protected override int Dimensions => 3; }

    public abstract class FixedVectorDrawer : PropertyDrawer
    {
        protected abstract int Dimensions { get; }
        private static readonly string[] Axes = { "x", "y", "z" };
        private readonly Dictionary<string, FixedStatus> messages = new Dictionary<string, FixedStatus>();
        private static int Precision(SerializedProperty value) => FixedVectorSerialization.Precision((byte)value.FindPropertyRelative("_precisionCode").intValue);
        private static string Message(FixedStatus status) => status.IsSuccess()
            ? "Valor arredondado para as casas decimais selecionadas."
            : "Valor inválido ou fora do intervalo da precisão selecionada: " + status;
        private string Key(SerializedProperty p) => p.serializedObject.targetObject.GetInstanceID() + ":" + p.propertyPath;

        // Validate every target first. Parsing never passes through an IEEE floating-point field.
        internal static bool TryEdit(SerializedProperty property, int dimensions, int axis, string text, int newPrecision, out FixedStatus status)
        {
            status = FixedStatus.None;
            Object[] targets = property.serializedObject.targetObjects;
            var serialized = new SerializedObject[targets.Length]; var values = new SerializedProperty[targets.Length];
            var candidates = new long[targets.Length, dimensions];
            for (int i = 0; i < targets.Length; i++)
            {
                serialized[i] = new SerializedObject(targets[i]); values[i] = serialized[i].FindProperty(property.propertyPath);
                int code = values[i].FindPropertyRelative("_precisionCode").intValue;
                if (code < 0 || code > 19) { status = FixedStatus.InvalidConfiguration; return false; }
                int oldPrecision = Precision(values[i]);
                for (int j = 0; j < dimensions; j++)
                {
                    long raw = values[i].FindPropertyRelative("_" + Axes[j] + "Raw").longValue;
                    candidates[i, j] = raw;
                    if (axis < 0)
                    {
                        FixedVectorSerialization.TryRescale(raw, oldPrecision, newPrecision, out candidates[i, j], out FixedStatus s);
                        status |= s;
                    }
                    else if (j == axis)
                    {
                        FixedVectorSerialization.TryParse(text, oldPrecision, out candidates[i, j], out FixedStatus s);
                        status |= s;
                    }
                }
            }
            if (!status.IsSuccess()) return false;
            property.serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("Edit fixed-point vector");
            for (int i = 0; i < targets.Length; i++)
            {
                for (int j = 0; j < dimensions; j++) values[i].FindPropertyRelative("_" + Axes[j] + "Raw").longValue = candidates[i, j];
                if (axis < 0) values[i].FindPropertyRelative("_precisionCode").intValue = newPrecision + 1;
                serialized[i].ApplyModifiedProperties();
            }
            property.serializedObject.Update(); return true;
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            (messages.TryGetValue(Key(property), out FixedStatus s) && s != FixedStatus.None ? 3 : 2) * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            bool mixed = EditorGUI.showMixedValue;
            try
            {
                float height = EditorGUIUtility.singleLineHeight, spacing = EditorGUIUtility.standardVerticalSpacing;
                var row = new Rect(position.x, position.y, position.width, height);
                var precisionProperty = property.FindPropertyRelative("_precisionCode");
                EditorGUI.showMixedValue = precisionProperty.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck(); int precision = EditorGUI.DelayedIntField(row, new GUIContent(label.text + " · casas decimais"), Precision(property));
                if (EditorGUI.EndChangeCheck()) { TryEdit(property, Dimensions, -1, null, precision, out FixedStatus status); messages[Key(property)] = status; }
                int currentPrecision = Precision(property);
                float width = (position.width - spacing * (Dimensions - 1)) / Dimensions;
                for (int i = 0; i < Dimensions; i++)
                {
                    SerializedProperty raw = property.FindPropertyRelative("_" + Axes[i] + "Raw");
                    EditorGUI.showMixedValue = raw.hasMultipleDifferentValues || precisionProperty.hasMultipleDifferentValues;
                    row = new Rect(position.x + i * (width + spacing), position.y + height + spacing, width, height);
                    float labelWidth = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 14;
                    EditorGUI.BeginChangeCheck(); string input = EditorGUI.DelayedTextField(row, Axes[i].ToUpperInvariant(), FixedVectorSerialization.Format(raw.longValue, currentPrecision));
                    EditorGUIUtility.labelWidth = labelWidth;
                    if (EditorGUI.EndChangeCheck()) { TryEdit(property, Dimensions, i, input, -1, out FixedStatus status); messages[Key(property)] = status; }
                }
                if (messages.TryGetValue(Key(property), out FixedStatus message) && message != FixedStatus.None)
                    EditorGUI.HelpBox(new Rect(position.x, position.y + 2 * (height + spacing), position.width, height), Message(message), message.IsSuccess() ? MessageType.Info : MessageType.Error);
            }
            finally { EditorGUI.showMixedValue = mixed; EditorGUI.EndProperty(); }
        }
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var precision = new IntegerField(property.displayName + " · casas decimais") { isDelayed = true };
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            var fields = new TextField[Dimensions];
            var message = new HelpBox("", HelpBoxMessageType.Info); message.style.display = DisplayStyle.None;
            root.Add(precision); root.Add(row); root.Add(message);
            for (int i = 0; i < Dimensions; i++)
            {
                int axis = i; fields[i] = new TextField(Axes[i].ToUpperInvariant()) { isDelayed = true };
                fields[i].style.flexGrow = 1; fields[i].style.flexBasis = 0;
                fields[i].Q<Label>().style.minWidth = 14; fields[i].Q<Label>().style.width = 14;
                row.Add(fields[i]);
                fields[i].RegisterValueChangedCallback(evt =>
                {
                    TryEdit(property, Dimensions, axis, evt.newValue, -1, out FixedStatus status);
                    Show(status); Refresh();
                });
            }
            precision.RegisterValueChangedCallback(evt => { TryEdit(property, Dimensions, -1, null, evt.newValue, out FixedStatus status); Show(status); Refresh(); });
            void Show(FixedStatus status)
            {
                message.style.display = status == FixedStatus.None ? DisplayStyle.None : DisplayStyle.Flex;
                message.text = Message(status); message.messageType = status.IsSuccess() ? HelpBoxMessageType.Info : HelpBoxMessageType.Error;
            }
            void Refresh()
            {
                property.serializedObject.Update(); int p = Precision(property);
                bool mixedPrecision = property.FindPropertyRelative("_precisionCode").hasMultipleDifferentValues;
                precision.SetValueWithoutNotify(p); precision.showMixedValue = mixedPrecision;
                for (int i = 0; i < Dimensions; i++)
                {
                    var raw = property.FindPropertyRelative("_" + Axes[i] + "Raw");
                    fields[i].SetValueWithoutNotify(FixedVectorSerialization.Format(raw.longValue, p));
                    fields[i].showMixedValue = mixedPrecision || raw.hasMultipleDifferentValues;
                }
            }
            root.TrackSerializedObjectValue(property.serializedObject, _ => Refresh()); Refresh(); return root;
        }
    }
}

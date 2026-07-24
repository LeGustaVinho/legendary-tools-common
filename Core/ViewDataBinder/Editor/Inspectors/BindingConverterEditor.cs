using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    [CustomEditor(typeof(BindingConverter), true)]
    public sealed class BindingConverterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            var converter = (BindingConverter)target;
            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Conversion Contract", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Forward",
                    $"{GetFriendlyTypeName(converter.SourceType)} -> {GetFriendlyTypeName(converter.TargetType)}");
                EditorGUILayout.LabelField(
                    "Reverse",
                    converter.SupportsReverseConversion
                        ? $"{GetFriendlyTypeName(converter.TargetType)} -> {GetFriendlyTypeName(converter.SourceType)}"
                        : "Not supported");
            }
        }

        private static string GetFriendlyTypeName(System.Type type)
        {
            return type == null ? "Unknown" : type.Name;
        }
    }
}

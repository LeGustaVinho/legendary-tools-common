using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    [CustomEditor(typeof(ViewDataBindingProfile))]
    public sealed class ViewDataBindingProfileEditor : UnityEditor.Editor
    {
        private SerializedProperty bindingsProperty;
        private ViewDataBinder authoringBinder;

        private void OnEnable()
        {
            bindingsProperty = serializedObject.FindProperty("bindings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Profiles are reusable binding definitions. Source endpoints normally use the $Source context and Targets use $Target. Each ViewDataBinder profile reference supplies the concrete roots. Additional Context names can be provided through named overrides or an inherited Binding Data Context.",
                MessageType.Info);

            authoringBinder = (ViewDataBinder)EditorGUILayout.ObjectField(
                "Authoring Binder",
                authoringBinder,
                typeof(ViewDataBinder),
                true);

            using (new EditorGUI.DisabledScope(authoringBinder == null))
            {
                if (GUILayout.Button("Replace Profile From Binder"))
                {
                    ReplaceFromBinder();
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.PropertyField(bindingsProperty, true);
            serializedObject.ApplyModifiedProperties();
        }

        private void ReplaceFromBinder()
        {
            if (!BindingProfileEditorUtility.TryCaptureSharedRoots(
                    authoringBinder,
                    out BindingProfileEditorUtility.RootSnapshot sourceRoot,
                    out BindingProfileEditorUtility.RootSnapshot targetRoot,
                    out string error))
            {
                EditorUtility.DisplayDialog("Cannot Replace Profile", error, "OK");
                return;
            }

            Undo.RecordObject(target, "Replace Binding Profile");
            BindingProfileEditorUtility.CopyLocalBindingsToProfile(
                authoringBinder,
                (ViewDataBindingProfile)target,
                sourceRoot,
                targetRoot);
            serializedObject.Update();
        }
    }
}

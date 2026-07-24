using UnityEditor;

namespace LegendaryTools.ViewBinding.Editor
{
    [CustomEditor(typeof(BindingDataContext))]
    public sealed class BindingDataContextEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Child binders resolve the nearest active context with the requested name. Use Default for a single inherited ViewModel, or names such as Player, Settings, Localization, Inventory, and Session.",
                MessageType.Info);
            DrawDefaultInspector();
        }
    }
}

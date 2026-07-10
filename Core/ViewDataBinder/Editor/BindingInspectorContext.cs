using UnityEditor;

namespace LegendaryTools.ViewBinding.Editor
{
    public sealed class BindingInspectorContext
    {
        public BindingInspectorContext(
            ViewDataBinder binder,
            SerializedObject serializedObject,
            SerializedProperty bindingProperty,
            int bindingIndex)
        {
            Binder = binder;
            SerializedObject = serializedObject;
            BindingProperty = bindingProperty;
            BindingIndex = bindingIndex;
        }

        public ViewDataBinder Binder { get; }

        public SerializedObject SerializedObject { get; }

        public SerializedProperty BindingProperty { get; }

        public int BindingIndex { get; }
    }
}

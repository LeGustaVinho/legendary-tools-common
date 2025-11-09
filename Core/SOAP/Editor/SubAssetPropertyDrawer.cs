#if UNITY_EDITOR
using System.Reflection;
using LegendaryTools.SOAP.SubAssets;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.SOAP.SubAssetsEditor
{
    /// <summary>
    /// PropertyDrawer for [SubAsset]. Schedules subasset creation on next editor tick
    /// to avoid mutating AssetDatabase during GUI drawing.
    /// Works for single object fields (not lists/arrays).
    /// </summary>
    [CustomPropertyDrawer(typeof(SubAssetAttribute))]
    public class SubAssetPropertyDrawer : PropertyDrawer
    {
        private bool _scheduled; // Avoid scheduling multiple times in the same repaint

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Schedule creation only if:
            // - It's an object reference field
            // - Belongs to a ScriptableObject asset on disk
            // - Is currently null
            if (!_scheduled &&
                property.propertyType == SerializedPropertyType.ObjectReference &&
                property.serializedObject?.targetObject is ScriptableObject owner &&
                property.objectReferenceValue == null)
            {
                string path = AssetDatabase.GetAssetPath(owner);
                if (!string.IsNullOrEmpty(path))
                {
                    _scheduled = true; // prevent double-scheduling on multiple OnGUI calls

                    // Capture locals
                    FieldInfo fi = fieldInfo;
                    SubAssetAttribute attr = (SubAssetAttribute)attribute;

                    // Defer to next editor tick (safe context for AssetDatabase ops)
                    EditorApplication.delayCall += () =>
                    {
                        // Target may have been destroyed or not an asset anymore
                        if (owner == null) return;
                        string currentPath = AssetDatabase.GetAssetPath(owner);
                        if (string.IsNullOrEmpty(currentPath)) return;

                        if (fi != null)
                        {
                            bool created = SubAssetEditorUtility.EnsureSingleSubAsset(
                                owner, fi, attr?.ChildName
                            );

                            if (created)
                                // Refresh the serialized object to reflect new reference
                                if (property.serializedObject != null)
                                    property.serializedObject.Update();
                        }

                        // Allow scheduling again on future GUI cycles if still needed
                        _scheduled = false;
                    };
                }
            }

            // Draw the actual property (after scheduling)
            EditorGUI.PropertyField(position, property, label, true);
            EditorGUI.EndProperty();
        }
    }
}
#endif
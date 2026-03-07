using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [InitializeOnLoad]
    public static class AssetUsageFinderContextMenus
    {
        static AssetUsageFinderContextMenus()
        {
            EditorApplication.contextualPropertyMenu -= OnContextualPropertyMenu;
            EditorApplication.contextualPropertyMenu += OnContextualPropertyMenu;
        }

        [MenuItem("Assets/Find Usages", false, 2001)]
        private static void FindUsagesForSelectedAsset()
        {
            if (Selection.activeObject != null)
                AssetUsageFinderWindow.ShowWindowForAsset(Selection.activeObject);
        }

        [MenuItem("Assets/Find Usages", true)]
        private static bool ValidateFindUsagesForSelectedAsset()
        {
            return Selection.activeObject != null;
        }

        [MenuItem("GameObject/Find Usages", false, 0)]
        private static void FindUsagesForSelectedGameObject()
        {
            if (Selection.activeGameObject != null)
            {
                AssetUsageFinderWindow.ShowWindowForContextual(
                    AssetUsageFinderContextualRequest.CreateForGameObject(Selection.activeGameObject));
            }
        }

        [MenuItem("GameObject/Find Usages", true)]
        private static bool ValidateFindUsagesForSelectedGameObject()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem("CONTEXT/Component/Find Usages")]
        private static void FindUsagesForComponent(MenuCommand command)
        {
            if (command.context is Component component)
            {
                AssetUsageFinderWindow.ShowWindowForContextual(
                    AssetUsageFinderContextualRequest.CreateForComponent(component));
            }
        }

        private static void OnContextualPropertyMenu(GenericMenu menu, SerializedProperty property)
        {
            if (menu == null || property == null || property.serializedObject == null)
                return;

            if (property.propertyPath == "m_Script")
                return;

            Object targetObject = property.serializedObject.targetObject;
            Object[] targetObjects = property.serializedObject.targetObjects;
            if (targetObject == null || targetObjects == null || targetObjects.Length != 1)
                return;

            AssetUsageFinderContextualRequest request =
                AssetUsageFinderContextualRequest.CreateForSerializedProperty(property);
            if (request == null)
                return;

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Find Usages"), false,
                () => AssetUsageFinderWindow.ShowWindowForContextual(request));
        }
    }
}

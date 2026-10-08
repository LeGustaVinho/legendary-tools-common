using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public static class GameObjectPathMenu
    {
        private const string MenuPath = "GameObject/Legendary Tools/Get GameObject Path";

        [MenuItem(MenuPath, false, 10)]
        private static void CopyGameObjectPath(MenuCommand command)
        {
            GameObject gameObject = GetTarget(command);
            if (gameObject == null)
            {
                return;
            }

            PrefabStage prefabStage = PrefabStageUtility.GetPrefabStage(gameObject);
            Transform root = prefabStage != null
                ? prefabStage.prefabContentsRoot.transform
                : gameObject.transform.root;

            EditorGUIUtility.systemCopyBuffer = GetPath(gameObject.transform, root);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateCopyGameObjectPath(MenuCommand command)
        {
            return GetTarget(command) != null;
        }

        private static GameObject GetTarget(MenuCommand command)
        {
            GameObject gameObject = command?.context as GameObject;
            return gameObject != null ? gameObject : Selection.activeGameObject;
        }

        private static string GetPath(Transform transform, Transform root)
        {
            if (transform == root || transform.parent == null)
            {
                return transform.name;
            }

            return GetPath(transform.parent, root) + "/" + transform.name;
        }
    }
}

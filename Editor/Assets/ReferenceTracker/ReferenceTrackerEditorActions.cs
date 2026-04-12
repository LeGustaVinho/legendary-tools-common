using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal static class ReferenceTrackerEditorActions
    {
        public static void Ping(ReferenceTrackerUsageResult result)
        {
            if (result.HostGameObject != null)
            {
                EditorGUIUtility.PingObject(result.HostGameObject);
            }
            else if (result.HostComponent != null)
            {
                EditorGUIUtility.PingObject(result.HostComponent);
            }
        }

        public static void Select(ReferenceTrackerUsageResult result)
        {
            if (result.HostComponent != null)
            {
                Selection.activeObject = result.HostComponent;
                EditorGUIUtility.PingObject(result.HostGameObject != null ? result.HostGameObject : result.HostComponent);
                return;
            }

            if (result.HostGameObject != null)
            {
                Selection.activeObject = result.HostGameObject;
                EditorGUIUtility.PingObject(result.HostGameObject);
            }
        }

        public static bool CanOpenOwningPrefab(GameObject hostGameObject)
        {
            return hostGameObject != null && PrefabUtility.IsPartOfPrefabInstance(hostGameObject);
        }

        public static void OpenOwningPrefab(GameObject hostGameObject)
        {
            if (!CanOpenOwningPrefab(hostGameObject))
            {
                return;
            }

            GameObject sourceRoot = PrefabUtility.GetOriginalSourceRootWhereGameObjectIsAdded(hostGameObject);
            if (sourceRoot == null)
            {
                sourceRoot = PrefabUtility.GetCorrespondingObjectFromOriginalSource(hostGameObject);
            }

            string assetPath = AssetDatabase.GetAssetPath(sourceRoot);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            PrefabStageUtility.OpenPrefab(assetPath);
        }
    }
}

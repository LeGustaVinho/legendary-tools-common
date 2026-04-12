using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal static class ReferenceTrackerEditorActions
    {
        public static void Ping(ReferenceTrackerUsageResult result)
        {
            UnityEngine.Object target = GetBestSelectableObject(result);
            if (target != null)
            {
                EditorGUIUtility.PingObject(target);
            }
        }

        public static void Select(ReferenceTrackerUsageResult result)
        {
            UnityEngine.Object target = GetBestSelectableObject(result);
            if (target != null)
            {
                Selection.activeObject = target;
                EditorGUIUtility.PingObject(target);
            }
        }

        public static UnityEngine.Object GetBestSelectableObject(ReferenceTrackerUsageResult result)
        {
            if (result == null)
            {
                return null;
            }

            if (result.IsLiveContext && result.HostComponent != null)
            {
                return result.HostComponent;
            }

            if (result.IsLiveContext && result.HostGameObject != null)
            {
                return result.HostGameObject;
            }

            if (result.HostObject != null)
            {
                return result.HostObject;
            }

            return result.AssetObject;
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

        public static bool CanOpenPrefab(ReferenceTrackerUsageResult result)
        {
            if (result == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(result.AssetPath) &&
                   result.AssetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase);
        }

        public static void OpenPrefab(ReferenceTrackerUsageResult result)
        {
            if (CanOpenPrefab(result))
            {
                PrefabStageUtility.OpenPrefab(result.AssetPath);
                return;
            }

            if (result != null && CanOpenOwningPrefab(result.HostGameObject))
            {
                OpenOwningPrefab(result.HostGameObject);
            }
        }

        public static bool CanOpenScene(ReferenceTrackerUsageResult result)
        {
            if (result == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(result.AssetPath) &&
                   result.AssetPath.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase);
        }

        public static void OpenScene(ReferenceTrackerUsageResult result)
        {
            if (!CanOpenScene(result))
            {
                return;
            }

            EditorSceneManager.OpenScene(result.AssetPath);
        }

        public static string GetCopyPath(ReferenceTrackerUsageResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(result.HostGameObjectPath))
            {
                return string.Format("{0}#{1}", result.AssetPath, result.HostGameObjectPath);
            }

            return result.AssetPath ?? string.Empty;
        }
    }
}

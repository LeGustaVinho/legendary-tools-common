using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace LegendaryTools
{
    public static class EditorExtensions
    {
#if UNITY_EDITOR
        public static List<T> FindAssetConfigNear<T>(this Object obj) where T : Object
        {
            string screenFlowConfigPath = AssetDatabase.GetAssetPath(obj);
            string screenFlowFolder = Path.GetDirectoryName(screenFlowConfigPath);

            return FindAssetsByType<T>(screenFlowFolder);
        }

        public static List<T> FindAssetsByType<T>(params string[] inFolders) where T : Object
        {
            List<T> assets = new List<T>();
            string filter = "t:" + typeof(T).Name;
            string[] guids = AssetDatabase.FindAssets(filter, inFolders);
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }
        
        public static List<T> FindAssetsByName<T>(string name, params string[] inFolders) where T : Object
        {
            List<T> assets = new List<T>();
            string filter = name;
            string[] guids = AssetDatabase.FindAssets(filter, inFolders);
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }
#endif
    }
}
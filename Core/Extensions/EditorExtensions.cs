using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

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
        
        public static List<(T, GameObject)> FindPrefabsOfType<T>()
        {
            List<(T, GameObject)> identifiedPrefabs = new List<(T, GameObject)>();

            // Obter todos os tipos que implementam IIdentifiedGameObject
            List<Type> identifiedTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => typeof(T).IsAssignableFrom(type))
                .ToList();

            // Criar um dicionário para armazenar o caminho do script de cada tipo
            Dictionary<Type, string> typeScriptPaths = new();

            foreach (Type type in identifiedTypes)
            {
                string scriptPath = GetScriptAssetPath(type);
                if (!string.IsNullOrEmpty(scriptPath)) typeScriptPaths[type] = scriptPath;
            }

            // Obter todos os caminhos de prefabs
            string[] allPrefabs = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (string prefabPath in allPrefabs)
            {
                // Obter dependências do prefab
                string[] dependencies = AssetDatabase.GetDependencies(prefabPath, false);

                foreach (KeyValuePair<Type, string> kvp in typeScriptPaths)
                {
                    if (dependencies.Contains(kvp.Value))
                    {
                        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        T component = prefabAsset.GetComponentInChildren<T>(true);

                        (T, GameObject) foundTuple = identifiedPrefabs.Find(item => item.Item2 == prefabAsset);

                        if (prefabAsset != null && foundTuple.Item2 == null)
                            identifiedPrefabs.Add((component, prefabAsset));
                        break;
                    }
                }
            }

            return identifiedPrefabs;
        }
        
        public static List<(T, GameObject)> FindSceneObjectsOfType<T>()
        {
            List<(T, GameObject)> identifiedSceneObjects = new List<(T, GameObject)>();

            // Obter todos os tipos que implementam IIdentifiedGameObject
            List<Type> identifiedTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => typeof(T).IsAssignableFrom(type))
                .ToList();

            // Obter todos os GameObjects nas cenas carregadas
            List<GameObject> rootObjects = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) rootObjects.AddRange(scene.GetRootGameObjects());
            }

            foreach (GameObject rootObj in rootObjects)
            {
                MonoBehaviour[] components = rootObj.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour comp in components)
                {
                    if (comp != null && identifiedTypes.Contains(comp.GetType()))
                    {
                        if (comp is T castedComponent)
                        {
                            (T, GameObject) foundTuple = identifiedSceneObjects.Find(item => item.Item2 == rootObj);

                            if (foundTuple.Item2 == null)
                                identifiedSceneObjects.Add((castedComponent, rootObj));
                            break;
                        }
                    }
                }
            }

            return identifiedSceneObjects;
        }
        
        public static string GetScriptAssetPath(Type type)
        {
            string[] guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type) return path;
            }

            return null;
        }
#endif
    }
}
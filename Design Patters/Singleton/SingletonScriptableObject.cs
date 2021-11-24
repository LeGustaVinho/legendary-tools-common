using System.IO;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace LegendaryTools
{
    public class SingletonScriptableObject<T> : ScriptableObject
        where T : SingletonScriptableObject<T>, new()
    {
        private static string ASSET_FORMAT = ".asset";
        private static string ASSETS_FOLDER = "Assets/";
        private static string ASSET_TYPE_FINDER_TOKEN = "t:";

        private static SingletonScriptableObjectAttribute configAttribute;
        private static T instance;

        private static SingletonScriptableObjectAttribute ConfigAttribute =>
            configAttribute ?? (configAttribute =
                typeof(T).GetCustomAttribute<SingletonScriptableObjectAttribute>() ??
                new SingletonScriptableObjectAttribute());

        public static bool HasInstanceLoaded => instance != null;

        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    if (!ConfigAttribute.UseAsset)
                    {
                        instance = CreateInstance<T>();
                        instance.name = typeof(T).Name;
                    }
                    else
                    {
                        LoadOrCreateInstance();
                    }
                }

                return instance;
            }
        }

        public static void LoadOrCreateInstance()
        {
            if (ConfigAttribute.IsInResourcesFolder)
            {
                instance = Resources.Load<T>(ConfigAttribute.ResourcesPath + typeof(T).Name);

                if (instance == null)
                {
                    instance = CreateInstance();
                }
            }
#if UNITY_EDITOR
            else
            {
                string assetName = typeof(T).Name;
                instance = AssetDatabase.LoadAssetAtPath<T>(ConfigAttribute.AssetPath + assetName + ASSET_FORMAT);
                if (instance == null)
                {
                    instance = AssetDatabase.LoadAssetAtPath<T>(
                        ASSETS_FOLDER + ConfigAttribute.AssetPath + assetName + ASSET_FORMAT);
                }
            }

            if (instance == null)
            {
                string[] relocatedScriptableObject = AssetDatabase.FindAssets(ASSET_TYPE_FINDER_TOKEN + typeof(T).Name);
                if (relocatedScriptableObject.Length > 0)
                {
                    instance = AssetDatabase.LoadAssetAtPath<T>(
                        AssetDatabase.GUIDToAssetPath(relocatedScriptableObject[0]));
                }
            }
#endif
            if (instance == null)
            {
                instance = CreateInstance();
            }
        }

        public static T CreateInstance()
        {
            T inst = CreateInstance<T>();

#if UNITY_EDITOR
            if (!Directory.Exists(ConfigAttribute.AssetPathWithAssetsPrefix))
            {
                Directory.CreateDirectory(new DirectoryInfo(ConfigAttribute.AssetPathWithAssetsPrefix)
                    .FullName);
                AssetDatabase.Refresh();
            }

            string assetName = typeof(T).Name;
            string assetPath;
            if (ConfigAttribute.AssetPath.StartsWith(ASSETS_FOLDER))
            {
                assetPath = ConfigAttribute.AssetPath + assetName + ASSET_FORMAT;
            }
            else
            {
                assetPath = ASSETS_FOLDER + ConfigAttribute.AssetPath + assetName + ASSET_FORMAT;
            }

            AssetDatabase.CreateAsset(inst, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#endif
            return inst;
        }
    }
}
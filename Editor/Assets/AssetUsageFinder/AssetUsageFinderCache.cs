using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderCache
    {
        private static readonly JsonSerializerSettings CacheJsonSettings = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            TypeNameHandling = TypeNameHandling.None,
            PreserveReferencesHandling = PreserveReferencesHandling.None,
            Formatting = Formatting.None
        };

        public void Save(string fileAssetPath, List<AssetUsageFinderCachedUsage> usages)
        {
            string key = MakeKey(fileAssetPath);
            string json =
                JsonConvert.SerializeObject(usages ?? new List<AssetUsageFinderCachedUsage>(), CacheJsonSettings);
            EditorPrefs.SetString(key, json);
        }

        public List<AssetUsageFinderCachedUsage> Load(string fileAssetPath)
        {
            string key = MakeKey(fileAssetPath);
            if (!EditorPrefs.HasKey(key))
                return new List<AssetUsageFinderCachedUsage>();

            string json = EditorPrefs.GetString(key);
            if (string.IsNullOrEmpty(json))
                return new List<AssetUsageFinderCachedUsage>();

            return JsonConvert.DeserializeObject<List<AssetUsageFinderCachedUsage>>(json, CacheJsonSettings)
                   ?? new List<AssetUsageFinderCachedUsage>();
        }

        private static string MakeKey(string fileAssetPath)
        {
            return $"AssetUsageFinder_{fileAssetPath}";
        }
    }
}
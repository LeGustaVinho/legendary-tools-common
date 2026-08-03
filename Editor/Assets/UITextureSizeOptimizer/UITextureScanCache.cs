using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [Serializable]
    internal sealed class UITextureScanCacheFile
    {
        public int Version = UITextureScanCache.CurrentVersion;
        public List<UITextureScanCacheEntry> Entries = new();
    }

    [Serializable]
    internal sealed class UITextureScanCacheEntry
    {
        public string AssetPath;
        public string DependencyHash;
        public string ConfigurationKey;
        public List<UITextureUsageRecord> Usages = new();
    }

    internal sealed class UITextureScanCache
    {
        public const int CurrentVersion = 2;
        public static readonly string CachePath = Path.Combine(
            "Library",
            "LegendaryTools",
            "UITextureSizeOptimizer",
            "ScanCache.json").Replace('\\', '/');

        private readonly Dictionary<string, UITextureScanCacheEntry> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        private UITextureScanCache()
        {
        }

        public static UITextureScanCache Load()
        {
            UITextureScanCache cache = new();
            if (!File.Exists(CachePath))
            {
                return cache;
            }

            try
            {
                UITextureScanCacheFile file = JsonUtility.FromJson<UITextureScanCacheFile>(File.ReadAllText(CachePath));
                if (file == null || file.Version != CurrentVersion || file.Entries == null)
                {
                    return cache;
                }

                foreach (UITextureScanCacheEntry entry in file.Entries.Where(entry =>
                             entry != null && !string.IsNullOrEmpty(entry.AssetPath)))
                {
                    cache._entries[entry.AssetPath] = entry;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to read UI Texture Optimizer cache: {exception.Message}");
            }

            return cache;
        }

        public bool TryGet(
            string assetPath,
            string dependencyHash,
            string configurationKey,
            out IReadOnlyList<UITextureUsageRecord> usages)
        {
            usages = null;
            if (!_entries.TryGetValue(assetPath, out UITextureScanCacheEntry entry) ||
                !string.Equals(entry.DependencyHash, dependencyHash, StringComparison.Ordinal) ||
                !string.Equals(entry.ConfigurationKey, configurationKey, StringComparison.Ordinal) ||
                entry.Usages == null)
            {
                return false;
            }

            usages = entry.Usages;
            return true;
        }

        public void Put(
            string assetPath,
            string dependencyHash,
            string configurationKey,
            IEnumerable<UITextureUsageRecord> usages)
        {
            _entries[assetPath] = new UITextureScanCacheEntry
            {
                AssetPath = assetPath,
                DependencyHash = dependencyHash,
                ConfigurationKey = configurationKey,
                Usages = usages.ToList()
            };
        }

        public void Save(IReadOnlyCollection<string> existingAssetPaths, bool pruneMissingEntries)
        {
            if (pruneMissingEntries)
            {
                HashSet<string> existing = new(existingAssetPaths, StringComparer.OrdinalIgnoreCase);
                foreach (string removed in _entries.Keys.Where(path => !existing.Contains(path)).ToList())
                {
                    _entries.Remove(removed);
                }
            }

            try
            {
                string directory = Path.GetDirectoryName(CachePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                UITextureScanCacheFile file = new()
                {
                    Version = CurrentVersion,
                    Entries = _entries.Values.OrderBy(entry => entry.AssetPath, StringComparer.OrdinalIgnoreCase).ToList()
                };
                File.WriteAllText(CachePath, JsonUtility.ToJson(file, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to save UI Texture Optimizer cache: {exception.Message}");
            }
        }

        public static bool Clear()
        {
            if (!File.Exists(CachePath))
            {
                return false;
            }

            File.Delete(CachePath);
            return true;
        }
    }
}

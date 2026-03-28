using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal static class PrefabThumbnailCache
    {
        private const string CacheFolderName = "PrefabThumbnails";
        private const string CacheManifestFileName = "manifest.json";

        [Serializable]
        private sealed class Manifest
        {
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        private sealed class Entry
        {
            public string assetGuid;
            public string prefabHash;
        }

        private static readonly Dictionary<string, Entry> EntryByAssetGuid =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Texture2D> TextureByAssetGuid =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        private static bool _isLoaded;

        static PrefabThumbnailCache()
        {
            AssemblyReloadEvents.beforeAssemblyReload += DestroyCachedTextures;
            EditorApplication.quitting += DestroyCachedTextures;
        }

        public static bool IsUpToDate(string assetGuid, string prefabHash)
        {
            EnsureLoaded();

            Entry entry;
            if (!EntryByAssetGuid.TryGetValue(assetGuid, out entry))
            {
                return false;
            }

            return string.Equals(entry.prefabHash, prefabHash, StringComparison.Ordinal) &&
                   File.Exists(GetThumbnailPath(assetGuid));
        }

        public static bool TryGetThumbnail(string assetGuid, out Texture2D thumbnail)
        {
            EnsureLoaded();

            thumbnail = null;
            if (string.IsNullOrEmpty(assetGuid) || !EntryByAssetGuid.ContainsKey(assetGuid))
            {
                return false;
            }

            if (TextureByAssetGuid.TryGetValue(assetGuid, out thumbnail) && thumbnail != null)
            {
                return true;
            }

            string thumbnailPath = GetThumbnailPath(assetGuid);
            if (!File.Exists(thumbnailPath))
            {
                RemoveThumbnail(assetGuid);
                return false;
            }

            byte[] pngBytes;
            try
            {
                pngBytes = File.ReadAllBytes(thumbnailPath);
            }
            catch
            {
                RemoveThumbnail(assetGuid);
                return false;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                name = assetGuid + "_PrefabThumbnail",
                hideFlags = HideFlags.HideAndDontSave
            };

            if (!texture.LoadImage(pngBytes, false))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                RemoveThumbnail(assetGuid);
                return false;
            }

            TextureByAssetGuid[assetGuid] = texture;
            thumbnail = texture;
            return true;
        }

        public static string GetThumbnailPath(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid))
            {
                throw new ArgumentException("Asset GUID is required.", nameof(assetGuid));
            }

            return Path.Combine(GetCacheDirectory(), assetGuid + ".png");
        }

        public static void SetThumbnail(string assetGuid, string prefabHash)
        {
            EnsureLoaded();

            if (string.IsNullOrEmpty(assetGuid) || string.IsNullOrEmpty(prefabHash))
            {
                return;
            }

            Entry entry;
            if (!EntryByAssetGuid.TryGetValue(assetGuid, out entry))
            {
                entry = new Entry
                {
                    assetGuid = assetGuid,
                    prefabHash = prefabHash
                };

                EntryByAssetGuid.Add(assetGuid, entry);
            }
            else
            {
                entry.prefabHash = prefabHash;
            }

            InvalidateLoadedTexture(assetGuid);
            SaveManifest();
        }

        public static void RemoveThumbnail(string assetGuid)
        {
            EnsureLoaded();

            if (string.IsNullOrEmpty(assetGuid))
            {
                return;
            }

            EntryByAssetGuid.Remove(assetGuid);
            InvalidateLoadedTexture(assetGuid);

            string thumbnailPath = GetThumbnailPath(assetGuid);
            if (File.Exists(thumbnailPath))
            {
                File.Delete(thumbnailPath);
            }

            SaveManifest();
        }

        public static void PruneMissingEntries(ISet<string> validAssetGuids)
        {
            EnsureLoaded();

            if (validAssetGuids == null)
            {
                return;
            }

            bool changed = false;
            List<string> assetGuids = new List<string>(EntryByAssetGuid.Keys);
            for (int i = 0; i < assetGuids.Count; i++)
            {
                string assetGuid = assetGuids[i];
                if (validAssetGuids.Contains(assetGuid))
                {
                    continue;
                }

                EntryByAssetGuid.Remove(assetGuid);
                InvalidateLoadedTexture(assetGuid);

                string thumbnailPath = GetThumbnailPath(assetGuid);
                if (File.Exists(thumbnailPath))
                {
                    File.Delete(thumbnailPath);
                }

                changed = true;
            }

            if (changed)
            {
                SaveManifest();
            }
        }

        public static void ClearAll()
        {
            EnsureLoaded();

            EntryByAssetGuid.Clear();
            DestroyCachedTextures();

            string cacheDirectory = GetCacheDirectory();
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, true);
            }
        }

        private static void EnsureLoaded()
        {
            if (_isLoaded)
            {
                return;
            }

            _isLoaded = true;
            EntryByAssetGuid.Clear();

            string manifestPath = GetManifestPath();
            if (!File.Exists(manifestPath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(manifestPath);
                Manifest manifest = JsonUtility.FromJson<Manifest>(json);
                if (manifest == null || manifest.entries == null)
                {
                    return;
                }

                for (int i = 0; i < manifest.entries.Count; i++)
                {
                    Entry entry = manifest.entries[i];
                    if (entry == null || string.IsNullOrEmpty(entry.assetGuid) || string.IsNullOrEmpty(entry.prefabHash))
                    {
                        continue;
                    }

                    EntryByAssetGuid[entry.assetGuid] = entry;
                }
            }
            catch
            {
                EntryByAssetGuid.Clear();
            }
        }

        private static void SaveManifest()
        {
            string cacheDirectory = GetCacheDirectory();
            if (!Directory.Exists(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
            }

            Manifest manifest = new Manifest();
            foreach (Entry entry in EntryByAssetGuid.Values)
            {
                manifest.entries.Add(entry);
            }

            string json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(GetManifestPath(), json);
        }

        private static void InvalidateLoadedTexture(string assetGuid)
        {
            Texture2D cachedTexture;
            if (!TextureByAssetGuid.TryGetValue(assetGuid, out cachedTexture))
            {
                return;
            }

            if (cachedTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(cachedTexture);
            }

            TextureByAssetGuid.Remove(assetGuid);
        }

        private static void DestroyCachedTextures()
        {
            foreach (Texture2D texture in TextureByAssetGuid.Values)
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            TextureByAssetGuid.Clear();
        }

        private static string GetCacheDirectory()
        {
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectPath ?? string.Empty, "Library", "LegendaryTools", CacheFolderName);
        }

        private static string GetManifestPath()
        {
            return Path.Combine(GetCacheDirectory(), CacheManifestFileName);
        }
    }
}

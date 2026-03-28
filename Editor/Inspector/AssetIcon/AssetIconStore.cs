using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    [FilePath("ProjectSettings/LegendaryAssetThumbnailStore.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class AssetThumbnailStore : ScriptableSingleton<AssetThumbnailStore>,
        ISerializationCallbackReceiver
    {
        [Serializable]
        private sealed class Entry
        {
            public string assetGuid;
            public string thumbnailGlobalId;
        }

        [SerializeField] private List<Entry> _entries = new();

        private Dictionary<string, string> _thumbnailIdByAssetGuid = new();
        private Dictionary<string, Object> _thumbnailCacheByGlobalId = new();

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            RebuildCache();
        }

        public void RebuildCache()
        {
            _thumbnailIdByAssetGuid = new Dictionary<string, string>();
            _thumbnailCacheByGlobalId = new Dictionary<string, Object>();

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.assetGuid) ||
                    string.IsNullOrEmpty(entry.thumbnailGlobalId))
                {
                    _entries.RemoveAt(i);
                    continue;
                }

                _thumbnailIdByAssetGuid[entry.assetGuid] = entry.thumbnailGlobalId;
            }
        }

        public bool TryGetThumbnail(string assetGuid, out Object thumbnail)
        {
            EnsureCache();

            if (string.IsNullOrEmpty(assetGuid) || !_thumbnailIdByAssetGuid.TryGetValue(assetGuid, out string globalId))
            {
                thumbnail = null;
                return false;
            }

            if (_thumbnailCacheByGlobalId.TryGetValue(globalId, out thumbnail) && thumbnail != null) return true;

            if (!GlobalObjectId.TryParse(globalId, out GlobalObjectId parsedGlobalId))
            {
                thumbnail = null;
                return false;
            }

            thumbnail = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsedGlobalId);
            if (thumbnail == null) return false;

            _thumbnailCacheByGlobalId[globalId] = thumbnail;
            return true;
        }

        public bool HasThumbnail(string assetGuid)
        {
            EnsureCache();
            return !string.IsNullOrEmpty(assetGuid) && _thumbnailIdByAssetGuid.ContainsKey(assetGuid);
        }

        public string GetThumbnailId(string assetGuid)
        {
            EnsureCache();
            return !string.IsNullOrEmpty(assetGuid) &&
                   _thumbnailIdByAssetGuid.TryGetValue(assetGuid, out string globalId)
                ? globalId
                : string.Empty;
        }

        public bool SetThumbnail(string assetGuid, Object thumbnail)
        {
            EnsureCache();

            if (string.IsNullOrEmpty(assetGuid)) return false;

            if (thumbnail == null) return RemoveThumbnail(assetGuid);

            string globalId = GlobalObjectId.GetGlobalObjectIdSlow(thumbnail).ToString();

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (entry == null || entry.assetGuid != assetGuid) continue;

                if (entry.thumbnailGlobalId == globalId) return false;

                entry.thumbnailGlobalId = globalId;
                _thumbnailIdByAssetGuid[assetGuid] = globalId;
                _thumbnailCacheByGlobalId[globalId] = thumbnail;
                Save(true);
                return true;
            }

            _entries.Add(new Entry
            {
                assetGuid = assetGuid,
                thumbnailGlobalId = globalId
            });

            _thumbnailIdByAssetGuid[assetGuid] = globalId;
            _thumbnailCacheByGlobalId[globalId] = thumbnail;
            Save(true);
            return true;
        }

        public bool RemoveThumbnail(string assetGuid)
        {
            EnsureCache();

            if (string.IsNullOrEmpty(assetGuid)) return false;

            bool removed = false;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (entry == null || entry.assetGuid != assetGuid) continue;

                _entries.RemoveAt(i);
                removed = true;
            }

            if (!removed) return false;

            _thumbnailIdByAssetGuid.Remove(assetGuid);
            Save(true);
            return true;
        }

        private void EnsureCache()
        {
            if (_thumbnailIdByAssetGuid == null || _thumbnailCacheByGlobalId == null) RebuildCache();
        }
    }
}
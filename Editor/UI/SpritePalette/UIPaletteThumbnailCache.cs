using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal sealed class UIPaletteThumbnailCache
    {
        private readonly Dictionary<string, Texture2D> _cache = new(2048);

        public Texture2D GetOrCreate(string guid, Sprite sprite)
        {
            if (string.IsNullOrEmpty(guid) || sprite == null)
                return null;

            if (_cache.TryGetValue(guid, out Texture2D cached) && cached != null)
                return cached;

            Texture2D tex = UIPaletteUtilities.RenderSpriteToTexture(sprite);

            if (tex == null)
            {
                tex = AssetPreview.GetAssetPreview(sprite);
                if (tex == null)
                    tex = AssetPreview.GetMiniThumbnail(sprite) as Texture2D;
            }

            if (tex != null)
                _cache[guid] = tex;

            return tex;
        }

        public void Remove(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            if (_cache.TryGetValue(guid, out Texture2D tex))
            {
                DestroyIfOwned(tex);
                _cache.Remove(guid);
            }
        }

        public void ClearAll()
        {
            foreach (Texture2D tex in _cache.Values)
            {
                DestroyIfOwned(tex);
            }

            _cache.Clear();
        }

        public void AgeCache(UIPaletteState state, List<string> indexedGuids)
        {
            if (_cache.Count <= 4000)
                return;

            HashSet<string> keep = new(indexedGuids ?? Enumerable.Empty<string>());

            if (state != null)
            {
                foreach (string g in state.PaletteGuids)
                {
                    keep.Add(g);
                }

                foreach (string g in state.FavoriteGuids)
                {
                    keep.Add(g);
                }

                foreach (string g in state.RecentGuids)
                {
                    keep.Add(g);
                }
            }

            List<string> keys = _cache.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                if (!keep.Contains(k))
                    Remove(k);
            }
        }

        public void Dispose()
        {
            ClearAll();
        }

        private void DestroyIfOwned(Texture2D tex)
        {
            if (tex == null)
                return;

            if ((tex.hideFlags & HideFlags.HideAndDontSave) != 0)
                Object.DestroyImmediate(tex);
        }
    }
}
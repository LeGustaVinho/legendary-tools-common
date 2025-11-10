using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal static class CostAndSort
    {
        public static GroupCost CostForMaterial(Material key, List<RendererEntry> visible)
        {
            int rCount = visible.Count;
            int dc = rCount; // proxy
            long bytes = SumUniqueTextures(visible);
            return new GroupCost { RendererCount = rCount, PotentialDrawCalls = dc, TextureBytes = bytes };
        }

        public static GroupCost CostForShader(Shader key, List<RendererEntry> visible)
        {
            int rCount = visible.Count;
            int dc = rCount;
            long bytes = SumUniqueTextures(visible);
            return new GroupCost { RendererCount = rCount, PotentialDrawCalls = dc, TextureBytes = bytes };
        }

        public static GroupCost CostForTexture(Texture key, List<RendererEntry> visible)
        {
            int rCount = visible.Count;
            int dc = rCount;
            long bytes = TextureUtil.EstimateTextureMemory(key);
            return new GroupCost { RendererCount = rCount, PotentialDrawCalls = dc, TextureBytes = bytes };
        }

        public static List<GroupRow<T>> BuildSortedRows<T>(
            Dictionary<T, List<RendererEntry>> dict,
            System.Func<T, List<RendererEntry>, GroupCost> costFunc,
            System.Func<RendererEntry, bool> predicate,
            GroupSortKey sortKey,
            bool descending) where T : Object
        {
            List<GroupRow<T>> rows = new(dict.Count);

            foreach (KeyValuePair<T, List<RendererEntry>> kv in dict)
            {
                List<RendererEntry> visible = kv.Value.Where(predicate).ToList();
                if (visible.Count == 0) continue;

                GroupCost cost = costFunc(kv.Key, visible);
                rows.Add(new GroupRow<T> { Key = kv.Key, VisibleList = visible, Cost = cost });
            }

            IOrderedEnumerable<GroupRow<T>> ordered;
            switch (sortKey)
            {
                case GroupSortKey.DrawCalls:
                    ordered = descending
                        ? rows.OrderByDescending(r => r.Cost.PotentialDrawCalls)
                        : rows.OrderBy(r => r.Cost.PotentialDrawCalls);
                    break;

                case GroupSortKey.TextureMemory:
                    ordered = descending
                        ? rows.OrderByDescending(r => r.Cost.TextureBytes)
                        : rows.OrderBy(r => r.Cost.TextureBytes);
                    break;

                case GroupSortKey.Count:
                default:
                    ordered = descending
                        ? rows.OrderByDescending(r => r.Cost.RendererCount)
                        : rows.OrderBy(r => r.Cost.RendererCount);
                    break;
            }

            return ordered.ToList();
        }

        private static long SumUniqueTextures(List<RendererEntry> list)
        {
            HashSet<Texture> set = new();
            foreach (RendererEntry e in list)
            foreach (Texture t in e.Textures)
            {
                if (t != null) set.Add(t);
            }

            long total = 0;
            foreach (Texture t in set)
            {
                total += TextureUtil.EstimateTextureMemory(t);
            }

            return total;
        }
    }
}
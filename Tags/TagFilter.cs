using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LegendaryTools.TagSystem
{
    public enum TagFilterRuleType
    {
        Include,
        Exclude,
    }

    [Serializable]
    public class TagFilterMatch
    {
        public Tag Tag;
        public TagFilterRuleType Rule;

        public TagFilterMatch(Tag tag, TagFilterRuleType rule)
        {
            Tag = tag;
            Rule = rule;
        }

        /// <summary>
        /// Checks whether the item meets all the requirements of this filter
        /// </summary>
        /// <param name="item"></param>
        /// <returns>True meet all requirements False, dont meet requirements</returns>
        public bool Match(ITaggable item)
        {
            if (item.Tags == null || item.Tags.Length == 0)
            {
                return false;
            }

            switch (Rule)
            {
                case TagFilterRuleType.Include:
                {
                    if (!item.ContainsTag(Tag))
                    {
                        return false;
                    }
                    break;
                }
                case TagFilterRuleType.Exclude:
                {
                    if (item.ContainsTag(Tag))
                    {
                        return false;
                    }
                    break;
                }
            }

            return true;
        }
    }

    [CreateAssetMenu(menuName = "Tools/Tag System/TagFilter")]
    public class TagFilter : ScriptableObject
    {
        public string Name;
        public string Description;

        public List<TagFilterMatch> TagFilters = new List<TagFilterMatch>();

        /// <summary>
        /// Checks whether the item meets all the requirements of this filter
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Match(ITaggable item)
        {
            foreach (TagFilterMatch tagFilter in TagFilters)
            {
                if (!tagFilter.Match(item))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Filter a list of items from the selected filter settings
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="items"></param>
        /// <returns>Returns an array of filtered items</returns>
        public static List<T> Filter<T>(TagFilter filter, IEnumerable<T> items) where T : ITaggable
        {
            return Filter(filter.TagFilters, items);
        }

        /// <summary>
        /// Filter a list of items from the selected filter settings
        /// </summary>
        /// <param name="filters"></param>
        /// <param name="items"></param>
        /// <returns>Returns an array of filtered items</returns>
        public static List<T> Filter<T>(List<TagFilterMatch> filters, IEnumerable<T> items) where T : ITaggable
        {
            List<T> selected = new List<T>();
            foreach (T item in items)
            {
                bool shouldAdd = true;
                foreach (TagFilterMatch filter in filters)
                {
                    if (!filter.Match(item))
                    {
                        shouldAdd = false;
                        break;
                    }
                }

                if (shouldAdd)
                {
                    selected.Add(item);
                }
            }

            return selected;
        }
        
        public static List<T> NegativeFilterAdditive<T>(List<TagFilterMatch> filters, IEnumerable<T> items) where T : ITaggable
        {
            if (filters == null || filters.Count == 0) return items.ToList();
            
            List<T> selected = new List<T>();
            foreach (T item in items)
            {
                var shouldFilter = true;
                foreach (TagFilterMatch filter in filters)
                {
                    shouldFilter &= filter.Match(item);
                        
                    if (!shouldFilter) break;
                }

                if (!shouldFilter)
                {
                    selected.Add(item);
                }
            }

            return selected;
        }
    }
}
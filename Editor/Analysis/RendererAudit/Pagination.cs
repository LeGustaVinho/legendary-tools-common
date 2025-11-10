using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal sealed class Pagination
    {
        private readonly Dictionary<string, int> _pageByGroup = new();

        public int PageSize { get; private set; } = 50;

        public void SetPageSize(int value)
        {
            PageSize = Mathf.Clamp(value, 5, 500);
            _pageByGroup.Clear();
        }

        public void ResetAll()
        {
            _pageByGroup.Clear();
        }

        public int GetPage(string key)
        {
            if (!_pageByGroup.TryGetValue(key, out int page)) return 0;
            return page;
        }

        public void SetPage(string key, int page)
        {
            _pageByGroup[key] = Mathf.Max(0, page);
        }

        public static int GetTotalPages(int count, int pageSize)
        {
            return Mathf.Max(1, (count + pageSize - 1) / pageSize);
        }

        public (int start, int end, int page, int totalPages) GetRange(string key, int count)
        {
            int page = GetPage(key);
            int totalPages = GetTotalPages(count, PageSize);
            page = Mathf.Clamp(page, 0, totalPages - 1);

            int start = Mathf.Clamp(page * PageSize, 0, Mathf.Max(0, count - 1));
            int end = Mathf.Min(start + PageSize, count);
            return (start, end, page, totalPages);
        }
    }
}
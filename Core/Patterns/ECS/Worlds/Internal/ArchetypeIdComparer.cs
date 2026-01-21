using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    internal sealed class ArchetypeIdComparer : IComparer<ArchetypeId>
    {
        public static readonly ArchetypeIdComparer Instance = new();

        public int Compare(ArchetypeId x, ArchetypeId y)
        {
            int cmp = x.Value.CompareTo(y.Value);
            if (cmp != 0) return cmp;

            return x.Disambiguator.CompareTo(y.Disambiguator);
        }
    }
}
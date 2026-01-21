using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// Stable archetype enumeration in deterministic order:
    /// ArchetypeId ascending (Value, then Disambiguator).
    /// </summary>
    internal readonly struct ArchetypeEnumerable
    {
        private readonly SortedDictionary<ArchetypeId, Archetype> _dict;

        public ArchetypeEnumerable(SortedDictionary<ArchetypeId, Archetype> dict)
        {
            _dict = dict;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_dict);
        }

        public struct Enumerator
        {
            private SortedDictionary<ArchetypeId, Archetype>.Enumerator _enumerator;

            public Archetype Current { get; private set; }

            public Enumerator(SortedDictionary<ArchetypeId, Archetype> dict)
            {
                _enumerator = dict.GetEnumerator();
                Current = null;
            }

            public bool MoveNext()
            {
                if (_enumerator.MoveNext())
                {
                    Current = _enumerator.Current.Value;
                    return true;
                }

                Current = null;
                return false;
            }
        }
    }
}
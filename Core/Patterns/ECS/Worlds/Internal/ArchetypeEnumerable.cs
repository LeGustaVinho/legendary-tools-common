using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// Stable archetype enumeration wrapper (deterministic order via SortedDictionary buckets).
    /// </summary>
    internal readonly struct ArchetypeEnumerable
    {
        private readonly SortedDictionary<ulong, List<Archetype>> _dict;

        public ArchetypeEnumerable(SortedDictionary<ulong, List<Archetype>> dict)
        {
            _dict = dict;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_dict);
        }

        public struct Enumerator
        {
            private SortedDictionary<ulong, List<Archetype>>.Enumerator _dictEnumerator;
            private List<Archetype> _currentList;
            private int _listIndex;

            public Archetype Current { get; private set; }

            public Enumerator(SortedDictionary<ulong, List<Archetype>> dict)
            {
                _dictEnumerator = dict.GetEnumerator();
                _currentList = null;
                _listIndex = -1;
                Current = null;
            }

            public bool MoveNext()
            {
                if (_currentList != null)
                {
                    _listIndex++;
                    if (_listIndex < _currentList.Count)
                    {
                        Current = _currentList[_listIndex];
                        return true;
                    }

                    _currentList = null;
                    _listIndex = -1;
                }

                while (_dictEnumerator.MoveNext())
                {
                    List<Archetype> list = _dictEnumerator.Current.Value;
                    if (list == null || list.Count == 0) continue;

                    _currentList = list;
                    _listIndex = 0;
                    Current = _currentList[0];
                    return true;
                }

                Current = null;
                return false;
            }
        }
    }
}
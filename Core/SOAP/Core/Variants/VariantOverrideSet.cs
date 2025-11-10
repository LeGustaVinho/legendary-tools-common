using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Stores a set of Unity property paths that are overridden in the payload.
    /// </summary>
    [Serializable]
    public sealed class VariantOverrideSet
    {
        [SerializeField] private List<string> _paths = new();

        /// <summary>Determines whether the set contains the specified Unity property path.</summary>
        public bool Contains(string propertyPath)
        {
            return _paths.Contains(propertyPath);
        }

        /// <summary>Adds the specified Unity property path to the set if it does not exist.</summary>
        public bool Add(string propertyPath)
        {
            if (_paths.Contains(propertyPath))
                return false;

            _paths.Add(propertyPath);
            return true;
        }

        /// <summary>Removes the specified Unity property path from the set if present.</summary>
        public bool Remove(string propertyPath)
        {
            return _paths.Remove(propertyPath);
        }

        /// <summary>Enumerates all overridden paths.</summary>
        public IEnumerable<string> Enumerate()
        {
            return _paths;
        }

        /// <summary>Clears all overrides.</summary>
        public void Clear()
        {
            _paths.Clear();
        }
    }
}
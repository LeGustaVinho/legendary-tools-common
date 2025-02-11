using System.Collections.Generic;

namespace LegendaryTools
{
    /// <summary>
    /// A generic many-to-many mapping between two types.
    /// For example, this could map Students to Courses where each student can be enrolled in many courses,
    /// and each course can have many students.
    /// </summary>
    public class ManyToManyMap<TLeft, TRight>
    {
        // Maps each left key to a set of right keys.
        private readonly Dictionary<TLeft, HashSet<TRight>> leftToRights;

        // Maps each right key to a set of left keys.
        private readonly Dictionary<TRight, HashSet<TLeft>> rightToLefts;

        public ManyToManyMap()
        {
            leftToRights = new Dictionary<TLeft, HashSet<TRight>>();
            rightToLefts = new Dictionary<TRight, HashSet<TLeft>>();
        }

        /// <summary>
        /// Gets all left keys.
        /// </summary>
        public IEnumerable<TLeft> Lefts => leftToRights.Keys;

        /// <summary>
        /// Gets all right keys.
        /// </summary>
        public IEnumerable<TRight> Rights => rightToLefts.Keys;

        /// <summary>
        /// Adds a left key to the structure if it doesn't already exist.
        /// </summary>
        public void AddLeft(TLeft left)
        {
            if (!leftToRights.ContainsKey(left))
            {
                leftToRights.Add(left, new HashSet<TRight>());
            }
        }

        /// <summary>
        /// Adds a right key to the structure if it doesn't already exist.
        /// </summary>
        public void AddRight(TRight right)
        {
            if (!rightToLefts.ContainsKey(right))
            {
                rightToLefts.Add(right, new HashSet<TLeft>());
            }
        }

        /// <summary>
        /// Establishes a relationship between the given left and right keys.
        /// If the keys do not already exist, they are automatically added.
        /// </summary>
        public void Add(TLeft left, TRight right)
        {
            // Ensure the left key exists.
            if (!leftToRights.TryGetValue(left, out var rights))
            {
                rights = new HashSet<TRight>();
                leftToRights.Add(left, rights);
            }
            // Ensure the right key exists.
            if (!rightToLefts.TryGetValue(right, out var lefts))
            {
                lefts = new HashSet<TLeft>();
                rightToLefts.Add(right, lefts);
            }
            // Add the relationship in both directions.
            rights.Add(right);
            lefts.Add(left);
        }

        /// <summary>
        /// Removes the relationship between the given left and right keys.
        /// If the relationship is the only one for a key, that key is removed from the mapping.
        /// </summary>
        /// <returns>True if the relationship existed and was removed; otherwise, false.</returns>
        public bool Remove(TLeft left, TRight right)
        {
            bool removed = false;
            if (leftToRights.TryGetValue(left, out var rights))
            {
                removed = rights.Remove(right);
                if (rights.Count == 0)
                {
                    leftToRights.Remove(left);
                }
            }
            if (rightToLefts.TryGetValue(right, out var lefts))
            {
                lefts.Remove(left);
                if (lefts.Count == 0)
                {
                    rightToLefts.Remove(right);
                }
            }
            return removed;
        }

        /// <summary>
        /// Checks whether a relationship exists between the specified left and right keys.
        /// </summary>
        public bool Contains(TLeft left, TRight right)
        {
            return leftToRights.TryGetValue(left, out var rights) && rights.Contains(right);
        }

        /// <summary>
        /// Retrieves all the right keys related to the given left key.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Thrown if the left key is not found.</exception>
        public IEnumerable<TRight> GetRightsForLeft(TLeft left)
        {
            if (leftToRights.TryGetValue(left, out var rights))
            {
                return rights;
            }
            throw new KeyNotFoundException("Left key not found in the map.");
        }

        /// <summary>
        /// Retrieves all the left keys related to the given right key.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Thrown if the right key is not found.</exception>
        public IEnumerable<TLeft> GetLeftsForRight(TRight right)
        {
            if (rightToLefts.TryGetValue(right, out var lefts))
            {
                return lefts;
            }
            throw new KeyNotFoundException("Right key not found in the map.");
        }

        /// <summary>
        /// Attempts to get the set of right keys for the given left key.
        /// </summary>
        public bool TryGetRightsForLeft(TLeft left, out HashSet<TRight> rights)
        {
            return leftToRights.TryGetValue(left, out rights);
        }

        /// <summary>
        /// Attempts to get the set of left keys for the given right key.
        /// </summary>
        public bool TryGetLeftsForRight(TRight right, out HashSet<TLeft> lefts)
        {
            return rightToLefts.TryGetValue(right, out lefts);
        }

        /// <summary>
        /// Removes the left key and all its associated relationships.
        /// </summary>
        /// <returns>True if the left key existed and was removed; otherwise, false.</returns>
        public bool RemoveLeft(TLeft left)
        {
            if (!leftToRights.TryGetValue(left, out var rights))
                return false;

            // Remove this left key from every related right key.
            foreach (var right in rights)
            {
                if (rightToLefts.TryGetValue(right, out var lefts))
                {
                    lefts.Remove(left);
                    if (lefts.Count == 0)
                        rightToLefts.Remove(right);
                }
            }
            leftToRights.Remove(left);
            return true;
        }

        /// <summary>
        /// Removes the right key and all its associated relationships.
        /// </summary>
        /// <returns>True if the right key existed and was removed; otherwise, false.</returns>
        public bool RemoveRight(TRight right)
        {
            if (!rightToLefts.TryGetValue(right, out var lefts))
                return false;

            // Remove this right key from every related left key.
            foreach (var left in lefts)
            {
                if (leftToRights.TryGetValue(left, out var rights))
                {
                    rights.Remove(right);
                    if (rights.Count == 0)
                        leftToRights.Remove(left);
                }
            }
            rightToLefts.Remove(right);
            return true;
        }
    }
}

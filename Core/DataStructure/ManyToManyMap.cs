using System;
using System.Collections.Generic;
using System.Linq;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
#endif

namespace LegendaryTools
{
    /// <summary>
    ///     A generic many-to-many mapping between two types.
    ///     This implementation supports batch operations, full pair enumeration,
    ///     relationship counting, predicate-based queries, and notifications via Action delegates.
    ///     It also provides clear/reset functionality.
    /// </summary>
    public class ManyToManyMap<TLeft, TRight>
    {
        // Internal storage: each left key maps to a set of right keys,
        // and each right key maps to a set of left keys.
#if ODIN_INSPECTOR
        [ShowInInspector] [OnCollectionChanged("OnLeftToRightsChanged")]
#endif
        private readonly Dictionary<TLeft, HashSet<TRight>> leftToRights = new Dictionary<TLeft, HashSet<TRight>>();
#if ODIN_INSPECTOR
        [ShowInInspector] [OnCollectionChanged("OnRightToLeftsChanged")]
#endif
        private readonly Dictionary<TRight, HashSet<TLeft>> rightToLefts = new Dictionary<TRight, HashSet<TLeft>>();

#if ODIN_INSPECTOR
        private bool isSyncing;
#endif

        #region Notification Actions

        /// <summary>
        ///     Invoked when a new (left, right) relationship is added.
        /// </summary>
        public event Action<(TLeft left, TRight right)> RelationshipAdded;

        /// <summary>
        ///     Invoked when a (left, right) relationship is removed.
        /// </summary>
        public event Action<(TLeft left, TRight right)> RelationshipRemoved;

        /// <summary>
        ///     Invoked when a new left key is added.
        /// </summary>
        public event Action<TLeft> LeftAdded;

        /// <summary>
        ///     Invoked when a left key is removed.
        /// </summary>
        public event Action<TLeft> LeftRemoved;

        /// <summary>
        ///     Invoked when a new right key is added.
        /// </summary>
        public event Action<TRight> RightAdded;

        /// <summary>
        ///     Invoked when a right key is removed.
        /// </summary>
        public event Action<TRight> RightRemoved;

        #endregion

        #region Properties

        /// <summary>
        ///     Gets all left keys.
        /// </summary>
        public IEnumerable<TLeft> Lefts => leftToRights.Keys;

        /// <summary>
        ///     Gets all right keys.
        /// </summary>
        public IEnumerable<TRight> Rights => rightToLefts.Keys;

        /// <summary>
        ///     Enumerates all (left, right) relationships.
        /// </summary>
        public IEnumerable<(TLeft left, TRight right)> Relationships
        {
            get
            {
                foreach (KeyValuePair<TLeft, HashSet<TRight>> leftKvp in leftToRights)
                {
                    TLeft left = leftKvp.Key;
                    foreach (TRight right in leftKvp.Value)
                    {
                        yield return (left, right);
                    }
                }
            }
        }

        /// <summary>
        ///     Gets the total number of relationships.
        /// </summary>
        public int RelationshipCount => leftToRights.Values.Sum(set => set.Count);

        #endregion

        #region Basic Operations

        /// <summary>
        ///     Adds a relationship between a left key and a right key.
        ///     If a left or right key is new, it is automatically added.
        /// </summary>
        public void Add(TLeft left, TRight right)
        {
            // Add left if not present.
            if (!leftToRights.TryGetValue(left, out HashSet<TRight> rights))
            {
                rights = new HashSet<TRight>();
                leftToRights[left] = rights;
                LeftAdded?.Invoke(left);
            }

            // Add right if not present.
            if (!rightToLefts.TryGetValue(right, out HashSet<TLeft> lefts))
            {
                lefts = new HashSet<TLeft>();
                rightToLefts[right] = lefts;
                RightAdded?.Invoke(right);
            }

            // Add the relationship if it doesn't already exist.
            if (rights.Add(right))
            {
                lefts.Add(left);
                RelationshipAdded?.Invoke((left, right));
            }
        }

        /// <summary>
        ///     Removes the relationship between the given left and right keys.
        /// </summary>
        /// <returns>True if the relationship existed and was removed; otherwise, false.</returns>
        public bool Remove(TLeft left, TRight right)
        {
            bool removed = false;
            if (leftToRights.TryGetValue(left, out HashSet<TRight> rights) && rights.Remove(right))
            {
                removed = true;
                // Remove the corresponding left from the right's set.
                if (rightToLefts.TryGetValue(right, out HashSet<TLeft> lefts))
                {
                    lefts.Remove(left);
                    if (lefts.Count == 0)
                    {
                        rightToLefts.Remove(right);
                        RightRemoved?.Invoke(right);
                    }
                }

                // If the left key has no more relationships, remove it.
                if (rights.Count == 0)
                {
                    leftToRights.Remove(left);
                    LeftRemoved?.Invoke(left);
                }

                RelationshipRemoved?.Invoke((left, right));
            }

            return removed;
        }

        #endregion

        #region Batch Operations

        /// <summary>
        ///     Adds multiple relationships for a given left key.
        /// </summary>
        public void AddRange(TLeft left, IEnumerable<TRight> rights)
        {
            if (rights == null)
            {
                throw new ArgumentNullException(nameof(rights));
            }

            foreach (TRight right in rights)
            {
                Add(left, right);
            }
        }

        /// <summary>
        ///     Adds multiple (left, right) pairs.
        /// </summary>
        public void AddRange(IEnumerable<(TLeft left, TRight right)> pairs)
        {
            if (pairs == null)
            {
                throw new ArgumentNullException(nameof(pairs));
            }

            foreach ((TLeft left, TRight right) pair in pairs)
            {
                Add(pair.left, pair.right);
            }
        }

        /// <summary>
        ///     Removes multiple relationships for a given left key.
        /// </summary>
        public void RemoveRange(TLeft left, IEnumerable<TRight> rights)
        {
            if (rights == null)
            {
                throw new ArgumentNullException(nameof(rights));
            }

            // Create a list to avoid modifying the collection during iteration.
            foreach (TRight right in rights.ToList())
            {
                Remove(left, right);
            }
        }

        /// <summary>
        ///     Removes multiple (left, right) pairs.
        /// </summary>
        public void RemoveRange(IEnumerable<(TLeft left, TRight right)> pairs)
        {
            if (pairs == null)
            {
                throw new ArgumentNullException(nameof(pairs));
            }

            foreach ((TLeft left, TRight right) pair in pairs.ToList())
            {
                Remove(pair.left, pair.right);
            }
        }

        #endregion

        #region Predicate-Based Queries

        /// <summary>
        ///     Finds all (left, right) relationships that satisfy the given predicate.
        /// </summary>
        public IEnumerable<(TLeft left, TRight right)> FindPairs(Func<TLeft, TRight, bool> predicate)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            foreach (KeyValuePair<TLeft, HashSet<TRight>> leftKvp in leftToRights)
            {
                TLeft left = leftKvp.Key;
                foreach (TRight right in leftKvp.Value)
                {
                    if (predicate(left, right))
                    {
                        yield return (left, right);
                    }
                }
            }
        }

        #endregion

        #region Clear and Reset Operations

        /// <summary>
        ///     Clears all relationships and resets the mapping.
        ///     Invokes notifications for each removed relationship and key.
        /// </summary>
        public void Clear()
        {
            // Notify removal for each relationship.
            foreach ((TLeft left, TRight right) pair in Relationships.ToList())
            {
                RelationshipRemoved?.Invoke(pair);
            }

            // Notify removal for each left key.
            foreach (TLeft left in leftToRights.Keys.ToList())
            {
                LeftRemoved?.Invoke(left);
            }

            // Notify removal for each right key.
            foreach (TRight right in rightToLefts.Keys.ToList())
            {
                RightRemoved?.Invoke(right);
            }

            leftToRights.Clear();
            rightToLefts.Clear();
        }

        /// <summary>
        ///     Resets the mapping (alias for Clear).
        /// </summary>
        public void Reset()
        {
            Clear();
        }

        #endregion

#if ODIN_INSPECTOR

        #region Odin Inspector Synchronization

        /// <summary>
        ///     Called by Odin Inspector when the leftToRights dictionary changes.
        ///     Synchronizes the rightToLefts dictionary.
        /// </summary>
        private void OnLeftToRightsChanged(CollectionChangeInfo changeInfo)
        {
            if (isSyncing) return;
            isSyncing = true;
            rightToLefts.Clear();
            foreach (KeyValuePair<TLeft, HashSet<TRight>> kvp in leftToRights)
            {
                TLeft left = kvp.Key;
                foreach (TRight right in kvp.Value)
                {
                    if (!rightToLefts.TryGetValue(right, out HashSet<TLeft> leftSet))
                    {
                        leftSet = new HashSet<TLeft>();
                        rightToLefts[right] = leftSet;
                    }

                    leftSet.Add(left);
                }
            }

            isSyncing = false;
        }

        /// <summary>
        ///     Called by Odin Inspector when the rightToLefts dictionary changes.
        ///     Synchronizes the leftToRights dictionary.
        /// </summary>
        private void OnRightToLeftsChanged(CollectionChangeInfo changeInfo)
        {
            if (isSyncing) return;
            isSyncing = true;
            leftToRights.Clear();
            foreach (KeyValuePair<TRight, HashSet<TLeft>> kvp in rightToLefts)
            {
                TRight right = kvp.Key;
                foreach (TLeft left in kvp.Value)
                {
                    if (!leftToRights.TryGetValue(left, out HashSet<TRight> rightSet))
                    {
                        rightSet = new HashSet<TRight>();
                        leftToRights[left] = rightSet;
                    }

                    rightSet.Add(right);
                }
            }

            isSyncing = false;
        }

        #endregion

#endif
    }
}
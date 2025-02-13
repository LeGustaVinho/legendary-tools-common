using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace LegendaryTools
{
    /// <summary>
    ///     Represents a bidirectional one-to-one mapping between Left and Right values.
    /// </summary>
    public class OneToOneMap<TLeft, TRight>
    {
#if ODIN_INSPECTOR
        [ShowInInspector] [OnCollectionChanged("OnLeftToRightChanged")]
#endif
        private Dictionary<TLeft, TRight> leftToRight = new Dictionary<TLeft, TRight>();

#if ODIN_INSPECTOR
        [ShowInInspector] [OnCollectionChanged("OnRightToLeftChanged")]
#endif
        private Dictionary<TRight, TLeft> rightToLeft = new Dictionary<TRight, TLeft>();

        // This flag prevents recursive synchronization when one dictionary is updated as a result of changes in the other.
        private bool isSyncing;

        /// <summary>
        ///     Occurs when a new pair is added.
        /// </summary>
        public event Action<TLeft, TRight> Added;

        /// <summary>
        ///     Occurs when an existing pair is removed.
        /// </summary>
        public event Action<TLeft, TRight> Removed;

        #region Basic Operations

        /// <summary>
        ///     Adds a new one-to-one mapping between left and right.
        /// </summary>
        public void Add(TLeft left, TRight right)
        {
            if (leftToRight.ContainsKey(left))
            {
                throw new ArgumentException("The left value is already mapped.");
            }

            if (rightToLeft.ContainsKey(right))
            {
                throw new ArgumentException("The right value is already mapped.");
            }

            leftToRight.Add(left, right);
            rightToLeft.Add(right, left);

            Added?.Invoke(left, right);
        }

        /// <summary>
        ///     Removes the mapping associated with the specified left value.
        /// </summary>
        public bool RemoveByLeft(TLeft left)
        {
            if (leftToRight.TryGetValue(left, out TRight right))
            {
                leftToRight.Remove(left);
                rightToLeft.Remove(right);

                Removed?.Invoke(left, right);
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Removes the mapping associated with the specified right value.
        /// </summary>
        public bool RemoveByRight(TRight right)
        {
            if (rightToLeft.TryGetValue(right, out TLeft left))
            {
                rightToLeft.Remove(right);
                leftToRight.Remove(left);

                Removed?.Invoke(left, right);
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Clears all mappings.
        /// </summary>
        public void Clear()
        {
            leftToRight.Clear();
            rightToLeft.Clear();
            // Note: Events are not fired for each removal. Modify if needed.
        }

        /// <summary>
        ///     Gets the number of mappings.
        /// </summary>
        public int Count => leftToRight.Count;

        /// <summary>
        ///     Determines whether the specified left value exists.
        /// </summary>
        public bool ContainsLeft(TLeft left)
        {
            return leftToRight.ContainsKey(left);
        }

        /// <summary>
        ///     Determines whether the specified right value exists.
        /// </summary>
        public bool ContainsRight(TRight right)
        {
            return rightToLeft.ContainsKey(right);
        }

        #endregion

        #region Indexers

        /// <summary>
        ///     Gets or sets the right value associated with the specified left value.
        /// </summary>
        public TRight this[TLeft left]
        {
            get => leftToRight[left];
            set
            {
                if (leftToRight.ContainsKey(left))
                {
                    // Update existing mapping.
                    TRight oldRight = leftToRight[left];
                    if (!EqualityComparer<TRight>.Default.Equals(oldRight, value))
                    {
                        // Ensure the new right is not already mapped.
                        if (rightToLeft.ContainsKey(value))
                        {
                            throw new ArgumentException("The new right value is already mapped to another left value.");
                        }

                        leftToRight[left] = value;
                        rightToLeft.Remove(oldRight);
                        rightToLeft[value] = left;

                        // Fire events for the change.
                        Removed?.Invoke(left, oldRight);
                        Added?.Invoke(left, value);
                    }
                }
                else
                {
                    // Add new mapping.
                    if (rightToLeft.ContainsKey(value))
                    {
                        throw new ArgumentException("The right value is already mapped to another left value.");
                    }

                    leftToRight[left] = value;
                    rightToLeft[value] = left;
                    Added?.Invoke(left, value);
                }
            }
        }

        /// <summary>
        ///     Gets or sets the left value associated with the specified right value.
        /// </summary>
        public TLeft this[TRight right]
        {
            get => rightToLeft[right];
            set
            {
                if (rightToLeft.ContainsKey(right))
                {
                    // Update existing mapping.
                    TLeft oldLeft = rightToLeft[right];
                    if (!EqualityComparer<TLeft>.Default.Equals(oldLeft, value))
                    {
                        // Ensure the new left is not already mapped.
                        if (leftToRight.ContainsKey(value))
                        {
                            throw new ArgumentException("The new left value is already mapped to another right value.");
                        }

                        rightToLeft[right] = value;
                        leftToRight.Remove(oldLeft);
                        leftToRight[value] = right;

                        Removed?.Invoke(oldLeft, right);
                        Added?.Invoke(value, right);
                    }
                }
                else
                {
                    // Add new mapping.
                    if (leftToRight.ContainsKey(value))
                    {
                        throw new ArgumentException("The left value is already mapped to another right value.");
                    }

                    rightToLeft[right] = value;
                    leftToRight[value] = right;
                    Added?.Invoke(value, right);
                }
            }
        }

        #endregion

        #region Enumerations

        /// <summary>
        ///     Gets all the left values in the mapping.
        /// </summary>
        public IEnumerable<TLeft> LeftValues => leftToRight.Keys;

        /// <summary>
        ///     Gets all the right values in the mapping.
        /// </summary>
        public IEnumerable<TRight> RightValues => rightToLeft.Keys;

        #endregion

        #region Odin Inspector Synchronization

#if UNITY_EDITOR && ODIN_INSPECTOR
        /// <summary>
        ///     Called by Odin Inspector when the left-to-right dictionary changes.
        ///     Synchronizes the right-to-left dictionary.
        /// </summary>
        /// <param name="changeInfo">Information about the change.</param>
        private void OnLeftToRightChanged(CollectionChangeInfo changeInfo)
        {
            if (isSyncing) return;
            isSyncing = true;

            // Rebuild the right-to-left dictionary from _leftToRight.
            rightToLeft.Clear();
            foreach (KeyValuePair<TLeft, TRight> kvp in leftToRight)
            {
                // Since this is a one-to-one map, we assume unique keys and values.
                rightToLeft[kvp.Value] = kvp.Key;
            }

            isSyncing = false;
        }

        /// <summary>
        ///     Called by Odin Inspector when the right-to-left dictionary changes.
        ///     Synchronizes the left-to-right dictionary.
        /// </summary>
        /// <param name="changeInfo">Information about the change.</param>
        private void OnRightToLeftChanged(CollectionChangeInfo changeInfo)
        {
            if (isSyncing) return;
            isSyncing = true;

            // Rebuild the left-to-right dictionary from _rightToLeft.
            leftToRight.Clear();
            foreach (KeyValuePair<TRight, TLeft> kvp in rightToLeft)
            {
                leftToRight[kvp.Value] = kvp.Key;
            }

            isSyncing = false;
        }
#endif

        #endregion
    }
}
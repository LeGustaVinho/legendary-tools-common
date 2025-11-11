using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    /// <summary>
    /// List with change notifications including the affected index.
    /// </summary>
    [Serializable]
    public class ObservableList<T> : IList<T>
    {
        public T this[int index]
        {
            get => collection[index];
            set
            {
                T oldValue = collection[index];
                collection[index] = value;
                OnUpdate?.Invoke(this, oldValue, value, index);
            }
        }

        public int Count => collection.Count;
        public bool IsReadOnly => ((IList<T>)collection).IsReadOnly;

        /// <summary>
        /// Raised when an item is added at an index. Args: (list, item, index).
        /// </summary>
        public event Action<ObservableList<T>, T, int> OnAdd;

        /// <summary>
        /// Raised when an item is updated in place. Args: (list, oldItem, newItem, index).
        /// </summary>
        public event Action<ObservableList<T>, T, T, int> OnUpdate;

        /// <summary>
        /// Raised when an item is removed from an index. Args: (list, removedItem, index).
        /// </summary>
        public event Action<ObservableList<T>, T, int> OnRemove;

        /// <summary>
        /// Raised when the list is cleared. Args: (list).
        /// </summary>
        public event Action<ObservableList<T>> OnClear;

        [SerializeField] private readonly List<T> collection = new();

        public int IndexOf(T item)
        {
            return collection.IndexOf(item);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return collection.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(T item)
        {
            collection.Add(item);
            OnAdd?.Invoke(this, item, collection.Count - 1);
        }

        public void Clear()
        {
            collection.Clear();
            OnClear?.Invoke(this);
        }

        public bool Contains(T item)
        {
            return collection.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            collection.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            int idx = collection.IndexOf(item);
            if (idx < 0) return false;
            T removed = collection[idx];
            collection.RemoveAt(idx);
            OnRemove?.Invoke(this, removed, idx);
            return true;
        }

        public void Insert(int index, T item)
        {
            collection.Insert(index, item);
            OnAdd?.Invoke(this, item, index);
        }

        public void RemoveAt(int index)
        {
            T removed = collection[index];
            collection.RemoveAt(index);
            OnRemove?.Invoke(this, removed, index);
        }
    }
}
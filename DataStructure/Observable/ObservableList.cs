using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
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
                OnUpdate?.Invoke(this, oldValue, value);
            }
        }

        public int Count => collection.Count;
        public bool IsReadOnly => (collection as IList<T>).IsReadOnly;
        
        public event Action<ObservableList<T>, T> OnAdd;
        public event Action<ObservableList<T>, T, T> OnUpdate;
        public event Action<ObservableList<T>, T> OnRemove;
        public event Action<ObservableList<T>> OnClear;
        
        [SerializeField] private List<T> collection = new List<T>();
        
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
            OnAdd?.Invoke(this, item);
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
            bool result = collection.Remove(item);
            OnRemove?.Invoke(this, item);
            return result;
        }

        public void Insert(int index, T item)
        {
            collection.Insert(index, item);
            OnAdd?.Invoke(this, item);
        }

        public void RemoveAt(int index)
        {
            T removed = collection[index];
            collection.RemoveAt(index);
            OnRemove?.Invoke(this, removed);
        }
    }
}
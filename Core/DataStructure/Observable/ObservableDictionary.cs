using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools
{
    [Serializable]
    public class ObservableDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        public TValue this[TKey key]
        {
            get => dictionary[key];
            set
            {
                TValue oldValue = dictionary[key];
                dictionary[key] = value;
                OnUpdate?.Invoke(this, key, oldValue, value);
            }
        }

        public ICollection<TKey> Keys => dictionary.Keys;
        public ICollection<TValue> Values => dictionary.Values;
        public int Count => dictionary.Count;
        public bool IsReadOnly => (dictionary as IDictionary<TKey, TValue>).IsReadOnly;
        public event Action<ObservableDictionary<TKey, TValue>, TKey, TValue> OnAdd;
        public event Action<ObservableDictionary<TKey, TValue>, TKey, TValue, TValue> OnUpdate;
        public event Action<ObservableDictionary<TKey, TValue>, TKey, TValue> OnRemove;
        public event Action<ObservableDictionary<TKey, TValue>> OnClear;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        private Dictionary<TKey, TValue> dictionary = new();

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return dictionary.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            (dictionary as IDictionary<TKey, TValue>).Add(item);
            OnAdd?.Invoke(this, item.Key, item.Value);
        }

        public void Clear()
        {
            if (dictionary.Count == 0) return;
            dictionary.Clear();
            OnClear?.Invoke(this);
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return (dictionary as IDictionary<TKey, TValue>).Contains(item);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            (dictionary as IDictionary<TKey, TValue>).CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (dictionary.TryGetValue(item.Key, out TValue existing) &&
                EqualityComparer<TValue>.Default.Equals(existing, item.Value) &&
                (dictionary as IDictionary<TKey, TValue>).Remove(item))
            {
                OnRemove?.Invoke(this, item.Key, existing);
                return true;
            }

            return false;
        }

        public void Add(TKey key, TValue value)
        {
            dictionary.Add(key, value);
            OnAdd?.Invoke(this, key, value);
        }

        public bool ContainsKey(TKey key)
        {
            return dictionary.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            if (dictionary.TryGetValue(key, out TValue removed) && dictionary.Remove(key))
            {
                OnRemove?.Invoke(this, key, removed);
                return true;
            }

            return false;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return dictionary.TryGetValue(key, out value);
        }
    }
}
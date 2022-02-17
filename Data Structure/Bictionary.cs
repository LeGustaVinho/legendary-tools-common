using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools
{
    public class Bictionary<TKey,TValue> : IDictionary<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> forward;
        private readonly Dictionary<TValue, TKey> backward;

        public int Count => forward.Count;
        public bool IsReadOnly => (forward as IDictionary<TKey, TValue>).IsReadOnly;

        public ICollection<TKey> Keys => forward.Keys;
        public ICollection<TValue> Values => forward.Values;

        public Bictionary()
        {
            forward = new Dictionary<TKey, TValue>();
            backward = new Dictionary<TValue, TKey>();
        }
        
        public Bictionary(int capacity)
        {
            forward = new Dictionary<TKey, TValue>(capacity);
            backward = new Dictionary<TValue, TKey>(capacity);
        }

        public TValue this[TKey key]
        {
            get => forward[key];
            set => forward[key] = value;
        }
        
        public TKey this[TValue key]
        {
            get => backward[key];
            set => backward[key] = value;
        }
        
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return forward.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            (forward as IDictionary<TKey, TValue>).Add(item);
            (backward as IDictionary<TValue, TKey>).Add(new KeyValuePair<TValue, TKey>(item.Value, item.Key));
        }

        public void Clear()
        {
            forward.Clear();
            backward.Clear();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return (forward as IDictionary<TKey, TValue>).Contains(item);
        }
        
        public bool Contains(KeyValuePair<TValue, TKey> item)
        {
            return (backward as IDictionary<TValue,TKey>).Contains(item);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            (forward as IDictionary<TKey, TValue>).CopyTo(array, arrayIndex);
        }
        
        public void CopyTo(KeyValuePair<TValue, TKey>[] array, int arrayIndex)
        {
            (backward as IDictionary<TValue, TKey>).CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            (backward as IDictionary<TValue, TKey>).Remove(new KeyValuePair<TValue, TKey>(item.Value, item.Key));
            return (forward as IDictionary<TKey, TValue>).Remove(item);
        }

        public void Add(TKey key, TValue value)
        {
            forward.Add(key, value);
            backward.Add(value, key);
        }

        public bool ContainsKey(TKey key)
        {
            return forward.ContainsKey(key);
        }
        
        public bool ContainsKey(TValue key)
        {
            return backward.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            backward.Remove(forward[key]);
            return forward.Remove(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return forward.TryGetValue(key, out value);
        }
        
        public bool TryGetValue(TValue key, out TKey value)
        {
            return backward.TryGetValue(key, out value);
        }
    }
}
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    [Serializable]
    public class SerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, 
        IDictionary, 
        IDictionary<TKey, TValue>, 
        ISerializationCallbackReceiver
    {
        [SerializeField]
        private List<TKey> keys = new List<TKey>();

        [SerializeField]
        private List<TValue> values = new List<TValue>();

        // Constructor
        public SerializableDictionary() : base() { }

        // Synchronize serialized lists before serialization
        public void OnBeforeSerialize()
        {
            keys.Clear();
            values.Clear();
            foreach (var kvp in this)
            {
                keys.Add(kvp.Key);
                values.Add(kvp.Value);
            }
        }

        // Rebuild dictionary from serialized lists after deserialization
        public void OnAfterDeserialize()
        {
            Clear();
            var seenKeys = new HashSet<TKey>();
            for (int i = 0; i < Mathf.Min(keys.Count, values.Count); i++)
            {
                if (keys[i] != null && seenKeys.Add(keys[i]))
                {
                    try
                    {
                        Add(keys[i], values[i]);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to add key {keys[i]} during deserialization: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"Duplicate or null key {keys[i]} ignored during deserialization.");
                }
            }
        }

        // Override Add methods to synchronize serialized lists
        public new void Add(TKey key, TValue value)
        {
            base.Add(key, value);
            keys.Add(key);
            values.Add(value);
        }

        void IDictionary.Add(object key, object value)
        {
            if (key is TKey tKey && value is TValue tValue)
            {
                Add(tKey, tValue);
            }
            else
            {
                throw new ArgumentException($"Key must be of type {typeof(TKey)} and value must be of type {typeof(TValue)}.");
            }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        // Override Clear methods to synchronize serialized lists
        public new void Clear()
        {
            base.Clear();
            keys.Clear();
            values.Clear();
        }

        // Override Remove methods to synchronize serialized lists
        public new bool Remove(TKey key)
        {
            if (base.Remove(key))
            {
                int index = keys.IndexOf(key);
                if (index >= 0)
                {
                    keys.RemoveAt(index);
                    values.RemoveAt(index);
                }
                return true;
            }
            return false;
        }

        void IDictionary.Remove(object key)
        {
            if (key is TKey tKey)
            {
                Remove(tKey);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            if (((ICollection<KeyValuePair<TKey, TValue>>)this).Contains(item))
            {
                return Remove(item.Key);
            }
            return false;
        }

        // IDictionary implementation
        bool IDictionary.Contains(object key)
        {
            if (key is TKey tKey)
            {
                return ContainsKey(tKey);
            }
            return false;
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            return new DictionaryEnumerator(this);
        }

        bool IDictionary.IsFixedSize => false;

        bool IDictionary.IsReadOnly => false;

        ICollection IDictionary.Keys => keys.ToArray();

        ICollection IDictionary.Values => values.ToArray();

        object IDictionary.this[object key]
        {
            get
            {
                if (key is TKey tKey && TryGetValue(tKey, out TValue value))
                {
                    return value;
                }
                return null;
            }
            set
            {
                if (key is TKey tKey && value is TValue tValue)
                {
                    this[tKey] = tValue;
                }
                else
                {
                    throw new ArgumentException($"Key must be of type {typeof(TKey)} and value must be of type {typeof(TValue)}.");
                }
            }
        }

        // ICollection implementation
        void ICollection.CopyTo(Array array, int index)
        {
            if (array is KeyValuePair<TKey, TValue>[] pairs)
            {
                ((ICollection<KeyValuePair<TKey, TValue>>)this).CopyTo(pairs, index);
            }
            else
            {
                throw new ArgumentException("Array must be of type KeyValuePair<TKey, TValue>[].");
            }
        }

        int ICollection.Count => Count;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => ((ICollection)this).SyncRoot;

        // IEnumerator implementation
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // Helper class for IDictionaryEnumerator
        private class DictionaryEnumerator : IDictionaryEnumerator
        {
            private readonly IEnumerator<KeyValuePair<TKey, TValue>> enumerator;

            public DictionaryEnumerator(SerializableDictionary<TKey, TValue> dictionary)
            {
                enumerator = dictionary.GetEnumerator();
            }

            public DictionaryEntry Entry => new DictionaryEntry(enumerator.Current.Key, enumerator.Current.Value);

            public object Key => enumerator.Current.Key;

            public object Value => enumerator.Current.Value;

            public object Current => Entry;

            public bool MoveNext() => enumerator.MoveNext();

            public void Reset() => enumerator.Reset();
        }
    }
}
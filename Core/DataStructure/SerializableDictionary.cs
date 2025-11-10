using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    /// <summary>
    /// A serializable dictionary that integrates with Unity's serialization system.
    /// Internally stores keys and values in serialized lists, and reconstructs the runtime dictionary
    /// during deserialization. Also exposes non-generic IDictionary members for editor/utility code.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    [Serializable]
    public class SerializableDictionary<TKey, TValue> :
        Dictionary<TKey, TValue>,
        IDictionary,
        IDictionary<TKey, TValue>,
        ISerializationCallbackReceiver
    {
        [SerializeField] private List<TKey> keys = new();
        [SerializeField] private List<TValue> values = new();

        // Backing object used by ICollection.SyncRoot. Avoids the recursion bug of casting to ICollection.
        private readonly object _syncRoot = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializableDictionary{TKey,TValue}"/> class.
        /// </summary>
        public SerializableDictionary() : base()
        {
        }

        /// <summary>
        /// Called by Unity before serialization. Rebuilds the 'keys' and 'values' lists from the runtime dictionary.
        /// </summary>
        public void OnBeforeSerialize()
        {
            // Clear serialized lists to ensure a fresh rebuild from current dictionary state.
            keys.Clear();
            values.Clear();

            // Iterate current dictionary entries and push into serialized lists in matching order.
            foreach (KeyValuePair<TKey, TValue> kvp in this)
            {
                keys.Add(kvp.Key);
                values.Add(kvp.Value);
            }
        }

        /// <summary>
        /// Called by Unity after deserialization. Reconstructs the runtime dictionary from 'keys' and 'values'.
        /// </summary>
        public void OnAfterDeserialize()
        {
            // IMPORTANT: Only clear the runtime dictionary. We must NOT clear the serialized lists here.
            // Using base.Clear() prevents wiping 'keys' and 'values' before we copy from them.
            base.Clear();

            // Track duplicates while rebuilding to preserve first occurrence and warn about repeated keys.
            HashSet<TKey> seenKeys = new();

            // Use the minimum count to avoid out-of-range if lists become unsynchronized.
            int count = Mathf.Min(keys.Count, values.Count);

            for (int i = 0; i < count; i++)
            {
                // For reference types, a null key is invalid. For value types, null does not apply.
                bool keyIsNullRef = keys[i] is null && !typeof(TKey).IsValueType;
                if (keyIsNullRef)
                    continue;

                // Attempt to add the pair if the key has not been seen before.
                if (seenKeys.Add(keys[i]))
                    try
                    {
                        // Use base.Add to set only the runtime dictionary (lists are already populated by Unity).
                        base.Add(keys[i], values[i]);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to add key {keys[i]} during deserialization: {e.Message}");
                    }
                else
                    Debug.LogWarning($"Duplicate key {keys[i]} ignored during deserialization.");
            }
        }

        /// <summary>
        /// Adds the specified key/value pair to the dictionary and keeps the serialized lists in sync.
        /// </summary>
        /// <param name="key">The key to add.</param>
        /// <param name="value">The value to add.</param>
        public new void Add(TKey key, TValue value)
        {
            // Add to runtime dictionary first; will throw if key already exists.
            base.Add(key, value);

            // Keep serialized lists synchronized to reflect this insertion immediately in the Inspector.
            keys.Add(key);
            values.Add(value);
        }

        /// <summary>
        /// Removes all items from the dictionary and clears the serialized lists.
        /// </summary>
        public new void Clear()
        {
            // Clear the runtime dictionary.
            base.Clear();

            // Clear the serialized lists to keep them consistent.
            keys.Clear();
            values.Clear();
        }

        /// <summary>
        /// Removes the entry with the specified key from the dictionary and the serialized lists.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns>True if the element is successfully found and removed; otherwise, false.</returns>
        public new bool Remove(TKey key)
        {
            // Remove from runtime dictionary first.
            if (base.Remove(key))
            {
                // If runtime removal succeeded, remove the corresponding serialized entries.
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

        /// <summary>
        /// Gets or sets the value associated with the specified key, synchronizing the serialized lists.
        /// </summary>
        public new TValue this[TKey key]
        {
            get => base[key];
            set
            {
                // If key already exists, update both runtime dictionary and serialized list value.
                if (ContainsKey(key))
                {
                    base[key] = value;
                    int idx = keys.IndexOf(key);
                    if (idx >= 0) values[idx] = value;
                }
                else
                {
                    // If key does not exist, delegate to Add to keep lists in sync.
                    Add(key, value);
                }
            }
        }

        // ----------------------------
        // Non-generic IDictionary impl
        // ----------------------------

        /// <summary>
        /// Adds a key/value pair using non-generic IDictionary interface.
        /// Validates casting to TKey/TValue at runtime.
        /// </summary>
        void IDictionary.Add(object key, object value)
        {
            if (key is TKey tKey && value is TValue tValue)
                Add(tKey, tValue);
            else
                throw new ArgumentException(
                    $"Key must be of type {typeof(TKey)} and value must be of type {typeof(TValue)}.");
        }

        /// <summary>
        /// Removes the element with the specified key via non-generic IDictionary.
        /// </summary>
        void IDictionary.Remove(object key)
        {
            if (key is TKey tKey) Remove(tKey);
        }

        /// <summary>
        /// Determines whether the dictionary contains an element with the specified key (non-generic).
        /// </summary>
        bool IDictionary.Contains(object key)
        {
            if (key is TKey tKey) return ContainsKey(tKey);
            return false;
        }

        /// <summary>
        /// Returns an IDictionaryEnumerator for non-generic enumeration.
        /// </summary>
        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            return new DictionaryEnumerator(this);
        }

        bool IDictionary.IsFixedSize => false;
        bool IDictionary.IsReadOnly => false;

        /// <summary>
        /// Gets an ICollection of keys (as an array) for non-generic IDictionary.
        /// </summary>
        ICollection IDictionary.Keys => keys.ToArray();

        /// <summary>
        /// Gets an ICollection of values (as an array) for non-generic IDictionary.
        /// </summary>
        ICollection IDictionary.Values => values.ToArray();

        /// <summary>
        /// Gets or sets the element with the specified key via non-generic indexer.
        /// </summary>
        object IDictionary.this[object key]
        {
            get
            {
                if (key is TKey tKey && TryGetValue(tKey, out TValue value)) return value;
                return null;
            }
            set
            {
                if (key is TKey tKey && value is TValue tValue)
                    // Delegate to the synchronized generic indexer to keep lists in sync.
                    this[tKey] = tValue;
                else
                    throw new ArgumentException(
                        $"Key must be of type {typeof(TKey)} and value must be of type {typeof(TValue)}.");
            }
        }

        // ----------------------------
        // Non-generic ICollection impl
        // ----------------------------

        /// <summary>
        /// Copies the elements of the dictionary to a compatible one-dimensional array, starting at the specified index.
        /// This overload supports DictionaryEntry[] or object[] as expected by the non-generic ICollection contract.
        /// </summary>
        /// <param name="array">Destination array.</param>
        /// <param name="index">Starting index.</param>
        void ICollection.CopyTo(Array array, int index)
        {
            // Support copying into DictionaryEntry[] to meet non-generic ICollection expectations.
            if (array is DictionaryEntry[] entries)
            {
                int i = index;
                foreach (KeyValuePair<TKey, TValue> kv in this)
                {
                    entries[i++] = new DictionaryEntry(kv.Key, kv.Value);
                }

                return;
            }

            // Support copying into object[] (each element becomes a DictionaryEntry boxed as object).
            if (array is object[] objects)
            {
                int i = index;
                foreach (KeyValuePair<TKey, TValue> kv in this)
                {
                    objects[i++] = new DictionaryEntry(kv.Key, kv.Value);
                }

                return;
            }

            throw new ArgumentException("Array must be DictionaryEntry[] or object[].");
        }

        int ICollection.Count => Count;

        bool ICollection.IsSynchronized => false;

        /// <summary>
        /// Gets an object that can be used to synchronize access to the collection (non-generic ICollection).
        /// </summary>
        object ICollection.SyncRoot => _syncRoot;

        // ----------------------------
        // IEnumerable impls
        // ----------------------------

        /// <summary>
        /// Returns an enumerator that iterates through the collection (non-generic IEnumerable).
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Helper non-generic enumerator that adapts KeyValuePair to DictionaryEntry.
        /// </summary>
        private class DictionaryEnumerator : IDictionaryEnumerator
        {
            private readonly IEnumerator<KeyValuePair<TKey, TValue>> enumerator;

            public DictionaryEnumerator(SerializableDictionary<TKey, TValue> dictionary)
            {
                enumerator = dictionary.GetEnumerator();
            }

            public DictionaryEntry Entry => new(enumerator.Current.Key, enumerator.Current.Value);
            public object Key => enumerator.Current.Key;
            public object Value => enumerator.Current.Value;
            public object Current => Entry;

            public bool MoveNext()
            {
                return enumerator.MoveNext();
            }

            public void Reset()
            {
                enumerator.Reset();
            }
        }
    }
}
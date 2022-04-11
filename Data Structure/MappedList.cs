using System;
using System.Collections.Generic;

namespace LegendaryTools
{
    public interface IKeyValuePair<TKey, TValue>
    {
        TKey Key { get; set; }
        TValue Value { get; set; }
    }
    
    [Serializable]
    public class MappedList<T,TKey,TValue>
        where T : IKeyValuePair<TKey,TValue>
    {
        public List<T> List;
        public Dictionary<TKey, TValue> Dictionary;

        public T this[int index]
        {
            get => List[index];
            set => List[index] = value;
        }

        public TValue this[TKey key]
        {
            get => Dictionary[key];
            set => Dictionary[key] = value;
        }

        public ICollection<TKey> Keys => Dictionary.Keys;
        public ICollection<TValue> Values => Dictionary.Values;

        public int Count => List.Count;

        public MappedList()
        {
            List = new List<T>();
            Dictionary = new Dictionary<TKey, TValue>();
        }

        public MappedList(int capacity)
        {
            List = new List<T>(capacity);
            Dictionary = new Dictionary<TKey, TValue>(capacity);
        }

        public MappedList(IEnumerable<T> collection)
        {
            List = new List<T>(collection);
            Dictionary = new Dictionary<TKey, TValue>();
            RebuildDictionary();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return List.GetEnumerator();
        }

        public void RebuildDictionary()
        {
            Dictionary.Clear();
            foreach (T itemList in List)
            {
                Dictionary.Add(itemList.Key, itemList.Value);
            }
        }

        public void Add(T item)
        {
            List.Add(item);
            Dictionary.Add(item.Key, item.Value);
        }

        public void Add(TKey key, TValue value)
        {
            Dictionary.Add(key, value);
            
            T newItem = default(T);
            newItem.Key = key;
            newItem.Value = value;
            List.Add(newItem);
        }

        public void Clear()
        {
            List.Clear();
            Dictionary.Clear();
        }

        public void Insert(int index, T item)
        {
            List.Insert(index, item);
            Dictionary.Add(item.Key, item.Value);
        }

        public bool Remove(T item)
        {
            Dictionary.Remove(item.Key);
            
            return List.Remove(item);
        }

        public bool Remove(TKey key)
        {
            List.RemoveAll(item => item.Key.Equals(key));
            return Dictionary.Remove(key);
        }
        
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            List.RemoveAll(iterator => iterator.Key.Equals(item.Key));
            return (Dictionary as IDictionary<TKey, TValue>).Remove(item);
        }
        
        public void RemoveAt(int index)
        {
            Dictionary.Remove(this[index].Key);
            List.RemoveAt(index);
        }

        public int IndexOf(T item)
        {
            return List.IndexOf(item);
        }

        public bool Contains(T item)
        {
            return List.Contains(item);
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return (Dictionary as IDictionary<TKey, TValue>).Contains(item);
        }

        public bool ContainsKey(TKey key)
        {
            return Dictionary.ContainsKey(key);
        }
        
        public bool ContainsValue(TValue value)
        {
            return Dictionary.ContainsValue(value);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return Dictionary.TryGetValue(key, out value);
        }

        public void AddRange(IEnumerable<T> collection)
        {
            List.AddRange(collection);
        }

        public int BinarySearch(int index, int count, T item, IComparer<T> comparer)
        {
            return List.BinarySearch(index, count, item, comparer);
        }

        public int BinarySearch(T item)
        {
            return List.BinarySearch(item);
        }

        public int BinarySearch(T item, IComparer<T> comparer)
        {
            return List.BinarySearch(item, comparer);
        }

        public List<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter)
        {
            return List.ConvertAll(converter);
        }

        public bool Exists(Predicate<T> match)
        {
            return List.Exists(match);
        }

        public T Find(Predicate<T> match)
        {
            return List.Find(match);
        }

        public List<T> FindAll(Predicate<T> match)
        {
            return List.FindAll(match);
        }

        public int FindIndex(Predicate<T> match)
        {
            return List.FindIndex(match);
        }

        public int FindIndex(int startIndex, Predicate<T> match)
        {
            return List.FindIndex(startIndex, match);
        }

        public int FindIndex(int startIndex, int count, Predicate<T> match)
        {
            return List.FindIndex(startIndex, count, match);
        }

        public T FindLast(Predicate<T> match)
        {
            return List.FindLast(match);
        }

        public int FindLastIndex(Predicate<T> match)
        {
            return List.FindLastIndex(match);
        }

        public int FindLastIndex(int startIndex, Predicate<T> match)
        {
            return List.FindLastIndex(startIndex, match);
        }

        public int FindLastIndex(int startIndex, int count, Predicate<T> match)
        {
            return List.FindLastIndex(startIndex, count, match);
        }

        public void ForEach(Action<T> action)
        {
            List.ForEach(action);
        }

        public List<T> GetRange(int index, int count)
        {
            return List.GetRange(index, count);
        }

        public void RemoveRange(int index, int count)
        {
            List.RemoveRange(index, count);
        }

        public void Reverse()
        {
            List.Reverse();
        }

        public void Reverse(int index, int count)
        {
            List.Reverse(index, count);
        }

        public void Sort()
        {
            List.Sort();
        }

        public void Sort(IComparer<T> comparer)
        {
            List.Sort(comparer);
        }

        public void Sort(int index, int count, IComparer<T> comparer)
        {
            List.Sort(index, count, comparer);
        }

        public void Sort(Comparison<T> comparison)
        {
            List.Sort(comparison);
        }

        public T[] ToArray()
        {
            return List.ToArray();
        }

        public void TrimExcess()
        {
            List.TrimExcess();
        }

        public bool TrueForAll(Predicate<T> match)
        {
            return List.TrueForAll(match);
        }
    }
}
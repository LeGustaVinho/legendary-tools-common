using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools
{
    public static class DictionaryExtension
    {
        public static void AddOrUpdate<K,V>(this IDictionary<K,V> dictionary, K key, V value)
        {
            if (dictionary.ContainsKey(key))
            {
                dictionary[key] = value;
            }
            else
            {
                dictionary.Add(key, value);
            }
        }
        
        public static V[] ValuesToArray<K,V>(this IDictionary<K,V> dictionary)
        {
            V[] result = new V[dictionary.Count];
            int i = 0;
            foreach (KeyValuePair<K, V> pair in dictionary)
            {
                result[i] = pair.Value;
                i++;
            }
            return result;
        }
        
        public static K[] KeysToArray<K,V>(this IDictionary<K,V> dictionary)
        {
            K[] result = new K[dictionary.Count];
            int i = 0;
            foreach (KeyValuePair<K, V> pair in dictionary)
            {
                result[i] = pair.Key;
                i++;
            }
            return result;
        }
        
        public static List<V> ValuesToList<K,V>(this IDictionary<K,V> dictionary)
        {
            List<V> result = new List<V>(dictionary.Count); 
            int i = 0;
            foreach (KeyValuePair<K, V> pair in dictionary)
            {
                result[i] = pair.Value;
                i++;
            }
            return result;
        }
        
        public static List<K> KeysToList<K,V>(this IDictionary<K,V> dictionary)
        {
            List<K> result = new List<K>(dictionary.Count);
            int i = 0;
            foreach (KeyValuePair<K, V> pair in dictionary)
            {
                result[i] = pair.Key;
                i++;
            }
            return result;
        }
        
        public static T GetRandomWeight<T>(this IDictionary<T, float> dictionary)
        {
            float total = 0;
            foreach (KeyValuePair<T, float> pair in dictionary) 
            {
                total += pair.Value;
            }

            float randomPoint = UnityEngine.Random.value * total;

            foreach (KeyValuePair<T, float> pair in dictionary) 
            {
                if (randomPoint < pair.Value) 
                {
                    return pair.Key;
                }
                
                randomPoint -= pair.Value;
            }
            
            return default(T);
        }
    }
}
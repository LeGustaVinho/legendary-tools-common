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
    }
}
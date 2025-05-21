using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Inventory
{
#if ODIN_INSPECTOR
    [Serializable]
    public class ScriptableObjectInventory<E, C> : Inventory<E>, IInventory<C>
        where C : ScriptableObject
        where E : struct, Enum, IConvertible
    {
        public DictionaryConfigEnumWeaver<E, C> ScriptableConverter;

        public float this[C key]
        {
            get
            {
                if (ScriptableConverter.InvertedConfigs.TryGetValue(key, out E enumName))
                {
                    return this[enumName];
                }

                throw new Exception($"[ScriptableObjectInventory] Element with key {key.name} was not found.");
            }
        }
    
#pragma warning disable CS0108, CS0114
        public event Action<C, float, float> OnInventoryChange;
#pragma warning restore CS0108, CS0114

        public ScriptableObjectInventory() : base()
        {
            
        }
        
        public ScriptableObjectInventory(DictionaryConfigEnumWeaver<E, C> scriptableConverter) : this()
        {
            ScriptableConverter = scriptableConverter;
            base.OnInventoryChange += OnBaseInventoryChange;
        }
        
        public ScriptableObjectInventory(DictionaryConfigEnumWeaver<E, C> scriptableConverter, 
            Dictionary<C, float> data) : this(scriptableConverter) 
        {
            foreach (KeyValuePair<C, float> item in data)
            {
                if (scriptableConverter.InvertedConfigs.TryGetValue(item.Key, out E enumName))
                {
                    Items.Add(enumName, item.Value);
                }
            }
        }

        private void OnBaseInventoryChange(E enumName, float oldValue, float newValue)
        {
            if (ScriptableConverter.Configs.TryGetValue(enumName, out C scriptableObject))
            {
                OnInventoryChange?.Invoke(scriptableObject, oldValue, newValue);
            }
        }

        public void Add(C item, float amount)
        {
            if (ScriptableConverter.InvertedConfigs.TryGetValue(item, out E enumName))
            {
                base.Add(enumName, amount);
            }
        }

        public bool Remove(C item, float amount)
        {
            if (ScriptableConverter.InvertedConfigs.TryGetValue(item, out E enumName))
            {
                return base.Remove(enumName, amount);
            }

            return false;
        }

        public bool Contains(C item)
        {
            if (ScriptableConverter.InvertedConfigs.TryGetValue(item, out E enumName))
            {
                return base.Contains(enumName);
            }

            return false;
        }

        public bool TryGetValue(C item, out float amount)
        {
            if (ScriptableConverter.InvertedConfigs.TryGetValue(item, out E enumName))
            {
                return base.TryGetValue(enumName, out amount);
            }

            amount = 0;
            return false;
        }

#pragma warning disable CS0108, CS0114
        public Dictionary<C, float> GetAll()
#pragma warning restore CS0108, CS0114
        {
            Dictionary<C, float> clone = new Dictionary<C, float>();
            foreach (KeyValuePair<E, float> item in Items)
            {
                if (ScriptableConverter.Configs.TryGetValue(item.Key, out C scriptableObject))
                {
                    clone.Add(scriptableObject, item.Value);
                }
            }

            return clone;
        }

        public void TransferAllTo(IInventory<C> inventory)
        {
            base.TransferAllTo(inventory as Inventory<E>);
        }
    }
#endif
}
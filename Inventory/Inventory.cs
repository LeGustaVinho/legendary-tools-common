using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.Inventory
{
    public interface IInventory<T>
    {
        void Add(T item, float amount);
        bool Remove(T item, float amount);
        bool Contains(T item);
        bool TryGetValue(T item, out float amount);
        Dictionary<T, float> GetAll();
        float this[T key] { get; }
        void Clear();
        void TransferAllTo(IInventory<T> inventory);
        event Action<T, float, float> OnInventoryChange;
    }

    [Serializable]
    public class Inventory<T> : IInventory<T>, IDictionary<T, float>
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        protected readonly Dictionary<T, float> Items = new Dictionary<T, float>();
        public event Action<T, float, float> OnInventoryChange;

        public float this[T key]
        {
            get => Items[key];
            set => throw new NotImplementedException();
        }

        #region IDictionary Implementation

        public ICollection<T> Keys => Items.Keys;
        public ICollection<float> Values => Items.Values;
        public int Count => Items.Count;
        public bool IsReadOnly => (Items as IDictionary<T, float>).IsReadOnly;

        public Inventory()
        {
        }
        
        public Inventory(Dictionary<T, float> data) : this()
        {
            foreach (KeyValuePair<T, float> item in data)
            {
                Items.Add(item.Key, item.Value);
            }
        }

        public void Add(KeyValuePair<T, float> item)
        {
            Add(item.Key, item.Value);
        }

        public bool Contains(KeyValuePair<T, float> item)
        {
            return (Items as IDictionary<T, float>).Contains(item);
        }

        public void CopyTo(KeyValuePair<T, float>[] array, int arrayIndex)
        {
            (Items as IDictionary<T, float>).CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<T, float> item)
        {
            return Remove(item.Key, item.Value);
        }
        
        public bool ContainsKey(T key)
        {
            return Contains(key);
        }

        public bool Remove(T key)
        {
            return Items.TryGetValue(key, out float currentAmount) && Remove(key, currentAmount);
        }
        
        public IEnumerator<KeyValuePair<T, float>> GetEnumerator()
        {
            return Items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        
        #endregion
        
        public Dictionary<T, float> GetAll()
        {
            Dictionary<T, float> clone = new Dictionary<T, float>();

            foreach (KeyValuePair<T, float> keypair in Items)
            {
                clone.Add(keypair.Key, keypair.Value);
            }
            
            return clone;
        }        
        
        public virtual void Clear()
        {
            foreach (KeyValuePair<T, float> valueKeyPair in Items)
            {
                OnInventoryChange?.Invoke(valueKeyPair.Key, valueKeyPair.Value, 0);
            }
            
            Items.Clear();
        }

        public virtual void TransferAllTo(IInventory<T> inventory)
        {
            foreach (KeyValuePair<T, float> valueKeyPair in Items)
            {
                inventory.Add(valueKeyPair.Key, valueKeyPair.Value);
                OnInventoryChange?.Invoke(valueKeyPair.Key, valueKeyPair.Value, 0);
            }
            
            Items.Clear();
        }
        public virtual void Add(T item, float amount)
        {
            if (Items.TryGetValue(item, out float currentAmount))
            {
                float oldAmount = Items[item];
                Items[item] += amount;
                OnInventoryChange?.Invoke(item, oldAmount, Items[item]);
            }
            else
            {
                Items.Add(item, amount);
                OnInventoryChange?.Invoke(item, 0, amount);
            }
        }
        
        public virtual bool Remove(T item, float amount)
        {
            if (Items.TryGetValue(item, out float currentAmount))
            {
                if (currentAmount >= amount)
                {
                    float oldAmount = Items[item];
                    Items[item] -= amount;
                    OnInventoryChange?.Invoke(item, oldAmount, Items[item]);

                    if (Items[item] == 0)
                    {
                        Items.Remove(item);
                    }
                    
                    return true;
                }
            }

            return false;
        }

        public virtual bool Contains(T item)
        {
            return Items.ContainsKey(item);
        }

        public virtual bool TryGetValue(T item, out float amount)
        {
            return Items.TryGetValue(item, out amount);
        }
    }
}
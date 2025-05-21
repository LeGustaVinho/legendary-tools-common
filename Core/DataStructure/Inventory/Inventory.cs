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
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));

            if (ReferenceEquals(this, inventory))
                throw new InvalidOperationException("Cannot transfer items to the same inventory.");
            
            foreach (KeyValuePair<T, float> valueKeyPair in Items)
            {
                inventory.Add(valueKeyPair.Key, valueKeyPair.Value);
                OnInventoryChange?.Invoke(valueKeyPair.Key, valueKeyPair.Value, 0);
            }
            
            Items.Clear();
        }
        public virtual void Add(T item, float amount)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (amount < 0) throw new ArgumentException("Amount cannot be negative.", nameof(amount));
            if (float.IsInfinity(amount)) throw new OverflowException("Amount cannot be infinity.");
            
            if (Items.TryGetValue(item, out float currentAmount))
            {
                // Check for potential overflow
                if (float.IsPositiveInfinity(currentAmount + amount))
                    throw new OverflowException("Total amount exceeds float.MaxValue.");

                if (currentAmount > float.MaxValue - amount)
                    throw new OverflowException("Total amount exceeds float.MaxValue.");
                
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
            if (item == null) return false;
            if (amount < 0) throw new ArgumentException("Amount cannot be negative.", nameof(amount));
            if (!Items.TryGetValue(item, out float currentAmount)) return false;
            if (!(currentAmount >= amount)) return false;
            
            float oldAmount = Items[item];
            Items[item] -= amount;
            OnInventoryChange?.Invoke(item, oldAmount, Items[item]);

            if (Items[item] == 0)
            {
                Items.Remove(item);
            }
                    
            return true;

        }

        public virtual bool Contains(T item)
        {
            if (item == null) return false;
            return Items.ContainsKey(item);
        }

        public virtual bool TryGetValue(T item, out float amount)
        {
            if (item == null)
            {
                amount = 0;
                return false;
            }
            return Items.TryGetValue(item, out amount);
        }
    }
}
using System;
using System.Collections.Generic;

namespace LegendaryTools.Inventory
{
    public interface ICargo<T>
    {
        float MaxLimit { get; }
        float AvailableLimit { get; }
        float CurrentLimit { get; }
        List<ICargoContainer<T>> Containers { get; }
        bool Add(float weight, IInventory<T> inventory);
        bool Add(List<ICargoContainer<T>> containersToAdd);
        List<ICargoContainer<T>> Remove(float maxLimitToExtract);
        void TransferWhenPossibleTo(ICargo<T> targetCargo);
        void TransferAllTo(IInventory<T> inventory);
        List<ICargoContainer<T>> RemoveAll();

        event Action<float, float> OnCargoLimitChange;
    }

    [Serializable]
    public class Cargo<T> : ICargo<T>
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public virtual float MaxLimit { get; set; }
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public float AvailableLimit => MaxLimit - CurrentLimit;
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public float CurrentLimit => GetListLimit(Containers);

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        private List<ICargoContainer<T>> containers = new List<ICargoContainer<T>>();

        public List<ICargoContainer<T>> Containers
        {
            get
            {
                List<ICargoContainer<T>> clone = new List<ICargoContainer<T>>();

                if (containers != null)
                {
                    foreach (ICargoContainer<T> cargoContainer in containers)
                    {
                        clone.Add(cargoContainer);
                    }
                }
            
                return clone;
            }
        }
        
        public event Action<float, float> OnCargoLimitChange;

        public virtual bool Add(float limit, IInventory<T> inventory)
        {
            if (inventory == null)
            {
                return false;
            }
            
            if (limit + CurrentLimit <= MaxLimit)
            {
                float oldLimit = CurrentLimit;
                containers.Add(ConstructCargoContainer(limit, inventory));
                containers.Sort((x,y) => x.Limit.CompareTo(y.Limit)); //Sort ASC by Limit
                OnCargoLimitChange?.Invoke(oldLimit, oldLimit + limit);
                return true;
            }

            return false;
        }
        
        public virtual bool Add(List<ICargoContainer<T>> containersToAdd)
        {
            if (containers == null)
            {
                return false;
            }

            float totalLimit = GetListLimit(containersToAdd);

            if (totalLimit <= AvailableLimit)
            {
                float oldLimit = CurrentLimit;
                foreach (ICargoContainer<T> cargoContainer in containersToAdd)
                {
                    containers.Add(cargoContainer);
                }

                containersToAdd.Clear();
                OnCargoLimitChange?.Invoke(oldLimit, oldLimit + totalLimit);
                return true;
            }

            return false;
        }

        public virtual List<ICargoContainer<T>> Remove(float maxLimitToExtract)
        {
            if (maxLimitToExtract > MaxLimit)
            {
                return RemoveAll();
            }
            
            List<ICargoContainer<T>> cargoContainersToExtracted = new List<ICargoContainer<T>>();
            float limitExtracted = 0;

            float oldLimit = CurrentLimit;
            int loopProtection = containers.Count;
            while (limitExtracted <= maxLimitToExtract && containers.Count > 0 && loopProtection > 0)
            {
                ICargoContainer<T> container = containers[0];
                if (container.Limit + limitExtracted <= maxLimitToExtract)
                {
                    containers.Remove(container);
                    limitExtracted += container.Limit;
                    cargoContainersToExtracted.Add(container);
                }

                loopProtection--;
            }
            
            OnCargoLimitChange?.Invoke(oldLimit, CurrentLimit);
            return cargoContainersToExtracted;
        }

        public void TransferWhenPossibleTo(ICargo<T> targetCargo)
        {
            List<ICargoContainer<T>> cargoContainersToTransfer = Remove(targetCargo.AvailableLimit);
            targetCargo.Add(cargoContainersToTransfer);
        }
        
        public void TransferAllTo(IInventory<T> inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            
            List<ICargoContainer<T>> cargoContainersToTransfer = RemoveAll();

            foreach (ICargoContainer<T> cargoContainer in cargoContainersToTransfer)
            {
                cargoContainer.Inventory.TransferAllTo(inventory);
            }
        }

        public virtual List<ICargoContainer<T>> RemoveAll()
        {
            float oldLimit = CurrentLimit;
            List<ICargoContainer<T>> clone = Containers;
            OnCargoLimitChange?.Invoke(oldLimit, 0);
            containers.Clear();
            return clone;
        }

        public virtual ICargoContainer<T> ConstructCargoContainer(float limit, IInventory<T> inventory)
        {
            return new CargoContainer<T>(limit, inventory);
        }

        public static float GetListLimit(List<ICargoContainer<T>> containers)
        {
            float totalLimit = 0;
            foreach (ICargoContainer<T> cargoContainer in containers)
            {
                totalLimit += cargoContainer.Limit;
            }

            return totalLimit;
        }
    }
}
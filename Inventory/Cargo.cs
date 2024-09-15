using System;
using System.Collections.Generic;

namespace LegendaryTools.Inventory
{
    public interface ICargo<TInv, T>
        where TInv : IInventory<T>
    {
        float MaxLimit { get; }
        float AvailableLimit { get; }
        float CurrentLimit { get; }
        List<CargoContainer<TInv, T>> Containers { get; }
        bool Add(float weight, TInv inventory);
        bool Add(List<CargoContainer<TInv, T>> containersToAdd);
        List<CargoContainer<TInv, T>> Remove(float maxLimitToExtract);
        void TransferWhenPossibleTo(Cargo<TInv, T> targetCargo);
        void TransferAllTo(TInv inventory);
        List<CargoContainer<TInv, T>> RemoveAll();

        event Action<float, float> OnCargoLimitChange;
    }

    [Serializable]
    public class Cargo<TInv, T> : ICargo<TInv, T> where TInv : IInventory<T>
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public virtual float MaxLimit { get; protected set; }
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
        private List<CargoContainer<TInv, T>> containers = new List<CargoContainer<TInv, T>>();

        public List<CargoContainer<TInv, T>> Containers
        {
            get
            {
                List<CargoContainer<TInv, T>> clone = new List<CargoContainer<TInv, T>>();

                if (containers != null)
                {
                    foreach (CargoContainer<TInv, T> cargoContainer in containers)
                    {
                        clone.Add(cargoContainer);
                    }
                }
            
                return clone;
            }
        }
        
        public event Action<float, float> OnCargoLimitChange;

        public virtual bool Add(float weight, TInv inventory)
        {
            if (inventory == null)
            {
                return false;
            }
            
            if (weight + CurrentLimit <= MaxLimit)
            {
                float oldLimit = CurrentLimit;
                containers.Add(new CargoContainer<TInv, T>(weight, inventory));
                containers.Sort((x,y) => x.Limit.CompareTo(y.Limit)); //Sort ASC by Limit
                OnCargoLimitChange?.Invoke(oldLimit, oldLimit + weight);
                return true;
            }

            return false;
        }
        
        public virtual bool Add(List<CargoContainer<TInv, T>> containersToAdd)
        {
            if (containers == null)
            {
                return false;
            }

            float totalLimit = GetListLimit(containersToAdd);

            if (totalLimit <= AvailableLimit)
            {
                float oldLimit = CurrentLimit;
                foreach (CargoContainer<TInv, T> cargoContainer in containersToAdd)
                {
                    containers.Add(cargoContainer);
                }

                containersToAdd.Clear();
                OnCargoLimitChange?.Invoke(oldLimit, oldLimit + totalLimit);
                return true;
            }

            return false;
        }

        public virtual List<CargoContainer<TInv, T>> Remove(float maxLimitToExtract)
        {
            if (maxLimitToExtract > MaxLimit)
            {
                return RemoveAll();
            }
            
            List<CargoContainer<TInv, T>> cargoContainersToExtracted = new List<CargoContainer<TInv, T>>();
            float limitExtracted = 0;

            float oldLimit = CurrentLimit;
            int loopProtection = containers.Count;
            while (limitExtracted <= maxLimitToExtract && containers.Count > 0 && loopProtection > 0)
            {
                CargoContainer<TInv, T> container = containers[0];
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

        public void TransferWhenPossibleTo(Cargo<TInv, T> targetCargo)
        {
            List<CargoContainer<TInv, T>> cargoContainersToTransfer = Remove(targetCargo.AvailableLimit);
            targetCargo.Add(cargoContainersToTransfer);
        }
        
        public void TransferAllTo(TInv inventory)
        {
            List<CargoContainer<TInv, T>> cargoContainersToTransfer = RemoveAll();

            foreach (CargoContainer<TInv, T> cargoContainer in cargoContainersToTransfer)
            {
                cargoContainer.Inventory.TransferAllTo(inventory);
            }
        }

        public virtual List<CargoContainer<TInv, T>> RemoveAll()
        {
            float oldLimit = CurrentLimit;
            List<CargoContainer<TInv, T>> clone = Containers;
            OnCargoLimitChange?.Invoke(oldLimit, 0);
            containers.Clear();
            return clone;
        }

        public static float GetListLimit(List<CargoContainer<TInv, T>> containers)
        {
            float totalLimit = 0;
            foreach (CargoContainer<TInv, T> cargoContainer in containers)
            {
                totalLimit += cargoContainer.Limit;
            }

            return totalLimit;
        }
    }
}
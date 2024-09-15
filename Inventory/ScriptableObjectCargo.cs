using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Inventory
{
    [Serializable]
    public class ScriptableObjectCargo<E, C> : Cargo<ScriptableObjectInventory<E, C>, E>, ICargo<ScriptableObjectInventory<E, C>, C>
        where C : ScriptableObject
        where E : struct, Enum, IConvertible
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
#pragma warning disable CS0108, CS0114
        public List<CargoContainer<ScriptableObjectInventory<E, C>, C>> Containers
#pragma warning restore CS0108, CS0114
        {
            get
            {
                List<CargoContainer<ScriptableObjectInventory<E, C>, E>> enumContainers = base.Containers;
                List<CargoContainer<ScriptableObjectInventory<E, C>, C>> configContainers =
                    new List<CargoContainer<ScriptableObjectInventory<E, C>, C>>();

                foreach (CargoContainer<ScriptableObjectInventory<E, C>, E> enumContainer in enumContainers)
                {
                    configContainers.Add(new CargoContainer<ScriptableObjectInventory<E, C>, C>(enumContainer.Limit, 
                        enumContainer.Inventory));
                }

                return configContainers;
            }
        }

        public bool Add(List<CargoContainer<ScriptableObjectInventory<E, C>, C>> containersToAdd)
        {
            List<CargoContainer<ScriptableObjectInventory<E, C>, E>> enumCargoContainerCollection =
                new List<CargoContainer<ScriptableObjectInventory<E, C>, E>>(containersToAdd.Count);

            foreach (CargoContainer<ScriptableObjectInventory<E, C>, C> configContainer in containersToAdd)
                enumCargoContainerCollection.Add(new CargoContainer<ScriptableObjectInventory<E, C>, E>(
                    configContainer.Limit,
                    configContainer.Inventory));

            return base.Add(enumCargoContainerCollection);
        }

#pragma warning disable CS0108, CS0114
        public List<CargoContainer<ScriptableObjectInventory<E, C>, C>> Remove(float maxLimitToExtract)
#pragma warning restore CS0108, CS0114
        {
            List<CargoContainer<ScriptableObjectInventory<E, C>, E>> removed = base.Remove(maxLimitToExtract);

            List<CargoContainer<ScriptableObjectInventory<E, C>, C>> configCargoContainerCollection =
                new List<CargoContainer<ScriptableObjectInventory<E, C>, C>>(removed.Count);

            foreach (CargoContainer<ScriptableObjectInventory<E, C>, E> enumCargoContainer in removed)
                configCargoContainerCollection.Add(
                    new CargoContainer<ScriptableObjectInventory<E, C>, C>(enumCargoContainer.Limit,
                        enumCargoContainer.Inventory));

            return configCargoContainerCollection;
        }

        public void TransferWhenPossibleTo(Cargo<ScriptableObjectInventory<E, C>, C> targetCargo)
        {
            List<CargoContainer<ScriptableObjectInventory<E, C>, C>> cargoContainersToTransfer = Remove(targetCargo.AvailableLimit);
            targetCargo.Add(cargoContainersToTransfer);
        }

#pragma warning disable CS0108, CS0114
        public List<CargoContainer<ScriptableObjectInventory<E, C>, C>> RemoveAll()
#pragma warning restore CS0108, CS0114
        {
            List<CargoContainer<ScriptableObjectInventory<E, C>, E>> removed = base.RemoveAll();

            List<CargoContainer<ScriptableObjectInventory<E, C>, C>> configCargoContainerCollection =
                new List<CargoContainer<ScriptableObjectInventory<E, C>, C>>(removed.Count);

            foreach (CargoContainer<ScriptableObjectInventory<E, C>, E> enumCargoContainer in removed)
                configCargoContainerCollection.Add(
                    new CargoContainer<ScriptableObjectInventory<E, C>, C>(enumCargoContainer.Limit,
                        enumCargoContainer.Inventory));

            return configCargoContainerCollection;
        }
    }
}
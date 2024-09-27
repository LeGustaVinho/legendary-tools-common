using System;

namespace LegendaryTools.Inventory
{
    public interface ICargoContainer<T>
    {
        public float Limit { get; set; }
        public IInventory<T> Inventory { get; set; }
    }

    [Serializable]
    public class CargoContainer<T> : ICargoContainer<T>
    {
        public float Limit { get; set; }
        public IInventory<T> Inventory { get; set; }
        
        public CargoContainer(float limit, IInventory<T> inventory)
        {
            Limit = limit;
            Inventory = inventory;
        }
    }
}
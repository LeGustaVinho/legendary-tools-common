using System;

namespace LegendaryTools.Inventory
{
    [Serializable]
    public class CargoContainer<TInv, T>
        where TInv : IInventory<T>
    {
        public float Limit;
        public TInv Inventory;
        
        public CargoContainer(float limit, TInv inventory)
        {
            Limit = limit;
            Inventory = inventory;
        }
    }
}
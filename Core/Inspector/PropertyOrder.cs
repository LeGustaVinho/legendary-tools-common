using System;

namespace LegendaryTools.Inspector
{
    public class PropertyOrderAttribute : Attribute
    {
        /// <summary>The order for the property.</summary>
        public float Order;
        
        public PropertyOrderAttribute()
        {
        }

        /// <summary>Defines a custom order for the property.</summary>
        /// <param name="order">The order for the property.</param>
        public PropertyOrderAttribute(float order) => this.Order = order;
    }
}
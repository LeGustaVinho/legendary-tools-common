using System;

namespace LegendaryTools.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AssetIconAttribute : Attribute
    {
    }
}

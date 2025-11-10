using System;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Non-generic base for all Reference structs to enable a single PropertyDrawer.
    /// </summary>
    [Serializable]
    public abstract class SOReferenceBase
    {
        public bool UseConstant = true;
    }
}
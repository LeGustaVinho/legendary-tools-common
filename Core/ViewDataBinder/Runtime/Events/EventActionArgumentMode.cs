using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    public enum EventActionArgumentMode
    {
        [InspectorName("No Arguments")]
        None = 0,

        [InspectorName("Old Value")]
        OldValue = 1,

        [InspectorName("New Value")]
        NewValue = 2,

        [InspectorName("Old Value + New Value")]
        OldAndNewValue = 3
    }
}

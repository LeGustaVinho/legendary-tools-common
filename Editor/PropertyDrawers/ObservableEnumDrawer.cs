#if UNITY_EDITOR
using UnityEditor;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Bridges ObservableEnum<TEnum> to the same drawing/assignment logic used by Observable<T>.
    /// </summary>
    [CustomPropertyDrawer(typeof(ObservableEnum<>), true)]
    public class ObservableEnumDrawer : ObservableDrawer
    {
        // Intentionally empty. Inherits all behavior from ObservableDrawer.
    }
}
#endif
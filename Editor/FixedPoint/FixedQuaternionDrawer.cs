using UnityEditor;

namespace LegendaryTools.FixedPoint.Editor
{
    /// <summary>Uses the same raw decimal editing and atomic precision changes as the vector drawers.</summary>
    [CustomPropertyDrawer(typeof(SerializedFixedQuaternion))]
    public sealed class FixedQuaternionDrawer : FixedVectorDrawer
    {
        protected override int Dimensions => 4;
    }
}

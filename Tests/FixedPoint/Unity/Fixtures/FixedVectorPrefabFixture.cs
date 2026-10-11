using UnityEngine;

namespace LegendaryTools.FixedPoint.Tests
{
    // A runtime test assembly permits attachment to a prefab; Editor-only behaviours cannot be attached.
    public sealed class FixedVectorPrefabFixture : MonoBehaviour
    {
        public SerializedFixedVector3 Value;
    }
}

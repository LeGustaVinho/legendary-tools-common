using UnityEngine;

namespace LegendaryTools
{
    public interface ISimpleTransform
    {
        Vector3 LossyScale { get; }
        Transform Parent { get; set; }
        Vector3 Position { get; set; }
        Quaternion Rotation { get; set; }
    }
}
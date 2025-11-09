using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.SOAP.Variables
{
    [CreateAssetMenu(menuName = "Tools/SOAP/Variables/Float (Ranged)", fileName = "FloatVariableRanged")]
    public class FloatVariableRanged : SORangedNumber<float>
    {
        protected override float Clamp(float value, float min, float max)
        {
            return Mathf.Clamp(value, min, max);
        }

        protected override IEqualityComparer<float> Comparer => new FloatComparer();

        private sealed class FloatComparer : IEqualityComparer<float>
        {
            public bool Equals(float x, float y)
            {
                return Mathf.Approximately(x, y);
            }

            public int GetHashCode(float obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
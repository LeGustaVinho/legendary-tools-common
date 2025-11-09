using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.SOAP.Variables
{
    [CreateAssetMenu(menuName = "Tools/SOAP/Variables/Float", fileName = "FloatVariable")]
    public class FloatVariable : SOVariable<float>
    {
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
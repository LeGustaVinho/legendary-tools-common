using UnityEngine;

namespace LegendaryTools.SOAP.Variables
{
    [CreateAssetMenu(menuName = "Tools/SOAP/Variables/Int (Ranged)", fileName = "IntVariableRanged")]
    public class IntVariableRanged : SORangedNumber<int>
    {
        protected override int Clamp(int value, int min, int max)
        {
            return Mathf.Clamp(value, min, max);
        }
    }
}
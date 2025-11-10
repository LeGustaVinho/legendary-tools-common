using System;
using UnityEngine;

namespace LegendaryTools.SOAP
{
    [CreateAssetMenu(menuName = "Tools/SOAP/Variables/Double (Ranged)", fileName = "DoubleVariableRanged")]
    public class DoubleVariableRanged : SORangedNumber<double>
    {
        protected override double Clamp(double value, double min, double max)
        {
            return Math.Min(Math.Max(value, min), max);
        }
    }
}
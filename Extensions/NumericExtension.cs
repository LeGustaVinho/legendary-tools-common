using UnityEngine;

namespace LegendaryTools
{
    public static class NumericExtension
    {
        public static float Remap(this float value, float low1, float high1, float low2, float high2)
        {
            return low2 + (high2 - low2) * (value - low1) / (high1 - low1);
        }

        public static bool IsSimilar(this float lhs, float rhs, float threshold)
        {
            return Mathf.Abs(lhs - rhs) < threshold;
        }
    }
}
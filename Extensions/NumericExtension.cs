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
        
        public static string CompactFloat(float value)
        {
            return value switch
            {
                >= 1_000_000_000_000_000 => $"{(value / 1_000_000_000_000_000f):0.#}Q",  // Quadrillion
                >= 1_000_000_000_000 => $"{(value / 1_000_000_000_000f):0.#}T",          // Trillion
                >= 1_000_000_000 => $"{(value / 1_000_000_000f):0.#}B",                  // Billion
                >= 1_000_000 => $"{(value / 1_000_000f):0.#}M",                          // Million
                >= 1_000 => $"{(value / 1_000f):0.#}K",                                  // Thousand
                _ => value.ToString("0.#")
            };
        }
    }
}
using System;
using System.Collections.Generic;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random
{
    /// <summary>
    /// Convenience gameplay APIs built on top of IRng.
    /// </summary>
    public static class RngExtensions
    {
        /// <summary>
        /// Fisher–Yates shuffle (in-place), deterministic.
        /// </summary>
        public static void Shuffle<T>(this IRng rng, IList<T> list)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (list == null) throw new ArgumentNullException(nameof(list));

            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                if (j == i) continue;

                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>
        /// Picks one element uniformly.
        /// </summary>
        public static T Pick<T>(this IRng rng, IReadOnlyList<T> list)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (list.Count == 0) throw new ArgumentException("List must not be empty.", nameof(list));

            return list[rng.NextInt(list.Count)];
        }

        /// <summary>
        /// Picks an index based on weights (non-negative). Uses cumulative selection.
        /// Deterministic given same weights and RNG state.
        /// </summary>
        public static int PickWeightedIndex(this IRng rng, IReadOnlyList<float> weights)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (weights.Count == 0) throw new ArgumentException("Weights must not be empty.", nameof(weights));

            double sum = 0d;
            for (int i = 0; i < weights.Count; i++)
            {
                float w = weights[i];
                if (w < 0f) throw new ArgumentOutOfRangeException(nameof(weights), "Weights must be non-negative.");
                sum += w;
            }

            if (sum <= 0d)
                // All zero weights: fallback to uniform.
                return rng.NextInt(weights.Count);

            double r = rng.NextDouble01() * sum;
            double acc = 0d;

            for (int i = 0; i < weights.Count; i++)
            {
                acc += weights[i];
                if (r < acc) return i;
            }

            // Numerical edge case: return last.
            return weights.Count - 1;
        }

        /// <summary>
        /// Picks an element based on weights (non-negative).
        /// </summary>
        public static T PickWeighted<T>(this IRng rng, IReadOnlyList<T> items, IReadOnlyList<float> weights)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (items.Count != weights.Count)
                throw new ArgumentException("Items and weights must have the same length.");

            int idx = rng.PickWeightedIndex(weights);
            return items[idx];
        }

        /// <summary>
        /// Returns a normally distributed value using Box-Muller transform.
        /// Deterministic and suitable for gameplay (spread, recoil, noise).
        /// </summary>
        public static double NextGaussian(this IRng rng, double mean = 0d, double stdDev = 1d)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (stdDev < 0d) throw new ArgumentOutOfRangeException(nameof(stdDev), "stdDev must be non-negative.");

            // u1 in (0,1], u2 in [0,1)
            double u1 = 1.0 - rng.NextDouble01();
            double u2 = rng.NextDouble01();

            double radius = Math.Sqrt(-2.0 * Math.Log(u1));
            double theta = 2.0 * Math.PI * u2;

            double z = radius * Math.Cos(theta);
            return mean + z * stdDev;
        }

        /// <summary>
        /// Returns a value in [0,1) biased toward 0 (power curve).
        /// power > 1 biases toward 0, power < 1 biases toward 1.
        /// </summary>
        public static float NextPower01(this IRng rng, float power)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (power <= 0f) throw new ArgumentOutOfRangeException(nameof(power), "power must be > 0.");

            float u = rng.NextFloat01();
            return (float)Math.Pow(u, power);
        }

        /// <summary>
        /// Rolls a dice with sides in [1..sides]. Returns [1..sides].
        /// </summary>
        public static int RollDice(this IRng rng, int sides)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (sides <= 0) throw new ArgumentOutOfRangeException(nameof(sides), "sides must be > 0.");

            return rng.NextIntInclusive(1, sides);
        }

        /// <summary>
        /// Rolls NdM (count dice with 'sides' sides). Returns sum.
        /// </summary>
        public static int RollDice(this IRng rng, int count, int sides)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 0.");
            if (sides <= 0) throw new ArgumentOutOfRangeException(nameof(sides), "sides must be > 0.");

            int sum = 0;
            for (int i = 0; i < count; i++)
            {
                sum += rng.RollDice(sides);
            }

            return sum;
        }
    }
}
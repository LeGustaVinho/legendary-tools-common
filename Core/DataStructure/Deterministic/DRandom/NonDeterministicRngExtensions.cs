using System;
using System.Collections.Generic;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random.NonDeterministic
{
    /// <summary>
    /// Floating-point convenience APIs that are not guaranteed bit-identical across
    /// runtimes, CPUs, Mono and IL2CPP. Do not use for lockstep or authoritative simulation.
    /// </summary>
    public static class NonDeterministicRngExtensions
    {
        public static int PickWeightedIndex(this IRng rng, IReadOnlyList<float> weights)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            ValidateWeights(weights, out double sum);
            if (sum <= 0d) return rng.NextInt(weights.Count);
            return FindWeightedIndex(weights, rng.NextDouble01() * sum);
        }

        public static int PickWeightedIndex(
            this ref DeterministicRng rng,
            IReadOnlyList<float> weights)
        {
            ValidateWeights(weights, out double sum);
            if (sum <= 0d) return rng.NextInt(weights.Count);
            return FindWeightedIndex(weights, rng.NextDouble01() * sum);
        }

        public static T PickWeighted<T>(
            this IRng rng,
            IReadOnlyList<T> items,
            IReadOnlyList<float> weights)
        {
            ValidateItemsAndWeights(items, weights);
            return items[rng.PickWeightedIndex(weights)];
        }

        public static T PickWeighted<T>(
            this ref DeterministicRng rng,
            IReadOnlyList<T> items,
            IReadOnlyList<float> weights)
        {
            ValidateItemsAndWeights(items, weights);
            return items[rng.PickWeightedIndex(weights)];
        }

        public static double NextGaussian(this IRng rng, double mean = 0d, double stdDev = 1d)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            return NextGaussianCore(rng.NextDouble01(), rng.NextDouble01(), mean, stdDev);
        }

        public static double NextGaussian(
            this ref DeterministicRng rng,
            double mean = 0d,
            double stdDev = 1d)
        {
            return NextGaussianCore(rng.NextDouble01(), rng.NextDouble01(), mean, stdDev);
        }

        public static float NextPower01(this IRng rng, float power)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            return NextPower01Core(rng.NextFloat01(), power);
        }

        public static float NextPower01(this ref DeterministicRng rng, float power)
        {
            return NextPower01Core(rng.NextFloat01(), power);
        }

        private static double NextGaussianCore(double first, double second, double mean, double stdDev)
        {
            if (stdDev < 0d) throw new ArgumentOutOfRangeException(nameof(stdDev), "stdDev must be non-negative.");

            double radius = Math.Sqrt(-2.0 * Math.Log(1.0 - first));
            double theta = 2.0 * Math.PI * second;
            return mean + radius * Math.Cos(theta) * stdDev;
        }

        private static float NextPower01Core(float value, float power)
        {
            if (power <= 0f) throw new ArgumentOutOfRangeException(nameof(power), "power must be > 0.");
            return (float)Math.Pow(value, power);
        }

        private static void ValidateWeights(IReadOnlyList<float> weights, out double sum)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (weights.Count == 0) throw new ArgumentException("Weights must not be empty.", nameof(weights));

            sum = 0d;
            for (int i = 0; i < weights.Count; i++)
            {
                float weight = weights[i];
                if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f)
                    throw new ArgumentOutOfRangeException(
                        nameof(weights),
                        "Weights must be finite and non-negative.");
                sum += weight;
            }
        }

        private static int FindWeightedIndex(IReadOnlyList<float> weights, double roll)
        {
            double cumulative = 0d;
            for (int i = 0; i < weights.Count; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative) return i;
            }

            return weights.Count - 1;
        }

        private static void ValidateItemsAndWeights<T>(
            IReadOnlyList<T> items,
            IReadOnlyList<float> weights)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (items.Count != weights.Count)
                throw new ArgumentException("Items and weights must have the same length.");
        }
    }
}

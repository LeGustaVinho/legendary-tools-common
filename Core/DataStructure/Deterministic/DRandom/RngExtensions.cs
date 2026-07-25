using System;
using System.Collections.Generic;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random
{
    /// <summary>
    /// Integer-only convenience APIs suitable for deterministic simulation.
    /// </summary>
    public static class RngExtensions
    {
        public static void Shuffle<T>(this ref DeterministicRng rng, IList<T> list)
        {
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
        /// Interface overload intended for reference-type IRng implementations or a
        /// deliberately persistent boxed instance.
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

        public static T Pick<T>(this ref DeterministicRng rng, IReadOnlyList<T> list)
        {
            ValidateList(list);
            return list[rng.NextInt(list.Count)];
        }

        public static T Pick<T>(this IRng rng, IReadOnlyList<T> list)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            ValidateList(list);
            return list[rng.NextInt(list.Count)];
        }

        /// <summary>
        /// Picks an index from integer weights using only integer arithmetic.
        /// </summary>
        public static int PickWeightedIndex(this ref DeterministicRng rng, IReadOnlyList<ulong> weights)
        {
            ulong sum = SumWeights(weights);
            if (sum == 0UL) return rng.NextInt(weights.Count);

            return FindWeightedIndex(weights, NextULongBounded(ref rng, sum));
        }

        public static int PickWeightedIndex(this IRng rng, IReadOnlyList<ulong> weights)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            ulong sum = SumWeights(weights);
            if (sum == 0UL) return rng.NextInt(weights.Count);

            return FindWeightedIndex(weights, NextULongBounded(rng, sum));
        }

        public static T PickWeighted<T>(
            this ref DeterministicRng rng,
            IReadOnlyList<T> items,
            IReadOnlyList<ulong> weights)
        {
            ValidateItemsAndWeights(items, weights);
            return items[rng.PickWeightedIndex(weights)];
        }

        public static T PickWeighted<T>(
            this IRng rng,
            IReadOnlyList<T> items,
            IReadOnlyList<ulong> weights)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            ValidateItemsAndWeights(items, weights);
            return items[rng.PickWeightedIndex(weights)];
        }

        public static int RollDice(this ref DeterministicRng rng, int sides)
        {
            if (sides <= 0) throw new ArgumentOutOfRangeException(nameof(sides), "sides must be > 0.");
            return rng.NextIntInclusive(1, sides);
        }

        public static int RollDice(this IRng rng, int sides)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (sides <= 0) throw new ArgumentOutOfRangeException(nameof(sides), "sides must be > 0.");
            return rng.NextIntInclusive(1, sides);
        }

        public static int RollDice(this ref DeterministicRng rng, int count, int sides)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 0.");
            if (sides <= 0) throw new ArgumentOutOfRangeException(nameof(sides), "sides must be > 0.");

            int sum = 0;
            for (int i = 0; i < count; i++)
                sum = checked(sum + rng.RollDice(sides));

            return sum;
        }

        public static int RollDice(this IRng rng, int count, int sides)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 0.");
            if (sides <= 0) throw new ArgumentOutOfRangeException(nameof(sides), "sides must be > 0.");

            int sum = 0;
            for (int i = 0; i < count; i++)
                sum = checked(sum + rng.RollDice(sides));

            return sum;
        }

        private static void ValidateList<T>(IReadOnlyList<T> list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (list.Count == 0) throw new ArgumentException("List must not be empty.", nameof(list));
        }

        private static ulong SumWeights(IReadOnlyList<ulong> weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (weights.Count == 0) throw new ArgumentException("Weights must not be empty.", nameof(weights));

            ulong sum = 0UL;
            for (int i = 0; i < weights.Count; i++)
                sum = checked(sum + weights[i]);

            return sum;
        }

        private static int FindWeightedIndex(IReadOnlyList<ulong> weights, ulong roll)
        {
            ulong cumulative = 0UL;
            for (int i = 0; i < weights.Count; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative) return i;
            }

            throw new InvalidOperationException("Weighted selection failed.");
        }

        private static ulong NextULongBounded(ref DeterministicRng rng, ulong bound)
        {
            ulong threshold = unchecked(0UL - bound) % bound;
            while (true)
            {
                ulong value = rng.NextULong();
                if (value >= threshold) return value % bound;
            }
        }

        private static ulong NextULongBounded(IRng rng, ulong bound)
        {
            ulong threshold = unchecked(0UL - bound) % bound;
            while (true)
            {
                ulong value = rng.NextULong();
                if (value >= threshold) return value % bound;
            }
        }

        private static void ValidateItemsAndWeights<T>(
            IReadOnlyList<T> items,
            IReadOnlyList<ulong> weights)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (items.Count != weights.Count)
                throw new ArgumentException("Items and weights must have the same length.");
        }
    }
}

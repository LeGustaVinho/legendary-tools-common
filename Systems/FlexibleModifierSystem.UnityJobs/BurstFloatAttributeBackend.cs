using System;
using DeterministicFixedPoint;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.ModifierSystem.UnityJobs
{
    /// <summary>
    /// Blittable contribution data consumed by the Burst backend. The caller
    /// supplies contributions in the same deterministic operation/priority/
    /// sequence order used by the managed runtime.
    /// </summary>
    public struct BurstFixedContribution
    {
        public ModifierOperation Operation;
        public int MagnitudeRaw;
        public byte Active;
    }

    [BurstCompile]
    public struct BurstFixedAttributeJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> BaseValuesRaw;
        [ReadOnly] public NativeArray<int> ContributionStarts;
        [ReadOnly] public NativeArray<int> ContributionCounts;
        [ReadOnly] public NativeArray<BurstFixedContribution> Contributions;
        [WriteOnly] public NativeArray<int> ResultsRaw;
        public DetOverflowMode OverflowMode;

        public void Execute(int index)
        {
            int value = BaseValuesRaw[index];
            int start = ContributionStarts[index];
            int end = start + ContributionCounts[index];
            for (int contributionIndex = start; contributionIndex < end; contributionIndex++)
            {
                BurstFixedContribution contribution = Contributions[contributionIndex];
                if (contribution.Active == 0) continue;
                switch (contribution.Operation)
                {
                    case ModifierOperation.Add:
                        value = ApplyRange((long)value + contribution.MagnitudeRaw);
                        break;
                    case ModifierOperation.Multiply:
                        value = ApplyRange(RoundDivNearestAwayFromZero(
                            (long)value * contribution.MagnitudeRaw, DetS32.Scale));
                        break;
                    case ModifierOperation.Replace:
                        value = contribution.MagnitudeRaw;
                        break;
                    case ModifierOperation.ClampMinimum:
                    case ModifierOperation.Maximum:
                        if (value < contribution.MagnitudeRaw) value = contribution.MagnitudeRaw;
                        break;
                    case ModifierOperation.ClampMaximum:
                    case ModifierOperation.Minimum:
                        if (value > contribution.MagnitudeRaw) value = contribution.MagnitudeRaw;
                        break;
                    default:
                        // Custom managed policies intentionally stay outside Burst jobs.
                        break;
                }
            }
            ResultsRaw[index] = value;
        }

        private int ApplyRange(long value)
        {
            if (OverflowMode == DetOverflowMode.Wrap) return unchecked((int)value);
            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;
            return (int)value;
        }

        private static long RoundDivNearestAwayFromZero(long numerator, long denominator)
        {
            long quotient = numerator / denominator;
            long remainder = numerator % denominator;
            if (remainder == 0) return quotient;
            ulong absoluteRemainder = (ulong)(remainder < 0 ? -remainder : remainder);
            if (absoluteRemainder * 2UL >= (ulong)denominator)
                quotient += numerator > 0 ? 1L : -1L;
            return quotient;
        }
    }

    public static class BurstFixedAttributeBackend
    {
        public static JobHandle Schedule(NativeArray<int> baseValuesRaw,
            NativeArray<int> contributionStarts,
            NativeArray<int> contributionCounts,
            NativeArray<BurstFixedContribution> contributions,
            NativeArray<int> resultsRaw,
            DetOverflowMode overflowMode,
            int innerLoopBatchCount = 64,
            JobHandle dependency = default)
        {
            if (!baseValuesRaw.IsCreated)
                throw new ArgumentException("Base values are required.", nameof(baseValuesRaw));
            if (!contributionStarts.IsCreated || contributionStarts.Length != baseValuesRaw.Length)
                throw new ArgumentException("One contribution start is required per attribute.",
                    nameof(contributionStarts));
            if (!contributionCounts.IsCreated || contributionCounts.Length != baseValuesRaw.Length)
                throw new ArgumentException("One contribution count is required per attribute.",
                    nameof(contributionCounts));
            if (!resultsRaw.IsCreated || resultsRaw.Length != baseValuesRaw.Length)
                throw new ArgumentException("The result buffer must match the base-value buffer.",
                    nameof(resultsRaw));
            if (!contributions.IsCreated)
                throw new ArgumentException("The contribution buffer is required.", nameof(contributions));
            if (innerLoopBatchCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(innerLoopBatchCount));

            for (int index = 0; index < contributionStarts.Length; index++)
            {
                int start = contributionStarts[index];
                int count = contributionCounts[index];
                if (start < 0 || count < 0 || start > contributions.Length - count)
                    throw new ArgumentException(
                        $"Contribution range {index} is outside the contribution buffer.");
            }

            return new BurstFixedAttributeJob
            {
                BaseValuesRaw = baseValuesRaw,
                ContributionStarts = contributionStarts,
                ContributionCounts = contributionCounts,
                Contributions = contributions,
                ResultsRaw = resultsRaw,
                OverflowMode = overflowMode
            }.Schedule(baseValuesRaw.Length, innerLoopBatchCount, dependency);
        }
    }
}

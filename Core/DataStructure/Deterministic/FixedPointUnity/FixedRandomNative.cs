using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Caller owns Cumulative and disposes it after dependent jobs. Keep its contents immutable.</summary>
    public readonly struct FixedRandomNativeWeights
    {
        [ReadOnly] public readonly NativeArray<ulong> Cumulative;
        public readonly int Count;
        public readonly ulong Total;
        internal FixedRandomNativeWeights(NativeArray<ulong> cumulative, FixedRandomWeightedTable view)
        { Cumulative = cumulative; Count = view.Count; Total = view.Total; }
    }

    /// <summary>Native buffer adapters; scalar kernels are shared with managed. Scheduling belongs to the caller.</summary>
    public static unsafe partial class FixedRandomNative
    {
        public static bool TryFillBytes(ref FixedRandom random, NativeArray<byte> output, out FixedStatus status)
        {
            status = output.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryFillBytes((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(output), output.Length, out status);
        }
        public static bool TryChoose<T>(ref FixedRandom random, NativeArray<T> values, out T value, out FixedStatus status) where T : unmanaged
        {
            value = default; status = values.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryChoose((T*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(values), values.Length, out value, out status);
        }
        public static bool TryShuffle<T>(ref FixedRandom random, NativeArray<T> values, out FixedStatus status) where T : unmanaged
        {
            status = values.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryShuffle((T*)NativeArrayUnsafeUtility.GetUnsafePtr(values), values.Length, out status);
        }
        public static bool TrySampleWithoutReplacement(ref FixedRandom random, int populationCount, NativeArray<int> output, NativeArray<int> scratch, out FixedStatus status)
        {
            status = output.IsCreated && scratch.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TrySampleWithoutReplacement(populationCount, (int*)NativeArrayUnsafeUtility.GetUnsafePtr(output), output.Length,
                (int*)NativeArrayUnsafeUtility.GetUnsafePtr(scratch), scratch.Length, out status);
        }
        public static bool TryWeightedIndex(ref FixedRandom random, FixedRandomNativeWeights table, out int index, out FixedStatus status)
        {
            index = -1; status = table.Cumulative.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            if (!status.IsSuccess()) return false;
            var view = new FixedRandomWeightedTable((ulong*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(table.Cumulative), table.Count, table.Total);
            return random.TryWeightedIndex(view, out index, out status);
        }
    }
}

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
namespace LegendaryTools.FixedPoint
{
    public static unsafe partial class FixedRandomNative
    {
        public static bool TryFill(ref FixedRandom random, NativeArray<Fixed32> output, out FixedStatus status)
        {
            status = output.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryFill((Fixed32*)NativeArrayUnsafeUtility.GetUnsafePtr(output), output.Length, out status);
        }
        public static bool TryWeightedIndex(ref FixedRandom random, NativeArray<Fixed32> weights, out int index, out FixedStatus status)
        {
            index = -1; status = weights.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryWeightedIndex((Fixed32*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(weights), weights.Length, out index, out status);
        }
        public static bool TryBuildWeights(NativeArray<Fixed32> weights, NativeArray<ulong> cumulative, out FixedRandomNativeWeights table, out FixedStatus status)
        {
            table = default; status = weights.IsCreated && cumulative.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            if (!status.IsSuccess()) return false;
            bool ok = FixedRandomWeights.TryBuild((Fixed32*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(weights), weights.Length,
                (ulong*)NativeArrayUnsafeUtility.GetUnsafePtr(cumulative), cumulative.Length, out FixedRandomWeightedTable view, out status);
            if (ok) table = new FixedRandomNativeWeights(cumulative, view);
            return ok;
        }
        public static bool TryFill(ref FixedRandom random, NativeArray<UFixed32> output, out FixedStatus status)
        {
            status = output.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryFill((UFixed32*)NativeArrayUnsafeUtility.GetUnsafePtr(output), output.Length, out status);
        }
        public static bool TryWeightedIndex(ref FixedRandom random, NativeArray<UFixed32> weights, out int index, out FixedStatus status)
        {
            index = -1; status = weights.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryWeightedIndex((UFixed32*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(weights), weights.Length, out index, out status);
        }
        public static bool TryBuildWeights(NativeArray<UFixed32> weights, NativeArray<ulong> cumulative, out FixedRandomNativeWeights table, out FixedStatus status)
        {
            table = default; status = weights.IsCreated && cumulative.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            if (!status.IsSuccess()) return false;
            bool ok = FixedRandomWeights.TryBuild((UFixed32*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(weights), weights.Length,
                (ulong*)NativeArrayUnsafeUtility.GetUnsafePtr(cumulative), cumulative.Length, out FixedRandomWeightedTable view, out status);
            if (ok) table = new FixedRandomNativeWeights(cumulative, view);
            return ok;
        }
        public static bool TryFill(ref FixedRandom random, NativeArray<Fixed64> output, out FixedStatus status)
        {
            status = output.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryFill((Fixed64*)NativeArrayUnsafeUtility.GetUnsafePtr(output), output.Length, out status);
        }
        public static bool TryWeightedIndex(ref FixedRandom random, NativeArray<Fixed64> weights, out int index, out FixedStatus status)
        {
            index = -1; status = weights.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryWeightedIndex((Fixed64*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(weights), weights.Length, out index, out status);
        }
        public static bool TryBuildWeights(NativeArray<Fixed64> weights, NativeArray<ulong> cumulative, out FixedRandomNativeWeights table, out FixedStatus status)
        {
            table = default; status = weights.IsCreated && cumulative.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            if (!status.IsSuccess()) return false;
            bool ok = FixedRandomWeights.TryBuild((Fixed64*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(weights), weights.Length,
                (ulong*)NativeArrayUnsafeUtility.GetUnsafePtr(cumulative), cumulative.Length, out FixedRandomWeightedTable view, out status);
            if (ok) table = new FixedRandomNativeWeights(cumulative, view);
            return ok;
        }
        public static bool TryFill(ref FixedRandom random, NativeArray<UFixed64> output, out FixedStatus status)
        {
            status = output.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryFill((UFixed64*)NativeArrayUnsafeUtility.GetUnsafePtr(output), output.Length, out status);
        }
        public static bool TryWeightedIndex(ref FixedRandom random, NativeArray<UFixed64> weights, out int index, out FixedStatus status)
        {
            index = -1; status = weights.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            return status.IsSuccess() && random.TryWeightedIndex((UFixed64*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(weights), weights.Length, out index, out status);
        }
        public static bool TryBuildWeights(NativeArray<UFixed64> weights, NativeArray<ulong> cumulative, out FixedRandomNativeWeights table, out FixedStatus status)
        {
            table = default; status = weights.IsCreated && cumulative.IsCreated ? FixedStatus.None : FixedStatus.InvalidDomain;
            if (!status.IsSuccess()) return false;
            bool ok = FixedRandomWeights.TryBuild((UFixed64*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(weights), weights.Length,
                (ulong*)NativeArrayUnsafeUtility.GetUnsafePtr(cumulative), cumulative.Length, out FixedRandomWeightedTable view, out status);
            if (ok) table = new FixedRandomNativeWeights(cumulative, view);
            return ok;
        }
    }
}

using System;
namespace LegendaryTools.FixedPoint
{
    public partial struct FixedRandom
    {
        public unsafe bool TryWeightedIndex(Fixed32* weights, int count, out int index, out FixedStatus status)
            => FixedRandomWeightsKernel.Select(ref this, weights, count, FixedKind.Signed32, FixedRuntime.State, out index, out status);
        public unsafe bool TryWeightedIndex(ReadOnlySpan<Fixed32> weights, out int index, out FixedStatus status)
        { fixed (Fixed32* pointer = weights) return TryWeightedIndex(pointer, weights.Length, out index, out status); }
        public int WeightedIndex(ReadOnlySpan<Fixed32> weights)
        { TryWeightedIndex(weights, out int index, out FixedStatus status); status.ThrowIfFailure(); return index; }
        public unsafe bool TryWeightedIndex(UFixed32* weights, int count, out int index, out FixedStatus status)
            => FixedRandomWeightsKernel.Select(ref this, weights, count, FixedKind.Unsigned32, FixedRuntime.State, out index, out status);
        public unsafe bool TryWeightedIndex(ReadOnlySpan<UFixed32> weights, out int index, out FixedStatus status)
        { fixed (UFixed32* pointer = weights) return TryWeightedIndex(pointer, weights.Length, out index, out status); }
        public int WeightedIndex(ReadOnlySpan<UFixed32> weights)
        { TryWeightedIndex(weights, out int index, out FixedStatus status); status.ThrowIfFailure(); return index; }
        public unsafe bool TryWeightedIndex(Fixed64* weights, int count, out int index, out FixedStatus status)
            => FixedRandomWeightsKernel.Select(ref this, weights, count, FixedKind.Signed64, FixedRuntime.State, out index, out status);
        public unsafe bool TryWeightedIndex(ReadOnlySpan<Fixed64> weights, out int index, out FixedStatus status)
        { fixed (Fixed64* pointer = weights) return TryWeightedIndex(pointer, weights.Length, out index, out status); }
        public int WeightedIndex(ReadOnlySpan<Fixed64> weights)
        { TryWeightedIndex(weights, out int index, out FixedStatus status); status.ThrowIfFailure(); return index; }
        public unsafe bool TryWeightedIndex(UFixed64* weights, int count, out int index, out FixedStatus status)
            => FixedRandomWeightsKernel.Select(ref this, weights, count, FixedKind.Unsigned64, FixedRuntime.State, out index, out status);
        public unsafe bool TryWeightedIndex(ReadOnlySpan<UFixed64> weights, out int index, out FixedStatus status)
        { fixed (UFixed64* pointer = weights) return TryWeightedIndex(pointer, weights.Length, out index, out status); }
        public int WeightedIndex(ReadOnlySpan<UFixed64> weights)
        { TryWeightedIndex(weights, out int index, out FixedStatus status); status.ThrowIfFailure(); return index; }
    }
    public static partial class FixedRandomWeights
    {
        public static unsafe bool TryBuild(Fixed32* weights, int count, ulong* cumulative, int capacity,
            out FixedRandomWeightedTable table, out FixedStatus status)
            => FixedRandomWeightsKernel.Build(weights, count, FixedKind.Signed32, FixedRuntime.State, cumulative, capacity, out table, out status);
        public static unsafe bool TryBuild(ReadOnlySpan<Fixed32> weights, Span<ulong> cumulative,
            out FixedRandomWeightView table, out FixedStatus status)
        {
            table = default;
            fixed (Fixed32* input = weights)
            fixed (ulong* output = cumulative)
            {
                bool ok = TryBuild(input, weights.Length, output, cumulative.Length, out FixedRandomWeightedTable native, out status);
                if (ok) table = new FixedRandomWeightView(cumulative.Slice(0, weights.Length), native.Total);
                return ok;
            }
        }
        public static unsafe bool TryBuild(UFixed32* weights, int count, ulong* cumulative, int capacity,
            out FixedRandomWeightedTable table, out FixedStatus status)
            => FixedRandomWeightsKernel.Build(weights, count, FixedKind.Unsigned32, FixedRuntime.State, cumulative, capacity, out table, out status);
        public static unsafe bool TryBuild(ReadOnlySpan<UFixed32> weights, Span<ulong> cumulative,
            out FixedRandomWeightView table, out FixedStatus status)
        {
            table = default;
            fixed (UFixed32* input = weights)
            fixed (ulong* output = cumulative)
            {
                bool ok = TryBuild(input, weights.Length, output, cumulative.Length, out FixedRandomWeightedTable native, out status);
                if (ok) table = new FixedRandomWeightView(cumulative.Slice(0, weights.Length), native.Total);
                return ok;
            }
        }
        public static unsafe bool TryBuild(Fixed64* weights, int count, ulong* cumulative, int capacity,
            out FixedRandomWeightedTable table, out FixedStatus status)
            => FixedRandomWeightsKernel.Build(weights, count, FixedKind.Signed64, FixedRuntime.State, cumulative, capacity, out table, out status);
        public static unsafe bool TryBuild(ReadOnlySpan<Fixed64> weights, Span<ulong> cumulative,
            out FixedRandomWeightView table, out FixedStatus status)
        {
            table = default;
            fixed (Fixed64* input = weights)
            fixed (ulong* output = cumulative)
            {
                bool ok = TryBuild(input, weights.Length, output, cumulative.Length, out FixedRandomWeightedTable native, out status);
                if (ok) table = new FixedRandomWeightView(cumulative.Slice(0, weights.Length), native.Total);
                return ok;
            }
        }
        public static unsafe bool TryBuild(UFixed64* weights, int count, ulong* cumulative, int capacity,
            out FixedRandomWeightedTable table, out FixedStatus status)
            => FixedRandomWeightsKernel.Build(weights, count, FixedKind.Unsigned64, FixedRuntime.State, cumulative, capacity, out table, out status);
        public static unsafe bool TryBuild(ReadOnlySpan<UFixed64> weights, Span<ulong> cumulative,
            out FixedRandomWeightView table, out FixedStatus status)
        {
            table = default;
            fixed (UFixed64* input = weights)
            fixed (ulong* output = cumulative)
            {
                bool ok = TryBuild(input, weights.Length, output, cumulative.Length, out FixedRandomWeightedTable native, out status);
                if (ok) table = new FixedRandomWeightView(cumulative.Slice(0, weights.Length), native.Total);
                return ok;
            }
        }
    }
}

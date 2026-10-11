using System;

namespace LegendaryTools.FixedPoint
{
    /// <summary>Validated caller-owned prefix buffer. Keep it alive and immutable while the view is in use.</summary>
    public readonly unsafe struct FixedRandomWeightedTable
    {
        internal readonly ulong* Cumulative;
        public readonly int Count;
        public readonly ulong Total;
        internal FixedRandomWeightedTable(ulong* cumulative, int count, ulong total)
        { Cumulative = cumulative; Count = count; Total = total; }
        public bool IsValid => Cumulative != null && Count > 0 && Total != 0;
    }

    /// <summary>Managed span view of a validated caller-owned prefix buffer; no pin survives the build call.</summary>
    public readonly ref struct FixedRandomWeightView
    {
        internal readonly ReadOnlySpan<ulong> Cumulative;
        public readonly ulong Total;
        public int Count => Cumulative.Length;
        public bool IsValid => Count > 0 && Total != 0;
        internal FixedRandomWeightView(ReadOnlySpan<ulong> cumulative, ulong total) { Cumulative = cumulative; Total = total; }
    }

    public static partial class FixedRandomWeights { }

    internal static unsafe class FixedRandomWeightsKernel
    {
        private static ulong Read(void* weights, int index, FixedKind kind) => kind < FixedKind.Signed64 ? ((uint*)weights)[index] : ((ulong*)weights)[index];
        private static bool Total(void* weights, int count, FixedKind kind, FixedRuntimeState state, out ulong total, out FixedStatus status)
        {
            total = 0;
            status = !state.IsInitialized ? FixedStatus.Uninitialized : count <= 0 || weights == null ? FixedStatus.InvalidDomain : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            var f = new FixedFormat(kind, state);
            for (int i = 0; i < count; i++)
            {
                var value = f.Decode(Read(weights, i, kind));
                if (!value.IsFinite || value.Negative && !value.IsZero) { status = FixedStatus.InvalidDomain; return false; }
                if (value.Magnitude > ulong.MaxValue - total) { status = FixedStatus.Overflow; return false; }
                total += value.Magnitude;
            }
            if (total == 0) { status = FixedStatus.InvalidDomain; return false; }
            return true;
        }
        internal static bool Build(void* weights, int count, FixedKind kind, FixedRuntimeState state, ulong* cumulative, int capacity,
            out FixedRandomWeightedTable table, out FixedStatus status)
        {
            table = default;
            status = capacity < count || capacity < 0 || cumulative == null ? FixedStatus.BufferTooSmall : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            if (!Total(weights, count, kind, state, out ulong total, out status)) return false;
            ulong input = (ulong)weights, output = (ulong)cumulative;
            ulong inputBytes = (ulong)count * (kind < FixedKind.Signed64 ? 4UL : 8UL), outputBytes = (ulong)count * 8;
            if (input <= output ? output - input < inputBytes : input - output < outputBytes)
            { status = FixedStatus.InvalidDomain; return false; }
            var f = new FixedFormat(kind, state); ulong sum = 0;
            for (int i = 0; i < count; i++) { sum += f.Decode(Read(weights, i, kind)).Magnitude; cumulative[i] = sum; }
            table = new FixedRandomWeightedTable(cumulative, count, total); return true;
        }
        internal static bool Select(ref FixedRandom random, void* weights, int count, FixedKind kind, FixedRuntimeState state,
            out int index, out FixedStatus status)
        {
            index = -1;
            if (!Total(weights, count, kind, state, out ulong total, out status)) return false;
            FixedRandom trial = random;
            if (!FixedRandomKernel.Bounded(ref trial, total, out ulong sample, out status)) return false;
            var f = new FixedFormat(kind, state); ulong sum = 0;
            for (int i = 0; i < count; i++)
            { sum += f.Decode(Read(weights, i, kind)).Magnitude; if (sample < sum) { index = i; random = trial; return true; } }
            status = FixedStatus.InvalidDomain; return false;
        }
        internal static bool Select(ref FixedRandom random, ulong* cumulative, int count, ulong total, out int index, out FixedStatus status)
        {
            index = -1; status = cumulative == null || count <= 0 || total == 0 ? FixedStatus.InvalidDomain : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            FixedRandom trial = random;
            if (!FixedRandomKernel.Bounded(ref trial, total, out ulong sample, out status)) return false;
            int lo = 0, hi = count;
            while (lo < hi) { int mid = lo + ((hi - lo) >> 1); if (sample < cumulative[mid]) hi = mid; else lo = mid + 1; }
            if (lo == count) { status = FixedStatus.InvalidDomain; return false; }
            index = lo; random = trial; return true;
        }
    }

    public partial struct FixedRandom
    {
        public unsafe bool TryWeightedIndex(FixedRandomWeightedTable table, out int index, out FixedStatus status)
            => FixedRandomWeightsKernel.Select(ref this, table.Cumulative, table.Count, table.Total, out index, out status);
        public unsafe bool TryWeightedIndex(FixedRandomWeightView table, out int index, out FixedStatus status)
        { fixed (ulong* pointer = table.Cumulative) return FixedRandomWeightsKernel.Select(ref this, pointer, table.Count, table.Total, out index, out status); }
        public int WeightedIndex(FixedRandomWeightView table)
        { TryWeightedIndex(table, out int index, out FixedStatus s); s.ThrowIfFailure(); return index; }
        public int WeightedIndex(FixedRandomWeightedTable table, out FixedStatus status)
        { TryWeightedIndex(table, out int index, out status); return index; }
    }
}

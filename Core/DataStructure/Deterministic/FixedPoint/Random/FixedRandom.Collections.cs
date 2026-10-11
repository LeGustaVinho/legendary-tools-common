using System;

namespace LegendaryTools.FixedPoint
{
    public partial struct FixedRandom
    {
        public bool TryPickIndex(int count, out int index, out FixedStatus status)
        {
            index = -1; status = count <= 0 ? FixedStatus.InvalidDomain : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            if (!TryRange(0, count, out int sample, out status)) return false;
            index = sample; return true;
        }
        public int PickIndex(int count) => PickIndex(count, out _);
        public int PickIndex(int count, out FixedStatus status) { TryPickIndex(count, out int index, out status); return index; }
        public int PickIndexChecked(int count) { int v = PickIndex(count, out FixedStatus s); s.ThrowIfFailure(); return v; }
        public bool TryChoose<T>(ReadOnlySpan<T> values, out T value, out FixedStatus status)
        {
            value = default;
            if (!TryPickIndex(values.Length, out int index, out status)) return false;
            value = values[index]; return true;
        }
        public T Choose<T>(ReadOnlySpan<T> values)
        { TryChoose(values, out T value, out FixedStatus s); s.ThrowIfFailure(); return value; }
        public unsafe bool TryChoose<T>(T* values, int count, out T value, out FixedStatus status) where T : unmanaged
        {
            value = default; status = count < 0 || values == null && count != 0 ? FixedStatus.InvalidDomain : FixedStatus.None;
            return status.IsSuccess() && TryChoose(new ReadOnlySpan<T>(values, count), out value, out status);
        }

        /// <summary>Fisher-Yates. A preflight pass guarantees failure leaves both the stream and collection untouched.</summary>
        public bool TryShuffle<T>(Span<T> values, out FixedStatus status)
        {
            status = FixedStatus.None; FixedRandom end = this;
            for (int i = values.Length - 1; i > 0; i--)
                if (!end.TryRangeInclusive(0, i, out int ignored, out status)) return false;
            FixedRandom replay = this;
            for (int i = values.Length - 1; i > 0; i--)
            { int j = replay.RangeInclusive(0, i); T temp = values[i]; values[i] = values[j]; values[j] = temp; }
            this = end; return true;
        }
        public void Shuffle<T>(Span<T> values) { TryShuffle(values, out FixedStatus s); s.ThrowIfFailure(); }
        public unsafe bool TryShuffle<T>(T* values, int count, out FixedStatus status) where T : unmanaged
        {
            status = count < 0 || values == null && count != 0 ? FixedStatus.InvalidDomain : FixedStatus.None;
            return status.IsSuccess() && TryShuffle(new Span<T>(values, count), out status);
        }

        /// <summary>Partial Fisher-Yates. Scratch needs populationCount ints and must not overlap output.</summary>
        public bool TrySampleWithoutReplacement(int populationCount, Span<int> output, Span<int> scratch, out FixedStatus status)
        {
            status = populationCount < 0 || output.Length > populationCount || scratch.Length < populationCount || output.Overlaps(scratch)
                ? FixedStatus.InvalidDomain : FixedStatus.None;
            if (!status.IsSuccess()) return false;
            FixedRandom end = this;
            for (int i = 0; i < output.Length; i++)
                if (!end.TryRange(i, populationCount, out int ignored, out status)) return false;
            if (output.Length == 0) return true;
            for (int i = 0; i < populationCount; i++) scratch[i] = i;
            FixedRandom replay = this;
            for (int i = 0; i < output.Length; i++)
            {
                int j = replay.Range(i, populationCount), temp = scratch[i]; scratch[i] = scratch[j]; scratch[j] = temp;
                output[i] = scratch[i];
            }
            this = end; return true;
        }
        public void SampleWithoutReplacement(int populationCount, Span<int> output, Span<int> scratch)
        { TrySampleWithoutReplacement(populationCount, output, scratch, out FixedStatus s); s.ThrowIfFailure(); }
        public unsafe bool TrySampleWithoutReplacement(int populationCount, int* output, int count, int* scratch, int scratchCount, out FixedStatus status)
        {
            status = count < 0 || scratchCount < 0 || output == null && count != 0 || scratch == null && scratchCount != 0
                ? FixedStatus.InvalidDomain : FixedStatus.None;
            return status.IsSuccess() && TrySampleWithoutReplacement(populationCount, new Span<int>(output, count), new Span<int>(scratch, scratchCount), out status);
        }
    }
}

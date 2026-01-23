using System;
using System.Collections.Generic;

namespace LegendaryTools.Common.Core.Patterns.ECS.Parallel
{
    /// <summary>
    /// Managed deterministic reduction helper.
    /// Each worker writes to a local buffer. Merge is ordered and stable.
    /// </summary>
    /// <typeparam name="T">Reduction item type.</typeparam>
    public sealed class DeterministicReduction<T>
    {
        private sealed class WorkerBuffer
        {
            public readonly List<T> Items;

            public WorkerBuffer(int initialCapacity)
            {
                Items = new List<T>(Math.Max(0, initialCapacity));
            }

            public void Clear()
            {
                Items.Clear();
            }
        }

        private WorkerBuffer[] _workers;
        private int _workerCount;

        public DeterministicReduction(int workerCount, int initialCapacityPerWorker = 8)
        {
            EnsureWorkers(workerCount, initialCapacityPerWorker);
        }

        public int WorkerCount => _workerCount;

        public void EnsureWorkers(int workerCount, int initialCapacityPerWorker = 8)
        {
            if (workerCount < 1) workerCount = 1;
            if (initialCapacityPerWorker < 0) initialCapacityPerWorker = 0;

            if (_workers != null && _workerCount == workerCount)
                return;

            _workerCount = workerCount;
            _workers = new WorkerBuffer[_workerCount];

            for (int i = 0; i < _workerCount; i++)
            {
                _workers[i] = new WorkerBuffer(initialCapacityPerWorker);
            }
        }

        public void Clear()
        {
            if (_workers == null) return;

            for (int i = 0; i < _workerCount; i++)
            {
                _workers[i].Clear();
            }
        }

        public void Add(int worker, in T item)
        {
            if ((uint)worker >= (uint)_workerCount)
                throw new ArgumentOutOfRangeException(nameof(worker));

            _workers[worker].Items.Add(item);
        }

        /// <summary>
        /// Deterministically merges all worker buffers into output.
        /// If comparer is provided, output will be stably sorted by comparer, tie-broken by emission order.
        /// </summary>
        public void MergeAndSort(List<T> output, IComparer<T> comparer)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));

            output.Clear();

            if (_workers == null || _workerCount <= 0)
                return;

            // Deterministic emission order = worker index, then per-worker insertion order.
            int total = 0;
            for (int w = 0; w < _workerCount; w++)
            {
                total += _workers[w].Items.Count;
            }

            if (output.Capacity < total) output.Capacity = total;

            if (comparer == null)
            {
                for (int w = 0; w < _workerCount; w++)
                {
                    List<T> items = _workers[w].Items;
                    for (int i = 0; i < items.Count; i++)
                    {
                        output.Add(items[i]);
                    }
                }

                return;
            }

            // Stable sort: add seq and tie-break by seq.
            Entry[] tmp = new Entry[total];
            int write = 0;
            int seq = 0;

            for (int w = 0; w < _workerCount; w++)
            {
                List<T> items = _workers[w].Items;
                for (int i = 0; i < items.Count; i++)
                {
                    tmp[write++] = new Entry(items[i], seq++);
                }
            }

            Array.Sort(tmp, 0, write, new EntryComparer(comparer));

            for (int i = 0; i < write; i++)
            {
                output.Add(tmp[i].Value);
            }
        }

        private readonly struct Entry
        {
            public readonly T Value;
            public readonly int Seq;

            public Entry(T value, int seq)
            {
                Value = value;
                Seq = seq;
            }
        }

        private sealed class EntryComparer : IComparer<Entry>
        {
            private readonly IComparer<T> _valueComparer;

            public EntryComparer(IComparer<T> valueComparer)
            {
                _valueComparer = valueComparer;
            }

            public int Compare(Entry x, Entry y)
            {
                int cmp = _valueComparer.Compare(x.Value, y.Value);
                if (cmp != 0) return cmp;

                return x.Seq.CompareTo(y.Seq);
            }
        }
    }
}
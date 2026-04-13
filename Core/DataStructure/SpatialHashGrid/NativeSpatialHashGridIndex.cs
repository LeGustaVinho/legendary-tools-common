using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools
{
    internal readonly struct SpatialHashIndexBuildItem
    {
        public readonly int StableId;
        public readonly int Level;
        public readonly int OccupiedCellCount;
        public readonly bool IsOverflow;
        public readonly SpatialHashQuantizedAabb QuantizedBounds;

        public SpatialHashIndexBuildItem(
            int stableId,
            int level,
            int occupiedCellCount,
            bool isOverflow,
            SpatialHashQuantizedAabb quantizedBounds)
        {
            StableId = stableId;
            Level = level;
            OccupiedCellCount = occupiedCellCount;
            IsOverflow = isOverflow;
            QuantizedBounds = quantizedBounds;
        }
    }

    internal sealed class NativeSpatialHashGridIndex : IDisposable
    {
        private NativeList<SpatialHashCellPair> cellPairs;
        private NativeList<SpatialHashCellRange> cellRanges;
        private NativeList<int> overflowStableIds;
        private readonly SpatialHashGridDimension dimension;

        public NativeSpatialHashGridIndex(SpatialHashGridDimension dimension)
        {
            this.dimension = dimension;
            cellPairs = new NativeList<SpatialHashCellPair>(Allocator.Persistent);
            cellRanges = new NativeList<SpatialHashCellRange>(Allocator.Persistent);
            overflowStableIds = new NativeList<int>(Allocator.Persistent);
        }

        public int PairCount => cellPairs.IsCreated ? cellPairs.Length : 0;
        public int CellCount => cellRanges.IsCreated ? cellRanges.Length : 0;
        public int OverflowCount => overflowStableIds.IsCreated ? overflowStableIds.Length : 0;

        public void Rebuild(SpatialHashIndexBuildItem[] buildItems)
        {
            EnsureCreated();
            cellPairs.Clear();
            cellRanges.Clear();
            overflowStableIds.Clear();

            if (buildItems == null || buildItems.Length == 0)
            {
                return;
            }

            int overflowCount = 0;
            int totalPairCount = 0;
            for (int i = 0; i < buildItems.Length; i++)
            {
                if (buildItems[i].IsOverflow)
                {
                    overflowCount++;
                }
                else
                {
                    totalPairCount += buildItems[i].OccupiedCellCount;
                }
            }

            if (overflowCount > 0)
            {
                overflowStableIds.ResizeUninitialized(overflowCount);
                int overflowIndex = 0;
                for (int i = 0; i < buildItems.Length; i++)
                {
                    if (buildItems[i].IsOverflow)
                    {
                        overflowStableIds[overflowIndex++] = buildItems[i].StableId;
                    }
                }
            }

            if (totalPairCount == 0)
            {
                return;
            }

            NativeArray<SpatialHashIndexBuildItem> nativeBuildItems = new(buildItems, Allocator.TempJob);
            NativeArray<int> offsets = new(buildItems.Length, Allocator.TempJob);

            try
            {
                int runningOffset = 0;
                for (int i = 0; i < buildItems.Length; i++)
                {
                    offsets[i] = runningOffset;
                    runningOffset += buildItems[i].IsOverflow ? 0 : buildItems[i].OccupiedCellCount;
                }

                cellPairs.ResizeUninitialized(totalPairCount);

                GenerateCellPairsJob generateCellPairsJob = new()
                {
                    BuildItems = nativeBuildItems,
                    Offsets = offsets,
                    CellPairs = cellPairs.AsArray(),
                    Dimension = dimension
                };

                JobHandle jobHandle = generateCellPairsJob.Schedule(buildItems.Length, 32);
                jobHandle.Complete();

                cellPairs.AsArray().Sort();
                CompactRanges();
            }
            finally
            {
                if (offsets.IsCreated)
                {
                    offsets.Dispose();
                }

                if (nativeBuildItems.IsCreated)
                {
                    nativeBuildItems.Dispose();
                }
            }
        }

        public void CollectCandidates(SpatialHashQuantizedAabb queryBounds, int levelCount, List<int> results, Dictionary<int, int> queryMarks, int queryToken)
        {
            NativeArray<SpatialHashCellRange> ranges = cellRanges.AsArray();
            NativeArray<SpatialHashCellPair> pairs = cellPairs.AsArray();

            for (int level = 0; level < levelCount; level++)
            {
                queryBounds.GetCellRange(level, dimension, out int minX, out int maxX, out int minY, out int maxY, out int minZ, out int maxZ);

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            SpatialHashCellKey key = new(level, x, y, z);
                            int rangeIndex = BinarySearchRange(ranges, key);
                            if (rangeIndex < 0)
                            {
                                continue;
                            }

                            SpatialHashCellRange range = ranges[rangeIndex];
                            int end = range.StartIndex + range.Count;
                            for (int i = range.StartIndex; i < end; i++)
                            {
                                int stableId = pairs[i].StableId;
                                if (queryMarks.TryGetValue(stableId, out int token) && token == queryToken)
                                {
                                    continue;
                                }

                                queryMarks[stableId] = queryToken;
                                results.Add(stableId);
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < overflowStableIds.Length; i++)
            {
                int stableId = overflowStableIds[i];
                if (queryMarks.TryGetValue(stableId, out int token) && token == queryToken)
                {
                    continue;
                }

                queryMarks[stableId] = queryToken;
                results.Add(stableId);
            }
        }

        public void Clear()
        {
            EnsureCreated();
            cellPairs.Clear();
            cellRanges.Clear();
            overflowStableIds.Clear();
        }

        public void Dispose()
        {
            if (cellPairs.IsCreated)
            {
                cellPairs.Dispose();
            }

            if (cellRanges.IsCreated)
            {
                cellRanges.Dispose();
            }

            if (overflowStableIds.IsCreated)
            {
                overflowStableIds.Dispose();
            }
        }

        private void EnsureCreated()
        {
            if (!cellPairs.IsCreated)
            {
                cellPairs = new NativeList<SpatialHashCellPair>(Allocator.Persistent);
            }

            if (!cellRanges.IsCreated)
            {
                cellRanges = new NativeList<SpatialHashCellRange>(Allocator.Persistent);
            }

            if (!overflowStableIds.IsCreated)
            {
                overflowStableIds = new NativeList<int>(Allocator.Persistent);
            }
        }

        private void CompactRanges()
        {
            cellRanges.Clear();
            if (cellPairs.Length == 0)
            {
                return;
            }

            NativeArray<SpatialHashCellPair> sortedPairs = cellPairs.AsArray();
            int startIndex = 0;
            SpatialHashCellKey currentKey = sortedPairs[0].Key;

            for (int i = 1; i < sortedPairs.Length; i++)
            {
                if (sortedPairs[i].Key.Equals(currentKey))
                {
                    continue;
                }

                cellRanges.Add(new SpatialHashCellRange(currentKey, startIndex, i - startIndex));
                currentKey = sortedPairs[i].Key;
                startIndex = i;
            }

            cellRanges.Add(new SpatialHashCellRange(currentKey, startIndex, sortedPairs.Length - startIndex));
        }

        private static int BinarySearchRange(NativeArray<SpatialHashCellRange> ranges, SpatialHashCellKey key)
        {
            int low = 0;
            int high = ranges.Length - 1;

            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                int comparison = ranges[mid].Key.CompareTo(key);
                if (comparison == 0)
                {
                    return mid;
                }

                if (comparison < 0)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return -1;
        }

        [BurstCompile]
        private struct GenerateCellPairsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SpatialHashIndexBuildItem> BuildItems;
            [ReadOnly] public NativeArray<int> Offsets;
            [NativeDisableParallelForRestriction]
            [WriteOnly] public NativeArray<SpatialHashCellPair> CellPairs;
            public SpatialHashGridDimension Dimension;

            public void Execute(int index)
            {
                SpatialHashIndexBuildItem buildItem = BuildItems[index];
                if (buildItem.IsOverflow)
                {
                    return;
                }

                buildItem.QuantizedBounds.GetCellRange(
                    buildItem.Level,
                    Dimension,
                    out int minX,
                    out int maxX,
                    out int minY,
                    out int maxY,
                    out int minZ,
                    out int maxZ);

                int writeIndex = Offsets[index];
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            CellPairs[writeIndex++] = new SpatialHashCellPair(new SpatialHashCellKey(buildItem.Level, x, y, z), buildItem.StableId);
                        }
                    }
                }
            }
        }

        private readonly struct SpatialHashCellPair : IComparable<SpatialHashCellPair>
        {
            public readonly SpatialHashCellKey Key;
            public readonly int StableId;

            public SpatialHashCellPair(SpatialHashCellKey key, int stableId)
            {
                Key = key;
                StableId = stableId;
            }

            public int CompareTo(SpatialHashCellPair other)
            {
                int keyComparison = Key.CompareTo(other.Key);
                if (keyComparison != 0)
                {
                    return keyComparison;
                }

                return StableId.CompareTo(other.StableId);
            }
        }

        private readonly struct SpatialHashCellRange
        {
            public readonly SpatialHashCellKey Key;
            public readonly int StartIndex;
            public readonly int Count;

            public SpatialHashCellRange(SpatialHashCellKey key, int startIndex, int count)
            {
                Key = key;
                StartIndex = startIndex;
                Count = count;
            }
        }

        private readonly struct SpatialHashCellKey : IComparable<SpatialHashCellKey>, IEquatable<SpatialHashCellKey>
        {
            public readonly int Level;
            public readonly int X;
            public readonly int Y;
            public readonly int Z;

            public SpatialHashCellKey(int level, int x, int y, int z)
            {
                Level = level;
                X = x;
                Y = y;
                Z = z;
            }

            public int CompareTo(SpatialHashCellKey other)
            {
                int levelComparison = Level.CompareTo(other.Level);
                if (levelComparison != 0)
                {
                    return levelComparison;
                }

                int xComparison = X.CompareTo(other.X);
                if (xComparison != 0)
                {
                    return xComparison;
                }

                int yComparison = Y.CompareTo(other.Y);
                if (yComparison != 0)
                {
                    return yComparison;
                }

                return Z.CompareTo(other.Z);
            }

            public bool Equals(SpatialHashCellKey other)
            {
                return Level == other.Level && X == other.X && Y == other.Y && Z == other.Z;
            }
        }
    }
}

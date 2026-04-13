using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace LegendaryTools
{
    [DisallowMultipleComponent]
    public class SpatialHashRuntimeBenchmark : UnityBehaviour
    {
        [Header("Execution")]
        [SerializeField] private bool runOnStart;
        [SerializeField] private bool collectGarbageBeforeSamples = true;
        [SerializeField] private bool exactFilterSpatialHashResults;

        [Header("Dataset")]
        [SerializeField] private int randomSeed = 12345;
        [SerializeField] private int entityCount = 10000;
        [SerializeField] private int queryCount = 1000;
        [SerializeField, Range(0f, 1f)] private float dynamicEntityRatio = 1f;
        [SerializeField, Range(0f, 1f)] private float updateEntityRatio = 0.1f;
        [SerializeField] private Bounds worldBounds = new(Vector3.zero, new Vector3(256f, 256f, 256f));
        [SerializeField] private Vector2 entitySizeRange = new(0.5f, 2f);
        [SerializeField] private Vector2 querySizeRange = new(4f, 16f);

        [Header("Grid")]
        [SerializeField] private SpatialHashGridDimension dimension = SpatialHashGridDimension.XYZ3D;
        [SerializeField] private float baseCellSize = 4f;
        [SerializeField] private int levelCount = 6;
        [SerializeField] private int maxCellsPerAxis = 4;
        [SerializeField] private int maxCellsPerObject = 64;

        [Header("Sampling")]
        [SerializeField] private int warmupIterations = 1;
        [SerializeField] private int measuredIterations = 5;
        [SerializeField] private bool runFrameBenchmark = true;

        private readonly List<BenchmarkEntity> spatialHashResults = new(256);
        private readonly List<BenchmarkEntity> manualResults = new(256);
        private readonly StringBuilder reportBuilder = new(2048);

        private void Start()
        {
            if (runOnStart)
            {
                RunBenchmark();
            }
        }

        [ContextMenu("Run Spatial Hash Benchmark")]
        public void RunBenchmark()
        {
            ValidateSettings();

            BenchmarkEntity[] entities = GenerateEntities();
            Bounds[] queries = GenerateQueries();
            int[] updateIndices = GenerateUpdateIndices(entities);
            Bounds[][] updateBoundsByIteration = GenerateUpdateBounds(updateIndices);
            SpatialHashQueryFilter filter = SpatialHashQueryFilter.Default;

            using HierarchicalSparseSpatialHashGrid<BenchmarkEntity> grid = CreateGrid();
            double buildMs = MeasureMilliseconds(() => BuildGrid(grid, entities));
            VerificationResult verification = VerifyGrid(grid, entities, queries, filter);

            for (int i = 0; i < warmupIterations; i++)
            {
                RunSpatialHashQueries(grid, queries, filter);
                RunManualQueries(entities, queries, filter);
            }

            long spatialHashQueryTicks = 0;
            long manualQueryTicks = 0;
            int spatialHashHits = 0;
            int manualHits = 0;

            for (int i = 0; i < measuredIterations; i++)
            {
                CollectGarbageIfNeeded();
                spatialHashQueryTicks += MeasureTicks(() => spatialHashHits = RunSpatialHashQueries(grid, queries, filter));

                CollectGarbageIfNeeded();
                manualQueryTicks += MeasureTicks(() => manualHits = RunManualQueries(entities, queries, filter));
            }

            reportBuilder.Clear();
            reportBuilder.AppendLine("[SpatialHashRuntimeBenchmark]");
            reportBuilder.AppendLine($"Entities: {entityCount:n0}, Queries: {queryCount:n0}, Warmup: {warmupIterations}, Samples: {measuredIterations}");
            reportBuilder.AppendLine($"Dimension: {dimension}, World: {worldBounds.size}, BaseCellSize: {baseCellSize}, Levels: {levelCount}");
            reportBuilder.AppendLine($"Additional exact-filter SpatialHash results: {exactFilterSpatialHashResults}");
            reportBuilder.AppendLine($"Dynamic entities: {Mathf.RoundToInt(entityCount * dynamicEntityRatio):n0}, Updated per frame sample: {updateIndices.Length:n0}");
            reportBuilder.AppendLine();
            reportBuilder.AppendLine("HierarchicalSparseSpatialHashGrid vs manual Bounds scan");
            reportBuilder.AppendLine($"Build + rebuild indexes: {buildMs:0.###} ms");
            AppendQueryComparison(spatialHashQueryTicks, manualQueryTicks, spatialHashHits, manualHits, verification);

            if (runFrameBenchmark)
            {
                AppendFrameComparison(entities, queries, updateIndices, updateBoundsByIteration, filter);
            }

            Debug.Log(reportBuilder.ToString(), this);
        }

        private HierarchicalSparseSpatialHashGrid<BenchmarkEntity> CreateGrid()
        {
            return new HierarchicalSparseSpatialHashGrid<BenchmarkEntity>(
                worldBounds,
                baseCellSize,
                levelCount,
                dimension,
                maxCellsPerAxis,
                maxCellsPerObject);
        }

        private void BuildGrid(HierarchicalSparseSpatialHashGrid<BenchmarkEntity> grid, BenchmarkEntity[] entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Upsert(grid, entities[i]);
            }

            grid.RebuildStaticIndex();
            grid.RebuildDynamicIndex();
        }

        private static void Upsert(HierarchicalSparseSpatialHashGrid<BenchmarkEntity> grid, BenchmarkEntity entity)
        {
            grid.Upsert(
                entity.StableId,
                entity.Bounds,
                entity,
                new SpatialHashItemMetadata(entity.IsDynamic, entity.Layer, entity.UserFlags));
        }

        private int RunSpatialHashQueries(
            HierarchicalSparseSpatialHashGrid<BenchmarkEntity> grid,
            Bounds[] queries,
            SpatialHashQueryFilter filter)
        {
            int totalHits = 0;
            for (int i = 0; i < queries.Length; i++)
            {
                spatialHashResults.Clear();
                grid.Query(queries[i], spatialHashResults, filter);
                totalHits += CountSpatialHashResults(queries[i]);
            }

            return totalHits;
        }

        private int RunManualQueries(BenchmarkEntity[] entities, Bounds[] queries, SpatialHashQueryFilter filter)
        {
            int totalHits = 0;
            SpatialHashQueryFilter normalizedFilter = NormalizeFilter(filter);

            for (int queryIndex = 0; queryIndex < queries.Length; queryIndex++)
            {
                manualResults.Clear();
                Bounds query = queries[queryIndex];

                for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
                {
                    BenchmarkEntity entity = entities[entityIndex];
                    if (PassesFilter(entity, normalizedFilter) && Intersects(entity.Bounds, query))
                    {
                        manualResults.Add(entity);
                    }
                }

                totalHits += manualResults.Count;
            }

            return totalHits;
        }

        private void AppendFrameComparison(
            BenchmarkEntity[] sourceEntities,
            Bounds[] queries,
            int[] updateIndices,
            Bounds[][] updateBoundsByIteration,
            SpatialHashQueryFilter filter)
        {
            BenchmarkEntity[] spatialHashEntities = CloneEntities(sourceEntities);
            BenchmarkEntity[] manualEntities = CloneEntities(sourceEntities);

            using HierarchicalSparseSpatialHashGrid<BenchmarkEntity> frameGrid = CreateGrid();
            BuildGrid(frameGrid, spatialHashEntities);

            for (int i = 0; i < warmupIterations; i++)
            {
                RunSpatialHashFrame(frameGrid, spatialHashEntities, queries, updateIndices, updateBoundsByIteration, filter);
                RunManualFrame(manualEntities, queries, updateIndices, updateBoundsByIteration, filter);
            }

            CollectGarbageIfNeeded();
            int spatialHashHits = 0;
            long spatialHashFrameTicks = MeasureTicks(() =>
                spatialHashHits = RunSpatialHashFrame(frameGrid, spatialHashEntities, queries, updateIndices, updateBoundsByIteration, filter));

            CollectGarbageIfNeeded();
            int manualHits = 0;
            long manualFrameTicks = MeasureTicks(() =>
                manualHits = RunManualFrame(manualEntities, queries, updateIndices, updateBoundsByIteration, filter));

            double spatialHashFrameMs = TicksToMilliseconds(spatialHashFrameTicks) / measuredIterations;
            double manualFrameMs = TicksToMilliseconds(manualFrameTicks) / measuredIterations;

            reportBuilder.AppendLine();
            reportBuilder.AppendLine($"Frame update + queries avg/sample: SpatialHash {spatialHashFrameMs:0.###} ms, Manual {manualFrameMs:0.###} ms, Speedup {FormatSpeedup(manualFrameMs, spatialHashFrameMs)}");
            reportBuilder.AppendLine($"Frame hits: SpatialHash {spatialHashHits:n0}, Manual {manualHits:n0}");
        }

        private int RunSpatialHashFrame(
            HierarchicalSparseSpatialHashGrid<BenchmarkEntity> grid,
            BenchmarkEntity[] entities,
            Bounds[] queries,
            int[] updateIndices,
            Bounds[][] updateBoundsByIteration,
            SpatialHashQueryFilter filter)
        {
            int hits = 0;

            for (int iteration = 0; iteration < measuredIterations; iteration++)
            {
                ApplyEntityUpdates(entities, updateIndices, updateBoundsByIteration[iteration]);
                for (int i = 0; i < updateIndices.Length; i++)
                {
                    Upsert(grid, entities[updateIndices[i]]);
                }

                grid.RebuildDynamicIndex();
                hits = RunSpatialHashQueries(grid, queries, filter);
            }

            return hits;
        }

        private int RunManualFrame(
            BenchmarkEntity[] entities,
            Bounds[] queries,
            int[] updateIndices,
            Bounds[][] updateBoundsByIteration,
            SpatialHashQueryFilter filter)
        {
            int hits = 0;

            for (int iteration = 0; iteration < measuredIterations; iteration++)
            {
                ApplyEntityUpdates(entities, updateIndices, updateBoundsByIteration[iteration]);
                hits = RunManualQueries(entities, queries, filter);
            }

            return hits;
        }

        private void AppendQueryComparison(
            long spatialHashTicks,
            long manualTicks,
            int spatialHashHits,
            int manualHits,
            VerificationResult verification)
        {
            double spatialHashMs = TicksToMilliseconds(spatialHashTicks) / measuredIterations;
            double manualMs = TicksToMilliseconds(manualTicks) / measuredIterations;

            reportBuilder.AppendLine($"Queries avg/sample: SpatialHash {spatialHashMs:0.###} ms, Manual {manualMs:0.###} ms, Speedup {FormatSpeedup(manualMs, spatialHashMs)}");
            reportBuilder.AppendLine($"Hits last sample: SpatialHash {spatialHashHits:n0}, Manual {manualHits:n0}");
            reportBuilder.AppendLine($"Correctness: {(verification.Matches ? "OK" : "MISMATCH")} ({verification.CheckedQueries:n0} queries checked, {verification.MismatchCount:n0} mismatches)");
        }

        private VerificationResult VerifyGrid(
            HierarchicalSparseSpatialHashGrid<BenchmarkEntity> grid,
            BenchmarkEntity[] entities,
            Bounds[] queries,
            SpatialHashQueryFilter filter)
        {
            int mismatchCount = 0;
            int checkedQueries = Mathf.Min(queries.Length, 64);

            for (int i = 0; i < checkedQueries; i++)
            {
                spatialHashResults.Clear();
                manualResults.Clear();

                grid.Query(queries[i], spatialHashResults, filter);
                FilterSpatialHashResults(queries[i]);
                CollectManualResults(entities, queries[i], filter);

                if (!SameEntityIds(spatialHashResults, manualResults))
                {
                    mismatchCount++;
                }
            }

            return new VerificationResult(checkedQueries, mismatchCount);
        }

        private void CollectManualResults(BenchmarkEntity[] entities, Bounds query, SpatialHashQueryFilter filter)
        {
            SpatialHashQueryFilter normalizedFilter = NormalizeFilter(filter);

            for (int i = 0; i < entities.Length; i++)
            {
                BenchmarkEntity entity = entities[i];
                if (PassesFilter(entity, normalizedFilter) && Intersects(entity.Bounds, query))
                {
                    manualResults.Add(entity);
                }
            }
        }

        private int CountSpatialHashResults(Bounds query)
        {
            if (!exactFilterSpatialHashResults)
            {
                return spatialHashResults.Count;
            }

            int count = 0;
            for (int i = 0; i < spatialHashResults.Count; i++)
            {
                if (Intersects(spatialHashResults[i].Bounds, query))
                {
                    count++;
                }
            }

            return count;
        }

        private void FilterSpatialHashResults(Bounds query)
        {
            if (!exactFilterSpatialHashResults)
            {
                return;
            }

            for (int i = spatialHashResults.Count - 1; i >= 0; i--)
            {
                if (!Intersects(spatialHashResults[i].Bounds, query))
                {
                    spatialHashResults.RemoveAt(i);
                }
            }
        }

        private static bool SameEntityIds(List<BenchmarkEntity> left, List<BenchmarkEntity> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i].StableId != right[i].StableId)
                {
                    return false;
                }
            }

            return true;
        }

        private BenchmarkEntity[] GenerateEntities()
        {
            Random random = new(randomSeed);
            BenchmarkEntity[] entities = new BenchmarkEntity[entityCount];

            for (int i = 0; i < entities.Length; i++)
            {
                entities[i] = new BenchmarkEntity
                {
                    StableId = i + 1,
                    Bounds = GenerateBounds(random, entitySizeRange),
                    Layer = 0,
                    UserFlags = 0u,
                    IsDynamic = random.NextDouble() < dynamicEntityRatio
                };
            }

            return entities;
        }

        private Bounds[] GenerateQueries()
        {
            Random random = new(randomSeed + 7919);
            Bounds[] queries = new Bounds[queryCount];

            for (int i = 0; i < queries.Length; i++)
            {
                queries[i] = GenerateBounds(random, querySizeRange);
            }

            return queries;
        }

        private int[] GenerateUpdateIndices(BenchmarkEntity[] entities)
        {
            List<int> dynamicIndices = new();
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i].IsDynamic)
                {
                    dynamicIndices.Add(i);
                }
            }

            if (dynamicIndices.Count == 0 || updateEntityRatio <= 0f)
            {
                return Array.Empty<int>();
            }

            int updateCount = Mathf.Clamp(Mathf.RoundToInt(dynamicIndices.Count * updateEntityRatio), 1, dynamicIndices.Count);
            int[] indices = new int[updateCount];
            int stride = Mathf.Max(1, dynamicIndices.Count / updateCount);

            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = dynamicIndices[Mathf.Min(i * stride, dynamicIndices.Count - 1)];
            }

            return indices;
        }

        private Bounds[][] GenerateUpdateBounds(int[] updateIndices)
        {
            Random random = new(randomSeed + 15485863);
            Bounds[][] updateBounds = new Bounds[measuredIterations][];

            for (int iteration = 0; iteration < measuredIterations; iteration++)
            {
                Bounds[] iterationBounds = new Bounds[updateIndices.Length];
                for (int i = 0; i < iterationBounds.Length; i++)
                {
                    iterationBounds[i] = GenerateBounds(random, entitySizeRange);
                }

                updateBounds[iteration] = iterationBounds;
            }

            return updateBounds;
        }

        private Bounds GenerateBounds(Random random, Vector2 sizeRange)
        {
            float sizeX = RandomRange(random, sizeRange.x, sizeRange.y);
            float sizeY = UsesAxis(1) ? RandomRange(random, sizeRange.x, sizeRange.y) : 0.01f;
            float sizeZ = UsesAxis(2) ? RandomRange(random, sizeRange.x, sizeRange.y) : 0.01f;
            Vector3 size = new(sizeX, sizeY, sizeZ);

            Vector3 min = worldBounds.min + size * 0.5f;
            Vector3 max = worldBounds.max - size * 0.5f;
            Vector3 center = new(
                RandomRange(random, min.x, max.x),
                UsesAxis(1) ? RandomRange(random, min.y, max.y) : worldBounds.center.y,
                UsesAxis(2) ? RandomRange(random, min.z, max.z) : worldBounds.center.z);

            return new Bounds(center, size);
        }

        private static BenchmarkEntity[] CloneEntities(BenchmarkEntity[] source)
        {
            BenchmarkEntity[] clone = new BenchmarkEntity[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                clone[i] = source[i].Clone();
            }

            return clone;
        }

        private static void ApplyEntityUpdates(BenchmarkEntity[] entities, int[] updateIndices, Bounds[] updateBounds)
        {
            for (int i = 0; i < updateIndices.Length; i++)
            {
                entities[updateIndices[i]].Bounds = updateBounds[i];
            }
        }

        private bool Intersects(Bounds left, Bounds right)
        {
            Vector3 leftMin = left.min;
            Vector3 leftMax = left.max;
            Vector3 rightMin = right.min;
            Vector3 rightMax = right.max;

            bool intersectsX = leftMin.x <= rightMax.x && leftMax.x >= rightMin.x;
            bool intersectsY = !UsesAxis(1) || (leftMin.y <= rightMax.y && leftMax.y >= rightMin.y);
            bool intersectsZ = !UsesAxis(2) || (leftMin.z <= rightMax.z && leftMax.z >= rightMin.z);
            return intersectsX && intersectsY && intersectsZ;
        }

        private bool UsesAxis(int axis)
        {
            return dimension switch
            {
                SpatialHashGridDimension.XY2D => axis != 2,
                SpatialHashGridDimension.XZ2D => axis != 1,
                _ => true
            };
        }

        private static bool PassesFilter(BenchmarkEntity entity, SpatialHashQueryFilter filter)
        {
            if ((filter.LayerMask & (1 << entity.Layer)) == 0)
            {
                return false;
            }

            if ((entity.UserFlags & filter.RequiredFlags) != filter.RequiredFlags)
            {
                return false;
            }

            if ((entity.UserFlags & filter.ExcludedFlags) != 0u)
            {
                return false;
            }

            return true;
        }

        private static SpatialHashQueryFilter NormalizeFilter(SpatialHashQueryFilter filter)
        {
            return filter.LayerMask == 0 && filter.RequiredFlags == 0u && filter.ExcludedFlags == 0u
                ? SpatialHashQueryFilter.Default
                : filter;
        }

        private static long MeasureTicks(Action action)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            return stopwatch.ElapsedTicks;
        }

        private static double MeasureMilliseconds(Action action)
        {
            return TicksToMilliseconds(MeasureTicks(action));
        }

        private static double TicksToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static string FormatSpeedup(double manualMs, double spatialHashMs)
        {
            if (spatialHashMs <= 0.000001)
            {
                return "n/a";
            }

            return $"{manualMs / spatialHashMs:0.##}x";
        }

        private void CollectGarbageIfNeeded()
        {
            if (!collectGarbageBeforeSamples)
            {
                return;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private void ValidateSettings()
        {
            entityCount = Mathf.Max(1, entityCount);
            queryCount = Mathf.Max(1, queryCount);
            measuredIterations = Mathf.Max(1, measuredIterations);
            warmupIterations = Mathf.Max(0, warmupIterations);
            baseCellSize = Mathf.Max(0.0001f, baseCellSize);
            levelCount = Mathf.Max(1, levelCount);
            maxCellsPerAxis = Mathf.Max(1, maxCellsPerAxis);
            maxCellsPerObject = Mathf.Max(1, maxCellsPerObject);
            entitySizeRange = NormalizeRange(entitySizeRange, 0.0001f);
            querySizeRange = NormalizeRange(querySizeRange, 0.0001f);

            Vector3 size = worldBounds.size;
            size.x = Mathf.Max(size.x, querySizeRange.y + 0.0001f);
            size.y = Mathf.Max(size.y, querySizeRange.y + 0.0001f);
            size.z = Mathf.Max(size.z, querySizeRange.y + 0.0001f);
            worldBounds.size = size;
        }

        private void OnValidate()
        {
            ValidateSettings();
        }

        private static Vector2 NormalizeRange(Vector2 range, float min)
        {
            float x = Mathf.Max(min, range.x);
            float y = Mathf.Max(min, range.y);
            if (x > y)
            {
                (x, y) = (y, x);
            }

            return new Vector2(x, y);
        }

        private static float RandomRange(Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private sealed class BenchmarkEntity
        {
            public int StableId;
            public Bounds Bounds;
            public int Layer;
            public uint UserFlags;
            public bool IsDynamic;

            public BenchmarkEntity Clone()
            {
                return new BenchmarkEntity
                {
                    StableId = StableId,
                    Bounds = Bounds,
                    Layer = Layer,
                    UserFlags = UserFlags,
                    IsDynamic = IsDynamic
                };
            }
        }

        private readonly struct VerificationResult
        {
            public readonly int CheckedQueries;
            public readonly int MismatchCount;
            public bool Matches => MismatchCount == 0;

            public VerificationResult(int checkedQueries, int mismatchCount)
            {
                CheckedQueries = checkedQueries;
                MismatchCount = mismatchCount;
            }
        }
    }
}

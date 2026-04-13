using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    [DisallowMultipleComponent]
    public class SpatialHashGridVolume : UnityBehaviour
    {
        [SerializeField] private SpatialHashGridDimension dimension = SpatialHashGridDimension.XYZ3D;
        [SerializeField] private Vector3 localCenter;
        [SerializeField] private Vector3 localSize = new(64f, 64f, 64f);
        [SerializeField] private float baseCellSize = 1f;
        [SerializeField] private int levelCount = 6;
        [SerializeField] private int maxCellsPerAxis = 4;
        [SerializeField] private int maxCellsPerObject = 64;
        [SerializeField] private SpatialHashDynamicUpdateMode dynamicUpdateMode = SpatialHashDynamicUpdateMode.FullDynamicRebuild;
        [SerializeField] private bool refreshDynamicBodiesInFixedUpdate = true;
        [SerializeField] private bool drawLevel0Cells;

        private SpatialHashGridSystem<SpatialHashBody> system;

        public SpatialHashGridDimension Dimension => dimension;
        public Bounds WorldBounds => new(transform.TransformPoint(localCenter), Vector3.Scale(localSize, Abs(transform.lossyScale)));
        public HierarchicalSparseSpatialHashGrid<SpatialHashBody> Grid
        {
            get
            {
                EnsureSystem();
                return system.Grid;
            }
        }

        public bool RegisterBody(SpatialHashBody body)
        {
            EnsureSystem();
            if (!system.Register(body, out SpatialHashBody conflictingBody))
            {
                if (conflictingBody != null)
                {
                    Debug.LogWarning($"SpatialHashGridVolume ignored duplicate stable id {body.StableId} on {body.name}.", this);
                }

                return false;
            }

            return true;
        }

        public bool UnregisterBody(SpatialHashBody body)
        {
            EnsureSystem();
            return system.Unregister(body);
        }

        public bool RefreshBody(SpatialHashBody body)
        {
            EnsureSystem();
            return system.Refresh(body);
        }

        public void RefreshDynamicBodies()
        {
            EnsureSystem();
            system.DynamicUpdateMode = dynamicUpdateMode;
            system.RefreshDynamic();
        }

        public void RebuildAll()
        {
            EnsureSystem(forceRebuild: true);
        }

        public int Query(Bounds bounds, List<SpatialHashBody> results, SpatialHashQueryFilter filter = default)
        {
            EnsureSystem();
            return system.Query(bounds, results, filter);
        }

        public int QueryBroadphase(Bounds bounds, List<SpatialHashBody> results, SpatialHashQueryFilter filter = default)
        {
            EnsureSystem();
            return system.QueryBroadphase(bounds, results, filter);
        }

        public int QueryIds(Bounds bounds, List<int> results, SpatialHashQueryFilter filter = default)
        {
            EnsureSystem();
            return system.QueryIds(bounds, results, filter);
        }

        public int QueryIdsBroadphase(Bounds bounds, List<int> results, SpatialHashQueryFilter filter = default)
        {
            EnsureSystem();
            return system.QueryIdsBroadphase(bounds, results, filter);
        }

        private void OnEnable()
        {
            EnsureSystem(forceRebuild: true);
        }

        private void OnDestroy()
        {
            system?.Dispose();
            system = null;
        }

        private void FixedUpdate()
        {
            if (refreshDynamicBodiesInFixedUpdate)
            {
                RefreshDynamicBodies();
            }
        }

        private void OnValidate()
        {
            localSize.x = Mathf.Max(0.0001f, localSize.x);
            localSize.y = Mathf.Max(0.0001f, localSize.y);
            localSize.z = Mathf.Max(0.0001f, localSize.z);
            baseCellSize = Mathf.Max(0.0001f, baseCellSize);
            levelCount = Mathf.Max(1, levelCount);
            maxCellsPerAxis = Mathf.Max(1, maxCellsPerAxis);
            maxCellsPerObject = Mathf.Max(1, maxCellsPerObject);

            if (Application.isPlaying)
            {
                RebuildAll();
            }
        }

        private void OnDrawGizmosSelected()
        {
            Bounds bounds = WorldBounds;
            Gizmos.color = new Color(0.25f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, GetVisibleGizmoSize(bounds.size));

            if (!drawLevel0Cells)
            {
                return;
            }

            float cellSize = baseCellSize;
            Vector3 min = bounds.min;
            Vector3 size = GetVisibleGizmoSize(bounds.size);
            int xCells = Mathf.Max(1, Mathf.CeilToInt(size.x / cellSize));
            int yCells = dimension == SpatialHashGridDimension.XZ2D ? 1 : Mathf.Max(1, Mathf.CeilToInt(size.y / cellSize));
            int zCells = dimension == SpatialHashGridDimension.XY2D ? 1 : Mathf.Max(1, Mathf.CeilToInt(size.z / cellSize));

            Gizmos.color = new Color(0.25f, 0.8f, 1f, 0.15f);
            for (int x = 0; x < xCells; x++)
            {
                for (int y = 0; y < yCells; y++)
                {
                    for (int z = 0; z < zCells; z++)
                    {
                        Vector3 cellCenter = min + new Vector3((x + 0.5f) * cellSize, (y + 0.5f) * cellSize, (z + 0.5f) * cellSize);
                        Vector3 cellVisualSize = GetVisibleGizmoSize(new Vector3(cellSize, cellSize, cellSize));
                        Gizmos.DrawWireCube(cellCenter, cellVisualSize);
                    }
                }
            }
        }

        private void EnsureSystem(bool forceRebuild = false)
        {
            SpatialHashGridSettings settings = CreateSettings();
            if (system == null)
            {
                system = new SpatialHashGridSystem<SpatialHashBody>(settings, dynamicUpdateMode);
                return;
            }

            system.DynamicUpdateMode = dynamicUpdateMode;

            if (forceRebuild || !system.Settings.Equals(settings))
            {
                system.Rebuild(settings);
            }
        }

        private SpatialHashGridSettings CreateSettings()
        {
            return new SpatialHashGridSettings(
                WorldBounds,
                baseCellSize,
                levelCount,
                dimension,
                maxCellsPerAxis,
                maxCellsPerObject);
        }

        private Vector3 GetVisibleGizmoSize(Vector3 size)
        {
            const float PlaneThickness = 0.05f;

            return dimension switch
            {
                SpatialHashGridDimension.XY2D => new Vector3(size.x, size.y, Mathf.Max(size.z, PlaneThickness)),
                SpatialHashGridDimension.XZ2D => new Vector3(size.x, Mathf.Max(size.y, PlaneThickness), size.z),
                _ => size
            };
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Systems.Voxel
{
    /// <summary>
    /// Procedurally generates a voxel-like box grid using greedy meshing:
    /// - Supports multiple adjacent cubes (grid of voxels).
    /// - Culls internal faces (only solid-to-empty boundaries produce quads).
    /// - Merges coplanar contiguous quads per face direction (greedy meshing).
    /// UVs tile over merged rectangles using uvScale/uvOffset.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class DynamicCube : MonoBehaviour
    {
        // =======================
        // Dimensions & Layout
        // =======================
        [Header("Grid Dimensions")] [Min(1)] public int gridSizeX = 2;
        [Min(1)] public int gridSizeY = 2;
        [Min(1)] public int gridSizeZ = 2;

        [Header("Cell Size (per voxel)")] [Min(0f)]
        public float cellWidth = 1f;

        [Min(0f)] public float cellHeight = 1f;
        [Min(0f)] public float cellDepth = 1f;

        [Header("Pivot")] public bool pivotCentered = true;

        // =======================
        // UV & Rendering
        // =======================
        [Header("UV Mapping")] public Vector2 uvScale = Vector2.one;
        public Vector2 uvOffset = Vector2.zero;

        [Header("Rendering")] public Material material;
        public bool addMeshCollider = false;

        // =======================
        // Voxel Control
        // =======================
        public enum VoxelFillMode
        {
            FillAllSolid, // All voxels in bounds are solid
            UseSolidList, // Only positions listed in solidVoxels are solid
            UseEmptyList // All solid except positions listed in emptyVoxels
        }

        [Header("Voxels (Optional)")] public VoxelFillMode mode = VoxelFillMode.FillAllSolid;

        /// <summary>
        /// When mode == UseSolidList: positions in this set are solid.
        /// </summary>
        [Tooltip("Voxel coordinates that should be solid when mode = UseSolidList.")]
        public List<Vector3Int> solidVoxels = new List<Vector3Int>();

        /// <summary>
        /// When mode == UseEmptyList: positions in this set are empty.
        /// </summary>
        [Tooltip("Voxel coordinates that should be empty when mode = UseEmptyList.")]
        public List<Vector3Int> emptyVoxels = new List<Vector3Int>();

        // =======================
        // Internals
        // =======================
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private MeshCollider _meshCollider;
        private Mesh _meshInstance;

        // Scratch buffers to avoid allocations on every rebuild
        private readonly List<Vector3> _verts = new List<Vector3>(4096);
        private readonly List<Vector3> _norms = new List<Vector3>(4096);
        private readonly List<Vector2> _uvs = new List<Vector2>(4096);
        private readonly List<int> _tris = new List<int>(8192);

        private readonly HashSet<Vector3Int> _solidSet = new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> _emptySet = new HashSet<Vector3Int>();

        private void Reset()
        {
            EnsureComponents();
            Rebuild();
        }

        private void OnEnable()
        {
            EnsureComponents();
            Rebuild();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            gridSizeX = Mathf.Max(1, gridSizeX);
            gridSizeY = Mathf.Max(1, gridSizeY);
            gridSizeZ = Mathf.Max(1, gridSizeZ);
            cellWidth = Mathf.Max(0f, cellWidth);
            cellHeight = Mathf.Max(0f, cellHeight);
            cellDepth = Mathf.Max(0f, cellDepth);
            EnsureComponents();
            Rebuild();
        }
#endif

        /// <summary>
        /// Ensures the required components exist and caches references.
        /// </summary>
        private void EnsureComponents()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
                if (_meshFilter == null) _meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            if (_meshRenderer == null)
            {
                _meshRenderer = GetComponent<MeshRenderer>();
                if (_meshRenderer == null) _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            if (_meshInstance == null)
            {
                _meshInstance = new Mesh { name = "DynamicCubeGreedyMesh" };
                _meshInstance.hideFlags = HideFlags.DontSave;
            }

            if (material != null)
                _meshRenderer.sharedMaterial = material;

            if (addMeshCollider)
            {
                if (_meshCollider == null)
                {
                    _meshCollider = GetComponent<MeshCollider>();
                    if (_meshCollider == null) _meshCollider = gameObject.AddComponent<MeshCollider>();
                }
            }
            else
            {
                if (_meshCollider != null)
                {
#if UNITY_EDITOR
                    DestroyImmediate(_meshCollider);
#else
                Destroy(_meshCollider);
#endif
                    _meshCollider = null;
                }
            }
        }

        /// <summary>
        /// Rebuilds the mesh using greedy meshing with internal face culling.
        /// </summary>
        public void Rebuild()
        {
            PrepareVoxelSets();

            _verts.Clear();
            _norms.Clear();
            _uvs.Clear();
            _tris.Clear();

            // Greedy meshing on all three axes:
            // Axis 0 = X faces (normals ±X) over YZ slices
            // Axis 1 = Y faces (normals ±Y) over XZ slices
            // Axis 2 = Z faces (normals ±Z) over XY slices
            GreedyAxis(0);
            GreedyAxis(1);
            GreedyAxis(2);

            // Push to mesh
            _meshInstance.Clear();
            _meshInstance.SetVertices(_verts);
            _meshInstance.SetNormals(_norms);
            _meshInstance.SetUVs(0, _uvs);
            _meshInstance.SetTriangles(_tris, 0, true);
            _meshInstance.RecalculateTangents();

            _meshFilter.sharedMesh = _meshInstance;

            if (_meshCollider != null)
            {
                _meshCollider.sharedMesh = null;
                _meshCollider.sharedMesh = _meshInstance;
            }

            if (material != null && _meshRenderer.sharedMaterial != material)
                _meshRenderer.sharedMaterial = material;
        }

        /// <summary>
        /// Public API to set solid voxels (overwrites existing) and rebuild.
        /// </summary>
        public void SetSolidVoxels(IEnumerable<Vector3Int> voxels, VoxelFillMode fillMode = VoxelFillMode.UseSolidList)
        {
            mode = fillMode;
            solidVoxels.Clear();
            emptyVoxels.Clear();

            if (voxels != null)
                solidVoxels.AddRange(voxels);

            Rebuild();
        }

        // =======================
        // Voxel Helpers
        // =======================

        /// <summary>
        /// Populates internal HashSets for fast membership queries according to mode.
        /// </summary>
        private void PrepareVoxelSets()
        {
            _solidSet.Clear();
            _emptySet.Clear();

            if (mode == VoxelFillMode.UseSolidList)
            {
                foreach (var v in solidVoxels)
                    if (InBounds(v))
                        _solidSet.Add(v);
            }
            else if (mode == VoxelFillMode.UseEmptyList)
            {
                foreach (var v in emptyVoxels)
                    if (InBounds(v))
                        _emptySet.Add(v);
            }
        }

        /// <summary>
        /// Returns true if voxel at (x,y,z) is inside grid bounds.
        /// </summary>
        private bool InBounds(int x, int y, int z)
        {
            return (x >= 0 && x < gridSizeX) &&
                   (y >= 0 && y < gridSizeY) &&
                   (z >= 0 && z < gridSizeZ);
        }

        private bool InBounds(in Vector3Int v) => InBounds(v.x, v.y, v.z);

        /// <summary>
        /// Returns whether a voxel is solid according to current mode.
        /// </summary>
        private bool IsSolid(int x, int y, int z)
        {
            if (!InBounds(x, y, z)) return false;

            switch (mode)
            {
                case VoxelFillMode.FillAllSolid:
                    return true;

                case VoxelFillMode.UseSolidList:
                    return _solidSet.Contains(new Vector3Int(x, y, z));

                case VoxelFillMode.UseEmptyList:
                    return !_emptySet.Contains(new Vector3Int(x, y, z));

                default:
                    return true;
            }
        }

        // =======================
        // Greedy Meshing
        // =======================

        /// <summary>
        /// Runs greedy meshing on a given axis.
        /// Axis 0 -> X faces (slice YZ)
        /// Axis 1 -> Y faces (slice XZ)
        /// Axis 2 -> Z faces (slice XY)
        /// </summary>
        private void GreedyAxis(int axis)
        {
            // Dimensions per axis
            int u = (axis + 1) % 3; // first in-plane axis
            int v = (axis + 2) % 3; // second in-plane axis

            int[] dims = { gridSizeX, gridSizeY, gridSizeZ };
            int Du = dims[u];
            int Dv = dims[v];
            int Dw = dims[axis]; // slices count along the axis

            // Mask holds: 0=no face, +1=positive normal face, -1=negative normal face
            int[,] mask = new int[Du, Dv];

            // Iterate slices including the "outside" one to capture boundary faces.
            // For each slice w from 0..Dw, we compare solids at w-1 and w.
            for (int w = 0; w <= Dw; w++)
            {
                // Build mask for this slice
                for (int iu = 0; iu < Du; iu++)
                {
                    for (int iv = 0; iv < Dv; iv++)
                    {
                        // Map (iu, iv, w) into (x, y, z) for the pair of cells being compared.
                        // a is at w-1, b is at w.
                        int xA, yA, zA;
                        int xB, yB, zB;
                        IndexMap(axis, u, v, w - 1, iu, iv, out xA, out yA, out zA);
                        IndexMap(axis, u, v, w, iu, iv, out xB, out yB, out zB);

                        bool aSolid = InBounds(xA, yA, zA) && IsSolid(xA, yA, zA);
                        bool bSolid = InBounds(xB, yB, zB) && IsSolid(xB, yB, zB);

                        // Outward normals:
                        // If a is solid and b is empty: +axis face at boundary w (outward from 'a').
                        // If b is solid and a is empty: -axis face at boundary w (outward from 'b').
                        int val = 0;
                        if (aSolid != bSolid)
                        {
                            val = aSolid ? +1 : -1;
                        }

                        mask[iu, iv] = val;
                    }
                }

                // Greedy merge rectangles on mask
                bool[,] consumed = new bool[Du, Dv];

                for (int iv = 0; iv < Dv; iv++)
                {
                    for (int iu = 0; iu < Du; iu++)
                    {
                        if (consumed[iu, iv]) continue;
                        int m = mask[iu, iv];
                        if (m == 0) continue;

                        // Find rectangle width
                        int width = 1;
                        while (iu + width < Du && !consumed[iu + width, iv] && mask[iu + width, iv] == m)
                            width++;

                        // Find rectangle height (greedy), ensuring all rows match the span
                        int height = 1;
                        bool done = false;
                        while (iv + height < Dv && !done)
                        {
                            for (int k = 0; k < width; k++)
                            {
                                if (consumed[iu + k, iv + height] || mask[iu + k, iv + height] != m)
                                {
                                    done = true;
                                    break;
                                }
                            }

                            if (!done) height++;
                        }

                        // Mark consumed
                        for (int dv2 = 0; dv2 < height; dv2++)
                        for (int du2 = 0; du2 < width; du2++)
                            consumed[iu + du2, iv + dv2] = true;

                        // Emit quad for this merged rectangle
                        AddGreedyQuad(axis, u, v, w, iu, iv, width, height, m);
                    }
                }
            }
        }

        /// <summary>
        /// Maps (axis,u,v) index triple into (x,y,z) coords.
        /// wIndex is along 'axis', iu along 'u', iv along 'v'.
        /// </summary>
        private static void IndexMap(int axis, int u, int v, int wIndex, int iu, int iv,
            out int x, out int y, out int z)
        {
            int[] arr = new int[3];
            arr[axis] = wIndex;
            arr[u] = iu;
            arr[v] = iv;
            x = arr[0];
            y = arr[1];
            z = arr[2];
        }

        /// <summary>
        /// Adds one merged quad to the mesh lists with proper orientation, position, normal, and UVs.
        /// </summary>
        private void AddGreedyQuad(int axis, int u, int v,
            int w, int iu, int iv,
            int width, int height, int sign)
        {
            // Cell sizes per axis
            float[] size = { cellWidth, cellHeight, cellDepth };

            // Compute pivot offset (centered or corner)
            Vector3 gridWorldSize = new Vector3(gridSizeX * cellWidth, gridSizeY * cellHeight, gridSizeZ * cellDepth);
            Vector3 pivotOffset = pivotCentered ? -0.5f * gridWorldSize : Vector3.zero;

            // The face lies exactly on the boundary plane at index w
            float plane = (axis == 0 ? w * size[0]
                : axis == 1 ? w * size[1]
                : w * size[2]);

            // In-plane vectors
            Vector3 duVec = Vector3.zero;
            duVec[u] = width * size[u];
            Vector3 dvVec = Vector3.zero;
            dvVec[v] = height * size[v];

            // Start corner on the plane
            Vector3 start = Vector3.zero;
            start[u] = iu * size[u];
            start[v] = iv * size[v];
            start[axis] = plane;

            // Normal points outward: +axis for sign>0, -axis for sign<0
            Vector3 normal = Vector3.zero;
            normal[axis] = (sign > 0) ? 1f : -1f;

            // CCW relative to outward normal
            Vector3 v0 = start;
            Vector3 v1 = start + duVec;
            Vector3 v2 = start + duVec + dvVec;
            Vector3 v3 = start + dvVec;

            // Apply pivot
            v0 += pivotOffset;
            v1 += pivotOffset;
            v2 += pivotOffset;
            v3 += pivotOffset;

            int baseIndex = _verts.Count;

            _verts.Add(v0);
            _verts.Add(v1);
            _verts.Add(v2);
            _verts.Add(v3);
            _norms.Add(normal);
            _norms.Add(normal);
            _norms.Add(normal);
            _norms.Add(normal);

            // UVs: tile according to merged size (width x height), scaled and offset
            Vector2 uv0 = uvOffset + Vector2.Scale(new Vector2(0f, 0f), uvScale);
            Vector2 uv1 = uvOffset + Vector2.Scale(new Vector2(width, 0f), uvScale);
            Vector2 uv2 = uvOffset + Vector2.Scale(new Vector2(width, height), uvScale);
            Vector2 uv3 = uvOffset + Vector2.Scale(new Vector2(0f, height), uvScale);
            _uvs.Add(uv0);
            _uvs.Add(uv1);
            _uvs.Add(uv2);
            _uvs.Add(uv3);

            // Triangle winding matches outward normal
            if (sign > 0)
            {
                _tris.Add(baseIndex + 0);
                _tris.Add(baseIndex + 1);
                _tris.Add(baseIndex + 2);
                _tris.Add(baseIndex + 0);
                _tris.Add(baseIndex + 2);
                _tris.Add(baseIndex + 3);
            }
            else
            {
                _tris.Add(baseIndex + 0);
                _tris.Add(baseIndex + 2);
                _tris.Add(baseIndex + 1);
                _tris.Add(baseIndex + 0);
                _tris.Add(baseIndex + 3);
                _tris.Add(baseIndex + 2);
            }
        }

        // =======================
        // Convenience APIs
        // =======================

        /// <summary>
        /// Sets the grid size (voxel count on each axis) and rebuilds.
        /// </summary>
        public void SetGridSize(int x, int y, int z)
        {
            gridSizeX = Mathf.Max(1, x);
            gridSizeY = Mathf.Max(1, y);
            gridSizeZ = Mathf.Max(1, z);
            Rebuild();
        }

        /// <summary>
        /// Sets the per-voxel cell size and rebuilds.
        /// </summary>
        public void SetCellSize(float w, float h, float d)
        {
            cellWidth = Mathf.Max(0f, w);
            cellHeight = Mathf.Max(0f, h);
            cellDepth = Mathf.Max(0f, d);
            Rebuild();
        }

        /// <summary>
        /// Sets UV tiling/offset and rebuilds.
        /// </summary>
        public void SetUV(Vector2 scale, Vector2 offset)
        {
            uvScale = scale;
            uvOffset = offset;
            Rebuild();
        }
    }
}
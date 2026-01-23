using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Visualization
{
    /// <summary>
    /// Visualizes the boids simulation using GameObjects (cubes) and allows switching simulation backend at runtime.
    /// Uses IMGUI for a simple in-editor/runtime control panel.
    /// </summary>
    public sealed class BoidsGameObjectVisualizer : MonoBehaviour
    {
        [Header("Backend")] [SerializeField] private BoidsBackendType backend = BoidsBackendType.EcsSingleThread;
        [SerializeField] private uint seed = 12345;

        [Header("Simulation")] [SerializeField]
        private BoidsSimConfig config = default;

        [Header("Visualization")] [SerializeField]
        private GameObject cubePrefab;

        [SerializeField] private int visualCount = 2000;
        [SerializeField] private float cubeScale = 0.12f;
        [SerializeField] private bool showUi = true;

        [Header("Runtime")] [SerializeField] private bool autoApplyBackendChange = true;

        private IBoidsBackend _backend;
        private Transform[] _cubes;
        private Vector3[] _positions;

        private BoidsBackendType _lastBackend;

        private void Reset()
        {
            config = BoidsSimConfig.Default;
            visualCount = 2000;
            cubeScale = 0.12f;
            backend = BoidsBackendType.EcsSingleThread;
            autoApplyBackendChange = true;
            showUi = true;
        }

        private void Awake()
        {
            if (config.BoidCount < 1) config.BoidCount = 1;
            if (visualCount < 1) visualCount = 1;

            EnsureCubes();
            CreateBackend(backend);
        }

        private void OnDestroy()
        {
            DestroyBackend();
            DestroyCubes();
        }

        private void Update()
        {
            if (_backend == null)
                return;

            if (autoApplyBackendChange && _lastBackend != backend) CreateBackend(backend);

            _backend.Step(Time.deltaTime);

            EnsurePositionsBuffer();

            _backend.CopyPositions(_positions);

            int n = Mathf.Min(visualCount, _backend.BoidCount);
            for (int i = 0; i < n; i++)
            {
                _cubes[i].position = _positions[i];
            }
        }

        private void OnGUI()
        {
            if (!showUi) return;

            const int w = 340;
            const int h = 210;

            GUILayout.BeginArea(new Rect(12, 12, w, h), GUI.skin.box);

            GUILayout.Label("Boids Visualizer (GameObjects)");

            GUILayout.Space(6);
            GUILayout.Label($"Backend: {backend}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ECS Single", GUILayout.Height(24))) backend = BoidsBackendType.EcsSingleThread;
            if (GUILayout.Button("ECS Multi", GUILayout.Height(24))) backend = BoidsBackendType.EcsMultiThread;
            if (GUILayout.Button("No ECS", GUILayout.Height(24))) backend = BoidsBackendType.ClassicNoEcs;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            autoApplyBackendChange = GUILayout.Toggle(autoApplyBackendChange, "Auto apply backend change");

            if (!autoApplyBackendChange)
                if (GUILayout.Button("Apply Backend Now", GUILayout.Height(26)))
                    CreateBackend(backend);

            GUILayout.Space(6);
            showUi = GUILayout.Toggle(showUi, "Show UI");

            GUILayout.EndArea();
        }

        private void CreateBackend(BoidsBackendType type)
        {
            DestroyBackend();

            // Keep visualCount <= boidCount (otherwise you would be reading outside the sim range).
            if (config.BoidCount < 1) config.BoidCount = 1;
            if (visualCount > config.BoidCount) visualCount = config.BoidCount;

            _backend = BoidsBackendFactory.Create(type, config, seed);
            _lastBackend = type;

            EnsurePositionsBuffer();
            EnsureCubes();

            ApplyCubeScale();
        }

        private void DestroyBackend()
        {
            if (_backend != null)
            {
                _backend.Dispose();
                _backend = null;
            }
        }

        private void EnsurePositionsBuffer()
        {
            int required = _backend != null ? _backend.BoidCount : Mathf.Max(1, config.BoidCount);

            if (_positions == null || _positions.Length < required) _positions = new Vector3[required];
        }

        private void EnsureCubes()
        {
            int needed = Mathf.Max(1, visualCount);

            if (_cubes != null && _cubes.Length == needed)
                return;

            DestroyCubes();

            if (cubePrefab == null)
            {
                // Fallback primitive.
                cubePrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubePrefab.name = "BoidCubePrefab (Auto)";
                cubePrefab.SetActive(false);
            }

            _cubes = new Transform[needed];

            for (int i = 0; i < needed; i++)
            {
                GameObject go = Instantiate(cubePrefab, transform);
                go.name = $"BoidCube_{i:00000}";
                go.SetActive(true);

                _cubes[i] = go.transform;
            }

            ApplyCubeScale();
        }

        private void ApplyCubeScale()
        {
            if (_cubes == null) return;

            Vector3 s = new(cubeScale, cubeScale, cubeScale);
            for (int i = 0; i < _cubes.Length; i++)
            {
                if (_cubes[i] != null) _cubes[i].localScale = s;
            }
        }

        private void DestroyCubes()
        {
            if (_cubes == null) return;

            for (int i = 0; i < _cubes.Length; i++)
            {
                if (_cubes[i] == null) continue;
                Destroy(_cubes[i].gameObject);
            }

            _cubes = null;
        }
    }
}
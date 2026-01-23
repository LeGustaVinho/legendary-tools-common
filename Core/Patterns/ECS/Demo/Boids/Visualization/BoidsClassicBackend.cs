using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Visualization
{
    /// <summary>
    /// Non-ECS baseline backend. Single-thread, managed arrays, O(n^2).
    /// </summary>
    public sealed class BoidsClassicBackend : IBoidsBackend
    {
        private readonly BoidsSimConfig _cfg;

        private readonly Vector3[] _pos;
        private readonly Vector3[] _vel;

        public BoidsClassicBackend(BoidsSimConfig cfg, uint seed = 12345)
        {
            _cfg = Sanitize(cfg);

            _pos = new Vector3[_cfg.BoidCount];
            _vel = new Vector3[_cfg.BoidCount];

            UnityEngine.Random.InitState(unchecked((int)seed));

            for (int i = 0; i < _cfg.BoidCount; i++)
            {
                _pos[i] = new Vector3(
                    UnityEngine.Random.Range(-_cfg.Bounds, _cfg.Bounds),
                    UnityEngine.Random.Range(-_cfg.Bounds, _cfg.Bounds),
                    UnityEngine.Random.Range(-_cfg.Bounds, _cfg.Bounds));

                _vel[i] = UnityEngine.Random.insideUnitSphere * (_cfg.MaxSpeed * 0.5f);
            }
        }

        public int BoidCount => _cfg.BoidCount;

        public void Step(float deltaTime)
        {
            float dt = deltaTime;

            float neighborR2 = _cfg.NeighborRadius * _cfg.NeighborRadius;
            float sepR2 = _cfg.SeparationRadius * _cfg.SeparationRadius;

            for (int i = 0; i < _cfg.BoidCount; i++)
            {
                Vector3 p0 = _pos[i];
                Vector3 v0 = _vel[i];

                Vector3 alignment = Vector3.zero;
                Vector3 cohesion = Vector3.zero;
                Vector3 separation = Vector3.zero;
                int neighbors = 0;

                for (int j = 0; j < _cfg.BoidCount; j++)
                {
                    if (i == j) continue;

                    Vector3 dp = _pos[j] - p0;
                    float d2 = dp.sqrMagnitude;
                    if (d2 > neighborR2) continue;

                    neighbors++;
                    alignment += _vel[j];
                    cohesion += _pos[j];

                    if (d2 < sepR2)
                    {
                        float inv = 1.0f / (Mathf.Sqrt(d2) + 1e-5f);
                        separation -= dp * inv;
                    }
                }

                if (neighbors > 0)
                {
                    float inv = 1.0f / neighbors;

                    alignment = (alignment * inv).normalized * _cfg.MaxSpeed - v0;
                    cohesion = (cohesion * inv - p0).normalized * _cfg.MaxSpeed - v0;
                    separation = separation.normalized * _cfg.MaxSpeed - v0;

                    Vector3 accel =
                        alignment * _cfg.AlignmentWeight +
                        cohesion * _cfg.CohesionWeight +
                        separation * _cfg.SeparationWeight;

                    v0 += accel * dt;
                }

                float sp = v0.magnitude;
                if (sp > _cfg.MaxSpeed) v0 = v0 * (_cfg.MaxSpeed / (sp + 1e-6f));

                p0 += v0 * dt;

                if (p0.x < -_cfg.Bounds)
                {
                    p0.x = -_cfg.Bounds;
                    v0.x = -v0.x;
                }
                else if (p0.x > _cfg.Bounds)
                {
                    p0.x = _cfg.Bounds;
                    v0.x = -v0.x;
                }

                if (p0.y < -_cfg.Bounds)
                {
                    p0.y = -_cfg.Bounds;
                    v0.y = -v0.y;
                }
                else if (p0.y > _cfg.Bounds)
                {
                    p0.y = _cfg.Bounds;
                    v0.y = -v0.y;
                }

                if (p0.z < -_cfg.Bounds)
                {
                    p0.z = -_cfg.Bounds;
                    v0.z = -v0.z;
                }
                else if (p0.z > _cfg.Bounds)
                {
                    p0.z = _cfg.Bounds;
                    v0.z = -v0.z;
                }

                _pos[i] = p0;
                _vel[i] = v0;
            }
        }

        public void CopyPositions(Vector3[] destination)
        {
            int n = _cfg.BoidCount;
            if (destination == null || destination.Length < n)
                throw new System.ArgumentException("Destination array is null or too small.", nameof(destination));

            System.Array.Copy(_pos, 0, destination, 0, n);
        }

        public void Dispose()
        {
            // No resources.
        }

        private static BoidsSimConfig Sanitize(BoidsSimConfig cfg)
        {
            if (cfg.BoidCount < 1) cfg.BoidCount = 1;

            if (cfg.MaxSpeed <= 0f) cfg.MaxSpeed = 1f;
            if (cfg.Bounds <= 0f) cfg.Bounds = 1f;

            if (cfg.NeighborRadius <= 0f) cfg.NeighborRadius = 1f;
            if (cfg.SeparationRadius <= 0f) cfg.SeparationRadius = 0.5f;

            if (cfg.SimulationHz < 1) cfg.SimulationHz = 60;
            if (cfg.ChunkCapacity < 1) cfg.ChunkCapacity = 128;
            if (cfg.InitialCapacity < cfg.BoidCount) cfg.InitialCapacity = cfg.BoidCount;

            if (cfg.WorkerCount < 1) cfg.WorkerCount = 1;
            if (cfg.HotSpeedThreshold <= 0f) cfg.HotSpeedThreshold = cfg.MaxSpeed;

            return cfg;
        }
    }
}
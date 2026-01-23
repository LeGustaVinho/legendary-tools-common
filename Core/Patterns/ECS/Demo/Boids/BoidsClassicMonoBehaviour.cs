using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids
{
    /// <summary>
    /// Heavy boids without ECS: arrays + naive O(n^2) per frame.
    /// This is the baseline "no ECS, single thread".
    /// </summary>
    public sealed class BoidsClassicMonoBehaviour : MonoBehaviour
    {
        [Header("Boids")] [SerializeField] private int boidCount = 5000;
        [SerializeField] private float bounds = 50f;
        [SerializeField] private float maxSpeed = 8f;

        [Header("Rules")] [SerializeField] private float neighborRadius = 3.5f;
        [SerializeField] private float separationRadius = 1.2f;
        [SerializeField] private float alignmentWeight = 1.0f;
        [SerializeField] private float cohesionWeight = 0.7f;
        [SerializeField] private float separationWeight = 1.3f;

        [Header("Debug")] [SerializeField] private bool drawGizmos = true;
        [SerializeField] private int gizmosMax = 2000;

        private Vector3[] _pos;
        private Vector3[] _vel;

        private void Awake()
        {
            _pos = new Vector3[boidCount];
            _vel = new Vector3[boidCount];

            UnityEngine.Random.InitState(12345);

            for (int i = 0; i < boidCount; i++)
            {
                _pos[i] = new Vector3(
                    UnityEngine.Random.Range(-bounds, bounds),
                    UnityEngine.Random.Range(-bounds, bounds),
                    UnityEngine.Random.Range(-bounds, bounds));

                _vel[i] = UnityEngine.Random.insideUnitSphere * (maxSpeed * 0.5f);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            float neighborR2 = neighborRadius * neighborRadius;
            float sepR2 = separationRadius * separationRadius;

            for (int i = 0; i < boidCount; i++)
            {
                Vector3 p0 = _pos[i];
                Vector3 v0 = _vel[i];

                Vector3 alignment = Vector3.zero;
                Vector3 cohesion = Vector3.zero;
                Vector3 separation = Vector3.zero;
                int neighbors = 0;

                for (int j = 0; j < boidCount; j++)
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

                    alignment = (alignment * inv).normalized * maxSpeed - v0;
                    cohesion = (cohesion * inv - p0).normalized * maxSpeed - v0;
                    separation = separation.normalized * maxSpeed - v0;

                    Vector3 accel =
                        alignment * alignmentWeight +
                        cohesion * cohesionWeight +
                        separation * separationWeight;

                    v0 += accel * dt;
                }

                float sp = v0.magnitude;
                if (sp > maxSpeed) v0 = v0 * (maxSpeed / (sp + 1e-6f));

                p0 += v0 * dt;

                if (p0.x < -bounds)
                {
                    p0.x = -bounds;
                    v0.x = -v0.x;
                }
                else if (p0.x > bounds)
                {
                    p0.x = bounds;
                    v0.x = -v0.x;
                }

                if (p0.y < -bounds)
                {
                    p0.y = -bounds;
                    v0.y = -v0.y;
                }
                else if (p0.y > bounds)
                {
                    p0.y = bounds;
                    v0.y = -v0.y;
                }

                if (p0.z < -bounds)
                {
                    p0.z = -bounds;
                    v0.z = -v0.z;
                }
                else if (p0.z > bounds)
                {
                    p0.z = bounds;
                    v0.z = -v0.z;
                }

                _pos[i] = p0;
                _vel[i] = v0;
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            if (_pos == null) return;

            int n = Mathf.Min(gizmosMax, _pos.Length);
            for (int i = 0; i < n; i++)
            {
                Gizmos.DrawSphere(_pos[i], 0.08f);
            }
        }
    }
}
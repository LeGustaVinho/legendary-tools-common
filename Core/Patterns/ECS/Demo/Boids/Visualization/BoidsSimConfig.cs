using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Visualization
{
    [System.Serializable]
    public struct BoidsSimConfig
    {
        [Header("World")] public int InitialCapacity;
        public int ChunkCapacity;
        public bool Deterministic;
        public int SimulationHz;

        [Header("Boids")] public int BoidCount;
        public float Bounds;
        public float MaxSpeed;

        [Header("Rules")] public float NeighborRadius;
        public float SeparationRadius;
        public float AlignmentWeight;
        public float CohesionWeight;
        public float SeparationWeight;

        [Header("ECS Multi")] public int WorkerCount;
        public float HotSpeedThreshold;

        public static BoidsSimConfig Default => new()
        {
            InitialCapacity = 50000,
            ChunkCapacity = 128,
            Deterministic = true,
            SimulationHz = 60,

            BoidCount = 20000,
            Bounds = 50f,
            MaxSpeed = 8f,

            NeighborRadius = 3.5f,
            SeparationRadius = 1.2f,
            AlignmentWeight = 1.0f,
            CohesionWeight = 0.7f,
            SeparationWeight = 1.3f,

            WorkerCount = 4,
            HotSpeedThreshold = 7.0f
        };
    }
}
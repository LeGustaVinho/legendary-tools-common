namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Systems
{
    /// <summary>
    /// Worker-local partial reduction for deterministic merge.
    /// </summary>
    public readonly struct WorkerBoidReduction
    {
        public readonly int Worker;
        public readonly float SpeedSum;
        public readonly int Count;

        public WorkerBoidReduction(int worker, float speedSum, int count)
        {
            Worker = worker;
            SpeedSum = speedSum;
            Count = count;
        }
    }
}
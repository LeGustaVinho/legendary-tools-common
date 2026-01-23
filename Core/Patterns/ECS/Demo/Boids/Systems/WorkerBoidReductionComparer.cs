using System.Collections.Generic;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Systems
{
    /// <summary>
    /// Deterministic ordering by Worker index.
    /// </summary>
    public sealed class WorkerBoidReductionComparer : IComparer<WorkerBoidReduction>
    {
        public static readonly WorkerBoidReductionComparer Instance = new();

        public int Compare(WorkerBoidReduction x, WorkerBoidReduction y)
        {
            return x.Worker.CompareTo(y.Worker);
        }
    }
}
using System;
using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Visualization
{
    public interface IBoidsBackend : IDisposable
    {
        int BoidCount { get; }

        /// <summary>
        /// Advances the simulation by deltaTime.
        /// </summary>
        void Step(float deltaTime);

        /// <summary>
        /// Copies positions into destination array (length must be >= BoidCount).
        /// </summary>
        void CopyPositions(Vector3[] destination);
    }
}
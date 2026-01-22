using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Systems
{
    /// <summary>
    /// Fixed-step clock with accumulator. No allocations; intended for simulation determinism.
    /// </summary>
    internal struct FixedTickClock
    {
        private float _accumulator;

        /// <summary>
        /// Gets the number of simulation ticks per second.
        /// </summary>
        public readonly int SimulationHz;
        /// <summary>
        /// Gets the time delta per tick (1/Hz).
        /// </summary>
        public readonly float TickDelta;

        /// <summary>
        /// Gets the current tick count.
        /// </summary>
        public int Tick { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FixedTickClock"/> struct.
        /// </summary>
        /// <param name="simulationHz">Frequency of simulation steps.</param>
        /// <param name="initialTick">Initial tick count.</param>
        public FixedTickClock(int simulationHz, int initialTick = 0)
        {
            if (simulationHz < 1)
                throw new ArgumentOutOfRangeException(nameof(simulationHz), "SimulationHz must be >= 1.");

            SimulationHz = simulationHz;
            TickDelta = 1.0f / simulationHz;

            _accumulator = 0f;
            Tick = initialTick;
        }

        /// <summary>
        /// Resets the clock to a specific tick count and zero accumulator.
        /// </summary>
        /// <param name="tick">New tick count.</param>
        public void Reset(int tick)
        {
            _accumulator = 0f;
            Tick = tick;
        }

        /// <summary>
        /// Accumulates variable time into the simulation buffer.
        /// </summary>
        /// <param name="deltaTime">Time to add.</param>
        public void Accumulate(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            _accumulator += deltaTime;
        }

        /// <summary>
        /// Consumes a tick from the accumulator if enough time has passed.
        /// </summary>
        /// <param name="tick">The current tick count explicitly.</param>
        /// <returns>True if a tick was consumed.</returns>
        public bool TryConsumeTick(out int tick)
        {
            if (_accumulator < TickDelta)
            {
                tick = Tick;
                return false;
            }

            _accumulator -= TickDelta;
            Tick++;
            tick = Tick;
            return true;
        }
    }
}
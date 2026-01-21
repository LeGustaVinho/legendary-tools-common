using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Systems
{
    /// <summary>
    /// Fixed-step clock with accumulator. No allocations; intended for simulation determinism.
    /// </summary>
    internal struct FixedTickClock
    {
        private float _accumulator;

        public readonly int SimulationHz;
        public readonly float TickDelta;

        public int Tick { get; private set; }

        public FixedTickClock(int simulationHz, int initialTick = 0)
        {
            if (simulationHz < 1)
                throw new ArgumentOutOfRangeException(nameof(simulationHz), "SimulationHz must be >= 1.");

            SimulationHz = simulationHz;
            TickDelta = 1.0f / simulationHz;

            _accumulator = 0f;
            Tick = initialTick;
        }

        public void Reset(int tick)
        {
            _accumulator = 0f;
            Tick = tick;
        }

        public void Accumulate(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            _accumulator += deltaTime;
        }

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
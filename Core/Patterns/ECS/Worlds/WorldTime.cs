namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    /// <summary>
    /// Deterministic time snapshot for the ECS world.
    /// Simulation time advances in fixed ticks. Presentation may use variable delta time.
    /// </summary>
    public readonly struct WorldTime
    {
        /// <summary>Current simulation tick.</summary>
        public readonly int Tick;

        /// <summary>Fixed simulation delta time in seconds (1 / SimulationHz).</summary>
        public readonly float TickDelta;

        /// <summary>Variable delta time in seconds (frame delta), intended only for presentation.</summary>
        public readonly float PresentationDeltaTime;

        /// <summary>Simulation tick rate in Hz.</summary>
        public readonly int SimulationHz;

        internal WorldTime(int tick, float tickDelta, float presentationDeltaTime, int simulationHz)
        {
            Tick = tick;
            TickDelta = tickDelta;
            PresentationDeltaTime = presentationDeltaTime;
            SimulationHz = simulationHz;
        }

        public override string ToString()
        {
            return
                $"WorldTime(Tick={Tick}, TickDelta={TickDelta}, PresentationDelta={PresentationDeltaTime}, Hz={SimulationHz})";
        }
    }
}
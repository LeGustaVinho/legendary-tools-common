namespace LegendaryTools.Common.Core.Patterns.ECS.Systems
{
    /// <summary>
    /// Fixed tick phases. Order must remain stable for determinism and networking.
    /// </summary>
    public enum SystemPhase : byte
    {
        /// <summary>
        /// Optional in MVP. Useful later for barriers, applying last tick ECB, etc.
        /// </summary>
        BeginSimulation = 0,

        /// <summary>
        /// Main simulation phase. Must be deterministic and data-only.
        /// </summary>
        Simulation = 1,

        /// <summary>
        /// End of simulation. Playback ECB happens here.
        /// </summary>
        EndSimulation = 2,

        /// <summary>
        /// Presentation phase. Must not mutate simulation state.
        /// </summary>
        Presentation = 3
    }
}
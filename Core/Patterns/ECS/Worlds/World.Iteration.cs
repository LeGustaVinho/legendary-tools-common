namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Enters an iteration scope. Structural changes are forbidden while iterating.
        /// Intended for internal use by query iteration APIs.
        /// </summary>
        internal void EnterIteration()
        {
            State.IterationDepth++;
        }

        /// <summary>
        /// Exits an iteration scope.
        /// Intended for internal use by query iteration APIs.
        /// </summary>
        internal void ExitIteration()
        {
            State.IterationDepth--;
        }
    }
}
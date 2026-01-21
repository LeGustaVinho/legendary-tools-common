using LegendaryTools.Common.Core.Patterns.ECS.Commands;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Begins a fixed tick update scope. While updating, structural changes are only allowed via ECB.
        /// </summary>
        /// <param name="tick">Current tick.</param>
        public void BeginTick(int tick)
        {
            if (State.IsUpdating)
                // Nested BeginTick is almost always a bug.
                throw new System.InvalidOperationException("World is already updating.");

            EnsureEcbInitialized();

            State.IsUpdating = true;

            // Deterministic tick state for simulation.
            State.CurrentTick = tick;

            // Reset system order for this tick.
            State.CurrentSystemOrder = 0;

            StateEcb.Reset(tick);
        }

        /// <summary>
        /// Ends the tick update scope and plays back structural changes recorded in the ECB.
        /// </summary>
        public void EndTick()
        {
            if (!State.IsUpdating) throw new System.InvalidOperationException("World is not updating.");

            // Unlock first so playback can apply immediate changes safely.
            State.IsUpdating = false;

            // Playback in a defined point: end of tick.
            StateEcb.Playback();
        }

        /// <summary>
        /// Gets the command buffer for the current tick update scope.
        /// </summary>
        public ICommandBuffer ECB
        {
            get
            {
                EnsureEcbInitialized();
                return StateEcb;
            }
        }
    }
}
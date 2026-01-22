using LegendaryTools.Common.Core.Patterns.ECS.Components;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Returns a determinism-friendly manifest for the registered component set.
        /// Useful for peer validation/handshake in lockstep.
        /// </summary>
        public ComponentManifest GetComponentManifest()
        {
            return Storage.ComponentManifest;
        }
    }
}
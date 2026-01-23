namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Visualization
{
    public static class BoidsBackendFactory
    {
        public static IBoidsBackend Create(BoidsBackendType type, BoidsSimConfig cfg, uint seed)
        {
            return type switch
            {
                BoidsBackendType.EcsSingleThread => new BoidsEcsSingleBackend(cfg, seed),
                BoidsBackendType.EcsMultiThread => new BoidsEcsMultiBackend(cfg, seed),
                BoidsBackendType.ClassicNoEcs => new BoidsClassicBackend(cfg, seed),
                _ => new BoidsEcsSingleBackend(cfg, seed)
            };
        }
    }
}
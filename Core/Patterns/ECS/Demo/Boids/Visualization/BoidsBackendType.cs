namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Visualization
{
    public enum BoidsBackendType : byte
    {
        EcsSingleThread = 0,
        EcsMultiThread = 1,
        ClassicNoEcs = 2
    }
}
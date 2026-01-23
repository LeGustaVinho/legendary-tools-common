using LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Visualization
{
    public sealed class BoidsEcsSingleBackend : BoidsEcsBackendBase
    {
        public BoidsEcsSingleBackend(BoidsSimConfig cfg, uint seed = 12345) : base(cfg, seed)
        {
        }

        protected override void InstallSystems(Scheduler scheduler)
        {
            scheduler.AddSystem(
                SystemPhase.Simulation,
                new BoidsEcsSingleThreadSystem(
                    Cfg.NeighborRadius,
                    Cfg.SeparationRadius,
                    Cfg.MaxSpeed,
                    Cfg.Bounds,
                    Cfg.AlignmentWeight,
                    Cfg.CohesionWeight,
                    Cfg.SeparationWeight),
                100);
        }
    }
}
using LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Systems;
using LegendaryTools.Common.Core.Patterns.ECS.Systems;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo.Boids.Visualization
{
    public sealed class BoidsEcsMultiBackend : BoidsEcsBackendBase
    {
        public BoidsEcsMultiBackend(BoidsSimConfig cfg, uint seed = 12345) : base(cfg, seed)
        {
        }

        protected override void InstallSystems(Scheduler scheduler)
        {
            scheduler.AddSystem(
                SystemPhase.Simulation,
                new BoidsEcsMultiThreadSystem(
                    Cfg.WorkerCount,
                    Cfg.NeighborRadius,
                    Cfg.SeparationRadius,
                    Cfg.MaxSpeed,
                    Cfg.Bounds,
                    Cfg.AlignmentWeight,
                    Cfg.CohesionWeight,
                    Cfg.SeparationWeight,
                    Cfg.HotSpeedThreshold),
                100);
        }
    }
}
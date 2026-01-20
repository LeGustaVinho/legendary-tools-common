using UnityEngine;

namespace LegendaryTools.Common.Core.Patterns.ECS.Demo
{
    /// <summary>
    /// Demo configuration for the deterministic ECS template.
    /// </summary>
    [CreateAssetMenu(menuName = "LegendaryTools/ECS Demo Config", fileName = "EcsDemoConfig")]
    public sealed class EcsDemoConfig : ScriptableObject
    {
        [Header("Simulation")] [Min(1)] public int InitialEntityCount = 5000;

        [Min(1)] public int TickRate = 60;

        [Min(0)] public int SpawnPerTick = 0;

        [Min(1)] public int LifetimeMinTicks = 300;

        [Min(1)] public int LifetimeMaxTicks = 900;

        [Header("Presentation")] public bool EnableHud = true;

        [Min(1)] public int HudRefreshHz = 4;

        [Header("Safety")] public bool StopOnException = true;
    }
}
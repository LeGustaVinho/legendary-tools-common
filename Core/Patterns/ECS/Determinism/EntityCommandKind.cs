#nullable enable

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Supported ECB structural command kinds.
    /// </summary>
    public enum EntityCommandKind : byte
    {
        CreateEntity = 1,
        DestroyEntity = 2,
        AddComponent = 3,
        RemoveComponent = 4
    }
}
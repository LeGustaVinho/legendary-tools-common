#nullable enable

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Compact ECB command record.
    /// </summary>
    public readonly struct EntityCommand
    {
        public readonly EntityCommandKind Kind;
        public readonly int Tick;
        public readonly int SystemOrder;
        public readonly long SortKey;
        public readonly int Sequence;

        public readonly Entity Entity;
        public readonly ComponentTypeId ComponentId;

        // For CreateEntity, we keep the created id explicit for future "entity reservation" support.
        public readonly Entity CreatedEntity;

        public EntityCommand(
            EntityCommandKind kind,
            int tick,
            int systemOrder,
            long sortKey,
            int sequence,
            Entity entity,
            ComponentTypeId componentId,
            Entity createdEntity)
        {
            Kind = kind;
            Tick = tick;
            SystemOrder = systemOrder;
            SortKey = sortKey;
            Sequence = sequence;

            Entity = entity;
            ComponentId = componentId;
            CreatedEntity = createdEntity;
        }

        public override string ToString()
        {
            return
                $"ECB({Kind}, t={Tick}, sys={SystemOrder}, key={SortKey}, seq={Sequence}, e={Entity}, c={ComponentId.Value})";
        }
    }
}
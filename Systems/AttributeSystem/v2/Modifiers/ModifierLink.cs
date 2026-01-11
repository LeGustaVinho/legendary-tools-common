using System;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Represents a single link from (source modifier attribute) -> (target attribute).
    /// Used mainly for Reactive mode bookkeeping.
    /// </summary>
    public sealed class ModifierLink : IDisposable
    {
        public Entity Source { get; }
        public Entity Target { get; }
        public AttributeInstance Modifier { get; }
        public AttributeInstance TargetAttribute { get; }

        public bool IsApplied { get; private set; }

        private bool _disposed;

        public ModifierLink(Entity source, Entity target, AttributeInstance modifier, AttributeInstance targetAttribute)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Modifier = modifier ?? throw new ArgumentNullException(nameof(modifier));
            TargetAttribute = targetAttribute ?? throw new ArgumentNullException(nameof(targetAttribute));
        }

        public void MarkApplied(bool applied)
        {
            IsApplied = applied;
        }

        public void Dispose()
        {
            _disposed = true;
        }

        public bool IsDisposed => _disposed;
    }
}
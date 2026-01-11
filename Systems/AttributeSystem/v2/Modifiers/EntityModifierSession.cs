using System;
using System.Collections.Generic;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// A reactive relationship between a source entity and a target entity.
    /// It listens for state changes (attribute value changes) and re-evaluates eligibility.
    /// </summary>
    public sealed class EntityModifierSession : IDisposable
    {
        private readonly AttributeModifierCoordinator _coordinator;
        private readonly Entity _source;
        private readonly Entity _target;
        private readonly ReactiveIneligibleBehavior _ineligibleBehavior;

        private readonly List<ModifierLink> _links = new();

        private bool _disposed;

        public Entity Source => _source;
        public Entity Target => _target;

        internal EntityModifierSession(
            AttributeModifierCoordinator coordinator,
            Entity source,
            Entity target,
            ReactiveIneligibleBehavior ineligibleBehavior)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _ineligibleBehavior = ineligibleBehavior;

            HookEntity(_source);
            HookEntity(_target);

            // Initial build and apply.
            _coordinator.BuildOrRefreshLinks(_source, _target, _links);
            _coordinator.EvaluateAndApplyLinks(_links, _ineligibleBehavior);
        }

        private void HookEntity(Entity entity)
        {
            entity.AttributeAdded += OnAttributeAdded;

            foreach (AttributeInstance a in entity.GetAllAttributes())
            {
                a.ValueChanged += OnAttributeValueChanged;
                a.BaseValueChanged += OnAttributeBaseValueChanged;
            }
        }

        private void UnhookEntity(Entity entity)
        {
            entity.AttributeAdded -= OnAttributeAdded;

            foreach (AttributeInstance a in entity.GetAllAttributes())
            {
                a.ValueChanged -= OnAttributeValueChanged;
                a.BaseValueChanged -= OnAttributeBaseValueChanged;
            }
        }

        private void OnAttributeAdded(Entity owner, AttributeInstance instance)
        {
            instance.ValueChanged += OnAttributeValueChanged;
            instance.BaseValueChanged += OnAttributeBaseValueChanged;

            // Rebuild links because a new attribute can unlock eligibility (missing target, etc.).
            _coordinator.BuildOrRefreshLinks(_source, _target, _links);
            _coordinator.EvaluateAndApplyLinks(_links, _ineligibleBehavior);
        }

        private void OnAttributeValueChanged(AttributeInstance source, AttributeValue oldValue, AttributeValue newValue)
        {
            // Any state change on source OR target may affect conditions (future) or clamp references.
            _coordinator.EvaluateAndApplyLinks(_links, _ineligibleBehavior);
        }

        private void OnAttributeBaseValueChanged(AttributeInstance source, AttributeValue oldValue, AttributeValue newValue)
        {
            _coordinator.EvaluateAndApplyLinks(_links, _ineligibleBehavior);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            UnhookEntity(_source);
            UnhookEntity(_target);

            // On dispose, remove all applied modifiers from this session.
            _coordinator.RemoveLinks(_links);
            _links.Clear();

            foreach (ModifierLink link in _links) link.Dispose();
        }
    }
}

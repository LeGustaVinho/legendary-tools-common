using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [AddComponentMenu("Legendary Tools/View Data Event Binder")]
    public sealed class ViewDataEventBinder : BindingPollingBehaviour
    {
        [SerializeField] private List<ViewDataEventBinding> eventBindings =
            new List<ViewDataEventBinding>();

        private readonly Dictionary<string, EventBindingRuntimeState> runtimeStates =
            new Dictionary<string, EventBindingRuntimeState>();

        public IReadOnlyList<ViewDataEventBinding> EventBindings => eventBindings;

        public void ProcessManualBindings()
        {
            ProcessBindingTiming(BindingUpdateTiming.Manual);
        }

        public void ProcessAll()
        {
            EnsureBindingIds();

            for (int i = 0; i < eventBindings.Count; i++)
            {
                ProcessEventBinding(i);
            }
        }

        public bool ProcessEventBinding(int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= eventBindings.Count)
            {
                return false;
            }

            ViewDataEventBinding binding = eventBindings[bindingIndex];
            if (binding == null)
            {
                return false;
            }

            binding.EnsureId();
            EventBindingRuntimeState state = GetOrCreateState(binding.Id);
            return ProcessEventBinding(binding, state);
        }

        protected override void ProcessBindingTiming(BindingUpdateTiming timing)
        {
            EnsureBindingIds();

            for (int i = 0; i < eventBindings.Count; i++)
            {
                ViewDataEventBinding binding = eventBindings[i];
                if (binding == null || binding.UpdateTiming != timing)
                {
                    continue;
                }

                ProcessEventBinding(i);
            }
        }

        private static bool ProcessEventBinding(
            ViewDataEventBinding binding,
            EventBindingRuntimeState state)
        {
            if (!binding.Enabled)
            {
                state.Reset();
                return false;
            }

            int sourceCount = binding.Sources?.Count ?? 0;
            if (sourceCount == 0)
            {
                state.Reset();
                return false;
            }

            state.EnsureSourceCount(sourceCount);
            object[] currentValues = state.CurrentValues;
            BindingMemberMetadata[] sourceMetadata = state.SourceMetadata;

            for (int i = 0; i < sourceCount; i++)
            {
                BindingSource source = binding.Sources[i];
                if (source == null || source.Endpoint == null)
                {
                    return false;
                }

                if (!BindingBackendRegistry.MemberBackend.TryGetMetadata(
                        source.Endpoint,
                        out sourceMetadata[i],
                        out _))
                {
                    return false;
                }

                if (!sourceMetadata[i].CanRead ||
                    !BindingBackendRegistry.MemberBackend.TryRead(source.Endpoint, out currentValues[i], out _))
                {
                    return false;
                }
            }

            if (!state.Initialized)
            {
                Array.Copy(currentValues, state.LastValues, sourceCount);
                state.Initialized = true;

                if (!binding.TriggerOnInitialize)
                {
                    return false;
                }

                return EvaluateConditionsOnInitialize(
                    binding,
                    currentValues,
                    sourceMetadata);
            }

            bool[] changedSources = state.ChangedSources;
            bool hasChanges = false;

            for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
            {
                changedSources[sourceIndex] = !ValuesEqual(
                    state.LastValues[sourceIndex],
                    currentValues[sourceIndex]);
                hasChanges |= changedSources[sourceIndex];
            }

            bool anyTriggered = hasChanges && EvaluateConditionsForChanges(
                binding,
                changedSources,
                state.LastValues,
                currentValues,
                sourceMetadata);

            Array.Copy(currentValues, state.LastValues, sourceCount);
            return anyTriggered;
        }

        private static bool EvaluateConditionsOnInitialize(
            ViewDataEventBinding binding,
            IReadOnlyList<object> currentValues,
            IReadOnlyList<BindingMemberMetadata> sourceMetadata)
        {
            bool anyTriggered = false;

            if (binding.Conditions == null)
            {
                return false;
            }

            for (int i = 0; i < binding.Conditions.Count; i++)
            {
                EventBindingCondition condition = binding.Conditions[i];
                if (condition == null || !condition.Enabled || condition.Clauses == null || condition.Clauses.Count == 0)
                {
                    continue;
                }

                if (!EventBindingConditionEvaluator.TryEvaluate(
                        condition,
                        currentValues,
                        sourceMetadata,
                        out bool conditionResult,
                        out _) ||
                    !conditionResult)
                {
                    continue;
                }

                int sourceIndex = condition.Clauses[0].SourceIndex;
                object newValue = sourceIndex >= 0 && sourceIndex < currentValues.Count
                    ? currentValues[sourceIndex]
                    : null;

                condition.InvokeActions(null, newValue);
                anyTriggered = true;
            }

            return anyTriggered;
        }

        private static bool EvaluateConditionsForChanges(
            ViewDataEventBinding binding,
            IReadOnlyList<bool> changedSources,
            IReadOnlyList<object> oldValues,
            IReadOnlyList<object> currentValues,
            IReadOnlyList<BindingMemberMetadata> sourceMetadata)
        {
            bool anyTriggered = false;

            if (binding.Conditions == null)
            {
                return false;
            }

            for (int i = 0; i < binding.Conditions.Count; i++)
            {
                EventBindingCondition condition = binding.Conditions[i];
                if (condition == null || !condition.Enabled)
                {
                    continue;
                }

                int triggeringSourceIndex = FindTriggeringSourceIndex(condition, changedSources);
                if (triggeringSourceIndex < 0)
                {
                    continue;
                }

                if (!EventBindingConditionEvaluator.TryEvaluate(
                        condition,
                        currentValues,
                        sourceMetadata,
                        out bool conditionResult,
                        out _) ||
                    !conditionResult)
                {
                    continue;
                }

                condition.InvokeActions(
                    oldValues[triggeringSourceIndex],
                    currentValues[triggeringSourceIndex]);
                anyTriggered = true;
            }

            return anyTriggered;
        }

        private static int FindTriggeringSourceIndex(
            EventBindingCondition condition,
            IReadOnlyList<bool> changedSources)
        {
            if (condition.Clauses == null)
            {
                return -1;
            }

            for (int i = 0; i < condition.Clauses.Count; i++)
            {
                EventBindingConditionClause clause = condition.Clauses[i];
                if (clause == null)
                {
                    continue;
                }

                int sourceIndex = clause.SourceIndex;
                if (sourceIndex >= 0 &&
                    sourceIndex < changedSources.Count &&
                    changedSources[sourceIndex])
                {
                    return sourceIndex;
                }
            }

            return -1;
        }

        private EventBindingRuntimeState GetOrCreateState(string bindingId)
        {
            if (!runtimeStates.TryGetValue(bindingId, out EventBindingRuntimeState state))
            {
                state = new EventBindingRuntimeState();
                runtimeStates.Add(bindingId, state);
            }

            return state;
        }

        private void EnsureBindingIds()
        {
            for (int i = 0; i < eventBindings.Count; i++)
            {
                eventBindings[i]?.EnsureId();
            }
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (left is UnityEngine.Object leftObject && leftObject == null)
            {
                left = null;
            }

            if (right is UnityEngine.Object rightObject && rightObject == null)
            {
                right = null;
            }

            return Equals(left, right);
        }
    }
}

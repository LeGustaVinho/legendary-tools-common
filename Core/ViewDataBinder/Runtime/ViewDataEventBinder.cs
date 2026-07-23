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
        private readonly HashSet<string> activeTaskBindingIds = new HashSet<string>();
        private readonly List<string> completedTaskBindingIds = new List<string>();
        private readonly BindingContextResolver contextResolver = new BindingContextResolver();

        public IReadOnlyList<ViewDataEventBinding> EventBindings => eventBindings;

        public void ProcessManualBindings()
        {
            ProcessBindingTiming(BindingUpdateTiming.Manual);
        }

        public BindingSyncResult ProcessManualBinding(int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= eventBindings.Count)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"Event binding index {bindingIndex} is outside the valid range.");
            }

            ViewDataEventBinding binding = eventBindings[bindingIndex];
            if (binding == null)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    "The event binding is null.");
            }

            if (binding.UpdateTiming != BindingUpdateTiming.Manual)
            {
                return BindingSyncResult.NoChange(
                    $"Event binding '{binding.Name}' does not use Manual polling.");
            }

            return ProcessEventBindingDetailed(bindingIndex, out _);
        }

        public BindingSyncResult ProcessManualBinding(string bindingIdOrName)
        {
            return TryGetBindingIndex(bindingIdOrName, out int bindingIndex)
                ? ProcessManualBinding(bindingIndex)
                : new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"No event binding with ID or name '{bindingIdOrName}' was found.");
        }

        public BindingSyncResult ProcessEventBinding(string bindingIdOrName, out bool triggered)
        {
            if (!TryGetBindingIndex(bindingIdOrName, out int bindingIndex))
            {
                triggered = false;
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"No event binding with ID or name '{bindingIdOrName}' was found.");
            }

            return ProcessEventBindingDetailed(bindingIndex, out triggered);
        }

        public int ProcessManualBindingsForSource(UnityEngine.Object sourceObject)
        {
            if (sourceObject == null)
            {
                return 0;
            }

            EnsureBindingIds();
            int processedCount = 0;
            for (int i = 0; i < eventBindings.Count; i++)
            {
                ViewDataEventBinding binding = eventBindings[i];
                if (binding == null || binding.UpdateTiming != BindingUpdateTiming.Manual)
                {
                    continue;
                }

                using (BindingResolutionScope.Push(this, contextResolver, null))
                {
                    if (!UsesSourceObject(binding, sourceObject))
                    {
                        continue;
                    }
                }

                ProcessEventBindingDetailed(i, out _);
                processedCount++;
            }

            return processedCount;
        }

        public bool InvalidateEventBinding(int bindingIndex)
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
            if (runtimeStates.TryGetValue(binding.Id, out EventBindingRuntimeState state))
            {
                state.Reset();
            }

            ResetActionRuntimeState(binding);
            InvalidateBindingCaches(binding);
            activeTaskBindingIds.Remove(binding.Id);
            return true;
        }

        public bool InvalidateEventBinding(string bindingIdOrName)
        {
            return TryGetBindingIndex(bindingIdOrName, out int bindingIndex) &&
                   InvalidateEventBinding(bindingIndex);
        }

        public bool IsTaskRunning(int bindingIndex)
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
            return runtimeStates.TryGetValue(binding.Id, out EventBindingRuntimeState state) &&
                   state.HasRunningTasks;
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
            BindingSyncResult result = ProcessEventBindingDetailed(bindingIndex, out bool triggered);
            return result.IsSuccess && triggered;
        }

        public BindingSyncResult ProcessEventBindingDetailed(int bindingIndex, out bool triggered)
        {
            triggered = false;

            if (bindingIndex < 0 || bindingIndex >= eventBindings.Count)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"Event binding index {bindingIndex} is outside the valid range.");
            }

            ViewDataEventBinding binding = eventBindings[bindingIndex];
            if (binding == null)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    "The event binding is null.");
            }

            binding.EnsureId();
            EventBindingRuntimeState state = GetOrCreateState(binding.Id);
            using (BindingResolutionScope.Push(this, contextResolver, null))
            {
                PrepareMissingEndpointRecovery(binding, state);

                BindingSyncResult result = ProcessEventBinding(binding, state, out triggered);
                if (result.Status == BindingSyncStatus.UnresolvedInstance &&
                    result.EndpointRole == BindingEndpointRole.Source)
                {
                    result = ApplyMissingEndpointPolicy(binding, state, result, out bool retryTriggered);
                    triggered |= retryTriggered;
                }
                else
                {
                    state.ClearMissingEndpoint();
                }

                if (state.HasRunningTasks)
                {
                    activeTaskBindingIds.Add(binding.Id);
                }
                else
                {
                    activeTaskBindingIds.Remove(binding.Id);
                }

                return ApplyErrorPolicy(binding, state, result);
            }
        }

        public bool TryGetLastResult(int bindingIndex, out BindingSyncResult result)
        {
            result = default;

            if (bindingIndex < 0 || bindingIndex >= eventBindings.Count)
            {
                return false;
            }

            ViewDataEventBinding binding = eventBindings[bindingIndex];
            if (binding == null || string.IsNullOrWhiteSpace(binding.Id))
            {
                return false;
            }

            if (!runtimeStates.TryGetValue(binding.Id, out EventBindingRuntimeState state) ||
                !state.HasResult)
            {
                return false;
            }

            result = state.LastResult;
            return true;
        }

        protected override void Update()
        {
            base.Update();
            ObserveActiveTaskBindings();
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

        private static BindingSyncResult ProcessEventBinding(
            ViewDataEventBinding binding,
            EventBindingRuntimeState state,
            out bool triggered)
        {
            triggered = false;

            if (!binding.Enabled)
            {
                state.Reset();
                ResetActionRuntimeState(binding);
                return new BindingSyncResult(BindingSyncStatus.Disabled, "Event binding is disabled.");
            }

            if (state.RuntimeDisabled)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.Disabled,
                    "Event binding was disabled by its error policy. Invalidate it to retry.");
            }

            bool taskRunning = state.HasRunningTasks;
            if (taskRunning)
            {
                bool observationSucceeded = TryObserveAsyncActions(
                    binding,
                    out taskRunning,
                    out string taskError);
                state.HasRunningTasks = taskRunning;
                if (!observationSucceeded)
                {
                    return new BindingSyncResult(BindingSyncStatus.ActionFailed, taskError);
                }
            }

            int sourceCount = binding.Sources?.Count ?? 0;
            if (sourceCount == 0)
            {
                state.Reset();
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidSourceCount,
                    "An event binding requires at least one Source.");
            }

            state.EnsureSourceCount(sourceCount);
            object[] currentValues = state.CurrentValues;
            BindingMemberMetadata[] sourceMetadata = state.SourceMetadata;

            for (int i = 0; i < sourceCount; i++)
            {
                BindingSource source = binding.Sources[i];
                if (source == null || source.Endpoint == null)
                {
                    return new BindingSyncResult(
                        BindingSyncStatus.InvalidMemberPath,
                        $"Source {i + 1} is null.");
                }

                if (!BindingBackendRegistry.MemberBackend.TryGetMetadata(
                        source.Endpoint,
                        out sourceMetadata[i],
                        out string metadataError))
                {
                    return new BindingSyncResult(
                        ClassifyEndpointFailure(source.Endpoint),
                        $"Source {i + 1}: {metadataError}",
                        BindingEndpointRole.Source);
                }

                if (!sourceMetadata[i].CanRead)
                {
                    return new BindingSyncResult(
                        BindingSyncStatus.ReadFailed,
                        $"Source {i + 1} is not readable.",
                        BindingEndpointRole.Source);
                }

                if (!BindingBackendRegistry.MemberBackend.TryRead(
                        source.Endpoint,
                        out currentValues[i],
                        out string readError))
                {
                    BindingSyncStatus readStatus = ClassifyEndpointFailure(source.Endpoint) ==
                                                   BindingSyncStatus.UnresolvedInstance
                        ? BindingSyncStatus.UnresolvedInstance
                        : BindingSyncStatus.ReadFailed;
                    return new BindingSyncResult(
                        readStatus,
                        $"Source {i + 1}: {readError}",
                        BindingEndpointRole.Source);
                }
            }

            if (!state.Initialized)
            {
                Array.Copy(currentValues, state.LastValues, sourceCount);
                state.Initialized = true;

                if (!binding.TriggerOnInitialize)
                {
                    return BindingSyncResult.NoChange("Event binding initialized without triggering actions.");
                }

                bool evaluationSucceeded = EvaluateConditionsOnInitialize(
                    binding,
                    currentValues,
                    sourceMetadata,
                    out triggered,
                    out bool initializedTaskRunning,
                    out BindingSyncStatus evaluationStatus,
                    out string evaluationError);
                state.HasRunningTasks |= initializedTaskRunning;
                if (!evaluationSucceeded)
                {
                    return new BindingSyncResult(evaluationStatus, evaluationError);
                }

                return triggered
                    ? BindingSyncResult.Success("One or more event conditions triggered during initialization.")
                    : BindingSyncResult.NoChange("No event condition matched during initialization.");
            }

            bool[] changedSources = state.ChangedSources;
            bool hasChanges = false;

            for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
            {
                changedSources[sourceIndex] = !BindingValueComparer.AreEqual(
                    state.LastValues[sourceIndex],
                    currentValues[sourceIndex]);
                hasChanges |= changedSources[sourceIndex];
            }

            if (!hasChanges)
            {
                return taskRunning
                    ? BindingSyncResult.NoChange("One or more Task actions are running.")
                    : BindingSyncResult.NoChange();
            }

            bool conditionEvaluationSucceeded = EvaluateConditionsForChanges(
                binding,
                changedSources,
                state.LastValues,
                currentValues,
                sourceMetadata,
                out triggered,
                out bool invokedTaskRunning,
                out BindingSyncStatus conditionStatus,
                out string conditionError);
            state.HasRunningTasks |= invokedTaskRunning;
            if (!conditionEvaluationSucceeded)
            {
                return new BindingSyncResult(conditionStatus, conditionError);
            }

            Array.Copy(currentValues, state.LastValues, sourceCount);
            return triggered
                ? BindingSyncResult.Success("One or more event conditions triggered.")
                : BindingSyncResult.NoChange("Source values changed, but no event condition matched.");
        }

        private static bool EvaluateConditionsOnInitialize(
            ViewDataEventBinding binding,
            IReadOnlyList<object> currentValues,
            IReadOnlyList<BindingMemberMetadata> sourceMetadata,
            out bool anyTriggered,
            out bool taskRunning,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            anyTriggered = false;
            taskRunning = false;
            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;

            if (binding.Conditions == null)
            {
                return true;
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
                        out string evaluationError))
                {
                    failureStatus = BindingSyncStatus.ConditionFailed;
                    error = $"Condition {i + 1}: {evaluationError}";
                    return false;
                }

                if (!conditionResult)
                {
                    continue;
                }

                int sourceIndex = condition.Clauses[0].SourceIndex;
                object newValue = sourceIndex >= 0 && sourceIndex < currentValues.Count
                    ? currentValues[sourceIndex]
                    : null;

                bool actionsSucceeded = condition.TryInvokeActions(
                    null,
                    newValue,
                    out bool conditionTaskRunning,
                    out string actionError);
                taskRunning |= conditionTaskRunning;
                if (!actionsSucceeded)
                {
                    failureStatus = BindingSyncStatus.ActionFailed;
                    error = $"Condition {i + 1} action failed: {actionError}";
                    return false;
                }
                anyTriggered = true;
            }

            return true;
        }

        private static bool EvaluateConditionsForChanges(
            ViewDataEventBinding binding,
            IReadOnlyList<bool> changedSources,
            IReadOnlyList<object> oldValues,
            IReadOnlyList<object> currentValues,
            IReadOnlyList<BindingMemberMetadata> sourceMetadata,
            out bool anyTriggered,
            out bool taskRunning,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            anyTriggered = false;
            taskRunning = false;
            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;

            if (binding.Conditions == null)
            {
                return true;
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
                        out string evaluationError))
                {
                    failureStatus = BindingSyncStatus.ConditionFailed;
                    error = $"Condition {i + 1}: {evaluationError}";
                    return false;
                }

                if (!conditionResult)
                {
                    continue;
                }

                bool actionsSucceeded = condition.TryInvokeActions(
                    oldValues[triggeringSourceIndex],
                    currentValues[triggeringSourceIndex],
                    out bool conditionTaskRunning,
                    out string actionError);
                taskRunning |= conditionTaskRunning;
                if (!actionsSucceeded)
                {
                    failureStatus = BindingSyncStatus.ActionFailed;
                    error = $"Condition {i + 1} action failed: {actionError}";
                    return false;
                }
                anyTriggered = true;
            }

            return true;
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

        protected override void ResetRuntimeState()
        {
            for (int i = 0; i < eventBindings.Count; i++)
            {
                ResetActionRuntimeState(eventBindings[i]);
            }

            runtimeStates.Clear();
            activeTaskBindingIds.Clear();
            completedTaskBindingIds.Clear();
            contextResolver.Invalidate();
        }

        private void ObserveActiveTaskBindings()
        {
            if (activeTaskBindingIds.Count == 0)
            {
                return;
            }

            completedTaskBindingIds.Clear();
            foreach (string bindingId in activeTaskBindingIds)
            {
                if (!TryGetBindingById(bindingId, out ViewDataEventBinding binding) ||
                    !runtimeStates.TryGetValue(bindingId, out EventBindingRuntimeState state))
                {
                    completedTaskBindingIds.Add(bindingId);
                    continue;
                }

                bool observationSucceeded = TryObserveAsyncActions(
                    binding,
                    out bool taskRunning,
                    out string error);
                state.HasRunningTasks = taskRunning;
                if (!observationSucceeded)
                {
                    ApplyErrorPolicy(
                        binding,
                        state,
                        new BindingSyncResult(BindingSyncStatus.ActionFailed, error));
                }

                if (!taskRunning)
                {
                    completedTaskBindingIds.Add(bindingId);
                }
            }

            for (int i = 0; i < completedTaskBindingIds.Count; i++)
            {
                activeTaskBindingIds.Remove(completedTaskBindingIds[i]);
            }
        }

        private bool TryGetBindingById(string bindingId, out ViewDataEventBinding binding)
        {
            for (int i = 0; i < eventBindings.Count; i++)
            {
                binding = eventBindings[i];
                if (binding != null && string.Equals(binding.Id, bindingId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            binding = null;
            return false;
        }

        private bool TryGetBindingIndex(string bindingIdOrName, out int bindingIndex)
        {
            bindingIndex = -1;
            if (string.IsNullOrWhiteSpace(bindingIdOrName))
            {
                return false;
            }

            EnsureBindingIds();
            for (int i = 0; i < eventBindings.Count; i++)
            {
                ViewDataEventBinding binding = eventBindings[i];
                if (binding != null &&
                    (string.Equals(binding.Id, bindingIdOrName, StringComparison.Ordinal) ||
                     string.Equals(binding.Name, bindingIdOrName, StringComparison.Ordinal)))
                {
                    bindingIndex = i;
                    return true;
                }
            }

            return false;
        }

        private void PrepareMissingEndpointRecovery(
            ViewDataEventBinding binding,
            EventBindingRuntimeState state)
        {
            if (state.RuntimeDisabled || !state.SourceEndpointMissing ||
                !AreSourceEndpointsAvailable(binding.Sources))
            {
                return;
            }

            InvalidateBindingCaches(binding);
            state.ResetObservation();
            state.ClearMissingEndpoint();
        }

        private BindingSyncResult ApplyMissingEndpointPolicy(
            ViewDataEventBinding binding,
            EventBindingRuntimeState state,
            BindingSyncResult result,
            out bool triggered)
        {
            triggered = false;
            if (result.Status != BindingSyncStatus.UnresolvedInstance ||
                result.EndpointRole != BindingEndpointRole.Source)
            {
                return result;
            }

            state.MarkSourceEndpointMissing(binding.SourceMissingPolicy);

            switch (binding.SourceMissingPolicy)
            {
                case MissingEndpointPolicy.Wait:
                    return BindingSyncResult.NoChange(
                        "Waiting for the missing Source endpoint. " + result.Message);

                case MissingEndpointPolicy.Disable:
                    state.RuntimeDisabled = true;
                    return new BindingSyncResult(
                        BindingSyncStatus.Disabled,
                        "Event binding disabled because a Source endpoint is missing. Invalidate it to retry.",
                        BindingEndpointRole.Source);

                case MissingEndpointPolicy.ClearTarget:
                    return BindingSyncResult.NoChange(
                        "Event bindings have no Target to clear; waiting for the Source endpoint.");

                case MissingEndpointPolicy.UseFallback:
                    return new BindingSyncResult(
                        BindingSyncStatus.FallbackFailed,
                        "Event bindings do not define a fallback value for missing Sources.",
                        BindingEndpointRole.Source);

                case MissingEndpointPolicy.ReResolve:
                    InvalidateBindingCaches(binding);
                    state.ResetObservation();
                    BindingSyncResult retryResult = ProcessEventBinding(binding, state, out triggered);
                    if (retryResult.Status == BindingSyncStatus.UnresolvedInstance)
                    {
                        state.MarkSourceEndpointMissing(binding.SourceMissingPolicy);
                    }
                    else
                    {
                        state.ClearMissingEndpoint();
                    }

                    return retryResult;

                case MissingEndpointPolicy.ReportError:
                    return result;

                default:
                    return new BindingSyncResult(
                        BindingSyncStatus.InvalidMemberPath,
                        $"Unsupported missing endpoint policy: {binding.SourceMissingPolicy}.",
                        BindingEndpointRole.Source);
            }
        }

        private void InvalidateBindingCaches(ViewDataEventBinding binding)
        {
            contextResolver.Invalidate();
            if (!(BindingBackendRegistry.MemberBackend is IBindingMemberCacheInvalidator invalidator) ||
                binding?.Sources == null)
            {
                return;
            }

            for (int i = 0; i < binding.Sources.Count; i++)
            {
                invalidator.Invalidate(binding.Sources[i]?.Endpoint);
            }
        }

        private BindingSyncResult ApplyErrorPolicy(
            ViewDataEventBinding binding,
            EventBindingRuntimeState state,
            BindingSyncResult result)
        {
            state.LastResult = result;
            state.HasResult = true;
            if (result.IsSuccess)
            {
                state.LastLoggedStatus = BindingSyncStatus.Success;
                state.LastLoggedMessage = null;
                return result;
            }

            if (result.Status == BindingSyncStatus.Disabled)
            {
                return result;
            }

            string message = $"Event binding '{binding.Name}' failed with {result.Status}: {result.Message}";
            switch (binding.ErrorPolicy)
            {
                case BindingErrorPolicy.ReportOnly:
                    break;

                case BindingErrorPolicy.LogOnce:
                    if (state.LastLoggedStatus != result.Status ||
                        !string.Equals(state.LastLoggedMessage, result.Message, StringComparison.Ordinal))
                    {
                        Debug.LogWarning(message, this);
                        state.LastLoggedStatus = result.Status;
                        state.LastLoggedMessage = result.Message;
                    }
                    break;

                case BindingErrorPolicy.LogEveryTime:
                    Debug.LogWarning(message, this);
                    break;

                case BindingErrorPolicy.DisableUntilReset:
                    state.RuntimeDisabled = true;
                    Debug.LogWarning(message + " The binding was disabled until reset.", this);
                    break;

                case BindingErrorPolicy.ThrowException:
                    throw new InvalidOperationException(message);

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return result;
        }

        private static bool TryObserveAsyncActions(
            ViewDataEventBinding binding,
            out bool taskRunning,
            out string error)
        {
            taskRunning = false;
            string firstError = null;

            if (binding.Conditions == null)
            {
                error = string.Empty;
                return true;
            }

            for (int i = 0; i < binding.Conditions.Count; i++)
            {
                EventBindingCondition condition = binding.Conditions[i];
                if (condition == null)
                {
                    continue;
                }

                bool conditionSucceeded = condition.TryObserveTasks(
                    out bool conditionTaskRunning,
                    out string conditionError);
                taskRunning |= conditionTaskRunning;
                if (!conditionSucceeded && firstError == null)
                {
                    firstError = $"Condition {i + 1}: {conditionError}";
                }
            }

            error = firstError ?? string.Empty;
            return firstError == null;
        }

        private static void ResetActionRuntimeState(ViewDataEventBinding binding)
        {
            if (binding?.Conditions == null)
            {
                return;
            }

            for (int i = 0; i < binding.Conditions.Count; i++)
            {
                binding.Conditions[i]?.ResetRuntimeState();
            }
        }

        private static bool UsesSourceObject(
            ViewDataEventBinding binding,
            UnityEngine.Object sourceObject)
        {
            if (binding?.Sources == null)
            {
                return false;
            }

            for (int i = 0; i < binding.Sources.Count; i++)
            {
                BindingInstanceReference instance = binding.Sources[i]?.Endpoint?.Instance;
                if (instance != null && instance.ReferencesObject(sourceObject))
                {
                    return true;
                }
            }

            return false;
        }

        private static BindingSyncStatus ClassifyEndpointFailure(BindingEndpoint endpoint)
        {
            return GetEndpointAvailability(endpoint) == BindingEndpointAvailability.Missing
                ? BindingSyncStatus.UnresolvedInstance
                : BindingSyncStatus.InvalidMemberPath;
        }

        private static BindingEndpointAvailability GetEndpointAvailability(BindingEndpoint endpoint)
        {
            if (endpoint == null || endpoint.Instance == null)
            {
                return BindingEndpointAvailability.InvalidConfiguration;
            }

            if (BindingBackendRegistry.MemberBackend is IBindingEndpointAvailabilityBackend availabilityBackend)
            {
                return availabilityBackend.GetEndpointAvailability(endpoint, out _);
            }

            return endpoint.Instance.TryResolve(out _, out _)
                ? BindingEndpointAvailability.Available
                : BindingEndpointAvailability.Missing;
        }

        private static bool AreSourceEndpointsAvailable(IReadOnlyList<BindingSource> sources)
        {
            if (sources == null || sources.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < sources.Count; i++)
            {
                if (GetEndpointAvailability(sources[i]?.Endpoint) !=
                    BindingEndpointAvailability.Available)
                {
                    return false;
                }
            }

            return true;
        }

    }
}

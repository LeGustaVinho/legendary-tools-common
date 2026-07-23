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
        private readonly HashSet<EventBindingExecutionEntry> activeTaskBindings =
            new HashSet<EventBindingExecutionEntry>();
        private readonly List<EventBindingExecutionEntry> completedTaskBindings =
            new List<EventBindingExecutionEntry>();
        private readonly Dictionary<string, EventBindingExecutionEntry> executionEntriesById =
            new Dictionary<string, EventBindingExecutionEntry>(StringComparer.Ordinal);
        private readonly List<EventBindingExecutionEntry>[] executionBuckets =
            new List<EventBindingExecutionEntry>[6];
        private readonly BindingContextResolver contextResolver = new BindingContextResolver();
        private bool executionBucketsBuilt;
        private readonly BindingRuntimeStatistics statistics = new BindingRuntimeStatistics();

        public IReadOnlyList<ViewDataEventBinding> EventBindings => eventBindings;

        public BindingRuntimeStatistics Statistics => statistics;

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
                state.InvalidatePlan();
            }

            ResetActionRuntimeState(binding);
            InvalidateBindingCaches(binding);
            if (executionEntriesById.TryGetValue(binding.Id, out EventBindingExecutionEntry entry))
            {
                activeTaskBindings.Remove(entry);
            }
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

            EnsureExecutionBuckets();
            ViewDataEventBinding binding = eventBindings[bindingIndex];
            if (binding == null ||
                !executionEntriesById.TryGetValue(binding.Id, out EventBindingExecutionEntry entry))
            {
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    "The event binding is null or has no execution entry.");
            }

            return ProcessEventBindingDetailed(entry, out triggered);
        }

        private BindingSyncResult ProcessEventBindingDetailed(
            EventBindingExecutionEntry entry,
            out bool triggered)
        {
            triggered = false;
            ViewDataEventBinding binding = entry.Binding;
            EventBindingRuntimeState state = entry.State;
            BindingPerformanceSample performanceSample = statistics.BeginSample();

            try
            {
                using (BindingResolutionScope.Push(this, contextResolver, null))
                {
                    if (!PrepareMissingEndpointRecovery(
                            binding,
                            state,
                            out BindingSyncResult cachedResult))
                    {
                        statistics.AvoidedEndpointRetries++;
                        statistics.Record(cachedResult);
                        return ApplyErrorPolicy(binding, state, cachedResult);
                    }

                    BindingSyncResult result = ProcessEventBinding(binding, state, out triggered);
                    if (result.Status == BindingSyncStatus.UnresolvedInstance &&
                        result.EndpointRole == BindingEndpointRole.Source)
                    {
                        result = ApplyMissingEndpointPolicy(
                            binding,
                            state,
                            result,
                            out bool retryTriggered);
                        triggered |= retryTriggered;
                    }
                    else
                    {
                        state.ClearMissingEndpoint();
                    }

                    if (state.HasRunningTasks)
                    {
                        activeTaskBindings.Add(entry);
                    }
                    else
                    {
                        activeTaskBindings.Remove(entry);
                    }

                    statistics.Record(result);
                    return ApplyErrorPolicy(binding, state, result);
                }
            }
            finally
            {
                statistics.EndSample(binding.Name, performanceSample);
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

        protected override void PrepareRuntime()
        {
            EnsureExecutionBuckets();
        }

        protected override bool HasBindingsForTiming(BindingUpdateTiming timing)
        {
            EnsureExecutionBuckets();
            List<EventBindingExecutionEntry> bucket = executionBuckets[(int)timing];
            return bucket != null && bucket.Count > 0;
        }

        protected override bool HasAdditionalScheduledWork(BindingUpdateTiming timing)
        {
            return timing == BindingUpdateTiming.Update && activeTaskBindings.Count > 0;
        }

        protected override void AfterScheduledTiming(BindingUpdateTiming timing)
        {
            if (timing == BindingUpdateTiming.Update)
            {
                ObserveActiveTaskBindings();
            }
        }

        protected override void ProcessBindingTiming(BindingUpdateTiming timing)
        {
            EnsureExecutionBuckets();
            List<EventBindingExecutionEntry> bucket = executionBuckets[(int)timing];
            if (bucket == null)
            {
                return;
            }

#if UNITY_2020_2_OR_NEWER
            using (BindingRuntimeProfiler.ProcessTiming.Auto())
#endif
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    ProcessEventBindingDetailed(bucket[i], out _);
                }
            }
        }

        public void RebuildExecutionPlan()
        {
            InvalidateConditionRuntimeCaches();
            foreach (EventBindingRuntimeState state in runtimeStates.Values)
            {
                state.InvalidatePlan();
            }

            executionBucketsBuilt = false;
            RebuildExecutionBuckets();
        }

        public void ReleaseRuntimeResources()
        {
            for (int i = 0; i < eventBindings.Count; i++)
            {
                ReleaseActionRuntimeResources(eventBindings[i]);
            }

            foreach (EventBindingRuntimeState state in runtimeStates.Values)
            {
                state.ReleaseResources();
            }

            runtimeStates.Clear();
            activeTaskBindings.Clear();
            completedTaskBindings.Clear();
            executionEntriesById.Clear();
            executionBucketsBuilt = false;
            contextResolver.Invalidate();
        }

        private void EnsureExecutionBuckets()
        {
            if (!executionBucketsBuilt)
            {
                RebuildExecutionBuckets();
            }
        }

        private void RebuildExecutionBuckets()
        {
            EnsureBindingIds();
            executionEntriesById.Clear();
            for (int i = 0; i < executionBuckets.Length; i++)
            {
                if (executionBuckets[i] == null)
                {
                    executionBuckets[i] = new List<EventBindingExecutionEntry>();
                }
                else
                {
                    executionBuckets[i].Clear();
                }
            }

            for (int i = 0; i < eventBindings.Count; i++)
            {
                ViewDataEventBinding binding = eventBindings[i];
                if (binding == null)
                {
                    continue;
                }

                EventBindingExecutionEntry entry = new EventBindingExecutionEntry(
                    i,
                    binding,
                    GetOrCreateState(binding.Id));
                executionEntriesById[binding.Id] = entry;
                executionBuckets[(int)binding.UpdateTiming].Add(entry);
            }

            executionBucketsBuilt = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            InvalidateConditionRuntimeCaches();
            executionBucketsBuilt = false;
            foreach (EventBindingRuntimeState state in runtimeStates.Values)
            {
                state.InvalidatePlan();
            }
        }
#endif

        private BindingSyncResult ProcessEventBinding(
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

            int sourceCount = binding.Sources?.Count ?? 0;
            if (sourceCount == 0)
            {
                state.Reset();
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidSourceCount,
                    "An event binding requires at least one Source.");
            }

            state.EnsureSourceCount(sourceCount);
            if (state.MetadataInitialized)
            {
                if (!state.MatchesResolution(binding))
                {
                    state.ResetObservation(true);
                }
                else
                {
                    statistics.ExecutionPlanCacheHits++;
                }
            }

            object[] currentValues = state.CurrentValues;
            BindingMemberMetadata[] sourceMetadata = state.SourceMetadata;

            if (!state.MetadataInitialized)
            {
                statistics.ExecutionPlanBuilds++;
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
                }

                if (!state.TryCaptureResolution(binding, out string resolutionError))
                {
                    return new BindingSyncResult(
                        BindingSyncStatus.UnresolvedInstance,
                        resolutionError,
                        BindingEndpointRole.Source);
                }

                state.MetadataInitialized = true;
            }

            for (int i = 0; i < sourceCount; i++)
            {
                BindingSource source = binding.Sources[i];
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

            foreach (EventBindingRuntimeState state in runtimeStates.Values)
            {
                state.Reset();
            }

            activeTaskBindings.Clear();
            completedTaskBindings.Clear();
            contextResolver.Invalidate();
        }

        private void ObserveActiveTaskBindings()
        {
            if (activeTaskBindings.Count == 0)
            {
                return;
            }

#if UNITY_2020_2_OR_NEWER
            using (BindingRuntimeProfiler.ObserveTasks.Auto())
#endif
            {
                completedTaskBindings.Clear();
                foreach (EventBindingExecutionEntry entry in activeTaskBindings)
                {
                    ViewDataEventBinding binding = entry.Binding;
                    EventBindingRuntimeState state = entry.State;

                    statistics.ObservedTasks++;
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
                        completedTaskBindings.Add(entry);
                    }
                }

                for (int i = 0; i < completedTaskBindings.Count; i++)
                {
                    activeTaskBindings.Remove(completedTaskBindings[i]);
                }
            }
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

        private bool PrepareMissingEndpointRecovery(
            ViewDataEventBinding binding,
            EventBindingRuntimeState state,
            out BindingSyncResult cachedResult)
        {
            cachedResult = default;
            if (state.RuntimeDisabled || !state.SourceEndpointMissing)
            {
                return true;
            }

            float currentTime = Time.unscaledTime;
            if (state.HasCachedMissingEndpointResult && currentTime < state.NextMissingEndpointRetryTime)
            {
                cachedResult = state.CachedMissingEndpointResult;
                return false;
            }

            if (!AreSourceEndpointsAvailable(binding.Sources))
            {
                BindingSyncResult result = state.HasCachedMissingEndpointResult
                    ? state.CachedMissingEndpointResult
                    : BindingSyncResult.NoChange("Waiting for the missing Source endpoint.");
                state.CacheMissingEndpointResult(
                    result,
                    currentTime + GetMissingEndpointRetryDelay(binding, state));
                cachedResult = result;
                return false;
            }

            InvalidateBindingCaches(binding);
            state.InvalidatePlan();
            state.ClearMissingEndpoint();
            return true;
        }

        private static float GetMissingEndpointRetryDelay(
            ViewDataEventBinding binding,
            EventBindingRuntimeState state)
        {
            int exponent = Math.Min(state.MissingEndpointRetryAttempt, 8);
            float delay = binding.MissingEndpointRetryInterval * (1 << exponent);
            return Math.Min(delay, binding.MaximumMissingEndpointRetryInterval);
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
            BindingSyncResult policyResult;
            switch (binding.SourceMissingPolicy)
            {
                case MissingEndpointPolicy.Wait:
                    policyResult = BindingSyncResult.NoChange(
                        "Waiting for the missing Source endpoint. " + result.Message);
                    break;

                case MissingEndpointPolicy.Disable:
                    state.RuntimeDisabled = true;
                    policyResult = new BindingSyncResult(
                        BindingSyncStatus.Disabled,
                        "Event binding disabled because a Source endpoint is missing. Invalidate it to retry.",
                        BindingEndpointRole.Source);
                    break;

                case MissingEndpointPolicy.ClearTarget:
                    policyResult = BindingSyncResult.NoChange(
                        "Event bindings have no Target to clear; waiting for the Source endpoint.");
                    break;

                case MissingEndpointPolicy.UseFallback:
                    policyResult = new BindingSyncResult(
                        BindingSyncStatus.FallbackFailed,
                        "Event bindings do not define a fallback value for missing Sources.",
                        BindingEndpointRole.Source);
                    break;

                case MissingEndpointPolicy.ReResolve:
                    InvalidateBindingCaches(binding);
                    state.InvalidatePlan();
                    BindingSyncResult retryResult = ProcessEventBinding(binding, state, out triggered);
                    if (retryResult.Status == BindingSyncStatus.UnresolvedInstance)
                    {
                        state.MarkSourceEndpointMissing(binding.SourceMissingPolicy);
                        policyResult = retryResult;
                    }
                    else
                    {
                        state.ClearMissingEndpoint();
                        return retryResult;
                    }
                    break;

                case MissingEndpointPolicy.ReportError:
                    policyResult = result;
                    break;

                default:
                    policyResult = new BindingSyncResult(
                        BindingSyncStatus.InvalidMemberPath,
                        $"Unsupported missing endpoint policy: {binding.SourceMissingPolicy}.",
                        BindingEndpointRole.Source);
                    break;
            }

            if (!state.RuntimeDisabled)
            {
                state.CacheMissingEndpointResult(
                    policyResult,
                    Time.unscaledTime + GetMissingEndpointRetryDelay(binding, state));
            }

            return policyResult;
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

        private void InvalidateConditionRuntimeCaches()
        {
            for (int bindingIndex = 0; bindingIndex < eventBindings.Count; bindingIndex++)
            {
                IReadOnlyList<EventBindingCondition> conditions =
                    eventBindings[bindingIndex]?.Conditions;
                if (conditions == null)
                {
                    continue;
                }

                for (int conditionIndex = 0; conditionIndex < conditions.Count; conditionIndex++)
                {
                    conditions[conditionIndex]?.InvalidateRuntimeCaches();
                }
            }
        }

        private static void ReleaseActionRuntimeResources(ViewDataEventBinding binding)
        {
            if (binding?.Conditions == null)
            {
                return;
            }

            for (int i = 0; i < binding.Conditions.Count; i++)
            {
                binding.Conditions[i]?.ReleaseRuntimeResources();
            }
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

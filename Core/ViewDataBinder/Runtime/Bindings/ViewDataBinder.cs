using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [AddComponentMenu("Legendary Tools/View Data Binder")]
    public sealed class ViewDataBinder : BindingPollingBehaviour
    {
        [SerializeField] private List<ViewDataBinding> bindings = new List<ViewDataBinding>();
        [SerializeField] private List<ViewDataBindingProfileReference> profiles =
            new List<ViewDataBindingProfileReference>();

        private static readonly Dictionary<Type, object> DefaultValueCache =
            new Dictionary<Type, object>();
        private static readonly object DefaultValueCacheLock = new object();

        private readonly Dictionary<string, BindingRuntimeState> runtimeStates =
            new Dictionary<string, BindingRuntimeState>();
        private readonly BindingContextResolver contextResolver = new BindingContextResolver();
        private readonly List<BindingExecutionEntry>[] executionBuckets =
            new List<BindingExecutionEntry>[6];
        private bool executionBucketsBuilt;
        private readonly BindingRuntimeStatistics statistics = new BindingRuntimeStatistics();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCaches()
        {
            lock (DefaultValueCacheLock)
            {
                DefaultValueCache.Clear();
            }
        }

        public IReadOnlyList<ViewDataBinding> Bindings => bindings;

        public BindingRuntimeStatistics Statistics => statistics;

        public IReadOnlyList<ViewDataBindingProfileReference> Profiles => profiles;

        public int AddBinding(ViewDataBinding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            binding.EnsureId();
            bindings.Add(binding);
            RebuildExecutionPlan();
            return bindings.Count - 1;
        }

        public bool ConfigureBinding(int bindingIndex, Action<ViewDataBinding> configure)
        {
            if (configure == null ||
                bindingIndex < 0 ||
                bindingIndex >= bindings.Count ||
                bindings[bindingIndex] == null)
            {
                return false;
            }

            configure(bindings[bindingIndex]);
            bindings[bindingIndex].EnsureId();
            RebuildExecutionPlan();
            return true;
        }

        public bool RemoveBindingAt(int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= bindings.Count)
            {
                return false;
            }

            bindings.RemoveAt(bindingIndex);
            RebuildExecutionPlan();
            return true;
        }

        public void ClearBindings()
        {
            bindings.Clear();
            RebuildExecutionPlan();
        }

        public int AddProfile(ViewDataBindingProfileReference profileReference)
        {
            if (profileReference == null)
            {
                throw new ArgumentNullException(nameof(profileReference));
            }

            profileReference.EnsureId();
            profiles.Add(profileReference);
            RebuildExecutionPlan();
            return profiles.Count - 1;
        }

        public bool ConfigureProfile(
            int profileIndex,
            Action<ViewDataBindingProfileReference> configure)
        {
            if (configure == null ||
                profileIndex < 0 ||
                profileIndex >= profiles.Count ||
                profiles[profileIndex] == null)
            {
                return false;
            }

            configure(profiles[profileIndex]);
            profiles[profileIndex].EnsureId();
            RebuildExecutionPlan();
            return true;
        }

        public bool RemoveProfileAt(int profileIndex)
        {
            if (profileIndex < 0 || profileIndex >= profiles.Count)
            {
                return false;
            }

            profiles.RemoveAt(profileIndex);
            RebuildExecutionPlan();
            return true;
        }

        public void ClearProfiles()
        {
            profiles.Clear();
            RebuildExecutionPlan();
        }

        public bool SetSourceInstance(int bindingIndex, int sourceIndex, object instance)
        {
            if (!TryGetLocalBinding(bindingIndex, out ViewDataBinding binding, out _) ||
                !TrySetSourceInstance(binding, sourceIndex, instance, out bool changed))
            {
                return false;
            }

            if (!changed)
            {
                return true;
            }
            return InvalidateBinding(bindingIndex);
        }

        public bool SetSourceInstance(string bindingIdOrName, int sourceIndex, object instance)
        {
            if (!TryFindBinding(
                    bindingIdOrName,
                    out ViewDataBinding binding,
                    out _,
                    out ViewDataBindingProfileReference profileReference) ||
                profileReference != null ||
                !TrySetSourceInstance(binding, sourceIndex, instance, out bool changed))
            {
                return false;
            }

            if (!changed)
            {
                return true;
            }
            return InvalidateBinding(bindingIdOrName);
        }

        public bool SetProfileSourceRoot(
            int profileIndex,
            object instance,
            Type declaredType = null)
        {
            return SetProfileContext(
                profileIndex,
                BindingContextConstants.ProfileSource,
                instance,
                declaredType);
        }

        public bool SetProfileTargetRoot(
            int profileIndex,
            object instance,
            Type declaredType = null)
        {
            return SetProfileContext(
                profileIndex,
                BindingContextConstants.ProfileTarget,
                instance,
                declaredType);
        }

        public bool SetProfileContext(
            int profileIndex,
            string contextName,
            object instance,
            Type declaredType = null)
        {
            if (!TryGetProfileReference(profileIndex, out ViewDataBindingProfileReference profileReference))
            {
                return false;
            }

            bool changed = instance == null
                ? profileReference.RemoveRuntimeContext(contextName)
                : profileReference.SetRuntimeContext(contextName, instance, declaredType);
            if (!changed && instance != null)
            {
                return false;
            }

            InvalidateProfileReference(profileReference);
            return true;
        }

        public bool ClearProfileContext(int profileIndex, string contextName)
        {
            if (!TryGetProfileReference(profileIndex, out ViewDataBindingProfileReference profileReference))
            {
                return false;
            }

            profileReference.RemoveRuntimeContext(contextName);
            InvalidateProfileReference(profileReference);
            return true;
        }

        public void SynchronizeManualBindings()
        {
            ProcessBindingTiming(BindingUpdateTiming.Manual);
        }

        public BindingSyncResult SynchronizeManualBinding(int bindingIndex)
        {
            if (!TryGetLocalBinding(bindingIndex, out ViewDataBinding binding, out string stateKey))
            {
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"Binding index {bindingIndex} is outside the valid range or null.");
            }

            if (binding.UpdateTiming != BindingUpdateTiming.Manual)
            {
                return BindingSyncResult.NoChange(
                    $"Binding '{binding.Name}' does not use Manual polling.");
            }

            return SynchronizeBindingInternal(binding, stateKey, null);
        }

        public BindingSyncResult SynchronizeManualProfileBinding(
            int profileIndex,
            int bindingIndex)
        {
            if (!TryGetProfileBinding(
                    profileIndex,
                    bindingIndex,
                    out ViewDataBinding binding,
                    out string stateKey,
                    out ViewDataBindingProfileReference profileReference))
            {
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"Profile binding [{profileIndex}, {bindingIndex}] is outside the valid range or null.");
            }

            if (binding.UpdateTiming != BindingUpdateTiming.Manual)
            {
                return BindingSyncResult.NoChange(
                    $"Binding '{binding.Name}' does not use Manual polling.");
            }

            return SynchronizeBindingInternal(binding, stateKey, profileReference);
        }

        public BindingSyncResult SynchronizeManualBinding(string bindingIdOrName)
        {
            if (!TryFindBinding(
                    bindingIdOrName,
                    out ViewDataBinding binding,
                    out string stateKey,
                    out ViewDataBindingProfileReference profileReference))
            {
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"No binding with ID or name '{bindingIdOrName}' was found.");
            }

            if (binding.UpdateTiming != BindingUpdateTiming.Manual)
            {
                return BindingSyncResult.NoChange(
                    $"Binding '{binding.Name}' does not use Manual polling.");
            }

            return SynchronizeBindingInternal(binding, stateKey, profileReference);
        }

        public BindingSyncResult SynchronizeBinding(string bindingIdOrName)
        {
            return TryFindBinding(
                bindingIdOrName,
                out ViewDataBinding binding,
                out string stateKey,
                out ViewDataBindingProfileReference profileReference)
                ? SynchronizeBindingInternal(binding, stateKey, profileReference)
                : new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"No binding with ID or name '{bindingIdOrName}' was found.");
        }

        public int SynchronizeManualBindingsForSource(UnityEngine.Object sourceObject)
        {
            if (sourceObject == null)
            {
                return 0;
            }

            EnsureBindingIds();
            int synchronizedCount = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                ViewDataBinding binding = bindings[i];
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

                SynchronizeBindingInternal(binding, GetLocalStateKey(binding), null);
                synchronizedCount++;
            }

            for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
            {
                ViewDataBindingProfileReference profileReference = profiles[profileIndex];
                if (!IsProfileActive(profileReference))
                {
                    continue;
                }

                IReadOnlyList<ViewDataBinding> profileBindings = profileReference.Profile.Bindings;
                for (int bindingIndex = 0; bindingIndex < profileBindings.Count; bindingIndex++)
                {
                    ViewDataBinding binding = profileBindings[bindingIndex];
                    if (binding == null || binding.UpdateTiming != BindingUpdateTiming.Manual)
                    {
                        continue;
                    }

                    using (BindingResolutionScope.Push(this, contextResolver, profileReference))
                    {
                        if (!UsesSourceObject(binding, sourceObject))
                        {
                            continue;
                        }
                    }

                    SynchronizeBindingInternal(
                        binding,
                        GetProfileStateKey(profileReference, binding),
                        profileReference);
                    synchronizedCount++;
                }
            }

            return synchronizedCount;
        }

        public bool InvalidateBinding(int bindingIndex)
        {
            if (!TryGetLocalBinding(bindingIndex, out ViewDataBinding binding, out string stateKey))
            {
                return false;
            }

            ResetState(stateKey);
            InvalidateBindingCaches(binding);
            return true;
        }

        public bool InvalidateProfileBinding(int profileIndex, int bindingIndex)
        {
            if (!TryGetProfileBinding(
                    profileIndex,
                    bindingIndex,
                    out ViewDataBinding binding,
                    out string stateKey,
                    out _))
            {
                return false;
            }

            ResetState(stateKey);
            InvalidateBindingCaches(binding);
            return true;
        }

        public bool InvalidateBinding(string bindingIdOrName)
        {
            if (!TryFindBinding(
                    bindingIdOrName,
                    out ViewDataBinding binding,
                    out string stateKey,
                    out _))
            {
                return false;
            }

            ResetState(stateKey);
            InvalidateBindingCaches(binding);
            return true;
        }

        public void SynchronizeAll()
        {
            EnsureBindingIds();

            for (int i = 0; i < bindings.Count; i++)
            {
                ViewDataBinding binding = bindings[i];
                if (binding != null)
                {
                    SynchronizeBindingInternal(binding, GetLocalStateKey(binding), null);
                }
            }

            for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
            {
                ViewDataBindingProfileReference profileReference = profiles[profileIndex];
                if (!IsProfileActive(profileReference))
                {
                    continue;
                }

                IReadOnlyList<ViewDataBinding> profileBindings = profileReference.Profile.Bindings;
                for (int bindingIndex = 0; bindingIndex < profileBindings.Count; bindingIndex++)
                {
                    ViewDataBinding binding = profileBindings[bindingIndex];
                    if (binding != null)
                    {
                        SynchronizeBindingInternal(
                            binding,
                            GetProfileStateKey(profileReference, binding),
                            profileReference);
                    }
                }
            }
        }

        public BindingSyncResult SynchronizeBinding(int bindingIndex)
        {
            return TryGetLocalBinding(bindingIndex, out ViewDataBinding binding, out string stateKey)
                ? SynchronizeBindingInternal(binding, stateKey, null)
                : new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"Binding index {bindingIndex} is outside the valid range or null.");
        }

        public BindingSyncResult SynchronizeProfileBinding(int profileIndex, int bindingIndex)
        {
            return TryGetProfileBinding(
                profileIndex,
                bindingIndex,
                out ViewDataBinding binding,
                out string stateKey,
                out ViewDataBindingProfileReference profileReference)
                ? SynchronizeBindingInternal(binding, stateKey, profileReference)
                : new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"Profile binding [{profileIndex}, {bindingIndex}] is outside the valid range or null.");
        }

        public bool TryEvaluateSourceValue(
            int bindingIndex,
            out object value,
            out BindingSyncResult result)
        {
            using (BindingResolutionScope.Push(this, contextResolver, null))
            {
                return TryEvaluateSourceValueCore(bindingIndex, out value, out result);
            }
        }

        private bool TryEvaluateSourceValueCore(
            int bindingIndex,
            out object value,
            out BindingSyncResult result)
        {
            value = null;

            if (bindingIndex < 0 || bindingIndex >= bindings.Count)
            {
                result = new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"Binding index {bindingIndex} is outside the valid range.");
                return false;
            }

            ViewDataBinding binding = bindings[bindingIndex];
            if (binding == null)
            {
                result = new BindingSyncResult(BindingSyncStatus.InvalidMemberPath, "The binding is null.");
                return false;
            }

            binding.EnsureId();
            BindingRuntimeState state = GetOrCreateState(GetLocalStateKey(binding));

            if (binding.Formatter != null &&
                binding.Formatter.Enabled &&
                binding.Direction != BindingSyncDirection.SourceToTarget)
            {
                result = new BindingSyncResult(
                    BindingSyncStatus.FormatterFailed,
                    "Formatters are supported only for Source -> Target bindings because formatting is not reversible.");
                return false;
            }

            if (!TryGetSourceOutputMetadata(
                    binding,
                    state,
                    out BindingMemberMetadata sourceOutputMetadata,
                    out List<BindingMemberMetadata> sourceInputMetadata,
                    out BindingSyncStatus metadataStatus,
                    out string metadataError))
            {
                result = new BindingSyncResult(metadataStatus, metadataError);
                return false;
            }

            if (!TryGetEndpointMetadata(
                    binding.Target,
                    out BindingMemberMetadata targetMetadata,
                    out BindingSyncStatus targetMetadataStatus,
                    out string targetMetadataError))
            {
                result = new BindingSyncResult(targetMetadataStatus, targetMetadataError);
                return false;
            }

            if (!TryValidateConverter(
                    binding,
                    sourceOutputMetadata.ValueType,
                    targetMetadata.ValueType,
                    false,
                    out string converterValidationError))
            {
                result = new BindingSyncResult(BindingSyncStatus.TypeMismatch, converterValidationError);
                return false;
            }

            if (!TryReadSourceOutput(
                    binding,
                    state,
                    sourceOutputMetadata.ValueType,
                    sourceInputMetadata,
                    out object sourceValue,
                    out bool skipSynchronization,
                    out BindingSyncStatus readStatus,
                    out string readError))
            {
                result = new BindingSyncResult(readStatus, readError);
                return false;
            }

            if (skipSynchronization)
            {
                result = BindingSyncResult.NoChange("Synchronization skipped by the null handling policy.");
                return false;
            }

            if (!TryConvertForward(
                    binding,
                    sourceValue,
                    sourceOutputMetadata.ValueType,
                    targetMetadata.ValueType,
                    out value,
                    out skipSynchronization,
                    out BindingSyncStatus converterStatus,
                    out string converterError))
            {
                result = new BindingSyncResult(converterStatus, converterError);
                return false;
            }

            if (skipSynchronization)
            {
                result = BindingSyncResult.NoChange("Synchronization skipped by the null handling policy.");
                return false;
            }

            result = BindingSyncResult.Success();
            return true;
        }

        public bool TryGetPreview(string bindingIdOrName, out BindingPreview preview)
        {
            if (!TryFindBinding(
                    bindingIdOrName,
                    out ViewDataBinding binding,
                    out string stateKey,
                    out ViewDataBindingProfileReference profileReference))
            {
                preview = CreateFailedPreview(
                    BindingSyncStatus.InvalidMemberPath,
                    $"No binding with ID or name '{bindingIdOrName}' was found.");
                return false;
            }

            using (BindingResolutionScope.Push(this, contextResolver, profileReference))
            {
                return TryGetPreviewForBinding(binding, stateKey, out preview);
            }
        }

        public bool TryGetPreview(int bindingIndex, out BindingPreview preview)
        {
            using (BindingResolutionScope.Push(this, contextResolver, null))
            {
                return TryGetPreviewCore(
                    bindingIndex,
                    GetLocalStateKeyForIndex(bindingIndex),
                    out preview);
            }
        }

        public bool TryGetProfilePreview(
            int profileIndex,
            int bindingIndex,
            out BindingPreview preview)
        {
            if (!TryGetProfileBinding(
                    profileIndex,
                    bindingIndex,
                    out ViewDataBinding binding,
                    out string stateKey,
                    out ViewDataBindingProfileReference profileReference))
            {
                preview = CreateFailedPreview(
                    BindingSyncStatus.InvalidMemberPath,
                    $"Profile binding [{profileIndex}, {bindingIndex}] is outside the valid range or null.");
                return false;
            }

            using (BindingResolutionScope.Push(this, contextResolver, profileReference))
            {
                return TryGetPreviewForBinding(binding, stateKey, out preview);
            }
        }

        private bool TryGetPreviewCore(
            int bindingIndex,
            string stateKey,
            out BindingPreview preview)
        {
            preview = default;

            if (bindingIndex < 0 || bindingIndex >= bindings.Count)
            {
                preview = CreateFailedPreview(
                    BindingSyncStatus.InvalidMemberPath,
                    $"Binding index {bindingIndex} is outside the valid range.");
                return false;
            }

            ViewDataBinding binding = bindings[bindingIndex];
            if (binding == null)
            {
                preview = CreateFailedPreview(
                    BindingSyncStatus.InvalidMemberPath,
                    "The binding is null.");
                return false;
            }

            return TryGetPreviewForBinding(binding, stateKey, out preview);
        }

        private bool TryGetPreviewForBinding(
            ViewDataBinding binding,
            string stateKey,
            out BindingPreview preview)
        {
            preview = default;
            if (binding.Formatter != null &&
                binding.Formatter.Enabled &&
                binding.Direction != BindingSyncDirection.SourceToTarget)
            {
                preview = CreateFailedPreview(
                    BindingSyncStatus.FormatterFailed,
                    "Formatters are supported only for Source -> Target bindings.");
                return false;
            }

            binding.EnsureId();
            BindingRuntimeState state = GetOrCreateState(stateKey);

            if (!TryGetSourceOutputMetadata(
                    binding,
                    state,
                    out BindingMemberMetadata sourceMetadata,
                    out List<BindingMemberMetadata> sourceInputMetadata,
                    out BindingSyncStatus sourceMetadataStatus,
                    out string sourceMetadataError))
            {
                preview = CreateFailedPreview(sourceMetadataStatus, sourceMetadataError);
                return false;
            }

            if (!TryGetEndpointMetadata(
                    binding.Target,
                    out BindingMemberMetadata targetMetadata,
                    out BindingSyncStatus targetMetadataStatus,
                    out string targetMetadataError))
            {
                preview = CreateFailedPreview(targetMetadataStatus, targetMetadataError);
                return false;
            }

            if (!TryValidateConverter(
                    binding,
                    sourceMetadata.ValueType,
                    targetMetadata.ValueType,
                    binding.Direction != BindingSyncDirection.SourceToTarget,
                    out string validationError))
            {
                preview = CreateFailedPreview(BindingSyncStatus.TypeMismatch, validationError);
                return false;
            }

            object sourceValue = null;
            object convertedSourceValue = null;
            object targetValue = null;
            object convertedTargetValue = null;
            string informationalMessage = string.Empty;

            bool requiresSourceRead = binding.Direction != BindingSyncDirection.TargetToSource;
            bool requiresForwardConversion = binding.Direction != BindingSyncDirection.TargetToSource;
            if (sourceMetadata.CanRead)
            {
                bool sourceReadSucceeded = TryReadSourceOutput(
                    binding,
                    state,
                    sourceMetadata.ValueType,
                    sourceInputMetadata,
                    out sourceValue,
                    out bool skipForward,
                    out BindingSyncStatus sourceReadStatus,
                    out string sourceReadError);
                if (!sourceReadSucceeded)
                {
                    if (requiresSourceRead)
                    {
                        preview = CreateFailedPreview(sourceReadStatus, sourceReadError);
                        return false;
                    }

                    informationalMessage = AppendPreviewMessage(
                        informationalMessage,
                        "The current Source value could not be read: " + sourceReadError);
                }
                else if (requiresForwardConversion &&
                         !skipForward &&
                         !TryConvertForward(
                             binding,
                             sourceValue,
                             sourceMetadata.ValueType,
                             targetMetadata.ValueType,
                             out convertedSourceValue,
                             out skipForward,
                             out BindingSyncStatus forwardStatus,
                             out string forwardError))
                {
                    preview = new BindingPreview(
                        sourceValue,
                        null,
                        null,
                        null,
                        new BindingSyncResult(forwardStatus, forwardError));
                    return false;
                }

                if (sourceReadSucceeded && requiresForwardConversion && skipForward)
                {
                    informationalMessage = AppendPreviewMessage(
                        informationalMessage,
                        "Forward preview was skipped by the null handling policy.");
                }
            }
            else if (requiresSourceRead)
            {
                preview = CreateFailedPreview(
                    BindingSyncStatus.ReadFailed,
                    "The Source output is not readable.");
                return false;
            }
            else
            {
                informationalMessage = AppendPreviewMessage(
                    informationalMessage,
                    "The Source is write-only; its current value is unavailable.");
            }

            bool requiresTargetRead = binding.Direction != BindingSyncDirection.SourceToTarget;
            bool skipReverse = false;
            if (binding.Direction == BindingSyncDirection.SourceToTarget ||
                (binding.Direction == BindingSyncDirection.TwoWay && !state.Initialized))
            {
                if (targetMetadata.CanRead)
                {
                    if (!BindingBackendRegistry.MemberBackend.TryRead(
                            binding.Target,
                            out targetValue,
                            out string targetReadError))
                    {
                        if (requiresTargetRead)
                        {
                            preview = new BindingPreview(
                                sourceValue,
                                convertedSourceValue,
                                null,
                                null,
                                new BindingSyncResult(BindingSyncStatus.ReadFailed, targetReadError));
                            return false;
                        }

                        informationalMessage = AppendPreviewMessage(
                            informationalMessage,
                            "The current Target value could not be read: " + targetReadError);
                    }
                }
                else if (requiresTargetRead)
                {
                    preview = new BindingPreview(
                        sourceValue,
                        convertedSourceValue,
                        null,
                        null,
                        new BindingSyncResult(
                            BindingSyncStatus.ReadFailed,
                            "The Target path is not readable."));
                    return false;
                }
                else
                {
                    informationalMessage = AppendPreviewMessage(
                        informationalMessage,
                        "The Target is write-only; its current value is unavailable.");
                }

                if (binding.Direction == BindingSyncDirection.TwoWay && !state.Initialized)
                {
                    informationalMessage = AppendPreviewMessage(
                        informationalMessage,
                        "Two-way binding is not initialized; the first synchronization writes Source to Target before reverse conversion.");
                }
            }
            else if (!TryReadReverseValue(
                         binding,
                         sourceMetadata.ValueType,
                         targetMetadata.ValueType,
                         out targetValue,
                         out convertedTargetValue,
                         out skipReverse,
                         out BindingSyncStatus reverseStatus,
                         out string reverseError))
            {
                preview = new BindingPreview(
                    sourceValue,
                    convertedSourceValue,
                    targetValue,
                    null,
                    new BindingSyncResult(reverseStatus, reverseError));
                return false;
            }

            if (skipReverse)
            {
                informationalMessage = AppendPreviewMessage(
                    informationalMessage,
                    "Reverse preview was skipped by the null handling policy.");
            }

            preview = new BindingPreview(
                sourceValue,
                convertedSourceValue,
                targetValue,
                convertedTargetValue,
                BindingSyncResult.Success(informationalMessage));
            return true;
        }

        public bool TryGetLastResult(int bindingIndex, out BindingSyncResult result)
        {
            result = default;

            if (bindingIndex < 0 || bindingIndex >= bindings.Count)
            {
                return false;
            }

            ViewDataBinding binding = bindings[bindingIndex];
            if (binding == null || string.IsNullOrWhiteSpace(binding.Id))
            {
                return false;
            }

            if (!runtimeStates.TryGetValue(GetLocalStateKey(binding), out BindingRuntimeState state) ||
                !state.HasResult)
            {
                return false;
            }

            result = state.LastResult;
            return true;
        }

        public bool TryGetLastResult(string bindingIdOrName, out BindingSyncResult result)
        {
            result = default;
            if (!TryFindBinding(
                    bindingIdOrName,
                    out _,
                    out string stateKey,
                    out _))
            {
                return false;
            }

            if (!runtimeStates.TryGetValue(stateKey, out BindingRuntimeState state) ||
                !state.HasResult)
            {
                return false;
            }

            result = state.LastResult;
            return true;
        }

        public bool TryGetProfileLastResult(
            int profileIndex,
            int bindingIndex,
            out BindingSyncResult result)
        {
            result = default;
            if (!TryGetProfileBinding(
                    profileIndex,
                    bindingIndex,
                    out _,
                    out string stateKey,
                    out _))
            {
                return false;
            }

            if (!runtimeStates.TryGetValue(stateKey, out BindingRuntimeState state) ||
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
            List<BindingExecutionEntry> bucket = executionBuckets[(int)timing];
            return bucket != null && bucket.Count > 0;
        }

        protected override void ProcessBindingTiming(BindingUpdateTiming timing)
        {
            EnsureExecutionBuckets();
            List<BindingExecutionEntry> bucket = executionBuckets[(int)timing];
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
                    BindingExecutionEntry entry = bucket[i];
                    SynchronizeBindingInternal(
                        entry.Binding,
                        entry.StateKey,
                        entry.ProfileReference,
                        entry.State);
                }
            }
        }

        public void RebuildExecutionPlan()
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                profiles[i]?.InvalidateSerializedCaches();
            }

            foreach (BindingRuntimeState state in runtimeStates.Values)
            {
                state.InvalidateExecutionPlan();
                state.ResetSynchronizationValues();
            }

            executionBucketsBuilt = false;
            RebuildExecutionBuckets();
            RefreshScheduledRegistration();
        }

        public void ReleaseRuntimeResources()
        {
            foreach (BindingRuntimeState state in runtimeStates.Values)
            {
                state.ReleaseResources();
            }

            runtimeStates.Clear();
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
            var activeStateKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < executionBuckets.Length; i++)
            {
                if (executionBuckets[i] == null)
                {
                    executionBuckets[i] = new List<BindingExecutionEntry>();
                }
                else
                {
                    executionBuckets[i].Clear();
                }
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                ViewDataBinding binding = bindings[i];
                if (binding == null)
                {
                    continue;
                }

                string stateKey = GetLocalStateKey(binding);
                activeStateKeys.Add(stateKey);
                executionBuckets[(int)binding.UpdateTiming].Add(
                    new BindingExecutionEntry(binding, stateKey, null, GetOrCreateState(stateKey)));
            }

            for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
            {
                ViewDataBindingProfileReference profileReference = profiles[profileIndex];
                if (!IsProfileActive(profileReference))
                {
                    continue;
                }

                IReadOnlyList<ViewDataBinding> profileBindings = profileReference.Profile.Bindings;
                for (int bindingIndex = 0; bindingIndex < profileBindings.Count; bindingIndex++)
                {
                    ViewDataBinding binding = profileBindings[bindingIndex];
                    if (binding == null)
                    {
                        continue;
                    }

                    string stateKey = GetProfileStateKey(profileReference, binding);
                    activeStateKeys.Add(stateKey);
                    executionBuckets[(int)binding.UpdateTiming].Add(
                        new BindingExecutionEntry(
                            binding,
                            stateKey,
                            profileReference,
                            GetOrCreateState(stateKey)));
                }
            }

            if (runtimeStates.Count > activeStateKeys.Count)
            {
                var staleStateKeys = new List<string>();
                foreach (KeyValuePair<string, BindingRuntimeState> pair in runtimeStates)
                {
                    if (!activeStateKeys.Contains(pair.Key))
                    {
                        pair.Value.ReleaseResources();
                        staleStateKeys.Add(pair.Key);
                    }
                }
                for (int i = 0; i < staleStateKeys.Count; i++)
                {
                    runtimeStates.Remove(staleStateKeys[i]);
                }
            }

            executionBucketsBuilt = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                profiles[i]?.InvalidateSerializedCaches();
            }

            executionBucketsBuilt = false;
            foreach (BindingRuntimeState state in runtimeStates.Values)
            {
                state.InvalidateExecutionPlan();
                state.ResetSynchronizationValues();
            }
        }
#endif

        private BindingSyncResult Synchronize(ViewDataBinding binding, BindingRuntimeState state)
        {
            if (!binding.Enabled)
            {
                state.ResetValues();
                return new BindingSyncResult(BindingSyncStatus.Disabled, "Binding is disabled.");
            }

            if (state.RuntimeDisabled)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.Disabled,
                    "Binding was disabled by its error policy. Invalidate it to retry.");
            }

            if (!TryGetOrCompileExecutionPlan(binding, state, out BindingExecutionPlan plan))
            {
                return plan.ValidationResult;
            }

            if (plan.DirectPlan != null)
            {
                statistics.GeneratedDirectSynchronizations++;
                BindingSyncResult directResult = plan.DirectPlan.Synchronize(
                    plan.SourceRootIdentity,
                    plan.TargetRootIdentity,
                    binding.WritePolicy);
                if (directResult.Status == BindingSyncStatus.ReadFailed &&
                    !AreSourceEndpointsAvailable(binding.Sources))
                {
                    return new BindingSyncResult(
                        BindingSyncStatus.UnresolvedInstance,
                        directResult.Message,
                        BindingEndpointRole.Source);
                }

                if (directResult.Status == BindingSyncStatus.WriteFailed &&
                    !IsEndpointAvailable(binding.Target))
                {
                    return new BindingSyncResult(
                        BindingSyncStatus.UnresolvedInstance,
                        directResult.Message,
                        BindingEndpointRole.Target);
                }

                return directResult;
            }

            switch (binding.Direction)
            {
                case BindingSyncDirection.SourceToTarget:
                    return SynchronizeSourceToTarget(
                        binding,
                        state,
                        plan.SourceMetadata,
                        plan.SourceInputMetadata,
                        plan.TargetMetadata,
                        plan.Formatter);

                case BindingSyncDirection.TargetToSource:
                    return SynchronizeTargetToSource(
                        binding,
                        state,
                        plan.SourceMetadata,
                        plan.TargetMetadata);

                case BindingSyncDirection.TwoWay:
                    return SynchronizeTwoWay(
                        binding,
                        state,
                        plan.SourceMetadata,
                        plan.TargetMetadata);

                default:
                    return new BindingSyncResult(
                        BindingSyncStatus.InvalidMemberPath,
                        $"Unsupported direction: {binding.Direction}.");
            }
        }

        private bool TryGetOrCompileExecutionPlan(
            ViewDataBinding binding,
            BindingRuntimeState state,
            out BindingExecutionPlan plan)
        {
            plan = state.ExecutionPlan;
            if (plan.IsCompiled)
            {
                if (!plan.IsValid || plan.MatchesResolution(binding))
                {
                    statistics.ExecutionPlanCacheHits++;
                    return plan.IsValid;
                }

                state.ResetSynchronizationValues();
                plan.Invalidate();
            }

            statistics.ExecutionPlanBuilds++;

            if (binding.Formatter != null &&
                binding.Formatter.Enabled &&
                binding.Direction != BindingSyncDirection.SourceToTarget)
            {
                plan.SetInvalid(new BindingSyncResult(
                    BindingSyncStatus.FormatterFailed,
                    "Formatters are supported only for Source -> Target bindings because formatting is not reversible."));
                return false;
            }

            if (!TryGetSourceOutputMetadata(
                    binding,
                    state,
                    out BindingMemberMetadata sourceMetadata,
                    out List<BindingMemberMetadata> sourceInputMetadata,
                    out BindingSyncStatus sourceMetadataStatus,
                    out string sourceMetadataError))
            {
                plan.SetInvalid(new BindingSyncResult(
                    sourceMetadataStatus,
                    sourceMetadataError,
                    BindingEndpointRole.Source));
                return false;
            }

            if (!TryGetEndpointMetadata(
                    binding.Target,
                    out BindingMemberMetadata targetMetadata,
                    out BindingSyncStatus targetMetadataStatus,
                    out string targetMetadataError))
            {
                plan.SetInvalid(new BindingSyncResult(
                    targetMetadataStatus,
                    targetMetadataError,
                    BindingEndpointRole.Target));
                return false;
            }

            bool requiresReverse = binding.Direction != BindingSyncDirection.SourceToTarget;
            if (!TryValidateConverter(
                    binding,
                    sourceMetadata.ValueType,
                    targetMetadata.ValueType,
                    requiresReverse,
                    out string converterValidationError))
            {
                plan.SetInvalid(new BindingSyncResult(
                    BindingSyncStatus.TypeMismatch,
                    converterValidationError));
                return false;
            }

            if (binding.NullHandling == BindingNullHandlingMode.UseFallback &&
                (binding.Fallback == null || !binding.Fallback.Enabled))
            {
                plan.SetInvalid(new BindingSyncResult(
                    BindingSyncStatus.FallbackFailed,
                    "Null Handling is set to Use Fallback, but Fallback is disabled."));
                return false;
            }

            BindingMemberMetadata[] metadataArray = new BindingMemberMetadata[sourceInputMetadata.Count];
            sourceInputMetadata.CopyTo(metadataArray);
            IBindingFormatter formatter = null;
            if (binding.Formatter != null && binding.Formatter.Enabled)
            {
                BindingFormatterRegistry.TryGet(binding.Formatter.FormatterId, out formatter);
            }

            plan.SetValid(sourceMetadata, targetMetadata, metadataArray, formatter);
            if (!plan.TryCaptureResolution(
                    binding,
                    out BindingEndpointRole unresolvedRole,
                    out string resolutionError))
            {
                plan.SetInvalid(new BindingSyncResult(
                    BindingSyncStatus.UnresolvedInstance,
                    resolutionError,
                    unresolvedRole));
                return false;
            }

            plan.TryCreateGeneratedDirectPlan(
                binding,
                sourceMetadata.ValueType,
                targetMetadata.ValueType);
            return true;
        }

        private static BindingSyncResult SynchronizeSourceToTarget(
            ViewDataBinding binding,
            BindingRuntimeState state,
            BindingMemberMetadata sourceMetadata,
            IReadOnlyList<BindingMemberMetadata> sourceInputMetadata,
            BindingMemberMetadata targetMetadata,
            IBindingFormatter formatter)
        {
            if (!sourceMetadata.CanRead)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.ReadFailed,
                    "The Source output is not readable.",
                    BindingEndpointRole.Source);
            }

            if (!targetMetadata.CanWrite)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.WriteFailed,
                    "The Target path is not writable.",
                    BindingEndpointRole.Target);
            }

            if (!TryReadSourceOutput(
                    binding,
                    state,
                    sourceMetadata.ValueType,
                    sourceInputMetadata,
                    out object sourceValue,
                    out bool skipSynchronization,
                    out BindingSyncStatus readStatus,
                    out string readError,
                    formatter))
            {
                return CreateSourceFailureResult(binding.Sources, readStatus, readError);
            }

            if (skipSynchronization)
            {
                return BindingSyncResult.NoChange("Synchronization skipped because a Source value is null.");
            }

            if (binding.WritePolicy == BindingWritePolicy.WhenValueChanges &&
                state.Initialized &&
                !binding.AlwaysEvaluateTransformation &&
                (state.SourceInputsUnchanged ||
                 (formatter == null && BindingValueComparer.AreEqual(sourceValue, state.LastSourceValue))))
            {
                if (TryGetReusableTransformationFailure(state, out BindingSyncResult cachedFailure))
                {
                    return cachedFailure;
                }

                if (!state.HasResult || state.LastResult.IsSuccess)
                {
                    return BindingSyncResult.NoChange("The raw Source input has not changed.");
                }
            }

            if (!TryConvertForward(
                    binding,
                    sourceValue,
                    sourceMetadata.ValueType,
                    targetMetadata.ValueType,
                    out object targetValue,
                    out skipSynchronization,
                    out BindingSyncStatus converterStatus,
                    out string converterError,
                    state))
            {
                return new BindingSyncResult(converterStatus, converterError);
            }

            if (skipSynchronization)
            {
                return BindingSyncResult.NoChange("Synchronization skipped because the converted value is null.");
            }

            if (binding.WritePolicy == BindingWritePolicy.WhenValueChanges &&
                state.Initialized &&
                BindingValueComparer.AreEqual(targetValue, state.LastTargetValue) &&
                (!state.HasResult || state.LastResult.IsSuccess))
            {
                state.LastSourceValue = sourceValue;
                return BindingSyncResult.NoChange("The converted Source value has not changed.");
            }

            if (!BindingBackendRegistry.MemberBackend.TryWrite(binding.Target, targetValue, out string writeError))
            {
                return CreateEndpointFailureResult(
                    binding.Target,
                    BindingSyncStatus.WriteFailed,
                    writeError,
                    BindingEndpointRole.Target);
            }

            state.Initialized = true;
            state.LastSourceValue = sourceValue;
            state.LastTargetValue = targetValue;
            return BindingSyncResult.Success();
        }

        private static BindingSyncResult SynchronizeTargetToSource(
            ViewDataBinding binding,
            BindingRuntimeState state,
            BindingMemberMetadata sourceMetadata,
            BindingMemberMetadata targetMetadata)
        {
            if (!targetMetadata.CanRead)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.ReadFailed,
                    "The Target path is not readable.",
                    BindingEndpointRole.Target);
            }

            if (!sourceMetadata.CanWrite)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.WriteFailed,
                    "The Source path is not writable.",
                    BindingEndpointRole.Source);
            }

            if (!TryReadTargetRawValue(
                    binding,
                    sourceMetadata.ValueType,
                    targetMetadata.ValueType,
                    out object targetValue,
                    out bool usesSourceFallback,
                    out object sourceValue,
                    out bool skipSynchronization,
                    out BindingSyncStatus readStatus,
                    out string readError,
                    state))
            {
                return readStatus == BindingSyncStatus.ReadFailed
                    ? CreateEndpointFailureResult(
                        binding.Target,
                        readStatus,
                        readError,
                        BindingEndpointRole.Target)
                    : new BindingSyncResult(readStatus, readError);
            }

            if (skipSynchronization)
            {
                return BindingSyncResult.NoChange("Synchronization skipped because the Target value is null.");
            }

            if (binding.WritePolicy == BindingWritePolicy.WhenValueChanges &&
                state.Initialized &&
                !binding.AlwaysEvaluateTransformation &&
                BindingValueComparer.AreEqual(targetValue, state.LastTargetValue))
            {
                if (TryGetReusableTransformationFailure(state, out BindingSyncResult cachedFailure))
                {
                    return cachedFailure;
                }

                if (!state.HasResult || state.LastResult.IsSuccess)
                {
                    return BindingSyncResult.NoChange("The raw Target value has not changed.");
                }
            }

            if (!usesSourceFallback &&
                !TryConvertBackCore(
                    binding.Converter,
                    targetValue,
                    targetMetadata.ValueType,
                    sourceMetadata.ValueType,
                    out sourceValue,
                    out string conversionError))
            {
                bool useFallback = ShouldUseConverterFailureFallback(binding);
                BindingSyncStatus fallbackStatus = BindingSyncStatus.ConverterFailed;
                string fallbackError = conversionError;
                if (!useFallback ||
                    !TryGetFallbackValue(
                        binding,
                        state,
                        sourceMetadata.ValueType,
                        out sourceValue,
                        out fallbackStatus,
                        out fallbackError))
                {
                    return new BindingSyncResult(
                        useFallback ? fallbackStatus : BindingSyncStatus.ConverterFailed,
                        useFallback ? fallbackError : conversionError);
                }
            }

            if (binding.WritePolicy == BindingWritePolicy.WhenValueChanges &&
                state.Initialized &&
                BindingValueComparer.AreEqual(sourceValue, state.LastSourceValue) &&
                (!state.HasResult || state.LastResult.IsSuccess))
            {
                state.LastTargetValue = targetValue;
                return BindingSyncResult.NoChange("The converted Target value has not changed.");
            }

            if (!BindingBackendRegistry.SourceBackend.TryWrite(binding.Sources, sourceValue, out string writeError))
            {
                return CreateSourceFailureResult(
                    binding.Sources,
                    BindingSyncStatus.WriteFailed,
                    writeError);
            }

            state.Initialized = true;
            state.LastSourceValue = sourceValue;
            state.LastTargetValue = targetValue;
            return BindingSyncResult.Success();
        }

        private static BindingSyncResult SynchronizeTwoWay(
            ViewDataBinding binding,
            BindingRuntimeState state,
            BindingMemberMetadata sourceMetadata,
            BindingMemberMetadata targetMetadata)
        {
            if (!sourceMetadata.CanRead || !sourceMetadata.CanWrite)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.WriteFailed,
                    "Two-way binding requires the Source path to be readable and writable.",
                    BindingEndpointRole.Source);
            }

            if (!targetMetadata.CanRead || !targetMetadata.CanWrite)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.WriteFailed,
                    "Two-way binding requires the Target path to be readable and writable.",
                    BindingEndpointRole.Target);
            }

            if (!TryReadSingleSourceValue(
                    binding,
                    sourceMetadata.ValueType,
                    out object sourceValue,
                    out bool skipSource,
                    out BindingSyncStatus sourceReadStatus,
                    out string sourceReadError,
                    state))
            {
                return CreateSourceFailureResult(
                    binding.Sources,
                    sourceReadStatus,
                    sourceReadError);
            }

            if (skipSource)
            {
                return BindingSyncResult.NoChange("Two-way synchronization skipped by the null handling policy.");
            }

            if (!state.Initialized)
            {
                if (!TryConvertForward(
                        binding,
                        sourceValue,
                        sourceMetadata.ValueType,
                        targetMetadata.ValueType,
                        out object initialTargetValue,
                        out bool skipForward,
                        out BindingSyncStatus forwardStatus,
                        out string forwardError,
                        state))
                {
                    return new BindingSyncResult(forwardStatus, forwardError);
                }

                if (skipForward)
                {
                    return BindingSyncResult.NoChange("Two-way synchronization skipped by the null handling policy.");
                }

                if (!TryWriteTargetAndCaptureActualValue(
                        binding,
                        initialTargetValue,
                        out object initializedTargetValue,
                        out BindingSyncStatus initializationStatus,
                        out string initializationError))
                {
                    return CreateEndpointFailureResult(
                        binding.Target,
                        initializationStatus,
                        initializationError,
                        BindingEndpointRole.Target);
                }

                state.Initialized = true;
                state.LastSourceValue = sourceValue;
                state.LastTargetValue = initializedTargetValue;
                return BindingSyncResult.Success("Two-way binding initialized from Source to Target.");
            }

            if (!TryReadTargetRawValue(
                    binding,
                    sourceMetadata.ValueType,
                    targetMetadata.ValueType,
                    out object targetValue,
                    out bool targetUsesSourceFallback,
                    out object fallbackSourceValue,
                    out bool skipTarget,
                    out BindingSyncStatus targetReadStatus,
                    out string targetReadError,
                    state))
            {
                return targetReadStatus == BindingSyncStatus.ReadFailed
                    ? CreateEndpointFailureResult(
                        binding.Target,
                        targetReadStatus,
                        targetReadError,
                        BindingEndpointRole.Target)
                    : new BindingSyncResult(targetReadStatus, targetReadError);
            }

            if (skipTarget)
            {
                return BindingSyncResult.NoChange("Two-way synchronization skipped by the null handling policy.");
            }

            bool sourceChanged = !BindingValueComparer.AreEqual(sourceValue, state.LastSourceValue);
            bool targetChanged = !BindingValueComparer.AreEqual(targetValue, state.LastTargetValue);

            if (!sourceChanged && !targetChanged)
            {
                return BindingSyncResult.NoChange();
            }

            bool sourceWins = sourceChanged &&
                              (!targetChanged || binding.ConflictResolution == BindingConflictResolution.SourceWins);
            if (sourceWins)
            {
                if (!TryConvertForward(
                        binding,
                        sourceValue,
                        sourceMetadata.ValueType,
                        targetMetadata.ValueType,
                        out object sourceAsTargetValue,
                        out bool skipForward,
                        out BindingSyncStatus forwardStatus,
                        out string forwardError,
                        state))
                {
                    return new BindingSyncResult(forwardStatus, forwardError);
                }

                if (skipForward)
                {
                    return BindingSyncResult.NoChange("Two-way synchronization skipped by the null handling policy.");
                }

                if (!TryWriteTargetAndCaptureActualValue(
                        binding,
                        sourceAsTargetValue,
                        out object writtenTargetValue,
                        out BindingSyncStatus writeStatus,
                        out string writeError))
                {
                    return CreateEndpointFailureResult(
                        binding.Target,
                        writeStatus,
                        writeError,
                        BindingEndpointRole.Target);
                }

                state.LastSourceValue = sourceValue;
                state.LastTargetValue = writtenTargetValue;
                return BindingSyncResult.Success(sourceChanged && targetChanged
                    ? "Both sides changed; Source won the conflict."
                    : "Source change propagated to Target through the Converter.");
            }

            object targetAsSourceValue;
            if (targetUsesSourceFallback)
            {
                targetAsSourceValue = fallbackSourceValue;
            }
            else if (!TryConvertBackCore(
                         binding.Converter,
                         targetValue,
                         targetMetadata.ValueType,
                         sourceMetadata.ValueType,
                         out targetAsSourceValue,
                         out string reverseError))
            {
                bool useFallback = ShouldUseConverterFailureFallback(binding);
                BindingSyncStatus fallbackStatus = BindingSyncStatus.ConverterFailed;
                string fallbackError = reverseError;
                if (!useFallback ||
                    !TryGetFallbackValue(
                        binding,
                        state,
                        sourceMetadata.ValueType,
                        out targetAsSourceValue,
                        out fallbackStatus,
                        out fallbackError))
                {
                    return new BindingSyncResult(
                        useFallback ? fallbackStatus : BindingSyncStatus.ConverterFailed,
                        useFallback ? fallbackError : reverseError);
                }
            }

            if (!TryWriteSourceAndCaptureActualValue(
                    binding,
                    targetAsSourceValue,
                    out object writtenSourceValue,
                    out BindingSyncStatus sourceWriteStatus,
                    out string sourceWriteError))
            {
                return CreateSourceFailureResult(
                    binding.Sources,
                    sourceWriteStatus,
                    sourceWriteError);
            }

            state.LastSourceValue = writtenSourceValue;
            state.LastTargetValue = targetValue;
            return BindingSyncResult.Success(sourceChanged && targetChanged
                ? "Both sides changed; Target won the conflict."
                : "Target change propagated to Source through reverse conversion.");
        }

        private static bool TryReadTargetRawValue(
            ViewDataBinding binding,
            Type sourceType,
            Type targetType,
            out object targetValue,
            out bool usesSourceFallback,
            out object fallbackSourceValue,
            out bool skipSynchronization,
            out BindingSyncStatus failureStatus,
            out string error,
            BindingRuntimeState state = null)
        {
            targetValue = null;
            usesSourceFallback = false;
            fallbackSourceValue = null;
            skipSynchronization = false;

            if (!BindingBackendRegistry.MemberBackend.TryRead(
                    binding.Target,
                    out object readTargetValue,
                    out string readError))
            {
                if (ShouldUseReadFailureFallback(binding) &&
                    TryGetFallbackValue(
                        binding,
                        state,
                        sourceType,
                        out fallbackSourceValue,
                        out failureStatus,
                        out error))
                {
                    usesSourceFallback = true;
                    return true;
                }

                failureStatus = BindingSyncStatus.ReadFailed;
                error = readError;
                return false;
            }

            targetValue = readTargetValue;
            if (!TryHandleNullValue(
                    binding,
                    readTargetValue,
                    targetType,
                    out object processedTargetValue,
                    out bool useFallback,
                    out skipSynchronization,
                    out failureStatus,
                    out error))
            {
                return false;
            }

            if (useFallback)
            {
                if (!TryGetFallbackValue(
                        binding,
                        state,
                        sourceType,
                        out fallbackSourceValue,
                        out failureStatus,
                        out error))
                {
                    return false;
                }

                usesSourceFallback = true;
                return true;
            }

            targetValue = processedTargetValue;
            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;
            return true;
        }

        private static bool TryGetSourceOutputMetadata(
            ViewDataBinding binding,
            BindingRuntimeState state,
            out BindingMemberMetadata outputMetadata,
            out List<BindingMemberMetadata> inputMetadata,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            int sourceCount = binding.Sources?.Count ?? 0;
            inputMetadata = state.PrepareSourceMetadataBuffer(Math.Max(1, sourceCount));

            if (binding.Formatter == null || !binding.Formatter.Enabled)
            {
                if (!BindingBackendRegistry.SourceBackend.TryGetMetadata(
                        binding.Sources,
                        out outputMetadata,
                        out error))
                {
                    failureStatus = ClassifySourceMetadataFailure(binding.Sources);
                    return false;
                }

                inputMetadata.Add(outputMetadata);
                failureStatus = BindingSyncStatus.Success;
                return true;
            }

            if (sourceCount == 0)
            {
                outputMetadata = default;
                failureStatus = BindingSyncStatus.InvalidSourceCount;
                error = "A formatter requires at least one Source.";
                return false;
            }

            for (int i = 0; i < sourceCount; i++)
            {
                BindingSource source = binding.Sources[i];
                if (source == null || source.Endpoint == null)
                {
                    outputMetadata = default;
                    failureStatus = BindingSyncStatus.InvalidMemberPath;
                    error = $"Source {i + 1} is null.";
                    return false;
                }

                if (!TryGetEndpointMetadata(
                        source.Endpoint,
                        out BindingMemberMetadata metadata,
                        out failureStatus,
                        out string metadataError))
                {
                    outputMetadata = default;
                    error = $"Source {i + 1}: {metadataError}";
                    return false;
                }

                if (!metadata.CanRead)
                {
                    outputMetadata = default;
                    failureStatus = BindingSyncStatus.ReadFailed;
                    error = $"Source {i + 1} is not readable.";
                    return false;
                }

                inputMetadata.Add(metadata);
            }

            if (!BindingFormatterRegistry.TryGet(binding.Formatter.FormatterId, out IBindingFormatter formatter))
            {
                outputMetadata = default;
                failureStatus = BindingSyncStatus.FormatterFailed;
                error = $"Formatter '{binding.Formatter.FormatterId}' is not registered.";
                return false;
            }

            if (!formatter.TryGetOutputType(inputMetadata, out Type outputType, out error))
            {
                outputMetadata = default;
                failureStatus = BindingSyncStatus.FormatterFailed;
                return false;
            }

            if (outputType == null)
            {
                outputMetadata = default;
                failureStatus = BindingSyncStatus.FormatterFailed;
                error = $"Formatter '{formatter.DisplayName}' returned no output Type.";
                return false;
            }

            outputMetadata = new BindingMemberMetadata(outputType, true, false);
            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;
            return true;
        }

        private static bool TryGetEndpointMetadata(
            BindingEndpoint endpoint,
            out BindingMemberMetadata metadata,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            if (BindingBackendRegistry.MemberBackend.TryGetMetadata(endpoint, out metadata, out error))
            {
                failureStatus = BindingSyncStatus.Success;
                return true;
            }

            failureStatus = ClassifyEndpointFailure(endpoint);
            return false;
        }

        private static BindingSyncStatus ClassifySourceMetadataFailure(
            IReadOnlyList<BindingSource> sources)
        {
            if (sources == null || sources.Count == 0)
            {
                return BindingSyncStatus.InvalidSourceCount;
            }

            if (BindingBackendRegistry.SourceBackend is SingleSourceBindingSourceBackend &&
                sources.Count != 1)
            {
                return BindingSyncStatus.InvalidSourceCount;
            }

            if (sources.Count == 1)
            {
                BindingSource source = sources[0];
                return source == null
                    ? BindingSyncStatus.InvalidMemberPath
                    : ClassifyEndpointFailure(source.Endpoint);
            }

            return BindingSyncStatus.InvalidMemberPath;
        }

        private static BindingSyncStatus ClassifyEndpointFailure(BindingEndpoint endpoint)
        {
            return GetEndpointAvailability(endpoint, out _) == BindingEndpointAvailability.Missing
                ? BindingSyncStatus.UnresolvedInstance
                : BindingSyncStatus.InvalidMemberPath;
        }

        private static BindingEndpointAvailability GetEndpointAvailability(
            BindingEndpoint endpoint,
            out string error)
        {
            if (endpoint == null || endpoint.Instance == null)
            {
                error = "The endpoint or its instance reference is null.";
                return BindingEndpointAvailability.InvalidConfiguration;
            }

            if (BindingBackendRegistry.MemberBackend is IBindingEndpointAvailabilityBackend availabilityBackend)
            {
                return availabilityBackend.GetEndpointAvailability(endpoint, out error);
            }

            if (!endpoint.Instance.TryResolve(out _, out error))
            {
                return BindingEndpointAvailability.Missing;
            }

            error = string.Empty;
            return BindingEndpointAvailability.Available;
        }

        private static bool IsEndpointAvailable(BindingEndpoint endpoint)
        {
            return GetEndpointAvailability(endpoint, out _) == BindingEndpointAvailability.Available;
        }

        private static bool AreSourceEndpointsAvailable(IReadOnlyList<BindingSource> sources)
        {
            if (sources == null || sources.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < sources.Count; i++)
            {
                if (!IsEndpointAvailable(sources[i]?.Endpoint))
                {
                    return false;
                }
            }

            return true;
        }

        private static BindingSyncResult CreateEndpointFailureResult(
            BindingEndpoint endpoint,
            BindingSyncStatus fallbackStatus,
            string message,
            BindingEndpointRole role)
        {
            BindingSyncStatus status = ClassifyEndpointFailure(endpoint) == BindingSyncStatus.UnresolvedInstance
                ? BindingSyncStatus.UnresolvedInstance
                : fallbackStatus;
            return new BindingSyncResult(status, message, role);
        }

        private static BindingSyncResult CreateSourceFailureResult(
            IReadOnlyList<BindingSource> sources,
            BindingSyncStatus fallbackStatus,
            string message)
        {
            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    BindingEndpoint endpoint = sources[i]?.Endpoint;
                    if (ClassifyEndpointFailure(endpoint) == BindingSyncStatus.UnresolvedInstance)
                    {
                        return new BindingSyncResult(
                            BindingSyncStatus.UnresolvedInstance,
                            message,
                            BindingEndpointRole.Source);
                    }
                }
            }

            return new BindingSyncResult(fallbackStatus, message, BindingEndpointRole.Source);
        }

        private static bool TryValidateConverter(
            ViewDataBinding binding,
            Type sourceType,
            Type targetType,
            bool requiresReverse,
            out string error)
        {
            BindingConverter converter = binding.Converter;
            if (converter == null)
            {
                if (sourceType == targetType)
                {
                    error = string.Empty;
                    return true;
                }

                error = $"Type mismatch: source output is '{GetTypeName(sourceType)}' and target is '{GetTypeName(targetType)}'. Assign a compatible Binding Converter to bridge these types.";
                return false;
            }

            if (!converter.CanConvert(sourceType, targetType))
            {
                error = $"Converter '{converter.name}' cannot convert '{GetTypeName(sourceType)}' to '{GetTypeName(targetType)}'.";
                return false;
            }

            if (requiresReverse && !converter.CanConvertBack(targetType, sourceType))
            {
                error = $"Converter '{converter.name}' does not support reverse conversion from '{GetTypeName(targetType)}' to '{GetTypeName(sourceType)}'.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryConvertForward(
            ViewDataBinding binding,
            object sourceValue,
            Type sourceType,
            Type targetType,
            out object targetValue,
            out bool skipSynchronization,
            out BindingSyncStatus failureStatus,
            out string error,
            BindingRuntimeState state = null)
        {
            targetValue = null;
            skipSynchronization = false;

            if (!TryConvertForwardCore(
                    binding.Converter,
                    sourceValue,
                    sourceType,
                    targetType,
                    out targetValue,
                    out error))
            {
                if (!ShouldUseConverterFailureFallback(binding))
                {
                    failureStatus = BindingSyncStatus.ConverterFailed;
                    return false;
                }

                if (!TryGetFallbackValue(binding, state, sourceType, out object fallbackValue, out failureStatus, out error))
                {
                    return false;
                }

                if (!TryConvertForwardCore(
                        binding.Converter,
                        fallbackValue,
                        sourceType,
                        targetType,
                        out targetValue,
                        out error))
                {
                    failureStatus = BindingSyncStatus.ConverterFailed;
                    error = $"Converter fallback failed: {error}";
                    return false;
                }
            }

            if (!TryHandleNullValue(
                    binding,
                    targetValue,
                    targetType,
                    out object processedValue,
                    out bool useFallback,
                    out skipSynchronization,
                    out failureStatus,
                    out error))
            {
                return false;
            }

            if (skipSynchronization)
            {
                targetValue = null;
                return true;
            }

            if (useFallback)
            {
                if (!TryGetFallbackValue(binding, state, sourceType, out object fallbackValue, out failureStatus, out error))
                {
                    return false;
                }

                if (!TryConvertForwardCore(
                        binding.Converter,
                        fallbackValue,
                        sourceType,
                        targetType,
                        out targetValue,
                        out error))
                {
                    failureStatus = BindingSyncStatus.ConverterFailed;
                    error = $"Fallback conversion failed: {error}";
                    return false;
                }

                if (IsNullValue(targetValue))
                {
                    failureStatus = BindingSyncStatus.FallbackFailed;
                    error = "The converted fallback value is null.";
                    return false;
                }

                failureStatus = BindingSyncStatus.Success;
                error = string.Empty;
                return true;
            }

            targetValue = processedValue;
            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;
            return true;
        }

        private static bool TryConvertForwardCore(
            BindingConverter converter,
            object sourceValue,
            Type sourceType,
            Type targetType,
            out object targetValue,
            out string error)
        {
#if UNITY_2020_2_OR_NEWER
            using (BindingRuntimeProfiler.Convert.Auto())
#endif
            {
                if (converter == null)
                {
                    targetValue = sourceValue;
                    return TryValidateRuntimeValueType(
                        targetValue,
                        targetType,
                        "Source output",
                        out error);
                }

                if (!converter.CanConvert(sourceType, targetType))
                {
                    targetValue = null;
                    error = $"Converter '{converter.name}' cannot convert '{GetTypeName(sourceType)}' to '{GetTypeName(targetType)}'.";
                    return false;
                }

                if (!converter.TryConvert(sourceValue, out targetValue, out error))
                {
                    return false;
                }

                return TryValidateRuntimeValueType(
                    targetValue,
                    targetType,
                    $"Converter '{converter.name}'",
                    out error);
            }
        }

        private static bool TryReadReverseValue(
            ViewDataBinding binding,
            Type sourceType,
            Type targetType,
            out object targetValue,
            out object sourceValue,
            out bool skipSynchronization,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            targetValue = null;
            sourceValue = null;
            skipSynchronization = false;

            if (!BindingBackendRegistry.MemberBackend.TryRead(binding.Target, out object readTargetValue, out string readError))
            {
                if (ShouldUseReadFailureFallback(binding))
                {
                    return TryGetFallbackValue(binding, sourceType, out sourceValue, out failureStatus, out error);
                }

                failureStatus = BindingSyncStatus.ReadFailed;
                error = readError;
                return false;
            }

            targetValue = readTargetValue;

            if (!TryHandleNullValue(
                    binding,
                    readTargetValue,
                    targetType,
                    out object processedTargetValue,
                    out bool useFallback,
                    out skipSynchronization,
                    out failureStatus,
                    out error))
            {
                return false;
            }

            if (skipSynchronization)
            {
                return true;
            }

            if (useFallback)
            {
                return TryGetFallbackValue(binding, sourceType, out sourceValue, out failureStatus, out error);
            }

            if (!TryConvertBackCore(
                    binding.Converter,
                    processedTargetValue,
                    targetType,
                    sourceType,
                    out sourceValue,
                    out error))
            {
                if (!ShouldUseConverterFailureFallback(binding))
                {
                    failureStatus = BindingSyncStatus.ConverterFailed;
                    return false;
                }

                return TryGetFallbackValue(binding, sourceType, out sourceValue, out failureStatus, out error);
            }

            if (!TryHandleNullValue(
                    binding,
                    sourceValue,
                    sourceType,
                    out object processedSourceValue,
                    out useFallback,
                    out skipSynchronization,
                    out failureStatus,
                    out error))
            {
                return false;
            }

            if (skipSynchronization)
            {
                sourceValue = null;
                return true;
            }

            if (useFallback)
            {
                return TryGetFallbackValue(binding, sourceType, out sourceValue, out failureStatus, out error);
            }

            sourceValue = processedSourceValue;
            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;
            return true;
        }

        private static bool TryConvertBackCore(
            BindingConverter converter,
            object targetValue,
            Type targetType,
            Type sourceType,
            out object sourceValue,
            out string error)
        {
#if UNITY_2020_2_OR_NEWER
            using (BindingRuntimeProfiler.Convert.Auto())
#endif
            {
                if (converter == null)
                {
                    sourceValue = targetValue;
                    return TryValidateRuntimeValueType(
                        sourceValue,
                        sourceType,
                        "Target output",
                        out error);
                }

                if (!converter.CanConvertBack(targetType, sourceType))
                {
                    sourceValue = null;
                    error = $"Converter '{converter.name}' does not support reverse conversion from '{GetTypeName(targetType)}' to '{GetTypeName(sourceType)}'.";
                    return false;
                }

                if (!converter.TryConvertBack(targetValue, out sourceValue, out error))
                {
                    return false;
                }

                return TryValidateRuntimeValueType(
                    sourceValue,
                    sourceType,
                    $"Converter '{converter.name}' reverse output",
                    out error);
            }
        }

        private static bool TryValidateRuntimeValueType(
            object value,
            Type expectedType,
            string producerName,
            out string error)
        {
            if (IsNullValue(value))
            {
                error = string.Empty;
                return true;
            }

            Type actualType = value.GetType();
            Type nullableType = Nullable.GetUnderlyingType(expectedType);
            if (actualType == expectedType || actualType == nullableType)
            {
                error = string.Empty;
                return true;
            }

            error = $"{producerName} returned '{GetTypeName(actualType)}' but '{GetTypeName(expectedType)}' was expected.";
            return false;
        }

        private static bool ShouldUseConverterFailureFallback(ViewDataBinding binding)
        {
            return binding.Fallback != null &&
                   binding.Fallback.Enabled &&
                   binding.Fallback.UseOnConverterFailure;
        }

        private static bool TryGetReusableTransformationFailure(
            BindingRuntimeState state,
            out BindingSyncResult result)
        {
            result = state.LastResult;
            if (!state.HasResult)
            {
                return false;
            }

            switch (result.Status)
            {
                case BindingSyncStatus.ConverterFailed:
                case BindingSyncStatus.FormatterFailed:
                case BindingSyncStatus.FallbackFailed:
                case BindingSyncStatus.TypeMismatch:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetAndCacheFormattedFallback(
            ViewDataBinding binding,
            BindingRuntimeState state,
            IReadOnlyList<object> sourceValues,
            Type outputType,
            out object value,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            if (!TryGetFallbackValue(
                    binding,
                    state,
                    outputType,
                    out value,
                    out failureStatus,
                    out error))
            {
                return false;
            }

            state.CaptureSourceInputs(sourceValues);
            state.CacheSourceOutput(value);
            return true;
        }

        private static bool TryReadSourceOutput(
            ViewDataBinding binding,
            BindingRuntimeState state,
            Type outputType,
            IReadOnlyList<BindingMemberMetadata> inputMetadata,
            out object value,
            out bool skipSynchronization,
            out BindingSyncStatus failureStatus,
            out string error,
            IBindingFormatter cachedFormatter = null)
        {
            state.SourceInputsUnchanged = false;
            if (binding.Formatter == null || !binding.Formatter.Enabled)
            {
                return TryReadSingleSourceValue(
                    binding,
                    outputType,
                    out value,
                    out skipSynchronization,
                    out failureStatus,
                    out error,
                    state);
            }

            value = null;
            skipSynchronization = false;
            failureStatus = BindingSyncStatus.ReadFailed;
            List<object> sourceValues = state.PrepareSourceValueBuffer(binding.Sources.Count);

            for (int i = 0; i < binding.Sources.Count; i++)
            {
                BindingSource source = binding.Sources[i];
                if (!BindingBackendRegistry.MemberBackend.TryRead(
                        source.Endpoint,
                        out object sourceValue,
                        out string readError))
                {
                    if (ShouldUseReadFailureFallback(binding))
                    {
                        return TryGetFallbackValue(
                            binding,
                            state,
                            outputType,
                            out value,
                            out failureStatus,
                            out error);
                    }

                    error = $"Source {i + 1}: {readError}";
                    failureStatus = BindingSyncStatus.ReadFailed;
                    return false;
                }

                Type sourceType = inputMetadata[i].ValueType;
                if (!TryHandleNullValue(
                        binding,
                        sourceValue,
                        sourceType,
                        out object processedValue,
                        out bool useFinalFallback,
                        out skipSynchronization,
                        out failureStatus,
                        out error))
                {
                    return false;
                }

                if (skipSynchronization)
                {
                    value = null;
                    return true;
                }

                if (useFinalFallback)
                {
                    return TryGetFallbackValue(binding, state, outputType, out value, out failureStatus, out error);
                }

                sourceValues.Add(processedValue);
            }

            if (state.Initialized &&
                binding.WritePolicy == BindingWritePolicy.WhenValueChanges &&
                !binding.AlwaysEvaluateTransformation &&
                !state.SourceInputsChanged(sourceValues) &&
                state.TryGetCachedSourceOutput(out value))
            {
                state.SourceInputsUnchanged = true;
                failureStatus = BindingSyncStatus.Success;
                error = string.Empty;
                return true;
            }

            IBindingFormatter formatter = cachedFormatter;
            if (formatter == null &&
                !BindingFormatterRegistry.TryGet(binding.Formatter.FormatterId, out formatter))
            {
                failureStatus = BindingSyncStatus.FormatterFailed;
                error = $"Formatter '{binding.Formatter.FormatterId}' is not registered.";
                return false;
            }

            IReadOnlyList<object> formatterValues = sourceValues;
            object[] formatterArguments = null;
            if (sourceValues.Count > 3)
            {
                formatterArguments = state.PrepareFormatterArguments(sourceValues.Count);
                for (int i = 0; i < sourceValues.Count; i++)
                {
                    formatterArguments[i] = sourceValues[i];
                }

                formatterValues = formatterArguments;
            }

#if UNITY_2020_2_OR_NEWER
            using (BindingRuntimeProfiler.Format.Auto())
#endif
            if (!formatter.TryFormat(formatterValues, binding.Formatter, out value, out string formatterError))
            {
                if (formatterArguments != null)
                {
                    Array.Clear(formatterArguments, 0, formatterArguments.Length);
                }
                if (binding.Fallback != null &&
                    binding.Fallback.Enabled &&
                    binding.Fallback.UseOnFormatterFailure)
                {
                    return TryGetAndCacheFormattedFallback(
                        binding,
                        state,
                        sourceValues,
                        outputType,
                        out value,
                        out failureStatus,
                        out error);
                }

                failureStatus = BindingSyncStatus.FormatterFailed;
                error = formatterError;
                return false;
            }

            if (formatterArguments != null)
            {
                Array.Clear(formatterArguments, 0, formatterArguments.Length);
            }

            Type nullableOutputType = Nullable.GetUnderlyingType(outputType);
            if (!IsNullValue(value) &&
                value.GetType() != outputType &&
                value.GetType() != nullableOutputType)
            {
                if (binding.Fallback != null &&
                    binding.Fallback.Enabled &&
                    binding.Fallback.UseOnFormatterFailure)
                {
                    return TryGetAndCacheFormattedFallback(
                        binding,
                        state,
                        sourceValues,
                        outputType,
                        out value,
                        out failureStatus,
                        out error);
                }

                failureStatus = BindingSyncStatus.FormatterFailed;
                error = $"Formatter '{formatter.DisplayName}' returned '{value.GetType().FullName}' but declared '{outputType.FullName}'.";
                return false;
            }

            if (!TryHandleNullValue(
                    binding,
                    value,
                    outputType,
                    out object processedOutput,
                    out bool useOutputFallback,
                    out skipSynchronization,
                    out failureStatus,
                    out error))
            {
                return false;
            }

            if (skipSynchronization)
            {
                value = null;
                return true;
            }

            if (useOutputFallback)
            {
                return TryGetAndCacheFormattedFallback(
                    binding,
                    state,
                    sourceValues,
                    outputType,
                    out value,
                    out failureStatus,
                    out error);
            }

            value = processedOutput;
            state.CaptureSourceInputs(sourceValues);
            state.CacheSourceOutput(value);
            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;
            return true;
        }

        private static bool TryReadSingleSourceValue(
            ViewDataBinding binding,
            Type valueType,
            out object value,
            out bool skipSynchronization,
            out BindingSyncStatus failureStatus,
            out string error,
            BindingRuntimeState state = null)
        {
            value = null;
            skipSynchronization = false;

            if (!BindingBackendRegistry.SourceBackend.TryRead(binding.Sources, out object readValue, out string readError))
            {
                if (ShouldUseReadFailureFallback(binding))
                {
                    return TryGetFallbackValue(
                        binding,
                        state,
                        valueType,
                        out value,
                        out failureStatus,
                        out error);
                }

                failureStatus = BindingSyncStatus.ReadFailed;
                error = readError;
                return false;
            }

            if (!TryHandleNullValue(
                    binding,
                    readValue,
                    valueType,
                    out object processedValue,
                    out bool useFallback,
                    out skipSynchronization,
                    out failureStatus,
                    out error))
            {
                return false;
            }

            if (skipSynchronization)
            {
                value = null;
                return true;
            }

            if (useFallback)
            {
                return TryGetFallbackValue(binding, state, valueType, out value, out failureStatus, out error);
            }

            value = processedValue;
            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;
            return true;
        }

        private static bool TryReadEndpointValue(
            ViewDataBinding binding,
            BindingEndpoint endpoint,
            Type valueType,
            out object value,
            out bool skipSynchronization,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            value = null;
            skipSynchronization = false;

            if (!BindingBackendRegistry.MemberBackend.TryRead(endpoint, out object readValue, out string readError))
            {
                if (ShouldUseReadFailureFallback(binding))
                {
                    return TryGetFallbackValue(
                        binding,
                        valueType,
                        out value,
                        out failureStatus,
                        out error);
                }

                failureStatus = BindingSyncStatus.ReadFailed;
                error = readError;
                return false;
            }

            if (!TryHandleNullValue(
                    binding,
                    readValue,
                    valueType,
                    out object processedValue,
                    out bool useFallback,
                    out skipSynchronization,
                    out failureStatus,
                    out error))
            {
                return false;
            }

            if (skipSynchronization)
            {
                value = null;
                return true;
            }

            if (useFallback)
            {
                return TryGetFallbackValue(binding, valueType, out value, out failureStatus, out error);
            }

            value = processedValue;
            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;
            return true;
        }

        private static bool TryHandleNullValue(
            ViewDataBinding binding,
            object value,
            Type valueType,
            out object processedValue,
            out bool useFallback,
            out bool skipSynchronization,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            processedValue = value;
            useFallback = false;
            skipSynchronization = false;
            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;

            if (!IsNullValue(value))
            {
                return true;
            }

            switch (binding.NullHandling)
            {
                case BindingNullHandlingMode.PassThrough:
                    return true;

                case BindingNullHandlingMode.UseFallback:
                    if (binding.Fallback == null || !binding.Fallback.Enabled)
                    {
                        failureStatus = BindingSyncStatus.FallbackFailed;
                        error = "Null Handling is set to Use Fallback, but Fallback is disabled.";
                        return false;
                    }

                    useFallback = true;
                    return true;

                case BindingNullHandlingMode.SkipSynchronization:
                    skipSynchronization = true;
                    return true;

                case BindingNullHandlingMode.SetDefaultValue:
                    processedValue = GetDefaultValue(valueType);
                    return true;

                case BindingNullHandlingMode.Fail:
                    failureStatus = BindingSyncStatus.NullValueRejected;
                    error = $"A null value was rejected for type '{GetTypeName(valueType)}'.";
                    return false;

                default:
                    failureStatus = BindingSyncStatus.NullValueRejected;
                    error = $"Unsupported null handling mode: {binding.NullHandling}.";
                    return false;
            }
        }

        private static bool ShouldUseReadFailureFallback(ViewDataBinding binding)
        {
            return binding.Fallback != null &&
                   binding.Fallback.Enabled &&
                   binding.Fallback.UseOnReadFailure;
        }

        private static bool TryGetFallbackValue(
            ViewDataBinding binding,
            Type valueType,
            out object value,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            return TryGetFallbackValue(
                binding,
                null,
                valueType,
                out value,
                out failureStatus,
                out error);
        }

        private static bool TryGetFallbackValue(
            ViewDataBinding binding,
            BindingRuntimeState state,
            Type valueType,
            out object value,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            value = null;

            if (binding.Fallback == null || !binding.Fallback.Enabled)
            {
                failureStatus = BindingSyncStatus.FallbackFailed;
                error = "Fallback is disabled.";
                return false;
            }

            if (binding.Fallback.Value == null)
            {
                failureStatus = BindingSyncStatus.FallbackFailed;
                error = "Fallback value configuration is missing.";
                return false;
            }

            if (state != null &&
                state.TryGetCachedFallback(valueType, binding.Fallback.Value, out value))
            {
                failureStatus = BindingSyncStatus.Success;
                error = string.Empty;
                return true;
            }

            if (!binding.Fallback.Value.TryGetValue(valueType, out value, out error))
            {
                failureStatus = BindingSyncStatus.FallbackFailed;
                return false;
            }

            state?.CacheFallback(valueType, binding.Fallback.Value, value);
            failureStatus = BindingSyncStatus.Success;
            return true;
        }

        private static bool IsNullValue(object value)
        {
            if (value == null)
            {
                return true;
            }

            return value is UnityEngine.Object unityObject && unityObject == null;
        }

        private static object GetDefaultValue(Type type)
        {
            if (type == null || !type.IsValueType || Nullable.GetUnderlyingType(type) != null)
            {
                return null;
            }

            lock (DefaultValueCacheLock)
            {
                if (!DefaultValueCache.TryGetValue(type, out object value))
                {
                    value = Activator.CreateInstance(type);
                    DefaultValueCache.Add(type, value);
                }

                return value;
            }
        }

        private static bool TryWriteTargetAndCaptureActualValue(
            ViewDataBinding binding,
            object value,
            out object actualValue,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            actualValue = null;

            if (!BindingBackendRegistry.MemberBackend.TryWrite(binding.Target, value, out error))
            {
                failureStatus = BindingSyncStatus.WriteFailed;
                return false;
            }

            if (!BindingBackendRegistry.MemberBackend.TryRead(binding.Target, out actualValue, out error))
            {
                failureStatus = BindingSyncStatus.ReadFailed;
                return false;
            }

            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;
            return true;
        }

        private static bool TryWriteSourceAndCaptureActualValue(
            ViewDataBinding binding,
            object value,
            out object actualValue,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            actualValue = null;

            if (!BindingBackendRegistry.SourceBackend.TryWrite(binding.Sources, value, out error))
            {
                failureStatus = BindingSyncStatus.WriteFailed;
                return false;
            }

            if (!BindingBackendRegistry.SourceBackend.TryRead(binding.Sources, out actualValue, out error))
            {
                failureStatus = BindingSyncStatus.ReadFailed;
                return false;
            }

            failureStatus = BindingSyncStatus.Success;
            error = string.Empty;
            return true;
        }

        private BindingSyncResult SynchronizeBindingInternal(
            ViewDataBinding binding,
            string stateKey,
            ViewDataBindingProfileReference profileReference,
            BindingRuntimeState state = null)
        {
            BindingPerformanceSample performanceSample = statistics.BeginSample();
            try
            {
                using (BindingResolutionScope.Push(this, contextResolver, profileReference))
                {
                    state = state ?? GetOrCreateState(stateKey);
                    if (!PrepareMissingEndpointRecovery(
                            binding,
                            state,
                            out BindingSyncResult cachedResult))
                    {
                        statistics.AvoidedEndpointRetries++;
                        statistics.RecordDataBinding(cachedResult);
                        return ApplyErrorPolicy(binding, state, cachedResult);
                    }

#if UNITY_2020_2_OR_NEWER
                    using (BindingRuntimeProfiler.Synchronize.Auto())
#endif
                    {
                        BindingSyncResult result = Synchronize(binding, state);
                        if (result.Status == BindingSyncStatus.UnresolvedInstance &&
                            result.EndpointRole != BindingEndpointRole.None)
                        {
                            result = ApplyMissingEndpointPolicy(binding, state, result);
                        }
                        else
                        {
                            state.ClearMissingEndpoint();
                        }

                        statistics.RecordDataBinding(result);
                        return ApplyErrorPolicy(binding, state, result);
                    }
                }
            }
            finally
            {
                statistics.EndSample(binding.Name, performanceSample);
            }
        }

        private bool PrepareMissingEndpointRecovery(
            ViewDataBinding binding,
            BindingRuntimeState state,
            out BindingSyncResult cachedResult)
        {
            cachedResult = default;
            if (state.RuntimeDisabled || state.MissingEndpointRole == BindingEndpointRole.None)
            {
                return true;
            }

            float currentTime = Time.unscaledTime;
            if (state.HasCachedMissingEndpointResult && currentTime < state.NextMissingEndpointRetryTime)
            {
                cachedResult = state.CachedMissingEndpointResult;
                return false;
            }

            bool endpointAvailable = state.MissingEndpointRole == BindingEndpointRole.Source
                ? AreSourceEndpointsAvailable(binding.Sources)
                : IsEndpointAvailable(binding.Target);
            if (!endpointAvailable)
            {
                BindingSyncResult result = state.HasCachedMissingEndpointResult
                    ? state.CachedMissingEndpointResult
                    : BindingSyncResult.NoChange("Waiting for the missing endpoint.");
                state.CacheMissingEndpointResult(
                    result,
                    currentTime + GetMissingEndpointRetryDelay(binding, state));
                cachedResult = result;
                return false;
            }

            InvalidateBindingCaches(binding, state);
            state.ResetSynchronizationValues();
            state.ClearMissingEndpoint();
            return true;
        }

        private static float GetMissingEndpointRetryDelay(
            ViewDataBinding binding,
            BindingRuntimeState state)
        {
            int exponent = Math.Min(state.MissingEndpointRetryAttempt, 8);
            float delay = binding.MissingEndpointRetryInterval * (1 << exponent);
            return Math.Min(delay, binding.MaximumMissingEndpointRetryInterval);
        }

        private BindingSyncResult ApplyMissingEndpointPolicy(
            ViewDataBinding binding,
            BindingRuntimeState state,
            BindingSyncResult result)
        {
            if (result.Status != BindingSyncStatus.UnresolvedInstance ||
                result.EndpointRole == BindingEndpointRole.None)
            {
                return result;
            }

            MissingEndpointPolicy policy = result.EndpointRole == BindingEndpointRole.Source
                ? binding.SourceMissingPolicy
                : binding.TargetMissingPolicy;
            state.MarkMissingEndpoint(result.EndpointRole, policy);

            BindingSyncResult policyResult;
            switch (policy)
            {
                case MissingEndpointPolicy.Wait:
                    policyResult = BindingSyncResult.NoChange(
                        $"Waiting for the missing {result.EndpointRole.ToString().ToLowerInvariant()} endpoint. {result.Message}");
                    break;

                case MissingEndpointPolicy.Disable:
                    state.RuntimeDisabled = true;
                    policyResult = new BindingSyncResult(
                        BindingSyncStatus.Disabled,
                        $"Binding disabled because the {result.EndpointRole.ToString().ToLowerInvariant()} endpoint is missing. Invalidate it to retry.",
                        result.EndpointRole);
                    break;

                case MissingEndpointPolicy.ClearTarget:
                    policyResult = TryClearTargetForMissingEndpoint(binding, state, result);
                    break;

                case MissingEndpointPolicy.UseFallback:
                    policyResult = TryApplyMissingEndpointFallback(binding, state, result);
                    break;

                case MissingEndpointPolicy.ReResolve:
                    InvalidateBindingCaches(binding, state);
                    state.ResetSynchronizationValues();
                    BindingSyncResult retryResult = Synchronize(binding, state);
                    if (retryResult.Status == BindingSyncStatus.UnresolvedInstance)
                    {
                        state.MarkMissingEndpoint(retryResult.EndpointRole, policy);
                        policyResult = new BindingSyncResult(
                            retryResult.Status,
                            $"Endpoint re-resolution did not succeed. {retryResult.Message}",
                            retryResult.EndpointRole);
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
                        $"Unsupported missing endpoint policy: {policy}.",
                        result.EndpointRole);
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

        private BindingSyncResult TryClearTargetForMissingEndpoint(
            ViewDataBinding binding,
            BindingRuntimeState state,
            BindingSyncResult missingResult)
        {
            if (state.MissingEndpointActionApplied)
            {
                return BindingSyncResult.NoChange(
                    "The missing endpoint clear action was already applied; waiting for recovery.");
            }

            if (missingResult.EndpointRole == BindingEndpointRole.Target)
            {
                return BindingSyncResult.NoChange(
                    "The Target cannot be cleared while its endpoint is missing; waiting for it to become available.");
            }

            if (!TryGetEndpointMetadata(
                    binding.Target,
                    out BindingMemberMetadata targetMetadata,
                    out BindingSyncStatus metadataStatus,
                    out string metadataError))
            {
                return new BindingSyncResult(
                    metadataStatus,
                    metadataError,
                    BindingEndpointRole.Target);
            }

            if (!targetMetadata.CanWrite)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.WriteFailed,
                    "The Target cannot be cleared because it is not writable.",
                    BindingEndpointRole.Target);
            }

            object clearValue = GetDefaultValue(targetMetadata.ValueType);
            if (!BindingBackendRegistry.MemberBackend.TryWrite(binding.Target, clearValue, out string error))
            {
                return CreateEndpointFailureResult(
                    binding.Target,
                    BindingSyncStatus.WriteFailed,
                    error,
                    BindingEndpointRole.Target);
            }

            state.Initialized = true;
            state.LastSourceValue = null;
            state.LastTargetValue = clearValue;
            state.MarkMissingEndpointActionApplied();
            return BindingSyncResult.Success("The Target was cleared because a Source endpoint is missing.");
        }

        private BindingSyncResult TryApplyMissingEndpointFallback(
            ViewDataBinding binding,
            BindingRuntimeState state,
            BindingSyncResult missingResult)
        {
            if (state.MissingEndpointActionApplied)
            {
                return BindingSyncResult.NoChange(
                    "The missing endpoint fallback was already applied; waiting for recovery.");
            }

            if (binding.Fallback == null || !binding.Fallback.Enabled || binding.Fallback.Value == null)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.FallbackFailed,
                    "The missing endpoint policy requires an enabled Fallback value.",
                    missingResult.EndpointRole);
            }

            if (missingResult.EndpointRole == BindingEndpointRole.Source)
            {
                if (!TryGetEndpointMetadata(
                        binding.Target,
                        out BindingMemberMetadata targetMetadata,
                        out BindingSyncStatus metadataStatus,
                        out string metadataError))
                {
                    return new BindingSyncResult(
                        metadataStatus,
                        metadataError,
                        BindingEndpointRole.Target);
                }

                if (!targetMetadata.CanWrite)
                {
                    return new BindingSyncResult(
                        BindingSyncStatus.WriteFailed,
                        "The Target is not writable, so the missing Source fallback cannot be applied.",
                        BindingEndpointRole.Target);
                }

                if (!TryGetFallbackValue(
                        binding,
                        state,
                        targetMetadata.ValueType,
                        out object targetFallback,
                        out BindingSyncStatus fallbackStatus,
                        out string fallbackError))
                {
                    return new BindingSyncResult(
                        fallbackStatus,
                        fallbackError,
                        BindingEndpointRole.Source);
                }

                if (!BindingBackendRegistry.MemberBackend.TryWrite(
                        binding.Target,
                        targetFallback,
                        out string writeError))
                {
                    return CreateEndpointFailureResult(
                        binding.Target,
                        BindingSyncStatus.WriteFailed,
                        writeError,
                        BindingEndpointRole.Target);
                }

                state.Initialized = true;
                state.LastSourceValue = null;
                state.LastTargetValue = targetFallback;
                state.MarkMissingEndpointActionApplied();
                return BindingSyncResult.Success(
                    "Fallback was written to the Target because a Source endpoint is missing.");
            }

            if (binding.Direction == BindingSyncDirection.SourceToTarget)
            {
                return BindingSyncResult.NoChange(
                    "The Target is missing, so the fallback cannot be written yet.");
            }

            if (!BindingBackendRegistry.SourceBackend.TryGetMetadata(
                    binding.Sources,
                    out BindingMemberMetadata sourceMetadata,
                    out string sourceMetadataError))
            {
                return new BindingSyncResult(
                    ClassifySourceMetadataFailure(binding.Sources),
                    sourceMetadataError,
                    BindingEndpointRole.Source);
            }

            if (!sourceMetadata.CanWrite)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.WriteFailed,
                    "The Source is not writable, so the missing Target fallback cannot be applied.",
                    BindingEndpointRole.Source);
            }

            if (!TryGetFallbackValue(
                    binding,
                    state,
                    sourceMetadata.ValueType,
                    out object sourceFallback,
                    out BindingSyncStatus sourceFallbackStatus,
                    out string sourceFallbackError))
            {
                return new BindingSyncResult(
                    sourceFallbackStatus,
                    sourceFallbackError,
                    BindingEndpointRole.Target);
            }

            if (!BindingBackendRegistry.SourceBackend.TryWrite(
                    binding.Sources,
                    sourceFallback,
                    out string sourceWriteError))
            {
                return CreateSourceFailureResult(
                    binding.Sources,
                    BindingSyncStatus.WriteFailed,
                    sourceWriteError);
            }

            state.Initialized = true;
            state.LastSourceValue = sourceFallback;
            state.LastTargetValue = null;
            state.MarkMissingEndpointActionApplied();
            return BindingSyncResult.Success(
                "Fallback was written to the Source because the Target endpoint is missing.");
        }

        private bool TryGetLocalBinding(
            int bindingIndex,
            out ViewDataBinding binding,
            out string stateKey)
        {
            binding = null;
            stateKey = null;
            if (bindingIndex < 0 || bindingIndex >= bindings.Count)
            {
                return false;
            }

            binding = bindings[bindingIndex];
            if (binding == null)
            {
                return false;
            }

            binding.EnsureId();
            stateKey = GetLocalStateKey(binding);
            return true;
        }

        private bool TryGetProfileReference(
            int profileIndex,
            out ViewDataBindingProfileReference profileReference)
        {
            profileReference = null;
            if (profileIndex < 0 || profileIndex >= profiles.Count)
            {
                return false;
            }

            profileReference = profiles[profileIndex];
            return profileReference != null;
        }

        private void InvalidateProfileReference(
            ViewDataBindingProfileReference profileReference)
        {
            contextResolver.Invalidate();
            if (profileReference?.Profile == null)
            {
                return;
            }

            IReadOnlyList<ViewDataBinding> profileBindings = profileReference.Profile.Bindings;
            for (int i = 0; i < profileBindings.Count; i++)
            {
                ViewDataBinding binding = profileBindings[i];
                if (binding == null)
                {
                    continue;
                }

                binding.EnsureId();
                ResetState(GetProfileStateKey(profileReference, binding));
                InvalidateBindingCaches(binding);
            }
        }

        private bool TryGetProfileBinding(
            int profileIndex,
            int bindingIndex,
            out ViewDataBinding binding,
            out string stateKey,
            out ViewDataBindingProfileReference profileReference)
        {
            binding = null;
            stateKey = null;
            profileReference = null;
            if (profileIndex < 0 || profileIndex >= profiles.Count)
            {
                return false;
            }

            profileReference = profiles[profileIndex];
            if (!IsProfileActive(profileReference))
            {
                return false;
            }

            IReadOnlyList<ViewDataBinding> profileBindings = profileReference.Profile.Bindings;
            if (bindingIndex < 0 || bindingIndex >= profileBindings.Count)
            {
                return false;
            }

            binding = profileBindings[bindingIndex];
            if (binding == null)
            {
                return false;
            }

            binding.EnsureId();
            stateKey = GetProfileStateKey(profileReference, binding);
            return true;
        }

        private bool TryFindBinding(
            string bindingIdOrName,
            out ViewDataBinding binding,
            out string stateKey,
            out ViewDataBindingProfileReference profileReference)
        {
            binding = null;
            stateKey = null;
            profileReference = null;
            if (string.IsNullOrWhiteSpace(bindingIdOrName))
            {
                return false;
            }

            EnsureBindingIds();
            for (int i = 0; i < bindings.Count; i++)
            {
                ViewDataBinding candidate = bindings[i];
                if (MatchesBinding(candidate, bindingIdOrName))
                {
                    binding = candidate;
                    stateKey = GetLocalStateKey(candidate);
                    return true;
                }
            }

            for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
            {
                ViewDataBindingProfileReference candidateProfile = profiles[profileIndex];
                if (!IsProfileActive(candidateProfile))
                {
                    continue;
                }

                IReadOnlyList<ViewDataBinding> profileBindings = candidateProfile.Profile.Bindings;
                for (int bindingIndex = 0; bindingIndex < profileBindings.Count; bindingIndex++)
                {
                    ViewDataBinding candidate = profileBindings[bindingIndex];
                    string qualifiedName = candidate == null
                        ? null
                        : candidateProfile.Profile.name + "/" + candidate.Name;
                    if (!MatchesBinding(candidate, bindingIdOrName) &&
                        !string.Equals(qualifiedName, bindingIdOrName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    binding = candidate;
                    profileReference = candidateProfile;
                    stateKey = GetProfileStateKey(candidateProfile, candidate);
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesBinding(ViewDataBinding binding, string idOrName)
        {
            return binding != null &&
                   (string.Equals(binding.Id, idOrName, StringComparison.Ordinal) ||
                    string.Equals(binding.Name, idOrName, StringComparison.Ordinal));
        }

        private static bool IsProfileActive(ViewDataBindingProfileReference profileReference)
        {
            return profileReference != null &&
                   profileReference.Enabled &&
                   profileReference.Profile != null;
        }

        private static string GetLocalStateKey(ViewDataBinding binding)
        {
            return binding.Id;
        }

        private string GetLocalStateKeyForIndex(int bindingIndex)
        {
            return bindingIndex >= 0 && bindingIndex < bindings.Count && bindings[bindingIndex] != null
                ? GetLocalStateKey(bindings[bindingIndex])
                : string.Empty;
        }

        private static string GetProfileStateKey(
            ViewDataBindingProfileReference profileReference,
            ViewDataBinding binding)
        {
            return profileReference.GetStateKey(binding.Id);
        }

        private void ResetState(string stateKey)
        {
            if (!string.IsNullOrEmpty(stateKey) &&
                runtimeStates.TryGetValue(stateKey, out BindingRuntimeState state))
            {
                state.ResetValues();
                state.InvalidateExecutionPlan();
            }
        }

        private void InvalidateBindingCaches(
            ViewDataBinding binding,
            BindingRuntimeState state = null)
        {
            contextResolver.Invalidate();
            state?.InvalidateExecutionPlan();
            if (!(BindingBackendRegistry.MemberBackend is IBindingMemberCacheInvalidator invalidator) ||
                binding == null)
            {
                return;
            }

            invalidator.Invalidate(binding.Target);
            if (binding.Sources == null)
            {
                return;
            }

            for (int i = 0; i < binding.Sources.Count; i++)
            {
                invalidator.Invalidate(binding.Sources[i]?.Endpoint);
            }
        }

        private BindingSyncResult ApplyErrorPolicy(
            ViewDataBinding binding,
            BindingRuntimeState state,
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

            switch (binding.ErrorPolicy)
            {
                case BindingErrorPolicy.ReportOnly:
                    break;

                case BindingErrorPolicy.LogOnce:
                    if (state.LastLoggedStatus != result.Status ||
                        !string.Equals(state.LastLoggedMessage, result.Message, StringComparison.Ordinal))
                    {
                        string message =
                            $"Binding '{binding.Name}' failed with {result.Status}: {result.Message}";
                        Debug.LogWarning(message, this);
                        state.LastLoggedStatus = result.Status;
                        state.LastLoggedMessage = result.Message;
                    }
                    break;

                case BindingErrorPolicy.LogEveryTime:
                {
                    string message =
                        $"Binding '{binding.Name}' failed with {result.Status}: {result.Message}";
                    Debug.LogWarning(message, this);
                    break;
                }

                case BindingErrorPolicy.DisableUntilReset:
                {
                    string message =
                        $"Binding '{binding.Name}' failed with {result.Status}: {result.Message}";
                    state.RuntimeDisabled = true;
                    Debug.LogWarning(message + " The binding was disabled until reset.", this);
                    break;
                }

                case BindingErrorPolicy.ThrowException:
                {
                    string message =
                        $"Binding '{binding.Name}' failed with {result.Status}: {result.Message}";
                    throw new InvalidOperationException(message);
                }

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return result;
        }

        private static string AppendPreviewMessage(string currentMessage, string message)
        {
            return string.IsNullOrEmpty(currentMessage)
                ? message
                : currentMessage + " " + message;
        }

        private static BindingPreview CreateFailedPreview(
            BindingSyncStatus status,
            string message)
        {
            return new BindingPreview(
                null,
                null,
                null,
                null,
                new BindingSyncResult(status, message));
        }

        private static bool UsesSourceObject(
            ViewDataBinding binding,
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

        private static bool TrySetSourceInstance(
            ViewDataBinding binding,
            int sourceIndex,
            object instance,
            out bool changed)
        {
            changed = false;
            if (binding?.Sources == null ||
                sourceIndex < 0 ||
                sourceIndex >= binding.Sources.Count)
            {
                return false;
            }

            BindingInstanceReference reference =
                binding.Sources[sourceIndex]?.Endpoint?.Instance;
            if (reference == null)
            {
                return false;
            }
            if (reference.RuntimeInstanceEquals(instance))
            {
                return true;
            }

            changed = reference.SetRuntimeInstance(instance);
            return changed;
        }

        protected override void ResetRuntimeState()
        {
            foreach (BindingRuntimeState state in runtimeStates.Values)
            {
                state.ResetValues();
            }

            contextResolver.Invalidate();
        }

        private BindingRuntimeState GetOrCreateState(string bindingId)
        {
            if (!runtimeStates.TryGetValue(bindingId, out BindingRuntimeState state))
            {
                state = new BindingRuntimeState();
                runtimeStates.Add(bindingId, state);
            }

            return state;
        }

        private void EnsureBindingIds()
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                bindings[i]?.EnsureId();
            }

            for (int i = 0; i < profiles.Count; i++)
            {
                ViewDataBindingProfileReference profileReference = profiles[i];
                if (profileReference == null)
                {
                    continue;
                }

                profileReference.EnsureId();
                profileReference.Profile?.EnsureBindingIds();
            }
        }

        private static string GetTypeName(Type type)
        {
            return type?.FullName ?? "null";
        }
    }
}

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

        private readonly Dictionary<string, BindingRuntimeState> runtimeStates =
            new Dictionary<string, BindingRuntimeState>();
        private readonly BindingContextResolver contextResolver = new BindingContextResolver();

        public IReadOnlyList<ViewDataBinding> Bindings => bindings;

        public IReadOnlyList<ViewDataBindingProfileReference> Profiles => profiles;

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

        protected override void ProcessBindingTiming(BindingUpdateTiming timing)
        {
            EnsureBindingIds();

            for (int i = 0; i < bindings.Count; i++)
            {
                ViewDataBinding binding = bindings[i];
                if (binding == null || binding.UpdateTiming != timing)
                {
                    continue;
                }

                SynchronizeBindingInternal(binding, GetLocalStateKey(binding), null);
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
                    if (binding == null || binding.UpdateTiming != timing)
                    {
                        continue;
                    }

                    SynchronizeBindingInternal(
                        binding,
                        GetProfileStateKey(profileReference, binding),
                        profileReference);
                }
            }
        }

        private static BindingSyncResult Synchronize(ViewDataBinding binding, BindingRuntimeState state)
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

            if (binding.Formatter != null &&
                binding.Formatter.Enabled &&
                binding.Direction != BindingSyncDirection.SourceToTarget)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.FormatterFailed,
                    "Formatters are supported only for Source -> Target bindings because formatting is not reversible.");
            }

            if (!TryGetSourceOutputMetadata(
                    binding,
                    state,
                    out BindingMemberMetadata sourceMetadata,
                    out List<BindingMemberMetadata> sourceInputMetadata,
                    out BindingSyncStatus sourceMetadataStatus,
                    out string sourceMetadataError))
            {
                return new BindingSyncResult(
                    sourceMetadataStatus,
                    sourceMetadataError,
                    BindingEndpointRole.Source);
            }

            if (!TryGetEndpointMetadata(
                    binding.Target,
                    out BindingMemberMetadata targetMetadata,
                    out BindingSyncStatus targetMetadataStatus,
                    out string targetMetadataError))
            {
                return new BindingSyncResult(
                    targetMetadataStatus,
                    targetMetadataError,
                    BindingEndpointRole.Target);
            }

            bool requiresReverse = binding.Direction != BindingSyncDirection.SourceToTarget;
            if (!TryValidateConverter(
                    binding,
                    sourceMetadata.ValueType,
                    targetMetadata.ValueType,
                    requiresReverse,
                    out string converterValidationError))
            {
                return new BindingSyncResult(BindingSyncStatus.TypeMismatch, converterValidationError);
            }

            if (binding.NullHandling == BindingNullHandlingMode.UseFallback &&
                (binding.Fallback == null || !binding.Fallback.Enabled))
            {
                return new BindingSyncResult(
                    BindingSyncStatus.FallbackFailed,
                    "Null Handling is set to Use Fallback, but Fallback is disabled.");
            }

            switch (binding.Direction)
            {
                case BindingSyncDirection.SourceToTarget:
                    return SynchronizeSourceToTarget(
                        binding,
                        state,
                        sourceMetadata,
                        sourceInputMetadata,
                        targetMetadata);

                case BindingSyncDirection.TargetToSource:
                    return SynchronizeTargetToSource(binding, state, sourceMetadata, targetMetadata);

                case BindingSyncDirection.TwoWay:
                    return SynchronizeTwoWay(binding, state, sourceMetadata, targetMetadata);

                default:
                    return new BindingSyncResult(
                        BindingSyncStatus.InvalidMemberPath,
                        $"Unsupported direction: {binding.Direction}.");
            }
        }

        private static BindingSyncResult SynchronizeSourceToTarget(
            ViewDataBinding binding,
            BindingRuntimeState state,
            BindingMemberMetadata sourceMetadata,
            IReadOnlyList<BindingMemberMetadata> sourceInputMetadata,
            BindingMemberMetadata targetMetadata)
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
                    out string readError))
            {
                return CreateSourceFailureResult(binding.Sources, readStatus, readError);
            }

            if (skipSynchronization)
            {
                return BindingSyncResult.NoChange("Synchronization skipped because a Source value is null.");
            }

            if (!TryConvertForward(
                    binding,
                    sourceValue,
                    sourceMetadata.ValueType,
                    targetMetadata.ValueType,
                    out object targetValue,
                    out skipSynchronization,
                    out BindingSyncStatus converterStatus,
                    out string converterError))
            {
                return new BindingSyncResult(converterStatus, converterError);
            }

            if (skipSynchronization)
            {
                return BindingSyncResult.NoChange("Synchronization skipped because the converted value is null.");
            }

            if (binding.WritePolicy == BindingWritePolicy.WhenValueChanges &&
                state.Initialized &&
                BindingValueComparer.AreEqual(targetValue, state.LastTargetValue))
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

            if (!TryReadReverseValue(
                    binding,
                    sourceMetadata.ValueType,
                    targetMetadata.ValueType,
                    out object targetValue,
                    out object sourceValue,
                    out bool skipSynchronization,
                    out BindingSyncStatus readStatus,
                    out string readError))
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
                BindingValueComparer.AreEqual(sourceValue, state.LastSourceValue))
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
                    out string sourceReadError))
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

            if (!TryConvertForward(
                    binding,
                    sourceValue,
                    sourceMetadata.ValueType,
                    targetMetadata.ValueType,
                    out object sourceAsTargetValue,
                    out bool skipForward,
                    out BindingSyncStatus forwardStatus,
                    out string forwardError))
            {
                return new BindingSyncResult(forwardStatus, forwardError);
            }

            if (skipForward)
            {
                return BindingSyncResult.NoChange("Two-way synchronization skipped by the null handling policy.");
            }

            // Initialize from Source before attempting reverse conversion. The Target may
            // contain a value that is not reversible yet, such as an empty input string.
            if (!state.Initialized)
            {
                if (!TryWriteTargetAndCaptureActualValue(
                        binding,
                        sourceAsTargetValue,
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

            if (!TryReadReverseValue(
                    binding,
                    sourceMetadata.ValueType,
                    targetMetadata.ValueType,
                    out object targetValue,
                    out object targetAsSourceValue,
                    out bool skipTarget,
                    out BindingSyncStatus targetReadStatus,
                    out string targetReadError))
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

            if (sourceChanged && !targetChanged)
            {
                if (!TryWriteTargetAndCaptureActualValue(
                        binding,
                        sourceAsTargetValue,
                        out object writtenTargetValue,
                        out BindingSyncStatus sourceWriteStatus,
                        out string sourceWriteError))
                {
                    return CreateEndpointFailureResult(
                        binding.Target,
                        sourceWriteStatus,
                        sourceWriteError,
                        BindingEndpointRole.Target);
                }

                state.LastSourceValue = sourceValue;
                state.LastTargetValue = writtenTargetValue;
                return BindingSyncResult.Success("Source change propagated to Target through the Converter.");
            }

            if (!sourceChanged && targetChanged)
            {
                if (!TryWriteSourceAndCaptureActualValue(
                        binding,
                        targetAsSourceValue,
                        out object writtenSourceValue,
                        out BindingSyncStatus targetWriteStatus,
                        out string targetWriteError))
                {
                    return CreateSourceFailureResult(
                        binding.Sources,
                        targetWriteStatus,
                        targetWriteError);
                }

                state.LastSourceValue = writtenSourceValue;
                state.LastTargetValue = targetValue;
                return BindingSyncResult.Success("Target change propagated to Source through reverse conversion.");
            }

            if (binding.ConflictResolution == BindingConflictResolution.SourceWins)
            {
                if (!TryWriteTargetAndCaptureActualValue(
                        binding,
                        sourceAsTargetValue,
                        out object conflictTargetValue,
                        out BindingSyncStatus conflictStatus,
                        out string conflictError))
                {
                    return CreateEndpointFailureResult(
                        binding.Target,
                        conflictStatus,
                        conflictError,
                        BindingEndpointRole.Target);
                }

                state.LastSourceValue = sourceValue;
                state.LastTargetValue = conflictTargetValue;
                return BindingSyncResult.Success("Both sides changed; Source won the conflict.");
            }

            if (!TryWriteSourceAndCaptureActualValue(
                    binding,
                    targetAsSourceValue,
                    out object conflictSourceValue,
                    out BindingSyncStatus reverseConflictStatus,
                    out string reverseConflictError))
            {
                return CreateSourceFailureResult(
                    binding.Sources,
                    reverseConflictStatus,
                    reverseConflictError);
            }

            state.LastSourceValue = conflictSourceValue;
            state.LastTargetValue = targetValue;
            return BindingSyncResult.Success("Both sides changed; Target won the conflict.");
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
            out string error)
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

                if (!TryGetFallbackValue(binding, sourceType, out object fallbackValue, out failureStatus, out error))
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
                if (!TryGetFallbackValue(binding, sourceType, out object fallbackValue, out failureStatus, out error))
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
            if (converter == null)
            {
                targetValue = sourceValue;
                return TryValidateRuntimeValueType(targetValue, targetType, "Source output", out error);
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

            return TryValidateRuntimeValueType(targetValue, targetType, $"Converter '{converter.name}'", out error);
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
            if (converter == null)
            {
                sourceValue = targetValue;
                return TryValidateRuntimeValueType(sourceValue, sourceType, "Target output", out error);
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

            return TryValidateRuntimeValueType(sourceValue, sourceType, $"Converter '{converter.name}' reverse output", out error);
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

        private static bool TryReadSourceOutput(
            ViewDataBinding binding,
            BindingRuntimeState state,
            Type outputType,
            IReadOnlyList<BindingMemberMetadata> inputMetadata,
            out object value,
            out bool skipSynchronization,
            out BindingSyncStatus failureStatus,
            out string error)
        {
            if (binding.Formatter == null || !binding.Formatter.Enabled)
            {
                return TryReadSingleSourceValue(
                    binding,
                    outputType,
                    out value,
                    out skipSynchronization,
                    out failureStatus,
                    out error);
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
                    return TryGetFallbackValue(binding, outputType, out value, out failureStatus, out error);
                }

                sourceValues.Add(processedValue);
            }

            if (!BindingFormatterRegistry.TryGet(binding.Formatter.FormatterId, out IBindingFormatter formatter))
            {
                failureStatus = BindingSyncStatus.FormatterFailed;
                error = $"Formatter '{binding.Formatter.FormatterId}' is not registered.";
                return false;
            }

            if (!formatter.TryFormat(sourceValues, binding.Formatter, out value, out string formatterError))
            {
                if (binding.Fallback != null &&
                    binding.Fallback.Enabled &&
                    binding.Fallback.UseOnFormatterFailure)
                {
                    return TryGetFallbackValue(binding, outputType, out value, out failureStatus, out error);
                }

                failureStatus = BindingSyncStatus.FormatterFailed;
                error = formatterError;
                return false;
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
                    return TryGetFallbackValue(binding, outputType, out value, out failureStatus, out error);
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
                return TryGetFallbackValue(binding, outputType, out value, out failureStatus, out error);
            }

            value = processedOutput;
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
            out string error)
        {
            value = null;
            skipSynchronization = false;

            if (!BindingBackendRegistry.SourceBackend.TryRead(binding.Sources, out object readValue, out string readError))
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

            if (!binding.Fallback.Value.TryGetValue(valueType, out value, out error))
            {
                failureStatus = BindingSyncStatus.FallbackFailed;
                return false;
            }

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
            if (type == null || !type.IsValueType)
            {
                return null;
            }

            return Activator.CreateInstance(type);
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
            ViewDataBindingProfileReference profileReference)
        {
            using (BindingResolutionScope.Push(this, contextResolver, profileReference))
            {
                BindingRuntimeState state = GetOrCreateState(stateKey);
                PrepareMissingEndpointRecovery(binding, state);

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

                return ApplyErrorPolicy(binding, state, result);
            }
        }

        private void PrepareMissingEndpointRecovery(
            ViewDataBinding binding,
            BindingRuntimeState state)
        {
            if (state.RuntimeDisabled || state.MissingEndpointRole == BindingEndpointRole.None)
            {
                return;
            }

            bool endpointAvailable = state.MissingEndpointRole == BindingEndpointRole.Source
                ? AreSourceEndpointsAvailable(binding.Sources)
                : IsEndpointAvailable(binding.Target);
            if (!endpointAvailable)
            {
                return;
            }

            InvalidateBindingCaches(binding);
            state.ResetSynchronizationValues();
            state.ClearMissingEndpoint();
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

            switch (policy)
            {
                case MissingEndpointPolicy.Wait:
                    return BindingSyncResult.NoChange(
                        $"Waiting for the missing {result.EndpointRole.ToString().ToLowerInvariant()} endpoint. {result.Message}");

                case MissingEndpointPolicy.Disable:
                    state.RuntimeDisabled = true;
                    return new BindingSyncResult(
                        BindingSyncStatus.Disabled,
                        $"Binding disabled because the {result.EndpointRole.ToString().ToLowerInvariant()} endpoint is missing. Invalidate it to retry.",
                        result.EndpointRole);

                case MissingEndpointPolicy.ClearTarget:
                    return TryClearTargetForMissingEndpoint(binding, state, result);

                case MissingEndpointPolicy.UseFallback:
                    return TryApplyMissingEndpointFallback(binding, state, result);

                case MissingEndpointPolicy.ReResolve:
                    InvalidateBindingCaches(binding);
                    state.ResetSynchronizationValues();
                    BindingSyncResult retryResult = Synchronize(binding, state);
                    if (retryResult.Status == BindingSyncStatus.UnresolvedInstance)
                    {
                        state.MarkMissingEndpoint(retryResult.EndpointRole, policy);
                        return new BindingSyncResult(
                            retryResult.Status,
                            $"Endpoint re-resolution did not succeed. {retryResult.Message}",
                            retryResult.EndpointRole);
                    }

                    state.ClearMissingEndpoint();
                    return retryResult;

                case MissingEndpointPolicy.ReportError:
                    return result;

                default:
                    return new BindingSyncResult(
                        BindingSyncStatus.InvalidMemberPath,
                        $"Unsupported missing endpoint policy: {policy}.",
                        result.EndpointRole);
            }
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

                if (!binding.Fallback.Value.TryGetValue(
                        targetMetadata.ValueType,
                        out object targetFallback,
                        out string fallbackError))
                {
                    return new BindingSyncResult(
                        BindingSyncStatus.FallbackFailed,
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

            if (!binding.Fallback.Value.TryGetValue(
                    sourceMetadata.ValueType,
                    out object sourceFallback,
                    out string sourceFallbackError))
            {
                return new BindingSyncResult(
                    BindingSyncStatus.FallbackFailed,
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
            }
        }

        private void InvalidateBindingCaches(ViewDataBinding binding)
        {
            contextResolver.Invalidate();
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

            string message = $"Binding '{binding.Name}' failed with {result.Status}: {result.Message}";
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

        protected override void ResetRuntimeState()
        {
            runtimeStates.Clear();
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

using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [AddComponentMenu("Legendary Tools/View Data Binder")]
    public sealed class ViewDataBinder : BindingPollingBehaviour
    {
        [SerializeField] private List<ViewDataBinding> bindings = new List<ViewDataBinding>();

        private readonly Dictionary<string, BindingRuntimeState> runtimeStates =
            new Dictionary<string, BindingRuntimeState>();

        public IReadOnlyList<ViewDataBinding> Bindings => bindings;

        public void SynchronizeManualBindings()
        {
            ProcessBindingTiming(BindingUpdateTiming.Manual);
        }

        public void SynchronizeAll()
        {
            EnsureBindingIds();

            for (int i = 0; i < bindings.Count; i++)
            {
                SynchronizeBinding(i);
            }
        }

        public BindingSyncResult SynchronizeBinding(int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= bindings.Count)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.InvalidMemberPath,
                    $"Binding index {bindingIndex} is outside the valid range.");
            }

            ViewDataBinding binding = bindings[bindingIndex];
            if (binding == null)
            {
                return new BindingSyncResult(BindingSyncStatus.InvalidMemberPath, "The binding is null.");
            }

            binding.EnsureId();
            BindingRuntimeState state = GetOrCreateState(binding.Id);
            BindingSyncResult result = Synchronize(binding, state);
            state.LastResult = result;
            return result;
        }

        public bool TryEvaluateSourceValue(
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
                    out BindingMemberMetadata sourceOutputMetadata,
                    out List<BindingMemberMetadata> sourceInputMetadata,
                    out string metadataError))
            {
                result = CreateMetadataFailureResult(metadataError);
                return false;
            }

            if (!BindingBackendRegistry.MemberBackend.TryGetMetadata(
                    binding.Target,
                    out BindingMemberMetadata targetMetadata,
                    out string targetMetadataError))
            {
                result = CreateMetadataFailureResult(targetMetadataError);
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

            if (!runtimeStates.TryGetValue(binding.Id, out BindingRuntimeState state))
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

                SynchronizeBinding(i);
            }
        }

        private static BindingSyncResult Synchronize(ViewDataBinding binding, BindingRuntimeState state)
        {
            if (!binding.Enabled)
            {
                state.ResetValues();
                return new BindingSyncResult(BindingSyncStatus.Disabled, "Binding is disabled.");
            }

            if (binding.Formatter != null &&
                binding.Formatter.Enabled &&
                binding.Direction != BindingSyncDirection.SourceToTarget)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.FormatterFailed,
                    "Formatters are supported only for Source -> Target bindings because formatting is not reversible.");
            }

            IBindingMemberBackend memberBackend = BindingBackendRegistry.MemberBackend;

            if (!TryGetSourceOutputMetadata(
                    binding,
                    out BindingMemberMetadata sourceMetadata,
                    out List<BindingMemberMetadata> sourceInputMetadata,
                    out string sourceMetadataError))
            {
                return CreateMetadataFailureResult(sourceMetadataError);
            }

            if (!memberBackend.TryGetMetadata(
                    binding.Target,
                    out BindingMemberMetadata targetMetadata,
                    out string targetMetadataError))
            {
                return CreateMetadataFailureResult(targetMetadataError);
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
                return new BindingSyncResult(BindingSyncStatus.ReadFailed, "The Source output is not readable.");
            }

            if (!targetMetadata.CanWrite)
            {
                return new BindingSyncResult(BindingSyncStatus.WriteFailed, "The Target path is not writable.");
            }

            if (!TryReadSourceOutput(
                    binding,
                    sourceMetadata.ValueType,
                    sourceInputMetadata,
                    out object sourceValue,
                    out bool skipSynchronization,
                    out BindingSyncStatus readStatus,
                    out string readError))
            {
                return new BindingSyncResult(readStatus, readError);
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

            if (!BindingBackendRegistry.MemberBackend.TryWrite(binding.Target, targetValue, out string writeError))
            {
                return new BindingSyncResult(BindingSyncStatus.WriteFailed, writeError);
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
                return new BindingSyncResult(BindingSyncStatus.ReadFailed, "The Target path is not readable.");
            }

            if (!sourceMetadata.CanWrite)
            {
                return new BindingSyncResult(BindingSyncStatus.WriteFailed, "The Source path is not writable.");
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
                return new BindingSyncResult(readStatus, readError);
            }

            if (skipSynchronization)
            {
                return BindingSyncResult.NoChange("Synchronization skipped because the Target value is null.");
            }

            if (!BindingBackendRegistry.SourceBackend.TryWrite(binding.Sources, sourceValue, out string writeError))
            {
                return new BindingSyncResult(BindingSyncStatus.WriteFailed, writeError);
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
                    "Two-way binding requires the Source path to be readable and writable.");
            }

            if (!targetMetadata.CanRead || !targetMetadata.CanWrite)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.WriteFailed,
                    "Two-way binding requires the Target path to be readable and writable.");
            }

            if (!TryReadSingleSourceValue(
                    binding,
                    sourceMetadata.ValueType,
                    out object sourceValue,
                    out bool skipSource,
                    out BindingSyncStatus sourceReadStatus,
                    out string sourceReadError))
            {
                return new BindingSyncResult(sourceReadStatus, sourceReadError);
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
                return new BindingSyncResult(targetReadStatus, targetReadError);
            }

            if (skipForward || skipTarget)
            {
                return BindingSyncResult.NoChange("Two-way synchronization skipped by the null handling policy.");
            }

            if (!state.Initialized)
            {
                if (!BindingBackendRegistry.MemberBackend.TryWrite(binding.Target, sourceAsTargetValue, out string initializationError))
                {
                    return new BindingSyncResult(BindingSyncStatus.WriteFailed, initializationError);
                }

                state.Initialized = true;
                state.LastSourceValue = sourceValue;
                state.LastTargetValue = sourceAsTargetValue;
                return BindingSyncResult.Success("Two-way binding initialized from Source to Target.");
            }

            bool sourceChanged = !ValuesEqual(sourceValue, state.LastSourceValue);
            bool targetChanged = !ValuesEqual(targetValue, state.LastTargetValue);

            if (!sourceChanged && !targetChanged)
            {
                return BindingSyncResult.NoChange();
            }

            if (sourceChanged && !targetChanged)
            {
                if (!BindingBackendRegistry.MemberBackend.TryWrite(binding.Target, sourceAsTargetValue, out string sourceWriteError))
                {
                    return new BindingSyncResult(BindingSyncStatus.WriteFailed, sourceWriteError);
                }

                state.LastSourceValue = sourceValue;
                state.LastTargetValue = sourceAsTargetValue;
                return BindingSyncResult.Success("Source change propagated to Target through the Converter.");
            }

            if (!sourceChanged && targetChanged)
            {
                if (!BindingBackendRegistry.SourceBackend.TryWrite(binding.Sources, targetAsSourceValue, out string targetWriteError))
                {
                    return new BindingSyncResult(BindingSyncStatus.WriteFailed, targetWriteError);
                }

                state.LastSourceValue = targetAsSourceValue;
                state.LastTargetValue = targetValue;
                return BindingSyncResult.Success("Target change propagated to Source through reverse conversion.");
            }

            if (binding.ConflictResolution == BindingConflictResolution.SourceWins)
            {
                if (!BindingBackendRegistry.MemberBackend.TryWrite(binding.Target, sourceAsTargetValue, out string conflictError))
                {
                    return new BindingSyncResult(BindingSyncStatus.WriteFailed, conflictError);
                }

                state.LastSourceValue = sourceValue;
                state.LastTargetValue = sourceAsTargetValue;
                return BindingSyncResult.Success("Both sides changed; Source won the conflict.");
            }

            if (!BindingBackendRegistry.SourceBackend.TryWrite(binding.Sources, targetAsSourceValue, out string reverseConflictError))
            {
                return new BindingSyncResult(BindingSyncStatus.WriteFailed, reverseConflictError);
            }

            state.LastSourceValue = targetAsSourceValue;
            state.LastTargetValue = targetValue;
            return BindingSyncResult.Success("Both sides changed; Target won the conflict.");
        }

        private static bool TryGetSourceOutputMetadata(
            ViewDataBinding binding,
            out BindingMemberMetadata outputMetadata,
            out List<BindingMemberMetadata> inputMetadata,
            out string error)
        {
            inputMetadata = new List<BindingMemberMetadata>();

            if (binding.Formatter == null || !binding.Formatter.Enabled)
            {
                if (!BindingBackendRegistry.SourceBackend.TryGetMetadata(
                        binding.Sources,
                        out outputMetadata,
                        out error))
                {
                    return false;
                }

                inputMetadata.Add(outputMetadata);
                return true;
            }

            if (binding.Sources == null || binding.Sources.Count == 0)
            {
                outputMetadata = default;
                error = "A formatter requires at least one Source.";
                return false;
            }

            for (int i = 0; i < binding.Sources.Count; i++)
            {
                BindingSource source = binding.Sources[i];
                if (source == null || source.Endpoint == null)
                {
                    outputMetadata = default;
                    error = $"Source {i + 1} is null.";
                    return false;
                }

                if (!BindingBackendRegistry.MemberBackend.TryGetMetadata(
                        source.Endpoint,
                        out BindingMemberMetadata metadata,
                        out string metadataError))
                {
                    outputMetadata = default;
                    error = $"Source {i + 1}: {metadataError}";
                    return false;
                }

                if (!metadata.CanRead)
                {
                    outputMetadata = default;
                    error = $"Source {i + 1} is not readable.";
                    return false;
                }

                inputMetadata.Add(metadata);
            }

            if (!BindingFormatterRegistry.TryGet(binding.Formatter.FormatterId, out IBindingFormatter formatter))
            {
                outputMetadata = default;
                error = $"Formatter '{binding.Formatter.FormatterId}' is not registered.";
                return false;
            }

            if (!formatter.TryGetOutputType(inputMetadata, out Type outputType, out error))
            {
                outputMetadata = default;
                return false;
            }

            if (outputType == null)
            {
                outputMetadata = default;
                error = $"Formatter '{formatter.DisplayName}' returned no output Type.";
                return false;
            }

            outputMetadata = new BindingMemberMetadata(outputType, true, false);
            return true;
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
            var sourceValues = new List<object>(binding.Sources.Count);

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

        private static BindingSyncResult CreateMetadataFailureResult(string error)
        {
            if (!string.IsNullOrEmpty(error) && error.Contains("exactly one source"))
            {
                return new BindingSyncResult(BindingSyncStatus.InvalidSourceCount, error);
            }

            if (!string.IsNullOrEmpty(error) && error.IndexOf("formatter", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new BindingSyncResult(BindingSyncStatus.FormatterFailed, error);
            }

            if (!string.IsNullOrEmpty(error) &&
                (error.Contains("assigned") || error.Contains("resolved") || error.Contains("Provider")))
            {
                return new BindingSyncResult(BindingSyncStatus.UnresolvedInstance, error);
            }

            return new BindingSyncResult(BindingSyncStatus.InvalidMemberPath, error);
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
        }

        private static bool ValuesEqual(object left, object right)
        {
            return Equals(left, right);
        }

        private static string GetTypeName(Type type)
        {
            return type?.FullName ?? "null";
        }
    }
}

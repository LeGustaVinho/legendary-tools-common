using System;

namespace LegendaryTools.ViewBinding
{
    internal sealed class BindingExecutionPlan
    {
        private object[] sourceIdentities = Array.Empty<object>();
        private Type[] sourceResolvedTypes = Array.Empty<Type>();
        private bool[] sourceStaticFlags = Array.Empty<bool>();
        private object targetIdentity;
        private Type targetResolvedType;
        private bool targetIsStatic;

        public IBindingGeneratedDirectPlan DirectPlan { get; private set; }

        public object SourceRootIdentity =>
            sourceIdentities.Length == 1 ? sourceIdentities[0] : null;

        public object TargetRootIdentity => targetIdentity;

        public bool IsCompiled { get; private set; }

        public bool IsValid { get; private set; }

        public BindingMemberMetadata SourceMetadata { get; private set; }

        public BindingMemberMetadata TargetMetadata { get; private set; }

        public BindingMemberMetadata[] SourceInputMetadata { get; private set; } =
            Array.Empty<BindingMemberMetadata>();

        public IBindingFormatter Formatter { get; private set; }

        public BindingSyncResult ValidationResult { get; private set; }

        public bool MatchesResolution(ViewDataBinding binding)
        {
            int sourceCount = binding?.Sources?.Count ?? 0;
            if (sourceIdentities.Length != sourceCount)
            {
                return false;
            }

            for (int i = 0; i < sourceCount; i++)
            {
                if (!MatchesEndpointResolution(
                        binding.Sources[i]?.Endpoint,
                        sourceIdentities[i],
                        sourceResolvedTypes[i],
                        sourceStaticFlags[i]))
                {
                    return false;
                }
            }

            return MatchesEndpointResolution(
                binding.Target,
                targetIdentity,
                targetResolvedType,
                targetIsStatic);
        }

        public bool TryCaptureResolution(
            ViewDataBinding binding,
            out BindingEndpointRole failedRole,
            out string error)
        {
            failedRole = BindingEndpointRole.None;
            int sourceCount = binding?.Sources?.Count ?? 0;
            EnsureSourceCapacity(sourceCount);
            for (int i = 0; i < sourceCount; i++)
            {
                if (!TryResolveIdentity(
                        binding.Sources[i]?.Endpoint,
                        out sourceIdentities[i],
                        out sourceResolvedTypes[i],
                        out sourceStaticFlags[i],
                        out error))
                {
                    failedRole = BindingEndpointRole.Source;
                    return false;
                }
            }

            if (!TryResolveIdentity(
                    binding?.Target,
                    out targetIdentity,
                    out targetResolvedType,
                    out targetIsStatic,
                    out error))
            {
                failedRole = BindingEndpointRole.Target;
                return false;
            }

            return true;
        }


        public void TryCreateGeneratedDirectPlan(
            ViewDataBinding binding,
            Type sourceValueType,
            Type targetValueType)
        {
            DirectPlan = null;
            if (binding == null ||
                binding.Direction != BindingSyncDirection.SourceToTarget ||
                binding.Sources == null ||
                binding.Sources.Count != 1 ||
                binding.Converter != null ||
                (binding.Formatter != null && binding.Formatter.Enabled) ||
                (binding.Fallback != null && binding.Fallback.Enabled) ||
                sourceValueType == null ||
                sourceValueType != targetValueType ||
                !sourceValueType.IsValueType ||
                Nullable.GetUnderlyingType(sourceValueType) != null)
            {
                return;
            }

            string sourcePath = GetResolvedMemberPath(binding.Sources[0]?.Endpoint);
            string targetPath = GetResolvedMemberPath(binding.Target);
            BindingGeneratedDirectPlanRegistry.TryCreate(
                sourceResolvedTypes[0],
                sourcePath,
                sourceStaticFlags[0],
                targetResolvedType,
                targetPath,
                targetIsStatic,
                sourceValueType,
                out IBindingGeneratedDirectPlan directPlan);
            DirectPlan = directPlan;
        }

        public void ResetRuntimeState()
        {
            DirectPlan?.Reset();
        }

        public void SetValid(
            BindingMemberMetadata sourceMetadata,
            BindingMemberMetadata targetMetadata,
            BindingMemberMetadata[] sourceInputMetadata,
            IBindingFormatter formatter)
        {
            SourceMetadata = sourceMetadata;
            TargetMetadata = targetMetadata;
            SourceInputMetadata = sourceInputMetadata ?? Array.Empty<BindingMemberMetadata>();
            Formatter = formatter;
            ValidationResult = BindingSyncResult.Success();
            IsValid = true;
            IsCompiled = true;
        }

        public void SetInvalid(BindingSyncResult result)
        {
            ClearResolution();
            ValidationResult = result;
            IsValid = false;
            IsCompiled = true;
        }

        public void Invalidate()
        {
            IsCompiled = false;
            IsValid = false;
            SourceMetadata = default;
            TargetMetadata = default;
            SourceInputMetadata = Array.Empty<BindingMemberMetadata>();
            Formatter = null;
            ValidationResult = default;
            ClearResolution();
        }

        internal static bool MatchesEndpointResolution(
            BindingEndpoint endpoint,
            object previousIdentity,
            Type previousType,
            bool previousIsStatic)
        {
            if (endpoint?.Instance == null)
            {
                return false;
            }

            BindingInstanceKind kind = endpoint.Instance.Kind;
            if (kind == BindingInstanceKind.StaticType)
            {
                return previousIsStatic && previousType != null;
            }

            if (kind == BindingInstanceKind.UnityObject)
            {
                if (previousIsStatic || previousType == null || previousIdentity == null)
                {
                    return false;
                }

                return !(previousIdentity is UnityEngine.Object unityObject) || unityObject != null;
            }

            if (!TryResolveIdentity(
                    endpoint,
                    out object currentIdentity,
                    out Type currentType,
                    out bool currentIsStatic,
                    out _))
            {
                return false;
            }

            return IdentityMatches(
                previousIdentity,
                previousType,
                previousIsStatic,
                currentIdentity,
                currentType,
                currentIsStatic);
        }

        internal static bool TryResolveIdentity(
            BindingEndpoint endpoint,
            out object identity,
            out Type resolvedType,
            out bool isStatic,
            out string error)
        {
            if (endpoint == null || endpoint.Instance == null)
            {
                identity = null;
                resolvedType = null;
                isStatic = false;
                error = "The binding endpoint or its instance reference is null.";
                return false;
            }

            if (BindingBackendRegistry.MemberBackend is IBindingEndpointIdentityBackend identityBackend)
            {
                return identityBackend.TryGetEndpointIdentity(
                    endpoint,
                    out identity,
                    out resolvedType,
                    out isStatic,
                    out error);
            }

            if (!endpoint.Instance.TryResolve(out BindingInstanceHandle handle, out error))
            {
                identity = null;
                resolvedType = null;
                isStatic = false;
                return false;
            }

            identity = handle.Instance;
            resolvedType = handle.Type;
            isStatic = handle.IsStatic;
            return true;
        }

        private static string GetResolvedMemberPath(BindingEndpoint endpoint)
        {
            string memberPath = endpoint?.MemberPath ?? string.Empty;
            return ComponentBindingPath.TryParse(
                memberPath,
                out _,
                out _,
                out string componentMemberPath)
                ? componentMemberPath
                : memberPath;
        }

        private static bool IdentityMatches(
            object previousIdentity,
            Type previousType,
            bool previousIsStatic,
            object currentIdentity,
            Type currentType,
            bool currentIsStatic)
        {
            if (previousType != currentType || previousIsStatic != currentIsStatic)
            {
                return false;
            }

            if (previousIsStatic || (previousType != null && previousType.IsValueType))
            {
                return true;
            }

            return ReferenceEquals(previousIdentity, currentIdentity);
        }

        private void EnsureSourceCapacity(int count)
        {
            if (sourceIdentities.Length == count)
            {
                return;
            }

            sourceIdentities = new object[count];
            sourceResolvedTypes = new Type[count];
            sourceStaticFlags = new bool[count];
        }

        private void ClearResolution()
        {
            DirectPlan = null;
            Array.Clear(sourceIdentities, 0, sourceIdentities.Length);
            Array.Clear(sourceResolvedTypes, 0, sourceResolvedTypes.Length);
            Array.Clear(sourceStaticFlags, 0, sourceStaticFlags.Length);
            targetIdentity = null;
            targetResolvedType = null;
            targetIsStatic = false;
        }
    }
}

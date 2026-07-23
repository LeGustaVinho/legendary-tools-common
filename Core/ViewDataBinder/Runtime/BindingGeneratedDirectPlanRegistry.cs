using System;
using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public static class BindingGeneratedDirectPlanRegistry
    {
        private static readonly Dictionary<DirectPlanKey, Func<IBindingGeneratedDirectPlan>> Factories =
            new Dictionary<DirectPlanKey, Func<IBindingGeneratedDirectPlan>>();

        public static void Register<TValue>(
            Type sourceRootType,
            string sourceMemberPath,
            bool sourceIsStatic,
            Type targetRootType,
            string targetMemberPath,
            bool targetIsStatic,
            Func<object, TValue> getter,
            Action<object, TValue> setter)
        {
            if (sourceRootType == null)
            {
                throw new ArgumentNullException(nameof(sourceRootType));
            }

            if (targetRootType == null)
            {
                throw new ArgumentNullException(nameof(targetRootType));
            }

            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }

            if (setter == null)
            {
                throw new ArgumentNullException(nameof(setter));
            }

            var key = new DirectPlanKey(
                sourceRootType,
                sourceMemberPath,
                sourceIsStatic,
                targetRootType,
                targetMemberPath,
                targetIsStatic,
                typeof(TValue));
            Factories[key] = () => new GeneratedDirectBindingPlan<TValue>(getter, setter);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Clear();
        }

        public static void Clear()
        {
            Factories.Clear();
        }

        internal static bool TryCreate(
            Type sourceRootType,
            string sourceMemberPath,
            bool sourceIsStatic,
            Type targetRootType,
            string targetMemberPath,
            bool targetIsStatic,
            Type valueType,
            out IBindingGeneratedDirectPlan plan)
        {
            var key = new DirectPlanKey(
                sourceRootType,
                sourceMemberPath,
                sourceIsStatic,
                targetRootType,
                targetMemberPath,
                targetIsStatic,
                valueType);
            if (Factories.TryGetValue(key, out Func<IBindingGeneratedDirectPlan> factory))
            {
                plan = factory();
                return true;
            }

            plan = null;
            return false;
        }

        private readonly struct DirectPlanKey : IEquatable<DirectPlanKey>
        {
            public DirectPlanKey(
                Type sourceRootType,
                string sourceMemberPath,
                bool sourceIsStatic,
                Type targetRootType,
                string targetMemberPath,
                bool targetIsStatic,
                Type valueType)
            {
                SourceRootType = sourceRootType;
                SourceMemberPath = sourceMemberPath;
                SourceIsStatic = sourceIsStatic;
                TargetRootType = targetRootType;
                TargetMemberPath = targetMemberPath;
                TargetIsStatic = targetIsStatic;
                ValueType = valueType;
            }

            private Type SourceRootType { get; }
            private string SourceMemberPath { get; }
            private bool SourceIsStatic { get; }
            private Type TargetRootType { get; }
            private string TargetMemberPath { get; }
            private bool TargetIsStatic { get; }
            private Type ValueType { get; }

            public bool Equals(DirectPlanKey other)
            {
                return SourceRootType == other.SourceRootType &&
                       SourceIsStatic == other.SourceIsStatic &&
                       TargetRootType == other.TargetRootType &&
                       TargetIsStatic == other.TargetIsStatic &&
                       ValueType == other.ValueType &&
                       string.Equals(SourceMemberPath, other.SourceMemberPath, StringComparison.Ordinal) &&
                       string.Equals(TargetMemberPath, other.TargetMemberPath, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is DirectPlanKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = SourceRootType != null ? SourceRootType.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ SourceIsStatic.GetHashCode();
                    hashCode = (hashCode * 397) ^
                               (SourceMemberPath != null
                                   ? StringComparer.Ordinal.GetHashCode(SourceMemberPath)
                                   : 0);
                    hashCode = (hashCode * 397) ^
                               (TargetRootType != null ? TargetRootType.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ TargetIsStatic.GetHashCode();
                    hashCode = (hashCode * 397) ^
                               (TargetMemberPath != null
                                   ? StringComparer.Ordinal.GetHashCode(TargetMemberPath)
                                   : 0);
                    hashCode = (hashCode * 397) ^
                               (ValueType != null ? ValueType.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }
    }

    internal interface IBindingGeneratedDirectPlan
    {
        BindingSyncResult Synchronize(
            object sourceRoot,
            object targetRoot,
            BindingWritePolicy writePolicy);

        void Reset();
    }

    internal sealed class GeneratedDirectBindingPlan<TValue> : IBindingGeneratedDirectPlan
    {
        private readonly Func<object, TValue> getter;
        private readonly Action<object, TValue> setter;
        private readonly EqualityComparer<TValue> comparer = EqualityComparer<TValue>.Default;
        private TValue lastValue;
        private bool initialized;

        public GeneratedDirectBindingPlan(
            Func<object, TValue> getter,
            Action<object, TValue> setter)
        {
            this.getter = getter;
            this.setter = setter;
        }

        public BindingSyncResult Synchronize(
            object sourceRoot,
            object targetRoot,
            BindingWritePolicy writePolicy)
        {
            TValue value;
            try
            {
                value = getter(sourceRoot);
            }
            catch (Exception exception)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.ReadFailed,
                    GetInnermostMessage(exception),
                    BindingEndpointRole.Source);
            }

            if (writePolicy == BindingWritePolicy.WhenValueChanges &&
                initialized &&
                comparer.Equals(lastValue, value))
            {
                return BindingSyncResult.NoChange("The generated Source value has not changed.");
            }

            try
            {
                setter(targetRoot, value);
            }
            catch (Exception exception)
            {
                return new BindingSyncResult(
                    BindingSyncStatus.WriteFailed,
                    GetInnermostMessage(exception),
                    BindingEndpointRole.Target);
            }

            lastValue = value;
            initialized = true;
            return BindingSyncResult.Success("Generated typed binding synchronized.");
        }

        public void Reset()
        {
            lastValue = default;
            initialized = false;
        }

        private static string GetInnermostMessage(Exception exception)
        {
            while (exception.InnerException != null)
            {
                exception = exception.InnerException;
            }

            return exception.Message;
        }
    }
}

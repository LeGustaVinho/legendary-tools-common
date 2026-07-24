using System;

namespace LegendaryTools.ViewBinding
{
    public static class BindingBackendRegistry
    {
        private static IBindingInstanceResolver instanceResolver = new DefaultBindingInstanceResolver();
        private static IBindingMemberBackend memberBackend = new ReflectionBindingMemberBackend();
        private static IBindingSourceBackend sourceBackend = new SingleSourceBindingSourceBackend();

        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            DefaultBindingInstanceResolver.ClearTypeCache();
        }

        public static IBindingInstanceResolver InstanceResolver
        {
            get => instanceResolver;
            set => instanceResolver = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static IBindingMemberBackend MemberBackend
        {
            get => memberBackend;
            set => memberBackend = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static IBindingSourceBackend SourceBackend
        {
            get => sourceBackend;
            set => sourceBackend = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static void ResetDefaults()
        {
            instanceResolver = new DefaultBindingInstanceResolver();
            memberBackend = new ReflectionBindingMemberBackend();
            sourceBackend = new SingleSourceBindingSourceBackend();
            DefaultBindingInstanceResolver.ClearTypeCache();
            BindingFormatterRegistry.ResetDefaults();
        }
    }
}

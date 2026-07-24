using System;
using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public static class BindingGeneratedAccessorRegistry
    {
        private static readonly Dictionary<AccessorKey, Accessor> Accessors =
            new Dictionary<AccessorKey, Accessor>();

        public static void Register(
            Type rootType,
            string memberPath,
            bool isStatic,
            Func<object, object> getter,
            Action<object, object> setter = null)
        {
            if (rootType == null)
            {
                throw new ArgumentNullException(nameof(rootType));
            }

            if (string.IsNullOrWhiteSpace(memberPath))
            {
                throw new ArgumentException("Member path is required.", nameof(memberPath));
            }

            Accessors[new AccessorKey(rootType, memberPath, isStatic)] =
                new Accessor(getter, setter);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Clear();
        }

        public static void Clear()
        {
            Accessors.Clear();
        }

        internal static bool TryGet(
            Type rootType,
            string memberPath,
            bool isStatic,
            out Func<object, object> getter,
            out Action<object, object> setter)
        {
            if (Accessors.TryGetValue(
                    new AccessorKey(rootType, memberPath, isStatic),
                    out Accessor accessor))
            {
                getter = accessor.Getter;
                setter = accessor.Setter;
                return true;
            }

            getter = null;
            setter = null;
            return false;
        }

        private readonly struct AccessorKey : IEquatable<AccessorKey>
        {
            public AccessorKey(Type rootType, string memberPath, bool isStatic)
            {
                RootType = rootType;
                MemberPath = memberPath;
                IsStatic = isStatic;
            }

            private Type RootType { get; }

            private string MemberPath { get; }

            private bool IsStatic { get; }

            public bool Equals(AccessorKey other)
            {
                return RootType == other.RootType &&
                       IsStatic == other.IsStatic &&
                       string.Equals(MemberPath, other.MemberPath, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is AccessorKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = RootType != null ? RootType.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsStatic.GetHashCode();
                    hashCode = (hashCode * 397) ^
                               (MemberPath != null
                                   ? StringComparer.Ordinal.GetHashCode(MemberPath)
                                   : 0);
                    return hashCode;
                }
            }
        }

        private readonly struct Accessor
        {
            public Accessor(Func<object, object> getter, Action<object, object> setter)
            {
                Getter = getter;
                Setter = setter;
            }

            public Func<object, object> Getter { get; }

            public Action<object, object> Setter { get; }
        }
    }
}

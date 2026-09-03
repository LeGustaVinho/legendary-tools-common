using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FlatData
{
    internal static class TypeMetadataCache
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<CacheKey, IReadOnlyList<MemberAccessor>> Cache =
            new Dictionary<CacheKey, IReadOnlyList<MemberAccessor>>();

        public static IReadOnlyList<MemberAccessor> GetMembers(
            Type type,
            FlattenOptions options)
        {
            CacheKey key = new CacheKey(
                type,
                options.IncludeFields,
                options.IncludeProperties,
                options.BindingFlags);

            lock (SyncRoot)
            {
                IReadOnlyList<MemberAccessor> members;
                if (Cache.TryGetValue(key, out members))
                {
                    return members;
                }

                members = BuildMembers(type, options);
                Cache[key] = members;
                return members;
            }
        }

        public static MemberAccessor FindWritableMember(
            Type type,
            string memberName)
        {
            PropertyInfo property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public);

            if (property != null &&
                property.GetIndexParameters().Length == 0 &&
                property.GetSetMethod(true) != null)
            {
                return new PropertyAccessor(property);
            }

            FieldInfo field = type.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public);

            if (field != null && !field.IsInitOnly && !field.IsLiteral)
            {
                return new FieldAccessor(field);
            }

            return null;
        }

        private static IReadOnlyList<MemberAccessor> BuildMembers(
            Type type,
            FlattenOptions options)
        {
            List<MemberAccessor> members = new List<MemberAccessor>();

            if (options.IncludeProperties)
            {
                members.AddRange(
                    type.GetProperties(options.BindingFlags)
                        .Where(property =>
                            property.GetIndexParameters().Length == 0 &&
                            property.GetGetMethod(true) != null)
                        .Select(property => (MemberAccessor)new PropertyAccessor(property)));
            }

            if (options.IncludeFields)
            {
                members.AddRange(
                    type.GetFields(options.BindingFlags)
                        .Where(field => !field.IsStatic)
                        .Select(field => (MemberAccessor)new FieldAccessor(field)));
            }

            return members
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .ToArray();
        }

        private struct CacheKey : IEquatable<CacheKey>
        {
            public CacheKey(
                Type type,
                bool includeFields,
                bool includeProperties,
                BindingFlags bindingFlags)
            {
                Type = type;
                IncludeFields = includeFields;
                IncludeProperties = includeProperties;
                BindingFlags = bindingFlags;
            }

            public Type Type { get; }

            public bool IncludeFields { get; }

            public bool IncludeProperties { get; }

            public BindingFlags BindingFlags { get; }

            public bool Equals(CacheKey other)
            {
                return Type == other.Type &&
                       IncludeFields == other.IncludeFields &&
                       IncludeProperties == other.IncludeProperties &&
                       BindingFlags == other.BindingFlags;
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey && Equals((CacheKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = Type != null ? Type.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IncludeFields.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeProperties.GetHashCode();
                    hashCode = (hashCode * 397) ^ (int)BindingFlags;
                    return hashCode;
                }
            }
        }
    }
}

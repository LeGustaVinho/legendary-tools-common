using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace FlatData
{
    internal static class TypeUtility
    {
        public static bool IsSimple(Type type)
        {
            Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;

            return effectiveType.IsPrimitive ||
                   effectiveType.IsEnum ||
                   effectiveType == typeof(string) ||
                   effectiveType == typeof(decimal) ||
                   effectiveType == typeof(DateTime) ||
                   effectiveType == typeof(DateTimeOffset) ||
                   effectiveType == typeof(TimeSpan) ||
                   effectiveType == typeof(Guid);
        }

        public static bool IsEnumerable(Type type)
        {
            return type != typeof(string) &&
                   typeof(IEnumerable).IsAssignableFrom(type);
        }

        public static Type GetCollectionElementType(Type collectionType)
        {
            if (collectionType.IsArray)
            {
                return collectionType.GetElementType();
            }

            Type genericEnumerable = collectionType
                .GetInterfaces()
                .Concat(new[] { collectionType })
                .FirstOrDefault(type =>
                    type.IsGenericType &&
                    type.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return genericEnumerable != null
                ? genericEnumerable.GetGenericArguments()[0]
                : typeof(object);
        }

        public static Type TryGetEnumerableItemType(Type enumerableType)
        {
            if (enumerableType == null)
            {
                return null;
            }

            return GetCollectionElementType(enumerableType);
        }
    }
}

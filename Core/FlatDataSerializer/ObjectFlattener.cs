using System;
using System.Collections;
using System.Collections.Generic;

namespace FlatData
{
    internal sealed class ObjectFlattener
    {
        private readonly FlattenOptions _options;

        public ObjectFlattener(FlattenOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (_options.MaximumDepth < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "MaximumDepth must be at least 1.");
            }
        }

        public FlatObject Flatten(object instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            Type rootType = instance.GetType();
            string rootPath = _options.IncludeRootTypeName
                ? rootType.Name
                : string.Empty;

            Dictionary<string, object> output =
                new Dictionary<string, object>(StringComparer.Ordinal);

            HashSet<object> visited =
                new HashSet<object>(ReferenceEqualityComparer.Instance);

            FlattenValue(
                instance,
                rootType,
                rootPath,
                output,
                visited,
                0);

            return new FlatObject(rootType, output);
        }

        private void FlattenValue(
            object value,
            Type declaredType,
            string path,
            IDictionary<string, object> output,
            ISet<object> visited,
            int depth)
        {
            if (depth > _options.MaximumDepth)
            {
                throw new InvalidOperationException(
                    $"Maximum depth of {_options.MaximumDepth} exceeded at '{path}'.");
            }

            if (value == null)
            {
                if (_options.PreserveNullValues && !string.IsNullOrEmpty(path))
                {
                    output[path] = null;
                }

                return;
            }

            Type runtimeType = value.GetType();

            if (TypeUtility.IsSimple(runtimeType))
            {
                if (string.IsNullOrEmpty(path))
                {
                    throw new InvalidOperationException(
                        "A simple root value must have a root path.");
                }

                output[path] = value;
                return;
            }

            bool trackReference = !runtimeType.IsValueType;
            if (trackReference && !visited.Add(value))
            {
                throw new InvalidOperationException(
                    $"Circular or repeated reference detected at '{path}'.");
            }

            try
            {
                if (TypeUtility.IsEnumerable(runtimeType))
                {
                    int index = 0;
                    foreach (object item in (IEnumerable)value)
                    {
                        Type itemType = item != null
                            ? item.GetType()
                            : TypeUtility.GetCollectionElementType(declaredType);

                        FlattenValue(
                            item,
                            itemType,
                            AppendIndex(path, index),
                            output,
                            visited,
                            depth + 1);

                        index++;
                    }

                    return;
                }

                IReadOnlyList<MemberAccessor> members =
                    TypeMetadataCache.GetMembers(runtimeType, _options);

                foreach (MemberAccessor member in members)
                {
                    if (!member.CanRead)
                    {
                        continue;
                    }

                    object memberValue = member.GetValue(value);
                    string memberPath = AppendMember(path, member.Name);

                    FlattenValue(
                        memberValue,
                        member.MemberType,
                        memberPath,
                        output,
                        visited,
                        depth + 1);
                }
            }
            finally
            {
                if (trackReference)
                {
                    visited.Remove(value);
                }
            }
        }

        private static string AppendMember(string path, string memberName)
        {
            return string.IsNullOrEmpty(path)
                ? memberName
                : path + "." + memberName;
        }

        private static string AppendIndex(string path, int index)
        {
            return path + "[" + index + "]";
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace FlatData
{
    internal sealed class ObjectUnflattener
    {
        private readonly UnflattenOptions _options;

        public ObjectUnflattener(UnflattenOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.ObjectFactory = _options.ObjectFactory ?? new DefaultObjectFactory();
            _options.ValueConverter = _options.ValueConverter ?? new DefaultValueConverter();
        }

        public object Unflatten(Type targetType, IReadOnlyDictionary<string, object> values)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            DataNode root = BuildTree(targetType, values);
            return Materialize(targetType, root);
        }

        private DataNode BuildTree(
            Type targetType,
            IReadOnlyDictionary<string, object> values)
        {
            DataNode root = new DataNode();

            foreach (KeyValuePair<string, object> pair in values)
            {
                IReadOnlyList<PathToken> tokens = PathParser.Parse(pair.Key);
                int startIndex = 0;

                if (_options.IncludeRootTypeName &&
                    tokens.Count > 0 &&
                    tokens[0].Type == PathTokenType.Member &&
                    string.Equals(
                        tokens[0].MemberName,
                        targetType.Name,
                        StringComparison.Ordinal))
                {
                    startIndex = 1;
                }

                DataNode current = root;

                for (int index = startIndex; index < tokens.Count; index++)
                {
                    PathToken token = tokens[index];

                    if (token.Type == PathTokenType.Member)
                    {
                        current = current.GetOrCreateMember(token.MemberName);
                    }
                    else
                    {
                        current = current.GetOrCreateIndex(token.Index);
                    }
                }

                current.HasValue = true;
                current.Value = pair.Value;
            }

            return root;
        }

        private object Materialize(Type targetType, DataNode node)
        {
            Type nullableType = Nullable.GetUnderlyingType(targetType);
            Type effectiveType = nullableType ?? targetType;

            if (node.HasValue && node.MemberChildren.Count == 0 && node.IndexChildren.Count == 0)
            {
                return _options.ValueConverter.Convert(node.Value, targetType);
            }

            if (TypeUtility.IsSimple(effectiveType))
            {
                return _options.ValueConverter.Convert(
                    node.HasValue ? node.Value : null,
                    targetType);
            }

            if (effectiveType.IsArray)
            {
                return MaterializeArray(effectiveType, node);
            }

            if (typeof(IList).IsAssignableFrom(effectiveType) ||
                ImplementsGenericList(effectiveType))
            {
                return MaterializeList(effectiveType, node);
            }

            if (node.HasValue && node.Value == null &&
                node.MemberChildren.Count == 0 &&
                node.IndexChildren.Count == 0)
            {
                return null;
            }

            object instance = _options.ObjectFactory.Create(effectiveType);

            foreach (KeyValuePair<string, DataNode> pair in node.MemberChildren)
            {
                MemberAccessor member =
                    TypeMetadataCache.FindWritableMember(effectiveType, pair.Key);

                if (member == null)
                {
                    throw new InvalidOperationException(
                        $"Writable public member '{pair.Key}' was not found on " +
                        $"type '{effectiveType.FullName}'.");
                }

                object memberValue = Materialize(member.MemberType, pair.Value);
                member.SetValue(instance, memberValue);
            }

            return instance;
        }

        private object MaterializeArray(Type arrayType, DataNode node)
        {
            Type elementType = arrayType.GetElementType();
            int length = node.IndexChildren.Count == 0
                ? 0
                : node.IndexChildren.Keys.Max() + 1;

            Array array = Array.CreateInstance(elementType, length);

            foreach (KeyValuePair<int, DataNode> pair in node.IndexChildren)
            {
                array.SetValue(Materialize(elementType, pair.Value), pair.Key);
            }

            return array;
        }

        private object MaterializeList(Type listType, DataNode node)
        {
            Type elementType = TypeUtility.GetCollectionElementType(listType);
            Type concreteType = listType;

            if (listType.IsInterface || listType.IsAbstract)
            {
                concreteType = typeof(List<>).MakeGenericType(elementType);
            }

            IList list = (IList)_options.ObjectFactory.Create(concreteType);

            int length = node.IndexChildren.Count == 0
                ? 0
                : node.IndexChildren.Keys.Max() + 1;

            for (int index = 0; index < length; index++)
            {
                DataNode child;
                object value = node.IndexChildren.TryGetValue(index, out child)
                    ? Materialize(elementType, child)
                    : GetDefaultValue(elementType);

                list.Add(value);
            }

            return list;
        }

        private static bool ImplementsGenericList(Type type)
        {
            return type
                .GetInterfaces()
                .Concat(new[] { type })
                .Any(candidate =>
                    candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IList<>));
        }

        private static object GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private sealed class DataNode
        {
            public DataNode()
            {
                MemberChildren =
                    new Dictionary<string, DataNode>(StringComparer.Ordinal);

                IndexChildren = new SortedDictionary<int, DataNode>();
            }

            public bool HasValue { get; set; }

            public object Value { get; set; }

            public Dictionary<string, DataNode> MemberChildren { get; }

            public SortedDictionary<int, DataNode> IndexChildren { get; }

            public DataNode GetOrCreateMember(string memberName)
            {
                DataNode node;
                if (!MemberChildren.TryGetValue(memberName, out node))
                {
                    node = new DataNode();
                    MemberChildren.Add(memberName, node);
                }

                return node;
            }

            public DataNode GetOrCreateIndex(int index)
            {
                DataNode node;
                if (!IndexChildren.TryGetValue(index, out node))
                {
                    node = new DataNode();
                    IndexChildren.Add(index, node);
                }

                return node;
            }
        }
    }
}

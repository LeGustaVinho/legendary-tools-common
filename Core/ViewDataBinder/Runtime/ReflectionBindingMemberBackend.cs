using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    public sealed class ReflectionBindingMemberBackend :
        IBindingMemberBackend,
        IBindingMemberSearchBackend,
        IBindingMemberCacheInvalidator,
        IBindingEndpointAvailabilityBackend
    {
        private readonly Dictionary<string, MemberInfo[]> pathCache = new Dictionary<string, MemberInfo[]>();
        private readonly Dictionary<string, IReadOnlyList<BindingMemberDescriptor>> memberTreeCache =
            new Dictionary<string, IReadOnlyList<BindingMemberDescriptor>>();
        private readonly Dictionary<string, IReadOnlyList<MemberInfo>> bindableMemberCache =
            new Dictionary<string, IReadOnlyList<MemberInfo>>();
        private const int MaxSearchCacheEntries = 256;

        private readonly Dictionary<string, IReadOnlyList<BindingMemberDescriptor>> searchCache =
            new Dictionary<string, IReadOnlyList<BindingMemberDescriptor>>();
        private readonly Queue<string> searchCacheOrder = new Queue<string>();
        private readonly ConditionalWeakTable<BindingEndpoint, EndpointResolutionCache> endpointCache =
            new ConditionalWeakTable<BindingEndpoint, EndpointResolutionCache>();
        private readonly List<Component> componentBuffer = new List<Component>(8);

        public IReadOnlyList<BindingMemberDescriptor> GetMembers(BindingInstanceHandle root, int maxDepth)
        {
            if (root.Type == null || maxDepth < 1)
            {
                return Array.Empty<BindingMemberDescriptor>();
            }

            if (!root.IsStatic && root.Instance is GameObject gameObject)
            {
                return GetGameObjectMembers(gameObject, maxDepth);
            }

            string cacheKey = $"{root.Type.AssemblyQualifiedName}|{root.IsStatic}|{maxDepth}";
            if (memberTreeCache.TryGetValue(cacheKey, out IReadOnlyList<BindingMemberDescriptor> cachedMembers))
            {
                return cachedMembers;
            }

            IReadOnlyList<BindingMemberDescriptor> members = BuildDescriptors(
                root.Type,
                root.IsStatic,
                string.Empty,
                maxDepth,
                new HashSet<Type>(),
                true,
                true);

            memberTreeCache[cacheKey] = members;
            return members;
        }

        public IReadOnlyList<BindingMemberDescriptor> SearchMembers(
            BindingInstanceHandle root,
            int maxDepth,
            string query,
            int maxResults)
        {
            if (root.Type == null ||
                maxDepth < 1 ||
                maxResults < 1 ||
                string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<BindingMemberDescriptor>();
            }

            string normalizedQuery = query.Trim().ToLowerInvariant();
            string rootKey = GetSearchRootKey(root);
            string cacheKey = $"{rootKey}|{maxDepth}|{maxResults}|{normalizedQuery}";

            if (searchCache.TryGetValue(cacheKey, out IReadOnlyList<BindingMemberDescriptor> cachedResults))
            {
                return cachedResults;
            }

            var results = new List<BindingMemberDescriptor>();

            if (!root.IsStatic && root.Instance is GameObject gameObject)
            {
                SearchDescriptors(
                    typeof(GameObject),
                    false,
                    string.Empty,
                    maxDepth,
                    new HashSet<Type>(),
                    true,
                    true,
                    normalizedQuery,
                    "GameObject",
                    path => path,
                    maxResults,
                    results);

                Component[] components = gameObject.GetComponents<Component>();
                var typeOrdinals = new Dictionary<Type, int>();

                for (int i = 0; i < components.Length && results.Count < maxResults; i++)
                {
                    Component component = components[i];
                    if (component == null)
                    {
                        continue;
                    }

                    Type componentType = component.GetType();
                    typeOrdinals.TryGetValue(componentType, out int typeOrdinal);
                    typeOrdinals[componentType] = typeOrdinal + 1;

                    Type capturedType = componentType;
                    int capturedOrdinal = typeOrdinal;
                    SearchDescriptors(
                        componentType,
                        false,
                        string.Empty,
                        maxDepth,
                        new HashSet<Type>(),
                        true,
                        true,
                        normalizedQuery,
                        componentType.Name,
                        path => ComponentBindingPath.Create(capturedType, capturedOrdinal, path),
                        maxResults,
                        results);
                }
            }
            else
            {
                SearchDescriptors(
                    root.Type,
                    root.IsStatic,
                    string.Empty,
                    maxDepth,
                    new HashSet<Type>(),
                    true,
                    true,
                    normalizedQuery,
                    root.Type.Name,
                    path => path,
                    maxResults,
                    results);
            }

            CacheSearchResults(cacheKey, results);
            return results;
        }

        private void SearchDescriptors(
            Type type,
            bool staticOnly,
            string parentPath,
            int depthRemaining,
            HashSet<Type> ancestry,
            bool ancestorReadable,
            bool ancestorWritable,
            string normalizedQuery,
            string contextName,
            Func<string, string> pathTransform,
            int maxResults,
            List<BindingMemberDescriptor> results)
        {
            if (depthRemaining <= 0 ||
                type == null ||
                ancestry.Contains(type) ||
                results.Count >= maxResults)
            {
                return;
            }

            var nextAncestry = new HashSet<Type>(ancestry) { type };
            IReadOnlyList<MemberInfo> members = GetBindableMembers(type, staticOnly);

            for (int i = 0; i < members.Count && results.Count < maxResults; i++)
            {
                MemberInfo member = members[i];
                Type memberType = GetMemberType(member);
                bool memberReadable = ancestorReadable && CanRead(member);
                bool memberWritable = ancestorWritable && CanWrite(member);
                string path = string.IsNullOrEmpty(parentPath)
                    ? member.Name
                    : $"{parentPath}.{member.Name}";

                string typeName = memberType.FullName ?? memberType.Name;
                string haystack = $"{member.Name}\n{path}\n{typeName}\n{contextName}".ToLowerInvariant();
                if (haystack.IndexOf(normalizedQuery, StringComparison.Ordinal) >= 0)
                {
                    results.Add(new BindingMemberDescriptor(
                        member.Name,
                        pathTransform(path),
                        memberType,
                        memberReadable,
                        memberWritable,
                        false,
                        null));
                }

                bool childAncestorWritable = ancestorWritable &&
                                              memberReadable &&
                                              (!memberType.IsValueType || CanWrite(member));

                bool canRecurse = memberReadable &&
                                  depthRemaining > 1 &&
                                  ShouldRecurse(memberType) &&
                                  !nextAncestry.Contains(memberType);

                if (!canRecurse)
                {
                    continue;
                }

                SearchDescriptors(
                    memberType,
                    false,
                    path,
                    depthRemaining - 1,
                    nextAncestry,
                    memberReadable,
                    childAncestorWritable,
                    normalizedQuery,
                    contextName,
                    pathTransform,
                    maxResults,
                    results);
            }
        }

        private static string GetSearchRootKey(BindingInstanceHandle root)
        {
            if (!root.IsStatic && root.Instance is GameObject gameObject)
            {
                Component[] components = gameObject.GetComponents<Component>();
                string signature = string.Join(
                    ";",
                    components.Select(component => component == null
                        ? "missing"
                        : component.GetType().AssemblyQualifiedName));
                return $"GameObject|{gameObject.GetInstanceID()}|{signature}";
            }

            return $"{root.Type.AssemblyQualifiedName}|{root.IsStatic}";
        }

        private IReadOnlyList<BindingMemberDescriptor> GetGameObjectMembers(GameObject gameObject, int maxDepth)
        {
            Component[] components = gameObject.GetComponents<Component>();
            string componentSignature = string.Join(
                ";",
                components.Select(component => component == null
                    ? "missing"
                    : component.GetType().AssemblyQualifiedName));

            string cacheKey = $"GameObject|{gameObject.GetInstanceID()}|{componentSignature}|{maxDepth}";
            if (memberTreeCache.TryGetValue(cacheKey, out IReadOnlyList<BindingMemberDescriptor> cachedMembers))
            {
                return cachedMembers;
            }

            var roots = new List<BindingMemberDescriptor>();

            roots.Add(new BindingMemberDescriptor(
                "GameObject",
                "$gameObject",
                typeof(GameObject),
                false,
                false,
                true,
                () => BuildDescriptors(
                    typeof(GameObject),
                    false,
                    string.Empty,
                    maxDepth,
                    new HashSet<Type>(),
                    true,
                    true)));

            var typeOrdinals = new Dictionary<Type, int>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                Type componentType = component.GetType();
                typeOrdinals.TryGetValue(componentType, out int typeOrdinal);
                typeOrdinals[componentType] = typeOrdinal + 1;

                string groupName = typeOrdinal == 0
                    ? componentType.Name
                    : $"{componentType.Name} [{typeOrdinal + 1}]";
                Type capturedComponentType = componentType;
                int capturedTypeOrdinal = typeOrdinal;

                roots.Add(new BindingMemberDescriptor(
                    groupName,
                    $"$componentGroup|{capturedComponentType.AssemblyQualifiedName}|{capturedTypeOrdinal}",
                    capturedComponentType,
                    false,
                    false,
                    true,
                    () => BuildComponentDescriptors(capturedComponentType, capturedTypeOrdinal, maxDepth)));
            }

            memberTreeCache[cacheKey] = roots;
            return roots;
        }

        private IReadOnlyList<BindingMemberDescriptor> BuildComponentDescriptors(
            Type componentType,
            int typeOrdinal,
            int maxDepth)
        {
            IReadOnlyList<BindingMemberDescriptor> descriptors = BuildDescriptors(
                componentType,
                false,
                string.Empty,
                maxDepth,
                new HashSet<Type>(),
                true,
                true);

            return RewriteComponentPaths(descriptors, componentType, typeOrdinal);
        }

        private static IReadOnlyList<BindingMemberDescriptor> RewriteComponentPaths(
            IReadOnlyList<BindingMemberDescriptor> descriptors,
            Type componentType,
            int typeOrdinal)
        {
            var rewritten = new List<BindingMemberDescriptor>(descriptors.Count);

            for (int i = 0; i < descriptors.Count; i++)
            {
                BindingMemberDescriptor descriptor = descriptors[i];
                Func<IReadOnlyList<BindingMemberDescriptor>> childrenFactory = null;

                if (descriptor.CanExpand)
                {
                    childrenFactory = () => RewriteComponentPaths(
                        descriptor.Children,
                        componentType,
                        typeOrdinal);
                }

                rewritten.Add(new BindingMemberDescriptor(
                    descriptor.Name,
                    ComponentBindingPath.Create(componentType, typeOrdinal, descriptor.Path),
                    descriptor.ValueType,
                    descriptor.CanRead,
                    descriptor.CanWrite,
                    descriptor.CanExpand,
                    childrenFactory));
            }

            return rewritten;
        }

        public bool TryGetMetadata(
            BindingInstanceHandle root,
            string memberPath,
            out BindingMemberMetadata metadata,
            out string error)
        {
            metadata = default;

            BindingInstanceHandle resolvedRoot = root;
            if (!TryResolvePath(ref resolvedRoot, memberPath, out MemberInfo[] members, out error))
            {
                return false;
            }

            metadata = CreateMetadata(members);
            return true;
        }

        public bool TryGetMetadata(
            BindingEndpoint endpoint,
            out BindingMemberMetadata metadata,
            out string error)
        {
            metadata = default;

            if (!TryResolveEndpoint(
                    endpoint,
                    out BindingInstanceHandle root,
                    out MemberInfo[] members,
                    out EndpointResolutionEntry cacheEntry,
                    out error))
            {
                return false;
            }

            if (!cacheEntry.TryGetMetadata(out metadata))
            {
                metadata = CreateMetadata(members);
                cacheEntry.SetMetadata(metadata);
            }

            return true;
        }

        public bool TryRead(BindingEndpoint endpoint, out object value, out string error)
        {
            value = null;

            if (!TryResolveEndpoint(endpoint, out BindingInstanceHandle root, out MemberInfo[] members, out _, out error))
            {
                return false;
            }

            object current = root.Instance;

            try
            {
                for (int i = 0; i < members.Length; i++)
                {
                    current = GetValue(members[i], current);

                    if (IsNullValue(current) && i < members.Length - 1)
                    {
                        value = null;
                        error = string.Empty;
                        return true;
                    }
                }

                value = current;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Failed to read '{endpoint.MemberPath}': {GetInnermostMessage(exception)}";
                return false;
            }
        }

        public bool TryWrite(BindingEndpoint endpoint, object value, out string error)
        {
            if (!TryResolveEndpoint(endpoint, out BindingInstanceHandle root, out MemberInfo[] members, out _, out error))
            {
                return false;
            }

            if (!CanWrite(members[members.Length - 1]))
            {
                error = $"Member '{members[members.Length - 1].Name}' is not writable.";
                return false;
            }

            Type targetType = GetMemberType(members[members.Length - 1]);
            Type nullableUnderlyingType = Nullable.GetUnderlyingType(targetType);
            if (value != null &&
                value.GetType() != targetType &&
                value.GetType() != nullableUnderlyingType)
            {
                error = $"Value type '{value.GetType().FullName}' does not exactly match target type '{targetType.FullName}'.";
                return false;
            }

            if (value == null && targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
            {
                error = $"Cannot assign null to non-nullable value type '{targetType.FullName}'.";
                return false;
            }

            try
            {
                if (members.Length == 1)
                {
                    SetValue(members[0], root.Instance, value);
                    error = string.Empty;
                    return true;
                }

                var containers = new object[members.Length];
                containers[0] = root.Instance;

                for (int i = 0; i < members.Length - 1; i++)
                {
                    object next = GetValue(members[i], containers[i]);
                    if (next == null)
                    {
                        error = $"Member '{members[i].Name}' returned null before the target member could be reached.";
                        return false;
                    }

                    containers[i + 1] = next;
                }

                SetValue(members[members.Length - 1], containers[members.Length - 1], value);

                for (int i = members.Length - 2; i >= 0; i--)
                {
                    Type childType = GetMemberType(members[i]);
                    if (!childType.IsValueType)
                    {
                        break;
                    }

                    if (!CanWrite(members[i]))
                    {
                        error = $"Cannot write through value-type member '{members[i].Name}' because it is read-only.";
                        return false;
                    }

                    SetValue(members[i], containers[i], containers[i + 1]);
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Failed to write '{endpoint.MemberPath}': {GetInnermostMessage(exception)}";
                return false;
            }
        }

        private IReadOnlyList<BindingMemberDescriptor> BuildDescriptors(
            Type type,
            bool staticOnly,
            string parentPath,
            int depthRemaining,
            HashSet<Type> ancestry,
            bool ancestorReadable,
            bool ancestorWritable)
        {
            if (depthRemaining <= 0 || type == null || ancestry.Contains(type))
            {
                return Array.Empty<BindingMemberDescriptor>();
            }

            var nextAncestry = new HashSet<Type>(ancestry) { type };
            var descriptors = new List<BindingMemberDescriptor>();

            foreach (MemberInfo member in GetBindableMembers(type, staticOnly))
            {
                Type memberType = GetMemberType(member);
                bool memberReadable = ancestorReadable && CanRead(member);
                bool memberWritable = ancestorWritable && CanWrite(member);
                string path = string.IsNullOrEmpty(parentPath)
                    ? member.Name
                    : $"{parentPath}.{member.Name}";

                bool childAncestorWritable = ancestorWritable &&
                                              memberReadable &&
                                              (!memberType.IsValueType || CanWrite(member));

                bool canExpand = memberReadable &&
                                 depthRemaining > 1 &&
                                 ShouldRecurse(memberType) &&
                                 !nextAncestry.Contains(memberType);

                Func<IReadOnlyList<BindingMemberDescriptor>> childrenFactory = null;
                if (canExpand)
                {
                    Type capturedMemberType = memberType;
                    string capturedPath = path;
                    bool capturedReadable = memberReadable;
                    bool capturedWritable = childAncestorWritable;
                    int capturedDepth = depthRemaining - 1;
                    var capturedAncestry = new HashSet<Type>(nextAncestry);

                    childrenFactory = () => BuildDescriptors(
                        capturedMemberType,
                        false,
                        capturedPath,
                        capturedDepth,
                        capturedAncestry,
                        capturedReadable,
                        capturedWritable);
                }

                descriptors.Add(new BindingMemberDescriptor(
                    member.Name,
                    path,
                    memberType,
                    memberReadable,
                    memberWritable,
                    canExpand,
                    childrenFactory));
            }

            return descriptors;
        }

        public void Invalidate(BindingEndpoint endpoint)
        {
            if (endpoint != null)
            {
                endpointCache.Remove(endpoint);
            }
        }

        public BindingEndpointAvailability GetEndpointAvailability(
            BindingEndpoint endpoint,
            out string error)
        {
            if (endpoint == null || endpoint.Instance == null)
            {
                error = "The endpoint or its instance reference is null.";
                return BindingEndpointAvailability.InvalidConfiguration;
            }

            if (!endpoint.Instance.TryResolve(out BindingInstanceHandle root, out error))
            {
                return BindingEndpointAvailability.Missing;
            }

            if (!ComponentBindingPath.TryParse(
                    endpoint.MemberPath,
                    out string componentTypeName,
                    out int componentOrdinal,
                    out _))
            {
                error = string.Empty;
                return BindingEndpointAvailability.Available;
            }

            if (!(root.Instance is GameObject gameObject))
            {
                error = "A component member path requires a GameObject instance.";
                return BindingEndpointAvailability.InvalidConfiguration;
            }

            Type componentType = DefaultBindingInstanceResolver.FindType(componentTypeName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Component type '{componentTypeName}' could not be resolved.";
                return BindingEndpointAvailability.InvalidConfiguration;
            }

            if (!TryGetComponentByExactTypeAndOrdinal(
                    gameObject,
                    componentType,
                    componentOrdinal,
                    out _))
            {
                error = $"Component '{componentType.FullName}' at ordinal {componentOrdinal} was not found on GameObject '{gameObject.name}'.";
                return BindingEndpointAvailability.Missing;
            }

            error = string.Empty;
            return BindingEndpointAvailability.Available;
        }

        private bool TryResolveEndpoint(
            BindingEndpoint endpoint,
            out BindingInstanceHandle root,
            out MemberInfo[] members,
            out EndpointResolutionEntry cacheEntry,
            out string error)
        {
            root = default;
            members = null;
            cacheEntry = null;

            if (endpoint == null)
            {
                error = "The endpoint is null.";
                return false;
            }

            if (endpoint.Instance == null)
            {
                error = "The endpoint instance reference is null.";
                return false;
            }

            if (!endpoint.Instance.TryResolve(out BindingInstanceHandle initialRoot, out error))
            {
                return false;
            }

            EndpointResolutionCache cache = endpointCache.GetOrCreateValue(endpoint);
            if (cache.TryGet(initialRoot, endpoint.MemberPath, out cacheEntry))
            {
                root = cacheEntry.ResolvedRoot;
                members = cacheEntry.Members;
                error = string.Empty;
                return true;
            }

            root = initialRoot;
            if (!TryResolvePath(ref root, endpoint.MemberPath, out members, out error))
            {
                cache.Remove(initialRoot);
                return false;
            }

            cacheEntry = cache.Update(initialRoot, endpoint.MemberPath, root, members);
            return true;
        }

        private bool TryResolvePath(
            ref BindingInstanceHandle root,
            string memberPath,
            out MemberInfo[] members,
            out string error)
        {
            members = null;

            if (root.Type == null)
            {
                error = "The root type is invalid.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(memberPath))
            {
                error = "No member path is selected.";
                return false;
            }

            if (ComponentBindingPath.TryParse(
                    memberPath,
                    out string componentTypeName,
                    out int componentOrdinal,
                    out string componentMemberPath))
            {
                if (!(root.Instance is GameObject gameObject))
                {
                    error = "A component member path requires a GameObject instance.";
                    return false;
                }

                Type componentType = DefaultBindingInstanceResolver.FindType(componentTypeName);
                if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                {
                    error = $"Component type '{componentTypeName}' could not be resolved.";
                    return false;
                }

                if (!TryGetComponentByExactTypeAndOrdinal(
                        gameObject,
                        componentType,
                        componentOrdinal,
                        out Component component))
                {
                    error = $"Component '{componentType.FullName}' at ordinal {componentOrdinal} was not found on GameObject '{gameObject.name}'.";
                    return false;
                }
                root = new BindingInstanceHandle(component, componentType, false);
                memberPath = componentMemberPath;
            }

            string cacheKey = $"{root.Type.AssemblyQualifiedName}|{root.IsStatic}|{memberPath}";
            if (pathCache.TryGetValue(cacheKey, out members))
            {
                error = string.Empty;
                return true;
            }

            string[] segments = memberPath.Split('.');
            members = new MemberInfo[segments.Length];
            Type currentType = root.Type;

            for (int i = 0; i < segments.Length; i++)
            {
                bool staticOnly = i == 0 && root.IsStatic;
                MemberInfo member = FindBindableMember(currentType, segments[i], staticOnly);
                if (member == null)
                {
                    members = null;
                    error = $"Member '{segments[i]}' was not found on type '{currentType.FullName}'.";
                    return false;
                }

                members[i] = member;
                currentType = GetMemberType(member);
            }

            pathCache[cacheKey] = members;
            error = string.Empty;
            return true;
        }

        private bool TryGetComponentByExactTypeAndOrdinal(
            GameObject gameObject,
            Type componentType,
            int componentOrdinal,
            out Component component)
        {
            component = null;
            if (componentOrdinal < 0)
            {
                return false;
            }

            componentBuffer.Clear();
            gameObject.GetComponents(componentBuffer);

            int currentOrdinal = 0;
            for (int i = 0; i < componentBuffer.Count; i++)
            {
                Component candidate = componentBuffer[i];
                if (candidate == null || candidate.GetType() != componentType)
                {
                    continue;
                }

                if (currentOrdinal == componentOrdinal)
                {
                    component = candidate;
                    componentBuffer.Clear();
                    return true;
                }

                currentOrdinal++;
            }

            componentBuffer.Clear();
            return false;
        }

        private void CacheSearchResults(
            string cacheKey,
            IReadOnlyList<BindingMemberDescriptor> results)
        {
            if (searchCache.ContainsKey(cacheKey))
            {
                searchCache[cacheKey] = results;
                return;
            }

            while (searchCache.Count >= MaxSearchCacheEntries && searchCacheOrder.Count > 0)
            {
                searchCache.Remove(searchCacheOrder.Dequeue());
            }

            searchCache.Add(cacheKey, results);
            searchCacheOrder.Enqueue(cacheKey);
        }

        private static BindingMemberMetadata CreateMetadata(MemberInfo[] members)
        {
            bool canRead = true;
            bool canWrite = true;

            for (int i = 0; i < members.Length; i++)
            {
                MemberInfo member = members[i];
                bool memberCanRead = CanRead(member);
                bool memberCanWrite = CanWrite(member);

                canRead &= memberCanRead;

                if (i == members.Length - 1)
                {
                    canWrite &= memberCanWrite;
                }
                else
                {
                    canWrite &= memberCanRead;

                    if (GetMemberType(member).IsValueType)
                    {
                        canWrite &= memberCanWrite;
                    }
                }
            }

            return new BindingMemberMetadata(
                GetMemberType(members[members.Length - 1]),
                canRead,
                canWrite);
        }

        private IReadOnlyList<MemberInfo> GetBindableMembers(Type type, bool staticOnly)
        {
            string cacheKey = $"{type.AssemblyQualifiedName}|{staticOnly}";
            if (bindableMemberCache.TryGetValue(cacheKey, out IReadOnlyList<MemberInfo> cachedMembers))
            {
                return cachedMembers;
            }

            BindingFlags flags = BindingFlags.Public | BindingFlags.FlattenHierarchy |
                                 (staticOnly ? BindingFlags.Static : BindingFlags.Instance);

            var members = new List<MemberInfo>();
            members.AddRange(type.GetFields(flags).Where(field => !field.IsSpecialName));
            members.AddRange(type.GetProperties(flags).Where(property => property.GetIndexParameters().Length == 0));

            IReadOnlyList<MemberInfo> result = members
                .GroupBy(member => member.Name, StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(member => GetInheritanceDistance(type, member.DeclaringType))
                    .ThenBy(member => member is FieldInfo ? 0 : 1)
                    .First())
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .ToArray();

            bindableMemberCache[cacheKey] = result;
            return result;
        }

        private MemberInfo FindBindableMember(Type type, string name, bool staticOnly)
        {
            return GetBindableMembers(type, staticOnly)
                .FirstOrDefault(member => member.Name == name);
        }

        private static int GetInheritanceDistance(Type rootType, Type declaringType)
        {
            int distance = 0;
            Type current = rootType;

            while (current != null)
            {
                if (current == declaringType)
                {
                    return distance;
                }

                current = current.BaseType;
                distance++;
            }

            return int.MaxValue;
        }

        private static bool CanRead(MemberInfo member)
        {
            if (member is FieldInfo)
            {
                return true;
            }

            if (member is PropertyInfo property)
            {
                MethodInfo getter = property.GetGetMethod(false);
                return getter != null && getter.IsPublic;
            }

            return false;
        }

        private static bool CanWrite(MemberInfo member)
        {
            if (member is FieldInfo field)
            {
                return !field.IsInitOnly && !field.IsLiteral;
            }

            if (member is PropertyInfo property)
            {
                MethodInfo setter = property.GetSetMethod(false);
                return setter != null && setter.IsPublic;
            }

            return false;
        }

        private static Type GetMemberType(MemberInfo member)
        {
            if (member is FieldInfo field)
            {
                return field.FieldType;
            }

            if (member is PropertyInfo property)
            {
                return property.PropertyType;
            }

            throw new NotSupportedException($"Unsupported member type: {member.MemberType}.");
        }

        private static object GetValue(MemberInfo member, object instance)
        {
            if (member is FieldInfo field)
            {
                return field.GetValue(instance);
            }

            if (member is PropertyInfo property)
            {
                return property.GetValue(instance, null);
            }

            throw new NotSupportedException($"Unsupported member type: {member.MemberType}.");
        }

        private static void SetValue(MemberInfo member, object instance, object value)
        {
            if (member is FieldInfo field)
            {
                field.SetValue(instance, value);
                return;
            }

            if (member is PropertyInfo property)
            {
                property.SetValue(instance, value, null);
                return;
            }

            throw new NotSupportedException($"Unsupported member type: {member.MemberType}.");
        }

        private static bool ShouldRecurse(Type type)
        {
            if (type == null || type == typeof(string) || type.IsPrimitive || type.IsEnum)
            {
                return false;
            }

            if (type == typeof(decimal) ||
                type == typeof(DateTime) ||
                type == typeof(TimeSpan) ||
                type == typeof(Guid))
            {
                return false;
            }

            return true;
        }

        private sealed class EndpointResolutionCache
        {
            private readonly ConditionalWeakTable<object, EndpointResolutionEntry> instanceEntries =
                new ConditionalWeakTable<object, EndpointResolutionEntry>();
            private readonly Dictionary<Type, EndpointResolutionEntry> staticEntries =
                new Dictionary<Type, EndpointResolutionEntry>();

            public bool TryGet(
                BindingInstanceHandle root,
                string path,
                out EndpointResolutionEntry entry)
            {
                entry = null;

                if (root.IsStatic)
                {
                    if (!staticEntries.TryGetValue(root.Type, out entry))
                    {
                        return false;
                    }
                }
                else
                {
                    if (root.Instance == null || !instanceEntries.TryGetValue(root.Instance, out entry))
                    {
                        return false;
                    }
                }

                if (!entry.IsValid(root, path))
                {
                    Remove(root);
                    entry = null;
                    return false;
                }

                return true;
            }

            public EndpointResolutionEntry Update(
                BindingInstanceHandle initialRoot,
                string path,
                BindingInstanceHandle resolvedRoot,
                MemberInfo[] members)
            {
                var entry = new EndpointResolutionEntry(initialRoot, path, resolvedRoot, members);
                Remove(initialRoot);

                if (initialRoot.IsStatic)
                {
                    staticEntries[initialRoot.Type] = entry;
                }
                else if (initialRoot.Instance != null)
                {
                    instanceEntries.Add(initialRoot.Instance, entry);
                }

                return entry;
            }

            public void Remove(BindingInstanceHandle root)
            {
                if (root.IsStatic)
                {
                    if (root.Type != null)
                    {
                        staticEntries.Remove(root.Type);
                    }
                }
                else if (root.Instance != null)
                {
                    instanceEntries.Remove(root.Instance);
                }
            }
        }

        private sealed class EndpointResolutionEntry
        {
            private readonly Type initialType;
            private readonly bool initialIsStatic;
            private readonly string memberPath;
            private BindingMemberMetadata metadata;
            private bool hasMetadata;

            public EndpointResolutionEntry(
                BindingInstanceHandle initialRoot,
                string path,
                BindingInstanceHandle resolvedRoot,
                MemberInfo[] members)
            {
                initialType = initialRoot.Type;
                initialIsStatic = initialRoot.IsStatic;
                memberPath = path;
                ResolvedRoot = resolvedRoot;
                Members = members;
                metadata = CreateMetadata(members);
                hasMetadata = true;
            }

            public BindingInstanceHandle ResolvedRoot { get; }

            public MemberInfo[] Members { get; }

            public bool IsValid(BindingInstanceHandle root, string path)
            {
                if (Members == null || initialType != root.Type || initialIsStatic != root.IsStatic)
                {
                    return false;
                }

                if (!string.Equals(memberPath, path, StringComparison.Ordinal))
                {
                    return false;
                }

                return !(ResolvedRoot.Instance is UnityEngine.Object unityObject) || unityObject != null;
            }

            public bool TryGetMetadata(out BindingMemberMetadata value)
            {
                value = metadata;
                return hasMetadata;
            }

            public void SetMetadata(BindingMemberMetadata value)
            {
                metadata = value;
                hasMetadata = true;
            }
        }

        private static bool IsNullValue(object value)
        {
            if (value == null)
            {
                return true;
            }

            return value is UnityEngine.Object unityObject && unityObject == null;
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

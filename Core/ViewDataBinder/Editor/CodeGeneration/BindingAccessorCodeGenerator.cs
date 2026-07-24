using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using LegendaryTools.ViewBinding;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendaryTools.ViewBinding.Editor
{
    internal sealed class BindingAccessorCodeGenerator : IPreprocessBuildWithReport
    {
        private const string GeneratedDirectory = "Assets/Generated";
        private const string GeneratedPath =
            GeneratedDirectory + "/ViewDataBindingGeneratedAccessors.cs";

        public int callbackOrder => -1000;

        [MenuItem("Tools/Legendary Tools/View Data Binder/Generate Runtime Accessors")]
        public static void GenerateFromMenu()
        {
            bool changed = Generate();
            EditorUtility.DisplayDialog(
                "View Data Binder",
                changed
                    ? "Runtime accessors were generated. Unity will compile the generated file."
                    : "Runtime accessors are already up to date.",
                "OK");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (Generate())
            {
                throw new BuildFailedException(
                    "View Data Binder generated accessors changed. Wait for Unity to finish compiling, then build again.");
            }
        }

        private static bool Generate()
        {
            var accessors = new Dictionary<AccessorKey, AccessorDefinition>();
            var directPlans = new Dictionary<DirectPlanKey, DirectPlanDefinition>();
            CollectPrefabBindings(accessors, directPlans);
            CollectProfileBindings(accessors, directPlans);
            CollectBuildSceneBindings(accessors, directPlans);

            string generatedCode = BuildSource(accessors.Values, directPlans.Values);
            string currentCode = File.Exists(GeneratedPath)
                ? File.ReadAllText(GeneratedPath)
                : null;
            if (string.Equals(currentCode, generatedCode, StringComparison.Ordinal))
            {
                return false;
            }

            Directory.CreateDirectory(GeneratedDirectory);
            File.WriteAllText(GeneratedPath, generatedCode, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(GeneratedPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        private static void CollectPrefabBindings(
            Dictionary<AccessorKey, AccessorDefinition> accessors,
            Dictionary<DirectPlanKey, DirectPlanDefinition> directPlans)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    CollectFromGameObject(prefab, accessors, directPlans);
                }
            }
        }

        private static void CollectProfileBindings(
            Dictionary<AccessorKey, AccessorDefinition> accessors,
            Dictionary<DirectPlanKey, DirectPlanDefinition> directPlans)
        {
            string[] profileGuids = AssetDatabase.FindAssets("t:ViewDataBindingProfile");
            for (int i = 0; i < profileGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(profileGuids[i]);
                ViewDataBindingProfile profile =
                    AssetDatabase.LoadAssetAtPath<ViewDataBindingProfile>(path);
                if (profile == null)
                {
                    continue;
                }

                IReadOnlyList<ViewDataBinding> bindings = profile.Bindings;
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    CollectBinding(bindings[bindingIndex], accessors, directPlans);
                }
            }
        }

        private static void CollectBuildSceneBindings(
            Dictionary<AccessorKey, AccessorDefinition> accessors,
            Dictionary<DirectPlanKey, DirectPlanDefinition> directPlans)
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
                for (int i = 0; i < scenes.Length; i++)
                {
                    if (!scenes[i].enabled || string.IsNullOrEmpty(scenes[i].path))
                    {
                        continue;
                    }

                    Scene scene = SceneManager.GetSceneByPath(scenes[i].path);
                    bool wasLoaded = scene.IsValid() && scene.isLoaded;
                    if (!wasLoaded)
                    {
                        scene = EditorSceneManager.OpenScene(
                            scenes[i].path,
                            OpenSceneMode.Additive);
                    }

                    GameObject[] roots = scene.GetRootGameObjects();
                    for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                    {
                        CollectFromGameObject(roots[rootIndex], accessors, directPlans);
                    }

                    if (!wasLoaded)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
        }

        private static void CollectFromGameObject(
            GameObject root,
            Dictionary<AccessorKey, AccessorDefinition> accessors,
            Dictionary<DirectPlanKey, DirectPlanDefinition> directPlans)
        {
            ViewDataBinder[] binders = root.GetComponentsInChildren<ViewDataBinder>(true);
            for (int i = 0; i < binders.Length; i++)
            {
                IReadOnlyList<ViewDataBinding> bindings = binders[i].Bindings;
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    CollectBinding(bindings[bindingIndex], accessors, directPlans);
                }

                IReadOnlyList<ViewDataBindingProfileReference> profiles = binders[i].Profiles;
                for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
                {
                    ViewDataBindingProfile profile = profiles[profileIndex]?.Profile;
                    if (profile == null)
                    {
                        continue;
                    }

                    IReadOnlyList<ViewDataBinding> profileBindings = profile.Bindings;
                    for (int bindingIndex = 0; bindingIndex < profileBindings.Count; bindingIndex++)
                    {
                        CollectBinding(profileBindings[bindingIndex], accessors, directPlans);
                    }
                }
            }

            ViewDataEventBinder[] eventBinders =
                root.GetComponentsInChildren<ViewDataEventBinder>(true);
            for (int i = 0; i < eventBinders.Length; i++)
            {
                IReadOnlyList<ViewDataEventBinding> bindings = eventBinders[i].EventBindings;
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    IReadOnlyList<BindingSource> sources = bindings[bindingIndex]?.Sources;
                    if (sources == null)
                    {
                        continue;
                    }

                    for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                    {
                        CollectEndpoint(sources[sourceIndex]?.Endpoint, accessors);
                    }
                }
            }
        }

        private static void CollectBinding(
            ViewDataBinding binding,
            Dictionary<AccessorKey, AccessorDefinition> accessors,
            Dictionary<DirectPlanKey, DirectPlanDefinition> directPlans)
        {
            if (binding == null)
            {
                return;
            }

            IReadOnlyList<BindingSource> sources = binding.Sources;
            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    CollectEndpoint(sources[i]?.Endpoint, accessors);
                }
            }

            CollectEndpoint(binding.Target, accessors);
            CollectDirectPlan(binding, directPlans);
        }

        private static void CollectEndpoint(
            BindingEndpoint endpoint,
            Dictionary<AccessorKey, AccessorDefinition> accessors)
        {
            if (!TryCreateAccessorDefinition(endpoint, out AccessorDefinition definition))
            {
                return;
            }

            accessors[new AccessorKey(
                definition.RootType,
                definition.MemberPath,
                definition.IsStatic)] = definition;
        }

        private static void CollectDirectPlan(
            ViewDataBinding binding,
            Dictionary<DirectPlanKey, DirectPlanDefinition> directPlans)
        {
            if (binding.Direction != BindingSyncDirection.SourceToTarget ||
                binding.Sources == null ||
                binding.Sources.Count != 1 ||
                binding.Converter != null ||
                (binding.Formatter != null && binding.Formatter.Enabled) ||
                (binding.Fallback != null && binding.Fallback.Enabled) ||
                !TryCreateAccessorDefinition(
                    binding.Sources[0]?.Endpoint,
                    out AccessorDefinition source) ||
                !TryCreateAccessorDefinition(binding.Target, out AccessorDefinition target) ||
                !source.GenerateGetter ||
                !target.GenerateSetter ||
                source.ValueType != target.ValueType ||
                source.ValueType == null ||
                !source.ValueType.IsValueType ||
                Nullable.GetUnderlyingType(source.ValueType) != null)
            {
                return;
            }

            var definition = new DirectPlanDefinition(source, target);
            directPlans[new DirectPlanKey(source, target)] = definition;
        }

        private static bool TryCreateAccessorDefinition(
            BindingEndpoint endpoint,
            out AccessorDefinition definition)
        {
            definition = default;
            if (endpoint?.Instance == null ||
                string.IsNullOrWhiteSpace(endpoint.MemberPath) ||
                !TryResolveRootType(
                    endpoint.Instance,
                    out Type rootType,
                    out bool isStatic))
            {
                return false;
            }

            string memberPath = endpoint.MemberPath;
            if (ComponentBindingPath.TryParse(
                    memberPath,
                    out string componentTypeName,
                    out _,
                    out string componentMemberPath))
            {
                rootType = DefaultBindingInstanceResolver.FindType(componentTypeName);
                isStatic = false;
                memberPath = componentMemberPath;
            }

            if (!TryResolveMemberChain(
                    rootType,
                    memberPath,
                    isStatic,
                    out MemberInfo[] members,
                    out Type valueType,
                    out bool canRead,
                    out bool canWrite) ||
                !IsGeneratedTypeAccessible(rootType) ||
                !IsGeneratedTypeAccessible(valueType))
            {
                return false;
            }

            string rootTypeName = GetTypeName(rootType);
            string valueTypeName = GetTypeName(valueType);
            if (rootTypeName == null || valueTypeName == null)
            {
                return false;
            }

            definition = new AccessorDefinition(
                rootType,
                rootTypeName,
                valueType,
                valueTypeName,
                memberPath,
                isStatic,
                BuildAccessExpression(rootTypeName, members, isStatic),
                canRead,
                canWrite &&
                (isStatic || !rootType.IsValueType) &&
                CanGenerateDirectSetter(members));
            return true;
        }

        private static bool TryResolveRootType(
            BindingInstanceReference reference,
            out Type type,
            out bool isStatic)
        {
            isStatic = reference.Kind == BindingInstanceKind.StaticType;
            switch (reference.Kind)
            {
                case BindingInstanceKind.UnityObject:
                    type = reference.ObjectReference != null
                        ? reference.ObjectReference.GetType()
                        : null;
                    return type != null;

                case BindingInstanceKind.StaticType:
                    type = DefaultBindingInstanceResolver.FindType(reference.StaticTypeName);
                    return type != null;

                case BindingInstanceKind.Provider:
                    if (reference.ProviderReference is IBindingInstanceProvider provider)
                    {
                        try
                        {
                            type = provider.GetBindingInstanceType();
                            return type != null;
                        }
                        catch
                        {
                            type = null;
                            return false;
                        }
                    }

                    type = null;
                    return false;

                case BindingInstanceKind.Context:
                    type = DefaultBindingInstanceResolver.FindType(reference.ContextTypeName);
                    return type != null;

                default:
                    type = null;
                    return false;
            }
        }

        private static bool TryResolveMemberChain(
            Type rootType,
            string memberPath,
            bool isStatic,
            out MemberInfo[] members,
            out Type valueType,
            out bool canRead,
            out bool canWrite)
        {
            members = null;
            valueType = null;
            canRead = false;
            canWrite = false;
            if (rootType == null || string.IsNullOrWhiteSpace(memberPath))
            {
                return false;
            }

            string[] segments = memberPath.Split('.');
            members = new MemberInfo[segments.Length];
            Type currentType = rootType;
            canRead = true;

            for (int i = 0; i < segments.Length; i++)
            {
                BindingFlags flags = BindingFlags.Public | BindingFlags.FlattenHierarchy |
                                     (i == 0 && isStatic
                                         ? BindingFlags.Static
                                         : BindingFlags.Instance);
                MemberInfo member = (MemberInfo)currentType.GetField(segments[i], flags) ??
                                    currentType.GetProperty(segments[i], flags);
                if (member == null)
                {
                    return false;
                }

                if (member is PropertyInfo property)
                {
                    if (property.GetIndexParameters().Length != 0)
                    {
                        return false;
                    }

                    canRead &= property.GetGetMethod(false) != null;
                    currentType = property.PropertyType;
                }
                else
                {
                    currentType = ((FieldInfo)member).FieldType;
                }

                members[i] = member;
                if (i == segments.Length - 1)
                {
                    canWrite = member is FieldInfo field
                        ? !field.IsInitOnly && !field.IsLiteral
                        : ((PropertyInfo)member).GetSetMethod(false) != null;
                }
            }

            valueType = currentType;
            return true;
        }

        private static bool CanGenerateDirectSetter(MemberInfo[] members)
        {
            for (int i = 0; i < members.Length - 1; i++)
            {
                Type memberType = members[i] is FieldInfo field
                    ? field.FieldType
                    : ((PropertyInfo)members[i]).PropertyType;
                if (memberType.IsValueType)
                {
                    return false;
                }
            }

            return true;
        }

        private static string BuildAccessExpression(
            string rootTypeName,
            MemberInfo[] members,
            bool isStatic)
        {
            var builder = new StringBuilder();
            if (isStatic)
            {
                builder.Append(rootTypeName);
            }
            else
            {
                builder.Append("((");
                builder.Append(rootTypeName);
                builder.Append(")instance)");
            }

            for (int i = 0; i < members.Length; i++)
            {
                builder.Append('.');
                builder.Append(EscapeIdentifier(members[i].Name));
            }

            return builder.ToString();
        }

        private static string BuildSource(
            IEnumerable<AccessorDefinition> accessorDefinitions,
            IEnumerable<DirectPlanDefinition> directPlanDefinitions)
        {
            var accessors = new List<AccessorDefinition>(accessorDefinitions);
            accessors.Sort(AccessorDefinitionComparer.Instance);
            var directPlans = new List<DirectPlanDefinition>(directPlanDefinitions);
            directPlans.Sort(DirectPlanDefinitionComparer.Instance);

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("namespace LegendaryTools.ViewBinding.Generated");
            builder.AppendLine("{");
            builder.AppendLine("    internal static class ViewDataBindingGeneratedAccessors");
            builder.AppendLine("    {");
            builder.AppendLine("        [global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
            builder.AppendLine("        private static void Register()");
            builder.AppendLine("        {");

            for (int i = 0; i < accessors.Count; i++)
            {
                AppendAccessorRegistration(builder, accessors[i]);
            }

            for (int i = 0; i < directPlans.Count; i++)
            {
                AppendDirectPlanRegistration(builder, directPlans[i]);
            }

            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendAccessorRegistration(
            StringBuilder builder,
            AccessorDefinition definition)
        {
            builder.Append("            global::LegendaryTools.ViewBinding.BindingGeneratedAccessorRegistry.Register(typeof(");
            builder.Append(definition.RootTypeName);
            builder.Append("), \"");
            builder.Append(Escape(definition.MemberPath));
            builder.Append("\", ");
            builder.Append(definition.IsStatic ? "true" : "false");
            builder.Append(", ");
            builder.Append(definition.GenerateGetter
                ? "instance => " + definition.AccessExpression
                : "null");
            builder.Append(", ");
            if (definition.GenerateSetter)
            {
                builder.Append("(instance, value) => ");
                builder.Append(definition.AccessExpression);
                builder.Append(" = (");
                builder.Append(definition.ValueTypeName);
                builder.Append(")value");
            }
            else
            {
                builder.Append("null");
            }

            builder.AppendLine(");");
        }

        private static void AppendDirectPlanRegistration(
            StringBuilder builder,
            DirectPlanDefinition definition)
        {
            builder.Append("            global::LegendaryTools.ViewBinding.BindingGeneratedDirectPlanRegistry.Register<");
            builder.Append(definition.Source.ValueTypeName);
            builder.Append(">(typeof(");
            builder.Append(definition.Source.RootTypeName);
            builder.Append("), \"");
            builder.Append(Escape(definition.Source.MemberPath));
            builder.Append("\", ");
            builder.Append(definition.Source.IsStatic ? "true" : "false");
            builder.Append(", typeof(");
            builder.Append(definition.Target.RootTypeName);
            builder.Append("), \"");
            builder.Append(Escape(definition.Target.MemberPath));
            builder.Append("\", ");
            builder.Append(definition.Target.IsStatic ? "true" : "false");
            builder.Append(", instance => ");
            builder.Append(definition.Source.AccessExpression);
            builder.Append(", (instance, value) => ");
            builder.Append(definition.Target.AccessExpression);
            builder.AppendLine(" = value);");
        }

        private static bool IsGeneratedTypeAccessible(Type type)
        {
            if (type == null || !type.IsVisible)
            {
                return false;
            }

            if (type.IsArray)
            {
                return IsGeneratedTypeAccessible(type.GetElementType());
            }

            if (!type.IsGenericType)
            {
                return true;
            }

            Type[] arguments = type.GetGenericArguments();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (!IsGeneratedTypeAccessible(arguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetTypeName(Type type)
        {
            if (type == null || type.IsPointer || type.IsByRef || type.ContainsGenericParameters)
            {
                return null;
            }

            if (type.IsArray)
            {
                string elementName = GetTypeName(type.GetElementType());
                if (elementName == null)
                {
                    return null;
                }

                return elementName + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            }

            if (type.IsGenericType)
            {
                if (type.IsNested && type.DeclaringType != null && type.DeclaringType.IsGenericType)
                {
                    return null;
                }
                Type genericDefinition = type.GetGenericTypeDefinition();
                string definitionName = genericDefinition.FullName;
                if (string.IsNullOrEmpty(definitionName))
                {
                    return null;
                }

                int tickIndex = definitionName.IndexOf('`');
                if (tickIndex >= 0)
                {
                    definitionName = definitionName.Substring(0, tickIndex);
                }

                Type[] arguments = type.GetGenericArguments();
                var builder = new StringBuilder("global::");
                builder.Append(definitionName.Replace('+', '.'));
                builder.Append('<');
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    string argumentName = GetTypeName(arguments[i]);
                    if (argumentName == null)
                    {
                        return null;
                    }

                    builder.Append(argumentName);
                }

                builder.Append('>');
                return builder.ToString();
            }

            string fullName = type.FullName;
            return string.IsNullOrEmpty(fullName)
                ? null
                : "global::" + fullName.Replace('+', '.');
        }

        private static string EscapeIdentifier(string identifier)
        {
            switch (identifier)
            {
                case "abstract": case "as": case "base": case "bool": case "break":
                case "byte": case "case": case "catch": case "char": case "checked":
                case "class": case "const": case "continue": case "decimal": case "default":
                case "delegate": case "do": case "double": case "else": case "enum":
                case "event": case "explicit": case "extern": case "false": case "finally":
                case "fixed": case "float": case "for": case "foreach": case "goto":
                case "if": case "implicit": case "in": case "int": case "interface":
                case "internal": case "is": case "lock": case "long": case "namespace":
                case "new": case "null": case "object": case "operator": case "out":
                case "override": case "params": case "private": case "protected": case "public":
                case "readonly": case "ref": case "return": case "sbyte": case "sealed":
                case "short": case "sizeof": case "stackalloc": case "static": case "string":
                case "struct": case "switch": case "this": case "throw": case "true":
                case "try": case "typeof": case "uint": case "ulong": case "unchecked":
                case "unsafe": case "ushort": case "using": case "virtual": case "void":
                case "volatile": case "while":
                    return "@" + identifier;
                default:
                    return identifier;
            }
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private readonly struct AccessorKey : IEquatable<AccessorKey>
        {
            public AccessorKey(Type type, string path, bool isStatic)
            {
                Type = type;
                Path = path;
                IsStatic = isStatic;
            }

            private Type Type { get; }
            private string Path { get; }
            private bool IsStatic { get; }

            public bool Equals(AccessorKey other)
            {
                return Type == other.Type &&
                       IsStatic == other.IsStatic &&
                       string.Equals(Path, other.Path, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is AccessorKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = Type != null ? Type.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsStatic.GetHashCode();
                    hashCode = (hashCode * 397) ^
                               (Path != null ? StringComparer.Ordinal.GetHashCode(Path) : 0);
                    return hashCode;
                }
            }
        }

        private readonly struct DirectPlanKey : IEquatable<DirectPlanKey>
        {
            public DirectPlanKey(AccessorDefinition source, AccessorDefinition target)
            {
                Source = new AccessorKey(source.RootType, source.MemberPath, source.IsStatic);
                Target = new AccessorKey(target.RootType, target.MemberPath, target.IsStatic);
                ValueType = source.ValueType;
            }

            private AccessorKey Source { get; }
            private AccessorKey Target { get; }
            private Type ValueType { get; }

            public bool Equals(DirectPlanKey other)
            {
                return Source.Equals(other.Source) &&
                       Target.Equals(other.Target) &&
                       ValueType == other.ValueType;
            }

            public override bool Equals(object obj)
            {
                return obj is DirectPlanKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = Source.GetHashCode();
                    hashCode = (hashCode * 397) ^ Target.GetHashCode();
                    hashCode = (hashCode * 397) ^
                               (ValueType != null ? ValueType.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }

        private sealed class AccessorDefinitionComparer : IComparer<AccessorDefinition>
        {
            public static readonly AccessorDefinitionComparer Instance =
                new AccessorDefinitionComparer();

            public int Compare(AccessorDefinition left, AccessorDefinition right)
            {
                int result = string.CompareOrdinal(left.RootTypeName, right.RootTypeName);
                if (result != 0)
                {
                    return result;
                }

                result = string.CompareOrdinal(left.MemberPath, right.MemberPath);
                return result != 0 ? result : left.IsStatic.CompareTo(right.IsStatic);
            }
        }

        private sealed class DirectPlanDefinitionComparer : IComparer<DirectPlanDefinition>
        {
            public static readonly DirectPlanDefinitionComparer Instance =
                new DirectPlanDefinitionComparer();

            public int Compare(DirectPlanDefinition left, DirectPlanDefinition right)
            {
                int result = AccessorDefinitionComparer.Instance.Compare(
                    left.Source,
                    right.Source);
                return result != 0
                    ? result
                    : AccessorDefinitionComparer.Instance.Compare(left.Target, right.Target);
            }
        }

        private readonly struct AccessorDefinition
        {
            public AccessorDefinition(
                Type rootType,
                string rootTypeName,
                Type valueType,
                string valueTypeName,
                string memberPath,
                bool isStatic,
                string accessExpression,
                bool generateGetter,
                bool generateSetter)
            {
                RootType = rootType;
                RootTypeName = rootTypeName;
                ValueType = valueType;
                ValueTypeName = valueTypeName;
                MemberPath = memberPath;
                IsStatic = isStatic;
                AccessExpression = accessExpression;
                GenerateGetter = generateGetter;
                GenerateSetter = generateSetter;
            }

            public Type RootType { get; }
            public string RootTypeName { get; }
            public Type ValueType { get; }
            public string ValueTypeName { get; }
            public string MemberPath { get; }
            public bool IsStatic { get; }
            public string AccessExpression { get; }
            public bool GenerateGetter { get; }
            public bool GenerateSetter { get; }
        }

        private readonly struct DirectPlanDefinition
        {
            public DirectPlanDefinition(
                AccessorDefinition source,
                AccessorDefinition target)
            {
                Source = source;
                Target = target;
            }

            public AccessorDefinition Source { get; }
            public AccessorDefinition Target { get; }
        }
    }
}

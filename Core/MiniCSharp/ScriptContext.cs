using System;
using System.Collections.Generic;
using System.Reflection;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class ScriptContext
    {
        private readonly List<Dictionary<string, VariableSlot>> _scopes = new List<Dictionary<string, VariableSlot>>();
        private readonly Dictionary<string, Type> _registeredTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly Dictionary<string, TypeResolutionResult> _resolutionCache = new Dictionary<string, TypeResolutionResult>(StringComparer.Ordinal);

        public ScriptContext()
        {
            _scopes.Add(new Dictionary<string, VariableSlot>(StringComparer.Ordinal));
            AccessPolicy = new TypeAccessPolicy();

            RegisterBuiltInTypes();
        }

        public TypeAccessPolicy AccessPolicy { get; }

        public bool AutoResolveTypesFromAppDomain { get; set; } = true;

        public void PushScope()
        {
            _scopes.Add(new Dictionary<string, VariableSlot>(StringComparer.Ordinal));
        }

        public void PopScope()
        {
            if (_scopes.Count == 1)
            {
                throw new ScriptException("Cannot pop the global scope.");
            }

            _scopes.RemoveAt(_scopes.Count - 1);
        }

        public Type ResolveType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (_registeredTypes.TryGetValue(name, out Type registeredType))
            {
                AccessPolicy.ThrowIfDenied(registeredType, $"resolve registered type '{name}'");
                return registeredType;
            }

            if (!AutoResolveTypesFromAppDomain)
            {
                return null;
            }

            if (_resolutionCache.TryGetValue(name, out TypeResolutionResult cachedResult))
            {
                if (cachedResult.IsBlocked)
                {
                    AccessPolicy.ThrowIfDenied(cachedResult.Type, $"resolve type '{name}'");
                }

                return cachedResult.Type;
            }

            TypeResolutionResult result = ResolveTypeFromAppDomain(name);
            _resolutionCache[name] = result;

            if (result.IsBlocked)
            {
                AccessPolicy.ThrowIfDenied(result.Type, $"resolve type '{name}'");
            }

            return result.Type;
        }

        public void RegisterType(string alias, Type type)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                throw new ArgumentException("Type alias cannot be null or empty.", nameof(alias));
            }

            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            AccessPolicy.ThrowIfDenied(type, $"register type '{alias}'");

            _registeredTypes[alias] = type;

            RegisterTypeAlias(type.Name, type);

            if (!string.IsNullOrWhiteSpace(type.FullName))
            {
                RegisterTypeAlias(type.FullName, type);
            }
        }

        public void DefineVariable(string name, Type type, object value)
        {
            EnsureTypeAllowed(type, $"define variable '{name}'");

            if (value != null)
            {
                EnsureTypeAllowed(value.GetType(), $"define variable '{name}'");
            }

            Dictionary<string, VariableSlot> scope = _scopes[_scopes.Count - 1];

            if (scope.ContainsKey(name))
            {
                throw new ScriptException($"Variable '{name}' is already defined in this scope.");
            }

            scope[name] = new VariableSlot(type, RuntimeConversion.ConvertTo(value, type));
        }

        public void SetOrDefineGlobal(string name, Type type, object value)
        {
            EnsureTypeAllowed(type, $"set global variable '{name}'");

            if (value != null)
            {
                EnsureTypeAllowed(value.GetType(), $"set global variable '{name}'");
            }

            Dictionary<string, VariableSlot> globals = _scopes[0];

            if (globals.ContainsKey(name))
            {
                globals[name].Value = RuntimeConversion.ConvertTo(value, globals[name].Type);
            }
            else
            {
                globals[name] = new VariableSlot(type, RuntimeConversion.ConvertTo(value, type));
            }
        }

        public VariableSlot GetVariable(string name)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
            {
                if (_scopes[i].TryGetValue(name, out VariableSlot slot))
                {
                    EnsureTypeAllowed(slot.Type, $"read variable '{name}'");

                    if (slot.Value != null)
                    {
                        EnsureTypeAllowed(slot.Value.GetType(), $"read variable '{name}'");
                    }

                    return slot;
                }
            }

            throw new ScriptException($"Variable '{name}' is not defined.");
        }

        public void AssignVariable(string name, object value)
        {
            VariableSlot slot = GetVariable(name);

            EnsureTypeAllowed(slot.Type, $"assign variable '{name}'");

            if (value != null)
            {
                EnsureTypeAllowed(value.GetType(), $"assign variable '{name}'");
            }

            slot.Value = RuntimeConversion.ConvertTo(value, slot.Type);
        }

        public void EnsureTypeAllowed(Type type, string action)
        {
            AccessPolicy.ThrowIfDenied(type, action);
        }

        public void EnsureValueAllowed(object value, string action)
        {
            if (value == null)
            {
                return;
            }

            AccessPolicy.ThrowIfDenied(value.GetType(), action);
        }

        private TypeResolutionResult ResolveTypeFromAppDomain(string name)
        {
            Type directType = Type.GetType(name, throwOnError: false);

            if (directType != null)
            {
                return BuildResolutionResult(directType);
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < assemblies.Length; i++)
            {
                Type fullNameMatch = assemblies[i].GetType(name, throwOnError: false);

                if (fullNameMatch != null)
                {
                    return BuildResolutionResult(fullNameMatch);
                }
            }

            Type allowedSimpleNameMatch = null;
            Type blockedSimpleNameMatch = null;

            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types = GetLoadableTypes(assemblies[i]);

                for (int j = 0; j < types.Length; j++)
                {
                    Type type = types[j];

                    if (type == null || type.Name != name)
                    {
                        continue;
                    }

                    if (!AccessPolicy.IsAllowed(type))
                    {
                        blockedSimpleNameMatch = type;
                        continue;
                    }

                    if (allowedSimpleNameMatch != null && allowedSimpleNameMatch != type)
                    {
                        throw new ScriptException(
                            $"Type name '{name}' is ambiguous. Use the full type name or blacklist/register one of the conflicting types.");
                    }

                    allowedSimpleNameMatch = type;
                }
            }

            if (allowedSimpleNameMatch != null)
            {
                return TypeResolutionResult.Allowed(allowedSimpleNameMatch);
            }

            if (blockedSimpleNameMatch != null)
            {
                return TypeResolutionResult.Blocked(blockedSimpleNameMatch);
            }

            return TypeResolutionResult.NotFound();
        }

        private TypeResolutionResult BuildResolutionResult(Type type)
        {
            if (AccessPolicy.IsAllowed(type))
            {
                RegisterTypeAlias(type.Name, type);

                if (!string.IsNullOrWhiteSpace(type.FullName))
                {
                    RegisterTypeAlias(type.FullName, type);
                }

                return TypeResolutionResult.Allowed(type);
            }

            return TypeResolutionResult.Blocked(type);
        }

        private void RegisterBuiltInTypes()
        {
            RegisterType("object", typeof(object));
            RegisterType("string", typeof(string));
            RegisterType("bool", typeof(bool));
            RegisterType("byte", typeof(byte));
            RegisterType("short", typeof(short));
            RegisterType("int", typeof(int));
            RegisterType("long", typeof(long));
            RegisterType("float", typeof(float));
            RegisterType("double", typeof(double));
            RegisterType("decimal", typeof(decimal));
            RegisterType("void", typeof(void));
            RegisterType("List", typeof(List<>));
            RegisterType("Dictionary", typeof(Dictionary<,>));
        }

        private void RegisterTypeAlias(string alias, Type type)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            if (_registeredTypes.TryGetValue(alias, out Type existingType) && existingType != type)
            {
                return;
            }

            _registeredTypes[alias] = type;
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types;
            }
        }

        private readonly struct TypeResolutionResult
        {
            private TypeResolutionResult(Type type, bool isBlocked)
            {
                Type = type;
                IsBlocked = isBlocked;
            }

            public Type Type { get; }

            public bool IsBlocked { get; }

            public static TypeResolutionResult Allowed(Type type)
            {
                return new TypeResolutionResult(type, false);
            }

            public static TypeResolutionResult Blocked(Type type)
            {
                return new TypeResolutionResult(type, true);
            }

            public static TypeResolutionResult NotFound()
            {
                return new TypeResolutionResult(null, false);
            }
        }
    }
}

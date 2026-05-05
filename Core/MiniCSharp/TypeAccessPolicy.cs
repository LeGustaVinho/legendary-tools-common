using System;
using System.Collections.Generic;
using System.Reflection;

namespace LegendaryTools.MiniCSharp
{
    /// <summary>
    /// Defines an allow-by-default type access policy with blacklist rules.
    /// </summary>
    public sealed class TypeAccessPolicy
    {
        private readonly HashSet<Type> _blacklistedTypes = new HashSet<Type>();
        private readonly HashSet<string> _blacklistedTypeNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _blacklistedNamespaces = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _blacklistedAssemblies = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Func<Type, bool>> _customBlacklistRules = new List<Func<Type, bool>>();
        private readonly Dictionary<Type, bool> _allowedTypeCache = new Dictionary<Type, bool>();

        private bool _allowByDefault = true;

        /// <summary>
        /// Gets or sets whether types are allowed by default.
        /// Keep this true for blacklist mode.
        /// </summary>
        public bool AllowByDefault
        {
            get { return _allowByDefault; }
            set
            {
                if (_allowByDefault == value)
                {
                    return;
                }

                _allowByDefault = value;
                InvalidateAllowedTypeCache();
            }
        }

        /// <summary>
        /// Adds a type to the blacklist.
        /// </summary>
        public void BlacklistType<T>()
        {
            BlacklistType(typeof(T));
        }

        /// <summary>
        /// Adds a type to the blacklist.
        /// </summary>
        public void BlacklistType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            _blacklistedTypes.Add(NormalizeType(type));
            InvalidateAllowedTypeCache();
        }

        /// <summary>
        /// Adds a type name to the blacklist.
        /// Matches both Type.Name and Type.FullName.
        /// </summary>
        public void BlacklistTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new ArgumentException("Type name cannot be null or empty.", nameof(typeName));
            }

            _blacklistedTypeNames.Add(typeName);
            InvalidateAllowedTypeCache();
        }

        /// <summary>
        /// Adds a namespace prefix to the blacklist.
        /// Example: "System.IO" blocks System.IO and System.IO.*.
        /// </summary>
        public void BlacklistNamespace(string namespacePrefix)
        {
            if (string.IsNullOrWhiteSpace(namespacePrefix))
            {
                throw new ArgumentException("Namespace prefix cannot be null or empty.", nameof(namespacePrefix));
            }

            _blacklistedNamespaces.Add(namespacePrefix);
            InvalidateAllowedTypeCache();
        }

        /// <summary>
        /// Adds an assembly name to the blacklist.
        /// Use Assembly.GetName().Name, not the full display name.
        /// </summary>
        public void BlacklistAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                throw new ArgumentException("Assembly name cannot be null or empty.", nameof(assemblyName));
            }

            _blacklistedAssemblies.Add(assemblyName);
            InvalidateAllowedTypeCache();
        }

        /// <summary>
        /// Adds a custom blacklist rule.
        /// Return true to block the type.
        /// </summary>
        public void AddBlacklistRule(Func<Type, bool> rule)
        {
            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }

            _customBlacklistRules.Add(rule);
            InvalidateAllowedTypeCache();
        }

        /// <summary>
        /// Clears all blacklist rules.
        /// </summary>
        public void ClearBlacklist()
        {
            _blacklistedTypes.Clear();
            _blacklistedTypeNames.Clear();
            _blacklistedNamespaces.Clear();
            _blacklistedAssemblies.Clear();
            _customBlacklistRules.Clear();
            InvalidateAllowedTypeCache();
        }

        /// <summary>
        /// Clears cached type access decisions.
        /// Use this if a custom blacklist rule depends on external mutable state.
        /// </summary>
        public void ClearCache()
        {
            InvalidateAllowedTypeCache();
        }

        /// <summary>
        /// Returns true when a type can be accessed by the script.
        /// </summary>
        public bool IsAllowed(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (type == typeof(void))
            {
                return true;
            }

            type = NormalizeType(type);

            if (_allowedTypeCache.TryGetValue(type, out bool cachedResult))
            {
                return cachedResult;
            }

            bool result = CalculateIsAllowed(type);
            _allowedTypeCache[type] = result;

            return result;
        }

        /// <summary>
        /// Throws a ScriptException when a type is blocked.
        /// </summary>
        public void ThrowIfDenied(Type type, string action)
        {
            if (!IsAllowed(type))
            {
                string typeName = type == null ? "null" : type.FullName ?? type.Name;
                throw new ScriptException($"Access denied while trying to {action}. Type '{typeName}' is blacklisted.");
            }
        }

        private bool CalculateIsAllowed(Type type)
        {
            if (IsDirectlyBlacklisted(type))
            {
                return false;
            }

            Type declaringType = type.DeclaringType;

            while (declaringType != null)
            {
                if (IsDirectlyBlacklisted(declaringType))
                {
                    return false;
                }

                declaringType = declaringType.DeclaringType;
            }

            if (type.IsGenericType)
            {
                Type genericDefinition = type.GetGenericTypeDefinition();

                if (genericDefinition != type && IsDirectlyBlacklisted(genericDefinition))
                {
                    return false;
                }

                Type[] genericArguments = type.GetGenericArguments();

                for (int i = 0; i < genericArguments.Length; i++)
                {
                    if (!IsAllowed(genericArguments[i]))
                    {
                        return false;
                    }
                }
            }

            return _allowByDefault;
        }

        private bool IsDirectlyBlacklisted(Type type)
        {
            Type normalizedType = NormalizeType(type);

            if (_blacklistedTypes.Contains(normalizedType))
            {
                return true;
            }

            if (normalizedType.FullName != null && _blacklistedTypeNames.Contains(normalizedType.FullName))
            {
                return true;
            }

            if (_blacklistedTypeNames.Contains(normalizedType.Name))
            {
                return true;
            }

            Assembly assembly = normalizedType.Assembly;

            if (assembly != null)
            {
                string assemblyName = assembly.GetName().Name;

                if (_blacklistedAssemblies.Contains(assemblyName))
                {
                    return true;
                }
            }

            string typeNamespace = normalizedType.Namespace;

            if (!string.IsNullOrEmpty(typeNamespace))
            {
                foreach (string namespacePrefix in _blacklistedNamespaces)
                {
                    if (typeNamespace == namespacePrefix ||
                        typeNamespace.StartsWith(namespacePrefix + ".", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            for (int i = 0; i < _customBlacklistRules.Count; i++)
            {
                if (_customBlacklistRules[i](normalizedType))
                {
                    return true;
                }
            }

            return false;
        }

        private void InvalidateAllowedTypeCache()
        {
            _allowedTypeCache.Clear();
        }

        private static Type NormalizeType(Type type)
        {
            while (type.HasElementType)
            {
                type = type.GetElementType();
            }

            Type nullableType = Nullable.GetUnderlyingType(type);

            if (nullableType != null)
            {
                return nullableType;
            }

            return type;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Variable provider that resolves variables from an instance of a class
    /// using its public fields and properties.
    /// 
    /// Expected variable names in expressions use the '$' prefix, for example:
    ///   $hp, $mana, $speed
    /// 
    /// The prefix '$' is stripped and the remaining name is matched against
    /// public instance fields and properties of the provided object (case-insensitive).
    /// Types are validated and converted to T using the supplied INumberOperations.
    /// </summary>
    /// <typeparam name="T">Expression numeric/boolean type.</typeparam>
    public sealed class InstanceVariableProvider<T> : IVariableProvider<T>
    {
        private readonly object _instance;
        private readonly INumberOperations<T> _ops;
        private readonly Dictionary<string, Func<T>> _getters;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstanceVariableProvider{T}"/> class.
        /// </summary>
        /// <param name="instance">Object instance used as a variable source.</param>
        /// <param name="ops">Numeric/boolean operations helper to convert member values to T.</param>
        /// <param name="includeNonPublic">
        /// If true, also includes non-public instance fields and properties.
        /// By default, only public instance members are used.
        /// </param>
        public InstanceVariableProvider(object instance, INumberOperations<T> ops, bool includeNonPublic = false)
        {
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _ops = ops ?? throw new ArgumentNullException(nameof(ops));

            _getters = new Dictionary<string, Func<T>>(StringComparer.OrdinalIgnoreCase);

            BuildMemberMap(includeNonPublic);
        }

        /// <summary>
        /// Attempts to resolve a variable based on the instance's fields and properties.
        /// Variable names may start with '$', which will be stripped before lookup.
        /// </summary>
        public bool TryGetVariable(string name, out T value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            string normalized = NormalizeName(name);

            if (_getters.TryGetValue(normalized, out Func<T> getter))
            {
                value = getter();
                return true;
            }

            value = default!;
            return false;
        }

        /// <summary>
        /// Builds the internal map of variable-name -> value getter from
        /// the instance's fields and properties.
        /// </summary>
        private void BuildMemberMap(bool includeNonPublic)
        {
            Type type = _instance.GetType();

            BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public;

            if (includeNonPublic) flags |= BindingFlags.NonPublic;

            // Properties
            foreach (PropertyInfo prop in type.GetProperties(flags))
            {
                if (!prop.CanRead) continue;

                // Member name without '$' in front (expression name will be '$Name')
                string key = prop.Name;

                // Last registration wins if there is name collision between field and property.
                _getters[key] = CreatePropertyGetter(prop);
            }

            // Fields
            foreach (FieldInfo field in type.GetFields(flags))
            {
                // Member name without '$' in front
                string key = field.Name;

                // Only add if not already added by a property, to give properties precedence.
                if (_getters.ContainsKey(key)) continue;

                _getters[key] = CreateFieldGetter(field);
            }
        }

        /// <summary>
        /// Creates a getter delegate for a property, performing type validation and conversion.
        /// </summary>
        private Func<T> CreatePropertyGetter(PropertyInfo prop)
        {
            Type valueType = prop.PropertyType;

            return () =>
            {
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
                object? raw = prop.GetValue(_instance);
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

                if (raw == null)
                    throw new InvalidOperationException(
                        $"Property '{prop.Name}' on type '{_instance.GetType().Name}' returned null. " +
                        "Null values are not supported by this variable provider.");

                return ConvertValue(raw, valueType, prop.Name);
            };
        }

        /// <summary>
        /// Creates a getter delegate for a field, performing type validation and conversion.
        /// </summary>
        private Func<T> CreateFieldGetter(FieldInfo field)
        {
            Type valueType = field.FieldType;

            return () =>
            {
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
                object? raw = field.GetValue(_instance);
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

                if (raw == null)
                    throw new InvalidOperationException(
                        $"Field '{field.Name}' on type '{_instance.GetType().Name}' is null. " +
                        "Null values are not supported by this variable provider.");

                return ConvertValue(raw, valueType, field.Name);
            };
        }

        /// <summary>
        /// Converts a member value (field or property) to T using INumberOperations.
        /// Validates supported types and throws if conversion is not possible.
        /// </summary>
        private T ConvertValue(object raw, Type valueType, string memberName)
        {
            // If value is already of type T, just cast.
            if (raw is T alreadyTyped) return alreadyTyped;

            // If value is boolean, use FromBoolean.
            if (valueType == typeof(bool) || raw is bool)
            {
                bool b = (bool)raw;
                return _ops.FromBoolean(b);
            }

            // If value is convertible, convert to double and then to T.
            if (raw is IConvertible)
            {
                double d = Convert.ToDouble(raw);
                return _ops.FromDouble(d);
            }

            // Unsupported type.
            throw new InvalidOperationException(
                $"Member '{memberName}' on type '{_instance.GetType().Name}' has unsupported type '{valueType.FullName}' " +
                $"for InstanceVariableProvider<{typeof(T).Name}>.");
        }

        /// <summary>
        /// Normalizes a variable name by stripping a leading '$' if present.
        /// </summary>
        private static string NormalizeName(string name)
        {
            if (name.Length > 0 && name[0] == '$') return name.Substring(1);

            return name;
        }
    }
}
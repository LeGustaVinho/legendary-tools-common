using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Represents a named scope (e.g. "player", "enemy") with its own variables and providers.
    /// </summary>
    public sealed class ScopeBinding<T>
    {
        public string Name { get; }

        /// <summary>
        /// Local variables for this scope (keys include the '$' prefix, e.g. "$hp").
        /// </summary>
        public Dictionary<string, T> Variables { get; }

        /// <summary>
        /// Variable providers used when a variable is not found in Variables.
        /// Typical use: InstanceVariableProvider based on a C# object.
        /// </summary>
        public List<IVariableProvider<T>> VariableProviders { get; }

        /// <summary>
        /// Optional instance associated with this scope (for reflection-based providers, etc.).
        /// </summary>
        public object Instance { get; }

        public ScopeBinding(string name, object instance = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Instance = instance;
            Variables = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            VariableProviders = new List<IVariableProvider<T>>();
        }
    }
}
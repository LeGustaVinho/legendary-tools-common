using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Holds variables and functions for expression evaluation.
    /// Also supports chained external variable providers.
    /// </summary>
    public sealed class EvaluationContext<T>
    {
        /// <summary>
        /// Local variables dictionary. You can pre-populate or update it before evaluation.
        /// </summary>
        public IDictionary<string, T> Variables { get; }

        /// <summary>
        /// Functions dictionary. Functions take a list of arguments and return a value.
        /// </summary>
        public IDictionary<string, Func<IReadOnlyList<T>, T>> Functions { get; }

        /// <summary>
        /// Ordered list of external variable providers to query when a variable
        /// is not found in the local Variables dictionary.
        /// </summary>
        public IList<IVariableProvider<T>> VariableProviders { get; }

        public EvaluationContext()
        {
            Variables = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            Functions = new Dictionary<string, Func<IReadOnlyList<T>, T>>(StringComparer.OrdinalIgnoreCase);
            VariableProviders = new List<IVariableProvider<T>>();
        }

        /// <summary>
        /// Tries to resolve a variable from local storage or any registered provider.
        /// If a provider returns a value, it is cached in the local Variables dictionary.
        /// </summary>
        public bool TryResolveVariable(string name, out T value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            // First, try local dictionary.
            if (Variables.TryGetValue(name, out value)) return true;

            // Chain through providers.
            foreach (IVariableProvider<T> provider in VariableProviders)
            {
                if (provider == null) continue;

                if (provider.TryGetVariable(name, out value))
                {
                    // Optional: cache resolved value locally for faster subsequent lookups.
                    Variables[name] = value;
                    return true;
                }
            }

            value = default!;
            return false;
        }
    }
}
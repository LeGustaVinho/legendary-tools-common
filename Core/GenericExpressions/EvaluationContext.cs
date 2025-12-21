using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Holds variables, functions and scope information for expression evaluation.
    /// </summary>
    /// <typeparam name="T">Numeric/boolean type.</typeparam>
    public sealed class EvaluationContext<T>
    {
        /// <summary>
        /// Variables of the "self" scope.
        /// Keys include the '$' prefix, e.g. "$hp".
        /// </summary>
        public Dictionary<string, T> Variables { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Variable providers used when a variable is not found in the "self" Variables dictionary.
        /// </summary>
        public List<IVariableProvider<T>> VariableProviders { get; } =
            new();

        /// <summary>
        /// Functions available in this context.
        /// </summary>
        public Dictionary<string, Func<IReadOnlyList<T>, T>> Functions { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Name of the current "self" scope.
        /// By default this is "self", but you can change it to point to a specific logical scope
        /// (e.g. "player") if your relation providers are aware of that.
        /// </summary>
        public string CurrentScopeName { get; set; } = "self";

        /// <summary>
        /// Named scopes (e.g. "player", "enemy", "global").
        /// Each scope has its own variables and providers.
        /// </summary>
        public Dictionary<string, ScopeBinding<T>> Scopes { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Providers that know how to navigate between scopes (parent, owner, root, etc.).
        /// </summary>
        public List<IScopeRelationProvider<T>> ScopeRelationProviders { get; } =
            new();

        /// <summary>
        /// Resolves a variable in the "self" scope (no explicit scope path).
        /// </summary>
        internal bool TryResolveVariable(string variableName, out T value)
        {
            if (variableName == null) throw new ArgumentNullException(nameof(variableName));

            // 1. Local "self" variables
            if (Variables.TryGetValue(variableName, out value)) return true;

            // 2. Variable providers
            foreach (IVariableProvider<T> provider in VariableProviders)
            {
                if (provider.TryGetVariable(variableName, out value))
                {
                    // Cache in local variables for future lookup
                    Variables[variableName] = value;
                    return true;
                }
            }

            value = default!;
            return false;
        }

        /// <summary>
        /// Resolves a variable using a scope path and variable name.
        /// Example: path ["self", "parent", "owner"], variableName "$hp".
        /// </summary>
        internal bool TryResolveScopedVariable(
            IReadOnlyList<string> scopePath,
            string variableName,
            out T value)
        {
            if (variableName == null) throw new ArgumentNullException(nameof(variableName));

            // Determine starting scope name.
            string scopeName;
            int index = 0;

            if (scopePath == null || scopePath.Count == 0)
            {
                scopeName = CurrentScopeName;
            }
            else
            {
                string first = scopePath[0];

                if (string.Equals(first, "self", StringComparison.OrdinalIgnoreCase))
                {
                    scopeName = CurrentScopeName;
                    index = 1;
                }
                else if (string.Equals(first, "root", StringComparison.OrdinalIgnoreCase))
                {
                    scopeName = "root";
                    index = 1;
                }
                else
                {
                    // Named scope directly, e.g. "player"
                    scopeName = first;
                    index = 1;
                }
            }

            // Traverse relations: parent, owner, etc.
            for (; scopePath != null && index < scopePath.Count; index++)
            {
                string relation = scopePath[index];
                bool resolved = false;

                foreach (IScopeRelationProvider<T> provider in ScopeRelationProviders)
                {
                    if (provider.TryResolveRelatedScope(this, scopeName, relation, out string nextScope))
                    {
                        scopeName = nextScope;
                        resolved = true;
                        break;
                    }
                }

                if (!resolved)
                    // Fallback: treat the relation itself as a named scope.
                    // This allows simple paths like "player.$hp" without a relation provider.
                    scopeName = relation;
            }

            return TryResolveVariableInNamedScope(scopeName, variableName, out value);
        }

        /// <summary>
        /// Resolves a variable inside a specific named scope.
        /// If the scope is "self" (or null/empty), falls back to self resolution.
        /// </summary>
        private bool TryResolveVariableInNamedScope(string scopeName, string variableName, out T value)
        {
            if (string.IsNullOrWhiteSpace(scopeName) ||
                string.Equals(scopeName, "self", StringComparison.OrdinalIgnoreCase))
                // Self scope
                return TryResolveVariable(variableName, out value);

            if (Scopes.TryGetValue(scopeName, out ScopeBinding<T> binding))
            {
                // 1. Scope-local variables
                if (binding.Variables.TryGetValue(variableName, out value)) return true;

                // 2. Scope providers
                foreach (IVariableProvider<T> provider in binding.VariableProviders)
                {
                    if (provider.TryGetVariable(variableName, out value))
                    {
                        binding.Variables[variableName] = value;
                        return true;
                    }
                }
            }

            // As a last resort, try self
            return TryResolveVariable(variableName, out value);
        }
    }
}
using System;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Resolves related scopes based on a starting scope name and a relation name.
    /// Example relations: "parent", "owner", "root", etc.
    /// </summary>
    /// <typeparam name="T">Expression numeric/boolean type.</typeparam>
    public interface IScopeRelationProvider<T>
    {
        /// <summary>
        /// Tries to resolve a related scope name.
        /// For example: from "self" and relation "parent" produce "playerParent".
        /// </summary>
        /// <param name="context">Current evaluation context.</param>
        /// <param name="fromScopeName">Current scope name (can be "self").</param>
        /// <param name="relationName">Relation, e.g. "parent", "owner".</param>
        /// <param name="targetScopeName">Resolved target scope name.</param>
        /// <returns>True if relation was resolved, otherwise false.</returns>
        bool TryResolveRelatedScope(
            EvaluationContext<T> context,
            string fromScopeName,
            string relationName,
            out string targetScopeName);
    }
}
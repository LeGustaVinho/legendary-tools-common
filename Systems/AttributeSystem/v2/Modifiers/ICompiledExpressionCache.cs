using LegendaryTools.GenericExpressionEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Cache for compiled expressions to avoid reparsing the same condition strings.
    /// </summary>
    public interface ICompiledExpressionCache<T>
    {
        /// <summary>
        /// Returns a compiled expression for the given key/expression text.
        /// Implementations may compile and store on cache miss.
        /// </summary>
        CompiledExpression<T> GetOrCompile(ExpressionEngine<T> engine, string key, string expressionText);
    }
}
using System;
using System.Collections.Generic;
using LegendaryTools.GenericExpressionEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Simple dictionary-based compiled expression cache.
    /// </summary>
    public sealed class DictionaryCompiledExpressionCache<T> : ICompiledExpressionCache<T>
    {
        private readonly Dictionary<string, CompiledExpression<T>> _cache;

        public DictionaryCompiledExpressionCache(StringComparer comparer = null)
        {
            _cache = new Dictionary<string, CompiledExpression<T>>(comparer ?? StringComparer.Ordinal);
        }

        public CompiledExpression<T> GetOrCompile(ExpressionEngine<T> engine, string key, string expressionText)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (string.IsNullOrWhiteSpace(expressionText))
                throw new ArgumentException("Expression text is required.", nameof(expressionText));

            string finalKey = string.IsNullOrWhiteSpace(key) ? expressionText : key;

            if (_cache.TryGetValue(finalKey, out CompiledExpression<T> compiled) && compiled != null)
                return compiled;

            // Assumes your GenericExpressionEngine exposes Compile(string).
            compiled = engine.Compile(expressionText);
            _cache[finalKey] = compiled;
            return compiled;
        }
    }
}
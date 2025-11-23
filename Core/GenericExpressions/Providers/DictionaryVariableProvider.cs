using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Simple variable provider that wraps an external dictionary.
    /// Useful for sharing global variables across multiple contexts.
    /// </summary>
    public sealed class DictionaryVariableProvider<T> : IVariableProvider<T>
    {
        private readonly IDictionary<string, T> _source;

        public DictionaryVariableProvider(IDictionary<string, T> source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public bool TryGetVariable(string name, out T value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            return _source.TryGetValue(name, out value);
        }
    }
}
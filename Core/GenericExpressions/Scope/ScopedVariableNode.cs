using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Variable reference with explicit scope path, e.g.:
    /// - player.$hp          => scopePath ["player"], variableName "$hp"
    /// - self.parent.$hp     => scopePath ["self", "parent"], variableName "$hp"
    /// - root.owner.$hp      => scopePath ["root", "owner"], variableName "$hp"
    /// </summary>
    internal sealed class ScopedVariableNode<T> : ExpressionNode<T>
    {
        private readonly IReadOnlyList<string> _scopePath;
        private readonly string _variableName;

        public ScopedVariableNode(IReadOnlyList<string> scopePath, string variableName)
        {
            _scopePath = scopePath ?? throw new ArgumentNullException(nameof(scopePath));
            _variableName = variableName ?? throw new ArgumentNullException(nameof(variableName));
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!context.TryResolveScopedVariable(_scopePath, _variableName, out T value))
            {
                string path = string.Join(".", _scopePath);
                throw new KeyNotFoundException(
                    $"Variable '{_variableName}' was not found in scope path '{path}'.");
            }

            return value;
        }
    }
}
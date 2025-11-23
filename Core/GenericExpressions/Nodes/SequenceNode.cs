using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Sequence of expressions/statements separated by semicolons.
    /// Each statement is evaluated in order; the value of the last one is returned.
    /// </summary>
    internal sealed class SequenceNode<T> : ExpressionNode<T>
    {
        private readonly IReadOnlyList<ExpressionNode<T>> _statements;

        public SequenceNode(IReadOnlyList<ExpressionNode<T>> statements)
        {
            _statements = statements ?? throw new ArgumentNullException(nameof(statements));
            if (_statements.Count == 0)
                throw new ArgumentException("Sequence must contain at least one statement.", nameof(statements));
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            T last = ops.Zero;

            for (int i = 0; i < _statements.Count; i++)
            {
                last = _statements[i].Evaluate(context, ops);
            }

            return last;
        }
    }
}
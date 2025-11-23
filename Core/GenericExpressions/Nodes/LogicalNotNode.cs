using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Logical NOT operation (unary): not expr or !expr.
    /// </summary>
    internal sealed class LogicalNotNode<T> : ExpressionNode<T>
    {
        public ExpressionNode<T> Operand { get; }

        public LogicalNotNode(ExpressionNode<T> operand)
        {
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (ops == null) throw new ArgumentNullException(nameof(ops));

            T value = Operand.Evaluate(context, ops);
            bool b = ops.ToBoolean(value);
            return ops.FromBoolean(!b);
        }
    }
}
using System;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Boolean literal expression: true or false.
    /// Evaluates to a numeric representation via INumberOperations.FromBoolean.
    /// </summary>
    internal sealed class BooleanLiteralNode<T> : ExpressionNode<T>
    {
        private readonly bool _value;

        public BooleanLiteralNode(bool value)
        {
            _value = value;
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            if (ops == null) throw new ArgumentNullException(nameof(ops));

            return ops.FromBoolean(_value);
        }
    }
}
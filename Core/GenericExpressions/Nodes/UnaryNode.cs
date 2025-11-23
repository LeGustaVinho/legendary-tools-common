using System;

namespace LegendaryTools.GenericExpressionEngine
{
    internal sealed class UnaryNode<T> : ExpressionNode<T>
    {
        public UnaryOperator Operator { get; }
        public ExpressionNode<T> Operand { get; }

        public UnaryNode(UnaryOperator op, ExpressionNode<T> operand)
        {
            Operator = op;
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            T v = Operand.Evaluate(context, ops);

            return Operator switch
            {
                UnaryOperator.Plus => v,
                UnaryOperator.Minus => ops.Negate(v),
                _ => throw new InvalidOperationException($"Unsupported unary operator {Operator}.")
            };
        }
    }
}
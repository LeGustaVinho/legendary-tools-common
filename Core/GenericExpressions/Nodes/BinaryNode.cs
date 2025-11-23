using System;

namespace LegendaryTools.GenericExpressionEngine
{
    internal sealed class BinaryNode<T> : ExpressionNode<T>
    {
        public BinaryOperator Operator { get; }
        public ExpressionNode<T> Left { get; }
        public ExpressionNode<T> Right { get; }

        public BinaryNode(BinaryOperator op, ExpressionNode<T> left, ExpressionNode<T> right)
        {
            Operator = op;
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            T l = Left.Evaluate(context, ops);
            T r = Right.Evaluate(context, ops);

            return Operator switch
            {
                BinaryOperator.Add => ops.Add(l, r),
                BinaryOperator.Subtract => ops.Subtract(l, r),
                BinaryOperator.Multiply => ops.Multiply(l, r),
                BinaryOperator.Divide => ops.Divide(l, r),
                BinaryOperator.Power => ops.Power(l, r),
                _ => throw new InvalidOperationException($"Unsupported binary operator {Operator}.")
            };
        }
    }
}
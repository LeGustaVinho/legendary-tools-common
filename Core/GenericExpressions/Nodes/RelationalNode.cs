using System;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Relational operation: <, <=, >, >=, ==, !=.
    /// Result is a boolean stored in numeric form via INumberOperations.
    /// </summary>
    internal sealed class RelationalNode<T> : ExpressionNode<T>
    {
        public RelationalOperator Operator { get; }
        public ExpressionNode<T> Left { get; }
        public ExpressionNode<T> Right { get; }

        public RelationalNode(RelationalOperator op, ExpressionNode<T> left, ExpressionNode<T> right)
        {
            Operator = op;
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (ops == null) throw new ArgumentNullException(nameof(ops));

            T lVal = Left.Evaluate(context, ops);
            T rVal = Right.Evaluate(context, ops);

            double l = ops.ToDouble(lVal);
            double r = ops.ToDouble(rVal);

            bool result = Operator switch
            {
                RelationalOperator.Less => l < r,
                RelationalOperator.LessOrEqual => l <= r,
                RelationalOperator.Greater => l > r,
                RelationalOperator.GreaterOrEqual => l >= r,
                RelationalOperator.Equal => Math.Abs(l - r) < double.Epsilon,
                RelationalOperator.NotEqual => Math.Abs(l - r) >= double.Epsilon,
                _ => throw new InvalidOperationException($"Unsupported relational operator {Operator}.")
            };

            return ops.FromBoolean(result);
        }
    }
}
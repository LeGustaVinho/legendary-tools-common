using System;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Logical binary operation: and / or (including && / ||).
    /// Implements short-circuit evaluation.
    /// </summary>
    internal sealed class LogicalBinaryNode<T> : ExpressionNode<T>
    {
        public LogicalBinaryOperator Operator { get; }
        public ExpressionNode<T> Left { get; }
        public ExpressionNode<T> Right { get; }

        public LogicalBinaryNode(LogicalBinaryOperator op, ExpressionNode<T> left, ExpressionNode<T> right)
        {
            Operator = op;
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (ops == null) throw new ArgumentNullException(nameof(ops));

            T leftValue = Left.Evaluate(context, ops);
            bool leftBool = ops.ToBoolean(leftValue);

            switch (Operator)
            {
                case LogicalBinaryOperator.And:
                    if (!leftBool)
                        // Short-circuit: false && ... == false
                        return ops.FromBoolean(false);

                    T rightAnd = Right.Evaluate(context, ops);
                    bool rightBoolAnd = ops.ToBoolean(rightAnd);
                    return ops.FromBoolean(leftBool && rightBoolAnd);

                case LogicalBinaryOperator.Or:
                    if (leftBool)
                        // Short-circuit: true || ... == true
                        return ops.FromBoolean(true);

                    T rightOr = Right.Evaluate(context, ops);
                    bool rightBoolOr = ops.ToBoolean(rightOr);
                    return ops.FromBoolean(leftBool || rightBoolOr);

                default:
                    throw new InvalidOperationException($"Unsupported logical operator {Operator}.");
            }
        }
    }
}
using System;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Assignment expression: name = valueExpression.
    /// Evaluates the right-hand side, stores it in the context variables,
    /// and returns the assigned value.
    /// </summary>
    internal sealed class AssignmentNode<T> : ExpressionNode<T>
    {
        public string Name { get; }
        public ExpressionNode<T> ValueExpression { get; }

        public AssignmentNode(string name, ExpressionNode<T> valueExpression)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ValueExpression = valueExpression ?? throw new ArgumentNullException(nameof(valueExpression));
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Evaluate right-hand side
            T value = ValueExpression.Evaluate(context, ops);

            // Store in local variables (overrides provider values)
            context.Variables[Name] = value;

            // Return assigned value
            return value;
        }
    }
}
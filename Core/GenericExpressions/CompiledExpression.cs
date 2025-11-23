using System;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Represents a parsed expression that can be evaluated multiple times with different contexts.
    /// Also integrates with ExpressionEngine events for before/after evaluation notifications.
    /// </summary>
    public sealed class CompiledExpression<T>
    {
        private readonly ExpressionNode<T> _root;
        private readonly INumberOperations<T> _ops;
        private readonly ExpressionEngine<T> _engine;

        /// <summary>
        /// Original expression text used to create this compiled expression.
        /// Useful for logging, debugging and event handlers.
        /// </summary>
        public string ExpressionText { get; }

        internal CompiledExpression(
            ExpressionNode<T> root,
            INumberOperations<T> ops,
            ExpressionEngine<T> engine,
            string expressionText)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _ops = ops ?? throw new ArgumentNullException(nameof(ops));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            ExpressionText = expressionText ?? throw new ArgumentNullException(nameof(expressionText));
        }

        /// <summary>
        /// Evaluates the expression using the given context (variables and functions).
        /// Triggers engine-level events BeforeEvaluateExpression and AfterEvaluateExpression.
        /// </summary>
        public T Evaluate(EvaluationContext<T> context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Notify engine before evaluation
            _engine.RaiseBeforeEvaluate(ExpressionText, context);

            // Actual evaluation
            T result = _root.Evaluate(context, _ops);

            // Notify engine after evaluation
            _engine.RaiseAfterEvaluate(ExpressionText, context, result);

            return result;
        }
    }
}
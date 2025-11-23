using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    internal sealed class FunctionCallNode<T> : ExpressionNode<T>
    {
        public string Name { get; }
        public IReadOnlyList<ExpressionNode<T>> Arguments { get; }

        public FunctionCallNode(string name, IReadOnlyList<ExpressionNode<T>> arguments)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            if (!context.Functions.TryGetValue(Name, out Func<IReadOnlyList<T>, T> func))
                throw new KeyNotFoundException($"Function '{Name}' is not defined.");

            T[] evaluatedArgs = new T[Arguments.Count];
            for (int i = 0; i < Arguments.Count; i++)
            {
                evaluatedArgs[i] = Arguments[i].Evaluate(context, ops);
            }

            return func(evaluatedArgs);
        }
    }
}
using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    internal sealed class VariableNode<T> : ExpressionNode<T>
    {
        public string Name { get; }

        public VariableNode(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!context.TryResolveVariable(Name, out T value))
                throw new KeyNotFoundException($"Variable '{Name}' is not defined.");

            return value;
        }
    }
}
namespace LegendaryTools.GenericExpressionEngine
{
    internal sealed class ConstantNode<T> : ExpressionNode<T>
    {
        private readonly T _value;

        public ConstantNode(T value)
        {
            _value = value;
        }

        public override T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops)
        {
            return _value;
        }
    }
}
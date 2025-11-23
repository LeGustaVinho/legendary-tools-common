namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Base class for expression nodes.
    /// (Mantém igual à sua versão atual, não repito aqui.)
    /// </summary>
    internal abstract class ExpressionNode<T>
    {
        public abstract T Evaluate(EvaluationContext<T> context, INumberOperations<T> ops);
    }
}
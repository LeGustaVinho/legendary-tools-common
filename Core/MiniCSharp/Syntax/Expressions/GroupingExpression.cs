namespace LegendaryTools.MiniCSharp
{
    internal sealed class GroupingExpression : Expression
    {
        private readonly Expression _expression;

        public GroupingExpression(Expression expression)
        {
            _expression = expression;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            return _expression.Evaluate(context);
        }
    }
}
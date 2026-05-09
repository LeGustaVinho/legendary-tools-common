namespace LegendaryTools.MiniCSharp
{
    internal sealed class ExpressionStatement : Statement
    {
        private readonly Expression _expression;

        public ExpressionStatement(Expression expression)
        {
            _expression = expression;
        }

        public override void Execute(ScriptContext context)
        {
            _expression.Evaluate(context);
        }
    }
}
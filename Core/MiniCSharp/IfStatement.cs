namespace LegendaryTools.MiniCSharp
{
    internal sealed class IfStatement : Statement
    {
        private readonly Expression _condition;
        private readonly Statement _thenBranch;
        private readonly Statement _elseBranch;

        public IfStatement(Expression condition, Statement thenBranch, Statement elseBranch)
        {
            _condition = condition;
            _thenBranch = thenBranch;
            _elseBranch = elseBranch;
        }

        public override void Execute(ScriptContext context)
        {
            if (RuntimeConversion.ToBool(_condition.Evaluate(context).Value))
            {
                _thenBranch.Execute(context);
            }
            else
            {
                _elseBranch?.Execute(context);
            }
        }
    }
}
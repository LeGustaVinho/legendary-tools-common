namespace LegendaryTools.MiniCSharp
{
    internal sealed class LiteralExpression : Expression
    {
        private readonly object _value;

        public LiteralExpression(object value)
        {
            _value = value;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            return RuntimeValue.From(_value);
        }
    }
}
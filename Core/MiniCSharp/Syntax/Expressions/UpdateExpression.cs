namespace LegendaryTools.MiniCSharp
{
    internal sealed class UpdateExpression : Expression
    {
        private readonly IAssignableExpression _target;
        private readonly Token _operatorToken;
        private readonly bool _isPrefix;

        public UpdateExpression(IAssignableExpression target, Token operatorToken, bool isPrefix)
        {
            _target = target;
            _operatorToken = operatorToken;
            _isPrefix = isPrefix;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            RuntimeValue oldValue = _target.GetValue(context);
            object newValue = RuntimeOperators.IncrementOrDecrement(oldValue.Value, _operatorToken.Type == TokenType.PlusPlus);

            _target.Assign(context, newValue);

            RuntimeValue assignedValue = _target.GetValue(context);
            return _isPrefix ? assignedValue : oldValue;
        }
    }
}
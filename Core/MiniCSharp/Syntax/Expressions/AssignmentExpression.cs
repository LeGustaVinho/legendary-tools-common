namespace LegendaryTools.MiniCSharp
{
    internal sealed class AssignmentExpression : Expression
    {
        private readonly IAssignableExpression _target;
        private readonly Expression _value;

        public AssignmentExpression(IAssignableExpression target, Expression value)
        {
            _target = target;
            _value = value;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            RuntimeValue newValue = _value.Evaluate(context);
            _target.Assign(context, newValue.Value);
            return _target.GetValue(context);
        }
    }
}
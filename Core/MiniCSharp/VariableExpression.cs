namespace LegendaryTools.MiniCSharp
{
    internal sealed class VariableExpression : Expression, IAssignableExpression
    {
        private readonly Token _name;

        public VariableExpression(Token name)
        {
            _name = name;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            return GetValue(context);
        }

        public RuntimeValue GetValue(ScriptContext context)
        {
            VariableSlot slot = context.GetVariable(_name.Lexeme);

            context.EnsureTypeAllowed(slot.Type, $"read variable '{_name.Lexeme}'");
            context.EnsureValueAllowed(slot.Value, $"read variable '{_name.Lexeme}'");

            return new RuntimeValue(slot.Value, slot.Type);
        }

        public void Assign(ScriptContext context, object value)
        {
            context.EnsureValueAllowed(value, $"assign variable '{_name.Lexeme}'");
            context.AssignVariable(_name.Lexeme, value);
        }
    }
}

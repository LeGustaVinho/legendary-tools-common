using System;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class TypeExpression : Expression
    {
        private readonly Token _name;
        private readonly Type _type;

        public TypeExpression(Token name, Type type)
        {
            _name = name;
            _type = type;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            context.EnsureTypeAllowed(_type, $"access type '{_type.FullName ?? _type.Name}'");

            return new RuntimeValue(_type, typeof(Type));
        }

        public override string ToString()
        {
            return _name.Lexeme;
        }
    }
}

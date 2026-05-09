using System;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class VariableDeclarationStatement : Statement
    {
        private readonly Token _name;
        private readonly Type _declaredType;
        private readonly bool _inferType;
        private readonly Expression _initializer;

        public VariableDeclarationStatement(Token name, Type declaredType, bool inferType, Expression initializer)
        {
            _name = name;
            _declaredType = declaredType;
            _inferType = inferType;
            _initializer = initializer;
        }

        public override void Execute(ScriptContext context)
        {
            object value;
            Type type;

            if (_initializer != null)
            {
                RuntimeValue initial = _initializer.Evaluate(context);
                type = _inferType ? initial.Type : _declaredType;
                value = RuntimeConversion.ConvertTo(initial.Value, type);
            }
            else
            {
                type = _declaredType;
                value = RuntimeConversion.DefaultValue(type);
            }

            context.DefineVariable(_name.Lexeme, type, value);
        }
    }
}
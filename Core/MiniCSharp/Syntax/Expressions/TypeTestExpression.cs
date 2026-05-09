using System;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class CastExpression : Expression
    {
        private readonly Type _targetType;
        private readonly Expression _expression;

        public CastExpression(Type targetType, Expression expression)
        {
            _targetType = targetType;
            _expression = expression;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            RuntimeValue value = _expression.Evaluate(context);
            object converted = RuntimeConversion.ConvertTo(value.Value, _targetType);
            return new RuntimeValue(converted, _targetType);
        }
    }

    internal sealed class IsExpression : Expression
    {
        private readonly Expression _expression;
        private readonly Type _targetType;

        public IsExpression(Expression expression, Type targetType)
        {
            _expression = expression;
            _targetType = targetType;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            RuntimeValue value = _expression.Evaluate(context);
            return RuntimeValue.From(RuntimeConversion.IsCompatibleWithType(value.Value, _targetType));
        }
    }

    internal sealed class AsExpression : Expression
    {
        private readonly Expression _expression;
        private readonly Type _targetType;

        public AsExpression(Expression expression, Type targetType)
        {
            _expression = expression;
            _targetType = targetType;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            RuntimeValue value = _expression.Evaluate(context);

            if (RuntimeConversion.TryConvertWithAsSemantics(value.Value, _targetType, out object converted))
            {
                return new RuntimeValue(converted, _targetType);
            }

            return new RuntimeValue(null, _targetType);
        }
    }
}

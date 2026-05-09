using System;
using System.Reflection;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class NullCoalescingExpression : Expression
    {
        private readonly Expression _left;
        private readonly Expression _right;

        public NullCoalescingExpression(Expression left, Expression right)
        {
            _left = left;
            _right = right;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            RuntimeValue leftValue = _left.Evaluate(context);

            if (leftValue.Value != null)
            {
                return leftValue;
            }

            return _right.Evaluate(context);
        }
    }

    internal sealed class NullConditionalMemberExpression : Expression
    {
        private readonly Expression _target;
        private readonly string _memberName;

        public NullConditionalMemberExpression(Expression target, Token memberName)
        {
            _target = target;
            _memberName = memberName.Lexeme;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            RuntimeValue targetValue = _target.Evaluate(context);

            if (targetValue.Value == null)
            {
                return RuntimeValue.From(null);
            }

            Type targetType = targetValue.Value.GetType();
            context.EnsureTypeAllowed(targetType, $"access instance members of '{targetType.FullName ?? targetType.Name}'");

            FieldInfo field = ReflectionMembers.GetField(targetType, _memberName, isStatic: false);

            if (field != null)
            {
                object fieldValue = field.GetValue(targetValue.Value);
                context.EnsureValueAllowed(fieldValue, $"read field '{_memberName}'");
                return new RuntimeValue(fieldValue, field.FieldType);
            }

            PropertyInfo property = ReflectionMembers.GetProperty(targetType, _memberName, isStatic: false);

            if (property != null && property.CanRead)
            {
                object propertyValue = property.GetValue(targetValue.Value, null);
                context.EnsureValueAllowed(propertyValue, $"read property '{_memberName}'");
                return new RuntimeValue(propertyValue, property.PropertyType);
            }

            throw new ScriptException(
                $"Readable public instance field or property '{_memberName}' was not found on '{targetType.Name}'.");
        }
    }

    internal sealed class NullConditionalCallExpression : Expression
    {
        private readonly Expression _target;
        private readonly string _methodName;
        private readonly System.Collections.Generic.List<Expression> _arguments;
        private readonly object[] _argumentValues;

        public NullConditionalCallExpression(Expression target, Token methodName, System.Collections.Generic.List<Expression> arguments)
        {
            _target = target;
            _methodName = methodName.Lexeme;
            _arguments = arguments;
            _argumentValues = new object[arguments.Count];
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            RuntimeValue targetValue = _target.Evaluate(context);

            if (targetValue.Value == null)
            {
                return RuntimeValue.From(null);
            }

            Type targetType = targetValue.Value.GetType();
            context.EnsureTypeAllowed(targetType, $"access instance members of '{targetType.FullName ?? targetType.Name}'");

            for (int i = 0; i < _arguments.Count; i++)
            {
                RuntimeValue argument = _arguments[i].Evaluate(context);
                _argumentValues[i] = argument.Value;
            }

            return ReflectionMembers.InvokeBestMethod(
                targetType,
                targetValue.Value,
                _methodName,
                _argumentValues,
                isStatic: false,
                context.AccessPolicy);
        }
    }

    internal sealed class NullConditionalIndexExpression : Expression
    {
        private readonly Expression _target;
        private readonly Expression _index;

        public NullConditionalIndexExpression(Expression target, Expression index)
        {
            _target = target;
            _index = index;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            RuntimeValue targetValue = _target.Evaluate(context);

            if (targetValue.Value == null)
            {
                return RuntimeValue.From(null);
            }

            return new IndexExpression(new LiteralExpression(targetValue.Value), _index).Evaluate(context);
        }
    }
}

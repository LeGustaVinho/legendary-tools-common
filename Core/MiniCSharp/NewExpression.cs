using System;
using System.Collections.Generic;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class NewExpression : Expression
    {
        private readonly string _typeName;
        private readonly Type _type;
        private readonly List<Expression> _arguments;
        private readonly Expression _arrayLength;
        private readonly object[] _argumentValues;

        public NewExpression(string typeName, Type type, List<Expression> arguments)
        {
            _typeName = typeName;
            _type = type;
            _arguments = arguments;
            _arrayLength = null;
            _argumentValues = new object[arguments.Count];
        }

        public NewExpression(string typeName, Type type, Expression arrayLength)
        {
            _typeName = typeName;
            _type = type;
            _arguments = null;
            _arrayLength = arrayLength;
            _argumentValues = System.Array.Empty<object>();
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            context.EnsureTypeAllowed(_type, $"create instance of '{_typeName}'");

            if (_arrayLength != null)
            {
                RuntimeValue lengthValue = _arrayLength.Evaluate(context);
                int length = (int)RuntimeConversion.ConvertTo(lengthValue.Value, typeof(int));

                if (length < 0)
                {
                    throw new ScriptException("Array length cannot be negative.");
                }

                object array = System.Array.CreateInstance(_type, length);
                context.EnsureValueAllowed(array, $"create instance of '{_typeName}[]'");
                return new RuntimeValue(array, _type.MakeArrayType());
            }

            object[] arguments = EvaluateArguments(context);
            RuntimeValue result = ReflectionMembers.CreateInstance(_type, _typeName, arguments, context.AccessPolicy);

            context.EnsureValueAllowed(result.Value, $"create instance of '{_typeName}'");

            return result;
        }

        private object[] EvaluateArguments(ScriptContext context)
        {
            for (int i = 0; i < _arguments.Count; i++)
            {
                RuntimeValue argument = _arguments[i].Evaluate(context);

                context.EnsureValueAllowed(argument.Value, $"evaluate constructor argument {i}");

                _argumentValues[i] = argument.Value;
            }

            return _argumentValues;
        }
    }
}

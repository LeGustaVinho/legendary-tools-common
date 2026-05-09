using System;
using System.Collections.Generic;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class ArrayLiteralExpression : Expression
    {
        private readonly List<Expression> _elements;

        public ArrayLiteralExpression(List<Expression> elements)
        {
            _elements = elements;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            object[] values = new object[_elements.Count];
            Type elementType = null;

            for (int i = 0; i < _elements.Count; i++)
            {
                RuntimeValue value = _elements[i].Evaluate(context);
                values[i] = value.Value;
                elementType = GetCommonElementType(elementType, value.Type, value.Value);
            }

            elementType ??= typeof(object);
            Array array = Array.CreateInstance(elementType, values.Length);

            for (int i = 0; i < values.Length; i++)
            {
                array.SetValue(RuntimeConversion.ConvertTo(values[i], elementType), i);
            }

            return new RuntimeValue(array, elementType.MakeArrayType());
        }

        private static Type GetCommonElementType(Type currentType, Type nextType, object nextValue)
        {
            if (nextValue == null)
            {
                return currentType;
            }

            Type resolvedNextType = nextType ?? nextValue.GetType();

            if (currentType == null)
            {
                return resolvedNextType;
            }

            if (currentType == resolvedNextType)
            {
                return currentType;
            }

            if (currentType.IsAssignableFrom(resolvedNextType))
            {
                return currentType;
            }

            if (resolvedNextType.IsAssignableFrom(currentType))
            {
                return resolvedNextType;
            }

            return typeof(object);
        }
    }
}

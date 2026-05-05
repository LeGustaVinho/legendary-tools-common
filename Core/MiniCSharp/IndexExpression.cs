using System;
using System.Collections;
using System.Reflection;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class IndexExpression : Expression, IAssignableExpression
    {
        private readonly Expression _target;
        private readonly Expression _index;

        public IndexExpression(Expression target, Expression index)
        {
            _target = target;
            _index = index;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            return GetValue(context);
        }

        public RuntimeValue GetValue(ScriptContext context)
        {
            object targetObject = EvaluateTarget(context, out Type targetType);
            object indexValue = EvaluateIndex(context);

            if (targetObject is Array array)
            {
                int index = ConvertIndex(indexValue);
                object value;

                try
                {
                    value = array.GetValue(index);
                }
                catch (IndexOutOfRangeException)
                {
                    throw new ScriptException($"Array index {index} is out of range.");
                }

                context.EnsureValueAllowed(value, $"read index {index}");
                return new RuntimeValue(value, targetType.GetElementType() ?? typeof(object));
            }

            if (targetObject is IList list)
            {
                int index = ConvertIndex(indexValue);
                object value;

                try
                {
                    value = list[index];
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new ScriptException($"Index {index} is out of range.");
                }

                context.EnsureValueAllowed(value, $"read index {index}");
                return new RuntimeValue(value, ResolveIndexerValueType(targetType));
            }

            PropertyInfo indexer = ReflectionMembers.GetIndexer(targetType, context.AccessPolicy);

            if (indexer != null && indexer.CanRead)
            {
                object convertedIndex = RuntimeConversion.ConvertTo(indexValue, indexer.GetIndexParameters()[0].ParameterType);
                object value = indexer.GetValue(targetObject, new[] { convertedIndex });

                context.EnsureValueAllowed(value, $"read indexer on '{targetType.Name}'");
                return new RuntimeValue(value, indexer.PropertyType);
            }

            throw new ScriptException($"Type '{targetType.Name}' does not support index access.");
        }

        public void Assign(ScriptContext context, object value)
        {
            object targetObject = EvaluateTarget(context, out Type targetType);
            object indexValue = EvaluateIndex(context);

            context.EnsureValueAllowed(value, "assign indexed value");

            if (targetObject is Array array)
            {
                int index = ConvertIndex(indexValue);

                try
                {
                    array.SetValue(RuntimeConversion.ConvertTo(value, targetType.GetElementType()), index);
                    return;
                }
                catch (IndexOutOfRangeException)
                {
                    throw new ScriptException($"Array index {index} is out of range.");
                }
            }

            if (targetObject is IList list)
            {
                int index = ConvertIndex(indexValue);
                Type valueType = ResolveIndexerValueType(targetType);

                try
                {
                    list[index] = RuntimeConversion.ConvertTo(value, valueType);
                    return;
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new ScriptException($"Index {index} is out of range.");
                }
            }

            PropertyInfo indexer = ReflectionMembers.GetIndexer(targetType, context.AccessPolicy);

            if (indexer != null && indexer.CanWrite)
            {
                object convertedIndex = RuntimeConversion.ConvertTo(indexValue, indexer.GetIndexParameters()[0].ParameterType);
                object convertedValue = RuntimeConversion.ConvertTo(value, indexer.PropertyType);
                indexer.SetValue(targetObject, convertedValue, new[] { convertedIndex });
                return;
            }

            throw new ScriptException($"Type '{targetType.Name}' does not support indexed assignment.");
        }

        private object EvaluateTarget(ScriptContext context, out Type targetType)
        {
            RuntimeValue targetValue = _target.Evaluate(context);

            if (targetValue.Value == null)
            {
                throw new ScriptException("Cannot index into null.");
            }

            targetType = targetValue.Value.GetType();
            context.EnsureTypeAllowed(targetType, $"access indexed value on '{targetType.FullName ?? targetType.Name}'");

            return targetValue.Value;
        }

        private object EvaluateIndex(ScriptContext context)
        {
            RuntimeValue indexValue = _index.Evaluate(context);
            context.EnsureValueAllowed(indexValue.Value, "evaluate index");
            return indexValue.Value;
        }

        private static int ConvertIndex(object indexValue)
        {
            return (int)RuntimeConversion.ConvertTo(indexValue, typeof(int));
        }

        private static Type ResolveIndexerValueType(Type targetType)
        {
            if (targetType.IsArray)
            {
                return targetType.GetElementType() ?? typeof(object);
            }

            if (targetType.IsGenericType)
            {
                Type[] genericArguments = targetType.GetGenericArguments();

                if (genericArguments.Length == 1)
                {
                    return genericArguments[0];
                }
            }

            return typeof(object);
        }
    }
}

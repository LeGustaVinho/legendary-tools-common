using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace LegendaryTools.MiniCSharp
{
    internal static class RuntimeConversion
    {
        public static object DefaultValue(Type type)
        {
            if (type == null || type == typeof(object))
            {
                return null;
            }

            if (type == typeof(void))
            {
                throw new ScriptException("Cannot create a value of type 'void'.");
            }

            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        public static bool ToBool(object value)
        {
            if (value is bool boolValue)
            {
                return boolValue;
            }

            throw new ScriptException($"Expected bool value, got '{value?.GetType().Name ?? "null"}'.");
        }

        public static bool CanConvertTo(object value, Type targetType, out int score)
        {
            score = int.MaxValue;
            targetType = targetType ?? typeof(object);

            if (targetType == typeof(void))
            {
                return false;
            }

            if (targetType == typeof(object))
            {
                score = value == null ? 1 : 2;
                return true;
            }

            Type nullableType = Nullable.GetUnderlyingType(targetType);
            Type effectiveTargetType = nullableType ?? targetType;

            if (value == null)
            {
                if (!targetType.IsValueType || nullableType != null)
                {
                    score = 3;
                    return true;
                }

                return false;
            }

            Type valueType = value.GetType();

            if (valueType == targetType || valueType == effectiveTargetType)
            {
                score = 0;
                return true;
            }

            if (targetType.IsAssignableFrom(valueType))
            {
                score = 1;
                return true;
            }

            if (typeof(Delegate).IsAssignableFrom(targetType) && value is IScriptCallable)
            {
                score = 2;
                return true;
            }

            if (TryCanConvertCollection(value, targetType, out int collectionScore))
            {
                score = collectionScore;
                return true;
            }

            try
            {
                if (effectiveTargetType.IsEnum)
                {
                    if (value is string enumName)
                    {
                        Enum.Parse(effectiveTargetType, enumName, ignoreCase: false);
                    }
                    else
                    {
                        Enum.ToObject(effectiveTargetType, value);
                    }

                    score = 2;
                    return true;
                }

                if (effectiveTargetType == typeof(string))
                {
                    Convert.ToString(value, CultureInfo.InvariantCulture);
                    score = 2;
                    return true;
                }

                if (value is IConvertible)
                {
                    Convert.ChangeType(value, effectiveTargetType, CultureInfo.InvariantCulture);
                    score = 2;
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is InvalidCastException ||
                exception is FormatException ||
                exception is OverflowException ||
                exception is ArgumentException)
            {
                return false;
            }

            return false;
        }

        public static object ConvertTo(object value, Type targetType)
        {
            targetType = targetType ?? typeof(object);

            if (targetType == typeof(void))
            {
                throw new ScriptException("Cannot convert a value to 'void'.");
            }

            if (targetType == typeof(object))
            {
                return value;
            }

            Type nullableType = Nullable.GetUnderlyingType(targetType);
            Type effectiveTargetType = nullableType ?? targetType;

            if (value == null)
            {
                if (!targetType.IsValueType || nullableType != null)
                {
                    return null;
                }

                return Activator.CreateInstance(targetType);
            }

            Type valueType = value.GetType();

            if (targetType.IsAssignableFrom(valueType))
            {
                return value;
            }

            if (typeof(Delegate).IsAssignableFrom(targetType) && value is IScriptCallable scriptCallable)
            {
                if (scriptCallable is ScriptFunction scriptFunction)
                {
                    return scriptFunction.GetOrCreateDelegate(targetType);
                }

                if (scriptCallable is ScriptLambda scriptLambda)
                {
                    return scriptLambda.GetOrCreateDelegate(targetType);
                }

                return ScriptDelegateAdapter.CreateDelegate(targetType, scriptCallable);
            }

            if (TryConvertCollection(value, targetType, out object convertedCollection))
            {
                return convertedCollection;
            }

            try
            {
                if (effectiveTargetType.IsEnum)
                {
                    if (value is string enumName)
                    {
                        return Enum.Parse(effectiveTargetType, enumName, ignoreCase: false);
                    }

                    return Enum.ToObject(effectiveTargetType, value);
                }

                if (effectiveTargetType == typeof(string))
                {
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                }

                return Convert.ChangeType(value, effectiveTargetType, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (
                exception is InvalidCastException ||
                exception is FormatException ||
                exception is OverflowException ||
                exception is ArgumentException)
            {
                throw new ScriptException($"Cannot convert '{value}' from '{valueType.Name}' to '{targetType.Name}'.");
            }
        }

        public static object[] ConvertArguments(object[] arguments, ParameterInfo[] parameters)
        {
            return ConvertArguments(arguments, parameters, new object[arguments.Length]);
        }

        public static object[] ConvertArguments(object[] arguments, ParameterInfo[] parameters, object[] convertedArguments)
        {
            if (arguments.Length != parameters.Length)
            {
                throw new ScriptException($"Expected {parameters.Length} arguments, got {arguments.Length}.");
            }

            if (convertedArguments == null || convertedArguments.Length != arguments.Length)
            {
                convertedArguments = new object[arguments.Length];
            }

            for (int i = 0; i < arguments.Length; i++)
            {
                convertedArguments[i] = ConvertTo(arguments[i], parameters[i].ParameterType);
            }

            return convertedArguments;
        }

        public static bool IsCompatibleWithType(object value, Type targetType)
        {
            if (targetType == null || value == null)
            {
                return false;
            }

            Type nullableType = Nullable.GetUnderlyingType(targetType);

            if (nullableType != null)
            {
                targetType = nullableType;
            }

            Type valueType = value.GetType();

            if (targetType.IsAssignableFrom(valueType))
            {
                return true;
            }

            if (targetType.IsEnum)
            {
                return CanConvertTo(value, targetType, out _);
            }

            if (targetType.IsValueType)
            {
                return valueType == targetType;
            }

            return false;
        }

        public static bool TryConvertWithAsSemantics(object value, Type targetType, out object converted)
        {
            converted = null;

            if (targetType == null)
            {
                return false;
            }

            Type nullableType = Nullable.GetUnderlyingType(targetType);

            if (targetType.IsValueType && nullableType == null)
            {
                throw new ScriptException($"The 'as' operator requires a reference or nullable type, got '{targetType.Name}'.");
            }

            if (value == null)
            {
                return true;
            }

            Type effectiveTargetType = nullableType ?? targetType;
            Type valueType = value.GetType();

            if (effectiveTargetType.IsAssignableFrom(valueType))
            {
                converted = nullableType != null ? ConvertTo(value, targetType) : value;
                return true;
            }

            if (nullableType != null && valueType == nullableType)
            {
                converted = ConvertTo(value, targetType);
                return true;
            }

            return false;
        }
        private static bool TryCanConvertCollection(object value, Type targetType, out int score)
        {
            score = int.MaxValue;

            if (!(value is IEnumerable enumerable) || value is string)
            {
                return false;
            }

            if (targetType.IsArray)
            {
                Type elementType = targetType.GetElementType();
                Type sourceElementType = TryGetEnumerableElementType(value.GetType());

                if (sourceElementType != null && elementType.IsAssignableFrom(sourceElementType))
                {
                    score = sourceElementType == elementType ? 1 : 2;
                    return true;
                }

                return CanConvertEnumerableElements(enumerable, elementType, out score);
            }

            if (targetType.IsGenericType &&
                targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type elementType = targetType.GetGenericArguments()[0];
                Type sourceElementType = TryGetEnumerableElementType(value.GetType());

                if (sourceElementType != null && elementType.IsAssignableFrom(sourceElementType))
                {
                    score = sourceElementType == elementType ? 2 : 3;
                    return true;
                }

                if (CanConvertEnumerableElements(enumerable, elementType, out int elementScore))
                {
                    score = elementScore + 1;
                    return true;
                }
            }

            return false;
        }

        private static bool TryConvertCollection(object value, Type targetType, out object convertedCollection)
        {
            convertedCollection = null;

            if (!(value is IEnumerable enumerable) || value is string)
            {
                return false;
            }

            if (targetType.IsArray)
            {
                Type elementType = targetType.GetElementType();
                if (enumerable is ICollection collection)
                {
                    Array array = Array.CreateInstance(elementType, collection.Count);
                    int index = 0;

                    foreach (object item in enumerable)
                    {
                        array.SetValue(ConvertTo(item, elementType), index++);
                    }

                    convertedCollection = array;
                    return true;
                }

                convertedCollection = ToArray(enumerable, elementType);
                return true;
            }

            if (targetType.IsGenericType &&
                targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type elementType = targetType.GetGenericArguments()[0];
                IList list = enumerable is ICollection collection
                    ? (IList)Activator.CreateInstance(targetType, collection.Count)
                    : (IList)Activator.CreateInstance(targetType);

                CopyConvertedElements(enumerable, elementType, list);

                convertedCollection = list;
                return true;
            }

            return false;
        }

        private static bool CanConvertEnumerableElements(IEnumerable enumerable, Type elementType, out int score)
        {
            score = 0;

            foreach (object item in enumerable)
            {
                if (!CanConvertTo(item, elementType, out int elementScore))
                {
                    score = int.MaxValue;
                    return false;
                }

                score += elementScore;
            }

            return true;
        }

        private static Array ToArray(IEnumerable enumerable, Type elementType)
        {
            if (enumerable is ICollection collection)
            {
                Array array = Array.CreateInstance(elementType, collection.Count);
                int index = 0;

                foreach (object item in enumerable)
                {
                    array.SetValue(ConvertTo(item, elementType), index++);
                }

                return array;
            }

            var items = new List<object>();

            foreach (object item in enumerable)
            {
                items.Add(ConvertTo(item, elementType));
            }

            Array result = Array.CreateInstance(elementType, items.Count);

            for (int i = 0; i < items.Count; i++)
            {
                result.SetValue(items[i], i);
            }

            return result;
        }

        private static void CopyConvertedElements(IEnumerable enumerable, Type elementType, IList destination)
        {
            foreach (object item in enumerable)
            {
                destination.Add(ConvertTo(item, elementType));
            }
        }

        private static Type TryGetEnumerableElementType(Type enumerableType)
        {
            if (enumerableType == null)
            {
                return null;
            }

            if (enumerableType.IsArray)
            {
                return enumerableType.GetElementType();
            }

            if (enumerableType.IsGenericType)
            {
                Type genericDefinition = enumerableType.GetGenericTypeDefinition();

                if (genericDefinition == typeof(IEnumerable<>) ||
                    genericDefinition == typeof(IList<>) ||
                    genericDefinition == typeof(ICollection<>) ||
                    genericDefinition == typeof(List<>))
                {
                    return enumerableType.GetGenericArguments()[0];
                }
            }

            Type[] interfaces = enumerableType.GetInterfaces();

            for (int i = 0; i < interfaces.Length; i++)
            {
                Type interfaceType = interfaces[i];

                if (interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return interfaceType.GetGenericArguments()[0];
                }
            }

            return null;
        }
    }

    internal static class ScriptDelegateAdapter
    {
        private static readonly MethodInfo InvokeMethod =
            typeof(ScriptDelegateAdapter).GetMethod(nameof(Invoke), BindingFlags.NonPublic | BindingFlags.Static);

        public static Delegate CreateDelegate(Type delegateType, IScriptCallable scriptCallable)
        {
            if (delegateType == null)
            {
                throw new ArgumentNullException(nameof(delegateType));
            }

            if (scriptCallable == null)
            {
                throw new ArgumentNullException(nameof(scriptCallable));
            }

            MethodInfo delegateInvokeMethod = delegateType.GetMethod("Invoke");

            if (delegateInvokeMethod == null)
            {
                throw new ScriptException($"Delegate type '{delegateType.Name}' does not have an Invoke method.");
            }

            ParameterInfo[] parameters = delegateInvokeMethod.GetParameters();
            System.Linq.Expressions.ParameterExpression[] parameterExpressions =
                new System.Linq.Expressions.ParameterExpression[parameters.Length];
            System.Linq.Expressions.Expression[] boxedArguments =
                new System.Linq.Expressions.Expression[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                parameterExpressions[i] = System.Linq.Expressions.Expression.Parameter(parameters[i].ParameterType, parameters[i].Name);
                boxedArguments[i] = System.Linq.Expressions.Expression.Convert(parameterExpressions[i], typeof(object));
            }

            System.Linq.Expressions.ConstantExpression callableExpression =
                System.Linq.Expressions.Expression.Constant(scriptCallable, typeof(IScriptCallable));
            System.Linq.Expressions.NewArrayExpression argumentsArrayExpression =
                System.Linq.Expressions.Expression.NewArrayInit(typeof(object), boxedArguments);
            System.Linq.Expressions.MethodCallExpression invokeExpression =
                System.Linq.Expressions.Expression.Call(
                    InvokeMethod,
                    callableExpression,
                    argumentsArrayExpression,
                    System.Linq.Expressions.Expression.Constant(delegateInvokeMethod.ReturnType, typeof(Type)));

            System.Linq.Expressions.Expression body = delegateInvokeMethod.ReturnType == typeof(void)
                ? (System.Linq.Expressions.Expression)invokeExpression
                : System.Linq.Expressions.Expression.Convert(invokeExpression, delegateInvokeMethod.ReturnType);

            return System.Linq.Expressions.Expression.Lambda(delegateType, body, parameterExpressions).Compile();
        }

        private static object Invoke(IScriptCallable scriptCallable, object[] arguments, Type returnType)
        {
            RuntimeValue result = scriptCallable.Invoke(null, arguments);

            if (returnType == typeof(void))
            {
                return null;
            }

            return RuntimeConversion.ConvertTo(result.Value, returnType);
        }
    }
}

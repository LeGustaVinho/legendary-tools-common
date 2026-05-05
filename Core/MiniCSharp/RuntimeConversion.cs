using System;
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

                return ScriptDelegateAdapter.CreateDelegate(targetType, scriptCallable);
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

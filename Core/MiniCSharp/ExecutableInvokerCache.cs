using System;
using System.Collections.Generic;
using System.Reflection;

namespace LegendaryTools.MiniCSharp
{
    internal static class ExecutableInvokerCache
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<MethodBase, Func<object, object[], object>> InvocationCache =
            new Dictionary<MethodBase, Func<object, object[], object>>();

        public static bool TryGetMethodInvoker(MethodInfo methodInfo, out Func<object, object[], object> invoker)
        {
            if (methodInfo == null)
            {
                invoker = null;
                return false;
            }

            Type declaringType = methodInfo.DeclaringType;

            if (!methodInfo.IsStatic && (declaringType == null || declaringType.IsValueType))
            {
                invoker = null;
                return false;
            }

            invoker = GetOrCreateInvoker(methodInfo);
            return true;
        }

        public static Func<object, object[], object> GetConstructorInvoker(ConstructorInfo constructorInfo)
        {
            if (constructorInfo == null)
            {
                throw new ArgumentNullException(nameof(constructorInfo));
            }

            return GetOrCreateInvoker(constructorInfo);
        }

        private static Func<object, object[], object> GetOrCreateInvoker(MethodBase executable)
        {
            lock (CacheLock)
            {
                if (InvocationCache.TryGetValue(executable, out Func<object, object[], object> cachedInvoker))
                {
                    return cachedInvoker;
                }
            }

            Func<object, object[], object> invoker = BuildInvoker(executable);

            lock (CacheLock)
            {
                InvocationCache[executable] = invoker;
            }

            return invoker;
        }

        private static Func<object, object[], object> BuildInvoker(MethodBase executable)
        {
            System.Linq.Expressions.ParameterExpression targetParameter =
                System.Linq.Expressions.Expression.Parameter(typeof(object), "target");
            System.Linq.Expressions.ParameterExpression argumentsParameter =
                System.Linq.Expressions.Expression.Parameter(typeof(object[]), "arguments");

            ParameterInfo[] parameters = executable.GetParameters();
            System.Linq.Expressions.Expression[] convertedArguments =
                new System.Linq.Expressions.Expression[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                System.Linq.Expressions.Expression argumentAccess =
                    System.Linq.Expressions.Expression.ArrayIndex(
                        argumentsParameter,
                        System.Linq.Expressions.Expression.Constant(i));

                convertedArguments[i] =
                    System.Linq.Expressions.Expression.Convert(argumentAccess, parameters[i].ParameterType);
            }

            System.Linq.Expressions.Expression body;

            if (executable is ConstructorInfo constructorInfo)
            {
                body = System.Linq.Expressions.Expression.Convert(
                    System.Linq.Expressions.Expression.New(constructorInfo, convertedArguments),
                    typeof(object));
            }
            else
            {
                MethodInfo methodInfo = (MethodInfo)executable;
                System.Linq.Expressions.Expression instance = methodInfo.IsStatic
                    ? null
                    : System.Linq.Expressions.Expression.Convert(targetParameter, methodInfo.DeclaringType);
                System.Linq.Expressions.MethodCallExpression callExpression =
                    System.Linq.Expressions.Expression.Call(instance, methodInfo, convertedArguments);

                body = methodInfo.ReturnType == typeof(void)
                    ? System.Linq.Expressions.Expression.Block(
                        callExpression,
                        System.Linq.Expressions.Expression.Constant(null, typeof(object)))
                    : System.Linq.Expressions.Expression.Convert(callExpression, typeof(object));
            }

            return System.Linq.Expressions.Expression
                .Lambda<Func<object, object[], object>>(body, targetParameter, argumentsParameter)
                .Compile();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;

namespace LegendaryTools.MiniCSharp
{
    internal readonly struct DelegateInvocationInfo
    {
        public DelegateInvocationInfo(ParameterInfo[] parameters, Type returnType, Func<Delegate, object[], object> invoker)
        {
            Parameters = parameters;
            ReturnType = returnType;
            Invoker = invoker;
        }

        public ParameterInfo[] Parameters { get; }

        public Type ReturnType { get; }

        public Func<Delegate, object[], object> Invoker { get; }
    }

    internal static class DelegateInvokerCache
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<Type, DelegateInvocationInfo> InvocationCache =
            new Dictionary<Type, DelegateInvocationInfo>();

        public static DelegateInvocationInfo GetInvocationInfo(Type delegateType)
        {
            lock (CacheLock)
            {
                if (InvocationCache.TryGetValue(delegateType, out DelegateInvocationInfo cachedInfo))
                {
                    return cachedInfo;
                }
            }

            MethodInfo invokeMethod = delegateType.GetMethod("Invoke");

            if (invokeMethod == null)
            {
                throw new ScriptException("Delegate invoke method was not found.");
            }

            DelegateInvocationInfo invocationInfo = new DelegateInvocationInfo(
                invokeMethod.GetParameters(),
                invokeMethod.ReturnType,
                BuildInvoker(delegateType, invokeMethod));

            lock (CacheLock)
            {
                InvocationCache[delegateType] = invocationInfo;
            }

            return invocationInfo;
        }

        private static Func<Delegate, object[], object> BuildInvoker(Type delegateType, MethodInfo invokeMethod)
        {
            System.Linq.Expressions.ParameterExpression delegateParameter =
                System.Linq.Expressions.Expression.Parameter(typeof(Delegate), "delegateValue");
            System.Linq.Expressions.ParameterExpression argumentsParameter =
                System.Linq.Expressions.Expression.Parameter(typeof(object[]), "arguments");

            ParameterInfo[] parameters = invokeMethod.GetParameters();
            System.Linq.Expressions.Expression[] arguments = new System.Linq.Expressions.Expression[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                System.Linq.Expressions.Expression argumentAccess =
                    System.Linq.Expressions.Expression.ArrayIndex(
                        argumentsParameter,
                        System.Linq.Expressions.Expression.Constant(i));

                arguments[i] = System.Linq.Expressions.Expression.Convert(argumentAccess, parameters[i].ParameterType);
            }

            System.Linq.Expressions.Expression typedDelegate =
                System.Linq.Expressions.Expression.Convert(delegateParameter, delegateType);
            System.Linq.Expressions.MethodCallExpression callExpression =
                System.Linq.Expressions.Expression.Call(typedDelegate, invokeMethod, arguments);

            System.Linq.Expressions.Expression body = invokeMethod.ReturnType == typeof(void)
                ? System.Linq.Expressions.Expression.Block(
                    callExpression,
                    System.Linq.Expressions.Expression.Constant(null, typeof(object)))
                : System.Linq.Expressions.Expression.Convert(callExpression, typeof(object));

            return System.Linq.Expressions.Expression
                .Lambda<Func<Delegate, object[], object>>(body, delegateParameter, argumentsParameter)
                .Compile();
        }
    }
}

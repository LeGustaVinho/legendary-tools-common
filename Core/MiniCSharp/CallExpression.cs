using System;
using System.Collections.Generic;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class CallExpression : Expression
    {
        private readonly Expression _callee;
        private readonly List<Expression> _arguments;
        private readonly object[] _argumentValues;
        private readonly object[] _delegateConvertedArguments;

        public CallExpression(Expression callee, List<Expression> arguments)
        {
            _callee = callee;
            _arguments = arguments;
            _argumentValues = new object[arguments.Count];
            _delegateConvertedArguments = new object[arguments.Count];
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            object[] arguments = EvaluateArguments(context);

            if (_callee is MemberExpression memberExpression)
            {
                return memberExpression.Call(context, arguments);
            }

            RuntimeValue calleeValue = _callee.Evaluate(context);

            if (calleeValue.Value is Delegate delegateValue)
            {
                return InvokeDelegate(context, delegateValue, arguments);
            }

            if (calleeValue.Value is IScriptCallable scriptCallable)
            {
                return scriptCallable.Invoke(context, arguments);
            }

            throw new ScriptException("Only methods, delegates, and script functions can be called.");
        }

        private object[] EvaluateArguments(ScriptContext context)
        {
            for (int i = 0; i < _arguments.Count; i++)
            {
                RuntimeValue argument = _arguments[i].Evaluate(context);

                context.EnsureValueAllowed(argument.Value, $"evaluate call argument {i}");

                _argumentValues[i] = argument.Value;
            }

            return _argumentValues;
        }

        private RuntimeValue InvokeDelegate(ScriptContext context, Delegate delegateValue, object[] arguments)
        {
            Type delegateType = delegateValue.GetType();

            context.EnsureTypeAllowed(delegateType, $"call delegate '{delegateType.FullName ?? delegateType.Name}'");

            DelegateInvocationInfo invocationInfo = DelegateInvokerCache.GetInvocationInfo(delegateType);

            context.EnsureTypeAllowed(invocationInfo.ReturnType, "call delegate return type");

            for (int i = 0; i < invocationInfo.Parameters.Length; i++)
            {
                context.EnsureTypeAllowed(invocationInfo.Parameters[i].ParameterType, $"call delegate parameter {i}");
            }

            object[] convertedArguments = RuntimeConversion.ConvertArguments(
                arguments,
                invocationInfo.Parameters,
                _delegateConvertedArguments);

            try
            {
                object result = invocationInfo.Invoker(delegateValue, convertedArguments);

                if (invocationInfo.ReturnType == typeof(void))
                {
                    return new RuntimeValue(null, typeof(void));
                }

                context.EnsureValueAllowed(result, "call delegate result");

                return new RuntimeValue(result, invocationInfo.ReturnType);
            }
            catch (Exception exception)
            {
                string message = exception.Message;
                throw new ScriptException($"Delegate call failed: {message}");
            }
        }
    }
}

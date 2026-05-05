using System;
using System.Collections.Generic;

namespace LegendaryTools.MiniCSharp
{
    internal interface IScriptCallable
    {
        RuntimeValue Invoke(ScriptContext context, object[] arguments);
    }

    internal sealed class ScriptFunction : IScriptCallable
    {
        private readonly string _name;
        private readonly Type _returnType;
        private readonly List<ScriptFunctionParameter> _parameters;
        private readonly Statement _body;
        private readonly ScriptContext _declaringContext;
        private readonly Dictionary<Type, Delegate> _delegateCache = new Dictionary<Type, Delegate>();

        public ScriptFunction(
            string name,
            Type returnType,
            List<ScriptFunctionParameter> parameters,
            Statement body,
            ScriptContext declaringContext)
        {
            _name = name;
            _returnType = returnType;
            _parameters = parameters;
            _body = body;
            _declaringContext = declaringContext;
        }

        public RuntimeValue Invoke(ScriptContext context, object[] arguments)
        {
            ScriptContext executionContext = context ?? _declaringContext;

            if (executionContext == null)
            {
                throw new ScriptException($"Function '{_name}' does not have a valid script context.");
            }

            if (arguments.Length != _parameters.Count)
            {
                throw new ScriptException($"Function '{_name}' expected {_parameters.Count} arguments, got {arguments.Length}.");
            }

            executionContext.PushScope();

            try
            {
                for (int i = 0; i < _parameters.Count; i++)
                {
                    ScriptFunctionParameter parameter = _parameters[i];
                    object convertedArgument = RuntimeConversion.ConvertTo(arguments[i], parameter.Type);
                    executionContext.DefineVariable(parameter.Name, parameter.Type, convertedArgument);
                }

                try
                {
                    _body.Execute(executionContext);
                }
                catch (ScriptReturnException returnException)
                {
                    return BuildReturnValue(returnException);
                }

                if (_returnType == typeof(void))
                {
                    return new RuntimeValue(null, typeof(void));
                }

                throw new ScriptException(
                    $"Function '{_name}' with return type '{_returnType.Name}' must return a value.");
            }
            finally
            {
                executionContext.PopScope();
            }
        }

        public Delegate GetOrCreateDelegate(Type delegateType)
        {
            if (_delegateCache.TryGetValue(delegateType, out Delegate cachedDelegate))
            {
                return cachedDelegate;
            }

            Delegate createdDelegate = ScriptDelegateAdapter.CreateDelegate(delegateType, this);
            _delegateCache[delegateType] = createdDelegate;
            return createdDelegate;
        }

        private RuntimeValue BuildReturnValue(ScriptReturnException returnException)
        {
            if (_returnType == typeof(void))
            {
                if (returnException.HasExpression)
                {
                    throw new ScriptException($"Function '{_name}' has return type 'void' and cannot return a value.");
                }

                return new RuntimeValue(null, typeof(void));
            }

            if (!returnException.HasExpression)
            {
                throw new ScriptException(
                    $"Function '{_name}' with return type '{_returnType.Name}' must return a value.");
            }

            object convertedValue = RuntimeConversion.ConvertTo(returnException.ReturnValue.Value, _returnType);
            return new RuntimeValue(convertedValue, _returnType);
        }
    }

    internal readonly struct ScriptFunctionParameter
    {
        public ScriptFunctionParameter(string name, Type type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }

        public Type Type { get; }
    }

    internal sealed class FunctionDeclarationStatement : Statement
    {
        private readonly string _name;
        private readonly Type _returnType;
        private readonly List<ScriptFunctionParameter> _parameters;
        private readonly Statement _body;

        public FunctionDeclarationStatement(
            string name,
            Type returnType,
            List<ScriptFunctionParameter> parameters,
            Statement body)
        {
            _name = name;
            _returnType = returnType;
            _parameters = parameters;
            _body = body;
        }

        public override void Execute(ScriptContext context)
        {
            context.DefineVariable(
                _name,
                typeof(ScriptFunction),
                new ScriptFunction(_name, _returnType, _parameters, _body, context));
        }
    }
}

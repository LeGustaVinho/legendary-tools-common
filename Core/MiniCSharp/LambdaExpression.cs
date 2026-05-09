using System;
using System.Collections.Generic;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class LambdaExpression : Expression
    {
        private readonly List<string> _parameterNames;
        private readonly Expression _body;

        public LambdaExpression(List<string> parameterNames, Expression body)
        {
            _parameterNames = parameterNames;
            _body = body;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ScriptLambda lambda = new ScriptLambda(_parameterNames, _body, context.CaptureClosureContext());
            return new RuntimeValue(lambda, typeof(ScriptLambda));
        }
    }

    internal sealed class ScriptLambda : IScriptCallable
    {
        private readonly List<string> _parameterNames;
        private readonly Expression _body;
        private readonly ScriptContext _closureContext;
        private readonly Dictionary<Type, Delegate> _delegateCache = new Dictionary<Type, Delegate>();

        public ScriptLambda(List<string> parameterNames, Expression body, ScriptContext closureContext)
        {
            _parameterNames = parameterNames;
            _body = body;
            _closureContext = closureContext;
        }

        public RuntimeValue Invoke(ScriptContext context, object[] arguments)
        {
            if (arguments.Length != _parameterNames.Count)
            {
                throw new ScriptException($"Lambda expected {_parameterNames.Count} arguments, got {arguments.Length}.");
            }

            _closureContext.PushScope();

            try
            {
                for (int index = 0; index < _parameterNames.Count; index++)
                {
                    object argument = arguments[index];
                    Type argumentType = argument?.GetType() ?? typeof(object);
                    _closureContext.DefineVariable(_parameterNames[index], argumentType, argument);
                }

                return _body.Evaluate(_closureContext);
            }
            finally
            {
                _closureContext.PopScope();
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
    }
}

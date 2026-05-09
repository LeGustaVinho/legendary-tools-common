using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class ScriptCoroutineEnumerator : IEnumerator, IDisposable
    {
        private readonly string _functionName;
        private readonly List<ScriptFunctionParameter> _parameters;
        private readonly Statement _body;
        private readonly ScriptContext _executionContext;
        private readonly object[] _arguments;

        private IEnumerator _bodyEnumerator;
        private bool _initialized;
        private bool _completed;
        private object _current;

        public ScriptCoroutineEnumerator(
            string functionName,
            List<ScriptFunctionParameter> parameters,
            Statement body,
            ScriptContext executionContext,
            object[] arguments)
        {
            _functionName = functionName;
            _parameters = parameters;
            _body = body;
            _executionContext = executionContext;
            _arguments = arguments ?? Array.Empty<object>();
        }

        public object Current
        {
            get { return _current; }
        }

        public bool MoveNext()
        {
            if (_completed)
            {
                return false;
            }

            if (!_initialized)
            {
                Initialize();
            }

            try
            {
                if (_bodyEnumerator.MoveNext())
                {
                    _current = _bodyEnumerator.Current;
                    return true;
                }

                Complete();
                return false;
            }
            catch (ScriptYieldBreakException)
            {
                Complete();
                return false;
            }
            catch (ScriptReturnException returnException)
            {
                if (returnException.HasExpression)
                {
                    throw new ScriptException(
                        $"Coroutine function '{_functionName}' cannot return a value. Use 'yield return' or 'yield break'.");
                }

                Complete();
                return false;
            }
            catch (Exception)
            {
                Complete();
                throw;
            }
        }

        public void Reset()
        {
            throw new NotSupportedException("Script coroutines do not support Reset().");
        }

        public void Dispose()
        {
            Complete();
        }

        private void Initialize()
        {
            if (_executionContext == null)
            {
                throw new ScriptException($"Coroutine function '{_functionName}' does not have a valid script context.");
            }

            if (_arguments.Length != _parameters.Count)
            {
                throw new ScriptException($"Function '{_functionName}' expected {_parameters.Count} arguments, got {_arguments.Length}.");
            }

            _executionContext.PushScope();

            try
            {
                for (int i = 0; i < _parameters.Count; i++)
                {
                    ScriptFunctionParameter parameter = _parameters[i];
                    object convertedArgument = RuntimeConversion.ConvertTo(_arguments[i], parameter.Type);
                    _executionContext.DefineVariable(parameter.Name, parameter.Type, convertedArgument);
                }

                _bodyEnumerator = _body.ExecuteCoroutine(_executionContext);
                _initialized = true;
            }
            catch
            {
                _executionContext.PopScope();
                throw;
            }
        }

        private void Complete()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _current = null;

            if (_initialized)
            {
                _executionContext.PopScope();
            }
        }
    }
}

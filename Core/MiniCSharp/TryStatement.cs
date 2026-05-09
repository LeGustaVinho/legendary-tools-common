using System;
using System.Runtime.ExceptionServices;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class TryStatement : Statement
    {
        private readonly Statement _tryBlock;
        private readonly Type _catchType;
        private readonly string _catchVariableName;
        private readonly Statement _catchBlock;
        private readonly Statement _finallyBlock;

        public TryStatement(
            Statement tryBlock,
            Type catchType,
            string catchVariableName,
            Statement catchBlock,
            Statement finallyBlock)
        {
            _tryBlock = tryBlock;
            _catchType = catchType;
            _catchVariableName = catchVariableName;
            _catchBlock = catchBlock;
            _finallyBlock = finallyBlock;
        }

        public override void Execute(ScriptContext context)
        {
            Exception pendingException = null;

            try
            {
                _tryBlock.Execute(context);
            }
            catch (Exception exception)
            {
                pendingException = exception;

                if (CanHandle(exception))
                {
                    try
                    {
                        ExecuteCatchBlock(context, exception);
                        pendingException = null;
                    }
                    catch (Exception catchException)
                    {
                        pendingException = catchException;
                    }
                }
            }

            if (_finallyBlock != null)
            {
                try
                {
                    _finallyBlock.Execute(context);
                }
                catch (Exception finallyException)
                {
                    pendingException = finallyException;
                }
            }

            if (pendingException != null)
            {
                Rethrow(pendingException);
            }
        }

        public override System.Collections.IEnumerator ExecuteCoroutine(ScriptContext context)
        {
            Exception pendingException = null;
            System.Collections.IEnumerator tryEnumerator = _tryBlock.ExecuteCoroutine(context);

            while (true)
            {
                bool finished;
                object yieldedValue;
                Exception moveNextException;

                if (!TryMoveNext(tryEnumerator, out finished, out yieldedValue, out moveNextException))
                {
                    pendingException = moveNextException;
                    break;
                }

                if (finished)
                {
                    break;
                }

                yield return yieldedValue;
            }

            if (pendingException != null && CanHandle(pendingException))
            {
                Exception catchException = null;
                bool catchCompleted = true;

                using (new CatchScope(context, _catchType, _catchVariableName, pendingException))
                {
                    System.Collections.IEnumerator catchEnumerator = _catchBlock.ExecuteCoroutine(context);

                    while (true)
                    {
                        bool finished;
                        object yieldedValue;
                        Exception moveNextException;

                        if (!TryMoveNext(catchEnumerator, out finished, out yieldedValue, out moveNextException))
                        {
                            catchCompleted = false;
                            catchException = moveNextException;
                            break;
                        }

                        if (finished)
                        {
                            break;
                        }

                        yield return yieldedValue;
                    }
                }

                pendingException = catchCompleted ? null : catchException;
            }

            if (_finallyBlock != null)
            {
                Exception finallyException = null;
                bool finallyCompleted = true;
                System.Collections.IEnumerator finallyEnumerator = _finallyBlock.ExecuteCoroutine(context);

                while (true)
                {
                    bool finished;
                    object yieldedValue;
                    Exception moveNextException;

                    if (!TryMoveNext(finallyEnumerator, out finished, out yieldedValue, out moveNextException))
                    {
                        finallyCompleted = false;
                        finallyException = moveNextException;
                        break;
                    }

                    if (finished)
                    {
                        break;
                    }

                    yield return yieldedValue;
                }

                if (!finallyCompleted)
                {
                    pendingException = finallyException;
                }
            }

            if (pendingException != null)
            {
                Rethrow(pendingException);
            }
        }

        private bool CanHandle(Exception exception)
        {
            return _catchBlock != null &&
                   IsCatchableException(exception) &&
                   (_catchType == null || _catchType.IsAssignableFrom(exception.GetType()));
        }

        private void ExecuteCatchBlock(ScriptContext context, Exception exception)
        {
            using (new CatchScope(context, _catchType, _catchVariableName, exception))
            {
                _catchBlock.Execute(context);
            }
        }

        private static bool TryMoveNext(
            System.Collections.IEnumerator enumerator,
            out bool finished,
            out object yieldedValue,
            out Exception exception)
        {
            try
            {
                if (!enumerator.MoveNext())
                {
                    finished = true;
                    yieldedValue = null;
                    exception = null;
                    return true;
                }

                finished = false;
                yieldedValue = enumerator.Current;
                exception = null;
                return true;
            }
            catch (Exception moveNextException)
            {
                finished = false;
                yieldedValue = null;
                exception = moveNextException;
                return false;
            }
        }

        private static bool IsCatchableException(Exception exception)
        {
            return !(exception is ScriptBreakException) &&
                   !(exception is ScriptContinueException) &&
                   !(exception is ScriptReturnException) &&
                   !(exception is ScriptYieldBreakException);
        }

        private static void Rethrow(Exception exception)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        private sealed class CatchScope : IDisposable
        {
            private readonly ScriptContext _context;
            private readonly bool _hasScope;

            public CatchScope(ScriptContext context, Type catchType, string catchVariableName, Exception exception)
            {
                _context = context;
                _hasScope = !string.IsNullOrWhiteSpace(catchVariableName);

                if (!_hasScope)
                {
                    return;
                }

                _context.PushScope();
                Type variableType = catchType ?? exception.GetType();
                _context.DefineVariable(catchVariableName, variableType, exception);
            }

            public void Dispose()
            {
                if (_hasScope)
                {
                    _context.PopScope();
                }
            }
        }
    }
}

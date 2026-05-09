using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class ForeachStatement : Statement
    {
        private const int MaxIterations = 1000000;

        private readonly bool _inferType;
        private readonly Type _declaredType;
        private readonly string _variableName;
        private readonly Expression _enumerableExpression;
        private readonly Statement _body;

        public ForeachStatement(
            bool inferType,
            Type declaredType,
            string variableName,
            Expression enumerableExpression,
            Statement body)
        {
            _inferType = inferType;
            _declaredType = declaredType;
            _variableName = variableName;
            _enumerableExpression = enumerableExpression;
            _body = body;
        }

        public override void Execute(ScriptContext context)
        {
            RuntimeValue enumerableValue = _enumerableExpression.Evaluate(context);

            if (enumerableValue.Value == null)
            {
                throw new ScriptException("Cannot iterate over null.");
            }

            if (!(enumerableValue.Value is IEnumerable enumerable))
            {
                throw new ScriptException(
                    $"Type '{enumerableValue.Value.GetType().Name}' does not implement IEnumerable.");
            }

            Type inferredElementType = ResolveElementType(enumerableValue.Type);
            if (enumerableValue.Value is Array array)
            {
                ExecuteArrayLoop(context, array, inferredElementType);
                return;
            }

            if (enumerableValue.Value is IList list)
            {
                ExecuteListLoop(context, list, inferredElementType);
                return;
            }

            int iterationCount = 0;

            foreach (object item in enumerable)
            {
                if (++iterationCount > MaxIterations)
                {
                    throw new ScriptException($"Foreach loop exceeded the safety limit of {MaxIterations} iterations.");
                }

                if (!ExecuteIteration(context, item, inferredElementType))
                {
                    break;
                }
            }
        }

        public override System.Collections.IEnumerator ExecuteCoroutine(ScriptContext context)
        {
            RuntimeValue enumerableValue = _enumerableExpression.Evaluate(context);

            if (enumerableValue.Value == null)
            {
                throw new ScriptException("Cannot iterate over null.");
            }

            if (!(enumerableValue.Value is IEnumerable enumerable))
            {
                throw new ScriptException(
                    $"Type '{enumerableValue.Value.GetType().Name}' does not implement IEnumerable.");
            }

            Type inferredElementType = ResolveElementType(enumerableValue.Type);

            if (enumerableValue.Value is Array array)
            {
                int length = array.Length;

                for (int i = 0; i < length; i++)
                {
                    if (i + 1 > MaxIterations)
                    {
                        throw new ScriptException($"Foreach loop exceeded the safety limit of {MaxIterations} iterations.");
                    }

                    System.Collections.IEnumerator iteration = ExecuteIterationCoroutine(context, array.GetValue(i), inferredElementType);
                    bool shouldContinue = false;

                    while (true)
                    {
                        object yieldedValue;

                        try
                        {
                            if (!iteration.MoveNext())
                            {
                                break;
                            }

                            yieldedValue = iteration.Current;
                        }
                        catch (ScriptContinueException)
                        {
                            shouldContinue = true;
                            break;
                        }
                        catch (ScriptBreakException)
                        {
                            yield break;
                        }

                        yield return yieldedValue;
                    }

                    if (shouldContinue)
                    {
                        continue;
                    }
                }

                yield break;
            }

            if (enumerableValue.Value is IList list)
            {
                int count = list.Count;

                for (int i = 0; i < count; i++)
                {
                    if (i + 1 > MaxIterations)
                    {
                        throw new ScriptException($"Foreach loop exceeded the safety limit of {MaxIterations} iterations.");
                    }

                    System.Collections.IEnumerator iteration = ExecuteIterationCoroutine(context, list[i], inferredElementType);
                    bool shouldContinue = false;

                    while (true)
                    {
                        object yieldedValue;

                        try
                        {
                            if (!iteration.MoveNext())
                            {
                                break;
                            }

                            yieldedValue = iteration.Current;
                        }
                        catch (ScriptContinueException)
                        {
                            shouldContinue = true;
                            break;
                        }
                        catch (ScriptBreakException)
                        {
                            yield break;
                        }

                        yield return yieldedValue;
                    }

                    if (shouldContinue)
                    {
                        continue;
                    }
                }

                yield break;
            }

            int iterationCount = 0;

            foreach (object item in enumerable)
            {
                if (++iterationCount > MaxIterations)
                {
                    throw new ScriptException($"Foreach loop exceeded the safety limit of {MaxIterations} iterations.");
                }

                System.Collections.IEnumerator iteration = ExecuteIterationCoroutine(context, item, inferredElementType);
                bool shouldContinue = false;

                while (true)
                {
                    object yieldedValue;

                    try
                    {
                        if (!iteration.MoveNext())
                        {
                            break;
                        }

                        yieldedValue = iteration.Current;
                    }
                    catch (ScriptContinueException)
                    {
                        shouldContinue = true;
                        break;
                    }
                    catch (ScriptBreakException)
                    {
                        yield break;
                    }

                    yield return yieldedValue;
                }

                if (shouldContinue)
                {
                    continue;
                }
            }
        }

        private void ExecuteArrayLoop(ScriptContext context, Array array, Type inferredElementType)
        {
            int length = array.Length;

            for (int i = 0; i < length; i++)
            {
                if (i + 1 > MaxIterations)
                {
                    throw new ScriptException($"Foreach loop exceeded the safety limit of {MaxIterations} iterations.");
                }

                if (!ExecuteIteration(context, array.GetValue(i), inferredElementType))
                {
                    return;
                }
            }
        }

        private void ExecuteListLoop(ScriptContext context, IList list, Type inferredElementType)
        {
            int count = list.Count;

            for (int i = 0; i < count; i++)
            {
                if (i + 1 > MaxIterations)
                {
                    throw new ScriptException($"Foreach loop exceeded the safety limit of {MaxIterations} iterations.");
                }

                if (!ExecuteIteration(context, list[i], inferredElementType))
                {
                    return;
                }
            }
        }

        private bool ExecuteIteration(ScriptContext context, object item, Type inferredElementType)
        {
            Type variableType = _inferType
                ? inferredElementType ?? item?.GetType() ?? typeof(object)
                : _declaredType;

            object convertedValue = RuntimeConversion.ConvertTo(item, variableType);
            context.PushScope();

            try
            {
                context.DefineVariable(_variableName, variableType, convertedValue);

                _body.Execute(context);
                return true;
            }
            catch (ScriptContinueException)
            {
                return true;
            }
            catch (ScriptBreakException)
            {
                return false;
            }
            finally
            {
                context.PopScope();
            }
        }

        private System.Collections.IEnumerator ExecuteIterationCoroutine(
            ScriptContext context,
            object item,
            Type inferredElementType)
        {
            Type variableType = _inferType
                ? inferredElementType ?? item?.GetType() ?? typeof(object)
                : _declaredType;

            object convertedValue = RuntimeConversion.ConvertTo(item, variableType);
            context.PushScope();

            try
            {
                context.DefineVariable(_variableName, variableType, convertedValue);

                System.Collections.IEnumerator bodyEnumerator = _body.ExecuteCoroutine(context);

                while (bodyEnumerator.MoveNext())
                {
                    yield return bodyEnumerator.Current;
                }
            }
            finally
            {
                context.PopScope();
            }
        }

        private static Type ResolveElementType(Type enumerableType)
        {
            if (enumerableType == null)
            {
                return null;
            }

            if (enumerableType.IsArray)
            {
                return enumerableType.GetElementType();
            }

            if (enumerableType.IsGenericType &&
                enumerableType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return enumerableType.GetGenericArguments()[0];
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
}

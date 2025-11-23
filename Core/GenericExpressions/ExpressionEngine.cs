using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Main entry point: parses and evaluates expressions in string form.
    /// Generic, extensible and cache-aware.
    /// </summary>
    /// <typeparam name="T">Numeric type.</typeparam>
    public sealed class ExpressionEngine<T>
    {
        private readonly INumberOperations<T> _ops;

        /// <summary>
        /// Optional cache of compiled expressions.
        /// Keys are usually the raw expression texts, but you can also use custom keys
        /// via CompileAndCache / PrecompileExpressions.
        /// </summary>
        private readonly Dictionary<string, CompiledExpression<T>> _compiledCache;

        /// <summary>
        /// When true, the Evaluate(string, context) method will use the internal cache:
        /// - If the expression text is already compiled, reuse it.
        /// - Otherwise, compile once, store, and reuse next time.
        /// Default is true.
        /// </summary>
        public bool UseAutoCache { get; set; } = true;

        /// <summary>
        /// Event raised before any compiled expression is evaluated.
        /// Arguments:
        /// - string: expression text (or key, when evaluating via EvaluateCached)
        /// - EvaluationContext<T>: context used for evaluation
        /// </summary>
        public event Action<string, EvaluationContext<T>> BeforeEvaluateExpression;

        /// <summary>
        /// Event raised after any compiled expression is evaluated.
        /// Arguments:
        /// - string: expression text (or key, when evaluating via EvaluateCached)
        /// - EvaluationContext<T>: context used for evaluation
        /// - T: result of the evaluation
        /// </summary>
        public event Action<string, EvaluationContext<T>, T> AfterEvaluateExpression;

        public ExpressionEngine(INumberOperations<T> ops)
        {
            _ops = ops ?? throw new ArgumentNullException(nameof(ops));
            _compiledCache = new Dictionary<string, CompiledExpression<T>>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Internal helper called by CompiledExpression before evaluation.
        /// </summary>
        internal void RaiseBeforeEvaluate(string expressionText, EvaluationContext<T> context)
        {
            BeforeEvaluateExpression?.Invoke(expressionText, context);
        }

        /// <summary>
        /// Internal helper called by CompiledExpression after evaluation.
        /// </summary>
        internal void RaiseAfterEvaluate(string expressionText, EvaluationContext<T> context, T result)
        {
            AfterEvaluateExpression?.Invoke(expressionText, context, result);
        }

        /// <summary>
        /// Parses an expression string into a reusable compiled expression.
        /// This method does NOT use the internal cache: it always creates a new compiled object.
        /// For caching, use CompileAndCache / PrecompileExpressions / Evaluate with UseAutoCache.
        /// </summary>
        public CompiledExpression<T> Compile(string expressionText)
        {
            if (expressionText == null) throw new ArgumentNullException(nameof(expressionText));

            Tokenizer tokenizer = new(expressionText);
            List<Token> tokens = tokenizer.Tokenize();

            Parser<T> parser = new(tokens, _ops);
            ExpressionNode<T> root = parser.ParseExpression();

            // Pass this engine and the original text so compiled expression can trigger events.
            return new CompiledExpression<T>(root, _ops, this, expressionText);
        }

        /// <summary>
        /// Convenience method that compiles and evaluates in one call.
        /// When UseAutoCache is true (default), the engine will:
        /// - reuse a previously compiled expression if it is in the cache;
        /// - otherwise compile, store, and reuse later.
        /// When UseAutoCache is false, this behaves like "Compile(expressionText).Evaluate(context)".
        /// </summary>
        public T Evaluate(string expressionText, EvaluationContext<T> context)
        {
            if (expressionText == null) throw new ArgumentNullException(nameof(expressionText));

            if (context == null) throw new ArgumentNullException(nameof(context));

            CompiledExpression<T> compiled;

            if (UseAutoCache)
            {
                if (!_compiledCache.TryGetValue(expressionText, out compiled))
                {
                    compiled = Compile(expressionText);
                    _compiledCache[expressionText] = compiled;
                }
            }
            else
            {
                compiled = Compile(expressionText);
            }

            return compiled.Evaluate(context);
        }

        #region Cache management / precompilation

        /// <summary>
        /// Compiles the given expression and stores it in the internal cache under the given key.
        /// Returns the compiled expression.
        /// 
        /// Example:
        ///   engine.CompileAndCache("damageFormula", "$atk * 2 - $def");
        /// </summary>
        public CompiledExpression<T> CompileAndCache(string key, string expressionText)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));

            CompiledExpression<T> compiled = Compile(expressionText);
            _compiledCache[key] = compiled;
            return compiled;
        }

        /// <summary>
        /// Precompiles a set of expressions and stores them in the internal cache.
        /// The dictionary keys are used as cache keys, and the values are the expression texts.
        /// 
        /// Example:
        ///   engine.PrecompileExpressions(new Dictionary<string, string> {
        ///       ["damage"] = "$atk * 2 - $def",
        ///       ["critChance"] = "min(1, $critBase + $buff)"
        ///   });
        /// </summary>
        public void PrecompileExpressions(IDictionary<string, string> expressions)
        {
            if (expressions == null) throw new ArgumentNullException(nameof(expressions));

            foreach (KeyValuePair<string, string> kvp in expressions)
            {
                CompileAndCache(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Tries to get a compiled expression from the internal cache by key.
        /// </summary>
        public bool TryGetCompiled(string key, out CompiledExpression<T> compiled)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            return _compiledCache.TryGetValue(key, out compiled);
        }

        /// <summary>
        /// Evaluates a previously cached compiled expression by key.
        /// This method does NOT attempt to compile if the key is missing.
        /// </summary>
        public T EvaluateCached(string key, EvaluationContext<T> context)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!_compiledCache.TryGetValue(key, out CompiledExpression<T> compiled))
                throw new KeyNotFoundException($"No compiled expression found for key '{key}'. " +
                                               "Use CompileAndCache / PrecompileExpressions first.");

            return compiled.Evaluate(context);
        }

        /// <summary>
        /// Removes a compiled expression from the internal cache by key.
        /// </summary>
        public bool RemoveFromCache(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            return _compiledCache.Remove(key);
        }

        /// <summary>
        /// Clears all compiled expressions from the internal cache.
        /// </summary>
        public void ClearCache()
        {
            _compiledCache.Clear();
        }

        #endregion

        /// <summary>
        /// Registers a set of common math and helper functions (sin, cos, if, etc.).
        /// Only works when T can be converted to and from double and boolean via the provided INumberOperations.
        /// </summary>
        public void RegisterDefaultMathFunctions(EvaluationContext<T> context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Helper to wrap a Math.* double -> double function
            T WrapFunc1(Func<double, double> func, IReadOnlyList<T> args)
            {
                if (args.Count != 1) throw new ArgumentException("Function expects exactly one argument.");

                double d = _ops.ToDouble(args[0]);
                double result = func(d);
                return _ops.FromDouble(result);
            }

            // Helper for functions with two arguments
            T WrapFunc2(Func<double, double, double> func, IReadOnlyList<T> args)
            {
                if (args.Count != 2) throw new ArgumentException("Function expects exactly two arguments.");

                double d1 = _ops.ToDouble(args[0]);
                double d2 = _ops.ToDouble(args[1]);
                double result = func(d1, d2);
                return _ops.FromDouble(result);
            }

            // --- Math functions ---

            context.Functions["sin"] = args => WrapFunc1(Math.Sin, args);
            context.Functions["cos"] = args => WrapFunc1(Math.Cos, args);
            context.Functions["tan"] = args => WrapFunc1(Math.Tan, args);
            context.Functions["sqrt"] = args => WrapFunc1(Math.Sqrt, args);
            context.Functions["abs"] = args => WrapFunc1(Math.Abs, args);

            context.Functions["log"] = args =>
            {
                // log(x) or log(x, base)
                if (args.Count == 1) return WrapFunc1(Math.Log, args);

                if (args.Count == 2) return WrapFunc2((x, b) => Math.Log(x, b), args);

                throw new ArgumentException("log expects one or two arguments.");
            };

            context.Functions["min"] = args =>
            {
                if (args.Count == 0) throw new ArgumentException("min expects at least one argument.");

                double min = _ops.ToDouble(args[0]);
                for (int i = 1; i < args.Count; i++)
                {
                    double v = _ops.ToDouble(args[i]);
                    if (v < min) min = v;
                }

                return _ops.FromDouble(min);
            };

            context.Functions["max"] = args =>
            {
                if (args.Count == 0) throw new ArgumentException("max expects at least one argument.");

                double max = _ops.ToDouble(args[0]);
                for (int i = 1; i < args.Count; i++)
                {
                    double v = _ops.ToDouble(args[i]);
                    if (v > max) max = v;
                }

                return _ops.FromDouble(max);
            };

            // --- Inline conditional: if(cond, a, b) ---
            // cond can be any expression that can be converted to boolean via INumberOperations.ToBoolean.
            context.Functions["if"] = args =>
            {
                if (args.Count != 3)
                    throw new ArgumentException(
                        "if expects exactly three arguments: condition, valueIfTrue, valueIfFalse.");

                bool cond = _ops.ToBoolean(args[0]);
                return cond ? args[1] : args[2];
            };
        }

        #region C# function registration helpers

        /// <summary>
        /// Registers a C# function (with return value) to be used from expressions.
        /// The function receives the list of evaluated arguments and must return a value of type T.
        /// </summary>
        public void RegisterFunction(EvaluationContext<T> context, string name, Func<IReadOnlyList<T>, T> function)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Function name cannot be null or whitespace.", nameof(name));

            if (function == null) throw new ArgumentNullException(nameof(function));

            context.Functions[name] = function;
        }

        /// <summary>
        /// Registers a C# "void" function (without return value) to be used from expressions.
        /// It may have zero or many parameters.
        /// The expression engine will return Zero for this function call, so it can be used
        /// either as a statement (inside a sequence) or as part of a larger expression.
        /// </summary>
        public void RegisterVoidFunction(EvaluationContext<T> context, string name, Action<IReadOnlyList<T>> action)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Function name cannot be null or whitespace.", nameof(name));

            if (action == null) throw new ArgumentNullException(nameof(action));

            context.Functions[name] = args =>
            {
                action(args);
                // "Void" functions are represented as returning Zero in expression language.
                return _ops.Zero;
            };
        }

        #endregion

        #region Expression-based function registration

        /// <summary>
        /// Registers a function defined using the expression language itself.
        /// Example:
        ///   RegisterExpressionFunction(ctx, "damage", new[] { "$atk", "$def" }, "$atk * 2 - $def");
        ///
        /// Inside 'bodyExpression', the parameter names are used as variables (must start with '$').
        /// The function returns the result of evaluating bodyExpression.
        /// </summary>
        public void RegisterExpressionFunction(
            EvaluationContext<T> context,
            string name,
            IReadOnlyList<string> parameterNames,
            string bodyExpression)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Function name cannot be null or whitespace.", nameof(name));

            if (parameterNames == null) throw new ArgumentNullException(nameof(parameterNames));

            if (bodyExpression == null) throw new ArgumentNullException(nameof(bodyExpression));

            CompiledExpression<T> compiled = Compile(bodyExpression);

            context.Functions[name] = args =>
            {
                if (args == null) throw new ArgumentNullException(nameof(args));

                if (args.Count != parameterNames.Count)
                    throw new ArgumentException(
                        $"Function '{name}' expects {parameterNames.Count} arguments, but got {args.Count}.");

                // Backup previous variable values for parameters to restore after call.
                Dictionary<string, (bool hadValue, T oldValue)> backups = new(parameterNames.Count);

                try
                {
                    // Bind parameters as variables in the context.
                    for (int i = 0; i < parameterNames.Count; i++)
                    {
                        string paramName = parameterNames[i];
                        if (string.IsNullOrWhiteSpace(paramName))
                            throw new ArgumentException("Parameter name cannot be null or whitespace.",
                                nameof(parameterNames));

                        if (!paramName.StartsWith("$", StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                $"Parameter name '{paramName}' must start with '$' prefix.");

                        if (context.Variables.TryGetValue(paramName, out T existing))
                            backups[paramName] = (true, existing);
                        else
                            backups[paramName] = (false, _ops.Zero);

                        context.Variables[paramName] = args[i];
                    }

                    // Evaluate body expression with parameters bound.
                    T result = compiled.Evaluate(context);
                    return result;
                }
                finally
                {
                    // Restore previous variable state.
                    foreach (KeyValuePair<string, (bool hadValue, T oldValue)> kvp in backups)
                    {
                        if (kvp.Value.hadValue)
                            context.Variables[kvp.Key] = kvp.Value.oldValue;
                        else
                            context.Variables.Remove(kvp.Key);
                    }
                }
            };
        }

        /// <summary>
        /// Registers a "void" function defined using the expression language.
        /// It may have zero or many parameters.
        /// The body expression is evaluated for side effects (assignments, function calls, etc.).
        /// The expression engine will return Zero for this function when called.
        /// </summary>
        public void RegisterExpressionVoidFunction(
            EvaluationContext<T> context,
            string name,
            IReadOnlyList<string> parameterNames,
            string bodyExpression)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Function name cannot be null or whitespace.", nameof(name));

            if (parameterNames == null) throw new ArgumentNullException(nameof(parameterNames));

            if (bodyExpression == null) throw new ArgumentNullException(nameof(bodyExpression));

            CompiledExpression<T> compiled = Compile(bodyExpression);

            context.Functions[name] = args =>
            {
                if (args == null) throw new ArgumentNullException(nameof(args));

                if (args.Count != parameterNames.Count)
                    throw new ArgumentException(
                        $"Function '{name}' expects {parameterNames.Count} arguments, but got {args.Count}.");

                Dictionary<string, (bool hadValue, T oldValue)> backups = new(parameterNames.Count);

                try
                {
                    // Bind parameters as variables in the context.
                    for (int i = 0; i < parameterNames.Count; i++)
                    {
                        string paramName = parameterNames[i];
                        if (string.IsNullOrWhiteSpace(paramName))
                            throw new ArgumentException("Parameter name cannot be null or whitespace.",
                                nameof(parameterNames));

                        if (!paramName.StartsWith("$", StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                $"Parameter name '{paramName}' must start with '$' prefix.");

                        if (context.Variables.TryGetValue(paramName, out T existing))
                            backups[paramName] = (true, existing);
                        else
                            backups[paramName] = (false, _ops.Zero);

                        context.Variables[paramName] = args[i];
                    }

                    // Evaluate body expression for side effects only.
                    _ = compiled.Evaluate(context);

                    // "Void" expression functions return Zero at language level.
                    return _ops.Zero;
                }
                finally
                {
                    // Restore previous variable state.
                    foreach (KeyValuePair<string, (bool hadValue, T oldValue)> kvp in backups)
                    {
                        if (kvp.Value.hadValue)
                            context.Variables[kvp.Key] = kvp.Value.oldValue;
                        else
                            context.Variables.Remove(kvp.Key);
                    }
                }
            };
        }

        #endregion
    }
}
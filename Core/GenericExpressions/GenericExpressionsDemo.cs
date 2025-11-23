using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    internal static class Program
    {
        private static void Main()
        {
            Console.WriteLine("=== Generic Expression Engine Demo ===");
            Console.WriteLine();

            DemoDoubleExpressions();
            DemoFloatExpressions();
            DemoIntAndLongExpressions();
            DemoBooleanExpressions();
            DemoCSharpFunctions();
            DemoExpressionFunctions();
            DemoEvents();

            Console.WriteLine();
            Console.WriteLine("Demo finished. Press any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Demonstrates the engine using double:
        /// - arithmetic
        /// - relational and logical operators
        /// - variables with '$' prefix
        /// - assignments and sequences
        /// - if(cond, a, b)
        /// - variable providers
        /// </summary>
        private static void DemoDoubleExpressions()
        {
            Console.WriteLine("== Double demo ==");

            DoubleNumberOperations ops = new();
            ExpressionEngine<double> engine = new(ops);
            EvaluationContext<double> ctx = new();

            engine.RegisterDefaultMathFunctions(ctx);

            // Local variables (must start with '$').
            ctx.Variables["$x"] = 3.0;
            ctx.Variables["$y"] = 4.0;
            ctx.Variables["$hp"] = 15.0;
            ctx.Variables["$mana"] = 20.0;

            // Global/shared variables via provider
            Dictionary<string, double> globalVars = new(StringComparer.OrdinalIgnoreCase)
            {
                ["$g"] = 9.81,
                ["$pi"] = Math.PI
            };

            ctx.VariableProviders.Add(new DictionaryVariableProvider<double>(globalVars));

            // Arithmetic
            string expr1 = "2 * $x + $y"; // 2 * 3 + 4 = 10
            string expr2 = "$g * 2"; // from provider
            string expr3 = "$pi * $x^2"; // π * 9

            double r1 = engine.Evaluate(expr1, ctx);
            double r2 = engine.Evaluate(expr2, ctx);
            double r3 = engine.Evaluate(expr3, ctx);

            Console.WriteLine($"{expr1} = {r1}");
            Console.WriteLine($"{expr2} = {r2}");
            Console.WriteLine($"{expr3} = {r3}");

            // Relational + logical + boolean literals
            string exprLogic = "$hp > 0 and $mana >= 10 or false";
            double rLogic = engine.Evaluate(exprLogic, ctx);
            bool logicResult = ops.ToBoolean(rLogic);
            Console.WriteLine($"{exprLogic} = {logicResult}");

            // Inline if
            string exprIf = "if($hp > 0 and $mana >= 10, 100, 0)";
            double rIf = engine.Evaluate(exprIf, ctx);
            Console.WriteLine($"{exprIf} = {rIf}");

            // Assignments and sequences
            string exprSeq = "$x = 2; $y = $x * 3; $y + 1";
            // $x = 2; $y = 6; result = 7
            double rSeq = engine.Evaluate(exprSeq, ctx);
            Console.WriteLine($"{exprSeq} => {rSeq} (with $x={ctx.Variables["$x"]}, $y={ctx.Variables["$y"]})");

            Console.WriteLine();
        }

        /// <summary>
        /// Demonstrates using float as numeric type (Unity-friendly).
        /// </summary>
        private static void DemoFloatExpressions()
        {
            Console.WriteLine("== Float demo ==");

            FloatNumberOperations ops = new();
            ExpressionEngine<float> engine = new(ops);
            EvaluationContext<float> ctx = new();

            engine.RegisterDefaultMathFunctions(ctx);

            ctx.Variables["$speed"] = 3.5f;
            ctx.Variables["$time"] = 2.0f;

            string expr = "$speed * $time"; // 7.0f
            float distance = engine.Evaluate(expr, ctx);

            Console.WriteLine($"{expr} = {distance} (float)");
            Console.WriteLine();
        }

        /// <summary>
        /// Demonstrates using int and long as numeric types.
        /// </summary>
        private static void DemoIntAndLongExpressions()
        {
            Console.WriteLine("== Int and Long demo ==");

            // int
            ExpressionEngine<int> engineInt = new(new Int32NumberOperations());
            EvaluationContext<int> ctxInt = new();
            engineInt.RegisterDefaultMathFunctions(ctxInt);

            ctxInt.Variables["$level"] = 5;
            string exprInt = "$level * 2 + 1"; // 11
            int lvlResult = engineInt.Evaluate(exprInt, ctxInt);
            Console.WriteLine($"int: {exprInt} = {lvlResult}");

            // long
            ExpressionEngine<long> engineLong = new(new Int64NumberOperations());
            EvaluationContext<long> ctxLong = new();

            ctxLong.Variables["$score"] = 1_000_000_000L;
            string exprLong = "$score + 10";
            long scoreResult = engineLong.Evaluate(exprLong, ctxLong);
            Console.WriteLine($"long: {exprLong} = {scoreResult}");

            Console.WriteLine();
        }

        /// <summary>
        /// Demonstrates using bool as the underlying type for purely logical expressions.
        /// </summary>
        private static void DemoBooleanExpressions()
        {
            Console.WriteLine("== Boolean demo ==");

            ExpressionEngine<bool> engineBool = new(new BooleanNumberOperations());
            EvaluationContext<bool> ctxBool = new();

            ctxBool.Variables["$alive"] = true;
            ctxBool.Variables["$hasMana"] = false;

            string expr1 = "$alive and not $hasMana";
            string expr2 = "$alive && $hasMana";

            bool r1 = engineBool.Evaluate(expr1, ctxBool);
            bool r2 = engineBool.Evaluate(expr2, ctxBool);

            Console.WriteLine($"{expr1} = {r1}");
            Console.WriteLine($"{expr2} = {r2}");

            // if(cond, a, b) also works for bool (when default functions are registered).
            engineBool.RegisterDefaultMathFunctions(ctxBool);
            ctxBool.Variables["$cond"] = true;

            string exprIf = "if($cond, true, false)";
            bool rIf = engineBool.Evaluate(exprIf, ctxBool);
            Console.WriteLine($"{exprIf} = {rIf}");

            Console.WriteLine();
        }

        /// <summary>
        /// Demonstrates C# functions registered into the expression engine:
        /// - function with return value
        /// - "void" function (side effects only)
        /// </summary>
        private static void DemoCSharpFunctions()
        {
            Console.WriteLine("== C# function demo ==");

            DoubleNumberOperations ops = new();
            ExpressionEngine<double> engine = new(ops);
            EvaluationContext<double> ctx = new();

            engine.RegisterDefaultMathFunctions(ctx);

            // Function with return value: square(x) = x * x
            engine.RegisterFunction(ctx, "square", args =>
            {
                if (args.Count != 1) throw new ArgumentException("square expects one argument.");
                double v = ops.ToDouble(args[0]);
                return ops.FromDouble(v * v);
            });

            // "Void" function: logValue(x) prints to console, returns Zero in expression
            engine.RegisterVoidFunction(ctx, "logValue", args =>
            {
                Console.WriteLine("[logValue] Called from expression with arguments:");
                for (int i = 0; i < args.Count; i++)
                {
                    double v = ops.ToDouble(args[i]);
                    Console.WriteLine($"  arg[{i}] = {v}");
                }
            });

            ctx.Variables["$x"] = 3.0;

            string expr1 = "square($x)"; // 9
            string expr2 = "logValue($x, 10); 5"; // prints and result is 5 (last expression in sequence)

            double r1 = engine.Evaluate(expr1, ctx);
            double r2 = engine.Evaluate(expr2, ctx);

            Console.WriteLine($"{expr1} = {r1}");
            Console.WriteLine($"{expr2} = {r2}");
            Console.WriteLine();
        }

        /// <summary>
        /// Demonstrates functions declared using the expression language itself:
        /// - function with return value
        /// - "void" function (side effects only)
        /// </summary>
        private static void DemoExpressionFunctions()
        {
            Console.WriteLine("== Expression-based function demo ==");

            DoubleNumberOperations ops = new();
            ExpressionEngine<double> engine = new(ops);
            EvaluationContext<double> ctx = new();

            engine.RegisterDefaultMathFunctions(ctx);

            // damage($atk, $def) = $atk * 2 - $def
            engine.RegisterExpressionFunction(
                ctx,
                "damage",
                new[] { "$atk", "$def" },
                "$atk * 2 - $def"
            );

            // buff($atk) : $atk = $atk + 10 (void, side effect on parameter)
            engine.RegisterExpressionVoidFunction(
                ctx,
                "buff",
                new[] { "$atk" },
                "$atk = $atk + 10"
            );

            ctx.Variables["$atk"] = 10.0;
            ctx.Variables["$def"] = 3.0;

            // Call buff to modify $atk in outer scope.
            Console.WriteLine($"Before buff: $atk = {ctx.Variables["$atk"]}");
            engine.Evaluate("buff($atk)", ctx);
            Console.WriteLine($"After buff:  $atk = {ctx.Variables["$atk"]}");

            // Now use damage with explicit parameters (does not depend on outer $atk/$def).
            string exprDamage = "damage($atk, $def)";
            double dmg = engine.Evaluate(exprDamage, ctx);
            Console.WriteLine($"{exprDamage} = {dmg}");

            // Expression-based "void" function with non-parameter side effect:
            // storeDouble($value): $stored = $value * 2
            engine.RegisterExpressionVoidFunction(
                ctx,
                "storeDouble",
                new[] { "$value" },
                "$stored = $value * 2"
            );

            ctx.Variables["$value"] = 5.0;
            ctx.Variables["$stored"] = 0.0;

            engine.Evaluate("storeDouble(7)", ctx);
            Console.WriteLine($"storeDouble(7) -> $stored = {ctx.Variables["$stored"]}");

            Console.WriteLine();
        }

        /// <summary>
        /// Demonstrates engine events:
        /// - BeforeEvaluateExpression
        /// - AfterEvaluateExpression
        /// </summary>
        private static void DemoEvents()
        {
            Console.WriteLine("== Events demo ==");

            DoubleNumberOperations ops = new();
            ExpressionEngine<double> engine = new(ops);
            EvaluationContext<double> ctx = new();

            engine.RegisterDefaultMathFunctions(ctx);

            ctx.Variables["$x"] = 2.0;

            engine.BeforeEvaluateExpression += (expr, context) =>
            {
                Console.WriteLine($"[Before] Evaluating: {expr}");
            };

            engine.AfterEvaluateExpression += (expr, context, result) =>
            {
                Console.WriteLine($"[After]  {expr} = {result}");
            };

            string expr1 = "$x * 3";
            string expr2 = "$x = 10; $x + 1";

            double r1 = engine.Evaluate(expr1, ctx);
            double r2 = engine.Evaluate(expr2, ctx);

            Console.WriteLine($"Result 1: {r1}");
            Console.WriteLine($"Result 2: {r2}");
            Console.WriteLine($"Final $x: {ctx.Variables["$x"]}");
            Console.WriteLine();
        }
    }
}
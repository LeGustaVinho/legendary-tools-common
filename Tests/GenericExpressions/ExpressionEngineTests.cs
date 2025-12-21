using System;
using System.Collections.Generic;
using NUnit.Framework;
using LegendaryTools.GenericExpressionEngine;

namespace LegendaryTools.Tests.ExpressionEngineUnityTests
{
    /// <summary>
    /// Unit tests for the generic ExpressionEngine using the Unity Test Runner (NUnit).
    /// These tests focus on the public API of the engine across multiple numeric types.
    /// </summary>
    public class ExpressionEngineTests
    {
        private const double DoubleTolerance = 1e-9;
        private const float FloatTolerance = 1e-6f;

        private ExpressionEngine<double> _engineDouble;
        private EvaluationContext<double> _ctxDouble;

        [SetUp]
        public void SetUp()
        {
            DoubleNumberOperations ops = new();
            _engineDouble = new ExpressionEngine<double>(ops);
            _ctxDouble = new EvaluationContext<double>();

            // Register default math and helper functions (sin, cos, log, min, max, if, etc.).
            _engineDouble.RegisterDefaultMathFunctions(_ctxDouble);
        }

        #region Helper types

        /// <summary>
        /// Variable provider that records how many times it was called and resolves a specific name.
        /// Used to verify caching behavior in EvaluationContext.
        /// </summary>
        private sealed class RecordingVariableProvider : IVariableProvider<double>
        {
            public int CallCount { get; private set; }

            public bool TryGetVariable(string name, out double value)
            {
                CallCount++;

                if (string.Equals(name, "$foo", StringComparison.OrdinalIgnoreCase))
                {
                    value = 42.0;
                    return true;
                }

                value = 0.0;
                return false;
            }
        }

        #endregion

        #region Basic literals and arithmetic (double)

        [Test]
        public void LiteralExpression_ReturnsNumericValue_Double()
        {
            const string expression = "42";

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(42.0, result, DoubleTolerance);
        }

        [Test]
        public void SimpleAdditionSubtractionMultiplicationDivision_Double()
        {
            const string exprAdd = "1 + 2";
            const string exprSub = "5 - 3";
            const string exprMul = "4 * 2";
            const string exprDiv = "8 / 4";

            double rAdd = _engineDouble.Evaluate(exprAdd, _ctxDouble);
            double rSub = _engineDouble.Evaluate(exprSub, _ctxDouble);
            double rMul = _engineDouble.Evaluate(exprMul, _ctxDouble);
            double rDiv = _engineDouble.Evaluate(exprDiv, _ctxDouble);

            Assert.AreEqual(3.0, rAdd, DoubleTolerance);
            Assert.AreEqual(2.0, rSub, DoubleTolerance);
            Assert.AreEqual(8.0, rMul, DoubleTolerance);
            Assert.AreEqual(2.0, rDiv, DoubleTolerance);
        }

        [Test]
        public void OperatorPrecedence_MultiplicationBeforeAddition_Double()
        {
            const string expression = "2 + 3 * 4"; // 2 + (3 * 4) = 14

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(14.0, result, DoubleTolerance);
        }

        [Test]
        public void PowerOperator_IsRightAssociative_Double()
        {
            const string expression = "2 ^ 3 ^ 2"; // 2 ^ (3 ^ 2) = 2 ^ 9 = 512

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(512.0, result, DoubleTolerance);
        }

        [Test]
        public void UnaryPlusAndMinus_AreHandledCorrectly_Double()
        {
            const string expr1 = "-2";
            const string expr2 = "+2";
            const string expr3 = "--2"; // 2
            const string expr4 = "+-2"; // -2

            double r1 = _engineDouble.Evaluate(expr1, _ctxDouble);
            double r2 = _engineDouble.Evaluate(expr2, _ctxDouble);
            double r3 = _engineDouble.Evaluate(expr3, _ctxDouble);
            double r4 = _engineDouble.Evaluate(expr4, _ctxDouble);

            Assert.AreEqual(-2.0, r1, DoubleTolerance);
            Assert.AreEqual(2.0, r2, DoubleTolerance);
            Assert.AreEqual(2.0, r3, DoubleTolerance);
            Assert.AreEqual(-2.0, r4, DoubleTolerance);
        }

        [Test]
        public void Parentheses_OverrideDefaultPrecedence_Double()
        {
            const string expression = "(2 + 3) * 4"; // 20

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(20.0, result, DoubleTolerance);
        }

        #endregion

        #region Variables and variable providers (double, with $ prefix)

        [Test]
        public void VariableLookup_UsesLocalVariables_WithPrefix()
        {
            _ctxDouble.Variables["$x"] = 3.0;
            _ctxDouble.Variables["$y"] = 4.0;
            const string expression = "2 * $x + $y"; // 2 * 3 + 4 = 10

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(10.0, result, DoubleTolerance);
        }

        [Test]
        public void VariableWithoutPrefix_ThrowsOnCompile()
        {
            const string expression = "x = 1"; // Missing '$' prefix

            Assert.Throws<InvalidOperationException>(() => _engineDouble.Compile(expression));
        }

        [Test]
        public void VariableLookup_UsesProvidersWhenNotInLocalContext()
        {
            Dictionary<string, double> globals = new(StringComparer.OrdinalIgnoreCase)
            {
                ["$g"] = 9.81
            };

            DictionaryVariableProvider<double> provider = new(globals);
            _ctxDouble.VariableProviders.Add(provider);

            const string expression = "$g * 2";

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(9.81 * 2.0, result, DoubleTolerance);
        }

        [Test]
        public void VariableLookup_CachesProviderResultInLocalVariables()
        {
            RecordingVariableProvider recordingProvider = new();
            _ctxDouble.VariableProviders.Add(recordingProvider);

            const string expression = "$foo + $foo"; // Should hit provider once, then use cache

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(84.0, result, DoubleTolerance); // 42 + 42
            Assert.IsTrue(_ctxDouble.Variables.ContainsKey("$foo"), "Variable should be cached in context.");
            Assert.AreEqual(1, recordingProvider.CallCount, "Provider should be called only once due to caching.");
        }

        [Test]
        public void UndefinedVariable_ThrowsKeyNotFoundException()
        {
            const string expression = "$unknownVar + 1";

            Assert.Throws<KeyNotFoundException>(() => _engineDouble.Evaluate(expression, _ctxDouble));
        }

        #endregion

        #region Functions and math helpers (double)

        [Test]
        public void DefaultMathFunctions_SinCosSqrtAbs_WorkAsExpected()
        {
            _ctxDouble.Variables["$pi"] = Math.PI;
            const string exprSin = "sin($pi / 2)"; // 1
            const string exprCos = "cos(0)"; // 1
            const string exprSqrt = "sqrt(9)"; // 3
            const string exprAbs = "abs(-5)"; // 5

            double rSin = _engineDouble.Evaluate(exprSin, _ctxDouble);
            double rCos = _engineDouble.Evaluate(exprCos, _ctxDouble);
            double rSqrt = _engineDouble.Evaluate(exprSqrt, _ctxDouble);
            double rAbs = _engineDouble.Evaluate(exprAbs, _ctxDouble);

            Assert.AreEqual(1.0, rSin, DoubleTolerance);
            Assert.AreEqual(1.0, rCos, DoubleTolerance);
            Assert.AreEqual(3.0, rSqrt, DoubleTolerance);
            Assert.AreEqual(5.0, rAbs, DoubleTolerance);
        }

        [Test]
        public void LogFunction_SupportsOneAndTwoArguments()
        {
            _ctxDouble.Variables["$e"] = Math.E;
            const string exprNaturalLog = "log($e)";
            const string exprBaseLog = "log(16, 2)";

            double rNat = _engineDouble.Evaluate(exprNaturalLog, _ctxDouble);
            double rBase = _engineDouble.Evaluate(exprBaseLog, _ctxDouble);

            Assert.AreEqual(1.0, rNat, 1e-7);
            Assert.AreEqual(4.0, rBase, DoubleTolerance);
        }

        [Test]
        public void LogFunction_InvalidArgumentCount_Throws()
        {
            const string exprLogNoArgs = "log()";

            Assert.Throws<ArgumentException>(() => _engineDouble.Evaluate(exprLogNoArgs, _ctxDouble));
        }

        [Test]
        public void MinAndMaxFunctions_WorkWithMultipleArguments()
        {
            const string exprMin = "min(10, 5, 7, 2, 8)";
            const string exprMax = "max(10, 5, 7, 2, 8)";

            double rMin = _engineDouble.Evaluate(exprMin, _ctxDouble);
            double rMax = _engineDouble.Evaluate(exprMax, _ctxDouble);

            Assert.AreEqual(2.0, rMin, DoubleTolerance);
            Assert.AreEqual(10.0, rMax, DoubleTolerance);
        }

        [Test]
        public void IfFunction_WorksForNumericValues()
        {
            _ctxDouble.Variables["$hp"] = 10.0;
            const string expr = "if($hp > 0, 100, 0)";

            double result = _engineDouble.Evaluate(expr, _ctxDouble);

            Assert.AreEqual(100.0, result, DoubleTolerance);
        }

        [Test]
        public void CallingUnknownFunction_ThrowsKeyNotFoundException()
        {
            const string expression = "unknownFunc(1, 2)";

            Assert.Throws<KeyNotFoundException>(() => _engineDouble.Evaluate(expression, _ctxDouble));
        }

        #endregion

        #region Relational and logical operators (double)

        [Test]
        public void RelationalOperators_ReturnBooleanValues_Double()
        {
            const string expr1 = "1 < 2";
            const string expr2 = "2 <= 2";
            const string expr3 = "3 > 2";
            const string expr4 = "3 >= 3";
            const string expr5 = "5 == 5";
            const string expr6 = "5 != 4";

            double r1 = _engineDouble.Evaluate(expr1, _ctxDouble);
            double r2 = _engineDouble.Evaluate(expr2, _ctxDouble);
            double r3 = _engineDouble.Evaluate(expr3, _ctxDouble);
            double r4 = _engineDouble.Evaluate(expr4, _ctxDouble);
            double r5 = _engineDouble.Evaluate(expr5, _ctxDouble);
            double r6 = _engineDouble.Evaluate(expr6, _ctxDouble);

            DoubleNumberOperations ops = new();

            Assert.IsTrue(ops.ToBoolean(r1));
            Assert.IsTrue(ops.ToBoolean(r2));
            Assert.IsTrue(ops.ToBoolean(r3));
            Assert.IsTrue(ops.ToBoolean(r4));
            Assert.IsTrue(ops.ToBoolean(r5));
            Assert.IsTrue(ops.ToBoolean(r6));
        }

        [Test]
        public void LogicalOperators_AndOrNot_WithKeywords()
        {
            _ctxDouble.Variables["$hp"] = 10.0;
            _ctxDouble.Variables["$mana"] = 5.0;

            const string expr = "$hp > 0 and not ($mana <= 0)";

            double result = _engineDouble.Evaluate(expr, _ctxDouble);
            DoubleNumberOperations ops = new();
            bool b = ops.ToBoolean(result);

            Assert.IsTrue(b);
        }

        [Test]
        public void LogicalOperators_AndOrNot_WithSymbols()
        {
            _ctxDouble.Variables["$hp"] = 10.0;
            _ctxDouble.Variables["$mana"] = 0.0;

            const string expr = "$hp > 0 && !($mana > 0)";

            double result = _engineDouble.Evaluate(expr, _ctxDouble);
            DoubleNumberOperations ops = new();
            bool b = ops.ToBoolean(result);

            Assert.IsTrue(b);
        }

        [Test]
        public void LogicalOperators_ShortCircuit_And()
        {
            _ctxDouble.Variables["$hp"] = -1.0;

            const string expr = "$hp > 0 and (1 / 0) > 0";

            // Short-circuit: left is false, so right must not be evaluated.
            Assert.DoesNotThrow(() => _engineDouble.Evaluate(expr, _ctxDouble));
        }

        [Test]
        public void LogicalOperators_ShortCircuit_Or()
        {
            _ctxDouble.Variables["$hp"] = 10.0;

            const string expr = "$hp > 0 or (1 / 0) > 0";

            // Short-circuit: left is true, so right must not be evaluated.
            Assert.DoesNotThrow(() => _engineDouble.Evaluate(expr, _ctxDouble));
        }

        [Test]
        public void BooleanLiterals_TrueFalse_AreSupported()
        {
            const string exprTrue = "true";
            const string exprFalse = "false";
            const string exprCombo = "true and false";

            double rTrue = _engineDouble.Evaluate(exprTrue, _ctxDouble);
            double rFalse = _engineDouble.Evaluate(exprFalse, _ctxDouble);
            double rCombo = _engineDouble.Evaluate(exprCombo, _ctxDouble);

            DoubleNumberOperations ops = new();

            Assert.IsTrue(ops.ToBoolean(rTrue));
            Assert.IsFalse(ops.ToBoolean(rFalse));
            Assert.IsFalse(ops.ToBoolean(rCombo));
        }

        #endregion

        #region Assignment and sequences (double)

        [Test]
        public void AssignmentExpression_SetsVariableAndReturnsAssignedValue()
        {
            _ctxDouble.Variables["$y"] = 4.0;
            const string expression = "$x = 2 * $y + 3"; // $x = 2 * 4 + 3 = 11

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(11.0, result, DoubleTolerance, "Assignment expression should return the assigned value.");
            Assert.IsTrue(_ctxDouble.Variables.ContainsKey("$x"), "Variable $x should be created in context.");
            Assert.AreEqual(11.0, _ctxDouble.Variables["$x"], DoubleTolerance,
                "Variable $x should hold the assigned value.");
        }

        [Test]
        public void ChainedAssignment_IsRightAssociativeAndPropagatesValue()
        {
            const string expression = "$x = $y = 3";

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(3.0, result, DoubleTolerance, "Chained assignment should return the final assigned value.");
            Assert.IsTrue(_ctxDouble.Variables.ContainsKey("$y"), "Variable $y should be created.");
            Assert.IsTrue(_ctxDouble.Variables.ContainsKey("$x"), "Variable $x should be created.");

            Assert.AreEqual(3.0, _ctxDouble.Variables["$y"], DoubleTolerance, "Variable $y should be assigned first.");
            Assert.AreEqual(3.0, _ctxDouble.Variables["$x"], DoubleTolerance,
                "Variable $x should receive the value of $y.");
        }

        [Test]
        public void SequenceOfAssignmentsAndExpression_ReturnsLastExpressionValue()
        {
            const string expression = "$x = 2; $y = $x * 3; $y + 1";
            // $x = 2
            // $y = 6
            // result = 7

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(7.0, result, DoubleTolerance, "Sequence should return the value of the last expression.");

            Assert.IsTrue(_ctxDouble.Variables.ContainsKey("$x"), "Variable $x should be created in sequence.");
            Assert.IsTrue(_ctxDouble.Variables.ContainsKey("$y"), "Variable $y should be created in sequence.");

            Assert.AreEqual(2.0, _ctxDouble.Variables["$x"], DoubleTolerance);
            Assert.AreEqual(6.0, _ctxDouble.Variables["$y"], DoubleTolerance);
        }

        [Test]
        public void Sequence_MixingAssignmentsAndFunctions_WorksCorrectly()
        {
            _ctxDouble.Variables["$pi"] = Math.PI;
            const string expression = "$x = 0; $x = sin($pi / 2); $x + 1";
            // $x = 0
            // $x = 1
            // result = 2

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(2.0, result, 1e-7, "Sequence with assignments and functions should compute correctly.");
            Assert.IsTrue(_ctxDouble.Variables.ContainsKey("$x"), "Variable $x should exist after sequence.");
            Assert.AreEqual(1.0, _ctxDouble.Variables["$x"], 1e-7);
        }

        #endregion

        #region Compiled expression reuse (double)

        [Test]
        public void CompiledExpression_CanBeReusedWithDifferentVariableValues()
        {
            _ctxDouble.Variables["$x"] = 1.0;
            _ctxDouble.Variables["$y"] = 2.0;

            CompiledExpression<double> compiled = _engineDouble.Compile("$x * 10 + $y");

            double r1 = compiled.Evaluate(_ctxDouble); // 1 * 10 + 2 = 12

            _ctxDouble.Variables["$x"] = 3.0;
            _ctxDouble.Variables["$y"] = 4.0;

            double r2 = compiled.Evaluate(_ctxDouble); // 3 * 10 + 4 = 34

            Assert.AreEqual(12.0, r1, DoubleTolerance);
            Assert.AreEqual(34.0, r2, DoubleTolerance);
        }

        [Test]
        public void EvaluateMethod_CompilesAndEvaluatesInOneCall()
        {
            _ctxDouble.Variables["$x"] = 5.0;
            const string expression = "$x * 2";

            double result = _engineDouble.Evaluate(expression, _ctxDouble);

            Assert.AreEqual(10.0, result, DoubleTolerance);
        }

        #endregion

        #region Multi-type tests (float, int, long, bool)

        [Test]
        public void FloatEngine_BasicArithmeticAndFunctions()
        {
            ExpressionEngine<float> engine = new(new FloatNumberOperations());
            EvaluationContext<float> ctx = new();
            engine.RegisterDefaultMathFunctions(ctx);

            ctx.Variables["$x"] = 3.5f;

            float r1 = engine.Evaluate("$x * 2", ctx); // 7
            float r2 = engine.Evaluate("sqrt(4)", ctx); // 2

            Assert.AreEqual(7f, r1, FloatTolerance);
            Assert.AreEqual(2f, r2, FloatTolerance);
        }

        [Test]
        public void IntEngine_BasicArithmetic()
        {
            ExpressionEngine<int> engine = new(new Int32NumberOperations());
            EvaluationContext<int> ctx = new();
            engine.RegisterDefaultMathFunctions(ctx);

            ctx.Variables["$level"] = 5;

            int r1 = engine.Evaluate("$level * 2 + 1", ctx); // 11
            int r2 = engine.Evaluate("$level ^ 2", ctx); // 25

            Assert.AreEqual(11, r1);
            Assert.AreEqual(25, r2);
        }

        [Test]
        public void LongEngine_BasicArithmetic()
        {
            ExpressionEngine<long> engine = new(new Int64NumberOperations());
            EvaluationContext<long> ctx = new();

            ctx.Variables["$score"] = 1_000_000_000L;

            long r1 = engine.Evaluate("$score + 10", ctx);

            Assert.AreEqual(1_000_000_010L, r1);
        }

        [Test]
        public void BooleanEngine_LogicalExpressionsOnly()
        {
            ExpressionEngine<bool> engine = new(new BooleanNumberOperations());
            EvaluationContext<bool> ctx = new();

            // Variables are boolean values.
            ctx.Variables["$alive"] = true;
            ctx.Variables["$hasMana"] = false;

            bool r1 = engine.Evaluate("$alive and not $hasMana", ctx);
            bool r2 = engine.Evaluate("$alive && $hasMana", ctx);

            Assert.IsTrue(r1);
            Assert.IsFalse(r2);
        }

        [Test]
        public void BooleanEngine_IfFunctionWithBooleans()
        {
            ExpressionEngine<bool> engine = new(new BooleanNumberOperations());
            EvaluationContext<bool> ctx = new();
            engine.RegisterDefaultMathFunctions(ctx); // Registers "if", even for bool

            ctx.Variables["$cond"] = true;

            bool r1 = engine.Evaluate("if($cond, true, false)", ctx);
            bool r2 = engine.Evaluate("if(!$cond, true, false)", ctx);

            Assert.IsTrue(r1);
            Assert.IsFalse(r2);
        }

        [Test]
        public void BooleanEngine_ArithmeticOperatorsThrow()
        {
            ExpressionEngine<bool> engine = new(new BooleanNumberOperations());
            EvaluationContext<bool> ctx = new();

            const string expression = "true + false";

            Assert.Throws<NotSupportedException>(() => engine.Evaluate(expression, ctx));
        }

        #endregion

        #region Events and custom functions

        [Test]
        public void Events_BeforeAndAfterAreRaisedWithCorrectData()
        {
            _ctxDouble.Variables["$x"] = 2.0;
            const string expr = "$x * 3";

            bool beforeRaised = false;
            bool afterRaised = false;
            string beforeExpr = null;
            string afterExpr = null;
            double capturedResult = 0.0;

            _engineDouble.BeforeEvaluateExpression += (e, ctx) =>
            {
                beforeRaised = true;
                beforeExpr = e;
                Assert.AreSame(_ctxDouble, ctx, "Context instance passed to BeforeEvaluateExpression should match.");
            };

            _engineDouble.AfterEvaluateExpression += (e, ctx, result) =>
            {
                afterRaised = true;
                afterExpr = e;
                capturedResult = result;
                Assert.AreSame(_ctxDouble, ctx, "Context instance passed to AfterEvaluateExpression should match.");
            };

            double resultEval = _engineDouble.Evaluate(expr, _ctxDouble);

            Assert.IsTrue(beforeRaised, "BeforeEvaluateExpression should be raised.");
            Assert.IsTrue(afterRaised, "AfterEvaluateExpression should be raised.");
            Assert.AreEqual(expr, beforeExpr);
            Assert.AreEqual(expr, afterExpr);
            Assert.AreEqual(resultEval, capturedResult, DoubleTolerance);
            Assert.AreEqual(6.0, resultEval, DoubleTolerance);
        }

        [Test]
        public void Events_AreRaisedForCompiledExpressionReuse()
        {
            _ctxDouble.Variables["$x"] = 1.0;
            CompiledExpression<double> compiled = _engineDouble.Compile("$x * 2");

            int beforeCount = 0;
            int afterCount = 0;

            _engineDouble.BeforeEvaluateExpression += (_, __) => beforeCount++;
            _engineDouble.AfterEvaluateExpression += (_, __, ___) => afterCount++;

            // First evaluation
            compiled.Evaluate(_ctxDouble);

            // Second evaluation with different variable value
            _ctxDouble.Variables["$x"] = 5.0;
            compiled.Evaluate(_ctxDouble);

            Assert.AreEqual(2, beforeCount, "BeforeEvaluateExpression should be raised once per Evaluate call.");
            Assert.AreEqual(2, afterCount, "AfterEvaluateExpression should be raised once per Evaluate call.");
        }

        [Test]
        public void RegisterFunction_RegistersCSharpFunctionWithReturn()
        {
            DoubleNumberOperations ops = new();

            _engineDouble.RegisterFunction(_ctxDouble, "square", args =>
            {
                if (args.Count != 1) throw new ArgumentException("square expects one argument.");
                double v = ops.ToDouble(args[0]);
                return ops.FromDouble(v * v);
            });

            _ctxDouble.Variables["$x"] = 3.0;

            double result = _engineDouble.Evaluate("square($x) + 1", _ctxDouble);

            Assert.AreEqual(10.0, result, DoubleTolerance); // 3^2 + 1 = 10
        }

        [Test]
        public void RegisterVoidFunction_RegistersCSharpFunctionWithoutReturn()
        {
            bool called = false;

            _engineDouble.RegisterVoidFunction(_ctxDouble, "mark", args => { called = true; });

            // Function used alone; should return Zero at expression level.
            double resultAlone = _engineDouble.Evaluate("mark();", _ctxDouble);

            // Function used in a sequence; last expression dictates final result.
            double resultSequence = _engineDouble.Evaluate("mark(); 5", _ctxDouble);

            Assert.IsTrue(called, "Void function should have been called at least once.");
            Assert.AreEqual(0.0, resultAlone, DoubleTolerance, "Void function alone should return Zero.");
            Assert.AreEqual(5.0, resultSequence, DoubleTolerance, "Sequence should return value of last expression.");
        }

        [Test]
        public void RegisterExpressionFunction_WorksAndRestoresOuterVariables()
        {
            DoubleNumberOperations ops = new();

            // damage($atk, $def) = $atk * 2 - $def
            _engineDouble.RegisterExpressionFunction(
                _ctxDouble,
                "damage",
                new[] { "$atk", "$def" },
                "$atk * 2 - $def"
            );

            // Outer variables with same names, should be restored after call.
            _ctxDouble.Variables["$atk"] = 100.0;
            _ctxDouble.Variables["$def"] = 200.0;

            double result = _engineDouble.Evaluate("damage(10, 3)", _ctxDouble);

            // Check calculation
            Assert.AreEqual(17.0, result, DoubleTolerance);

            // Ensure outer variables are unchanged
            Assert.AreEqual(100.0, _ctxDouble.Variables["$atk"], DoubleTolerance);
            Assert.AreEqual(200.0, _ctxDouble.Variables["$def"], DoubleTolerance);
        }

        [Test]
        public void RegisterExpressionVoidFunction_HasSideEffectsOnNonParameterVariables()
        {
            DoubleNumberOperations ops = new();

            // storeDouble($value): $stored = $value * 2
            _engineDouble.RegisterExpressionVoidFunction(
                _ctxDouble,
                "storeDouble",
                new[] { "$value" },
                "$stored = $value * 2"
            );

            _ctxDouble.Variables["$value"] = 5.0;
            _ctxDouble.Variables["$stored"] = 0.0;

            double result = _engineDouble.Evaluate("storeDouble(7)", _ctxDouble);

            // Function is "void" at language level: returns Zero
            Assert.AreEqual(0.0, result, DoubleTolerance);

            // Parameter variable should remain unchanged in outer scope
            Assert.AreEqual(5.0, _ctxDouble.Variables["$value"], DoubleTolerance);

            // Non-parameter variable $stored should be updated by the body expression
            Assert.AreEqual(14.0, _ctxDouble.Variables["$stored"], DoubleTolerance);
        }

        #endregion

        #region Error handling and invalid expressions

        [Test]
        public void Compile_InvalidExpression_MissingOperand_Throws()
        {
            const string expression = "1 +";

            Assert.Throws<InvalidOperationException>(() => _engineDouble.Compile(expression));
        }

        [Test]
        public void Compile_InvalidExpression_MissingClosingParenthesis_Throws()
        {
            const string expression = "sin(1 + 2";

            Assert.Throws<InvalidOperationException>(() => _engineDouble.Compile(expression));
        }

        [Test]
        public void IdentifierUsedAsVariableWithoutDollar_Throws()
        {
            const string expression = "x + 1"; // "x" is not a function, so treated as variable -> must start with '$'

            Assert.Throws<InvalidOperationException>(() => _engineDouble.Compile(expression));
        }

        #endregion
    }
}
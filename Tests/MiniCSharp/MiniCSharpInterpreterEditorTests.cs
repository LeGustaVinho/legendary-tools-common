using System.Collections.Generic;
using System.Collections;
using NUnit.Framework;
using LegendaryTools.MiniCSharp;
using UnityEngine;

namespace LegendaryTools.Tests.MiniCSharp
{
    /// <summary>
    /// Editor tests for <see cref="MiniCSharpInterpreter"/>.
    /// </summary>
    public sealed class MiniCSharpInterpreterEditorTests
    {
        public sealed class List
        {
        }

        public enum ScriptState
        {
            Idle = 0,
            Running = 1,
            Completed = 2
        }

        public static class ScriptStateUtility
        {
            public static ScriptState Promote(ScriptState state)
            {
                switch (state)
                {
                    case ScriptState.Idle:
                        return ScriptState.Running;

                    case ScriptState.Running:
                        return ScriptState.Completed;

                    default:
                        return ScriptState.Completed;
                }
            }
        }

        public static class ScriptTypeUtility
        {
            public static bool AreSame(System.Type left, System.Type right)
            {
                return left == right;
            }

            public static string GetFriendlyName(System.Type type)
            {
                return type == null ? "null" : (type.FullName ?? type.Name);
            }
        }

        public sealed class ScriptCharacter
        {
            public static int DefaultHealth = 100;

            public static string StaticTitle { get; set; }

            public string Name;

            public int Health { get; set; }

            public bool IsInvincible { get; set; }

            public ScriptCharacter()
            {
                Name = "Unnamed";
                Health = DefaultHealth;
            }

            public ScriptCharacter(string name, int health)
            {
                Name = name;
                Health = health;
            }

            public int AddHealth(int amount)
            {
                Health += amount;
                return Health;
            }

            public int Damage(int amount)
            {
                Health -= amount;
                return Health;
            }

            public bool IsAlive()
            {
                return Health > 0 || IsInvincible;
            }

            public string Rename(string prefix, int index)
            {
                Name = prefix + index;
                return Name;
            }

            public static int ClampHealth(int health)
            {
                if (health < 0) return 0;

                if (health > DefaultHealth) return DefaultHealth;

                return health;
            }

            public static ScriptCharacter Create(string name, int health)
            {
                return new ScriptCharacter(name, health);
            }
        }

        public sealed class ReflectionTarget
        {
            public int PublicField;

            public float PublicFloatProperty { get; set; }

            public string PublicStringProperty { get; set; }

            public bool PublicBoolProperty { get; set; }
        }

        public struct ScriptStats
        {
            public int Health;

            public int Energy { get; set; }

            public void AddHealth(int amount)
            {
                Health += amount;
            }

            public static ScriptStats Create(int health, int energy)
            {
                ScriptStats stats = new ScriptStats();
                stats.Health = health;
                stats.Energy = energy;
                return stats;
            }
        }

        public interface IScriptEntity
        {
            int Health { get; set; }

            int Damage(int amount);
        }

        public abstract class ScriptEntityBase : IScriptEntity
        {
            public static int DefaultLives => 3;

            public string Name;

            public int Health { get; set; }

            protected ScriptEntityBase(string name, int health)
            {
                Name = name;
                Health = health;
            }

            public virtual int Damage(int amount)
            {
                Health -= amount;
                return Health;
            }
        }

        public sealed class ScriptWarrior : ScriptEntityBase
        {
            public ScriptWarrior(string name, int health) : base(name, health)
            {
            }

            public override int Damage(int amount)
            {
                Health -= amount * 2;
                return Health;
            }
        }

        public sealed class ScriptEventSource
        {
            public static event System.Action<string> GlobalEvent;

            public event System.Action<int> ValueChanged;

            public void RaiseValueChanged(int value)
            {
                ValueChanged?.Invoke(value);
            }

            public static void RaiseGlobalEvent(string message)
            {
                GlobalEvent?.Invoke(message);
            }

            public static void ClearGlobalHandlers()
            {
                GlobalEvent = null;
            }
        }

        public sealed class OverloadTarget
        {
            public string Select(int value)
            {
                return "int:" + value;
            }

            public string Select(string value)
            {
                return "string:" + value;
            }

            public string Select(float value)
            {
                return "float";
            }
        }

        public sealed class GenericTarget
        {
            public T Echo<T>(T value)
            {
                return value;
            }

            public static T Identity<T>(T value)
            {
                return value;
            }
        }

        [SetUp]
        public void SetUp()
        {
            ScriptCharacter.DefaultHealth = 100;
            ScriptCharacter.StaticTitle = null;
            ScriptEventSource.ClearGlobalHandlers();
        }

        [Test]
        public void Execute_WhenDeclaringBasicVariables_StoresExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int lives = 3;
                float speed = 4.5f;
                double weight = 7.25;
                bool active = true;
                string playerName = ""Ada"";
                var inferredValue = 12;
            ");

            Assert.AreEqual(3, interpreter.GetVariable<int>("lives"));
            Assert.AreEqual(4.5f, interpreter.GetVariable<float>("speed"), 0.0001f);
            Assert.AreEqual(7.25d, interpreter.GetVariable<double>("weight"), 0.0001d);
            Assert.IsTrue(interpreter.GetVariable<bool>("active"));
            Assert.AreEqual("Ada", interpreter.GetVariable<string>("playerName"));
            Assert.AreEqual(12, interpreter.GetVariable<int>("inferredValue"));
        }

        [Test]
        public void Execute_WhenReadingAndWritingVariables_UpdatesExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.SetVariable("health", 100);
            interpreter.SetVariable("playerName", "Knight");

            interpreter.Execute(@"
                health = health - 25;
                playerName = playerName + "" Level 2"";
            ");

            Assert.AreEqual(75, interpreter.GetVariable<int>("health"));
            Assert.AreEqual("Knight Level 2", interpreter.GetVariable<string>("playerName"));
        }

        [Test]
        public void Execute_WhenUsingStringInterpolation_ProducesExpectedText()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.SetVariable("playerName", "Ada");
            interpreter.SetVariable("health", 75);

            interpreter.Execute(@"
                string message = $""Player {playerName} has {health} HP."";
            ");

            Assert.AreEqual("Player Ada has 75 HP.", interpreter.GetVariable<string>("message"));
        }

        [Test]
        public void Execute_WhenUsingStringInterpolationWithExpressionsAndEscapedBraces_ProducesExpectedText()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                var character = new ScriptCharacter(""Hero"", 10);
                int[] values = new int[2];
                values[0] = 3;
                values[1] = 4;

                string message = $""{{Name={character.Name}, Total={values[0] + values[1]}}}"";
            ");

            Assert.AreEqual("{Name=Hero, Total=7}", interpreter.GetVariable<string>("message"));
        }

        [Test]
        public void Execute_WhenUsingNullCoalescing_ReturnsExpectedFallbacks()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                string missingName = null ?? ""Unknown"";
                string presentName = ""Ada"" ?? ""Unknown"";
                int score = null ?? 10;
            ");

            Assert.AreEqual("Unknown", interpreter.GetVariable<string>("missingName"));
            Assert.AreEqual("Ada", interpreter.GetVariable<string>("presentName"));
            Assert.AreEqual(10, interpreter.GetVariable<int>("score"));
        }

        [Test]
        public void Execute_WhenUsingNullConditionalMemberAndMethod_ReturnsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                ScriptCharacter missing = null;
                ScriptCharacter present = new ScriptCharacter(""Hero"", 10);

                string missingName = missing?.Name ?? ""Unknown"";
                string presentName = present?.Name ?? ""Unknown"";
                bool missingAlive = missing?.IsAlive() ?? false;
                bool presentAlive = present?.IsAlive() ?? false;
            ");

            Assert.AreEqual("Unknown", interpreter.GetVariable<string>("missingName"));
            Assert.AreEqual("Hero", interpreter.GetVariable<string>("presentName"));
            Assert.IsFalse(interpreter.GetVariable<bool>("missingAlive"));
            Assert.IsTrue(interpreter.GetVariable<bool>("presentAlive"));
        }

        [Test]
        public void Execute_WhenUsingNullConditionalIndexAccess_ReturnsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                List<int> missing = null;
                List<int> present = [3, 4, 5];

                int missingFirst = missing?[0] ?? -1;
                int presentFirst = present?[0] ?? -1;
            ");

            Assert.AreEqual(-1, interpreter.GetVariable<int>("missingFirst"));
            Assert.AreEqual(3, interpreter.GetVariable<int>("presentFirst"));
        }

        [Test]
        public void Execute_WhenUsingIfTrueBranch_ExecutesThenBranch()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int level = 5;
                int score = 0;

                if (level >= 5)
                {
                    score = 10;
                }
                else
                {
                    score = -10;
                }
            ");

            Assert.AreEqual(10, interpreter.GetVariable<int>("score"));
        }

        [Test]
        public void Execute_WhenUsingIfFalseBranch_ExecutesElseBranch()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int level = 2;
                int score = 0;

                if (level >= 5)
                {
                    score = 10;
                }
                else
                {
                    score = -10;
                }
            ");

            Assert.AreEqual(-10, interpreter.GetVariable<int>("score"));
        }

        [Test]
        public void Execute_WhenUsingElseIf_ExecutesExpectedBranch()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int level = 3;
                int score = 0;

                if (level >= 5)
                {
                    score = 10;
                }
                else if (level >= 3)
                {
                    score = 5;
                }
                else
                {
                    score = -10;
                }
            ");

            Assert.AreEqual(5, interpreter.GetVariable<int>("score"));
        }

        [Test]
        public void Execute_WhenUsingTryCatch_HandlesScriptExceptionAndContinues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int value = 0;
                string message = """";

                try
                {
                    int result = 10 / 0;
                    value = 99;
                }
                catch (Exception ex)
                {
                    value = 1;
                    message = ex.Message;
                }

                value += 1;
            ");

            Assert.AreEqual(2, interpreter.GetVariable<int>("value"));
            StringAssert.Contains("Division by zero", interpreter.GetVariable<string>("message"));
        }

        [Test]
        public void Execute_WhenUsingTryCatchFinally_RunsFinallyAfterCatch()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                string trace = """";

                try
                {
                    trace += ""try;"";
                    int result = 10 / 0;
                }
                catch (Exception ex)
                {
                    trace += ""catch;"";
                }
                finally
                {
                    trace += ""finally;"";
                }
            ");

            Assert.AreEqual("try;catch;finally;", interpreter.GetVariable<string>("trace"));
        }

        [Test]
        public void Execute_WhenUsingTryFinallyWithReturn_RunsFinallyBeforeStoppingScript()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int value = 0;

                try
                {
                    value = 1;
                    return;
                }
                finally
                {
                    value = 2;
                }

                value = 999;
            ");

            Assert.AreEqual(2, interpreter.GetVariable<int>("value"));
        }

        [Test]
        public void Execute_WhenUsingSwitchCase_ExecutesMatchingSection()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int level = 2;
                int score = 0;

                switch (level)
                {
                    case 1:
                        score = 10;
                        break;

                    case 2:
                        score = 20;
                        break;

                    default:
                        score = -1;
                        break;
                }
            ");

            Assert.AreEqual(20, interpreter.GetVariable<int>("score"));
        }

        [Test]
        public void Execute_WhenUsingSwitchDefault_ExecutesFallbackSection()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                string state = ""Paused"";
                int score = 0;

                switch (state)
                {
                    case ""Idle"":
                        score = 1;
                        break;

                    case ""Running"":
                        score = 2;
                        break;

                    default:
                        score = 99;
                        break;
                }
            ");

            Assert.AreEqual(99, interpreter.GetVariable<int>("score"));
        }

        [Test]
        public void Execute_WhenUsingSwitchWithMultipleCaseLabels_MatchesExpectedSharedSection()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                ScriptState state = ScriptState.Running;
                int score = 0;

                switch (state)
                {
                    case ScriptState.Idle:
                    case ScriptState.Running:
                        score = 7;
                        break;

                    default:
                        score = -1;
                        break;
                }
            ");

            Assert.AreEqual(7, interpreter.GetVariable<int>("score"));
        }

        [Test]
        public void Execute_WhenUsingScriptDeclaredFunction_ReturnsExpectedValue()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int Sum(int a, int b)
                {
                    return a + b;
                }

                int total = Sum(3, 4);
            ");

            Assert.AreEqual(7, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingVoidScriptDeclaredFunction_MutatesExpectedValue()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 1;

                void AddToTotal(int amount)
                {
                    total += amount;
                    return;
                }

                AddToTotal(4);
                AddToTotal(5);
            ");

            Assert.AreEqual(10, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingRecursiveScriptDeclaredFunction_ReturnsExpectedValue()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int Factorial(int value)
                {
                    if (value <= 1)
                    {
                        return 1;
                    }

                    return value * Factorial(value - 1);
                }

                int result = Factorial(5);
            ");

            Assert.AreEqual(120, interpreter.GetVariable<int>("result"));
        }

        [Test]
        public void Execute_WhenUsingLambdaAssignedToFunc_ReturnsExpectedValue()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                System.Func<int, int> doubleValue = (value) => value * 2;
                int result = doubleValue(6);
            ");

            Assert.AreEqual(12, interpreter.GetVariable<int>("result"));
        }

        [Test]
        public void Execute_WhenUsingLambdaWithCapturedVariable_ReflectsUpdatedState()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 5;
                System.Func<int> readTotal = () => total;

                total += 3;
                int result = readTotal();
            ");

            Assert.AreEqual(8, interpreter.GetVariable<int>("result"));
        }

        [Test]
        public void Execute_WhenUsingLambdaCapturingBlockScopedVariable_PreservesCapturedValueAfterScopeEnds()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                System.Func<int> readValue;

                {
                    int localValue = 7;
                    readValue = () => localValue;
                }

                int result = readValue();
            ");

            Assert.AreEqual(7, interpreter.GetVariable<int>("result"));
        }

        [Test]
        public void Execute_WhenUsingLambdaAssignedToAction_MutatesExpectedState()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 1;
                System.Action<int> addToTotal = (value) => total += value;

                addToTotal(4);
                addToTotal(5);
            ");

            Assert.AreEqual(10, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingLambdaWithWaitUntil_CreatesExpectedPredicate()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int health = 2;
                UnityEngine.WaitUntil wait = new UnityEngine.WaitUntil(() => health <= 0);

                bool before = wait.keepWaiting;
                health = 0;
                bool after = wait.keepWaiting;
            ");

            Assert.IsTrue(interpreter.GetVariable<bool>("before"));
            Assert.IsFalse(interpreter.GetVariable<bool>("after"));
        }

        [Test]
        public void Execute_WhenUsingForLoop_CalculatesExpectedTotal()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 0;

                for (int i = 0; i < 6; i++)
                {
                    total = total + i;
                }
            ");

            Assert.AreEqual(15, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingForLoopWithDecrement_CalculatesExpectedTotal()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 0;

                for (int i = 5; i > 0; i--)
                {
                    total = total + i;
                }
            ");

            Assert.AreEqual(15, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingWhileLoop_CalculatesExpectedTotal()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 0;
                int i = 0;

                while (i < 6)
                {
                    total = total + i;
                    i++;
                }
            ");

            Assert.AreEqual(15, interpreter.GetVariable<int>("total"));
            Assert.AreEqual(6, interpreter.GetVariable<int>("i"));
        }

        [Test]
        public void Execute_WhenUsingForeachWithArray_CalculatesExpectedTotal()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int[] values = new int[4];
                values[0] = 1;
                values[1] = 2;
                values[2] = 3;
                values[3] = 4;

                int total = 0;

                foreach (int value in values)
                {
                    total += value;
                }
            ");

            Assert.AreEqual(10, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingForeachWithListAndVar_SupportsContinueAndBreak()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                List<int> values = new List<int>();
                values.Add(1);
                values.Add(2);
                values.Add(3);
                values.Add(4);
                values.Add(5);

                int total = 0;

                foreach (var value in values)
                {
                    if (value == 2)
                    {
                        continue;
                    }

                    if (value == 5)
                    {
                        break;
                    }

                    total += value;
                }
            ");

            Assert.AreEqual(8, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingForeachWithDictionary_KeyValuePairsAreAccessible()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                Dictionary<string, int> values = new Dictionary<string, int>();
                values[""hp""] = 10;
                values[""mp""] = 5;

                int total = 0;
                string keys = """";

                foreach (System.Collections.Generic.KeyValuePair<string, int> pair in values)
                {
                    total += pair.Value;
                    keys += pair.Key;
                }
            ");

            Assert.AreEqual(15, interpreter.GetVariable<int>("total"));
            string keys = interpreter.GetVariable<string>("keys");
            Assert.IsTrue(keys.Contains("hp"));
            Assert.IsTrue(keys.Contains("mp"));
        }

        [Test]
        public void Execute_WhenUsingBreakInsideWhile_StopsLoopEarly()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 0;
                int i = 0;

                while (i < 10)
                {
                    if (i == 4)
                    {
                        break;
                    }

                    total = total + i;
                    i++;
                }
            ");

            Assert.AreEqual(6, interpreter.GetVariable<int>("total"));
            Assert.AreEqual(4, interpreter.GetVariable<int>("i"));
        }

        [Test]
        public void Execute_WhenUsingContinueInsideFor_SkipsExpectedIterations()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 0;

                for (int i = 0; i < 6; i++)
                {
                    if (i == 2 || i == 4)
                    {
                        continue;
                    }

                    total = total + i;
                }
            ");

            Assert.AreEqual(9, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingContinueInsideWhile_SkipsExpectedIterations()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 0;
                int i = 0;

                while (i < 6)
                {
                    i++;

                    if (i == 2 || i == 4)
                    {
                        continue;
                    }

                    total = total + i;
                }
            ");

            Assert.AreEqual(15, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingArrayAndIndexer_ReadsAndWritesExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int[] values = new int[3];

                values[0] = 10;
                values[1] = 20;
                values[2] = values[0] + values[1];

                int total = 0;

                for (int i = 0; i < values.Length; i++)
                {
                    total = total + values[i];
                }
            ");

            int[] values = interpreter.GetVariable<int[]>("values");

            Assert.AreEqual(3, values.Length);
            Assert.AreEqual(10, values[0]);
            Assert.AreEqual(20, values[1]);
            Assert.AreEqual(30, values[2]);
            Assert.AreEqual(60, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingGenericListAndIndexer_ReadsAndWritesExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                List<int> values = new List<int>();
                values.Add(3);
                values.Add(5);
                values[1] = values[0] + values[1];

                int first = values[0];
                int second = values[1];
                int count = values.Count;
            ");

            System.Collections.Generic.List<int> values = interpreter.GetVariable<System.Collections.Generic.List<int>>("values");

            Assert.AreEqual(2, values.Count);
            Assert.AreEqual(3, values[0]);
            Assert.AreEqual(8, values[1]);
            Assert.AreEqual(3, interpreter.GetVariable<int>("first"));
            Assert.AreEqual(8, interpreter.GetVariable<int>("second"));
            Assert.AreEqual(2, interpreter.GetVariable<int>("count"));
        }

        [Test]
        public void Execute_WhenUsingNamespacedGenericListType_CreatesExpectedInstance()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
                names.Add(""Ada"");
                names.Add(""Grace"");
                string combined = names[0] + "" & "" + names[1];
            ");

            System.Collections.Generic.List<string> names = interpreter.GetVariable<System.Collections.Generic.List<string>>("names");

            Assert.AreEqual(2, names.Count);
            Assert.AreEqual("Ada", names[0]);
            Assert.AreEqual("Grace", names[1]);
            Assert.AreEqual("Ada & Grace", interpreter.GetVariable<string>("combined"));
        }

        [Test]
        public void Execute_WhenUsingArrayLiteral_StoresExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int[] values = [1, 2, 3];
                int total = values[0] + values[1] + values[2];
            ");

            int[] values = interpreter.GetVariable<int[]>("values");

            Assert.AreEqual(3, values.Length);
            Assert.AreEqual(1, values[0]);
            Assert.AreEqual(2, values[1]);
            Assert.AreEqual(3, values[2]);
            Assert.AreEqual(6, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingVarWithArrayLiteral_InfersArrayType()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                var values = [1, 2, 3];
                int total = 0;

                foreach (int value in values)
                {
                    total += value;
                }
            ");

            int[] values = interpreter.GetVariable<int[]>("values");

            Assert.AreEqual(3, values.Length);
            Assert.AreEqual(6, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingListLiteral_ConvertsToExpectedList()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                List<int> values = [1, 2, 3];
                values.Add(4);

                int total = 0;

                foreach (int value in values)
                {
                    total += value;
                }
            ");

            System.Collections.Generic.List<int> values =
                interpreter.GetVariable<System.Collections.Generic.List<int>>("values");

            Assert.AreEqual(4, values.Count);
            Assert.AreEqual(10, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenUsingGenericDictionaryAndIndexer_ReadsAndWritesExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                Dictionary<string, int> values = new Dictionary<string, int>();
                values.Add(""hp"", 10);
                values[""mp""] = 5;
                values[""hp""] += values[""mp""];

                int hp = values[""hp""];
                int mp = values[""mp""];
                int count = values.Count;
            ");

            System.Collections.Generic.Dictionary<string, int> values =
                interpreter.GetVariable<System.Collections.Generic.Dictionary<string, int>>("values");

            Assert.AreEqual(2, values.Count);
            Assert.AreEqual(15, values["hp"]);
            Assert.AreEqual(5, values["mp"]);
            Assert.AreEqual(15, interpreter.GetVariable<int>("hp"));
            Assert.AreEqual(5, interpreter.GetVariable<int>("mp"));
            Assert.AreEqual(2, interpreter.GetVariable<int>("count"));
        }

        [Test]
        public void Execute_WhenUsingNamespacedGenericDictionaryType_CreatesExpectedInstance()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                System.Collections.Generic.Dictionary<string, string> names =
                    new System.Collections.Generic.Dictionary<string, string>();

                names[""first""] = ""Ada"";
                names[""last""] = ""Lovelace"";

                string combined = names[""first""] + "" "" + names[""last""];
            ");

            System.Collections.Generic.Dictionary<string, string> names =
                interpreter.GetVariable<System.Collections.Generic.Dictionary<string, string>>("names");

            Assert.AreEqual(2, names.Count);
            Assert.AreEqual("Ada", names["first"]);
            Assert.AreEqual("Lovelace", names["last"]);
            Assert.AreEqual("Ada Lovelace", interpreter.GetVariable<string>("combined"));
        }

        [Test]
        public void Execute_WhenUsingCompoundAssignment_ProducesExpectedResults()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int value = 10;
                value += 5;
                value -= 3;
                value *= 4;
                value /= 6;
                value %= 5;

                string label = ""A"";
                label += ""B"";

                int[] values = new int[2];
                values[0] = 2;
                values[0] += 3;
            ");

            Assert.AreEqual(3, interpreter.GetVariable<int>("value"));
            Assert.AreEqual("AB", interpreter.GetVariable<string>("label"));
            Assert.AreEqual(5, interpreter.GetVariable<int[]>("values")[0]);
        }

        [Test]
        public void Execute_WhenUsingMathOperators_ProducesExpectedResults()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int precedence = 2 + 3 * 4;
                int grouped = (2 + 3) * 4;
                int modulo = 17 % 5;
                int unary = -10 + 3;
                int integerDivision = 5 / 2;
                float floatDivision = 5f / 2f;

                int counter = 1;
                counter++;
                ++counter;
                counter--;
                --counter;
            ");

            Assert.AreEqual(14, interpreter.GetVariable<int>("precedence"));
            Assert.AreEqual(20, interpreter.GetVariable<int>("grouped"));
            Assert.AreEqual(2, interpreter.GetVariable<int>("modulo"));
            Assert.AreEqual(-7, interpreter.GetVariable<int>("unary"));
            Assert.AreEqual(2, interpreter.GetVariable<int>("integerDivision"));
            Assert.AreEqual(2.5f, interpreter.GetVariable<float>("floatDivision"), 0.0001f);
            Assert.AreEqual(1, interpreter.GetVariable<int>("counter"));
        }

        [Test]
        public void Execute_WhenUsingLogicalOperators_ProducesExpectedResults()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                bool andResult = true && false;
                bool orResult = true || false;
                bool notResult = !false;

                bool comparisonA = 5 > 3;
                bool comparisonB = 5 >= 5;
                bool comparisonC = 2 < 4;
                bool comparisonD = 2 <= 2;
                bool comparisonE = 10 == 10;
                bool comparisonF = 10 != 20;

                bool combined = comparisonA && comparisonB && comparisonC && comparisonD && comparisonE && comparisonF;
                bool failedCombined = false || (5 < 3);
            ");

            Assert.IsFalse(interpreter.GetVariable<bool>("andResult"));
            Assert.IsTrue(interpreter.GetVariable<bool>("orResult"));
            Assert.IsTrue(interpreter.GetVariable<bool>("notResult"));
            Assert.IsTrue(interpreter.GetVariable<bool>("comparisonA"));
            Assert.IsTrue(interpreter.GetVariable<bool>("comparisonB"));
            Assert.IsTrue(interpreter.GetVariable<bool>("comparisonC"));
            Assert.IsTrue(interpreter.GetVariable<bool>("comparisonD"));
            Assert.IsTrue(interpreter.GetVariable<bool>("comparisonE"));
            Assert.IsTrue(interpreter.GetVariable<bool>("comparisonF"));
            Assert.IsTrue(interpreter.GetVariable<bool>("combined"));
            Assert.IsFalse(interpreter.GetVariable<bool>("failedCombined"));
        }

        [Test]
        public void Execute_WhenUsingEnumValues_StoresAndComparesExpectedResults()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                ScriptState state = ScriptState.Running;
                bool isRunning = state == ScriptState.Running;

                state = ScriptState.Completed;
                bool isCompleted = state == ScriptState.Completed;
                int numericValue = ScriptState.Completed;
            ");

            Assert.AreEqual(ScriptState.Completed, interpreter.GetVariable<ScriptState>("state"));
            Assert.IsTrue(interpreter.GetVariable<bool>("isRunning"));
            Assert.IsTrue(interpreter.GetVariable<bool>("isCompleted"));
            Assert.AreEqual(2, interpreter.GetVariable<int>("numericValue"));
        }

        [Test]
        public void Execute_WhenAssigningEnumFromStringAndInteger_ConvertsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                ScriptState fromString = ""Running"";
                ScriptState fromInteger = 2;

                bool stringMatched = fromString == ScriptState.Running;
                bool integerMatched = fromInteger == ScriptState.Completed;
            ");

            Assert.AreEqual(ScriptState.Running, interpreter.GetVariable<ScriptState>("fromString"));
            Assert.AreEqual(ScriptState.Completed, interpreter.GetVariable<ScriptState>("fromInteger"));
            Assert.IsTrue(interpreter.GetVariable<bool>("stringMatched"));
            Assert.IsTrue(interpreter.GetVariable<bool>("integerMatched"));
        }

        [Test]
        public void Execute_WhenPassingAndReturningEnumThroughMethods_ReturnsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                ScriptState promotedFromIdle = ScriptStateUtility.Promote(ScriptState.Idle);
                ScriptState promotedFromRunning = ScriptStateUtility.Promote(ScriptState.Running);

                bool idlePromoted = promotedFromIdle == ScriptState.Running;
                bool runningPromoted = promotedFromRunning == ScriptState.Completed;
            ");

            Assert.AreEqual(ScriptState.Running, interpreter.GetVariable<ScriptState>("promotedFromIdle"));
            Assert.AreEqual(ScriptState.Completed, interpreter.GetVariable<ScriptState>("promotedFromRunning"));
            Assert.IsTrue(interpreter.GetVariable<bool>("idlePromoted"));
            Assert.IsTrue(interpreter.GetVariable<bool>("runningPromoted"));
        }

        [Test]
        public void Execute_WhenUsingExplicitCast_ReturnsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                object value = new ScriptWarrior(""Rhea"", 20);
                ScriptEntityBase asBase = (ScriptEntityBase)value;
                IScriptEntity asInterface = (IScriptEntity)value;
                int enumNumber = (int)ScriptState.Completed;
            ");

            Assert.IsInstanceOf<ScriptWarrior>(interpreter.GetVariable<ScriptEntityBase>("asBase"));
            Assert.IsInstanceOf<ScriptWarrior>(interpreter.GetVariable<IScriptEntity>("asInterface"));
            Assert.AreEqual(2, interpreter.GetVariable<int>("enumNumber"));
        }

        [Test]
        public void Execute_WhenUsingIsOperator_ReturnsExpectedBooleans()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                object warrior = new ScriptWarrior(""Rhea"", 20);
                object missing = null;

                bool isBase = warrior is ScriptEntityBase;
                bool isInterface = warrior is IScriptEntity;
                bool isCharacter = warrior is ScriptCharacter;
                bool isNullBase = missing is ScriptEntityBase;
            ");

            Assert.IsTrue(interpreter.GetVariable<bool>("isBase"));
            Assert.IsTrue(interpreter.GetVariable<bool>("isInterface"));
            Assert.IsFalse(interpreter.GetVariable<bool>("isCharacter"));
            Assert.IsFalse(interpreter.GetVariable<bool>("isNullBase"));
        }

        [Test]
        public void Execute_WhenUsingAsOperator_ReturnsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                object warrior = new ScriptWarrior(""Rhea"", 20);
                object character = new ScriptCharacter(""Hero"", 10);
                object missing = null;

                ScriptEntityBase baseValue = warrior as ScriptEntityBase;
                IScriptEntity interfaceValue = warrior as IScriptEntity;
                ScriptEntityBase incompatibleValue = character as ScriptEntityBase;
                ScriptEntityBase missingValue = missing as ScriptEntityBase;
            ");

            Assert.IsInstanceOf<ScriptWarrior>(interpreter.GetVariable<ScriptEntityBase>("baseValue"));
            Assert.IsInstanceOf<ScriptWarrior>(interpreter.GetVariable<IScriptEntity>("interfaceValue"));
            Assert.IsNull(interpreter.GetVariable<ScriptEntityBase>("incompatibleValue"));
            Assert.IsNull(interpreter.GetVariable<ScriptEntityBase>("missingValue"));
        }

        [Test]
        public void Execute_WhenUsingAsOperatorWithNonNullableValueType_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            ScriptException exception = Assert.Throws<ScriptException>(() =>
                interpreter.Execute(@"
                    object value = 5;
                    int converted = value as int;
                "));

            StringAssert.Contains("requires a reference or nullable type", exception.Message);
        }

        [Test]
        public void Execute_WhenUsingTypeOf_ReturnsExpectedTypes()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                Type intType = typeof(int);
                Type listType = typeof(List<int>);
                Type arrayType = typeof(ScriptCharacter[]);

                bool intMatches = intType == typeof(int);
                bool listMatches = listType == typeof(System.Collections.Generic.List<int>);
                bool arrayMatches = arrayType == typeof(ScriptCharacter[]);
            ");

            Assert.AreEqual(typeof(int), interpreter.GetVariable<System.Type>("intType"));
            Assert.AreEqual(typeof(System.Collections.Generic.List<int>), interpreter.GetVariable<System.Type>("listType"));
            Assert.AreEqual(typeof(ScriptCharacter[]), interpreter.GetVariable<System.Type>("arrayType"));
            Assert.IsTrue(interpreter.GetVariable<bool>("intMatches"));
            Assert.IsTrue(interpreter.GetVariable<bool>("listMatches"));
            Assert.IsTrue(interpreter.GetVariable<bool>("arrayMatches"));
        }

        [Test]
        public void Execute_WhenPassingTypeOfToMethod_ReturnsExpectedValue()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                string intName = ScriptTypeUtility.GetFriendlyName(typeof(int));
                bool sameWarriorType = ScriptTypeUtility.AreSame(typeof(ScriptWarrior), typeof(ScriptWarrior));
                bool differentTypes = ScriptTypeUtility.AreSame(typeof(ScriptWarrior), typeof(ScriptCharacter));
            ");

            Assert.AreEqual(typeof(int).FullName, interpreter.GetVariable<string>("intName"));
            Assert.IsTrue(interpreter.GetVariable<bool>("sameWarriorType"));
            Assert.IsFalse(interpreter.GetVariable<bool>("differentTypes"));
        }

        [Test]
        public void Execute_WhenUsingReflectionObject_ReadsAndWritesPublicFieldsAndProperties()
        {
            MiniCSharpInterpreter interpreter = new();

            ReflectionTarget target = new()
            {
                PublicField = 3,
                PublicFloatProperty = 2.5f,
                PublicStringProperty = "Start",
                PublicBoolProperty = false
            };

            interpreter.RegisterObject("target", target);

            interpreter.Execute(@"
                int fieldCopy = target.PublicField;

                target.PublicField = fieldCopy + 7;
                target.PublicFloatProperty = target.PublicFloatProperty * 2f;
                target.PublicStringProperty = target.PublicStringProperty + "" Done"";
                target.PublicBoolProperty = true;
            ");

            Assert.AreEqual(10, target.PublicField);
            Assert.AreEqual(5f, target.PublicFloatProperty, 0.0001f);
            Assert.AreEqual("Start Done", target.PublicStringProperty);
            Assert.IsTrue(target.PublicBoolProperty);
            Assert.AreEqual(3, interpreter.GetVariable<int>("fieldCopy"));
        }

        [Test]
        public void Execute_WhenUsingReflectionWithUnityObject_ReadsAndWritesUnityProperty()
        {
            MiniCSharpInterpreter interpreter = new();
            GameObject gameObject = new("Before");

            try
            {
                interpreter.RegisterObject("go", gameObject);

                interpreter.Execute(@"
                    go.name = ""After"";
                    string objectName = go.name;
                ");

                Assert.AreEqual("After", gameObject.name);
                Assert.AreEqual("After", interpreter.GetVariable<string>("objectName"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Execute_WhenAllocatingClassWithoutRegisteringType_CreatesExpectedInstance()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                var character = new ScriptCharacter(""Hero"", 25);

                string characterName = character.Name;
                int characterHealth = character.Health;
                bool isAlive = character.IsAlive();
            ");

            object characterObject = interpreter.GetVariable<object>("character");

            Assert.IsInstanceOf<ScriptCharacter>(characterObject);

            ScriptCharacter character = (ScriptCharacter)characterObject;

            Assert.AreEqual("Hero", character.Name);
            Assert.AreEqual(25, character.Health);
            Assert.AreEqual("Hero", interpreter.GetVariable<string>("characterName"));
            Assert.AreEqual(25, interpreter.GetVariable<int>("characterHealth"));
            Assert.IsTrue(interpreter.GetVariable<bool>("isAlive"));
        }

        [Test]
        public void Execute_WhenAllocatingClassWithDefaultConstructor_UsesDefaultConstructorValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                var character = new ScriptCharacter();

                string characterName = character.Name;
                int characterHealth = character.Health;
            ");

            object characterObject = interpreter.GetVariable<object>("character");

            Assert.IsInstanceOf<ScriptCharacter>(characterObject);

            ScriptCharacter character = (ScriptCharacter)characterObject;

            Assert.AreEqual("Unnamed", character.Name);
            Assert.AreEqual(100, character.Health);
            Assert.AreEqual("Unnamed", interpreter.GetVariable<string>("characterName"));
            Assert.AreEqual(100, interpreter.GetVariable<int>("characterHealth"));
        }

        [Test]
        public void Execute_WhenAllocatingStructWithDefaultConstructor_UsesDefaultValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                ScriptStats stats = new ScriptStats();

                int initialHealth = stats.Health;
                int initialEnergy = stats.Energy;
            ");

            ScriptStats stats = interpreter.GetVariable<ScriptStats>("stats");

            Assert.AreEqual(0, stats.Health);
            Assert.AreEqual(0, stats.Energy);
            Assert.AreEqual(0, interpreter.GetVariable<int>("initialHealth"));
            Assert.AreEqual(0, interpreter.GetVariable<int>("initialEnergy"));
        }

        [Test]
        public void Execute_WhenUsingStructMembersAndMethods_PersistsMutations()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                ScriptStats stats;
                stats.Health = 10;
                stats.Energy = 4;
                stats.AddHealth(7);
                stats.Energy += 3;

                int health = stats.Health;
                int energy = stats.Energy;
            ");

            ScriptStats stats = interpreter.GetVariable<ScriptStats>("stats");

            Assert.AreEqual(17, stats.Health);
            Assert.AreEqual(7, stats.Energy);
            Assert.AreEqual(17, interpreter.GetVariable<int>("health"));
            Assert.AreEqual(7, interpreter.GetVariable<int>("energy"));
        }

        [Test]
        public void Execute_WhenUsingStructFactoryMethod_StoresExpectedValue()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                ScriptStats stats = ScriptStats.Create(8, 2);
                stats.AddHealth(5);

                int health = stats.Health;
                int energy = stats.Energy;
            ");

            ScriptStats stats = interpreter.GetVariable<ScriptStats>("stats");

            Assert.AreEqual(13, stats.Health);
            Assert.AreEqual(2, stats.Energy);
            Assert.AreEqual(13, interpreter.GetVariable<int>("health"));
            Assert.AreEqual(2, interpreter.GetVariable<int>("energy"));
        }

        [Test]
        public void Execute_WhenUsingScriptDeclaredFunctionWithRuntimeTypes_ReturnsExpectedValue()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int DamageCharacter(ScriptCharacter character, int amount)
                {
                    return character.Damage(amount);
                }

                var character = new ScriptCharacter(""Hero"", 20);
                int remaining = DamageCharacter(character, 6);
            ");

            ScriptCharacter character = interpreter.GetVariable<ScriptCharacter>("character");

            Assert.AreEqual(14, character.Health);
            Assert.AreEqual(14, interpreter.GetVariable<int>("remaining"));
        }

        [Test]
        public void Execute_WhenUsingLocalFunctionBeforeDeclaration_ReturnsExpectedValue()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int Calculate(int value)
                {
                    return DoubleValue(value) + 1;

                    int DoubleValue(int current)
                    {
                        return current * 2;
                    }
                }

                int result = Calculate(3);
            ");

            Assert.AreEqual(7, interpreter.GetVariable<int>("result"));
        }

        [Test]
        public void Execute_WhenUsingNestedLocalFunction_CapturesOuterVariables()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int Calculate(int value)
                {
                    int bonus = 4;
                    return AddBonus(value);

                    int AddBonus(int current)
                    {
                        return current + bonus;
                    }
                }

                int result = Calculate(6);
            ");

            Assert.AreEqual(10, interpreter.GetVariable<int>("result"));
        }

        [Test]
        public void InvokeFunction_WhenCallingScriptDeclaredFunctionFromCSharp_ReturnsExpectedValue()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int Sum(int left, int right)
                {
                    return left + right;
                }
            ");

            int result = interpreter.InvokeFunction<int>("Sum", 3, 4);

            Assert.AreEqual(7, result);
        }

        [Test]
        public void InvokeFunction_WhenCallingVoidScriptDeclaredFunctionFromCSharp_UpdatesExpectedState()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 0;

                void AddToTotal(int value)
                {
                    total += value;
                }
            ");

            object result = interpreter.InvokeFunction("AddToTotal", 5);

            Assert.IsNull(result);
            Assert.AreEqual(5, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void InvokeFunction_WhenCallingCoroutineFunction_ReturnsExpectedEnumerator()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                IEnumerator Routine()
                {
                    yield return 3;
                    yield return null;
                    yield break;
                }
            ");

            IEnumerator routine = interpreter.InvokeFunction<IEnumerator>("Routine");

            Assert.IsTrue(routine.MoveNext());
            Assert.AreEqual(3, routine.Current);
            Assert.IsTrue(routine.MoveNext());
            Assert.IsNull(routine.Current);
            Assert.IsFalse(routine.MoveNext());
        }

        [Test]
        public void InvokeFunction_WhenCallingCoroutineFunctionWithLoop_YieldsExpectedSequence()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                IEnumerator CountToThree()
                {
                    for (int i = 1; i <= 3; i += 1)
                    {
                        yield return i;
                    }
                }
            ");

            IEnumerator routine = interpreter.InvokeFunction<IEnumerator>("CountToThree");

            Assert.IsTrue(routine.MoveNext());
            Assert.AreEqual(1, routine.Current);
            Assert.IsTrue(routine.MoveNext());
            Assert.AreEqual(2, routine.Current);
            Assert.IsTrue(routine.MoveNext());
            Assert.AreEqual(3, routine.Current);
            Assert.IsFalse(routine.MoveNext());
        }

        [Test]
        public void Execute_WhenCallingRegisteredDelegates_ReturnsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();
            int total = 0;

            interpreter.SetVariable("sum", new System.Func<int, int, int>((a, b) => a + b));
            interpreter.SetVariable("push", new System.Action<int>(value => total += value));

            interpreter.Execute(@"
                int result = sum(3, 4);
                push(5);
                push(7);
            ");

            Assert.AreEqual(7, interpreter.GetVariable<int>("result"));
            Assert.AreEqual(12, total);
        }

        [Test]
        public void Execute_WhenCallingOverloadedMethodWithDifferentArgumentSignatures_ReturnsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();
            OverloadTarget target = new();

            interpreter.RegisterObject("target", target);

            interpreter.Execute(@"
                string intResultA = target.Select(1);
                string stringResult = target.Select(""Ada"");
                string floatResult = target.Select(2.5f);
                string intResultB = target.Select(3);
            ");

            Assert.AreEqual("int:1", interpreter.GetVariable<string>("intResultA"));
            Assert.AreEqual("string:Ada", interpreter.GetVariable<string>("stringResult"));
            Assert.AreEqual("float", interpreter.GetVariable<string>("floatResult"));
            Assert.AreEqual("int:3", interpreter.GetVariable<string>("intResultB"));
        }

        [Test]
        public void Execute_WhenSubscribingScriptFunctionToInstanceEvent_HandlesAndUnsubscribesExpectedHandler()
        {
            MiniCSharpInterpreter interpreter = new();
            ScriptEventSource source = new();

            interpreter.RegisterObject("source", source);

            interpreter.Execute(@"
                int total = 0;

                void HandleValueChanged(int value)
                {
                    total += value;
                }

                source.ValueChanged += HandleValueChanged;
                source.RaiseValueChanged(4);
                source.ValueChanged -= HandleValueChanged;
                source.RaiseValueChanged(7);
            ");

            Assert.AreEqual(4, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenSubscribingLambdaToInstanceEvent_HandlesAndUnsubscribesExpectedHandler()
        {
            MiniCSharpInterpreter interpreter = new();
            ScriptEventSource source = new();

            interpreter.RegisterObject("source", source);

            interpreter.Execute(@"
                int total = 0;
                System.Action<int> handler = (value) => total += value;

                source.ValueChanged += handler;
                source.RaiseValueChanged(4);
                source.ValueChanged -= handler;
                source.RaiseValueChanged(7);
            ");

            Assert.AreEqual(4, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Execute_WhenSubscribingScriptFunctionToStaticEvent_HandlesExpectedHandler()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                string message = """";

                void HandleGlobalEvent(string value)
                {
                    message += value;
                }

                ScriptEventSource.GlobalEvent += HandleGlobalEvent;
                ScriptEventSource.RaiseGlobalEvent(""Hello"");
                ScriptEventSource.GlobalEvent -= HandleGlobalEvent;
                ScriptEventSource.RaiseGlobalEvent(""Ignored"");
            ");

            Assert.AreEqual("Hello", interpreter.GetVariable<string>("message"));
        }

        [Test]
        public void Execute_WhenUsingInterfaceTypedVariable_StoresAndCallsExpectedMembers()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                IScriptEntity entity = new ScriptWarrior(""Rhea"", 20);

                int remaining = entity.Damage(3);
                int health = entity.Health;
            ");

            IScriptEntity entity = interpreter.GetVariable<IScriptEntity>("entity");

            Assert.IsInstanceOf<ScriptWarrior>(entity);
            Assert.AreEqual(14, entity.Health);
            Assert.AreEqual(14, interpreter.GetVariable<int>("remaining"));
            Assert.AreEqual(14, interpreter.GetVariable<int>("health"));
        }

        [Test]
        public void Execute_WhenUsingAbstractClassTypedVariable_StoresAndCallsExpectedMembers()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                ScriptEntityBase entity = new ScriptWarrior(""Rhea"", 20);

                int remaining = entity.Damage(2);
                string name = entity.Name;
                int health = entity.Health;
            ");

            ScriptEntityBase entity = interpreter.GetVariable<ScriptEntityBase>("entity");

            Assert.IsInstanceOf<ScriptWarrior>(entity);
            Assert.AreEqual("Rhea", entity.Name);
            Assert.AreEqual(16, entity.Health);
            Assert.AreEqual(16, interpreter.GetVariable<int>("remaining"));
            Assert.AreEqual("Rhea", interpreter.GetVariable<string>("name"));
            Assert.AreEqual(16, interpreter.GetVariable<int>("health"));
        }

        [Test]
        public void Execute_WhenUsingBaseClassStaticMembers_ReturnsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int lives = ScriptEntityBase.DefaultLives;
            ");

            Assert.AreEqual(3, interpreter.GetVariable<int>("lives"));
        }

        [Test]
        public void Execute_WhenAllocatingInterface_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
                    IScriptEntity entity = new IScriptEntity();
                ");
            });
        }

        [Test]
        public void Execute_WhenAllocatingAbstractClass_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
                    ScriptEntityBase entity = new ScriptEntityBase();
                ");
            });
        }

        [Test]
        public void Execute_WhenCallingInstanceMethodsWithParameters_ReturnsAndMutatesExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                var character = new ScriptCharacter(""Hero"", 10);

                int healedHealth = character.AddHealth(15);
                int damagedHealth = character.Damage(5);
                string renamed = character.Rename(""Player"", 7);
                bool alive = character.IsAlive();
            ");

            object characterObject = interpreter.GetVariable<object>("character");

            Assert.IsInstanceOf<ScriptCharacter>(characterObject);

            ScriptCharacter character = (ScriptCharacter)characterObject;

            Assert.AreEqual("Player7", character.Name);
            Assert.AreEqual(20, character.Health);
            Assert.AreEqual(25, interpreter.GetVariable<int>("healedHealth"));
            Assert.AreEqual(20, interpreter.GetVariable<int>("damagedHealth"));
            Assert.AreEqual("Player7", interpreter.GetVariable<string>("renamed"));
            Assert.IsTrue(interpreter.GetVariable<bool>("alive"));
        }

        [Test]
        public void Execute_WhenCallingStaticMethodsWithoutRegisteringType_ReturnsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int clampedHigh = ScriptCharacter.ClampHealth(999);
                int clampedLow = ScriptCharacter.ClampHealth(-50);
                int clampedMiddle = ScriptCharacter.ClampHealth(75);

                var created = ScriptCharacter.Create(""CreatedByFactory"", 33);

                string createdName = created.Name;
                int createdHealth = created.Health;
            ");

            object createdObject = interpreter.GetVariable<object>("created");

            Assert.IsInstanceOf<ScriptCharacter>(createdObject);

            ScriptCharacter created = (ScriptCharacter)createdObject;

            Assert.AreEqual(100, interpreter.GetVariable<int>("clampedHigh"));
            Assert.AreEqual(0, interpreter.GetVariable<int>("clampedLow"));
            Assert.AreEqual(75, interpreter.GetVariable<int>("clampedMiddle"));
            Assert.AreEqual("CreatedByFactory", created.Name);
            Assert.AreEqual(33, created.Health);
            Assert.AreEqual("CreatedByFactory", interpreter.GetVariable<string>("createdName"));
            Assert.AreEqual(33, interpreter.GetVariable<int>("createdHealth"));
        }

        [Test]
        public void Execute_WhenCallingExplicitGenericMethods_ReturnsExpectedValues()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                var target = new GenericTarget();
                int echoedValue = target.Echo<int>(7);
                string echoedName = GenericTarget.Identity<string>(""Ada"");
            ");

            Assert.AreEqual(7, interpreter.GetVariable<int>("echoedValue"));
            Assert.AreEqual("Ada", interpreter.GetVariable<string>("echoedName"));
        }

        [Test]
        public void Execute_WhenCallingUnityStyleGenericMethods_ReturnsExpectedComponent()
        {
            MiniCSharpInterpreter interpreter = new();
            MiniCSharpBehaviour.RegisterCommonUnityTypes(interpreter);
            GameObject gameObject = new("MiniCSharpGenericHost");

            try
            {
                BoxCollider collider = gameObject.AddComponent<BoxCollider>();
                interpreter.RegisterObject("go", gameObject);

                interpreter.Execute(@"
                    UnityEngine.BoxCollider foundCollider = go.GetComponent<UnityEngine.BoxCollider>();
                    bool hasCollider = foundCollider != null;
                ");

                Assert.IsTrue(interpreter.GetVariable<bool>("hasCollider"));
                Assert.AreSame(collider, interpreter.GetVariable<object>("foundCollider"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Execute_WhenGettingAndSettingInstanceFieldsAndProperties_UpdatesClassInstance()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                var character = new ScriptCharacter(""OldName"", 10);

                string oldName = character.Name;
                int oldHealth = character.Health;

                character.Name = ""NewName"";
                character.Health = character.Health + 40;
                character.IsInvincible = true;

                string newName = character.Name;
                int newHealth = character.Health;
                bool invincible = character.IsInvincible;
            ");

            object characterObject = interpreter.GetVariable<object>("character");

            Assert.IsInstanceOf<ScriptCharacter>(characterObject);

            ScriptCharacter character = (ScriptCharacter)characterObject;

            Assert.AreEqual("NewName", character.Name);
            Assert.AreEqual(50, character.Health);
            Assert.IsTrue(character.IsInvincible);

            Assert.AreEqual("OldName", interpreter.GetVariable<string>("oldName"));
            Assert.AreEqual(10, interpreter.GetVariable<int>("oldHealth"));
            Assert.AreEqual("NewName", interpreter.GetVariable<string>("newName"));
            Assert.AreEqual(50, interpreter.GetVariable<int>("newHealth"));
            Assert.IsTrue(interpreter.GetVariable<bool>("invincible"));
        }

        [Test]
        public void Execute_WhenGettingAndSettingStaticFieldsAndProperties_UpdatesClassStatics()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int oldDefaultHealth = ScriptCharacter.DefaultHealth;

                ScriptCharacter.DefaultHealth = 250;
                ScriptCharacter.StaticTitle = ""Champion"";

                int newDefaultHealth = ScriptCharacter.DefaultHealth;
                string title = ScriptCharacter.StaticTitle;

                int clamped = ScriptCharacter.ClampHealth(999);
            ");

            Assert.AreEqual(250, ScriptCharacter.DefaultHealth);
            Assert.AreEqual("Champion", ScriptCharacter.StaticTitle);

            Assert.AreEqual(100, interpreter.GetVariable<int>("oldDefaultHealth"));
            Assert.AreEqual(250, interpreter.GetVariable<int>("newDefaultHealth"));
            Assert.AreEqual("Champion", interpreter.GetVariable<string>("title"));
            Assert.AreEqual(250, interpreter.GetVariable<int>("clamped"));
        }

        [Test]
        public void Execute_WhenUsingTypedClassVariableWithoutRegisteringType_StoresExpectedInstance()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                ScriptCharacter character = new ScriptCharacter(""Typed"", 42);

                string characterName = character.Name;
                int characterHealth = character.Health;
            ");

            object characterObject = interpreter.GetVariable<object>("character");

            Assert.IsInstanceOf<ScriptCharacter>(characterObject);

            ScriptCharacter character = (ScriptCharacter)characterObject;

            Assert.AreEqual("Typed", character.Name);
            Assert.AreEqual(42, character.Health);
            Assert.AreEqual("Typed", interpreter.GetVariable<string>("characterName"));
            Assert.AreEqual(42, interpreter.GetVariable<int>("characterHealth"));
        }

        [Test]
        public void Execute_WhenBlacklistedTypeIsAllocated_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.BlacklistType<ScriptCharacter>();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
                    var character = new ScriptCharacter(""Blocked"", 10);
                ");
            });
        }

        [Test]
        public void Execute_WhenBlacklistedTypeStaticMemberIsAccessed_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.BlacklistType<ScriptCharacter>();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
                    int health = ScriptCharacter.DefaultHealth;
                ");
            });
        }

        [Test]
        public void Execute_WhenUsingNestedBlocks_KeepsOuterVariableAssignments()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int result = 0;

                {
                    int localValue = 5;
                    result = localValue * 2;
                }
            ");

            Assert.AreEqual(10, interpreter.GetVariable<int>("result"));
            Assert.Throws<ScriptException>(() => interpreter.GetVariable<int>("localValue"));
        }

        [Test]
        public void Execute_WhenUsingComments_IgnoresComments()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                // Single-line comment.
                int value = 1;

                /*
                    Block comment.
                */
                value = value + 2;
            ");

            Assert.AreEqual(3, interpreter.GetVariable<int>("value"));
        }

        [Test]
        public void Execute_WhenAssigningUndefinedVariable_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
                    missingValue = 10;
                ");
            });
        }

        [Test]
        public void Execute_WhenDividingByZero_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
                    int value = 10 / 0;
                ");
            });
        }

        [Test]
        public void Execute_WhenForLoopExceedsSafetyLimit_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
                    for (;;)
                    {
                    }
                ");
            });
        }

        [Test]
        public void Execute_WhenWhileLoopExceedsSafetyLimit_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
                    while (true)
                    {
                    }
                ");
            });
        }

        [Test]
        public void Execute_WhenArrayIndexerIsOutOfRange_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
                    int[] values = new int[1];
                    values[1] = 10;
                ");
            });
        }

        [Test]
        public void Execute_WhenUsingReturn_StopsScriptExecution()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int value = 1;
                value = value + 1;
                return;
                value = 999;
            ");

            Assert.AreEqual(2, interpreter.GetVariable<int>("value"));
        }

        [Test]
        public void Execute_WhenUsingReturnInsideLoop_StopsScriptExecution()
        {
            MiniCSharpInterpreter interpreter = new();

            interpreter.Execute(@"
                int total = 0;

                for (int i = 0; i < 10; i++)
                {
                    if (i == 4)
                    {
                        return;
                    }

                    total = total + i;
                }

                total = 999;
            ");

            Assert.AreEqual(6, interpreter.GetVariable<int>("total"));
        }

        [Test]
        public void Compile_WhenUsingBreakOutsideLoop_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Compile(@"
                    break;
                ");
            });
        }

        [Test]
        public void Compile_WhenUsingContinueOutsideLoop_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Compile(@"
                    continue;
                ");
            });
        }

        [Test]
        public void Compile_WhenUsingTryWithoutCatchOrFinally_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Compile(@"
                    try
                    {
                        int value = 1;
                    }
                ");
            });
        }

        [Test]
        public void Compile_WhenUsingBreakOutsideLoopOrSwitch_ThrowsScriptException()
        {
            MiniCSharpInterpreter interpreter = new();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Compile(@"
                    if (true)
                    {
                        break;
                    }
                ");
            });
        }
        
        [Test]
        public void Execute_WhenAllocatingAppDomainTypeByFullName_CreatesExpectedInstance()
        {
            var interpreter = new MiniCSharpInterpreter();

            interpreter.Execute(@"
        var builder = new System.Text.StringBuilder();
        builder.Append(""Hello"");
        builder.Append("" "");
        builder.Append(""World"");

        string result = builder.ToString();
    ");

            Assert.AreEqual("Hello World", interpreter.GetVariable<string>("result"));
        }
        
        [Test]
        public void Execute_WhenCallingVoidMethod_ExecutesMethod()
        {
            var interpreter = new MiniCSharpInterpreter();

            interpreter.Execute(@"
        var builder = new System.Text.StringBuilder();
        builder.Append(""Hello"");
        builder.Clear();
        builder.Append(""AfterClear"");

        string result = builder.ToString();
    ");

            Assert.AreEqual("AfterClear", interpreter.GetVariable<string>("result"));
        }
        
        [Test]
        public void Execute_WhenCallingMissingMethod_ThrowsScriptException()
        {
            var interpreter = new MiniCSharpInterpreter();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
            var character = new ScriptCharacter(""Hero"", 10);
            character.DoesNotExist();
        ");
            });
        }
        
        [Test]
        public void Execute_WhenCallingMethodWithWrongArgumentCount_ThrowsScriptException()
        {
            var interpreter = new MiniCSharpInterpreter();

            Assert.Throws<ScriptException>(() =>
            {
                interpreter.Execute(@"
            var character = new ScriptCharacter(""Hero"", 10);
            character.AddHealth();
        ");
            });
        }
        
        [Test]
        public void Execute_WhenClassVariableWithoutInitializer_IsNull()
        {
            var interpreter = new MiniCSharpInterpreter();

            interpreter.Execute(@"
        ScriptCharacter character;
        bool isNull = character == null;
    ");

            Assert.IsNull(interpreter.GetVariable<ScriptCharacter>("character"));
            Assert.IsTrue(interpreter.GetVariable<bool>("isNull"));
        }
        
        [Test]
        public void Execute_WhenComparingNullWithNull_ReturnsTrue()
        {
            var interpreter = new MiniCSharpInterpreter();

            interpreter.Execute(@"
        bool result = null == null;
    ");

            Assert.IsTrue(interpreter.GetVariable<bool>("result"));
        }

        [Test]
        public void Execute_WhenComparingObjectWithNull_ReturnsExpectedResult()
        {
            var interpreter = new MiniCSharpInterpreter();

            interpreter.Execute(@"
        ScriptCharacter character = new ScriptCharacter(""Hero"", 10);
        bool isNull = character == null;
        bool isNotNull = character != null;
    ");

            Assert.IsFalse(interpreter.GetVariable<bool>("isNull"));
            Assert.IsTrue(interpreter.GetVariable<bool>("isNotNull"));
        }
    }
}

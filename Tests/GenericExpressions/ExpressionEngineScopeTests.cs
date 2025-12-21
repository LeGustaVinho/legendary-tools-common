using System;
using System.Collections.Generic;
using NUnit.Framework;
using LegendaryTools.GenericExpressionEngine;

namespace LegendaryTools.Tests.ExpressionEngineUnityTests
{
    /// <summary>
    /// Tests for scope resolution (self, named scopes, relations, chaining) in the ExpressionEngine.
    /// </summary>
    public class ExpressionEngineScopeTests
    {
        private const double Tolerance = 1e-9;

        #region Helper types

        private sealed class PlayerStats
        {
            public int Hp;
            public int Mana { get; set; }
            public float Speed;
            public bool IsAlive => Hp > 0;
        }

        /// <summary>
        /// Simple relation provider for tests:
        /// - from "self" with relation "parent" -> "parent"
        /// - from "parent" with relation "owner" -> "owner"
        /// </summary>
        private sealed class TestRelationProvider : IScopeRelationProvider<double>
        {
            public bool TryResolveRelatedScope(
                EvaluationContext<double> context,
                string fromScopeName,
                string relationName,
                out string targetScopeName)
            {
                if (string.Equals(fromScopeName, "self", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(relationName, "parent", StringComparison.OrdinalIgnoreCase))
                {
                    targetScopeName = "parent";
                    return true;
                }

                if (string.Equals(fromScopeName, "parent", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(relationName, "owner", StringComparison.OrdinalIgnoreCase))
                {
                    targetScopeName = "owner";
                    return true;
                }

                targetScopeName = null!;
                return false;
            }
        }

        #endregion

        #region Self scope

        [Test]
        public void SelfScope_VariableWithoutExplicitScope_ResolvesFromSelf()
        {
            var ops = new DoubleNumberOperations();
            var engine = new ExpressionEngine<double>(ops);
            var ctx = new EvaluationContext<double>();

            ctx.Variables["$hp"] = 10.0;

            double r1 = engine.Evaluate("$hp", ctx);
            double r2 = engine.Evaluate("self.$hp", ctx);

            Assert.AreEqual(10.0, r1, Tolerance);
            Assert.AreEqual(10.0, r2, Tolerance);
        }

        #endregion

        #region Named scopes

        [Test]
        public void NamedScope_VariableFromScopeBindingVariables()
        {
            var ops = new DoubleNumberOperations();
            var engine = new ExpressionEngine<double>(ops);
            var ctx = new EvaluationContext<double>();

            var playerScope = new ScopeBinding<double>("player");
            playerScope.Variables["$hp"] = 50.0;
            ctx.Scopes["player"] = playerScope;

            double result = engine.Evaluate("player.$hp", ctx);

            Assert.AreEqual(50.0, result, Tolerance);
        }

        [Test]
        public void NamedScope_VariableFromInstanceVariableProvider()
        {
            var ops = new DoubleNumberOperations();
            var engine = new ExpressionEngine<double>(ops);
            var ctx = new EvaluationContext<double>();

            var player = new PlayerStats
            {
                Hp = 30,
                Mana = 15,
                Speed = 3.5f
            };

            var playerScope = new ScopeBinding<double>("player", player);
            playerScope.VariableProviders.Add(new InstanceVariableProvider<double>(player, ops));
            ctx.Scopes["player"] = playerScope;

            double hp = engine.Evaluate("player.$Hp", ctx);
            double mana = engine.Evaluate("player.$Mana", ctx);
            double speedTimesTwo = engine.Evaluate("player.$Speed * 2", ctx);
            double isAlive = engine.Evaluate("player.$IsAlive", ctx);

            Assert.AreEqual(30.0, hp, Tolerance);
            Assert.AreEqual(15.0, mana, Tolerance);
            Assert.AreEqual(7.0, speedTimesTwo, Tolerance);
            Assert.IsTrue(ops.ToBoolean(isAlive));
        }

        #endregion

        #region Scope relations (parent, owner, chaining)

        [Test]
        public void ScopeRelation_SelfParent_ResolvesToParentScope()
        {
            var ops = new DoubleNumberOperations();
            var engine = new ExpressionEngine<double>(ops);
            var ctx = new EvaluationContext<double>();

            ctx.ScopeRelationProviders.Add(new TestRelationProvider());

            var parentScope = new ScopeBinding<double>("parent");
            parentScope.Variables["$hp"] = 20.0;
            ctx.Scopes["parent"] = parentScope;

            ctx.Variables["$hp"] = 5.0;

            double parentHp = engine.Evaluate("self.parent.$hp", ctx);
            double selfHp = engine.Evaluate("$hp", ctx);

            Assert.AreEqual(20.0, parentHp, Tolerance);
            Assert.AreEqual(5.0, selfHp, Tolerance);
        }

        [Test]
        public void ScopeRelation_SelfParentOwner_ChainedResolution()
        {
            var ops = new DoubleNumberOperations();
            var engine = new ExpressionEngine<double>(ops);
            var ctx = new EvaluationContext<double>();

            ctx.ScopeRelationProviders.Add(new TestRelationProvider());

            var parentScope = new ScopeBinding<double>("parent");
            parentScope.Variables["$hp"] = 20.0;
            ctx.Scopes["parent"] = parentScope;

            var ownerScope = new ScopeBinding<double>("owner");
            ownerScope.Variables["$hp"] = 40.0;
            ctx.Scopes["owner"] = ownerScope;

            ctx.Variables["$hp"] = 5.0;

            double ownerHp = engine.Evaluate("self.parent.owner.$hp", ctx);
            double parentHp = engine.Evaluate("self.parent.$hp", ctx);

            Assert.AreEqual(40.0, ownerHp, Tolerance);
            Assert.AreEqual(20.0, parentHp, Tolerance);
        }

        #endregion

        #region CurrentScopeName

        [Test]
        public void CurrentScopeName_AffectsSelfDotResolution_NotRawVariable()
        {
            var ops = new DoubleNumberOperations();
            var engine = new ExpressionEngine<double>(ops);
            var ctx = new EvaluationContext<double>();

            // Escopo "self" normal
            ctx.Variables["$hp"] = 10.0;

            // Escopo "player"
            var playerScope = new ScopeBinding<double>("player");
            playerScope.Variables["$hp"] = 100.0;
            ctx.Scopes["player"] = playerScope;

            // self.$hp deve apontar para o escopo cujo nome está em CurrentScopeName
            ctx.CurrentScopeName = "player";

            double selfDotHp = engine.Evaluate("self.$hp", ctx);

            // Já $hp (sem escopo) continua vindo do dicionário self padrão
            double rawHp = engine.Evaluate("$hp", ctx);

            Assert.AreEqual(100.0, selfDotHp, Tolerance, "self.$hp must resolve using CurrentScopeName.");
            Assert.AreEqual(10.0, rawHp, Tolerance, "$hp without scope should still use the base self Variables.");
        }

        #endregion

        #region Scoped variables combined with logic

        [Test]
        public void ScopedVariables_CanBeCombinedInLogicalExpressions()
        {
            var ops = new DoubleNumberOperations();
            var engine = new ExpressionEngine<double>(ops);
            var ctx = new EvaluationContext<double>();

            var playerScope = new ScopeBinding<double>("player");
            playerScope.Variables["$hp"] = 50.0;
            ctx.Scopes["player"] = playerScope;

            var enemyScope = new ScopeBinding<double>("enemy");
            enemyScope.Variables["$hp"] = 0.0;
            ctx.Scopes["enemy"] = enemyScope;

            string expr = "player.$hp > 0 and enemy.$hp <= 0";

            double result = engine.Evaluate(expr, ctx);
            bool b = ops.ToBoolean(result);

            Assert.IsTrue(b);
        }

        #endregion

        #region Error cases

        [Test]
        public void ScopedVariable_MissingVariable_ThrowsKeyNotFound()
        {
            var ops = new DoubleNumberOperations();
            var engine = new ExpressionEngine<double>(ops);
            var ctx = new EvaluationContext<double>();

            var playerScope = new ScopeBinding<double>("player");
            // Nenhuma variável registrada no escopo "player"
            ctx.Scopes["player"] = playerScope;

            Assert.Throws<KeyNotFoundException>(() => engine.Evaluate("player.$hp", ctx));
        }

        [Test]
        public void Parse_IncompleteScopePathWithoutVariable_Throws()
        {
            var ops = new DoubleNumberOperations();
            var engine = new ExpressionEngine<double>(ops);
            var ctx = new EvaluationContext<double>();

            // "player" sozinho vira referência de escopo incompleta
            Assert.Throws<InvalidOperationException>(() => engine.Evaluate("player", ctx));

            // "self.parent" também é incompleto sem ".$var"
            Assert.Throws<InvalidOperationException>(() => engine.Evaluate("self.parent", ctx));
        }

        #endregion
    }
}

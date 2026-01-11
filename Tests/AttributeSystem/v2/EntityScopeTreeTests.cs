using System;
using System.Collections.Generic;
using System.Reflection;
using LegendaryTools.GenericExpressionEngine;
using LegendaryTools.GraphV2;
using NUnit.Framework;
using UnityEngine;
using Tree = LegendaryTools.GraphV2.Tree;

namespace LegendaryTools.AttributeSystemV2.Tests
{
    // Assets/Tests/EditMode/EntityScopeTreeTests.cs
    //
    // Unity Test Runner (EditMode) tests for scope navigation using:
    // - LegendaryTools.GraphV2.Tree as the scope relationship model
    // - LegendaryTools.GenericExpressionEngine scoped variables (self.parent.$Var, root.$Var, etc.)
    // - LegendaryTools.AttributeSystemV2 Entity + EntityAttributeVariableProvider as variable source
    //
    // These tests intentionally cover practical scope use cases:
    // - self scope access
    // - named scope access
    // - parent/root/child/sibling navigation
    // - cross-scope comparisons
    // - flags access
    // - cache/staleness behavior in EvaluationContext scope caches
    public sealed class EntityScopeTreeTests
    {
        private AttributeDefinition _hp;
        private AttributeDefinition _damage;
        private AttributeDefinition _status;

        private EntityDefinition _playerDef;
        private EntityDefinition _weaponDef;
        private EntityDefinition _targetDef;

        private Entity _player;
        private Entity _weapon;
        private Entity _target;

        private Tree _tree;
        private EntityTreeNode _playerNode;
        private EntityTreeNode _weaponNode;
        private EntityTreeNode _targetNode;

        private DoubleNumberOperations _ops;
        private ExpressionEngine<double> _engine;
        private EvaluationContext<double> _ctx;

        private Dictionary<string, EntityTreeNode> _nodesByScope;

        [SetUp]
        public void SetUp()
        {
            // --- Attribute definitions ---
            _hp = CreateIntAttribute("HP", 0);
            _damage = CreateIntAttribute("Damage", 0);
            _status = CreateFlagsAttribute("Status", new[] { "Poisoned", "Stunned" }, 0b01UL);

            // --- Entity definitions ---
            _playerDef = CreateEntityDefinition("Player", new[]
            {
                new AttributeEntry { definition = _hp, baseValue = AttributeValue.FromInt(100) },
                new AttributeEntry { definition = _status, baseValue = AttributeValue.FromFlags(0b01UL) }
            });

            _weaponDef = CreateEntityDefinition("Weapon", new[]
            {
                new AttributeEntry { definition = _damage, baseValue = AttributeValue.FromInt(10) }
            });

            _targetDef = CreateEntityDefinition("Target", new[]
            {
                new AttributeEntry { definition = _hp, baseValue = AttributeValue.FromInt(40) }
            });

            // --- Runtime entities ---
            _player = new Entity(_playerDef);
            _weapon = new Entity(_weaponDef);
            _target = new Entity(_targetDef);

            // --- Tree relationship (scope graph) ---
            _tree = new Tree();

            _playerNode = new EntityTreeNode(_player);
            _weaponNode = new EntityTreeNode(_weapon);
            _targetNode = new EntityTreeNode(_target);

            _tree.AddTreeNode(_playerNode, null);
            _tree.AddTreeNode(_weaponNode, _playerNode);
            _tree.AddTreeNode(_targetNode, _playerNode);

            _nodesByScope = new Dictionary<string, EntityTreeNode>(StringComparer.OrdinalIgnoreCase)
            {
                [_playerNode.ScopeName] = _playerNode,
                [_weaponNode.ScopeName] = _weaponNode,
                [_targetNode.ScopeName] = _targetNode
            };

            // --- Expression engine + context ---
            _ops = new DoubleNumberOperations();
            _engine = new ExpressionEngine<double>(new DoubleNumberOperations());
            _ctx = new EvaluationContext<double>();

            // Add named scopes for each entity.
            AddEntityScope(_ctx, _playerNode.ScopeName, _player, _ops);
            AddEntityScope(_ctx, _weaponNode.ScopeName, _weapon, _ops);
            AddEntityScope(_ctx, _targetNode.ScopeName, _target, _ops);

            // Add special "root" scope (required by EvaluationContext when expression starts with "root.").
            AddEntityScope(_ctx, "root", _player, _ops);

            // Add scope navigation provider backed by Tree.
            _ctx.ScopeRelationProviders.Add(new EntityTreeScopeRelationProvider(_nodesByScope, "root"));

            // Default current scope = player.
            SetCurrentScope(_playerNode.ScopeName);
        }

        [TearDown]
        public void TearDown()
        {
            // Destroy ScriptableObjects created in tests.
            DestroyImmediateSafe(_hp);
            DestroyImmediateSafe(_damage);
            DestroyImmediateSafe(_status);

            DestroyImmediateSafe(_playerDef);
            DestroyImmediateSafe(_weaponDef);
            DestroyImmediateSafe(_targetDef);
        }

        [Test]
        public void SelfScope_CanReadOwnAttribute()
        {
            SetCurrentScope(_playerNode.ScopeName);

            double hp = _engine.Evaluate("self.$HP", _ctx);

            Assert.AreEqual(100d, hp, 0.0001d);
        }

        [Test]
        public void NamedScope_CanReadOtherEntityAttribute()
        {
            // Read weapon damage from any current scope via named scope.
            SetCurrentScope(_playerNode.ScopeName);

            double dmg = _engine.Evaluate($"{_weaponNode.ScopeName}.$Damage", _ctx);

            Assert.AreEqual(10d, dmg, 0.0001d);
        }

        [Test]
        public void ParentRelation_FromChildToParent_Works()
        {
            // weapon -> parent is player
            SetCurrentScope(_weaponNode.ScopeName);

            double parentHp = _engine.Evaluate("self.parent.$HP", _ctx);

            Assert.AreEqual(100d, parentHp, 0.0001d);
        }

        [Test]
        public void RootScope_FromAnyNode_Works()
        {
            SetCurrentScope(_targetNode.ScopeName);

            double rootHp = _engine.Evaluate("root.$HP", _ctx);

            Assert.AreEqual(100d, rootHp, 0.0001d);
        }

        [Test]
        public void ChildIndexRelation_FromParentToChild_Works()
        {
            // player.child0 is weapon (in insertion order)
            SetCurrentScope(_playerNode.ScopeName);

            double childDamage = _engine.Evaluate("self.child0.$Damage", _ctx);

            Assert.AreEqual(10d, childDamage, 0.0001d);
        }

        [Test]
        public void NextSiblingRelation_Works()
        {
            // weapon next sibling is target (since weapon was added before target)
            SetCurrentScope(_weaponNode.ScopeName);

            double nextHp = _engine.Evaluate("self.nextSibling.$HP", _ctx);

            Assert.AreEqual(40d, nextHp, 0.0001d);
        }

        [Test]
        public void CrossScopeComparison_Works()
        {
            SetCurrentScope(_playerNode.ScopeName);

            // Relational operators return boolean-like numeric (DoubleNumberOperations => 1/0).
            double result = _engine.Evaluate($"self.$HP > {_targetNode.ScopeName}.$HP", _ctx);

            Assert.AreEqual(1d, result, 0.0001d);
        }

        [Test]
        public void Flags_CanBeReadAsVariable()
        {
            SetCurrentScope(_playerNode.ScopeName);

            // EntityAttributeVariableProvider supports: $Status_Poisoned -> 1 if set, else 0
            double poisoned = _engine.Evaluate("self.$Status_Poisoned", _ctx);

            Assert.AreEqual(1d, poisoned, 0.0001d);
        }

        [Test]
        public void UnscopedVariable_UsesCurrentSelfVariableProviders()
        {
            // Unscoped variables are resolved from EvaluationContext.Variables + EvaluationContext.VariableProviders.
            // We update VariableProviders whenever CurrentScopeName changes.

            SetCurrentScope(_playerNode.ScopeName);
            double hpPlayer = _engine.Evaluate("$HP", _ctx);
            Assert.AreEqual(100d, hpPlayer, 0.0001d);

            SetCurrentScope(_targetNode.ScopeName);
            double hpTarget = _engine.Evaluate("$HP", _ctx);
            Assert.AreEqual(40d, hpTarget, 0.0001d);
        }

        [Test]
        public void ScopeVariableCache_CanBecomeStale_AndCanBeCleared()
        {
            SetCurrentScope(_playerNode.ScopeName);

            // First read caches into ScopeBinding.Variables for that scope.
            double hp1 = _engine.Evaluate("self.$HP", _ctx);
            Assert.AreEqual(100d, hp1, 0.0001d);

            // Change attribute base value (internal method, invoked via reflection for test).
            AttributeInstance hpInst = _player.GetAttribute(_hp);
            SetAttributeBaseValueViaReflection(hpInst, AttributeValue.FromInt(200));

            // Second read may still be stale due to cached scope variables.
            double hpStale = _engine.Evaluate("self.$HP", _ctx);
            Assert.AreEqual(100d, hpStale, 0.0001d);

            // Clear caches and confirm it updates.
            ClearAllContextVariableCaches(_ctx);

            double hpFresh = _engine.Evaluate("self.$HP", _ctx);
            Assert.AreEqual(200d, hpFresh, 0.0001d);
        }

        // ----------------------------
        // Helpers
        // ----------------------------

        private void SetCurrentScope(string scopeName)
        {
            _ctx.CurrentScopeName = scopeName;

            // clear self-scope cache when switching "self"
            _ctx.Variables.Clear();

            // Keep unscoped variables ($HP) working by pointing "self VariableProviders" to the current entity.
            _ctx.VariableProviders.Clear();

            if (!_nodesByScope.TryGetValue(scopeName, out EntityTreeNode node))
                throw new InvalidOperationException($"Unknown scope '{scopeName}'.");

            _ctx.VariableProviders.Add(new EntityAttributeVariableProvider<double>(node.Entity, _ops));
        }

        private static void AddEntityScope(EvaluationContext<double> ctx, string scopeName, Entity entity,
            INumberOperations<double> ops)
        {
            ScopeBinding<double> binding = new(scopeName, entity);
            binding.VariableProviders.Add(new EntityAttributeVariableProvider<double>(entity, ops));
            ctx.Scopes[scopeName] = binding;
        }

        private static AttributeDefinition CreateIntAttribute(string displayName, long baseValue)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = displayName;
            def.kind = AttributeKind.Integer;
            def.baseInteger = baseValue;
            def.clampMode = AttributeClampMode.ClampOnSet;
            def.minMode = AttributeLimitMode.None;
            def.maxMode = AttributeLimitMode.None;
            return def;
        }

        private static AttributeDefinition CreateFlagsAttribute(string displayName, string[] flagNames, ulong baseFlags)
        {
            AttributeDefinition def = ScriptableObject.CreateInstance<AttributeDefinition>();
            def.displayName = displayName;
            def.kind = AttributeKind.Flags;
            def.baseFlags = baseFlags;
            def.flagNames = flagNames ?? Array.Empty<string>();
            def.clampMode = AttributeClampMode.None;
            def.minMode = AttributeLimitMode.None;
            def.maxMode = AttributeLimitMode.None;
            return def;
        }

        private static EntityDefinition CreateEntityDefinition(string entityName, IEnumerable<AttributeEntry> entries)
        {
            EntityDefinition def = ScriptableObject.CreateInstance<EntityDefinition>();
            def.entityName = entityName;

            def.attributes.Clear();
            foreach (AttributeEntry e in entries)
            {
                def.attributes.Add(e);
            }

            return def;
        }

        private static void DestroyImmediateSafe(UnityEngine.Object obj)
        {
            if (obj == null) return;
            UnityEngine.Object.DestroyImmediate(obj);
        }

        private static void ClearAllContextVariableCaches(EvaluationContext<double> ctx)
        {
            ctx.Variables.Clear();

            foreach (KeyValuePair<string, ScopeBinding<double>> kvp in ctx.Scopes)
            {
                kvp.Value.Variables.Clear();
            }
        }

        private static void SetAttributeBaseValueViaReflection(AttributeInstance instance, AttributeValue value)
        {
            // AttributeInstance.SetBaseValue(AttributeValue newBaseValue, bool reapplyLimits = true) is internal.
            MethodInfo method = typeof(AttributeInstance).GetMethod(
                "SetBaseValue",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (method == null)
                throw new MissingMethodException("AttributeInstance.SetBaseValue method not found via reflection.");

            method.Invoke(instance, new object[] { value, true });
        }

        // ----------------------------
        // Tree-backed scope relations
        // ----------------------------

        private sealed class EntityTreeNode : TreeNode
        {
            public Entity Entity { get; }
            public string ScopeName { get; }

            public EntityTreeNode(Entity entity)
            {
                Entity = entity ?? throw new ArgumentNullException(nameof(entity));
                ScopeName = entity.Name;
            }
        }

        /// <summary>
        /// Resolves scope relations (parent/root/child/siblings) using a Tree of EntityTreeNodes.
        /// </summary>
        private sealed class EntityTreeScopeRelationProvider : IScopeRelationProvider<double>
        {
            private readonly IReadOnlyDictionary<string, EntityTreeNode> _nodesByScope;
            private readonly string _rootScopeName;

            public EntityTreeScopeRelationProvider(IReadOnlyDictionary<string, EntityTreeNode> nodesByScope,
                string rootScopeName)
            {
                _nodesByScope = nodesByScope ?? throw new ArgumentNullException(nameof(nodesByScope));
                _rootScopeName = string.IsNullOrWhiteSpace(rootScopeName) ? "root" : rootScopeName;
            }

            public bool TryResolveRelatedScope(
                EvaluationContext<double> context,
                string fromScopeName,
                string relationName,
                out string targetScopeName)
            {
                targetScopeName = null;

                if (string.IsNullOrWhiteSpace(fromScopeName) || string.IsNullOrWhiteSpace(relationName))
                    return false;

                if (relationName.Equals("root", StringComparison.OrdinalIgnoreCase))
                {
                    targetScopeName = _rootScopeName;
                    return true;
                }

                if (!_nodesByScope.TryGetValue(fromScopeName, out EntityTreeNode fromNode))
                    return false;

                if (relationName.Equals("parent", StringComparison.OrdinalIgnoreCase))
                {
                    EntityTreeNode parent = fromNode.ParentNode as EntityTreeNode;
                    if (parent == null) return false;

                    targetScopeName = parent.ScopeName;
                    return true;
                }

                if (TryResolveChild(fromNode, relationName, out EntityTreeNode child))
                {
                    targetScopeName = child.ScopeName;
                    return true;
                }

                if (relationName.Equals("nextSibling", StringComparison.OrdinalIgnoreCase))
                    return TryResolveSibling(fromNode, +1, out targetScopeName);

                if (relationName.Equals("prevSibling", StringComparison.OrdinalIgnoreCase))
                    return TryResolveSibling(fromNode, -1, out targetScopeName);

                return false;
            }

            private static bool TryResolveChild(EntityTreeNode fromNode, string relationName, out EntityTreeNode child)
            {
                child = null;

                if (fromNode.ChildNodes == null || fromNode.ChildNodes.Count == 0)
                    return false;

                // child0, child1, ...
                if (relationName.StartsWith("child", StringComparison.OrdinalIgnoreCase) &&
                    relationName.Length > "child".Length)
                {
                    string suffix = relationName.Substring("child".Length);

                    if (int.TryParse(suffix, out int index))
                    {
                        if (index < 0 || index >= fromNode.ChildNodes.Count)
                            return false;

                        child = fromNode.ChildNodes[index] as EntityTreeNode;
                        return child != null;
                    }

                    // child_<ScopeName>
                    if (suffix.StartsWith("_", StringComparison.Ordinal))
                    {
                        string wanted = suffix.Substring(1);
                        for (int i = 0; i < fromNode.ChildNodes.Count; i++)
                        {
                            EntityTreeNode c = fromNode.ChildNodes[i] as EntityTreeNode;
                            if (c == null) continue;

                            if (wanted.Equals(c.ScopeName, StringComparison.OrdinalIgnoreCase))
                            {
                                child = c;
                                return true;
                            }
                        }

                        return false;
                    }
                }

                return false;
            }

            private static bool TryResolveSibling(EntityTreeNode fromNode, int delta, out string siblingScope)
            {
                siblingScope = null;

                EntityTreeNode parent = fromNode.ParentNode as EntityTreeNode;
                if (parent == null || parent.ChildNodes == null)
                    return false;

                int index = parent.ChildNodes.IndexOf(fromNode);
                if (index < 0) return false;

                int next = index + delta;
                if (next < 0 || next >= parent.ChildNodes.Count)
                    return false;

                EntityTreeNode sib = parent.ChildNodes[next] as EntityTreeNode;
                if (sib == null) return false;

                siblingScope = sib.ScopeName;
                return true;
            }
        }
    }
}
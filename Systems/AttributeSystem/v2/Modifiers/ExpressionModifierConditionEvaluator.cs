using System;
using System.Collections.Generic;
using LegendaryTools.GenericExpressionEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Evaluates modifier conditions using GenericExpressionEngine and EntityScopeTree-based scopes.
    /// Supports:
    /// - Raw vars: $HP (self)
    /// - Scoped vars: source.$Level, target.$Armor, root.$Something
    /// - Relations: self.parent.$HP, self.child0.$HP, etc. (via EntityTreeScopeRelationProvider)
    /// </summary>
    public sealed class ExpressionModifierConditionEvaluator<T> : IModifierConditionEvaluator
    {
        private readonly ExpressionEngine<T> _engine;
        private readonly EntityExpressionScopeContext<T> _scopeContext;
        private readonly ICompiledExpressionCache<T> _cache;

        private readonly Func<Entity, AttributeInstance, Entity, AttributeInstance, bool> _codePredicate;

        private const string SourceScopeName = "source";
        private const string TargetScopeName = "target";

        public ExpressionModifierConditionEvaluator(
            ExpressionEngine<T> engine,
            EntityExpressionScopeContext<T> scopeContext,
            ICompiledExpressionCache<T> cache = null,
            Func<Entity, AttributeInstance, Entity, AttributeInstance, bool> codePredicate = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _scopeContext = scopeContext ?? throw new ArgumentNullException(nameof(scopeContext));
            _cache = cache; // Optional
            _codePredicate = codePredicate; // Optional

            // Make sure tree scopes + relation provider are installed once.
            _scopeContext.Install();
        }

        public bool ShouldApply(Entity source, AttributeInstance modifier, Entity target, AttributeInstance targetAttribute)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (targetAttribute == null) throw new ArgumentNullException(nameof(targetAttribute));

            AttributeDefinition def = modifier.Definition;
            if (def == null)
                return false;

            // No condition configured.
            if (!def.hasCondition || def.conditionMode == AttributeModifierConditionMode.None)
                return true;

            bool exprResult = true;
            bool codeResult = true;

            if (def.conditionMode == AttributeModifierConditionMode.ExpressionOnly ||
                def.conditionMode == AttributeModifierConditionMode.ExpressionAndCode)
            {
                exprResult = EvaluateExpressionCondition(def, source, modifier, target, targetAttribute);
            }

            if (def.conditionMode == AttributeModifierConditionMode.CodeOnly ||
                def.conditionMode == AttributeModifierConditionMode.ExpressionAndCode)
            {
                codeResult = _codePredicate == null || _codePredicate(source, modifier, target, targetAttribute);
            }

            return exprResult && codeResult;
        }

        private bool EvaluateExpressionCondition(
            AttributeDefinition modifierDef,
            Entity source,
            AttributeInstance modifier,
            Entity target,
            AttributeInstance targetAttribute)
        {
            string expr = modifierDef.conditionExpression;

            // Missing expression string: treat as "true" (configurable later if you prefer strict behavior).
            if (string.IsNullOrWhiteSpace(expr))
                return true;

            // Ensure "source" and "target" scopes exist every evaluation (they can point to any entities).
            EnsureNamedScope(_scopeContext.Context, SourceScopeName, source, _scopeContext.Ops);
            EnsureNamedScope(_scopeContext.Context, TargetScopeName, target, _scopeContext.Ops);

            // Decide what "self" means: target entity (common for "apply to target if ...").
            // Prefer the real tree scope name if target is in the tree; otherwise fallback to "target".
            string selfScopeName = _scopeContext.ScopeTree.GetScopeName(target);
            if (string.IsNullOrWhiteSpace(selfScopeName))
            {
                // Fallback: make current scope name "target" so relations like self.* resolve from that scope.
                _scopeContext.Context.CurrentScopeName = TargetScopeName;

                // Still install the self variable provider slot to keep $Var working.
                EnsureSelfProviderSlot(_scopeContext.Context, target, _scopeContext.Ops);
            }
            else
            {
                // Standard path: current scope becomes the entity's own scope name.
                _scopeContext.SetSelf(target);
            }

            // Avoid stale values from last evaluation.
            _scopeContext.Prepare();

            T result;

            // Use compiled cache if provided; otherwise evaluate raw expression.
            if (_cache != null)
            {
                string key = string.IsNullOrWhiteSpace(modifierDef.conditionExpressionKey)
                    ? expr
                    : modifierDef.conditionExpressionKey;

                CompiledExpression<T> compiled = _cache.GetOrCompile(_engine, key, expr);
                result = compiled.Evaluate(_scopeContext.Context);
            }
            else
            {
                result = _engine.Evaluate(expr, _scopeContext.Context);
            }

            return IsTruthy(result);
        }

        private static void EnsureNamedScope(EvaluationContext<T> ctx, string scopeName, Entity entity, INumberOperations<T> ops)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (string.IsNullOrWhiteSpace(scopeName)) throw new ArgumentException("Scope name is required.", nameof(scopeName));
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (ops == null) throw new ArgumentNullException(nameof(ops));

            if (!ctx.Scopes.TryGetValue(scopeName, out ScopeBinding<T> binding) || binding == null)
            {
                binding = new ScopeBinding<T>(scopeName, entity);
                ctx.Scopes[scopeName] = binding;
            }

            // Replace/ensure the provider for this scope name points to the right entity.
            RemoveProvidersOfType<EntityAttributeVariableProvider<T>>(binding.VariableProviders);
            binding.VariableProviders.Add(new EntityAttributeVariableProvider<T>(entity, ops));
        }

        private static void EnsureSelfProviderSlot(EvaluationContext<T> ctx, Entity self, INumberOperations<T> ops)
        {
            if (ctx.VariableProviders.Count == 0)
                ctx.VariableProviders.Add(new EntityAttributeVariableProvider<T>(self, ops));
            else
                ctx.VariableProviders[0] = new EntityAttributeVariableProvider<T>(self, ops);
        }

        private static void RemoveProvidersOfType<TProvider>(List<IVariableProvider<T>> providers)
        {
            for (int i = providers.Count - 1; i >= 0; i--)
            {
                if (providers[i] is TProvider)
                    providers.RemoveAt(i);
            }
        }

        private bool IsTruthy(T value)
        {
            // Truthy rule (engine usually yields 0/1 for booleans):
            // - Equals(Zero) => false
            // - Non-zero => true
            return !EqualityComparer<T>.Default.Equals(value, _scopeContext.Ops.Zero);
        }
    }
}

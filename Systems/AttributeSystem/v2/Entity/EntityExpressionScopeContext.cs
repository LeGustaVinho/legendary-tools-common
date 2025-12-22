using System;
using LegendaryTools.GenericExpressionEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// High-level reusable helper that binds:
    /// - EntityScopeTree (Tree relationships) -> GenericExpressionEngine scopes
    /// - EntityAttributeVariableProvider (per-scope + self)
    /// - Clears EvaluationContext caches before each evaluation to avoid stale reads.
    /// </summary>
    public sealed class EntityExpressionScopeContext<T>
    {
        public EntityScopeTree ScopeTree { get; }
        public INumberOperations<T> Ops { get; }
        public EvaluationContext<T> Context { get; }

        public EntityExpressionScopeContext(EntityScopeTree scopeTree, INumberOperations<T> ops)
        {
            ScopeTree = scopeTree ?? throw new ArgumentNullException(nameof(scopeTree));
            Ops = ops ?? throw new ArgumentNullException(nameof(ops));
            Context = new EvaluationContext<T>();
        }

        /// <summary>
        /// Installs scopes + relations into the context once (you can call it again safely).
        /// </summary>
        public void Install()
        {
            ScopeTree.InstallInto(Context, Ops, true);
        }

        /// <summary>
        /// Sets which entity is "self" for $Var resolution and "self.*" paths.
        /// </summary>
        public void SetSelf(Entity self)
        {
            ScopeTree.SetSelf(Context, self, Ops);
        }

        /// <summary>
        /// Ensures variable caches do not keep stale attribute values.
        /// Call this before every evaluation.
        /// </summary>
        public void Prepare()
        {
            EntityScopeTree.PrepareFreshLookupCaches(Context);
        }

        public T Evaluate(ExpressionEngine<T> engine, string expression)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            Prepare();
            return engine.Evaluate(expression, Context);
        }
    }
}
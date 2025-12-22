using System;
using System.Collections.Generic;
using LegendaryTools.GenericExpressionEngine;
using LegendaryTools.GraphV2;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Owns a Tree that represents entity scope relationships (parent/child/root).
    /// Provides utilities to bind that graph to GenericExpressionEngine EvaluationContext scopes.
    /// </summary>
    public sealed class EntityScopeTree
    {
        private readonly Tree _tree = new();

        private readonly Dictionary<Entity, EntityScopeNode> _byEntity = new();
        private readonly Dictionary<string, EntityScopeNode> _byScopeName = new(StringComparer.OrdinalIgnoreCase);

        public ITree Tree => _tree;
        public EntityScopeNode RootNode => _tree.RootNode as EntityScopeNode;

        /// <summary>
        /// Sets the root entity. Root scope name is always "root" for GenericExpressionEngine compatibility.
        /// </summary>
        public EntityScopeNode SetRoot(Entity rootEntity)
        {
            if (rootEntity == null) throw new ArgumentNullException(nameof(rootEntity));

            if (RootNode != null)
                throw new InvalidOperationException("Root already exists.");

            EntityScopeNode node = new(rootEntity, "root");
            _tree.AddTreeNode(node, null);

            RegisterNode(node);
            return node;
        }

        /// <summary>
        /// Adds a child entity under a parent entity. Scope name is identifier-safe and unique.
        /// </summary>
        public EntityScopeNode AddChild(Entity parent, Entity child, string scopeName = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (child == null) throw new ArgumentNullException(nameof(child));

            if (!_byEntity.TryGetValue(parent, out EntityScopeNode parentNode))
                throw new KeyNotFoundException($"Parent entity '{parent.Name}' is not in the scope tree.");

            if (_byEntity.ContainsKey(child))
                throw new InvalidOperationException($"Child entity '{child.Name}' is already in the scope tree.");

            string finalName = MakeUniqueScopeName(scopeName, child.Name);
            EntityScopeNode childNode = new(child, finalName);

            _tree.AddTreeNode(childNode, parentNode);
            RegisterNode(childNode);

            return childNode;
        }

        /// <summary>
        /// Adds an alias scope name for an existing node (multiple scope names can map to the same entity).
        /// Useful for "player", "enemy", etc.
        /// </summary>
        public void AddAlias(Entity entity, string aliasScopeName)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (string.IsNullOrWhiteSpace(aliasScopeName))
                throw new ArgumentException("Alias scope name is required.", nameof(aliasScopeName));

            if (!_byEntity.TryGetValue(entity, out EntityScopeNode node))
                throw new KeyNotFoundException($"Entity '{entity.Name}' is not in the scope tree.");

            string finalName = MakeUniqueScopeName(aliasScopeName, aliasScopeName);
            _byScopeName[finalName] = node;
        }

        public bool TryGetNode(string scopeName, out EntityScopeNode node)
        {
            if (string.IsNullOrWhiteSpace(scopeName))
            {
                node = null;
                return false;
            }

            return _byScopeName.TryGetValue(scopeName, out node);
        }

        public string GetScopeName(Entity entity)
        {
            return entity != null && _byEntity.TryGetValue(entity, out EntityScopeNode node) ? node.ScopeName : null;
        }

        /// <summary>
        /// Installs scopes and relation provider into the EvaluationContext.
        /// This registers each scope name as a ScopeBinding with an EntityAttributeVariableProvider.
        /// </summary>
        public void InstallInto<T>(
            EvaluationContext<T> ctx,
            INumberOperations<T> ops,
            bool addRelationProvider = true)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ops == null) throw new ArgumentNullException(nameof(ops));

            // Register all scope names (including aliases) into ctx.Scopes.
            foreach (KeyValuePair<string, EntityScopeNode> kvp in _byScopeName)
            {
                string scopeName = kvp.Key;
                EntityScopeNode node = kvp.Value;

                if (!ctx.Scopes.TryGetValue(scopeName, out ScopeBinding<T> binding))
                {
                    binding = new ScopeBinding<T>(scopeName, node.Entity);
                    ctx.Scopes[scopeName] = binding;
                }

                // Ensure provider exists (avoid duplicates).
                bool hasProvider = false;
                for (int i = 0; i < binding.VariableProviders.Count; i++)
                {
                    if (binding.VariableProviders[i] is EntityAttributeVariableProvider<T>)
                    {
                        hasProvider = true;
                        break;
                    }
                }

                if (!hasProvider)
                    binding.VariableProviders.Add(new EntityAttributeVariableProvider<T>(node.Entity, ops));
            }

            if (addRelationProvider)
            {
                // Avoid duplicates if called multiple times.
                bool alreadyAdded = false;
                for (int i = 0; i < ctx.ScopeRelationProviders.Count; i++)
                {
                    if (ctx.ScopeRelationProviders[i] is EntityTreeScopeRelationProvider<T> existing &&
                        ReferenceEquals(existing.OwnerTree, this))
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                    ctx.ScopeRelationProviders.Add(new EntityTreeScopeRelationProvider<T>(this));
            }
        }

        /// <summary>
        /// Sets "self" for the context:
        /// - ctx.CurrentScopeName points to the entity node scope name
        /// - ctx.VariableProviders[0] becomes the self EntityAttributeVariableProvider
        /// NOTE: call PrepareFreshLookupCaches() before every evaluation to prevent stale cached values.
        /// </summary>
        public void SetSelf<T>(EvaluationContext<T> ctx, Entity self, INumberOperations<T> ops)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (self == null) throw new ArgumentNullException(nameof(self));
            if (ops == null) throw new ArgumentNullException(nameof(ops));

            if (!_byEntity.TryGetValue(self, out EntityScopeNode node))
                throw new KeyNotFoundException($"Self entity '{self.Name}' is not in the scope tree.");

            ctx.CurrentScopeName = node.ScopeName;

            // Reserve index 0 as the self-provider slot.
            IVariableProvider<T> selfProvider = new EntityAttributeVariableProvider<T>(self, ops);

            if (ctx.VariableProviders.Count == 0)
                ctx.VariableProviders.Add(selfProvider);
            else
                ctx.VariableProviders[0] = selfProvider;
        }

        /// <summary>
        /// Clears EvaluationContext variable caches to prevent stale values.
        /// GenericExpressionEngine caches provider results into dictionaries; for dynamic attributes,
        /// you typically want fresh reads every evaluation.
        /// </summary>
        public static void PrepareFreshLookupCaches<T>(EvaluationContext<T> ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            ctx.Variables.Clear();

            foreach (KeyValuePair<string, ScopeBinding<T>> kvp in ctx.Scopes)
            {
                kvp.Value.Variables.Clear();
            }
        }

        internal bool TryGetNodeByScopeName(string scopeName, out EntityScopeNode node)
        {
            return _byScopeName.TryGetValue(scopeName, out node);
        }

        private void RegisterNode(EntityScopeNode node)
        {
            _byEntity.Add(node.Entity, node);
            _byScopeName.Add(node.ScopeName, node);

            // Root should also be reachable via "root" always.
            if (RootNode == node && !_byScopeName.ContainsKey("root"))
                _byScopeName["root"] = node;
        }

        private string MakeUniqueScopeName(string preferred, string fallbackBaseName)
        {
            string baseName = string.IsNullOrWhiteSpace(preferred) ? fallbackBaseName : preferred;
            baseName = IdentifierSanitizer.ToIdentifier(baseName, "scope");

            string candidate = baseName;
            int suffix = 2;

            while (_byScopeName.ContainsKey(candidate))
            {
                candidate = $"{baseName}_{suffix}";
                suffix++;
            }

            return candidate;
        }
    }

    internal static class IdentifierSanitizer
    {
        /// <summary>
        /// Converts an arbitrary string into an identifier-safe name for the expression tokenizer:
        /// - First char must be a letter or underscore
        /// - Remaining chars: letters, digits, underscore
        /// </summary>
        public static string ToIdentifier(string text, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;

            char[] buffer = text.Trim().ToCharArray();

            for (int i = 0; i < buffer.Length; i++)
            {
                char c = buffer[i];
                bool ok = char.IsLetterOrDigit(c) || c == '_';
                buffer[i] = ok ? c : '_';
            }

            string result = new(buffer);

            if (!(char.IsLetter(result[0]) || result[0] == '_'))
                result = "_" + result;

            // Avoid "$" start; variables use '$', scope should not.
            if (result[0] == '$')
                result = "_" + result.Substring(1);

            return result.Length == 0 ? fallback : result;
        }
    }
}
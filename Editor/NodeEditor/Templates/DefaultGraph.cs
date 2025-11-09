using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Generic, event-driven node manager for graphs.
    /// Ensures each TNode has a sibling TConfig on NodeAdded, and deletes it on NodeRemoved.
    /// </summary>
    /// <typeparam name="TNode">Concrete node (Node + IHasConfig{TConfig}).</typeparam>
    /// <typeparam name="TConfig">Config ScriptableObject base type.</typeparam>
    public class DefaultGraph<TNode, TConfig> : Graph
        where TNode : Node, IHasConfig<TConfig>
        where TConfig : ScriptableObject
    {
        // -------------------- Lifecycle --------------------

        /// <summary>
        /// Subscribes to Graph events to manage configs for nodes generically.
        /// </summary>
        protected virtual void OnEnable()
        {
            // Avoid double subscribe if Unity recreates the object domain.
            NodeAdded -= HandleNodeAdded;
            NodeRemoved -= HandleNodeRemoved;

            NodeAdded += HandleNodeAdded;
            NodeRemoved += HandleNodeRemoved;
        }

        protected virtual void OnDisable()
        {
            NodeAdded -= HandleNodeAdded;
            NodeRemoved -= HandleNodeRemoved;
        }

        // -------------------- Public factories (optional sugar) --------------------

        /// <summary>
        /// Creates a TNode and adds to graph. Config creation is handled by NodeAdded event.
        /// </summary>
        public virtual TNode CreateStepNode(Type explicitConfigTypeOrNull, string title, Vector2 position)
        {
            // Create and initialize node
            TNode node = CreateInstance<TNode>();
            node.Id = Guid.NewGuid().ToString("N");
            node.name = string.IsNullOrEmpty(title) ? typeof(TNode).Name : title;
            node.Title = node.name;
            node.Position = position;

            // Register in graph (Add will trigger NodeAdded -> config ensured)
            Add(node);
            return node;
        }

        /// <summary>
        /// Default generic CreateNode path if someone uses graph UI entrypoints.
        /// </summary>
        public override Node CreateNode(string title, Vector2 position)
        {
            return CreateStepNode(null, title, position);
        }

        /// <summary>
        /// Clone a TNode; Add will trigger NodeAdded to create a fresh config of same concrete type (when available).
        /// </summary>
        public override Node CreateNodeClone(Node template, Vector2 position)
        {
            if (template == null) return null;

            if (template is not TNode src)
                return base.CreateNodeClone(template, position);

            TNode clone = CreateInstance<TNode>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(src), clone);

            clone.Id = Guid.NewGuid().ToString("N");
            clone.name = $"{src.name}_Copy";
            clone.Title = clone.name;
            clone.Position = position;

            Add(clone); // NodeAdded will ensure a new config (sibling) is created and assigned
            return clone;
        }

        // -------------------- Event handlers --------------------

        /// <summary>
        /// Ensures a sibling TConfig asset exists and is assigned to the added TNode.
        /// </summary>
        protected virtual void HandleNodeAdded(Graph graph, Node node)
        {
            if (graph != this) return;
            if (node is not TNode typed) return;

            // If the node already has a config, do nothing.
            if (typed.Config != null) return;

#if UNITY_EDITOR
            // Select concrete TConfig type: prefer first non-abstract derived, or explicit passed via name tag.
            Type cfgType = ResolveTargetConfigType(null);
            TConfig newCfg = CreateConfigSiblingAsset(this, typed, cfgType);
            typed.Config = newCfg;

            EditorUtility.SetDirty(this);
            EditorUtility.SetDirty(typed);
            EditorUtility.SetDirty(newCfg);
            AssetDatabase.SaveAssets();
#endif
        }

        /// <summary>
        /// Deletes sibling TConfig asset when a TNode is removed.
        /// </summary>
        protected virtual void HandleNodeRemoved(Graph graph, Node node)
        {
            if (graph != this) return;
            if (node is not TNode typed) return;

#if UNITY_EDITOR
            TConfig cfg = typed.Config;
            if (cfg == null) return;

            try
            {
                string path = AssetDatabase.GetAssetPath(cfg);
                if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#endif
        }

        // -------------------- Editor utilities --------------------
#if UNITY_EDITOR
        /// <summary>
        /// Picks a concrete, non-abstract type assignable to TConfig.
        /// </summary>
        protected virtual Type ResolveTargetConfigType(Type explicitTypeOrNull)
        {
            if (explicitTypeOrNull != null &&
                typeof(TConfig).IsAssignableFrom(explicitTypeOrNull) &&
                !explicitTypeOrNull.IsAbstract)
                return explicitTypeOrNull;

            Type firstConcrete = TypeCache
                .GetTypesDerivedFrom<TConfig>()
                .FirstOrDefault(t => t != null && !t.IsAbstract && typeof(TConfig).IsAssignableFrom(t));

            if (firstConcrete == null)
                throw new InvalidOperationException(
                    $"No concrete {typeof(TConfig).Name} types were found in the project. " +
                    $"Create a non-abstract class deriving from {typeof(TConfig).Name}.");

            return firstConcrete;
        }

        /// <summary>
        /// Creates a sibling TConfig ScriptableObject in the same folder as this graph asset.
        /// </summary>
        protected virtual TConfig CreateConfigSiblingAsset(ScriptableObject ownerGraph, TNode node, Type cfgType)
        {
            if (ownerGraph == null) throw new ArgumentNullException(nameof(ownerGraph));
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (cfgType == null) throw new ArgumentNullException(nameof(cfgType));

            TConfig cfg = (TConfig)CreateInstance(cfgType);
            cfg.name = $"{node.name}_Config";

            string graphPath = AssetDatabase.GetAssetPath(ownerGraph);
            if (string.IsNullOrEmpty(graphPath))
                throw new InvalidOperationException(
                    "Owner graph must be saved as an asset before creating config assets.");

            string folder = Path.GetDirectoryName(graphPath).Replace('\\', '/');
            string fileName = $"{cfg.name}.asset";
            string candidatePath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}");

            AssetDatabase.CreateAsset(cfg, candidatePath);
            return cfg;
        }
#endif
    }
}
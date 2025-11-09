using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryTools.GraphV2;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using LegendaryTools.Maestro;
using LegendaryTools.NodeEditor;
using Node = LegendaryTools.NodeEditor.Node;

namespace LegendaryTools.Maestro.NodeEditor
{
    [CreateAssetMenu(menuName = "LegendaryTools/Maestro/MaestroEditor")]
    public class MaestroEditor : LegendaryTools.NodeEditor.Graph
    {
        public Maestro Maestro;

        private readonly MaestroNodeEditorMenu maestroNodeEditorMenu = new();
        public override IEdgeContextMenuProvider EdgeMenuProvider => maestroNodeEditorMenu;
        public override IGraphContextMenuProvider GraphMenuProvider => maestroNodeEditorMenu;
        public override IToolbarProvider ToolbarProvider => maestroNodeEditorMenu;

        /// <summary>
        /// Creates an InitStepNodeEditor at the given position. If a specific InitStepConfig
        /// subtype is provided, an instance of that type is created and assigned.
        /// </summary>
        public InitStepNodeEditor CreateStepNode(Type initStepConfigType, string title, Vector2 position)
        {
            // Create InitStepNodeEditor instead of a plain Node.
            InitStepNodeEditor node = CreateInstance<InitStepNodeEditor>();

            // Set basic metadata prior to registration in the graph.
            node.Id = Guid.NewGuid().ToString("N");
            node.name = string.IsNullOrEmpty(title) ? "InitStep" : title;
            node.Title = node.name;
            node.Position = position;

            // Add to graph using the safe API.
            Add(node);

#if UNITY_EDITOR
            // Ensure the desired Config exists and is attached as a sub-asset.
            InitStepConfig cfg = CreateInitStepConfigSubAsset(node, initStepConfigType);
            node.InitStepConfig = cfg;
            EditorUtility.SetDirty(node);
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
#endif
            return node;
        }

        /// <summary>
        /// Creates a default InitStepNodeEditor when the generic CreateNode(...) is used.
        /// </summary>
        public override Node CreateNode(string title, Vector2 position)
        {
            InitStepNodeEditor node = CreateInstance<InitStepNodeEditor>();
            node.Id = Guid.NewGuid().ToString("N");
            node.name = string.IsNullOrEmpty(title) ? "InitStep" : title;
            node.Title = node.name;
            node.Position = position;

            Add(node);

#if UNITY_EDITOR
            // Always ensure a config sub-asset is present and wired.
            InitStepConfig cfg = CreateInitStepConfigSubAsset(node, null);
            node.InitStepConfig = cfg;
            EditorUtility.SetDirty(node);
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
#endif
            return node;
        }

        /// <summary>
        /// Creates a deep clone of an existing InitStepNodeEditor and clears dependencies on the new config.
        /// </summary>
        public override Node CreateNodeClone(Node template, Vector2 position)
        {
            if (template == null) return null;

            // If not an InitStepNodeEditor, delegate to base implementation.
            if (template is not InitStepNodeEditor src)
                return base.CreateNodeClone(template, position);

            // Create a new InitStepNodeEditor “blank”.
            InitStepNodeEditor clone = CreateInstance<InitStepNodeEditor>();

            // Copy serialized data (except sub-asset references).
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(src), clone);

            // Reassign identity, title and position.
            clone.Id = Guid.NewGuid().ToString("N");
            clone.name = $"{src.name}_Copy";
            clone.Title = clone.name;
            clone.Position = position;

            // Register in graph.
            Add(clone);

#if UNITY_EDITOR
            // Always generate a NEW clean InitStepConfig sub-asset.
            Type sourceType = src.InitStepConfig != null && !src.InitStepConfig.GetType().IsAbstract
                ? src.InitStepConfig.GetType()
                : null;

            InitStepConfig newCfg = CreateInitStepConfigSubAsset(clone, sourceType);
            clone.InitStepConfig = newCfg;

            // Clear dependencies on clone’s config (do not inherit from source).
            clone.InitStepConfig.StepDependencies = Array.Empty<InitStepConfig>();

            EditorUtility.SetDirty(clone);
            EditorUtility.SetDirty(newCfg);
            AssetDatabase.SaveAssets();
#endif

            return clone;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Finds a default non-abstract InitStepConfig subtype to instantiate when none is explicitly provided.
        /// </summary>
        private static Type FindDefaultInitStepConfigConcreteTypeOrThrow()
        {
            Type concrete = TypeCache
                .GetTypesDerivedFrom<InitStepConfig>()
                .FirstOrDefault(t => t != null && !t.IsAbstract && typeof(InitStepConfig).IsAssignableFrom(t));

            if (concrete == null)
                throw new InvalidOperationException(
                    "No concrete InitStepConfig types were found in the project. " +
                    "Create a non-abstract class deriving from InitStepConfig.");

            return concrete;
        }

        /// <summary>
        /// Creates an InitStepConfig sub-asset under the node. If a specific subtype is passed,
        /// it will be used. Otherwise a concrete default subtype is discovered via TypeCache.
        /// </summary>
        private static InitStepConfig CreateInitStepConfigSubAsset(InitStepNodeEditor node, Type cfgTypeOrNull)
        {
            Type chosen =
                cfgTypeOrNull != null && typeof(InitStepConfig).IsAssignableFrom(cfgTypeOrNull) &&
                !cfgTypeOrNull.IsAbstract
                    ? cfgTypeOrNull
                    : FindDefaultInitStepConfigConcreteTypeOrThrow();

            InitStepConfig cfg = (InitStepConfig)CreateInstance(chosen);
            cfg.name = $"{node.name}_Config";
            AssetDatabase.AddObjectToAsset(cfg, node);
            return cfg;
        }
#endif

        // -------------------- Node/Config removal & cleanup --------------------

        /// <summary>
        /// Removes a node by Id, deletes its config sub-asset, and purges references
        /// from other nodes' StepDependencies. Also relies on base.RemoveNode to
        /// remove incident edges from the graph.
        /// </summary>
        public override void RemoveNode(string nodeId)
        {
            // Find the concrete node before calling base (we still need its config reference).
            InitStepNodeEditor n = Nodes.OfType<InitStepNodeEditor>().FirstOrDefault(x => x != null && x.Id == nodeId);
            InitStepConfig removedCfg = n != null ? n.InitStepConfig : null;

            // Let the base remove edges and the node registration.
            base.RemoveNode(nodeId);

#if UNITY_EDITOR
            // Delete the node's config sub-asset.
            if (n != null && removedCfg != null)
            {
                // Remove dependency references in *other* configs.
                PurgeConfigFromAllDependencies(removedCfg);

                // Remove sub-asset from the node and destroy.
                try
                {
                    AssetDatabase.RemoveObjectFromAsset(removedCfg);
                    DestroyImmediate(removedCfg, true);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
#endif
        }

        /// <summary>
        /// Removes occurrences of a config from every other InitStepConfig.StepDependencies array.
        /// </summary>
        private void PurgeConfigFromAllDependencies(InitStepConfig toRemove)
        {
            InitStepNodeEditor[] allStepNodes = Nodes.OfType<InitStepNodeEditor>().ToArray();
            foreach (InitStepNodeEditor stepNode in allStepNodes)
            {
                InitStepConfig cfg = stepNode?.InitStepConfig;
                if (cfg == null || cfg.StepDependencies == null || cfg.StepDependencies.Length == 0)
                    continue;

                List<InitStepConfig> list = new(cfg.StepDependencies);
                if (list.RemoveAll(x => x == null || ReferenceEquals(x, toRemove)) > 0)
                {
                    cfg.StepDependencies = list.ToArray();
#if UNITY_EDITOR
                    EditorUtility.SetDirty(cfg);
#endif
                }
            }
        }

        // -------------------- Edge dependency synchronization --------------------

        /// <summary>
        /// Legacy helper: unidirectional by default.
        /// </summary>
        public override bool TryAddEdge(string fromId, string toId, out string error)
        {
            return TryAddEdge(fromId, toId, NodeConnectionDirection.Unidirectional, out error);
        }

        /// <summary>
        /// Tries to add an edge by string Ids (redirects to INode overload which syncs dependencies).
        /// </summary>
        public override bool TryAddEdge(string fromId, string toId, NodeConnectionDirection direction, out string error)
        {
            // Resolve endpoints before calling the INode overload.
            Node from = Nodes.OfType<Node>().FirstOrDefault(n => n.Id == fromId);
            Node to = Nodes.OfType<Node>().FirstOrDefault(n => n.Id == toId);

            // Delegate to the INode overload that already synchronizes dependencies.
            bool ok = TryAddEdge(from, to, direction, out _, out error);
            return ok;
        }

        /// <summary>
        /// Ensures StepDependencies reflect an added edge: From -> To means From depends on To.
        /// </summary>
        private static void OnEdgeCreated(InitStepNodeEditor from, InitStepNodeEditor to)
        {
            if (from == null || to == null) return;
            InitStepConfig fromCfg = from.InitStepConfig;
            InitStepConfig toCfg = to.InitStepConfig;
            if (fromCfg == null || toCfg == null) return;

            List<InitStepConfig> list = new(fromCfg.StepDependencies ?? Array.Empty<InitStepConfig>());
            if (!list.Contains(toCfg))
            {
                list.Add(toCfg);
                fromCfg.StepDependencies = list.ToArray();
#if UNITY_EDITOR
                EditorUtility.SetDirty(fromCfg);
#endif
            }
        }

        /// <summary>
        /// Removes a single directed dependency From -> To when an edge is removed.
        /// </summary>
        private static void OnEdgeRemoved(InitStepNodeEditor from, InitStepNodeEditor to)
        {
            if (from == null || to == null) return;
            InitStepConfig fromCfg = from.InitStepConfig;
            InitStepConfig toCfg = to.InitStepConfig;
            if (fromCfg == null || toCfg == null) return;

            List<InitStepConfig> deps = new(fromCfg.StepDependencies ?? Array.Empty<InitStepConfig>());
            if (deps.Remove(toCfg))
            {
                fromCfg.StepDependencies = deps.ToArray();
#if UNITY_EDITOR
                EditorUtility.SetDirty(fromCfg);
#endif
            }
        }

        /// <summary>
        /// Intercepts all edge creations (including the normal UI flow via Node.ConnectTo)
        /// to synchronize InitStepConfig.StepDependencies.
        /// </summary>
        public override bool TryAddEdge(
            INode from,
            INode to,
            NodeConnectionDirection direction,
            out INodeConnection connection,
            out string error)
        {
            bool ok = base.TryAddEdge(from, to, direction, out connection, out error);
            if (!ok) return false;

            InitStepNodeEditor a = from as InitStepNodeEditor;
            InitStepNodeEditor b = to as InitStepNodeEditor;

            if (a != null && b != null)
            {
                if (direction == NodeConnectionDirection.Unidirectional)
                {
                    OnEdgeCreated(a, b); // A -> B: A depends on B
                }
                else if (direction == NodeConnectionDirection.Bidirectional)
                {
                    OnEdgeCreated(a, b);
                    OnEdgeCreated(b, a);
                }

#if UNITY_EDITOR
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssets();
#endif
            }

            return true;
        }

        /// <summary>
        /// When pasting/duplicating edges via the clipboard utility, Graph calls this hook.
        /// We mirror dependency creation here as well.
        /// </summary>
        public override void AddEdgeBetween(INode from, INode to, NodeConnectionDirection direction)
        {
            base.AddEdgeBetween(from, to, direction);

            if (direction == NodeConnectionDirection.Unidirectional &&
                from is InitStepNodeEditor a && to is InitStepNodeEditor b)
            {
                OnEdgeCreated(a, b);
            }
            else if (direction == NodeConnectionDirection.Bidirectional &&
                     from is InitStepNodeEditor ab && to is InitStepNodeEditor ba)
            {
                OnEdgeCreated(ab, ba);
                OnEdgeCreated(ba, ab);
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
#endif
        }

        /// <summary>
        /// Removes a graph edge and keeps InitStepConfig.StepDependencies in sync.
        /// Also removes inverse dependency to cover potential bidirectional edges.
        /// </summary>
        public override void RemoveEdge(string fromId, string toId)
        {
            // Obtain node references before removal.
            InitStepNodeEditor fromNode = Nodes?.OfType<InitStepNodeEditor>().FirstOrDefault(n => n.Id == fromId);
            InitStepNodeEditor toNode = Nodes?.OfType<InitStepNodeEditor>().FirstOrDefault(n => n.Id == toId);

            base.RemoveEdge(fromId, toId);

            if (fromNode != null && toNode != null)
            {
                // Remove dependency A->B
                OnEdgeRemoved(fromNode, toNode);
                // Remove dependency B->A (covers bidirectional; if not present, no change)
                OnEdgeRemoved(toNode, fromNode);
#if UNITY_EDITOR
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssets();
#endif
            }
        }
    }
}
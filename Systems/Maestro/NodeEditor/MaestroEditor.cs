using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryTools.GraphV2;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using LegendaryTools.NodeEditor;

namespace LegendaryTools.Maestro.NodeEditor
{
    /// <summary>
    /// Maestro graph: inherits generic node/config management from DefaultGraph and
    /// synchronizes InitStepConfig dependencies via Graph events (EdgeAdded/EdgeRemoved/NodeRemoved).
    /// </summary>
    [CreateAssetMenu(menuName = "Tools/Maestro/MaestroEditor")]
    public class MaestroEditor : DefaultGraph<InitStepNodeEditor, InitStepConfig>
    {
        public Maestro Maestro;

        private readonly MaestroNodeEditorMenu maestroNodeEditorMenu = new();
        public override IEdgeContextMenuProvider EdgeMenuProvider => maestroNodeEditorMenu;
        public override IGraphContextMenuProvider GraphMenuProvider => maestroNodeEditorMenu;
        public override IToolbarProvider ToolbarProvider => maestroNodeEditorMenu;

        // -------------------- Lifecycle: subscribe to graph events --------------------

        protected override void OnEnable()
        {
            base.OnEnable();

            // Unsubscribe first to avoid duplicates after domain reloads
            EdgeAdded -= HandleEdgeAdded;
            EdgeRemoved -= HandleEdgeRemoved;
            NodeRemoved -= HandleNodeRemovedDependencies;

            EdgeAdded += HandleEdgeAdded;
            EdgeRemoved += HandleEdgeRemoved;
            NodeRemoved += HandleNodeRemovedDependencies;
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            EdgeAdded -= HandleEdgeAdded;
            EdgeRemoved -= HandleEdgeRemoved;
            NodeRemoved -= HandleNodeRemovedDependencies;
        }

        // -------------------- Dependency sync handlers --------------------

        /// <summary>
        /// When an edge is added, mirror dependency: From depends on To.
        /// Bidirectional edges will arrive as a single Edge with Direction == Bidirectional;
        /// we record both A->B and B->A.
        /// </summary>
        protected virtual void HandleEdgeAdded(LegendaryTools.NodeEditor.Graph graph, Edge edge)
        {
            if (graph != this || edge == null) return;

            InitStepNodeEditor a = edge.From as InitStepNodeEditor;
            InitStepNodeEditor b = edge.To as InitStepNodeEditor;
            if (a == null || b == null) return;

            if (edge.Direction == NodeConnectionDirection.Unidirectional)
            {
                AddDependency(a, b);
            }
            else // Bidirectional
            {
                AddDependency(a, b);
                AddDependency(b, a);
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
#endif
        }

        /// <summary>
        /// When an edge is removed, remove both A->B and B->A to cover bidirectional cases.
        /// </summary>
        protected virtual void HandleEdgeRemoved(LegendaryTools.NodeEditor.Graph graph, Edge edge)
        {
            if (graph != this || edge == null) return;

            InitStepNodeEditor a = edge.From as InitStepNodeEditor;
            InitStepNodeEditor b = edge.To as InitStepNodeEditor;
            if (a == null || b == null) return;

            RemoveDependency(a, b);
            RemoveDependency(b, a);

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
#endif
        }

        /// <summary>
        /// When a node is removed, purge its config from other nodes' dependency arrays.
        /// </summary>
        protected virtual void HandleNodeRemovedDependencies(LegendaryTools.NodeEditor.Graph graph,
            LegendaryTools.NodeEditor.Node node)
        {
            if (graph != this) return;
            InitStepNodeEditor step = node as InitStepNodeEditor;
            InitStepConfig cfg = step != null ? step.InitStepConfig : null;
            if (cfg == null) return;

            PurgeConfigFromAllDependencies(cfg);
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
#endif
        }

        // -------------------- Dependency utilities --------------------

        protected virtual void AddDependency(InitStepNodeEditor from, InitStepNodeEditor to)
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

        protected virtual void RemoveDependency(InitStepNodeEditor from, InitStepNodeEditor to)
        {
            if (from == null || to == null) return;

            InitStepConfig fromCfg = from.InitStepConfig;
            InitStepConfig toCfg = to.InitStepConfig;
            if (fromCfg == null || toCfg == null) return;

            List<InitStepConfig> list = new(fromCfg.StepDependencies ?? Array.Empty<InitStepConfig>());
            if (list.Remove(toCfg))
            {
                fromCfg.StepDependencies = list.ToArray();
#if UNITY_EDITOR
                EditorUtility.SetDirty(fromCfg);
#endif
            }
        }

        protected virtual void PurgeConfigFromAllDependencies(InitStepConfig toRemove)
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
    }
}
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using LegendaryTools.NodeEditor;

namespace LegendaryTools.Maestro.NodeEditor
{
    /// <summary>
    /// Provides node/edge/graph context menus and toolbar entries that list all concrete
    /// InitStepConfig types and create corresponding InitStepNodeEditor nodes on demand.
    /// Mirrors default behaviors and extends them with "Create Step" actions.
    /// </summary>
    public sealed class MaestroNodeEditorMenu :
        INodeContextMenuProvider,
        IEdgeContextMenuProvider,
        IGraphContextMenuProvider,
        IToolbarProvider
    {
        // Cache of discovered InitStepConfig concrete types.
        private static List<Type> _cachedConfigTypes;

        // Delegates to reuse default behaviors and then extend with Maestro-specific items.
        private readonly DefaultNodeMenuProvider _defaultNodeMenu = new();
        private readonly DefaultEdgeMenuProvider _defaultEdgeMenu = new();
        private readonly DefaultGraphMenuProvider _defaultGraphMenu = new(false);
        private readonly DefaultToolbarProvider _defaultToolbar = new();

        /// <summary>
        /// Builds a list of non-abstract types derived from InitStepConfig.
        /// Uses TypeCache for editor-time performance.
        /// </summary>
        private static IReadOnlyList<Type> GetConfigTypes()
        {
            if (_cachedConfigTypes != null) return _cachedConfigTypes;

            TypeCache.TypeCollection all = TypeCache.GetTypesDerivedFrom<InitStepConfig>();
            _cachedConfigTypes = all
                .Where(t => t != null && !t.IsAbstract && typeof(InitStepConfig).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToList();
            return _cachedConfigTypes;
        }

        /// <summary>
        /// Adds "Create Step/{TypeName}" items that instantiate an InitStepNodeEditor with
        /// a sub-asset InitStepConfig of that specific type.
        /// </summary>
        private static void BuildCreateStepItems(
            IMenuBuilder menu,
            MaestroEditor graph,
            Func<Vector2> positionProvider,
            string rootPath = "Maestro/Create Step")
        {
            if (graph == null)
            {
                menu.AddItem($"{rootPath}/(Not a Maestro graph)", null, false);
                return;
            }

            IReadOnlyList<Type> types = GetConfigTypes();
            if (types.Count == 0)
            {
                menu.AddItem($"{rootPath}/(No InitStepConfig types found)", null, false);
                return;
            }

            foreach (Type t in types)
            {
                string path = $"{rootPath}/{t.Name}";
                menu.AddItem(path, () =>
                {
                    Vector2 pos = positionProvider != null ? positionProvider() : Vector2.zero;

                    Undo.RecordObject(graph, "Create Maestro Step");
                    graph.CreateStepNode(t, t.Name, pos);
                    EditorUtility.SetDirty(graph);
                    AssetDatabase.SaveAssets();
                });
            }
        }

        // -------------------- INodeContextMenuProvider --------------------

        /// <summary>
        /// Extends the default node menu with a "Create Step" submenu that spawns nodes
        /// near the first selected node.
        /// </summary>
        public void Build(NodeMenuContext context, IMenuBuilder menu)
        {
            // Keep default node actions.
            _defaultNodeMenu.Build(context, menu);

            // Then add Maestro creation submenu.
            menu.AddSeparator("");
            MaestroEditor graph = context.Graph as MaestroEditor;

            Vector2 PositionProvider()
            {
                IEditorNode n = context.Nodes.FirstOrDefault();
                if (n == null) return Vector2.zero;
                // Place to the right of the selected node.
                return n.Position + new Vector2(220f, 0f);
            }

            BuildCreateStepItems(menu, graph, PositionProvider, "Maestro/Create Step");
        }

        // -------------------- IEdgeContextMenuProvider --------------------

        /// <summary>
        /// Keeps default edge menu (delete).
        /// </summary>
        public void Build(EdgeMenuContext context, IMenuBuilder menu)
        {
            _defaultEdgeMenu.Build(context, menu);
        }

        // -------------------- IGraphContextMenuProvider --------------------

        /// <summary>
        /// Extends the default graph (canvas) menu with a "Create Step" submenu
        /// that drops the node at the click position.
        /// </summary>
        public void Build(GraphMenuContext context, IMenuBuilder menu)
        {
            // Keep default graph actions.
            _defaultGraphMenu.Build(context, menu);

            // Then add Maestro-specific creation submenu.
            menu.AddSeparator("");
            MaestroEditor graph = context.Graph as MaestroEditor;

            Vector2 PositionProvider()
            {
                return context.LogicalClickPosition;
            }

            BuildCreateStepItems(menu, graph, PositionProvider, "Maestro/Create Step");
        }

        // -------------------- IToolbarProvider --------------------

        /// <summary>
        /// Extends the default toolbar with an "Add Step" dropdown listing all concrete
        /// InitStepConfig types. Nodes are placed at (0,0) by default.
        /// </summary>
        public void Build(ToolbarContext context, IToolbarBuilder toolbar)
        {
            // Keep the default toolbar.
            _defaultToolbar.Build(context, toolbar);

            // Add a separator and our dropdown.
            toolbar.AddSeparator();

            toolbar.AddDropdown("Add Step", menu =>
            {
                MaestroEditor graph = context.Graph as MaestroEditor;
                BuildCreateStepItems(
                    menu,
                    graph,
                    () => Vector2.zero,
                    "Maestro/Add Step");
            });
        }
    }
}
#endif
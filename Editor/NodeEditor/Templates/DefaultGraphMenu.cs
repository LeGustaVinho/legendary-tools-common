#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Generic menu/toolbar builder for node-creation UX.
    /// Subclasses may extend or override to add edge-specific UI.
    /// </summary>
    public class DefaultGraphMenu<TNode, TConfig> :
        INodeContextMenuProvider,
        IEdgeContextMenuProvider,
        IGraphContextMenuProvider,
        IToolbarProvider
        where TNode : Node, IHasConfig<TConfig>
        where TConfig : ScriptableObject
    {
        protected readonly DefaultNodeMenuProvider _defaultNodeMenu;
        protected readonly DefaultEdgeMenuProvider _defaultEdgeMenu;
        protected readonly DefaultGraphMenuProvider _defaultGraphMenu;
        protected readonly DefaultToolbarProvider _defaultToolbar;

        public DefaultGraphMenu(bool includeDefaults = true)
        {
            _defaultNodeMenu = new DefaultNodeMenuProvider();
            _defaultEdgeMenu = new DefaultEdgeMenuProvider();
            _defaultGraphMenu = new DefaultGraphMenuProvider(includeDefaults);
            _defaultToolbar = new DefaultToolbarProvider(includeDefaults);
        }

        private static List<Type> _cachedConfigTypes;

        protected static IReadOnlyList<Type> GetConfigTypes()
        {
            if (_cachedConfigTypes != null) return _cachedConfigTypes;

            TypeCache.TypeCollection all = TypeCache.GetTypesDerivedFrom<TConfig>();
            _cachedConfigTypes = all
                .Where(t => t != null && !t.IsAbstract && typeof(TConfig).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToList();

            return _cachedConfigTypes;
        }

        protected virtual void BuildCreateStepItems(
            IMenuBuilder menu,
            DefaultGraph<TNode, TConfig> graph,
            Func<Vector2> positionProvider,
            string rootPath = "Create/Node")
        {
            if (graph == null)
            {
                menu.AddItem($"{rootPath}/(Incompatible Graph)", null, false);
                return;
            }

            IReadOnlyList<Type> types = GetConfigTypes();
            if (types.Count == 0)
            {
                menu.AddItem($"{rootPath}/(No {typeof(TConfig).Name} types found)", null, false);
                return;
            }

            foreach (Type t in types)
            {
                string path = $"{rootPath}/{t.Name}";
                menu.AddItem(path, () =>
                {
                    Vector2 pos = positionProvider != null ? positionProvider() : Vector2.zero;

                    Undo.RecordObject(graph, $"Create {typeof(TNode).Name}");
                    graph.CreateStepNode(t, t.Name, pos);
                    EditorUtility.SetDirty(graph);
                    AssetDatabase.SaveAssets();
                });
            }
        }

        // Node context menu
        public virtual void Build(NodeMenuContext context, IMenuBuilder menu)
        {
            _defaultNodeMenu.Build(context, menu);

            menu.AddSeparator("");
            DefaultGraph<TNode, TConfig> graph = context.Graph as DefaultGraph<TNode, TConfig>;

            Vector2 PositionProvider()
            {
                IEditorNode n = context.Nodes.FirstOrDefault();
                if (n == null) return Vector2.zero;
                return n.Position + new Vector2(220f, 0f);
            }

            BuildCreateStepItems(menu, graph, PositionProvider, $"{typeof(TConfig).Name}/Create");
        }

        // Edge context menu
        public virtual void Build(EdgeMenuContext context, IMenuBuilder menu)
        {
            // Keep default behavior (e.g., delete edge).
            _defaultEdgeMenu.Build(context, menu);
        }

        // Graph (canvas) context menu
        public virtual void Build(GraphMenuContext context, IMenuBuilder menu)
        {
            _defaultGraphMenu.Build(context, menu);

            menu.AddSeparator("");
            DefaultGraph<TNode, TConfig> graph = context.Graph as DefaultGraph<TNode, TConfig>;

            Vector2 PositionProvider()
            {
                return context.LogicalClickPosition;
            }

            BuildCreateStepItems(menu, graph, PositionProvider, $"{typeof(TConfig).Name}/Create");
        }

        // Toolbar
        public virtual void Build(ToolbarContext context, IToolbarBuilder toolbar)
        {
            _defaultToolbar.Build(context, toolbar);

            toolbar.AddSeparator();
            toolbar.AddDropdown($"Add {typeof(TNode).Name}", menu =>
            {
                DefaultGraph<TNode, TConfig> graph = context.Graph as DefaultGraph<TNode, TConfig>;
                BuildCreateStepItems(menu, graph, () => Vector2.zero, $"Add {typeof(TNode).Name}");
            });
        }
    }
}
#endif
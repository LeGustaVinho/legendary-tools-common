#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Describes the context available to toolbar providers. Providers should use this instead of
/// referencing editor singletons or the window directly.
/// </summary>
public sealed class ToolbarContext
{
    /// <summary>Current graph loaded in the editor window (get/set).</summary>
    public DagGraph Graph { get; set; }

    /// <summary>Selected nodes and edges (read-only views).</summary>
    public IReadOnlyCollection<IDagNode> SelectedNodes { get; }

    public IReadOnlyCollection<IDagEdge<IDagNode>> SelectedEdges { get; }

    /// <summary>Virtual canvas extent for layout-sensitive features.</summary>
    public Vector2 VirtualCanvasSize { get; }

    /// <summary>True when the editor is currently in connection mode.</summary>
    public bool IsConnectionMode { get; }

    /// <summary>Creates a new graph asset and loads it in the window.</summary>
    public Action CreateNewAsset { get; }

    /// <summary>Adds a node at the logical center of the current viewport.</summary>
    public Action AddNodeAtViewportCenter { get; }

    /// <summary>Requests a repaint of the host window.</summary>
    public Action Repaint { get; }

    /// <summary>Shows a small in-editor notification.</summary>
    public Action<string> Notify { get; }

    /// <summary>Allows providers to store transient state across frames.</summary>
    public IDictionary<string, object> State { get; }

    public ToolbarContext(
        DagGraph graph,
        IReadOnlyCollection<IDagNode> selectedNodes,
        IReadOnlyCollection<IDagEdge<IDagNode>> selectedEdges,
        Vector2 virtualCanvasSize,
        bool isConnectionMode,
        Action createNewAsset,
        Action addNodeAtViewportCenter,
        Action repaint,
        Action<string> notify,
        IDictionary<string, object> sharedState)
    {
        Graph = graph;
        SelectedNodes = selectedNodes;
        SelectedEdges = selectedEdges;
        VirtualCanvasSize = virtualCanvasSize;
        IsConnectionMode = isConnectionMode;
        CreateNewAsset = createNewAsset ?? (() => { });
        AddNodeAtViewportCenter = addNodeAtViewportCenter ?? (() => { });
        Repaint = repaint ?? (() => { });
        Notify = notify ?? (_ => { });
        State = sharedState ?? new Dictionary<string, object>();
    }
}

/// <summary>
/// Abstraction for building a toolbar without using Unity GUI directly. Providers call these methods
/// and the host window renders the concrete UI.
/// </summary>
public interface IToolbarBuilder
{
    /// <summary>Adds a labeled button.</summary>
    void AddButton(string text, Action onClick, Func<bool> enabled = null, string tooltip = null);

    /// <summary>
    /// Adds a dropdown button whose items are declared via a standard <see cref="IMenuBuilder"/>.
    /// </summary>
    void AddDropdown(string text, Action<IMenuBuilder> buildMenu, Func<bool> enabled = null, string tooltip = null);

    /// <summary>Adds a static label whose text is computed dynamically each frame.</summary>
    void AddLabel(Func<string> textProvider, string tooltip = null);

    /// <summary>Adds a toggle input.</summary>
    void AddToggle(Func<bool> get, Action<bool> set, string label = null, Func<bool> enabled = null,
        string tooltip = null);

    /// <summary>Adds an object field (asset picker) with fixed width.</summary>
    void AddObjectField<TObj>(Func<UnityEngine.Object> get, Action<UnityEngine.Object> set, float width,
        string tooltip = null)
        where TObj : UnityEngine.Object;

    /// <summary>Adds a single-line text field with fixed width.</summary>
    void AddTextField(Func<string> get, Action<string> set, float width, string placeholder = null,
        Func<bool> enabled = null, string tooltip = null);

    /// <summary>Adds a flexible space (pushes next items to the right).</summary>
    void AddFlexibleSpace();

    /// <summary>Adds a vertical separator.</summary>
    void AddSeparator();
}

/// <summary>
/// Provider for toolbar extensions. Implementations can add buttons, inputs, submenus, etc.
/// </summary>
public interface IToolbarProvider
{
    /// <summary>Builds toolbar items for the given context.</summary>
    void Build(ToolbarContext context, IToolbarBuilder toolbar);
}
#endif
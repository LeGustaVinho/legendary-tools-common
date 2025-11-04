#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Aggregates editor-time, mutable state that multiple services need to collaborate on.
/// </summary>
public class DagEditorContext
{
    /// <summary>Backed ScriptableObject graph asset.</summary>
    public DagGraph Graph;

    /// <summary>Current logical scroll (canvas units, 1:1 scale).</summary>
    public Vector2 Scroll;

    /// <summary>Legacy zoom field kept for compatibility; enforced at 1.</summary>
    public float Zoom = 1f;

    /// <summary>Virtual canvas extent for clamping scroll.</summary>
    public Vector2 VirtualCanvasSize = new(8000f, 8000f);

    /// <summary>Current connection source node id (GUID string). Null when not connecting.</summary>
    public string PendingFromNodeId;

    /// <summary>Nodes selected (abstractions to keep LSP).</summary>
    public readonly HashSet<IDagNode> SelectedNodes = new();

    /// <summary>Edges selected (abstractions to keep LSP).</summary>
    public readonly HashSet<IDagEdge<IDagNode>> SelectedEdges = new();

    /// <summary>Inspector editors cache: node.</summary>
    public Editor CachedNodeEditor;

    /// <summary>Inspector editors cache: edge.</summary>
    public Editor CachedEdgeEditor;

    /// <summary>Marquee selection runtime state.</summary>
    public readonly MarqueeState Marquee = new();

    /// <summary>Resize runtime state.</summary>
    public readonly ResizeState Resize = new();

    /// <summary>Shared reflection cache for [ShowInNode] fields.</summary>
    public readonly Dictionary<System.Type, System.Reflection.FieldInfo[]> ShowInNodeCache = new();
}

/// <summary>
/// Runtime state for marquee selection.
/// </summary>
public sealed class MarqueeState
{
    /// <summary>True when the user is currently dragging a marquee.</summary>
    public bool Active;

    /// <summary>Logical start point.</summary>
    public Vector2 Start;

    /// <summary>Logical end point.</summary>
    public Vector2 End;
}

/// <summary>
/// Runtime state for node resizing.
/// </summary>
public sealed class ResizeState
{
    /// <summary>True when a resize operation is running.</summary>
    public bool Active;

    /// <summary>The node being resized (abstraction to preserve LSP).</summary>
    public IDagNode Node;

    /// <summary>Logical rectangle at the start of the operation.</summary>
    public Rect StartRect;

    /// <summary>Logical mouse point at the start.</summary>
    public Vector2 StartMouse;

    /// <summary>Which sides are being dragged.</summary>
    public bool Left, Right, Top, Bottom;
}

/// <summary>
/// Value object representing immutable layout constants used by the editor.
/// </summary>
public readonly struct DagEditorLayout
{
    /// <summary>Toolbar height + dock layout offsets.</summary>
    public const float ToolbarHeight = 20f;

    /// <summary>Inspector panel width.</summary>
    public const float InspectorWidth = 320f;

    /// <summary>Scrollbar thickness.</summary>
    public const float ScrollbarThickness = 16f;

    /// <summary>Default node width used when node has no custom size.</summary>
    public const float DefaultNodeWidth = 200f;

    /// <summary>Base node height, before inline fields (ShowInNode) are added.</summary>
    public const float DefaultNodeBaseHeight = 80f;

    /// <summary>Inline field preferred height.</summary>
    public const float InlineFieldHeight = 20f;

    /// <summary>Minimum node width clamp for resizing.</summary>
    public const float MinNodeWidth = 100f;

    /// <summary>Minimum node height clamp for resizing.</summary>
    public const float MinNodeHeight = 60f;

    /// <summary>Pixel thickness of the resize sensitive band around node borders.</summary>
    public const float ResizeBandPx = 6f;
}
#endif
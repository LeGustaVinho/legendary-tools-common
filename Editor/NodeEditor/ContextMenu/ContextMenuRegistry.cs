#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// Registry that holds all context menu providers. External extensions should call the Register methods
/// to contribute new menu entries without modifying editor window code.
/// </summary>
public static class ContextMenuRegistry
{
    private static readonly List<INodeContextMenuProvider> _nodeProviders = new();
    private static readonly List<IEdgeContextMenuProvider> _edgeProviders = new();
    private static readonly List<IGraphContextMenuProvider> _graphProviders = new();

    /// <summary>Registers a node menu provider.</summary>
    public static void Register(INodeContextMenuProvider provider)
    {
        if (provider != null) _nodeProviders.Add(provider);
    }

    /// <summary>Registers an edge menu provider.</summary>
    public static void Register(IEdgeContextMenuProvider provider)
    {
        if (provider != null) _edgeProviders.Add(provider);
    }

    /// <summary>Registers a graph menu provider.</summary>
    public static void Register(IGraphContextMenuProvider provider)
    {
        if (provider != null) _graphProviders.Add(provider);
    }

    /// <summary>Enumerates current node providers.</summary>
    public static IEnumerable<INodeContextMenuProvider> NodeProviders => _nodeProviders;

    /// <summary>Enumerates current edge providers.</summary>
    public static IEnumerable<IEdgeContextMenuProvider> EdgeProviders => _edgeProviders;

    /// <summary>Enumerates current graph providers.</summary>
    public static IEnumerable<IGraphContextMenuProvider> GraphProviders => _graphProviders;
}
#endif
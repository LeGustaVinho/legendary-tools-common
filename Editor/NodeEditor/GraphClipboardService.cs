#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Serializes a selection of nodes (and internal edges) to the system clipboard,
/// and restores them with new IDs preserving relative offsets.
/// </summary>
public static class GraphClipboardService
{
    private const string Header = "DAGCLIP_v1"; // simple magic header to recognize our payload

    [Serializable]
    private class NodePayload
    {
        public string type;
        public string json; // JsonUtility dump of the node
        public Vector2 position; // original logical position (for relative offset preservation)
    }

    [Serializable]
    private class EdgePayload
    {
        public int from; // index in nodes list
        public int to; // index in nodes list
    }

    [Serializable]
    private class ClipboardPayload
    {
        public string header;
        public List<NodePayload> nodes = new();
        public List<EdgePayload> edges = new();
    }

    /// <summary>
    /// Copies the given nodes and internal edges to the system clipboard.
    /// </summary>
    public static void Copy(DagGraph graph, IReadOnlyList<IDagNode> selection)
    {
        if (graph == null || selection == null || selection.Count == 0) return;

        List<DagNode> concrete = selection.OfType<DagNode>().ToList();
        Dictionary<DagNode, int> mapIndex = new();
        ClipboardPayload payload = new() { header = Header };

        // Nodes
        for (int i = 0; i < concrete.Count; i++)
        {
            DagNode n = concrete[i];
            mapIndex[n] = i;
            payload.nodes.Add(new NodePayload
            {
                type = n.GetType().AssemblyQualifiedName,
                json = JsonUtility.ToJson(n),
                position = n.Position
            });
        }

        // Internal edges
        foreach (DagEdge e in graph.Edges.OfType<DagEdge>())
        {
            if (e == null || e.FromConcrete == null || e.ToConcrete == null) continue;
            if (!mapIndex.ContainsKey(e.FromConcrete) || !mapIndex.ContainsKey(e.ToConcrete)) continue;

            payload.edges.Add(new EdgePayload
            {
                from = mapIndex[e.FromConcrete],
                to = mapIndex[e.ToConcrete]
            });
        }

        EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(payload);
    }

    /// <summary>
    /// Attempts to paste the clipboard content into the graph at <paramref name="pasteOrigin"/>.
    /// Returns the new nodes pasted (selection convenience).
    /// </summary>
    public static List<DagNode> TryPaste(DagGraph graph, Vector2 pasteOrigin)
    {
        string buf = EditorGUIUtility.systemCopyBuffer;
        if (string.IsNullOrEmpty(buf)) return null;

        ClipboardPayload payload;
        try
        {
            payload = JsonUtility.FromJson<ClipboardPayload>(buf);
        }
        catch
        {
            return null;
        }

        if (payload == null || payload.header != Header || payload.nodes == null || payload.nodes.Count == 0)
            return null;

        // Compute relative offset: paste selected cluster keeping original layout
        Vector2 min = payload.nodes.Aggregate(new Vector2(float.MaxValue, float.MaxValue),
            (acc, np) => new Vector2(Mathf.Min(acc.x, np.position.x), Mathf.Min(acc.y, np.position.y)));

        List<DagNode> newNodes = new(payload.nodes.Count);

        // Create nodes
        foreach (NodePayload np in payload.nodes)
        {
            Type type = Type.GetType(np.type);
            if (type == null || !typeof(DagNode).IsAssignableFrom(type)) continue;

            DagNode temp = ScriptableObject.CreateInstance(type) as DagNode;
            JsonUtility.FromJsonOverwrite(np.json, temp);

            Vector2 rel = np.position - min;
            Vector2 newPos = pasteOrigin + rel;

            DagNode created = graph.CreateNodeClone(temp, newPos);
            newNodes.Add(created);

            UnityEngine.Object.DestroyImmediate(temp);
        }

        // Recreate internal edges with new nodes mapping
        for (int i = 0; i < payload.edges.Count; i++)
        {
            EdgePayload ep = payload.edges[i];
            if (ep.from < 0 || ep.from >= newNodes.Count) continue;
            if (ep.to < 0 || ep.to >= newNodes.Count) continue;

            graph.AddEdgeBetween(newNodes[ep.from], newNodes[ep.to]);
        }

        AssetDatabase.SaveAssets();
        return newNodes;
    }

    /// <summary>
    /// Duplicates the current selection with a small offset, preserving relative spacing and internal edges.
    /// </summary>
    public static List<DagNode> Duplicate(DagGraph graph, IReadOnlyList<IDagNode> selection, Vector2 offset)
    {
        if (graph == null || selection == null || selection.Count == 0) return null;

        List<DagNode> concrete = selection.OfType<DagNode>().ToList();
        Vector2 min = concrete.Aggregate(new Vector2(float.MaxValue, float.MaxValue),
            (acc, n) => new Vector2(Mathf.Min(acc.x, n.Position.x), Mathf.Min(acc.y, n.Position.y)));

        // Create clone mapping
        Dictionary<DagNode, DagNode> map = new();
        foreach (DagNode n in concrete)
        {
            Vector2 rel = n.Position - min;
            DagNode created = graph.CreateNodeClone(n, min + rel + offset);
            map[n] = created;
        }

        // Recreate internal edges
        foreach (DagEdge e in graph.Edges.OfType<DagEdge>())
        {
            if (e == null || e.FromConcrete == null || e.ToConcrete == null) continue;
            if (!map.ContainsKey(e.FromConcrete) || !map.ContainsKey(e.ToConcrete)) continue;

            graph.AddEdgeBetween(map[e.FromConcrete], map[e.ToConcrete]);
        }

        AssetDatabase.SaveAssets();
        return map.Values.ToList();
    }
}
#endif
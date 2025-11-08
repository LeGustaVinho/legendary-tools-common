#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using LegendaryTools.GraphV2; // For NodeConnectionDirection

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Serializes a selection of nodes (and internal edges) to the system clipboard,
    /// and restores them with new IDs preserving relative offsets.
    /// </summary>
    public static class GraphClipboardService
    {
        private const string Header = "NODE_EDITOR_CLIP_v1"; // Simple magic header to recognize our payload

        [Serializable]
        private class NodePayload
        {
            public string type;
            public string json; // JsonUtility dump of the node
            public Vector2 position; // Original logical position (for relative offset preservation)
        }

        [Serializable]
        private class EdgePayload
        {
            public int from; // Index in nodes list
            public int to; // Index in nodes list
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
        public static void Copy(Graph graph, IReadOnlyList<IEditorNode> selection)
        {
            if (graph == null || selection == null || selection.Count == 0) return;

            // We need concrete Node instances to create an index map.
            List<Node> concrete = selection.OfType<Node>().ToList();
            if (concrete.Count == 0) return;

            Dictionary<Node, int> mapIndex = new();
            ClipboardPayload payload = new() { header = Header };

            // Nodes
            for (int i = 0; i < concrete.Count; i++)
            {
                Node n = concrete[i];
                mapIndex[n] = i;
                payload.nodes.Add(new NodePayload
                {
                    type = n.GetType().AssemblyQualifiedName,
                    json = JsonUtility.ToJson(n),
                    position = n.Position
                });
            }

            // Internal edges: iterate abstract edges and add only those with both endpoints in the selection
            foreach (IEditorNodeEdge<IEditorNode> edge in graph.Edges)
            {
                if (edge == null) continue;

                // Endpoints are the abstract IEditorNode; we only proceed if they are Node
                Node from = edge.From as Node;
                Node to = edge.To as Node;
                if (from == null || to == null) continue;

                if (!mapIndex.TryGetValue(from, out int fromIdx)) continue;
                if (!mapIndex.TryGetValue(to, out int toIdx)) continue;

                payload.edges.Add(new EdgePayload
                {
                    from = fromIdx,
                    to = toIdx
                });
            }

            EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(payload);
        }

        /// <summary>
        /// Attempts to paste the clipboard content into the graph at <paramref name="pasteOrigin"/>.
        /// Returns the new nodes pasted (selection convenience).
        /// </summary>
        public static List<Node> TryPaste(Graph graph, Vector2 pasteOrigin)
        {
            if (graph == null) return null;

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

            List<Node> newNodes = new(payload.nodes.Count);

            // Create nodes (clone path) and place them with the preserved relative offset
            foreach (NodePayload np in payload.nodes)
            {
                Type type = Type.GetType(np.type);
                if (type == null || !typeof(Node).IsAssignableFrom(type)) continue;

                // Create a temporary node with the same serialized data,
                // then use the graph's clone routine to produce a proper sub-asset with a new Id.
                Node temp = ScriptableObject.CreateInstance(type) as Node;
                JsonUtility.FromJsonOverwrite(np.json, temp);

                Vector2 rel = np.position - min;
                Vector2 newPos = pasteOrigin + rel;

                Node created = graph.CreateNodeClone(temp, newPos);
                newNodes.Add(created);

                UnityEngine.Object.DestroyImmediate(temp);
            }

            // Recreate internal edges among the newly created nodes.
            // By design, clipboard edges are Unidirectional (same behavior as previous implementation).
            if (payload.edges != null)
                for (int i = 0; i < payload.edges.Count; i++)
                {
                    EdgePayload ep = payload.edges[i];
                    if (ep.from < 0 || ep.from >= newNodes.Count) continue;
                    if (ep.to < 0 || ep.to >= newNodes.Count) continue;

                    graph.AddEdgeBetween(newNodes[ep.from], newNodes[ep.to], NodeConnectionDirection.Unidirectional);
                }

            AssetDatabase.SaveAssets();
            return newNodes;
        }

        /// <summary>
        /// Duplicates the current selection with a small offset, preserving relative spacing and internal edges.
        /// </summary>
        public static List<Node> Duplicate(Graph graph, IReadOnlyList<IEditorNode> selection, Vector2 offset)
        {
            if (graph == null || selection == null || selection.Count == 0) return null;

            List<Node> concrete = selection.OfType<Node>().ToList();
            if (concrete.Count == 0) return null;

            Vector2 min = concrete.Aggregate(new Vector2(float.MaxValue, float.MaxValue),
                (acc, n) => new Vector2(Mathf.Min(acc.x, n.Position.x), Mathf.Min(acc.y, n.Position.y)));

            // Create clone mapping (old -> new), preserving layout + offset
            Dictionary<Node, Node> map = new();
            foreach (Node n in concrete)
            {
                Vector2 rel = n.Position - min;
                Node created = graph.CreateNodeClone(n, min + rel + offset);
                map[n] = created;
            }

            // Recreate internal edges using abstract edges (only those fully inside the selection)
            foreach (IEditorNodeEdge<IEditorNode> edge in graph.Edges)
            {
                if (edge == null) continue;

                Node from = edge.From as Node;
                Node to = edge.To as Node;
                if (from == null || to == null) continue;

                if (!map.TryGetValue(from, out Node newFrom)) continue;
                if (!map.TryGetValue(to, out Node newTo)) continue;

                graph.AddEdgeBetween(newFrom, newTo, NodeConnectionDirection.Unidirectional);
            }

            AssetDatabase.SaveAssets();
            return map.Values.ToList();
        }
    }
}
#endif
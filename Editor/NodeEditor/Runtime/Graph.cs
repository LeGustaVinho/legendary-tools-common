using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using LegendaryTools.GraphV2;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Graph asset that stores nodes/edges as sub-assets for Unity serialization,
    /// but exposes only abstractions to callers. Enforces graph constraints when requested.
    /// </summary>
    public class Graph : ScriptableObject,
        IGraph,
        IReadOnlyGraph<IEditorNode, IEditorNodeEdge<IEditorNode>>
    {
        [SerializeField] private string id;

        // Unity cannot serialize interface fields, so we keep concrete storage,
        // exposing interface views via properties.
        [SerializeField] private List<Node> nodes = new();
        [SerializeField] private List<Edge> edges = new();

        [SerializeField] private Graph parentGraph;
        [SerializeField] private List<Graph> childGraphs = new();

        // -------------------- IGraph --------------------

        /// <summary>Unique graph identifier (GUID string).</summary>
        public string Id
        {
            get => string.IsNullOrEmpty(id) ? id = Guid.NewGuid().ToString("N") : id;
            set => id = value;
        }

        /// <summary>True if the graph is directed and acyclic considering connection directions.</summary>
        public bool IsDirectedAcyclic => IsDirected && !HasDirectedCycleConsideringDirections();

        /// <summary>True if the graph is directed and contains a cycle considering connection directions.</summary>
        public bool IsDirectedCyclic => IsDirected && HasDirectedCycleConsideringDirections();

        /// <summary>True if the underlying undirected view is acyclic (i.e., a forest).</summary>
        public bool IsAcyclic => !HasUndirectedCycle();

        /// <summary>True if the underlying undirected view contains a cycle.</summary>
        public bool IsCyclic => HasUndirectedCycle();

        /// <summary>True if every connection is unidirectional.</summary>
        public bool IsDirected => edges.All(e => e != null && e.Direction == NodeConnectionDirection.Unidirectional);

        public IGraph ParentGraph => parentGraph;
        public IGraph[] ChildGraphs => childGraphs.Cast<IGraph>().ToArray();

        public IGraph[] GraphHierarchy
        {
            get
            {
                Stack<IGraph> stack = new();
                IGraph cur = ParentGraph;
                while (cur != null)
                {
                    stack.Push(cur);
                    cur = cur.ParentGraph;
                }

                return stack.ToArray();
            }
        }

        public INode[] AllNodes => nodes.Cast<INode>().ToArray();

        public INode[] AllNodesRecursive
        {
            get
            {
                List<INode> list = new(nodes);
                foreach (Graph child in childGraphs)
                {
                    if (child == null) continue;
                    list.AddRange(child.AllNodesRecursive);
                }

                return list.ToArray();
            }
        }

        public void Add(INode newNode)
        {
            if (newNode == null) return;
            if (newNode is not Node dn) throw new InvalidOperationException("Unsupported node implementation.");
            if (nodes.Contains(dn)) return;

            EnsureNodeIdentity(dn);
            AddNodeSubAsset(dn);
            dn.SetOwner(this);
        }

        public bool Remove(INode node)
        {
            if (node is not Node dn) return false;

            // Remove incident edges first
            edges.RemoveAll(e => e == null || e.FromNode == dn || e.ToNode == dn);

#if UNITY_EDITOR
            if (dn != null) AssetDatabase.RemoveObjectFromAsset(dn);
#endif
            bool removed = nodes.Remove(dn);
            if (removed) dn.SetOwner(null);
            return removed;
        }

        public void AddGraph(IGraph child)
        {
            if (child is not Graph cg) throw new InvalidOperationException("Unsupported graph implementation.");
            if (cg == this) throw new InvalidOperationException("Cannot add graph to itself.");
            if (!childGraphs.Contains(cg))
            {
                childGraphs.Add(cg);
                cg.parentGraph = this;
            }
        }

        public void RemoveGraph(IGraph child)
        {
            if (child is not Graph cg) return;
            if (childGraphs.Remove(cg)) cg.parentGraph = null;
        }

        public bool Contains(INode node)
        {
            return node is Node dn && nodes.Contains(dn);
        }

        public INode[] Neighbours(INode node)
        {
            if (node is not Node dn) return Array.Empty<INode>();
            HashSet<INode> set = new();

            foreach (Edge e in edges)
            {
                if (e == null) continue;
                if (e.FromNode == dn) set.Add(e.ToNode);
                if (e.Direction == NodeConnectionDirection.Bidirectional && e.ToNode == dn) set.Add(e.FromNode);
            }

            return set.ToArray();
        }

        // -------------------- IReadOnly --------------------

        /// <summary>Read-only nodes view as abstractions.</summary>
        public IReadOnlyList<IEditorNode> Nodes => nodes;

        /// <summary>Read-only edges view as abstractions (covariant).</summary>
        public IReadOnlyList<IEditorNodeEdge<IEditorNode>> Edges => edges;

        // -------------------- Creation / Editing API --------------------

        /// <summary>Creates a new node with a title and position.</summary>
        public virtual Node CreateNode(string title, Vector2 position)
        {
            Node n = CreateInstance<Node>();
            InitNewNode(n, title, position);
            return n;
        }

        /// <summary>Creates a deep clone of an existing node and adds it to this graph.</summary>
        public virtual Node CreateNodeClone(Node template, Vector2 position)
        {
            if (template == null) return null;

            Type t = template.GetType();
            Node clone = CreateInstance(t) as Node;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(template), clone);
            InitNewNode(clone, template.Title, position);
            return clone;
        }

        /// <summary>Moves a node to a new position by Id.</summary>
        public virtual void MoveNode(string nodeId, Vector2 newPosition)
        {
            Node n = nodes.Find(x => x != null && x.Id == nodeId);
            if (n != null) n.Position = newPosition;
        }

        /// <summary>
        /// Legacy helper: add a unidirectional edge (keeps older call sites working).
        /// </summary>
        public virtual bool TryAddEdge(string fromId, string toId, out string error)
        {
            return TryAddEdge(fromId, toId, NodeConnectionDirection.Unidirectional, out error);
        }

        /// <summary>
        /// Attempts to add a connection between two nodes by Ids.
        /// </summary>
        public virtual bool TryAddEdge(string fromId, string toId, NodeConnectionDirection direction, out string error)
        {
            Node from = nodes.Find(x => x != null && x.Id == fromId);
            Node to = nodes.Find(x => x != null && x.Id == toId);
            return TryAddEdge(from, to, direction, out _, out error);
        }

        /// <summary>
        /// Attempts to add a connection between two INode endpoints.
        /// Returns the created INodeConnection on success.
        /// </summary>
        public virtual bool TryAddEdge(INode from, INode to, NodeConnectionDirection direction,
            out INodeConnection connection, out string error)
        {
            connection = null;
            error = null;

            if (from == null || to == null)
            {
                error = "Endpoints not found.";
                return false;
            }

            if (ReferenceEquals(from, to))
            {
                error = "Cannot connect a node to itself.";
                return false;
            }

            if (from is not Node f || to is not Node t)
            {
                error = "Unsupported node implementation.";
                return false;
            }

            // Prevent duplicates in the same direction
            if (edges.Exists(e => e != null && e.FromNode == f && e.ToNode == t))
            {
                error = "Edge already exists.";
                return false;
            }

            // Dag rule: if we are adding a directed connection, ensure it does not create a cycle.
            if (direction == NodeConnectionDirection.Unidirectional && WouldIntroduceCycle(f, t))
            {
                error = "Adding this edge would create a cycle.";
                return false;
            }

            // For bidirectional, disallow if either direction would create a cycle (DAG cannot be bidirectional).
            if (direction == NodeConnectionDirection.Bidirectional &&
                (WouldIntroduceCycle(f, t) || WouldIntroduceCycle(t, f)))
            {
                error = "Bidirectional edges would create a cycle in a DAG.";
                return false;
            }

            Edge e = CreateEdgeInternal(f, t, direction);
            connection = e;
            return true;
        }

        /// <summary>Removes an edge by endpoints (Ids).</summary>
        public virtual void RemoveEdge(string fromId, string toId)
        {
            Node from = nodes.Find(x => x != null && x.Id == fromId);
            Node to = nodes.Find(x => x != null && x.Id == toId);
            edges.RemoveAll(e => e == null || (e.FromNode == from && e.ToNode == to));
        }

        /// <summary>Removes a node by Id and its incident edges.</summary>
        public virtual void RemoveNode(string nodeId)
        {
            Node n = nodes.Find(x => x != null && x.Id == nodeId);
            if (n == null) return;

            edges.RemoveAll(e => e == null || e.FromNode == n || e.ToNode == n);

#if UNITY_EDITOR
            AssetDatabase.RemoveObjectFromAsset(n);
#endif
            nodes.Remove(n);
            n.SetOwner(null);
        }

        /// <summary>
        /// Utility for paste/duplicate flows to rebuild edges using the safe API.
        /// </summary>
        public virtual void AddEdgeBetween(INode from, INode to, NodeConnectionDirection direction)
        {
            TryAddEdge(from, to, direction, out _, out _);
        }

        // -------------------- Internals --------------------

        private void EnsureNodeIdentity(Node n)
        {
            if (string.IsNullOrEmpty(n.Id))
                n.Id = Guid.NewGuid().ToString("N");
        }

        private void InitNewNode(Node n, string title, Vector2 position)
        {
            n.Id = Guid.NewGuid().ToString("N");
            n.Title = title;
            n.Position = position;
            n.SetOwner(this);

#if UNITY_EDITOR
            AssetDatabase.AddObjectToAsset(n, this);
#endif
            nodes.Add(n);
        }

        private Edge CreateEdgeInternal(Node from, Node to, NodeConnectionDirection direction)
        {
            Edge e = CreateInstance<Edge>();
            e.name = $"Edge_{from.Id}_{to.Id}";
            e.SetEndpoints(from, to);
            e.Direction = direction;

#if UNITY_EDITOR
            AssetDatabase.AddObjectToAsset(e, this);
#endif
            edges.Add(e);
            return e;
        }

        /// <summary>
        /// Safely extracts concrete endpoints from an edge. Returns false if any endpoint is not a Node.
        /// </summary>
        private static bool TryGetEndpoints(Edge e, out Node f, out Node t)
        {
            f = e?.FromNode as Node;
            t = e?.ToNode as Node;
            return f != null && t != null;
        }

        private bool WouldIntroduceCycle(Node from, Node to)
        {
            if (from == null || to == null) return false;

            // Build adjacency using directed semantics (keys/values are Node).
            Dictionary<Node, List<Node>> adj = new(nodes.Count);
            foreach (Node n in nodes)
            {
                if (n != null) adj[n] = new List<Node>();
            }

            foreach (Edge e in edges)
            {
                if (e == null) continue;
                if (!TryGetEndpoints(e, out Node f, out Node t)) continue;

                // Existing directed edge(s)
                adj[f].Add(t);
                if (e.Direction == NodeConnectionDirection.Bidirectional)
                    adj[t].Add(f);
            }

            // Test reachability from "to" to "from"
            Stack<Node> stack = new();
            HashSet<Node> visited = new();
            stack.Push(to);

            while (stack.Count > 0)
            {
                Node cur = stack.Pop();
                if (cur == from) return true;

                if (!visited.Add(cur)) continue;
                if (adj.TryGetValue(cur, out List<Node> next))
                    for (int i = 0; i < next.Count; i++)
                    {
                        Node n = next[i];
                        if (n != null && !visited.Contains(n)) stack.Push(n);
                    }
            }

            return false;
        }

        private bool HasDirectedCycleConsideringDirections()
        {
            // Kahn's algorithm on directed edges (expanding bidirectional as two one-way edges).
            Dictionary<Node, int> indegree = new();
            Dictionary<Node, List<Node>> outs = new();
            foreach (Node n in nodes)
            {
                if (n == null) continue;
                indegree[n] = 0;
                outs[n] = new List<Node>();
            }

            foreach (Edge e in edges)
            {
                if (e == null) continue;
                if (!TryGetEndpoints(e, out Node f, out Node t)) continue;

                outs[f].Add(t);
                indegree[t]++;

                if (e.Direction == NodeConnectionDirection.Bidirectional)
                {
                    outs[t].Add(f);
                    indegree[f]++;
                }
            }

            Queue<Node> q = new(indegree.Where(p => p.Value == 0).Select(p => p.Key));
            int visitedCount = 0;

            while (q.Count > 0)
            {
                Node n = q.Dequeue();
                visitedCount++;
                foreach (Node m in outs[n])
                {
                    indegree[m]--;
                    if (indegree[m] == 0) q.Enqueue(m);
                }
            }

            // If edges exist and we didn't process all nodes reachable by edges, a cycle exists.
            bool hasAnyEdge = edges.Any(e => e != null && e.FromNode as Node != null && e.ToNode as Node != null);
            return hasAnyEdge && visitedCount < indegree.Count;
        }

        private bool HasUndirectedCycle()
        {
            // Union-Find on undirected view (keys/values are Node).
            Dictionary<Node, Node> parent = new();
            foreach (Node n in nodes)
            {
                if (n != null) parent[n] = n;
            }

            Node Find(Node x)
            {
                if (!ReferenceEquals(parent[x], x)) parent[x] = Find(parent[x]);
                return parent[x];
            }

            void Union(Node a, Node b)
            {
                Node ra = Find(a);
                Node rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            foreach (Edge e in edges)
            {
                if (e == null) continue;
                if (!TryGetEndpoints(e, out Node f, out Node t)) continue;

                Node a = Find(f);
                Node b = Find(t);
                if (a == b) return true; // found a cycle
                Union(a, b);
            }

            return false;
        }

        private void AddNodeSubAsset(Node n)
        {
#if UNITY_EDITOR
            AssetDatabase.AddObjectToAsset(n, this);
#endif
            if (!nodes.Contains(n)) nodes.Add(n);
        }
    }
}
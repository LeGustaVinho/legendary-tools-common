using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.GraphV2
{
    public class Graph : IGraph
    {
        public string Id { get; set; }
        private readonly List<INode> nodes;
        private readonly List<IGraph> childGraphs;
        private IGraph parentGraph;

        public Graph()
        {
            Id = Guid.NewGuid().ToString();
            nodes = new List<INode>();
            childGraphs = new List<IGraph>();
        }

        /// <summary>
        /// Check if all connections are Bidirectional and dont have cycle
        /// </summary>
        public bool IsDirectedAcyclic => IsDirected && !IsCyclic;
        public bool IsDirectedCyclic => IsDirected && IsCyclic;
        public bool IsAcyclic => !IsCyclic;
        
        public bool IsCyclic => IsDirected ? HasCycleDirected() : HasCycleUndirected();

        public virtual bool IsDirected
        {
            get
            {
                if (nodes.Count == 1 && nodes[0].Connections.Count == 0)
                {
                    return true;
                }
                
                foreach (INode node in nodes)
                {
                    foreach (INodeConnection conn in node.Connections)
                    {
                        if (conn.Direction == NodeConnectionDirection.Unidirectional)
                            return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Gets or sets the parent graph. Setting is internal to prevent external modification.
        /// </summary>
        public IGraph ParentGraph
        {
            get => parentGraph;
            private set => parentGraph = value;
        }

        /// <summary>
        /// Gets the child graphs as an array.
        /// </summary>
        public IGraph[] ChildGraphs => childGraphs.ToArray();

        public IGraph[] GraphHierarchy
        {
            get
            {
                List<IGraph> hierarchy = new List<IGraph>();
                IGraph current = ParentGraph;
                while (current != null)
                {
                    hierarchy.Insert(0, current);
                    current = current.ParentGraph;
                }

                return hierarchy.ToArray();
            }
        }

        public INode[] AllNodes => nodes.ToArray();

        /// <summary>
        /// Gets all nodes in this graph and recursively in all child graphs.
        /// </summary>
        public INode[] AllNodesRecursive
        {
            get
            {
                HashSet<string> seenNodes = new HashSet<string>();
                List<INode> allNodes = new List<INode>();
                CollectAllNodesRecursive(this, allNodes, seenNodes);
                return allNodes.ToArray();
            }
        }
        
        /// <summary>
        /// Recursively collects all nodes from the graph and its children.
        /// </summary>
        /// <param name="graph">The graph to collect nodes from.</param>
        /// <param name="allNodes">The list to accumulate nodes.</param>
        /// <param name="seenNodes">A set to track already seen nodes to prevent duplicates.</param>
        private void CollectAllNodesRecursive(IGraph graph, List<INode> allNodes, HashSet<string> seenNodes)
        {
            foreach (var node in graph.AllNodes)
            {
                if (seenNodes.Add(node.Id))
                {
                    allNodes.Add(node);
                }
            }

            foreach (var child in graph.ChildGraphs)
            {
                CollectAllNodesRecursive(child, allNodes, seenNodes);
            }
        }
        
        public void Add(INode newNode)
        {
            if (newNode == null) throw new ArgumentNullException(nameof(newNode));
            if (nodes.Find(item => item.Id == newNode.Id) != null) 
                throw new InvalidOperationException($"Already contains id {newNode.Id}");
            if (newNode.Owner != null)
                throw new InvalidOperationException($"Node  {newNode.Id} already in a graph {newNode.Owner.Id}");

            if (!nodes.Contains(newNode))
            {
                nodes.Add(newNode);
                newNode.SetOwner(this);
            }
        }

        public virtual bool Remove(INode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (nodes.Remove(node))
            {
                // Remove all connections related to this node
                List<INodeConnection> connectionsCopy = new List<INodeConnection>(node.Connections);
                foreach (INodeConnection conn in connectionsCopy)
                {
                    conn.Disconnect();
                }
                return true;
            }
            node.SetOwner(null);
            return false;
        }

        /// <summary>
        /// Adds a child graph to this graph.
        /// </summary>
        /// <param name="child">The child graph to add.</param>
        public void AddGraph(IGraph child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (child == this) throw new InvalidOperationException("A graph cannot be a child of itself.");
            if (child.ParentGraph != null)
                throw new InvalidOperationException("The child graph already has a parent.");

            // Prevent circular hierarchy
            if (IsDescendantOf(child))
                throw new InvalidOperationException("Adding this child would create a circular hierarchy.");

            childGraphs.Add(child);
            if (child is Graph childGraph)
            {
                childGraph.ParentGraph = this;
            }
            else
            {
                throw new ArgumentException("Child graph must be of type Graph.", nameof(child));
            }
        }

        /// <summary>
        /// Removes a child graph from this graph.
        /// </summary>
        /// <param name="child">The child graph to remove.</param>
        public void RemoveGraph(IGraph child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (childGraphs.Remove(child))
            {
                if (child is Graph childGraph)
                {
                    childGraph.ParentGraph = null;
                }
            }
            else
            {
                throw new ArgumentException("The specified graph is not a child of this graph.", nameof(child));
            }
        }
        
        /// <summary>
        /// Checks if the current graph is a descendant of the potentialAncestor graph.
        /// Used to prevent circular hierarchies.
        /// </summary>
        /// <param name="potentialAncestor">The graph to check against.</param>
        /// <returns>True if the current graph is a descendant; otherwise, false.</returns>
        private bool IsDescendantOf(IGraph potentialAncestor)
        {
            IGraph current = this.ParentGraph;
            while (current != null)
            {
                if (current == potentialAncestor)
                    return true;
                current = current.ParentGraph;
            }
            return false;
        }

        public bool Contains(INode node)
        {
            return nodes.Contains(node);
        }

        public INode[] Neighbours(INode node)
        {
            if (!nodes.Contains(node)) throw new ArgumentException("Node does not exist in the graph.");
            return node.Neighbours;
        }
        
        private bool HasCycleDirected()
        {
            HashSet<INode> visited = new HashSet<INode>();
            HashSet<INode> recStack = new HashSet<INode>();

            foreach (var node in nodes)
            {
                if (DFSDirected(node, visited, recStack))
                    return true;
            }

            return false;
        }

        private bool DFSDirected(INode node, HashSet<INode> visited, HashSet<INode> recStack)
        {
            if (!visited.Contains(node))
            {
                visited.Add(node);
                recStack.Add(node);

                foreach (var conn in node.OutboundConnections)
                {
                    var neighbor = conn.ToNode;
                    if (!visited.Contains(neighbor) && DFSDirected(neighbor, visited, recStack))
                        return true;
                    else if (recStack.Contains(neighbor))
                        return true;
                }
            }

            recStack.Remove(node);
            return false;
        }

        private bool HasCycleUndirected()
        {
            HashSet<INode> visited = new HashSet<INode>();

            foreach (var node in nodes)
            {
                if (!visited.Contains(node))
                {
                    if (DFSUndirected(node, visited, null))
                        return true;
                }
            }

            return false;
        }

        private bool DFSUndirected(INode node, HashSet<INode> visited, INode parent)
        {
            visited.Add(node);

            foreach (var conn in node.OutboundConnections.Concat(node.InboundConnections))
            {
                var neighbor = conn.Direction == NodeConnectionDirection.Unidirectional ? conn.ToNode : 
                    (conn.FromNode == node ? conn.ToNode : conn.FromNode);
                if (neighbor == null) continue;

                if (!visited.Contains(neighbor))
                {
                    if (DFSUndirected(neighbor, visited, node))
                        return true;
                }
                else if (!neighbor.Equals(parent))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
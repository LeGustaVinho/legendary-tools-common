using System;
using System.Linq;
using NUnit.Framework;

namespace LegendaryTools.GraphV2.Tests
{
    [TestFixture]
    public class GraphTests
    {
        private Graph graph;
        private Node nodeA;
        private Node nodeB;
        private Node nodeC;
        private Node nodeD;
        private Node nodeE;
        private Node nodeF;


        [SetUp]
        public void Setup()
        {
            graph = new Graph();
            nodeA = new Node(true);
            nodeB = new Node(true);
            nodeC = new Node(true);
            nodeD = new Node(true);
            nodeE = new Node(true);
            nodeF = new Node(true);
        }

        [Test]
        public void AddNode_ShouldIncreaseNodeCount()
        {
            graph.Add(nodeA);
            Assert.AreEqual(1, graph.AllNodes.Length, "Adding a node should increase the node count to 1.");
        }

        [Test]
        public void AddNullNode_ShouldThrowArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => graph.Add(null), "Adding a null node should throw ArgumentNullException.");
            Assert.That(ex.ParamName, Is.EqualTo("newNode"), "Exception parameter name should be 'newNode'.");
        }

        [Test]
        public void RemoveNode_ShouldDecreaseNodeCount()
        {
            graph.Add(nodeA);
            bool removed = graph.Remove(nodeA);
            Assert.IsTrue(removed, "Removing an existing node should return true.");
            Assert.AreEqual(0, graph.AllNodes.Length, "Node count should be 0 after removal.");
        }

        [Test]
        public void RemoveNonExistentNode_ShouldReturnFalse()
        {
            bool removed = graph.Remove(nodeA);
            Assert.IsFalse(removed, "Removing a non-existent node should return false.");
        }

        [Test]
        public void ContainsNode_ShouldReturnTrueForExistingNode()
        {
            graph.Add(nodeA);
            Assert.IsTrue(graph.Contains(nodeA), "Graph should contain the node that was added.");
        }

        [Test]
        public void ContainsNode_ShouldReturnFalseForNonExistentNode()
        {
            Assert.IsFalse(graph.Contains(nodeA), "Graph should not contain a node that was not added.");
        }

        [Test]
        public void AddGraph_ShouldIncreaseChildGraphCount()
        {
            Graph childGraph = new Graph();
            graph.AddGraph(childGraph);
            Assert.AreEqual(1, graph.ChildGraphs.Length, "Adding a child graph should increase child graph count to 1.");
        }

        [Test]
        public void AddGraph_Null_ShouldThrowArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => graph.AddGraph(null), "Adding a null graph should throw ArgumentNullException.");
            Assert.That(ex.ParamName, Is.EqualTo("child"), "Exception parameter name should be 'child'.");
        }

        [Test]
        public void AddGraph_Self_ShouldThrowInvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => graph.AddGraph(graph), "Adding a graph as its own child should throw InvalidOperationException.");
            Assert.That(ex.Message, Is.EqualTo("A graph cannot be a child of itself."), "Exception message should indicate that a graph cannot be its own child.");
        }

        [Test]
        public void RemoveGraph_ShouldDecreaseChildGraphCount()
        {
            Graph childGraph = new Graph();
            graph.AddGraph(childGraph);
            graph.RemoveGraph(childGraph);
            Assert.AreEqual(0, graph.ChildGraphs.Length, "Removing a child graph should decrease child graph count to 0.");
        }

        [Test]
        public void RemoveGraph_NonExistent_ShouldThrowArgumentException()
        {
            Graph childGraph = new Graph();
            var ex = Assert.Throws<ArgumentException>(() => graph.RemoveGraph(childGraph), "Removing a non-existent child graph should throw ArgumentException.");
            Assert.That(ex.Message, Does.Contain("The specified graph is not a child of this graph."), "Exception message should indicate that the graph is not a child.");
        }

        [Test]
        public void GraphHierarchy_ShouldReturnCorrectHierarchy()
        {
            Graph parentGraph = new Graph();
            Graph childGraph = new Graph();
            Graph grandChildGraph = new Graph();

            parentGraph.AddGraph(childGraph);
            childGraph.AddGraph(grandChildGraph);

            Assert.AreEqual(2, grandChildGraph.GraphHierarchy.Length, "Graph hierarchy should include two ancestors.");
            Assert.AreEqual(parentGraph, grandChildGraph.GraphHierarchy[0], "The first ancestor should be the parent graph.");
            Assert.AreEqual(childGraph, grandChildGraph.GraphHierarchy[1], "The second ancestor should be the child graph.");
        }

        [Test]
        public void IsDirected_ShouldReturnFalseForUndirectedGraph()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            Assert.IsFalse(graph.IsDirected, "Graph should be undirected when all connections are bidirectional.");
        }

        [Test]
        public void IsDirected_ShouldReturnTrueForDirectedGraph()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            Assert.IsTrue(graph.IsDirected, "Graph should be directed when there is at least one unidirectional connection.");
        }

        [Test]
        public void IsAcyclic_ShouldReturnTrueForAcyclicGraph()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            Assert.IsTrue(graph.IsAcyclic, "Graph should be acyclic when there are no cycles.");
        }

        [Test]
        public void IsDirectedAcyclic_ShouldReturnTrueForDirectedAcyclicGraph()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeB.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);
            Assert.IsTrue(graph.IsDirectedAcyclic, "Graph should be directed acyclic when it is directed and has no cycles.");
        }

        [Test]
        public void IsDirectedCyclic_ShouldReturnTrueForDirectedCyclicGraph()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeB.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);
            Assert.IsFalse(graph.IsDirectedCyclic, "Graph should not be directed cyclic when it is directed and has cycles.");
        }

        [Test]
        public void AllNodesRecursive_ShouldReturnAllNodesIncludingChildren()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            Graph childGraph = new Graph();
            childGraph.Add(nodeC);
            graph.AddGraph(childGraph);

            Graph grandChildGraph = new Graph();
            grandChildGraph.Add(nodeD);
            childGraph.AddGraph(grandChildGraph);

            var allNodesRecursive = graph.AllNodesRecursive;
            Assert.AreEqual(4, allNodesRecursive.Length, "AllNodesRecursive should return all nodes including those in child graphs.");
            CollectionAssert.Contains(allNodesRecursive, nodeA, "AllNodesRecursive should contain nodeA.");
            CollectionAssert.Contains(allNodesRecursive, nodeB, "AllNodesRecursive should contain nodeB.");
            CollectionAssert.Contains(allNodesRecursive, nodeC, "AllNodesRecursive should contain nodeC.");
            CollectionAssert.Contains(allNodesRecursive, nodeD, "AllNodesRecursive should contain nodeD.");
        }

        [Test]
        public void AllNodes_ShouldReturnOnlyDirectNodes()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            Graph childGraph = new Graph();
            childGraph.Add(nodeC);
            graph.AddGraph(childGraph);

            var allNodes = graph.AllNodes;
            Assert.AreEqual(2, allNodes.Length, "AllNodes should return only the nodes directly added to the graph.");
            CollectionAssert.Contains(allNodes, nodeA, "AllNodes should contain nodeA.");
            CollectionAssert.Contains(allNodes, nodeB, "AllNodes should contain nodeB.");
            CollectionAssert.DoesNotContain(allNodes, nodeC, "AllNodes should not contain nodeC from the child graph.");
        }

        [Test]
        public void Neighbours_ShouldReturnCorrectNeighbours()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);

            var neighboursOfA = graph.Neighbours(nodeA);
            Assert.AreEqual(2, neighboursOfA.Length, "NodeA should have two neighbours.");
            CollectionAssert.Contains(neighboursOfA, nodeB, "NodeA's neighbours should include nodeB.");
            CollectionAssert.Contains(neighboursOfA, nodeC, "NodeA's neighbours should include nodeC.");

            var neighboursOfB = graph.Neighbours(nodeB);
            Assert.AreEqual(0, neighboursOfB.Length, "NodeB should have no neighbour.");

            var neighboursOfC = graph.Neighbours(nodeC);
            Assert.AreEqual(1, neighboursOfC.Length, "NodeC should have one neighbour.");
            CollectionAssert.Contains(neighboursOfC, nodeA, "NodeC's neighbour should include nodeA.");
        }

        [Test]
        public void Neighbours_NonExistentNode_ShouldThrowArgumentException()
        {
            graph.Add(nodeA);
            var ex = Assert.Throws<ArgumentException>(() => graph.Neighbours(nodeB), "Requesting neighbours of a non-existent node should throw ArgumentException.");
            Assert.That(ex.Message, Does.Contain("Node does not exist in the graph."), "Exception message should indicate that the node does not exist in the graph.");
        }

        [Test]
        public void AddGraph_CircularHierarchy_ShouldThrowInvalidOperationException()
        {
            Graph childGraph = new Graph();
            graph.AddGraph(childGraph);
            var ex = Assert.Throws<InvalidOperationException>(() => childGraph.AddGraph(graph), "Adding a parent graph as a child should throw InvalidOperationException due to circular hierarchy.");
            Assert.That(ex.Message, Is.EqualTo("Adding this child would create a circular hierarchy."), "Exception message should indicate that a circular hierarchy is being created.");
        }

        [Test]
        public void GraphHierarchy_NoParent_ShouldBeEmpty()
        {
            Assert.IsEmpty(graph.GraphHierarchy, "GraphHierarchy should be empty for a graph with no parent.");
        }

        [Test]
        public void IsAcyclic_DirectedGraphWithCycle_ShouldReturnFalse()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeB.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);
            Assert.IsTrue(graph.IsCyclic, "Undirected graph with a cycle should be cyclic.");
        }

        [Test]
        public void IsAcyclic_UndirectedGraphWithCycle_ShouldReturnFalse()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeB.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);
            nodeC.ConnectTo(nodeA, NodeConnectionDirection.Bidirectional);
            Assert.IsFalse(graph.IsAcyclic, "Undirected graph with a cycle should not be acyclic.");
        }
        
        [Test]
        public void AddMultipleChildGraphs_ShouldIncreaseChildGraphCountAppropriately()
        {
            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();
            Graph childGraph3 = new Graph();

            graph.AddGraph(childGraph1);
            graph.AddGraph(childGraph2);
            graph.AddGraph(childGraph3);

            Assert.AreEqual(3, graph.ChildGraphs.Length, "Adding multiple child graphs should increase child graph count accordingly.");
            CollectionAssert.Contains(graph.ChildGraphs, childGraph1, "ChildGraphs should contain childGraph1.");
            CollectionAssert.Contains(graph.ChildGraphs, childGraph2, "ChildGraphs should contain childGraph2.");
            CollectionAssert.Contains(graph.ChildGraphs, childGraph3, "ChildGraphs should contain childGraph3.");
        }

        [Test]
        public void ParentGraph_ShouldBeSetCorrectlyWhenAddingChildGraph()
        {
            Graph parentGraph = new Graph();
            Graph childGraph = new Graph();

            parentGraph.AddGraph(childGraph);

            Assert.AreEqual(parentGraph, childGraph.ParentGraph, "Child graph's ParentGraph should be set to the parent graph when added.");
        }

        [Test]
        public void ConnectNodes_WithBidirectional_ShouldCreateOneOutboundConnections()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            var connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            Assert.AreEqual(1, nodeA.OutboundConnections.Length, "Bidirectional connection should create one outbound connections for nodeA.");
            Assert.AreEqual(1, nodeB.OutboundConnections.Length, "Bidirectional connection should create one outbound connections for nodeB.");
            Assert.AreEqual(NodeConnectionDirection.Bidirectional, connection.Direction, "Connection direction should be set to Bidirectional.");
        }

        [Test]
        public void RemoveConnection_ShouldAffectNeighboursCorrectly()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            var connectionAB = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            var connectionAC = nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);

            bool removed = nodeA.RemoveConnection(connectionAB);

            Assert.IsTrue(removed, "Removing an existing connection should return true.");
            Assert.AreEqual(1, nodeA.Neighbours.Length, "After removal, nodeA should have one neighbour.");
            CollectionAssert.DoesNotContain(nodeA.Neighbours, nodeB, "After removal, nodeA should not have nodeB as a neighbour.");
            CollectionAssert.Contains(nodeA.Neighbours, nodeC, "After removal, nodeA should still have nodeC as a neighbour.");
        }

        [Test]
        public void DisconnectConnection_ShouldRemoveConnectionFromBothNodes()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            var connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            connection.Disconnect();

            Assert.IsFalse(nodeA.Connections.Contains(connection), "After disconnecting, nodeA should no longer contain the connection.");
            Assert.IsFalse(nodeB.Connections.Contains(connection), "After disconnecting, nodeB should no longer contain the connection.");
            Assert.IsEmpty(nodeA.Neighbours, "After disconnecting, nodeA should have no neighbours.");
            Assert.IsEmpty(nodeB.Neighbours, "After disconnecting, nodeB should have no neighbours.");
        }

        [Test]
        public void AddGraph_WithExistingParent_ShouldThrowInvalidOperationException()
        {
            Graph parentGraph1 = new Graph();
            Graph parentGraph2 = new Graph();
            Graph childGraph = new Graph();

            parentGraph1.AddGraph(childGraph);

            var ex = Assert.Throws<InvalidOperationException>(() => parentGraph2.AddGraph(childGraph), "Adding a graph that already has a parent should throw InvalidOperationException.");
            Assert.That(ex.Message, Is.EqualTo("The child graph already has a parent."), "Exception message should indicate that the child graph already has a parent.");
        }

        [Test]
        public void AddGraph_WithCircularHierarchy_ShouldThrowInvalidOperationException()
        {
            Graph childGraph = new Graph();
            graph.AddGraph(childGraph);

            var ex = Assert.Throws<InvalidOperationException>(() => childGraph.AddGraph(graph), "Adding a graph as a child to one of its descendants should throw InvalidOperationException.");
            Assert.That(ex.Message, Is.EqualTo("Adding this child would create a circular hierarchy."), "Exception message should indicate that a circular hierarchy is being created.");
        }

        [Test]
        public void AddingSameNodeMultipleTimes_ShouldOnlyAddOnce()
        {
            graph.Add(nodeA);

            Assert.Throws<InvalidOperationException>(() => graph.Add(nodeA), "Adding the same node multiple times should only add it once and thow error");
        }

        [Test]
        public void ConnectTo_Self_ShouldThrowInvalidOperationException()
        {
            graph.Add(nodeA);

            var ex = Assert.Throws<InvalidOperationException>(() => nodeA.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional), "Connecting a node to itself should throw InvalidOperationException.");
            Assert.That(ex.Message, Is.EqualTo("Cannot connect node to itself."), "Exception message should indicate that a node cannot be connected to itself.");
        }

        [Test]
        public void ConnectTo_WithInvalidDirection_ShouldThrowArgumentException()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            var invalidDirection = (NodeConnectionDirection)999;

            var ex = Assert.Throws<ArgumentException>(() => nodeA.ConnectTo(nodeB, invalidDirection), "Using an invalid NodeConnectionDirection should throw ArgumentException.");
            Assert.That(ex.Message, Does.Contain("999 is not a valid enum type for NodeConnectionDirection"), "Exception message should indicate invalid NodeConnectionDirection.");
        }

        [Test]
        public void RemoveGraph_ShouldUnsetParentGraph()
        {
            Graph childGraph = new Graph();
            graph.AddGraph(childGraph);
            graph.RemoveGraph(childGraph);

            Assert.IsNull(childGraph.ParentGraph, "After removing, child graph's ParentGraph should be null.");
            Assert.AreEqual(0, graph.ChildGraphs.Length, "Child graph count should be 0 after removal.");
        }

        [Test]
        public void RemoveGraph_ShouldThrowWhenChildGraphIsNull()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => graph.RemoveGraph(null), "Removing a null child graph should throw ArgumentNullException.");
            Assert.That(ex.ParamName, Is.EqualTo("child"), "Exception parameter name should be 'child'.");
        }

        [Test]
        public void AllNodesRecursive_ShouldHandleDeepHierarchies()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();
            Graph childGraph3 = new Graph();

            childGraph1.Add(nodeC);
            childGraph2.Add(nodeD);
            childGraph3.Add(nodeE);

            graph.AddGraph(childGraph1);
            childGraph1.AddGraph(childGraph2);
            childGraph2.AddGraph(childGraph3);

            var allNodesRecursive = graph.AllNodesRecursive;

            Assert.AreEqual(5, allNodesRecursive.Length, "AllNodesRecursive should include all nodes from deep hierarchies.");
            CollectionAssert.Contains(allNodesRecursive, nodeA, "AllNodesRecursive should contain nodeA.");
            CollectionAssert.Contains(allNodesRecursive, nodeB, "AllNodesRecursive should contain nodeB.");
            CollectionAssert.Contains(allNodesRecursive, nodeC, "AllNodesRecursive should contain nodeC.");
            CollectionAssert.Contains(allNodesRecursive, nodeD, "AllNodesRecursive should contain nodeD.");
            CollectionAssert.Contains(allNodesRecursive, nodeE, "AllNodesRecursive should contain nodeE.");
        }

        [Test]
        public void ParentGraph_ShouldNotAffectOtherGraphs()
        {
            Graph parentGraph1 = new Graph();
            Graph parentGraph2 = new Graph();
            Graph childGraph = new Graph();

            parentGraph1.AddGraph(childGraph);
            parentGraph2.AddGraph(new Graph());

            Assert.AreEqual(parentGraph1, childGraph.ParentGraph, "Child graph should have only one parent graph.");
            Assert.AreNotEqual(parentGraph2, childGraph.ParentGraph, "Child graph's parent should not be affected by other parent graphs.");
        }

        [Test]
        public void RemoveGraph_ShouldNotAffectOtherChildGraphs()
        {
            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();

            graph.AddGraph(childGraph1);
            graph.AddGraph(childGraph2);

            graph.RemoveGraph(childGraph1);

            Assert.AreEqual(1, graph.ChildGraphs.Length, "Removing one child graph should not affect other child graphs.");
            CollectionAssert.DoesNotContain(graph.ChildGraphs, childGraph1, "ChildGraphs should not contain the removed childGraph1.");
            CollectionAssert.Contains(graph.ChildGraphs, childGraph2, "ChildGraphs should still contain childGraph2.");
        }

        [Test]
        public void GraphWithNoNodes_ShouldBeAcyclic()
        {
            Assert.IsTrue(graph.IsAcyclic, "An empty graph should be considered acyclic.");
            Assert.IsFalse(graph.IsCyclic, "An empty graph should not be cyclic.");
        }

        [Test]
        public void GraphWithSingleNode_ShouldBeAcyclic()
        {
            graph.Add(nodeA);
            Assert.IsTrue(graph.IsAcyclic, "A graph with a single node should be acyclic.");
            Assert.IsFalse(graph.IsCyclic, "A graph with a single node should not be cyclic.");
        }

        [Test]
        public void Neighbours_ShouldReflectBidirectionalConnections()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);

            var neighboursOfB = graph.Neighbours(nodeB);
            Assert.AreEqual(1, neighboursOfB.Length, "NodeB should have one neighbour from bidirectional connection.");
            CollectionAssert.Contains(neighboursOfB, nodeA, "NodeB's neighbour should include nodeA.");

            var neighboursOfC = graph.Neighbours(nodeC);
            Assert.AreEqual(0, neighboursOfC.Length, "NodeC should have no neighbours as the connection is unidirectional from nodeA to nodeC.");
        }

        [Test]
        public void AddGraph_WithMultipleLevels_ShouldMaintainCorrectParentReferences()
        {
            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();
            Graph childGraph3 = new Graph();

            graph.AddGraph(childGraph1);
            childGraph1.AddGraph(childGraph2);
            childGraph2.AddGraph(childGraph3);

            Assert.AreEqual(graph, childGraph1.ParentGraph, "childGraph1's parent should be graph.");
            Assert.AreEqual(childGraph1, childGraph2.ParentGraph, "childGraph2's parent should be childGraph1.");
            Assert.AreEqual(childGraph2, childGraph3.ParentGraph, "childGraph3's parent should be childGraph2.");
        }

        [Test]
        public void ConnectNodes_ShouldNotAllowMultipleConnectionsInSameDirection()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            var firstConnection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            var secondConnection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            Assert.AreEqual(1, nodeA.OutboundConnections.Length, "Multiple unidirectional connections in the same direction should not be allowed.");
            Assert.AreSame(firstConnection, secondConnection, "Connecting the same nodes in the same direction should return the existing connection.");
        }

        [Test]
        public void ConnectNodes_ShouldUpgradeToBidirectional_WhenOppositeConnectionExists()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            var connAB = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            var connBA = nodeB.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);

            Assert.AreEqual(NodeConnectionDirection.Bidirectional, connAB.Direction, "Connection should upgrade to Bidirectional when opposite connection is added.");
            Assert.AreSame(connAB, connBA, "Both connections should reference the same connection object.");
        }

        [Test]
        public void RemoveNode_ShouldRemoveAllAssociatedConnections()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);

            bool removed = graph.Remove(nodeA);

            Assert.IsTrue(removed, "Removing an existing node should return true.");
            Assert.IsFalse(graph.Contains(nodeA), "Graph should no longer contain the removed node.");
            Assert.AreEqual(0, nodeB.Neighbours.Length, "NodeB should have no neighbours after nodeA is removed.");
            Assert.AreEqual(0, nodeC.Neighbours.Length, "NodeC should have no neighbours after nodeA is removed.");
        }

        [Test]
        public void RemoveNode_ShouldThrowWhenRemovingNull()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => graph.Remove(null), "Removing a null node should throw ArgumentNullException.");
            Assert.That(ex.ParamName, Is.EqualTo("node"), "Exception parameter name should be 'node'.");
        }

        [Test]
        public void ParentGraph_ShouldNotAffectGraphHierarchyOfOtherGraphs()
        {
            Graph parentGraph1 = new Graph();
            Graph parentGraph2 = new Graph();
            Graph childGraph = new Graph();

            parentGraph1.AddGraph(childGraph);
            parentGraph2.AddGraph(new Graph());

            Assert.AreEqual(parentGraph1, childGraph.ParentGraph, "childGraph's parent should remain as parentGraph1.");
            Assert.IsEmpty(parentGraph2.GraphHierarchy, "parentGraph2 should have its own separate hierarchy.");
        }

        [Test]
        public void Neighbours_ShouldHandleBidirectionalAndUnidirectionalConnections()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);
            graph.Add(nodeD);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);
            nodeD.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);

            var neighboursOfA = graph.Neighbours(nodeA);
            Assert.AreEqual(2, neighboursOfA.Length, "nodeA should have two neighbours from various connections.");
            CollectionAssert.Contains(neighboursOfA, nodeB, "nodeA should have nodeB as a neighbour.");
            CollectionAssert.Contains(neighboursOfA, nodeC, "nodeA should have nodeC as a neighbour.");

            var neighboursOfB = graph.Neighbours(nodeB);
            Assert.AreEqual(1, neighboursOfB.Length, "nodeB should have one neighbour.");
            CollectionAssert.Contains(neighboursOfB, nodeA, "nodeB's neighbour should include nodeA.");

            var neighboursOfC = graph.Neighbours(nodeC);
            Assert.AreEqual(0, neighboursOfC.Length, "nodeC should have no neighbours as the connection is unidirectional from nodeA to nodeC.");

            var neighboursOfD = graph.Neighbours(nodeD);
            Assert.AreEqual(1, neighboursOfD.Length, "nodeD should have one neighbour.");
            CollectionAssert.Contains(neighboursOfD, nodeA, "nodeD's neighbour should include nodeA.");
        }

        [Test]
        public void AllNodesRecursive_ShouldNotIncludeNodesFromRemovedChildGraphs()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            Graph childGraph = new Graph();
            childGraph.Add(nodeC);
            graph.AddGraph(childGraph);

            graph.RemoveGraph(childGraph);

            var allNodesRecursive = graph.AllNodesRecursive;

            Assert.AreEqual(2, allNodesRecursive.Length, "After removing a child graph, AllNodesRecursive should not include its nodes.");
            CollectionAssert.Contains(allNodesRecursive, nodeA, "AllNodesRecursive should contain nodeA.");
            CollectionAssert.Contains(allNodesRecursive, nodeB, "AllNodesRecursive should contain nodeB.");
            CollectionAssert.DoesNotContain(allNodesRecursive, nodeC, "AllNodesRecursive should not contain nodeC after the child graph is removed.");
        }

        [Test]
        public void AddGraph_ShouldNotAllowNullGraph()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => graph.AddGraph(null), "Adding a null graph should throw ArgumentNullException.");
            Assert.That(ex.ParamName, Is.EqualTo("child"), "Exception parameter name should be 'child'.");
        }

        [Test]
        public void AllNodes_ShouldReflectOnlyDirectlyAddedNodes()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            Graph childGraph = new Graph();
            childGraph.Add(nodeC);
            graph.AddGraph(childGraph);

            var allNodes = graph.AllNodes;

            Assert.AreEqual(2, allNodes.Length, "AllNodes should only include nodes directly added to the graph.");
            CollectionAssert.Contains(allNodes, nodeA, "AllNodes should contain nodeA.");
            CollectionAssert.Contains(allNodes, nodeB, "AllNodes should contain nodeB.");
            CollectionAssert.DoesNotContain(allNodes, nodeC, "AllNodes should not contain nodeC from the child graph.");
        }

        [Test]
        public void GraphWithOnlyBidirectionalConnections_ShouldBeUndirectedAndAcyclic()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeB.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);

            Assert.IsFalse(graph.IsDirected, "Graph should be undirected when all connections are bidirectional.");
            Assert.IsFalse(graph.IsAcyclic, "Undirected graph with bidirectional connections should not be acyclic.");
            Assert.IsTrue(graph.IsCyclic, "Undirected graph with bidirectional connections be cyclic.");
        }

        [Test]
        public void GraphWithBidirectionalCycle_ShouldBeCyclic()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeB.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);
            nodeC.ConnectTo(nodeA, NodeConnectionDirection.Bidirectional);

            Assert.IsFalse(graph.IsAcyclic, "Graph with a bidirectional cycle should not be acyclic.");
            Assert.IsTrue(graph.IsCyclic, "Graph with a bidirectional cycle should be cyclic.");
        }
        
        [Test]
        public void GraphWithUnidirectionalCycle_ShouldBeCyclicAndDirected()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeB.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);
            nodeC.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);

            Assert.IsFalse(graph.IsAcyclic, "Graph with a Unidirectional cycle should not be acyclic.");
            Assert.IsTrue(graph.IsCyclic, "Graph with a Unidirectional cycle should be cyclic.");
            Assert.IsTrue(graph.IsDirected, "Graph with Unidirectional connections should be Directed.");
        }

        [Test]
        public void AllNodesRecursive_ShouldHandleGraphsWithNoChildGraphs()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            var allNodesRecursive = graph.AllNodesRecursive;

            Assert.AreEqual(2, allNodesRecursive.Length, "AllNodesRecursive should correctly return all nodes when there are no child graphs.");
            CollectionAssert.Contains(allNodesRecursive, nodeA, "AllNodesRecursive should contain nodeA.");
            CollectionAssert.Contains(allNodesRecursive, nodeB, "AllNodesRecursive should contain nodeB.");
        }

        [Test]
        public void AddGraph_ShouldMaintainCorrectGraphHierarchy()
        {
            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();
            Graph childGraph3 = new Graph();

            graph.AddGraph(childGraph1);
            graph.AddGraph(childGraph2);
            childGraph1.AddGraph(childGraph3);

            var hierarchy = childGraph3.GraphHierarchy;

            Assert.AreEqual(2, hierarchy.Length, "GraphHierarchy should correctly reflect the hierarchy depth.");
            Assert.AreEqual(graph, hierarchy[0], "First element in GraphHierarchy should be the root graph.");
            Assert.AreEqual(childGraph1, hierarchy[1], "Second element in GraphHierarchy should be childGraph1.");
        }

        [Test]
        public void IsDirected_ShouldReflectMixedConnectionDirections()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);

            Assert.IsTrue(graph.IsDirected, "Graph should be directed when there are both unidirectional and bidirectional connections.");
        }
        
        [Test]
        public void GraphId_ShouldBeUnique()
        {
            Graph anotherGraph = new Graph();
            Assert.AreNotEqual(graph.Id, anotherGraph.Id, "Each Graph instance should have a unique Id.");
        }

        [Test]
        public void NodeId_ShouldBeUnique()
        {
            Node anotherNode = new Node();
            Assert.AreNotEqual(nodeA.Id, anotherNode.Id, "Each Node instance should have a unique Id.");
        }

        [Test]
        public void AddGraph_WithNonGraphType_ShouldThrowArgumentException()
        {
            var mockGraph = new MockGraph();
            var ex = Assert.Throws<ArgumentException>(() => graph.AddGraph(mockGraph), "Adding a graph of a different type should throw ArgumentException.");
            Assert.That(ex.Message, Does.Contain("Child graph must be of type Graph."), "Exception message should indicate that the child graph must be of type Graph.");
        }

        [Test]
        public void ParentGraph_ShouldRemainImmutableExternally()
        {
            Graph childGraph = new Graph();
            graph.AddGraph(childGraph);

            // Attempting to set ParentGraph externally should not be possible
            // Since ParentGraph's setter is private, this test ensures it remains unchanged
            Assert.AreEqual(graph, childGraph.ParentGraph, "ParentGraph should remain set to the parent graph and not be externally mutable.");
        }

        [Test]
        public void AddGraph_MultipleLevels_ShouldMaintainCorrectHierarchy()
        {
            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();
            Graph childGraph3 = new Graph();

            graph.AddGraph(childGraph1);
            childGraph1.AddGraph(childGraph2);
            childGraph2.AddGraph(childGraph3);

            Assert.AreEqual(3, childGraph3.GraphHierarchy.Length, "childGraph3 should have three ancestors in its GraphHierarchy.");
            Assert.AreEqual(graph, childGraph3.GraphHierarchy[0], "The first ancestor should be the main graph.");
            Assert.AreEqual(childGraph1, childGraph3.GraphHierarchy[1], "The second ancestor should be childGraph1.");
            Assert.AreEqual(childGraph2, childGraph3.GraphHierarchy[2], "The third ancestor should be childGraph2.");
        }

        [Test]
        public void AllNodesRecursive_ShouldIncludeNodesFromOtherGraphs()
        {
            Graph anotherGraph = new Graph();
            anotherGraph.Add(nodeF);

            graph.Add(nodeA);
            graph.AddGraph(anotherGraph);
            anotherGraph.Add(nodeB);

            var allNodesRecursive = graph.AllNodesRecursive;

            Assert.AreEqual(3, allNodesRecursive.Length, "AllNodesRecursive should include nodeA, nodeB and nodeF.");
            CollectionAssert.Contains(allNodesRecursive, nodeA, "AllNodesRecursive should contain nodeA.");
            CollectionAssert.Contains(allNodesRecursive, nodeB, "AllNodesRecursive should contain nodeB.");
            CollectionAssert.Contains(allNodesRecursive, nodeF, "AllNodesRecursive should contain nodeF.");
        }

        [Test]
        public void ConnectNodes_ShouldPreventDuplicateBidirectionalConnections()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            var firstConnection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            var secondConnection = nodeB.ConnectTo(nodeA, NodeConnectionDirection.Bidirectional);

            Assert.AreEqual(1, nodeA.OutboundConnections.Length, "Only one bidirectional connection should exist between nodeA and nodeB.");
            Assert.AreEqual(1, nodeB.OutboundConnections.Length, "Only one bidirectional connection should exist between nodeB and nodeA.");
            Assert.AreSame(firstConnection, secondConnection, "Both connections should reference the same connection object.");
        }

        [Test]
        public void ConnectNodes_ShouldHandleChangingConnectionDirection()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            var connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            Assert.AreEqual(NodeConnectionDirection.Unidirectional, connection.Direction, "Initial connection direction should be Unidirectional.");

            // Change direction by connecting in the opposite direction
            var updatedConnection = nodeB.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);
            Assert.AreEqual(NodeConnectionDirection.Bidirectional, connection.Direction, "Connection should upgrade to Bidirectional after opposite connection is added.");
            Assert.AreSame(connection, updatedConnection, "Updated connection should be the same as the original connection.");
        }

        [Test]
        public void RemoveConnection_ShouldNotAffectOtherConnections()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            var connectionAB = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            var connectionAC = nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);

            bool removed = nodeA.RemoveConnection(connectionAB);

            Assert.IsTrue(removed, "Removing connectionAB should return true.");
            Assert.IsFalse(nodeA.Connections.Contains(connectionAB), "nodeA should no longer contain connectionAB.");
            Assert.IsFalse(nodeB.Connections.Contains(connectionAB), "nodeB should no longer contain connectionAB.");
            Assert.AreEqual(1, nodeA.OutboundConnections.Length, "nodeA should have only one outbound connection after removal.");
            CollectionAssert.Contains(nodeA.Neighbours, nodeC, "nodeA should still have nodeC as a neighbour.");
            CollectionAssert.DoesNotContain(nodeA.Neighbours, nodeB, "nodeA should no longer have nodeB as a neighbour.");
        }

        [Test]
        public void AddGraph_ShouldThrowWhenGraphIsAlreadyInHierarchy()
        {
            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();

            graph.AddGraph(childGraph1);
            childGraph1.AddGraph(childGraph2);

            var ex = Assert.Throws<InvalidOperationException>(() => childGraph2.AddGraph(graph), "Adding the main graph as a child to childGraph2 should throw InvalidOperationException.");
            Assert.That(ex.Message, Is.EqualTo("Adding this child would create a circular hierarchy."), "Exception message should indicate circular hierarchy.");
        }

        [Test]
        public void AllNodes_ShouldReturnImmutableArray()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            var nodes = graph.AllNodes;
            nodes[0] = nodeC; // Attempt to modify the returned array

            Assert.AreEqual(2, graph.AllNodes.Length, "AllNodes should return a new array each time and modifications to it should not affect the graph.");
            CollectionAssert.Contains(graph.AllNodes, nodeA, "AllNodes should still contain nodeA.");
            CollectionAssert.Contains(graph.AllNodes, nodeB, "AllNodes should still contain nodeB.");
            CollectionAssert.DoesNotContain(graph.AllNodes, nodeC, "AllNodes should not contain nodeC after attempting external modification.");
        }

        [Test]
        public void Neighbours_ShouldReturnEmptyArrayForIsolatedNode()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            // nodeA is connected to nodeB
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // nodeC remains isolated

            var neighboursOfC = graph.Neighbours(nodeC);
            Assert.IsEmpty(neighboursOfC, "Isolated nodeC should have no neighbours.");
        }

        [Test]
        public void Neighbours_ShouldReturnCorrectNodesAfterRemovingConnection()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);

            // Remove connection from nodeA to nodeB
            var connectionAB = nodeA.OutboundConnections.FirstOrDefault(c => c.ToNode == nodeB);
            nodeA.RemoveConnection(connectionAB);

            var neighboursOfA = graph.Neighbours(nodeA);
            Assert.AreEqual(1, neighboursOfA.Length, "After removing connectionAB, nodeA should have one neighbour.");
            CollectionAssert.Contains(neighboursOfA, nodeC, "After removing connectionAB, nodeA should have nodeC as a neighbour.");
            CollectionAssert.DoesNotContain(neighboursOfA, nodeB, "After removing connectionAB, nodeA should not have nodeB as a neighbour.");

            var neighboursOfB = graph.Neighbours(nodeB);
            Assert.IsEmpty(neighboursOfB, "After removing connectionAB, nodeB should have no neighbours.");
        }

        [Test]
        public void GraphWithMultipleChildGraphs_ShouldMaintainSeparateHierarchies()
        {
            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();
            Graph childGraph3 = new Graph();

            graph.AddGraph(childGraph1);
            graph.AddGraph(childGraph2);
            childGraph1.AddGraph(childGraph3);

            Assert.AreEqual(1, childGraph1.GraphHierarchy.Length, "childGraph1 should have one ancestor in its GraphHierarchy.");
            Assert.AreEqual(graph, childGraph1.GraphHierarchy[0], "childGraph1's ancestor should be the main graph.");

            Assert.AreEqual(2, childGraph3.GraphHierarchy.Length, "childGraph3 should have two ancestors in its GraphHierarchy.");
            Assert.AreEqual(graph, childGraph3.GraphHierarchy[0], "The first ancestor of childGraph3 should be the main graph.");
            Assert.AreEqual(childGraph1, childGraph3.GraphHierarchy[1], "The second ancestor of childGraph3 should be childGraph1.");

            Assert.AreEqual(1, childGraph2.GraphHierarchy.Length, "childGraph2 should have one ancestor in its GraphHierarchy.");
            Assert.AreEqual(graph, childGraph2.GraphHierarchy[0], "childGraph2's ancestor should be the main graph.");
        }

        [Test]
        public void RemoveGraph_ShouldNotAffectParentGraph()
        {
            Graph childGraph = new Graph();
            graph.AddGraph(childGraph);

            graph.RemoveGraph(childGraph);

            Assert.IsNull(childGraph.ParentGraph, "After removal, childGraph's ParentGraph should be null.");
            Assert.IsEmpty(graph.ChildGraphs, "After removal, the main graph should have no child graphs.");
        }

        [Test]
        public void AddGraph_ShouldAllowReusingRemovedGraph()
        {
            Graph childGraph = new Graph();
            graph.AddGraph(childGraph);
            graph.RemoveGraph(childGraph);

            // Re-add the same graph
            graph.AddGraph(childGraph);

            Assert.AreEqual(1, graph.ChildGraphs.Length, "After re-adding, child graph count should be 1.");
            Assert.AreEqual(graph, childGraph.ParentGraph, "Re-added childGraph's ParentGraph should be set to the main graph.");
        }

        [Test]
        public void ConnectNodes_ShouldHandleMultipleConnectionsBetweenDifferentNodes()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);
            graph.Add(nodeD);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);
            nodeB.ConnectTo(nodeD, NodeConnectionDirection.Bidirectional);
            nodeC.ConnectTo(nodeD, NodeConnectionDirection.Unidirectional);

            Assert.AreEqual(2, nodeA.OutboundConnections.Length, "nodeA should have two outbound connections.");
            Assert.AreEqual(1, nodeD.OutboundConnections.Length, "nodeD should have one outbound connection.");
        }

        [Test]
        public void IsDirected_ShouldUpdateCorrectlyAfterChangingConnections()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            // Initially undirected
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeB.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);
            Assert.IsFalse(graph.IsDirected, "Graph should initially be undirected.");

            // Add a unidirectional connection
            nodeC.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);
            Assert.IsTrue(graph.IsDirected, "Graph should be directed after adding a unidirectional connection.");
        }

        [Test]
        public void IsCyclic_ShouldReflectChangesAfterAddingAndRemovingCycles()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            // Create a cycle: A -> B -> C -> A
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeB.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);
            nodeC.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);

            Assert.IsTrue(graph.IsCyclic, "Graph should be cyclic after adding a cycle.");

            // Remove the cycle by disconnecting C -> A
            var connectionCA = nodeC.OutboundConnections.FirstOrDefault(c => c.ToNode == nodeA);
            nodeC.RemoveConnection(connectionCA);

            Assert.IsFalse(graph.IsCyclic, "Graph should not be cyclic after removing the cycle.");
        }

        [Test]
        public void RemoveGraph_ShouldOnlyRemoveSpecifiedGraph()
        {
            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();
            Graph childGraph3 = new Graph();

            graph.AddGraph(childGraph1);
            graph.AddGraph(childGraph2);
            childGraph1.AddGraph(childGraph3);

            graph.RemoveGraph(childGraph1);

            Assert.IsNull(childGraph1.ParentGraph, "childGraph1's ParentGraph should be null after removal.");
            Assert.IsNotNull(childGraph2.ParentGraph, "childGraph2's ParentGraph should still be set.");
            Assert.AreEqual(graph, childGraph2.ParentGraph, "childGraph2's ParentGraph should still be the main graph.");

            // childGraph3 should have its ParentGraph as childGraph1
            Assert.AreEqual(childGraph1, childGraph3.ParentGraph, "childGraph3's ParentGraph should still be childGraph1.");
        }

        [Test]
        public void Neighbours_ShouldBeAccurateAfterMultipleConnectionChanges()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);
            graph.Add(nodeD);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);
            nodeB.ConnectTo(nodeD, NodeConnectionDirection.Unidirectional);

            // Initial Neighbours
            var neighboursOfA = graph.Neighbours(nodeA);
            Assert.AreEqual(2, neighboursOfA.Length, "nodeA should initially have two neighbours.");
            CollectionAssert.Contains(neighboursOfA, nodeB, "nodeA should have nodeB as a neighbour.");
            CollectionAssert.Contains(neighboursOfA, nodeC, "nodeA should have nodeC as a neighbour.");

            // Remove connection A -> B
            var connectionAB = nodeA.OutboundConnections.FirstOrDefault(c => c.ToNode == nodeB);
            nodeA.RemoveConnection(connectionAB);

            neighboursOfA = graph.Neighbours(nodeA);
            Assert.AreEqual(1, neighboursOfA.Length, "After removing connection A->B, nodeA should have one neighbour.");
            CollectionAssert.Contains(neighboursOfA, nodeC, "nodeA should still have nodeC as a neighbour.");
            CollectionAssert.DoesNotContain(neighboursOfA, nodeB, "nodeA should no longer have nodeB as a neighbour.");

            var neighboursOfB = graph.Neighbours(nodeB);
            Assert.AreEqual(1, neighboursOfB.Length, "nodeB should have one neighbour.");
            CollectionAssert.Contains(neighboursOfB, nodeD, "nodeB should have nodeD as a neighbour.");
            CollectionAssert.DoesNotContain(neighboursOfB, nodeA, "nodeB should no longer have nodeA as a neighbour.");
        }

        [Test]
        public void AllNodesRecursive_ShouldHandleGraphsWithSharedChildGraphs()
        {
            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();

            childGraph1.Add(nodeA);
            childGraph1.Add(nodeB);

             // nodeB is shared
             Assert.Throws<InvalidOperationException>(() => childGraph2.Add(nodeB), "Nodes cannot be shared");
        }

        [Test]
        public void ConnectNodes_ShouldThrowWhenConnectingToNull()
        {
            graph.Add(nodeA);

            var ex = Assert.Throws<ArgumentNullException>(() => nodeA.ConnectTo(null, NodeConnectionDirection.Unidirectional), "Connecting to a null node should throw ArgumentNullException.");
            Assert.That(ex.ParamName, Is.EqualTo("to"), "Exception parameter name should be 'to'.");
        }

        [Test]
        public void RemoveConnection_NonExistentConnection_ShouldReturnFalse()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            var connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            bool removed = nodeA.RemoveConnection(connection); // First removal
            bool removedAgain = nodeA.RemoveConnection(connection); // Attempt to remove again

            Assert.IsTrue(removed, "First removal of the connection should return true.");
            Assert.IsFalse(removedAgain, "Second removal of the same connection should return false.");
        }

        [Test]
        public void AddGraph_ShouldNotAllowAddingGraphMultipleTimes()
        {
            Graph childGraph = new Graph();
            graph.AddGraph(childGraph);

            var ex = Assert.Throws<InvalidOperationException>(() => graph.AddGraph(childGraph), "Adding the same child graph multiple times should throw InvalidOperationException.");
            Assert.That(ex.Message, Is.EqualTo("The child graph already has a parent."), "Exception message should indicate that the child graph already has a parent.");
        }

        [Test]
        public void AllNodesRecursive_ShouldHandleComplexHierarchies()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);

            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();
            Graph childGraph3 = new Graph();
            Graph childGraph4 = new Graph();

            childGraph1.Add(nodeC);
            childGraph2.Add(nodeD);
            childGraph3.Add(nodeE);
            childGraph4.Add(nodeF);

            graph.AddGraph(childGraph1);
            graph.AddGraph(childGraph2);
            childGraph1.AddGraph(childGraph3);
            childGraph2.AddGraph(childGraph4);

            var allNodesRecursive = graph.AllNodesRecursive;

            Assert.AreEqual(6, allNodesRecursive.Length, "AllNodesRecursive should include all nodes from complex hierarchies.");
            CollectionAssert.Contains(allNodesRecursive, nodeA, "AllNodesRecursive should contain nodeA.");
            CollectionAssert.Contains(allNodesRecursive, nodeB, "AllNodesRecursive should contain nodeB.");
            CollectionAssert.Contains(allNodesRecursive, nodeC, "AllNodesRecursive should contain nodeC.");
            CollectionAssert.Contains(allNodesRecursive, nodeD, "AllNodesRecursive should contain nodeD.");
            CollectionAssert.Contains(allNodesRecursive, nodeE, "AllNodesRecursive should contain nodeE.");
            CollectionAssert.Contains(allNodesRecursive, nodeF, "AllNodesRecursive should contain nodeF.");
        }

        [Test]
        public void IsDirectedCyclic_ShouldReturnFalseForUndirectedCyclicGraph()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeB.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);
            nodeC.ConnectTo(nodeA, NodeConnectionDirection.Bidirectional);

            Assert.IsFalse(graph.IsDirectedCyclic, "IsDirectedCyclic should return false for undirected cyclic graphs.");
        }

        [Test]
        public void IsDirectedAcyclic_ShouldReturnFalseForDirectedCyclicGraph()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeB.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);

            Assert.IsFalse(graph.IsDirectedAcyclic, "IsDirectedAcyclic should return false for directed cyclic graphs.");
        }

        [Test]
        public void GraphHierarchy_ShouldReflectDeeplyNestedGraphs()
        {
            Graph childGraph1 = new Graph();
            Graph childGraph2 = new Graph();
            Graph childGraph3 = new Graph();

            graph.AddGraph(childGraph1);
            childGraph1.AddGraph(childGraph2);
            childGraph2.AddGraph(childGraph3);

            var hierarchy = childGraph3.GraphHierarchy;

            Assert.AreEqual(3, hierarchy.Length, "GraphHierarchy should correctly reflect deeply nested graphs.");
            Assert.AreEqual(graph, hierarchy[0], "First ancestor should be the main graph.");
            Assert.AreEqual(childGraph1, hierarchy[1], "Second ancestor should be childGraph1.");
            Assert.AreEqual(childGraph2, hierarchy[2], "Third ancestor should be childGraph2.");
        }

        [Test]
        public void AllNodesRecursive_ShouldExcludeNodesFromSiblingGraphs()
        {
            Graph siblingGraph1 = new Graph();
            Graph siblingGraph2 = new Graph();

            siblingGraph1.Add(nodeA);
            siblingGraph2.Add(nodeB);

            graph.AddGraph(siblingGraph1);
            graph.AddGraph(siblingGraph2);

            var allNodesRecursive = graph.AllNodesRecursive;

            Assert.AreEqual(2, allNodesRecursive.Length, "AllNodesRecursive should include nodeA and nodeB only.");
            CollectionAssert.Contains(allNodesRecursive, nodeA, "AllNodesRecursive should contain nodeA.");
            CollectionAssert.Contains(allNodesRecursive, nodeB, "AllNodesRecursive should contain nodeB.");
        }

        [Test]
        public void GraphWithNoConnections_ShouldBeAcyclicAndUndirected()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            Assert.IsTrue(graph.IsAcyclic, "Graph with no connections should be acyclic.");
            Assert.IsFalse(graph.IsDirected, "Graph with no connections should be undirected.");
            Assert.IsFalse(graph.IsCyclic, "Graph with no connections should not be cyclic.");
        }

        [Test]
        public void ConnectNodes_ShouldMaintainCorrectInboundConnections()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeC.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            var inboundConnectionsB = nodeB.InboundConnections;

            Assert.AreEqual(2, inboundConnectionsB.Length, "nodeB should have two inbound connections.");
            CollectionAssert.Contains(inboundConnectionsB, nodeA.OutboundConnections.First(c => c.ToNode == nodeB), "nodeB should have connection from nodeA.");
            CollectionAssert.Contains(inboundConnectionsB, nodeC.OutboundConnections.First(c => c.ToNode == nodeB), "nodeB should have connection from nodeC.");
        }

        [Test]
        public void ConnectNodes_ShouldMaintainCorrectOutboundConnections()
        {
            graph.Add(nodeA);
            graph.Add(nodeB);
            graph.Add(nodeC);

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);

            var outboundConnectionsA = nodeA.OutboundConnections;

            Assert.AreEqual(2, outboundConnectionsA.Length, "nodeA should have two outbound connections.");
            CollectionAssert.Contains(outboundConnectionsA, nodeA.Connections.First(c => c.ToNode == nodeB), "nodeA should have outbound connection to nodeB.");
            CollectionAssert.Contains(outboundConnectionsA, nodeA.Connections.First(c => c.ToNode == nodeC), "nodeA should have outbound connection to nodeC.");
        }

        [Test]
        public void AllNodesRecursive_ShouldHandleGraphWithNoNodesButWithChildGraphs()
        {
            Graph childGraph = new Graph();
            childGraph.Add(nodeA);
            graph.AddGraph(childGraph);

            var allNodesRecursive = graph.AllNodesRecursive;

            Assert.AreEqual(1, allNodesRecursive.Length, "AllNodesRecursive should include nodes from child graphs even if the main graph has no nodes.");
            CollectionAssert.Contains(allNodesRecursive, nodeA, "AllNodesRecursive should contain nodeA from the child graph.");
        }

        [Test]
        public void GraphHierarchy_ShouldReflectImmediateParentOnly()
        {
            Graph childGraph = new Graph();
            graph.AddGraph(childGraph);

            var hierarchy = childGraph.GraphHierarchy;

            Assert.AreEqual(1, hierarchy.Length, "childGraph should have one ancestor in its GraphHierarchy.");
            Assert.AreEqual(graph, hierarchy[0], "childGraph's ancestor should be the main graph.");
        }

        [Test]
        public void AllNodesRecursive_ShouldReturnEmptyWhenNoNodesAndNoChildGraphs()
        {
            var allNodesRecursive = graph.AllNodesRecursive;

            Assert.IsEmpty(allNodesRecursive, "AllNodesRecursive should be empty when there are no nodes and no child graphs.");
        }
    }

    // MockGraph class to test adding non-Graph types
    public class MockGraph : IGraph
    {
        public string Id { get; set; }
        public bool IsDirectedAcyclic => throw new NotImplementedException();
        public bool IsDirectedCyclic => throw new NotImplementedException();
        public bool IsAcyclic => throw new NotImplementedException();
        public bool IsCyclic => throw new NotImplementedException();
        public bool IsDirected => throw new NotImplementedException();
        public IGraph ParentGraph { get; set; }
        public IGraph[] ChildGraphs => throw new NotImplementedException();
        public IGraph[] GraphHierarchy => throw new NotImplementedException();
        public INode[] AllNodes => throw new NotImplementedException();
        public INode[] AllNodesRecursive => throw new NotImplementedException();

        public void Add(INode newNode)
        {
            throw new NotImplementedException();
        }

        public bool Remove(INode node)
        {
            throw new NotImplementedException();
        }

        public void AddGraph(IGraph child)
        {
            throw new NotImplementedException();
        }

        public void RemoveGraph(IGraph child)
        {
            throw new NotImplementedException();
        }

        public bool Contains(INode node)
        {
            throw new NotImplementedException();
        }

        public INode[] Neighbours(INode node)
        {
            throw new NotImplementedException();
        }
    }
}
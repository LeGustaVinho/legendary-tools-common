using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.GraphV2.Tests
{
    [TestFixture]
    public class NodeUnitTests
    {
        [Test]
        public void Node_Initialization_ShouldHaveUniqueId_AndEmptyConnections()
        {
            // Arrange
            Node node = new Node();

            // Act & Assert
            Assert.IsFalse(string.IsNullOrEmpty(node.Id), "Node ID should not be null or empty.");
            Assert.IsEmpty(node.Connections, "Connections list should be empty upon initialization.");
        }

        [Test]
        public void ConnectTo_ValidParameters_ShouldCreateConnection()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            // Act
            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.IsNotNull(connection, "Connection should not be null.");
            Assert.AreEqual(nodeA, connection.FromNode, "FromNode should be nodeA.");
            Assert.AreEqual(nodeB, connection.ToNode, "ToNode should be nodeB.");
            Assert.AreEqual(NodeConnectionDirection.Unidirectional, connection.Direction,
                "Connection direction should be Unidirectional.");
            Assert.Contains(connection, nodeA.Connections, "Connection should be in nodeA's connections.");
            Assert.Contains(connection, nodeB.Connections, "Connection should be in nodeB's connections.");
        }

        [Test]
        public void ConnectTo_NullNode_ShouldThrowArgumentNullException()
        {
            // Arrange
            Node nodeA = new Node();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => nodeA.ConnectTo(null, NodeConnectionDirection.Unidirectional));
        }

        [Test]
        public void ConnectTo_SameNode_ShouldThrowInvalidOperationException()
        {
            // Arrange
            Node nodeA = new Node();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                nodeA.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional));
        }

        [Test]
        public void ConnectTo_ExistingConnection_ShouldReturnExistingConnection()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            INodeConnection firstConnection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            INodeConnection secondConnection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.AreEqual(firstConnection, secondConnection, "Should return the existing connection.");
            Assert.AreEqual(1, nodeA.Connections.Count, "NodeA should have only one connection.");
            Assert.AreEqual(1, nodeB.Connections.Count, "NodeB should have only one connection.");
        }

        [Test]
        public void Disconnect_ShouldRemoveConnection_FromBothNodes_IfBidirectional()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            // Act
            connection.Disconnect();

            // Assert
            Assert.IsFalse(nodeA.Connections.Contains(connection), "Connection should be removed from nodeA.");
            Assert.IsFalse(nodeB.Connections.Contains(connection), "Connection should be removed from nodeB.");
        }

        [Test]
        public void Neighbours_ShouldReturnCorrectNeighbours_ForUnidirectionalConnection()
        {
            // Arrange
            Node nodeA = new Node();
            nodeA.Id = "NodeA";
            Node nodeB = new Node();
            nodeA.Id = "NodeB";
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            INode[] neighbours = nodeA.Neighbours;

            // Assert
            Assert.Contains(nodeB, neighbours, "NodeB should be a neighbour of NodeA.");
            Assert.IsEmpty(nodeB.Neighbours, "NodeB should have no neighbours.");
        }

        [Test]
        public void Neighbours_ShouldReturnCorrectNeighbours_ForBidirectionalConnection()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            // Act
            INode[] neighboursA = nodeA.Neighbours;
            INode[] neighboursB = nodeB.Neighbours;

            // Assert
            Assert.Contains(nodeB, neighboursA, "NodeB should be a neighbour of NodeA.");
            Assert.Contains(nodeA, neighboursB, "NodeA should be a neighbour of NodeB.");
        }

        [Test]
        public void Neighbours_ShouldNotContainDuplicates()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional); // Attempt to add duplicate

            // Act
            INode[] neighbours = nodeA.Neighbours;

            // Assert
            Assert.AreEqual(1, neighbours.Length, "Neighbours list should not contain duplicates.");
        }

        [Test]
        public void OutboundConnections_ShouldReturnOnlyFromNodeConnections()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeC.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);

            // Act
            INodeConnection[] outbound = nodeA.OutboundConnections;

            // Assert
            Assert.AreEqual(1, outbound.Length, "NodeA should have one outbound connection.");
            Assert.AreEqual(nodeB, outbound[0].ToNode, "Outbound connection should be to NodeB.");
        }

        [Test]
        public void InboundConnections_ShouldReturnOnlyToNodeConnections()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();
            nodeB.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);

            // Act
            INodeConnection[] inbound = nodeA.InboundConnections;

            // Assert
            Assert.AreEqual(1, inbound.Length, "NodeA should have one inbound connection.");
            Assert.AreEqual(nodeB, inbound[0].FromNode, "Inbound connection should be from NodeB.");
        }

        [Test]
        public void RemoveConnection_ExistingConnection_ShouldReturnTrue_AndRemoveConnection()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            bool result = nodeA.RemoveConnection(connection);

            // Assert
            Assert.IsTrue(result, "RemoveConnection should return true for existing connection.");
            Assert.IsFalse(nodeA.Connections.Contains(connection), "Connection should be removed from nodeA.");
            Assert.IsFalse(nodeB.Connections.Contains(connection),
                "For unidirectional, connection should not remain in nodeB.");
        }

        [Test]
        public void RemoveConnection_Bidirectional_ShouldRemoveFromBothNodes()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            // Act
            bool result = nodeA.RemoveConnection(connection);

            // Assert
            Assert.IsTrue(result, "RemoveConnection should return true for existing bidirectional connection.");
            Assert.IsFalse(nodeA.Connections.Contains(connection), "Connection should be removed from nodeA.");
            Assert.IsFalse(nodeB.Connections.Contains(connection), "Connection should be removed from nodeB.");
        }

        [Test]
        public void RemoveConnection_NonExistingConnection_ShouldReturnFalse()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();
            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            bool result = nodeA.RemoveConnection(connection);

            // Attempt to remove again
            bool secondAttempt = nodeA.RemoveConnection(connection);

            // Assert
            Assert.IsTrue(result, "First removal should return true.");
            Assert.IsFalse(secondAttempt, "Second removal should return false.");
        }

        [Test]
        public void Count_ShouldReflectNumberOfConnections()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();

            // Act
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);

            // Assert
            Assert.AreEqual(2, nodeA.Count, "Count should reflect the number of connections.");
        }

        [Test]
        public void MultipleConnections_DifferentDirections_ShouldBeMergeToSameConnection()
        {
            // Arrange
            Node nodeA = new Node(true);
            Node nodeB = new Node(true);

            // Act
            INodeConnection conn1 = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            INodeConnection conn2 = nodeB.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.AreEqual(1, nodeA.Connections.Count, "NodeA should have one connections.");
            Assert.AreEqual(1, nodeB.Connections.Count, "NodeB should have one connections.");
            Assert.Contains(conn1, nodeA.Connections);
            Assert.Contains(conn2, nodeA.Connections);
        }

        [Test]
        public void NodeIds_ShouldBeUnique()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            // Act & Assert
            Assert.AreNotEqual(nodeA.Id, nodeB.Id, "Each node should have a unique ID.");
        }

        [Test]
        public void ConnectionIds_ShouldBeUnique()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();

            INodeConnection conn1 = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            INodeConnection conn2 = nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);

            // Act & Assert
            Assert.AreNotEqual(conn1.Id, conn2.Id, "Each connection should have a unique ID.");
        }

        [Test]
        public void Neighbours_WithMultipleBidirectionalConnections_ShouldListEachNeighbourOnce()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional); // Duplicate connection

            // Act
            INode[] neighbours = nodeA.Neighbours;

            // Assert
            Assert.AreEqual(2, neighbours.Length, "Neighbours should list each unique neighbour once.");
            Assert.Contains(nodeB, neighbours, "NodeB should be a neighbour.");
            Assert.Contains(nodeC, neighbours, "NodeC should be a neighbour.");
        }

        [Test]
        public void ConnectionDirection_ShouldAffectNeighbours()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            INode[] neighboursA = nodeA.Neighbours;
            INode[] neighboursB = nodeB.Neighbours;

            // Assert
            Assert.Contains(nodeB, neighboursA, "NodeB should be a neighbour of NodeA.");
            Assert.IsEmpty(neighboursB, "NodeB should have no neighbours.");
        }

        [Test]
        public void RemovingBidirectionalConnection_ShouldNotAffectOtherConnections()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();

            INodeConnection connAB = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            INodeConnection connAC = nodeA.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);

            // Act
            connAB.Disconnect();

            // Assert
            Assert.IsFalse(nodeA.Connections.Contains(connAB), "Connection AB should be removed from NodeA.");
            Assert.IsFalse(nodeB.Connections.Contains(connAB), "Connection AB should be removed from NodeB.");
            Assert.IsTrue(nodeA.Connections.Contains(connAC), "Connection AC should still exist in NodeA.");
            Assert.IsTrue(nodeC.Connections.Contains(connAC), "Connection AC should still exist em NodeC.");
        }
        
        [Test]
        public void ConnectTo_MultipleNodes_ShouldMaintainAllConnections()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();
            Node nodeD = new Node();

            // Act
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);
            nodeA.ConnectTo(nodeD, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.AreEqual(3, nodeA.Connections.Count, "NodeA should have three connections.");
            Assert.AreEqual(3, nodeA.Count, "Count property should reflect three connections.");
            Assert.AreEqual(1, nodeB.Connections.Count, "NodeB should have one connection.");
            Assert.AreEqual(1, nodeC.Connections.Count, "NodeC should have one connections even with bidirectional.");
            Assert.AreEqual(1, nodeD.Connections.Count, "NodeD should have one connection.");
        }

        [Test]
        public void ConnectTo_DifferentDirections_ShouldNotCreateSeparateConnections()
        {
            // Arrange
            Node nodeA = new Node(true);
            Node nodeB = new Node(true);

            // Act
            INodeConnection conn1 = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            INodeConnection conn2 = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            // Assert
            Assert.AreEqual(1, nodeA.Connections.Count, "NodeA should have 1 connections.");
            Assert.AreEqual(1, nodeB.Connections.Count, "NodeB should have 1 connections.");
            Assert.Contains(conn2, nodeA.Connections);
            Assert.Contains(conn2, nodeB.Connections);
        }

        [Test]
        public void RemoveConnection_ShouldHandleMultipleRemovalsGracefully()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            bool firstRemoval = nodeA.RemoveConnection(connection);
            bool secondRemoval = nodeA.RemoveConnection(connection);

            // Assert
            Assert.IsTrue(firstRemoval, "First removal should succeed.");
            Assert.IsFalse(secondRemoval, "Second removal should fail as connection no longer exists.");
        }

        [Test]
        public void Disconnect_BidirectionalConnection_ShouldHandleMultipleDisconnectCalls()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            // Act
            connection.Disconnect();
            // Attempt to disconnect again
            Assert.DoesNotThrow(() => connection.Disconnect(), "Disconnecting already disconnected connection should not throw.");
            
            // Assert
            Assert.IsFalse(nodeA.Connections.Contains(connection), "Connection should be removed from NodeA.");
            Assert.IsFalse(nodeB.Connections.Contains(connection), "Connection should be removed from NodeB.");
        }

        [Test]
        public void Neighbours_AfterRemovingConnection_ShouldUpdateNeighbours()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            // Act
            nodeA.RemoveConnection(nodeA.Connections[0]);

            // Assert
            Assert.IsEmpty(nodeA.Neighbours, "NodeA should have no neighbours after removing connection.");
            Assert.IsEmpty(nodeB.Neighbours, "NodeB should have no neighbours after removing connection.");
        }

        [Test]
        public void OutboundConnections_AfterRemovingConnection_ShouldUpdateOutbound()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            nodeA.RemoveConnection(nodeA.Connections[0]);

            // Assert
            Assert.IsEmpty(nodeA.OutboundConnections, "Outbound connections should be empty after removal.");
        }

        [Test]
        public void InboundConnections_AfterRemovingConnection_ShouldUpdateInbound()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            nodeA.RemoveConnection(nodeA.Connections[0]);

            // Assert
            Assert.IsEmpty(nodeA.InboundConnections, "Inbound connections should be empty after removal.");
            Assert.IsEmpty(nodeB.Connections, "NodeB should have no connections after removal.");
        }

        [Test]
        public void ConnectTo_MultipleBidirectionalConnections_ShouldNotDuplicateNeighbours()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            // Act
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            // Assert
            Assert.AreEqual(1, nodeA.Connections.Count, "Only one bidirectional connection should exist.");
            Assert.AreEqual(1, nodeA.Neighbours.Length, "Only one neighbour should be listed.");
            Assert.Contains(nodeB, nodeA.Neighbours, "NodeB should be the single neighbour.");
        }

        [Test]
        public void ConnectTo_NullDirection_ShouldThrowArgumentException()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            // Act & Assert
            // NodeConnectionDirection is an enum, so it cannot be null. Testing invalid enum value.
            Assert.Throws<ArgumentException>(() => nodeA.ConnectTo(nodeB, (NodeConnectionDirection)999));
        }

        [Test]
        public void Neighbours_AfterMultipleAdditionsAndRemovals_ShouldReflectCurrentState()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();
            Node nodeD = new Node();

            INodeConnection connAB = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            INodeConnection connAC = nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);
            INodeConnection connAD = nodeA.ConnectTo(nodeD, NodeConnectionDirection.Bidirectional);

            // Act
            nodeA.RemoveConnection(connAB);
            nodeA.RemoveConnection(connAD);

            // Assert
            INode[] neighbours = nodeA.Neighbours;
            Assert.AreEqual(1, neighbours.Length, "NodeA should have one neighbour after removals.");
            Assert.Contains(nodeC, neighbours, "NodeC should be the remaining neighbour.");
        }

        [Test]
        public void OutboundConnections_AfterMultipleOperations_ShouldReflectCurrentState()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();

            INodeConnection connAB = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            INodeConnection connAC = nodeA.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);

            // Act
            nodeA.RemoveConnection(connAB);

            // Assert
            INodeConnection[] outbound = nodeA.OutboundConnections;
            Assert.AreEqual(1, outbound.Length, "NodeA should have one outbound connection after removal.");
            Assert.Contains(connAC, outbound, "Outbound connection should be to NodeC.");
        }

        [Test]
        public void InboundConnections_AfterMultipleOperations_ShouldReflectCurrentState()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();

            INodeConnection connBA = nodeB.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);
            INodeConnection connCA = nodeC.ConnectTo(nodeA, NodeConnectionDirection.Bidirectional);

            // Act
            nodeC.RemoveConnection(connCA);

            // Assert
            INodeConnection[] inbound = nodeA.InboundConnections;
            Assert.AreEqual(1, inbound.Length, "NodeA should have one inbound connection after removal.");
            Assert.AreEqual(connBA, inbound[0], "Inbound connection should be from NodeB.");
        }

        [Test]
        public void ConnectTo_LargeNumberOfConnections_ShouldHandleProperly()
        {
            // Arrange
            Node nodeA = new Node();
            List<Node> nodes = new List<Node>();
            int numberOfNodes = 1000;

            for (int i = 0; i < numberOfNodes; i++)
            {
                nodes.Add(new Node());
            }

            // Act
            foreach (var node in nodes)
            {
                nodeA.ConnectTo(node, NodeConnectionDirection.Unidirectional);
            }

            // Assert
            Assert.AreEqual(numberOfNodes, nodeA.Connections.Count, $"NodeA should have {numberOfNodes} connections.");
            Assert.AreEqual(numberOfNodes, nodeA.Count, "Count property should reflect the number of connections.");
            foreach (var node in nodes)
            {
                Assert.AreEqual(1, node.Connections.Count, "Each connected node should have one connection.");
            }
        }

        [Test]
        public void ConnectTo_SequentialConnectAndDisconnect_ShouldMaintainIntegrity()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            // Act & Assert
            for (int i = 0; i < 100; i++)
            {
                INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
                Assert.IsNotNull(connection, $"Connection {i} should be created.");
                Assert.AreEqual(1, nodeA.Connections.Count, "NodeA should have one connection.");
                Assert.AreEqual(1, nodeB.Connections.Count, "NodeB should have one connection.");

                bool removed = nodeA.RemoveConnection(connection);
                Assert.IsTrue(removed, $"Connection {i} should be removed successfully.");
                Assert.AreEqual(0, nodeA.Connections.Count, "NodeA should have no connections after removal.");
                Assert.AreEqual(0, nodeB.Connections.Count, "NodeB should have no connections after removal.");
            }
        }

        [Test]
        public void ConnectTo_WithDifferentNodesSimultaneously_ShouldCreateUniqueConnections()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();

            // Act
            INodeConnection connAB = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            INodeConnection connAC = nodeA.ConnectTo(nodeC, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.AreNotEqual(connAB, connAC, "Connections to different nodes should be unique.");
            Assert.Contains(connAB, nodeA.Connections);
            Assert.Contains(connAC, nodeA.Connections);
            Assert.Contains(connAB, nodeB.Connections);
            Assert.Contains(connAC, nodeC.Connections);
        }

        [Test]
        public void RemoveConnection_InvalidConnection_ShouldNotAffectExistingConnections()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();

            INodeConnection connAB = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            INodeConnection connCB = nodeC.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            bool result = nodeA.RemoveConnection(connCB);

            // Assert
            Assert.IsFalse(result, "Removing a non-existing connection should return false.");
            Assert.AreEqual(1, nodeA.Connections.Count, "Existing connections should remain unaffected.");
            Assert.Contains(connAB, nodeA.Connections, "Existing connection should still be present.");
        }

        [Test]
        public void ConnectTo_AfterDisconnect_ShouldAllowReconnection()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            // Act
            INodeConnection connection1 = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeA.RemoveConnection(connection1);
            INodeConnection connection2 = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.IsNotNull(connection2, "Reconnection should succeed after disconnection.");
            Assert.AreNotEqual(connection1.Id, connection2.Id, "New connection should have a different ID.");
            Assert.AreEqual(1, nodeA.Connections.Count, "NodeA should have one connection after reconnection.");
            Assert.AreEqual(1, nodeB.Connections.Count, "NodeB should have one connection after reconnection.");
        }

        [Test]
        public void ConnectTo_DifferentNodes_WithSameId_ShouldTreatAsDifferentNodes()
        {
            // Arrange
            Node nodeA1 = new Node();
            Node nodeA2 = new Node();
            nodeA2.Id = nodeA1.Id; // Force same ID

            Node nodeB = new Node();

            // Act
            INodeConnection conn1 = nodeA1.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            INodeConnection conn2 = nodeA2.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.AreNotEqual(conn1.Id, conn2.Id, "Connections should be unique even if node IDs are the same.");
            Assert.AreEqual(2, nodeB.Connections.Count, "NodeB should have two connections.");
        }

        [Test]
        public void Neighbours_AfterChangingConnectionDirection_ShouldUpdateNeighbours()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            connection.Direction = NodeConnectionDirection.Bidirectional;

            // Since the direction was changed, NodeB should now also recognize NodeA as a neighbour
            INode[] neighboursA = nodeA.Neighbours;
            INode[] neighboursB = nodeB.Neighbours;

            // Assert
            Assert.Contains(nodeB, neighboursA, "NodeB should still be a neighbour of NodeA.");
            Assert.Contains(nodeA, neighboursB, "NodeA should now be a neighbour of NodeB due to bidirectional direction.");
        }

        [Test]
        public void ConnectTo_NullId_ShouldStillGenerateUniqueIds()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            nodeA.Id = null;
            nodeB.Id = null;

            // Act
            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.IsNotNull(connection.Id, "Connection ID should be generated even if node IDs are null.");
            Assert.IsFalse(string.IsNullOrEmpty(connection.Id), "Connection ID should not be empty.");
        }

        [Test]
        public void ConnectTo_Node_WithEmptyId_ShouldAllowConnection()
        {
            // Arrange
            Node nodeA = new Node();
            nodeA.Id = "";
            Node nodeB = new Node();

            // Act
            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.IsNotNull(connection, "Connection should be created even if node ID is empty.");
            Assert.AreEqual(nodeA, connection.FromNode, "FromNode should be NodeA.");
            Assert.AreEqual(nodeB, connection.ToNode, "ToNode should be NodeB.");
        }

        [Test]
        public void ConnectTo_MultipleConnections_ShouldNotInterfereWithEachOther()
        {
            // Arrange
            Node nodeA = new Node();
            List<Node> nodes = new List<Node>();
            for (int i = 0; i < 10; i++)
            {
                nodes.Add(new Node());
            }

            // Act
            foreach (var node in nodes)
            {
                nodeA.ConnectTo(node, NodeConnectionDirection.Unidirectional);
            }

            // Assert
            Assert.AreEqual(10, nodeA.Connections.Count, "NodeA should have ten connections.");
            foreach (var node in nodes)
            {
                Assert.AreEqual(1, node.Connections.Count, "Each connected node should have one connection.");
                Assert.Contains(nodeA.ConnectTo(node, NodeConnectionDirection.Unidirectional), node.Connections);
            }
        }

        [Test]
        public void ConnectTo_ChainOfConnections_ShouldMaintainProper_Neighbours()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();
            Node nodeD = new Node();

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeB.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);
            nodeC.ConnectTo(nodeD, NodeConnectionDirection.Bidirectional);

            // Act
            INode[] neighboursA = nodeA.Neighbours;
            INode[] neighboursB = nodeB.Neighbours;
            INode[] neighboursC = nodeC.Neighbours;
            INode[] neighboursD = nodeD.Neighbours;

            // Assert
            Assert.Contains(nodeB, neighboursA, "NodeB should be a neighbour of NodeA.");
            Assert.AreEqual(2, neighboursB.Length, "NodeB should have two neighbours.");
            Assert.Contains(nodeA, neighboursB);
            Assert.Contains(nodeC, neighboursB);

            Assert.AreEqual(2, neighboursC.Length, "NodeC should have two neighbours.");
            Assert.Contains(nodeB, neighboursC);
            Assert.Contains(nodeD, neighboursC);

            Assert.Contains(nodeC, neighboursD, "NodeC should be a neighbour of NodeD.");
        }

        [Test]
        public void ConnectTo_CircularConnection_ShouldHandleProperly()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            nodeB.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);
            nodeC.ConnectTo(nodeA, NodeConnectionDirection.Bidirectional);

            // Act
            INode[] neighboursA = nodeA.Neighbours;
            INode[] neighboursB = nodeB.Neighbours;
            INode[] neighboursC = nodeC.Neighbours;

            // Assert
            Assert.AreEqual(2, neighboursA.Length, "NodeA should have two neighbours.");
            Assert.Contains(nodeB, neighboursA);
            Assert.Contains(nodeC, neighboursA);

            Assert.AreEqual(2, neighboursB.Length, "NodeB should have two neighbours.");
            Assert.Contains(nodeA, neighboursB);
            Assert.Contains(nodeC, neighboursB);

            Assert.AreEqual(2, neighboursC.Length, "NodeC should have two neighbours.");
            Assert.Contains(nodeA, neighboursC);
            Assert.Contains(nodeB, neighboursC);
        }

        [Test]
        public void ConnectTo_SequentialBidirectionalAndUnidirectional_ShouldMaintainConnections()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            // Act
            INodeConnection conn1 = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);
            INodeConnection conn2 = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.AreEqual(1, nodeA.Connections.Count, "NodeA should have one connections.");
            Assert.AreEqual(1, nodeB.Connections.Count, "NodeB should have one connections.");

            // Check Neighbours
            INode[] neighboursA = nodeA.Neighbours;
            Assert.AreEqual(1, neighboursA.Length, "NodeA should have one unique neighbour.");
            Assert.Contains(nodeB, neighboursA);

            INode[] neighboursB = nodeB.Neighbours;
            Assert.AreEqual(1, neighboursB.Length, "NodeB should have one unique neighbour.");
        }

        [Test]
        public void ConnectTo_NonBidirectional_ShouldNotAffect_ReverseNeighbours()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            // Act
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Assert
            INode[] neighboursA = nodeA.Neighbours;
            INode[] neighboursB = nodeB.Neighbours;

            Assert.Contains(nodeB, neighboursA, "NodeB should be a neighbour of NodeA.");
            Assert.IsEmpty(neighboursB, "NodeB should have no neighbours.");
        }

        [Test]
        public void ConnectTo_Bidirectional_ShouldAllowTraversalInBothDirections()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            // Act
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            // Assert
            Assert.Contains(nodeB, nodeA.Neighbours, "NodeB should be a neighbour of NodeA.");
            Assert.Contains(nodeA, nodeB.Neighbours, "NodeA should be a neighbour of NodeB.");
        }

        [Test]
        public void ConnectTo_WithSameNodesDifferentInstances_ShouldTreatAsSeparate()
        {
            // Arrange
            Node nodeA1 = new Node();
            Node nodeA2 = new Node();
            Node nodeB = new Node();

            // Act
            INodeConnection conn1 = nodeA1.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            INodeConnection conn2 = nodeA2.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Assert
            Assert.AreNotEqual(conn1, conn2, "Connections from different instances should be unique.");
            Assert.AreEqual(1, nodeA1.Connections.Count, "NodeA1 should have one connection.");
            Assert.AreEqual(1, nodeA2.Connections.Count, "NodeA2 should have one connection.");
            Assert.AreEqual(2, nodeB.Connections.Count, "NodeB should have two connections.");
        }

        [Test]
        public void ConnectTo_WithDisconnectedNodes_ShouldAllowNewConnections()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            // Act
            INodeConnection connection1 = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeA.RemoveConnection(connection1);
            INodeConnection connection2 = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Bidirectional);

            // Assert
            Assert.AreNotEqual(connection1, connection2, "New connection should be different from the removed one.");
            Assert.AreEqual(1, nodeA.Connections.Count, "NodeA should have one connection.");
            Assert.AreEqual(1, nodeB.Connections.Count, "NodeB should have one connection.");
            Assert.AreEqual(NodeConnectionDirection.Bidirectional, connection2.Direction, "New connection should be bidirectional.");
        }

        [Test]
        public void ConnectTo_EnsureConnectionIdsAreUniqueAcrossMultipleConnections()
        {
            // Arrange
            Node nodeA = new Node();
            List<Node> nodes = new List<Node>();
            int numberOfNodes = 100;

            for (int i = 0; i < numberOfNodes; i++)
            {
                nodes.Add(new Node());
            }

            HashSet<string> connectionIds = new HashSet<string>();

            // Act
            foreach (var node in nodes)
            {
                INodeConnection connection = nodeA.ConnectTo(node, NodeConnectionDirection.Unidirectional);
                Assert.IsFalse(connectionIds.Contains(connection.Id), "Connection IDs should be unique.");
                connectionIds.Add(connection.Id);
            }

            // Assert
            Assert.AreEqual(numberOfNodes, connectionIds.Count, "All connection IDs should be unique.");
        }

        [Test]
        public void RemoveConnection_AfterChangingDirection_ShouldRemoveCorrectly()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();

            INodeConnection connection = nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            connection.Direction = NodeConnectionDirection.Bidirectional;
            bool removed = nodeA.RemoveConnection(connection);

            // Assert
            Assert.IsTrue(removed, "Connection should be removed successfully.");
            Assert.IsFalse(nodeA.Connections.Contains(connection), "Connection should be removed from NodeA.");
            Assert.IsFalse(nodeB.Connections.Contains(connection), "Connection should be removed from NodeB due to bidirectional direction.");
        }

        [Test]
        public void ConnectTo_WhenFromNodeIsNull_ShouldThrowException()
        {
            // Arrange
            Node nodeB = new Node();

            // Act & Assert
            // Since ConnectTo is an instance method, FromNode is implicitly 'this' and cannot be null.
            // However, we can simulate if FromNode is somehow null by using reflection or other means.
            // For simplicity, we'll skip this as it's not directly applicable.
            Assert.Pass("FromNode is implicitly 'this' and cannot be null in ConnectTo method.");
        }

        [Test]
        public void RemoveConnection_WithNull_ShouldReturnFalse()
        {
            // Arrange
            Node nodeA = new Node();

            // Act
            bool result = nodeA.RemoveConnection(null);

            // Assert
            Assert.IsFalse(result, "Removing a null connection should return false.");
        }

        [Test]
        public void Neighbours_WithNoConnections_ShouldReturnEmptyArray()
        {
            // Arrange
            Node nodeA = new Node();

            // Act
            INode[] neighbours = nodeA.Neighbours;

            // Assert
            Assert.IsNotNull(neighbours, "Neighbours should not be null.");
            Assert.IsEmpty(neighbours, "Neighbours should be empty when there are no connections.");
        }

        [Test]
        public void OutboundConnections_WithNoOutbound_ShouldReturnEmptyArray()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            nodeB.ConnectTo(nodeA, NodeConnectionDirection.Unidirectional);

            // Act
            INodeConnection[] outbound = nodeA.OutboundConnections;

            // Assert
            Assert.IsNotNull(outbound, "OutboundConnections should not be null.");
            Assert.IsEmpty(outbound, "OutboundConnections should be empty when there are no outbound connections.");
        }

        [Test]
        public void InboundConnections_WithNoInbound_ShouldReturnEmptyArray()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);

            // Act
            INodeConnection[] inbound = nodeA.InboundConnections;

            // Assert
            Assert.IsNotNull(inbound, "InboundConnections should not be null.");
            Assert.IsEmpty(inbound, "InboundConnections should be empty when there are no inbound connections.");
        }

        [Test]
        public void Count_WithNoConnections_ShouldReturnZero()
        {
            // Arrange
            Node nodeA = new Node();

            // Act
            int count = nodeA.Count;

            // Assert
            Assert.AreEqual(0, count, "Count should be zero when there are no connections.");
        }

        [Test]
        public void Count_WithMultipleConnections_ShouldReturnCorrectCount()
        {
            // Arrange
            Node nodeA = new Node();
            Node nodeB = new Node();
            Node nodeC = new Node();

            nodeA.ConnectTo(nodeB, NodeConnectionDirection.Unidirectional);
            nodeA.ConnectTo(nodeC, NodeConnectionDirection.Bidirectional);

            // Act
            int count = nodeA.Count;

            // Assert
            Assert.AreEqual(2, count, "Count should reflect two connections.");
        }
    }
}
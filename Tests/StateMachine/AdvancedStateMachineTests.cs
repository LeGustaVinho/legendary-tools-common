using System;
using System.Collections.Generic;
using LegendaryTools.GraphV2;
using NUnit.Framework;

namespace LegendaryTools.StateMachineV2.Tests
{
    public class AdvancedStateMachineTests
    {
        private AdvancedStateMachine<string> CreateSimpleStateMachine(string name = "TestStateMachine")
        {
            // Create AnyState
            State<string> anyState = new State<string>("AnyState");

            // Initialize var
            AdvancedStateMachine<string> advancedStateMachine = new AdvancedStateMachine<string>(anyState, name);

            // Add AnyState to the var
            advancedStateMachine.Add(anyState);

            return advancedStateMachine;
        }

        private IAdvancedState<string> CreateState(string name)
        {
            return new State<string>(name);
        }

        private void ConnectStates(IAdvancedState<string> from, IAdvancedState<string> to,
            NodeConnectionDirection direction = NodeConnectionDirection.Unidirectional)
        {
            from.ConnectTo(to, 0, direction);
        }

        [Test]
        public void AddParameter_ShouldAddParameterSuccessfully()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            string paramName = "Health";
            ParameterType paramType = ParameterType.Float;

            // Act
            advancedStateMachine.AddParameter(paramName, paramType);

            // Assert
            Assert.IsTrue(advancedStateMachine.ParameterValues.ContainsKey(paramName),
                $"Parameter '{paramName}' should exist in ParameterValues.");
            Assert.AreEqual(paramType, advancedStateMachine.ParameterValues[paramName].Type,
                $"Parameter '{paramName}' should be of type {paramType}.");
        }

        [Test]
        public void RemoveParameter_ShouldRemoveExistingParameter()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            string paramName = "IsAlive";
            ParameterType paramType = ParameterType.Bool;
            advancedStateMachine.AddParameter(paramName, paramType);

            // Act
            bool removed = advancedStateMachine.RemoveParameter(paramName, paramType);

            // Assert
            Assert.IsTrue(removed, $"Parameter '{paramName}' should be removed successfully.");
            Assert.IsFalse(advancedStateMachine.ParameterValues.ContainsKey(paramName),
                $"ParameterValues should not contain '{paramName}' after removal.");
        }

        [Test]
        public void RemoveParameter_ShouldReturnFalseForNonExistingParameter()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            string paramName = "Speed";

            // Act
            bool removed = advancedStateMachine.RemoveParameter(paramName, ParameterType.Int);

            // Assert
            Assert.IsFalse(removed, $"Removing non-existing parameter '{paramName}' should return false.");
        }

        [Test]
        public void Start_ShouldSetCurrentStateAndInvokeOnStateEnter()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> startState = CreateState("StartState");
            advancedStateMachine.Add(startState);

            bool onEnterCalled = false;
            ((State<string>)startState).OnStateEnter += (state) => onEnterCalled = true;

            // Act
            advancedStateMachine.Start(startState);

            // Assert
            Assert.IsTrue(advancedStateMachine.IsRunning, "var should be running after Start.");
            Assert.AreEqual(startState, advancedStateMachine.CurrentState, "CurrentState should be set to startState.");
            Assert.IsTrue(onEnterCalled, "OnStateEnter should be invoked when starting the var.");
        }

        [Test]
        public void Start_ShouldThrowExceptionIfStartStateNotInStateMachine()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> startState = CreateState("NonExistentState");

            // Act & Assert
            Assert.Throws<InvalidOperationException>(
                () => advancedStateMachine.Start(startState),
                "Starting with a state not in the var should throw InvalidOperationException.");
        }

        [Test]
        public void Stop_ShouldUnsetCurrentStateAndInvokeOnStateExit()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> startState = CreateState("StartState");
            advancedStateMachine.Add(startState);
            advancedStateMachine.Start(startState);

            bool onExitCalled = false;
            ((State<string>)startState).OnStateExit += (state) => onExitCalled = true;

            // Act
            advancedStateMachine.Stop();

            // Assert
            Assert.IsFalse(advancedStateMachine.IsRunning, "var should not be running after Stop.");
            Assert.IsNull(advancedStateMachine.CurrentState, "CurrentState should be null after stopping the var.");
            Assert.IsTrue(onExitCalled, "OnStateExit should be invoked when stopping the var.");
        }

        [Test]
        public void Update_ShouldInvokeOnStateUpdateWhenRunning()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> startState = CreateState("StartState");
            advancedStateMachine.Add(startState);
            advancedStateMachine.Start(startState);

            bool onUpdateCalled = false;
            ((State<string>)startState).OnStateUpdate += (state) => onUpdateCalled = true;

            // Act
            advancedStateMachine.Update();

            // Assert
            Assert.IsTrue(onUpdateCalled, "OnStateUpdate should be invoked when var is running.");
        }

        [Test]
        public void Update_ShouldNotInvokeOnStateUpdateWhenNotRunning()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();

            bool onUpdateCalled = false;

            // No state started

            // Act
            advancedStateMachine.Update();

            // Assert
            Assert.IsFalse(onUpdateCalled, "OnStateUpdate should not be invoked when var is not running.");
        }

        [Test]
        public void SetTrigger_ShouldEvaluateConditionsAndTransitState()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Jump", ParameterType.Trigger);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Connect StateA to StateB with "Jump" trigger condition
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("Jump");

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetTrigger("Jump");

            // Assert
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState,
                "var should transition to StateB when 'Jump' trigger is set.");
            Assert.IsTrue(onEnterBCalled, "OnStateEnter of StateB should be invoked upon transition.");
        }

        [Test]
        public void SetBool_ShouldEvaluateConditionsAndTransitState()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("IsRunning", ParameterType.Bool);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Connect StateA to StateB with "IsRunning" condition set to true
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("IsRunning", BoolParameterCondition.True);

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetBool("IsRunning", true);

            // Assert
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState,
                "var should transition to StateB when 'IsRunning' is set to true.");
            Assert.IsTrue(onEnterBCalled, "OnStateEnter of StateB should be invoked upon transition.");
        }

        [Test]
        public void SetInt_ShouldEvaluateConditionsAndTransitState()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Health", ParameterType.Int);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Connect StateA to StateB with "Health" > 50 condition
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("Health", IntParameterCondition.Greater, 50);

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetInt("Health", 75);

            // Assert
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState,
                "var should transition to StateB when 'Health' is greater than 50.");
            Assert.IsTrue(onEnterBCalled, "OnStateEnter of StateB should be invoked upon transition.");
        }

        [Test]
        public void SetFloat_ShouldEvaluateConditionsAndTransitState()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Speed", ParameterType.Float);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Connect StateA to StateB with "Speed" < 10.0f condition
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("Speed", FloatParameterCondition.Less, 10.0f);

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetFloat("Speed", 5.0f);

            // Assert
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState,
                "var should transition to StateB when 'Speed' is less than 10.0f.");
            Assert.IsTrue(onEnterBCalled, "OnStateEnter of StateB should be invoked upon transition.");
        }

        [Test]
        public void AddParameter_ShouldThrowExceptionWhenAddingDuplicateParameter()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            string paramName = "Energy";
            ParameterType paramType = ParameterType.Float;
            advancedStateMachine.AddParameter(paramName, paramType);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(
                () => advancedStateMachine.AddParameter(paramName, paramType),
                $"Adding duplicate parameter '{paramName}' should throw InvalidOperationException.");
        }

        [Test]
        public void Start_ShouldNotStartIfAlreadyRunning()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> startState = CreateState("StartState");
            advancedStateMachine.Add(startState);
            advancedStateMachine.Start(startState);

            bool onEnterCalled = false;
            ((State<string>)startState).OnStateEnter += (state) => onEnterCalled = true;

            // Act
            advancedStateMachine.Start(startState);

            // Assert
            Assert.IsTrue(advancedStateMachine.IsRunning, "var should remain running.");
            Assert.AreEqual(startState, advancedStateMachine.CurrentState, "CurrentState should remain as startState.");
            Assert.IsFalse(onEnterCalled, "OnStateEnter should not be invoked again when already running.");
        }

        [Test]
        public void Transit_ShouldInvokeOnStateExitAndOnStateEnter()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);
            advancedStateMachine.AddParameter("Trigger", ParameterType.Trigger);

            // Connect StateA to StateB with "Trigger" condition
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("Trigger");

            bool onExitCalled = false;
            bool onEnterCalled = false;

            ((State<string>)stateA).OnStateExit += (state) => onExitCalled = true;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetTrigger("Trigger");

            // Assert
            Assert.IsTrue(onExitCalled, "OnStateExit of StateA should be invoked upon transition.");
            Assert.IsTrue(onEnterCalled, "OnStateEnter of StateB should be invoked upon transition.");
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState, "var should transition to StateB.");
        }

        [Test]
        public void EvaluateConditions_ShouldReturnTrueWhenAllConditionsMet()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Health", ParameterType.Int);
            advancedStateMachine.AddParameter("IsAlive", ParameterType.Bool);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Connect StateA to StateB with "Health" > 50 and "IsAlive" == true conditions
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("Health", IntParameterCondition.Greater, 50);
            connection.AddCondition("IsAlive", BoolParameterCondition.True);

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetInt("Health", 75);
            advancedStateMachine.SetBool("IsAlive", true);
            
            // Assert
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState,
                "var should transition to StateB when all conditions are met.");
            Assert.IsTrue(onEnterBCalled, "OnStateEnter of StateB should be invoked upon transition.");
        }

        [Test]
        public void EvaluateConditions_ShouldReturnFalseWhenAnyConditionNotMet()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Health", ParameterType.Int);
            advancedStateMachine.AddParameter("IsAlive", ParameterType.Bool);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Connect StateA to StateB with "Health" > 50 and "IsAlive" == true conditions
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("Health", IntParameterCondition.Greater, 50);
            connection.AddCondition("IsAlive", BoolParameterCondition.True);

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetInt("Health", 40); // Condition not met
            advancedStateMachine.SetBool("IsAlive", true);

            // Assert
            Assert.AreEqual(stateA, advancedStateMachine.CurrentState,
                "var should remain in StateA when any condition is not met.");
            Assert.IsFalse(onEnterBCalled, "OnStateEnter of StateB should not be invoked when conditions are not met.");
        }

        [Test]
        public void AnyState_ShouldBeTransitionableFromAnyState()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();

            IAdvancedState<string> anyState = advancedStateMachine.AnyState;
            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            IAdvancedState<string> stateC = CreateState("StateC");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);
            advancedStateMachine.Add(stateC);

            advancedStateMachine.AddParameter("Trigger", ParameterType.Trigger);

            // Connect AnyState to StateC with "Trigger" condition
            ConnectStates(anyState, stateC);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)anyState.OutboundConnections[0];
            connection.AddCondition("Trigger");
            advancedStateMachine.Start(stateA);

            bool onExitA = false;
            bool onEnterC = false;

            ((State<string>)stateA).OnStateExit += (state) => onExitA = true;
            ((State<string>)stateC).OnStateEnter += (state) => onEnterC = true;

            // Act
            advancedStateMachine.SetTrigger("Trigger");

            // Assert
            Assert.IsTrue(onExitA, "OnStateExit of StateA should be invoked when transitioning via AnyState.");
            Assert.IsTrue(onEnterC, "OnStateEnter of StateC should be invoked when transitioning via AnyState.");
            Assert.AreEqual(stateC, advancedStateMachine.CurrentState,
                "var should transition to StateC via AnyState.");
        }

        [Test]
        public void ParameterDefinitions_ShouldContainAllAddedParameters()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            Dictionary<string, ParameterType> parameters = new Dictionary<string, ParameterType>
            {
                { "Health", ParameterType.Int },
                { "IsAlive", ParameterType.Bool },
                { "Speed", ParameterType.Float },
                { "Jump", ParameterType.Trigger }
            };

            // Act
            foreach (KeyValuePair<string, ParameterType> param in parameters)
                advancedStateMachine.AddParameter(param.Key, param.Value);

            // Assert
            foreach (KeyValuePair<string, ParameterType> param in parameters)
            {
                Assert.IsTrue(advancedStateMachine.ParameterValues.ContainsKey(param.Key),
                    $"ParameterDefinitions should contain '{param.Key}'.");
                Assert.AreEqual(param.Value, advancedStateMachine.ParameterValues[param.Key].Type,
                    $"Parameter '{param.Key}' should be of type {param.Value}.");
            }
        }

        [Test]
        public void CurrentState_ShouldBeNullAfterStopping()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> startState = CreateState("StartState");
            advancedStateMachine.Add(startState);
            advancedStateMachine.Start(startState);

            // Act
            advancedStateMachine.Stop();

            // Assert
            Assert.IsNull(advancedStateMachine.CurrentState, "CurrentState should be null after stopping the var.");
            Assert.IsFalse(advancedStateMachine.IsRunning, "var should not be running after stopping.");
        }

        [Test]
        public void Add_ShouldAddStateToStateMachine()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> state = CreateState("NewState");

            // Act
            advancedStateMachine.Add(state);

            // Assert
            Assert.Contains(state, advancedStateMachine.AllNodes,
                $"var should contain the added state '{state.Name}'.");
        }

        [Test]
        public void Remove_ShouldRemoveStateFromStateMachine()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> state = CreateState("RemovableState");
            advancedStateMachine.Add(state);

            // Act
            bool removed = advancedStateMachine.Remove(state);

            // Assert
            Assert.IsTrue(removed, $"State<string> '{state.Name}' should be removed successfully.");
            Assert.IsFalse(advancedStateMachine.Contains(state),
                $"var should not contain the removed state '{state.Name}'.");
        }

        [Test]
        public void Remove_ShouldReturnFalseWhenStateDoesNotExist()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> state = CreateState("NonExistentState");

            // Act
            bool removed = advancedStateMachine.Remove(state);

            // Assert
            Assert.IsFalse(removed, "Removing a non-existent state should return false.");
        }

        [Test]
        public void AddGraph_ShouldAddChildGraphToStateMachine()
        {
            // Arrange
            AdvancedStateMachine<string> parentAdvancedStateMachine = CreateSimpleStateMachine("ParentStateMachine");
            AdvancedStateMachine<string> childAdvancedStateMachine = CreateSimpleStateMachine("ChildStateMachine");

            // Act
            parentAdvancedStateMachine.AddGraph(childAdvancedStateMachine);

            // Assert
            Assert.Contains(childAdvancedStateMachine, parentAdvancedStateMachine.ChildGraphs,
                "ChildGraph should be added to the ParentStateMachine.");
        }

        [Test]
        public void RemoveGraph_ShouldRemoveChildGraphFromStateMachine()
        {
            // Arrange
            AdvancedStateMachine<string> parentAdvancedStateMachine = CreateSimpleStateMachine("ParentStateMachine");
            AdvancedStateMachine<string> childAdvancedStateMachine = CreateSimpleStateMachine("ChildStateMachine");
            parentAdvancedStateMachine.AddGraph(childAdvancedStateMachine);

            // Act
            parentAdvancedStateMachine.RemoveGraph(childAdvancedStateMachine);

            List<IGraph> childGraphs = new List<IGraph>(parentAdvancedStateMachine.ChildGraphs);
            // Assert
            Assert.IsFalse(childGraphs.Contains(childAdvancedStateMachine),
                "ChildGraph should be removed from the ParentStateMachine.");
        }

        [Test]
        public void AddMultipleParameters_ShouldContainAllParameters()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            Dictionary<string, ParameterType> parameters = new Dictionary<string, ParameterType>
            {
                { "Health", ParameterType.Int },
                { "IsAlive", ParameterType.Bool },
                { "Speed", ParameterType.Float },
                { "Jump", ParameterType.Trigger },
                { "Energy", ParameterType.Float }
            };

            // Act
            foreach (KeyValuePair<string, ParameterType> param in parameters)
                advancedStateMachine.AddParameter(param.Key, param.Value);

            // Assert
            foreach (KeyValuePair<string, ParameterType> param in parameters)
            {
                Assert.IsTrue(advancedStateMachine.ParameterValues.ContainsKey(param.Key),
                    $"ParameterValues should contain '{param.Key}'.");
                Assert.AreEqual(param.Value, advancedStateMachine.ParameterValues[param.Key].Type,
                    $"Parameter '{param.Key}' should be of type {param.Value}.");
            }
        }

        [Test]
        public void RemoveParameterInUse_ShouldRemoveParameterAndAffectTransitions()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Health", ParameterType.Int);
            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("Health", IntParameterCondition.Greater, 50);

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            bool removed = advancedStateMachine.RemoveParameter("Health", ParameterType.Int);

            // Assert
            Assert.IsTrue(removed, "Parameter 'Health' should be removed successfully.");
            Assert.IsFalse(advancedStateMachine.ParameterValues.ContainsKey("Health"),
                "ParameterValues should not contain 'Health' after removal.");

            // Attempt to set the removed parameter should throw exception
            Assert.Throws<InvalidOperationException>(
                () => advancedStateMachine.SetInt("Health", 75),
                "Setting a removed parameter should throw InvalidOperationException.");

            // Ensure no transition occurred
            Assert.AreEqual(stateA, advancedStateMachine.CurrentState,
                "var should remain in StateA after removing the parameter.");
            Assert.IsFalse(onEnterBCalled,
                "OnStateEnter of StateB should not be invoked after removing the parameter.");
        }

        [Test]
        public void SetParameterWithWrongType_ShouldThrowException()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("IsRunning", ParameterType.Bool);
            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("IsRunning", BoolParameterCondition.True);

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            advancedStateMachine.Start(stateA);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(
                () => advancedStateMachine.SetInt("IsRunning", 1),
                "Setting a parameter with wrong type should throw InvalidOperationException.");

            // Ensure no transition occurred
            Assert.AreEqual(stateA, advancedStateMachine.CurrentState,
                "var should remain in StateA after setting parameter with wrong type.");
            Assert.IsFalse(onEnterBCalled,
                "OnStateEnter of StateB should not be invoked after setting parameter with wrong type.");
        }

        [Test]
        public void SetNonExistentParameter_ShouldThrowException()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];

            Assert.Throws<InvalidOperationException>(() => connection.AddCondition("NonExistentParam"),
                "Setting a non-existent parameter should throw InvalidOperationException.");

            advancedStateMachine.Start(stateA);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => advancedStateMachine.SetTrigger("NonExistentParam"),
                "Setting a non-existent parameter should throw InvalidOperationException.");

            // Ensure no transition occurred
            Assert.AreEqual(stateA, advancedStateMachine.CurrentState,
                "var should remain in StateA after setting non-existent parameter.");
        }

        [Test]
        public void MultipleConnections_ShouldTransitionToFirstValidState()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Condition1", ParameterType.Bool);
            advancedStateMachine.AddParameter("Condition2", ParameterType.Bool);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            IAdvancedState<string> stateC = CreateState("StateC");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);
            advancedStateMachine.Add(stateC);

            // Connect StateA to StateB with Condition1 == true
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connectionAB = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connectionAB.AddCondition("Condition1", BoolParameterCondition.True);

            // Connect StateA to StateC with Condition2 == true
            ConnectStates(stateA, stateC);
            AdvancedStateConnection<string> connectionAC = (AdvancedStateConnection<string>)stateA.OutboundConnections[1];
            connectionAC.AddCondition("Condition2", BoolParameterCondition.True);

            bool onEnterBCalled = false;
            bool onEnterCCalled = false;

            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;
            ((State<string>)stateC).OnStateEnter += (state) => onEnterCCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetBool("Condition1", true);
            advancedStateMachine.SetBool("Condition2", true); // Both conditions are true

            // Assert
            Assert.IsTrue(onEnterBCalled, "var should transition to StateB as it is the first valid state.");
            Assert.IsFalse(onEnterCCalled,
                "var should not transition to StateC when StateB's condition is already met.");
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState, "var should be in StateB after transition.");
        }

        [Test]
        public void MultipleConditions_ShouldTransitionOnlyWhenAllConditionsMet()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Health", ParameterType.Int);
            advancedStateMachine.AddParameter("IsAlive", ParameterType.Bool);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Connect StateA to StateB with "Health" > 50 AND "IsAlive" == true
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("Health", IntParameterCondition.Greater, 50);
            connection.AddCondition("IsAlive", BoolParameterCondition.True);

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetInt("Health", 60); // Only one condition met
            Assert.AreEqual(stateA, advancedStateMachine.CurrentState,
                "var should remain in StateA when not all conditions are met.");
            Assert.IsFalse(onEnterBCalled,
                "OnStateEnter of StateB should not be invoked when not all conditions are met.");

            advancedStateMachine.SetBool("IsAlive", true); // Now both conditions met

            // Assert
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState,
                "var should transition to StateB when all conditions are met.");
            Assert.IsTrue(onEnterBCalled, "OnStateEnter of StateB should be invoked when all conditions are met.");
        }

        [Test]
        public void Neighbours_ShouldReturnCorrectNeighboursAfterMultipleConnections()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            IAdvancedState<string> stateC = CreateState("StateC");
            IAdvancedState<string> stateD = CreateState("StateD");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);
            advancedStateMachine.Add(stateC);
            advancedStateMachine.Add(stateD);

            // Connect StateA to StateB and StateC
            ConnectStates(stateA, stateB);
            ConnectStates(stateA, stateC);
            // Connect StateA to StateD with bidirectional
            ConnectStates(stateA, stateD, NodeConnectionDirection.Bidirectional);

            // Act
            INode[] neighbours = advancedStateMachine.Neighbours(stateA);

            // Assert
            Assert.AreEqual(3, neighbours.Length, "StateA should have three neighbours.");
            Assert.Contains(stateB, advancedStateMachine.Neighbours(stateA), "StateA should have StateB as a neighbour.");
            Assert.Contains(stateC, advancedStateMachine.Neighbours(stateA), "StateA should have StateC as a neighbour.");
            Assert.Contains(stateD, advancedStateMachine.Neighbours(stateA), "StateA should have StateD as a neighbour.");
        }

        [Test]
        public void GraphHierarchy_WithMultipleChildGraphs_ShouldReturnCorrectOrder()
        {
            // Arrange
            AdvancedStateMachine<string> rootAdvancedStateMachine = CreateSimpleStateMachine("RootStateMachine");
            AdvancedStateMachine<string> childAdvancedStateMachine1 = CreateSimpleStateMachine("ChildStateMachine1");
            AdvancedStateMachine<string> childAdvancedStateMachine2 = CreateSimpleStateMachine("ChildStateMachine2");
            AdvancedStateMachine<string> grandChildAdvancedStateMachine = CreateSimpleStateMachine("GrandChildStateMachine");

            rootAdvancedStateMachine.AddGraph(childAdvancedStateMachine1);
            childAdvancedStateMachine1.AddGraph(childAdvancedStateMachine2);
            childAdvancedStateMachine2.AddGraph(grandChildAdvancedStateMachine);

            // Act
            IGraph[] hierarchy = grandChildAdvancedStateMachine.GraphHierarchy;

            // Assert
            Assert.AreEqual(3, hierarchy.Length, "GraphHierarchy should contain four levels: Root, Child1, Child2.");
            Assert.AreEqual(rootAdvancedStateMachine, hierarchy[0], "First element should be RootStateMachine.");
            Assert.AreEqual(childAdvancedStateMachine1, hierarchy[1], "Second element should be ChildStateMachine1.");
            Assert.AreEqual(childAdvancedStateMachine2, hierarchy[2], "Third element should be ChildStateMachine2.");
        }

        [Test]
        public void IsCyclic_ShouldDetectMultipleCycles()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            IAdvancedState<string> stateC = CreateState("StateC");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);
            advancedStateMachine.Add(stateC);

            // Create cycles: A -> B -> C -> A and B -> A
            ConnectStates(stateA, stateB);
            ConnectStates(stateB, stateC);
            ConnectStates(stateC, stateA);
            ConnectStates(stateB, stateA);

            // Act
            bool isCyclic = advancedStateMachine.IsCyclic;

            // Assert
            Assert.IsTrue(isCyclic, "var should detect multiple cycles within the state connections.");
        }

        [Test]
        public void OnStateExit_ShouldBeCalledBeforeOnStateEnterDuringTransition()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("GoToB", ParameterType.Trigger);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("GoToB");

            List<string> eventOrder = new List<string>();

            ((State<string>)stateA).OnStateExit += (state) => eventOrder.Add("ExitA");
            ((State<string>)stateB).OnStateEnter += (state) => eventOrder.Add("EnterB");

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetTrigger("GoToB");

            // Assert
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState, "var should transition to StateB.");
            Assert.AreEqual(2, eventOrder.Count, "Two events should have been invoked: ExitA and EnterB.");
            Assert.AreEqual("ExitA", eventOrder[0],
                "OnStateExit of StateA should be invoked before OnStateEnter of StateB.");
            Assert.AreEqual("EnterB", eventOrder[1],
                "OnStateEnter of StateB should be invoked after OnStateExit of StateA.");
        }

        [Test]
        public void StateMachine_ShouldNotTransitionIfNotRunning()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Trigger", ParameterType.Trigger);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connection.AddCondition("Trigger");

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => advancedStateMachine.SetTrigger("Trigger"),
                "var should throw error if not running");
            Assert.IsFalse(advancedStateMachine.IsRunning, "var should not be running.");
            Assert.IsNull(advancedStateMachine.CurrentState, "CurrentState should be null.");
            Assert.IsFalse(onEnterBCalled, "OnStateEnter of StateB should not be invoked when var is not running.");
        }

        [Test]
        public void Start_ShouldInvokeOnlyOnStateEnterOnce()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> startState = CreateState("StartState");
            advancedStateMachine.Add(startState);

            int onEnterCallCount = 0;
            ((State<string>)startState).OnStateEnter += (state) => onEnterCallCount++;

            // Act
            advancedStateMachine.Start(startState);
            advancedStateMachine.Start(startState); // Attempt to start again

            // Assert
            Assert.IsTrue(advancedStateMachine.IsRunning, "var should be running after Start.");
            Assert.AreEqual(startState, advancedStateMachine.CurrentState, "CurrentState should be set to startState.");
            Assert.AreEqual(1, onEnterCallCount,
                "OnStateEnter should be invoked only once even if Start is called multiple times.");
        }

        [Test]
        public void Stop_ShouldInvokeOnlyOnStateExitOnce()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> startState = CreateState("StartState");
            advancedStateMachine.Add(startState);
            advancedStateMachine.Start(startState);

            int onExitCallCount = 0;
            ((State<string>)startState).OnStateExit += (state) => onExitCallCount++;

            // Act
            advancedStateMachine.Stop();
            advancedStateMachine.Stop(); // Attempt to stop again

            // Assert
            Assert.IsFalse(advancedStateMachine.IsRunning, "var should not be running after Stop.");
            Assert.IsNull(advancedStateMachine.CurrentState, "CurrentState should be null after stopping.");
            Assert.AreEqual(1, onExitCallCount,
                "OnStateExit should be invoked only once even if Stop is called multiple times.");
        }

        [Test]
        public void SetTriggerMultipleTimes_ShouldInvokeTransitionEachTime()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("GoToB", ParameterType.Trigger);
            advancedStateMachine.AddParameter("GoToA", ParameterType.Trigger);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Connect StateA to StateB with "GoToB" condition
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connectionAB = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connectionAB.AddCondition("GoToB");

            // Connect StateB to StateA with "GoToA" condition
            ConnectStates(stateB, stateA);
            AdvancedStateConnection<string> connectionBA = (AdvancedStateConnection<string>)stateB.OutboundConnections[0];
            connectionBA.AddCondition("GoToA");

            int onEnterBCalled = 0;
            int onEnterACalled = 0;

            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled++;
            ((State<string>)stateA).OnStateEnter += (state) => onEnterACalled++;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetTrigger("GoToB"); // Transition to B
            advancedStateMachine.SetTrigger("GoToA"); // Transition back to A
            advancedStateMachine.SetTrigger("GoToB"); // Transition to B again

            // Assert
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState, "var should be in StateB after the last transition.");
            Assert.AreEqual(2, onEnterBCalled, "OnStateEnter of StateB should be invoked twice.");
            Assert.AreEqual(2, onEnterACalled, "OnStateEnter of StateA should be invoked once.");
        }

        [Test]
        public void SettingMultipleParameters_ShouldCauseCorrectTransitions()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Health", ParameterType.Int);
            advancedStateMachine.AddParameter("IsAlive", ParameterType.Bool);
            advancedStateMachine.AddParameter("Speed", ParameterType.Float);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            IAdvancedState<string> stateC = CreateState("StateC");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);
            advancedStateMachine.Add(stateC);

            // Connect StateA to StateB with Health > 50
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connectionAB = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connectionAB.AddCondition("Health", IntParameterCondition.Greater, 50);

            // Connect StateA to StateC with Speed > 10
            ConnectStates(stateA, stateC);
            AdvancedStateConnection<string> connectionAC = (AdvancedStateConnection<string>)stateA.OutboundConnections[1];
            connectionAC.AddCondition("Speed", FloatParameterCondition.Greater, 10.0f);

            bool onEnterBCalled = false;
            bool onEnterCCalled = false;

            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;
            ((State<string>)stateC).OnStateEnter += (state) => onEnterCCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetInt("Health", 60);
            advancedStateMachine.SetFloat("Speed", 15.0f);

            // Assert
            // Since both conditions are met, the first valid transition (to StateB) should occur
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState, "var should transition to StateB when Health > 50.");
            Assert.IsTrue(onEnterBCalled, "OnStateEnter of StateB should be invoked.");
            Assert.IsFalse(onEnterCCalled,
                "OnStateEnter of StateC should not be invoked since StateB was the first valid transition.");
        }

        [Test]
        public void TransitionShouldNotOccurIfConditionFailsAfterParameterSet()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("Health", ParameterType.Int);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Connect StateA to StateB with Health > 50
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connectionAB = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connectionAB.AddCondition("Health", IntParameterCondition.Greater, 50);

            bool onEnterBCalled = false;
            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            advancedStateMachine.SetInt("Health", 60); // Should trigger transition to B
            advancedStateMachine.SetInt("Health", 40); // Health drops below condition

            // Assert
            Assert.AreEqual(stateB, advancedStateMachine.CurrentState,
                "var should remain in StateB after Health drops below condition.");
            Assert.IsTrue(onEnterBCalled, "OnStateEnter of StateB should have been invoked once.");
        }

        [Test]
        public void AnyState_ShouldNotOverrideExplicitTransitions()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            advancedStateMachine.AddParameter("TriggerA", ParameterType.Trigger);
            advancedStateMachine.AddParameter("TriggerAny", ParameterType.Trigger);

            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            IAdvancedState<string> stateC = CreateState("StateC");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);
            advancedStateMachine.Add(stateC);

            // Connect StateA to StateB with TriggerA
            ConnectStates(stateA, stateB);
            AdvancedStateConnection<string> connectionAB = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
            connectionAB.AddCondition("TriggerA");

            // Connect AnyState to StateC with TriggerAny
            ConnectStates(advancedStateMachine.AnyState, stateC);
            AdvancedStateConnection<string> connectionAnyC =
                (AdvancedStateConnection<string>)advancedStateMachine.AnyState.OutboundConnections[0];
            connectionAnyC.AddCondition("TriggerAny");

            bool onEnterBCalled = false;
            bool onEnterCCalled = false;

            ((State<string>)stateB).OnStateEnter += (state) => onEnterBCalled = true;
            ((State<string>)stateC).OnStateEnter += (state) => onEnterCCalled = true;

            advancedStateMachine.Start(stateA);

            // Act
            // Both TriggerA and TriggerAny are set
            advancedStateMachine.SetTrigger("TriggerA");
            advancedStateMachine.SetTrigger("TriggerAny");

            // Assert
            // Explicit transition to StateB should take precedence over AnyState transition to StateC
            Assert.AreEqual(stateC, advancedStateMachine.CurrentState, "var should transition to StateC");
            Assert.IsTrue(onEnterBCalled, "OnStateEnter of StateB should be invoked.");
            Assert.IsTrue(onEnterCCalled, "OnStateEnter of StateC should be invoked.");
        }

        [Test]
        public void AddGraph_ShouldMaintainParentReferenceInChildGraph()
        {
            // Arrange
            AdvancedStateMachine<string> parentAdvancedStateMachine = CreateSimpleStateMachine("ParentStateMachine");
            AdvancedStateMachine<string> childAdvancedStateMachine = CreateSimpleStateMachine("ChildStateMachine");

            // Act
            parentAdvancedStateMachine.AddGraph(childAdvancedStateMachine);

            // Assert
            Assert.AreEqual(parentAdvancedStateMachine, childAdvancedStateMachine.ParentGraph,
                "ChildStateMachine's ParentGraph should reference the ParentStateMachine.");
        }

        [Test]
        public void AddGraph_CanAddMultipleChildGraphs()
        {
            // Arrange
            AdvancedStateMachine<string> parentAdvancedStateMachine = CreateSimpleStateMachine("ParentStateMachine");
            AdvancedStateMachine<string> childAdvancedStateMachine1 = CreateSimpleStateMachine("ChildStateMachine1");
            AdvancedStateMachine<string> childAdvancedStateMachine2 = CreateSimpleStateMachine("ChildStateMachine2");

            // Act
            parentAdvancedStateMachine.AddGraph(childAdvancedStateMachine1);
            parentAdvancedStateMachine.AddGraph(childAdvancedStateMachine2);

            // Assert
            Assert.Contains(childAdvancedStateMachine1, parentAdvancedStateMachine.ChildGraphs,
                "ParentStateMachine should contain ChildStateMachine1.");
            Assert.Contains(childAdvancedStateMachine2, parentAdvancedStateMachine.ChildGraphs,
                "ParentStateMachine should contain ChildStateMachine2.");
            Assert.AreEqual(2, parentAdvancedStateMachine.ChildGraphs.Length,
                "ParentStateMachine should have two child graphs.");
        }

        [Test]
        public void AddGraph_ShouldThrowExceptionWhenAddingNullGraph()
        {
            // Arrange
            AdvancedStateMachine<string> parentAdvancedStateMachine = CreateSimpleStateMachine();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => parentAdvancedStateMachine.AddGraph(null),
                "Adding a null graph should throw ArgumentNullException.");
        }

        [Test]
        public void AllNodes_ShouldReturnOnlyDirectNodesExcludingAnyState()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            IAdvancedState<string> anyState = advancedStateMachine.AnyState;
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Act
            INode[] allNodes = advancedStateMachine.AllNodes;

            // Assert
            Assert.AreEqual(3, allNodes.Length, "AllNodes should include StateA, StateB, and AnyState.");
            Assert.Contains(stateA, allNodes, "AllNodes should contain StateA.");
            Assert.Contains(stateB, allNodes, "AllNodes should contain StateB.");
            Assert.Contains(anyState, allNodes, "AllNodes should contain AnyState.");
        }

        [Test]
        public void AddingConnectionWithNonExistentParameter_ShouldThrowException()
        {
            // Arrange
            AdvancedStateMachine<string> advancedStateMachine = CreateSimpleStateMachine();
            IAdvancedState<string> stateA = CreateState("StateA");
            IAdvancedState<string> stateB = CreateState("StateB");
            advancedStateMachine.Add(stateA);
            advancedStateMachine.Add(stateB);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
            {
                ConnectStates(stateA, stateB);
                AdvancedStateConnection<string> connection = (AdvancedStateConnection<string>)stateA.OutboundConnections[0];
                connection.AddCondition("NonExistentParam");
            }, "Adding a condition with a non-existent parameter should throw InvalidOperationException.");
        }
    }
}
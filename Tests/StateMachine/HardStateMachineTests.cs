using System;
using System.Collections.Generic;
using NUnit.Framework;
using LegendaryTools.StateMachineV2;

namespace LegendaryTools.StateMachineV2.Tests
{
    // Define a test enum with multiple states
    public enum TestStates
    {
        StateA,
        StateB,
        StateC,
        StateD,
        StateE
    }

    [TestFixture]
    public class HardStateMachineTests
    {
        private HardStateMachine<TestStates> stateMachine;
        private IHardState<TestStates> stateA;
        private IHardState<TestStates> stateB;
        private IHardState<TestStates> stateC;

        [SetUp]
        public void SetUp()
        {
            // Initialize the state machine without any transition restrictions
            stateMachine = new HardStateMachine<TestStates>();

            // Retrieve states from the state machine
            stateA = stateMachine.States[TestStates.StateA];
            stateB = stateMachine.States[TestStates.StateB];
            stateC = stateMachine.States[TestStates.StateC];
        }

        [Test]
        public void InitialState_IsNotRunning()
        {
            Assert.IsFalse(stateMachine.IsRunning, "State machine should not be running initially.");
            Assert.IsNull(stateMachine.CurrentState, "CurrentState should be null initially.");
        }

        [Test]
        public void Start_WithValidState_SetsCurrentStateAndIsRunning()
        {
            bool enterCalled = false;
            stateA.OnStateEnter += (state) => enterCalled = true;

            stateMachine.Start(stateA);

            Assert.IsTrue(stateMachine.IsRunning, "State machine should be running after Start.");
            Assert.AreEqual(stateA, stateMachine.CurrentState, "CurrentState should be set to the start state.");
            Assert.IsTrue(enterCalled, "OnStateEnter should be called when starting.");
        }

        [Test]
        public void Stop_WhenRunning_ClearsCurrentStateAndIsNotRunning()
        {
            bool exitCalled = false;
            stateA.OnStateExit += (state) => exitCalled = true;

            stateMachine.Start(stateA);
            stateMachine.Stop();

            Assert.IsFalse(stateMachine.IsRunning, "State machine should not be running after Stop.");
            Assert.IsNull(stateMachine.CurrentState, "CurrentState should be null after Stop.");
            Assert.IsTrue(exitCalled, "OnStateExit should be called when stopping.");
        }

        [Test]
        public void Stop_WhenNotRunning_DoesNothing()
        {
            // Attempt to stop when not running
            stateMachine.Stop();

            Assert.IsFalse(stateMachine.IsRunning, "State machine should remain not running after Stop when not running.");
            Assert.IsNull(stateMachine.CurrentState, "CurrentState should remain null after Stop when not running.");
        }

        [Test]
        public void Update_WhenRunning_CallsOnStateUpdate()
        {
            bool updateCalled = false;
            stateA.OnStateUpdate += (state) => updateCalled = true;

            stateMachine.Start(stateA);
            stateMachine.Update();

            Assert.IsTrue(updateCalled, "OnStateUpdate should be called when Update is invoked and state machine is running.");
        }

        [Test]
        public void Update_WhenNotRunning_DoesNotCallOnStateUpdate()
        {
            bool updateCalled = false;
            stateA.OnStateUpdate += (state) => updateCalled = true;

            stateMachine.Update();

            Assert.IsFalse(updateCalled, "OnStateUpdate should not be called when Update is invoked and state machine is not running.");
        }

        [Test]
        public void SetTrigger_WithValidTransition_ChangesState()
        {
            bool exitCalled = false;
            bool enterCalled = false;

            stateA.OnStateExit += (state) => exitCalled = true;
            stateB.OnStateEnter += (state) => enterCalled = true;

            stateMachine.Start(stateA);
            stateMachine.SetTrigger(TestStates.StateB);

            Assert.IsTrue(exitCalled, "OnStateExit should be called when transitioning out of a state.");
            Assert.IsTrue(enterCalled, "OnStateEnter should be called when transitioning into a new state.");
            Assert.AreEqual(stateB, stateMachine.CurrentState, "CurrentState should be updated to the new state after transition.");
        }

        [Test]
        public void Transitions_AllowedByAllowTransitionFunction()
        {
            // Allow only transitions from StateA to StateB
            stateMachine = new HardStateMachine<TestStates>((from, to) =>
                from == null || (from.Type == TestStates.StateA && to.Type == TestStates.StateB));

            stateA = stateMachine.States[TestStates.StateA];
            stateB = stateMachine.States[TestStates.StateB];
            stateC = stateMachine.States[TestStates.StateC];

            stateMachine.Start(stateA);
            stateMachine.SetTrigger(TestStates.StateB);

            Assert.AreEqual(stateB, stateMachine.CurrentState, "Transition from StateA to StateB should be allowed.");
            
            stateMachine.SetTrigger(TestStates.StateC);
            Assert.AreEqual(stateB, stateMachine.CurrentState, "Transition from StateB to StateC should be disallowed.");
        }

        [Test]
        public void Transitions_DisallowedByAllowTransitionFunction()
        {
            // Disallow all transitions
            stateMachine = new HardStateMachine<TestStates>((from, to) => false);

            stateA = stateMachine.States[TestStates.StateA];
            stateB = stateMachine.States[TestStates.StateB];

            stateMachine.Start(stateA);
            stateMachine.SetTrigger(TestStates.StateB);

            Assert.AreEqual(stateA, stateMachine.CurrentState, "Transition should be disallowed by allowTransition function.");
        }

        [Test]
        public void Start_WithIStateOverload_SetsCurrentState()
        {
            bool enterCalled = false;
            stateA.OnStateEnter += (state) => enterCalled = true;

            IState state = stateA;
            stateMachine.Start(state);

            Assert.IsTrue(stateMachine.IsRunning, "State machine should be running after Start with IState.");
            Assert.AreEqual(stateA, stateMachine.CurrentState, "CurrentState should be set to the start state when using IState overload.");
            Assert.IsTrue(enterCalled, "OnStateEnter should be called when starting with IState overload.");
        }

        [Test]
        public void OnStateEnter_CalledOnStart()
        {
            bool enterCalled = false;
            stateB.OnStateEnter += (state) => enterCalled = true;

            stateMachine.Start(stateB);

            Assert.IsTrue(enterCalled, "OnStateEnter should be called when starting the state machine.");
        }

        [Test]
        public void OnStateExit_CalledOnStop()
        {
            bool exitCalled = false;
            stateC.OnStateExit += (state) => exitCalled = true;

            stateMachine.Start(stateC);
            stateMachine.Stop();

            Assert.IsTrue(exitCalled, "OnStateExit should be called when stopping the state machine.");
        }

        [Test]
        public void MultipleTriggers_ProcessTransitionsCorrectly()
        {
            List<TestStates> enteredStates = new List<TestStates>();

            stateA.OnStateEnter += (state) => enteredStates.Add(TestStates.StateA);
            stateB.OnStateEnter += (state) => enteredStates.Add(TestStates.StateB);
            stateC.OnStateEnter += (state) => enteredStates.Add(TestStates.StateC);

            stateMachine.Start(stateA);
            stateMachine.SetTrigger(TestStates.StateB);
            stateMachine.SetTrigger(TestStates.StateC);

            CollectionAssert.AreEqual(
                new List<TestStates> { TestStates.StateA, TestStates.StateB, TestStates.StateC },
                enteredStates,
                "State machine should process multiple triggers and enter states in correct order."
            );
            Assert.AreEqual(stateC, stateMachine.CurrentState, "CurrentState should be the last triggered state.");
        }

        [Test]
        public void States_Dictionary_IsPopulatedCorrectly()
        {
            Assert.AreEqual(5, stateMachine.States.Count, "States dictionary should contain all enum states.");
            Assert.IsTrue(stateMachine.States.ContainsKey(TestStates.StateA), "States dictionary should contain StateA.");
            Assert.IsTrue(stateMachine.States.ContainsKey(TestStates.StateB), "States dictionary should contain StateB.");
            Assert.IsTrue(stateMachine.States.ContainsKey(TestStates.StateC), "States dictionary should contain StateC.");
            Assert.IsTrue(stateMachine.States.ContainsKey(TestStates.StateD), "States dictionary should contain StateD.");
            Assert.IsTrue(stateMachine.States.ContainsKey(TestStates.StateE), "States dictionary should contain StateE.");
        }

        [Test]
        public void Name_Property_ReturnsCorrectTypeName()
        {
            Assert.AreEqual(nameof(TestStates), stateMachine.Name, "Name property should return the correct type name.");
        }

        [Test]
        public void IStateMachine_Name_CanBeSetAndGet()
        {
            string customName = "CustomStateMachine";
            ((IStateMachine<TestStates>)stateMachine).Name = customName;

            Assert.AreEqual(customName, ((IStateMachine<TestStates>)stateMachine).Name, "IStateMachine.Name should be set and retrieved correctly.");
        }

        [Test]
        public void CurrentState_Property_ReturnsCorrectState()
        {
            stateMachine.Start(stateB);

            Assert.AreEqual(stateB, stateMachine.CurrentState, "CurrentState property should return the correct state.");
        }

        [Test]
        public void CurrentState_InIHardStateMachine_IsHardState()
        {
            stateMachine.Start(stateC);

            Assert.IsInstanceOf<IHardState<TestStates>>(stateMachine.CurrentState, "CurrentState should be of type IHardState<T>.");
        }

        [Test]
        public void Start_WhenAlreadyRunning_DoesNotChangeState()
        {
            stateMachine.Start(stateA);
            stateMachine.Start(stateB); // Attempt to start again with a different state

            Assert.AreEqual(stateA, stateMachine.CurrentState, "Starting the state machine when already running should not change the current state.");
        }

        [Test]
        public void Transit_WithNullFromState_CallsOnlyEnter()
        {
            bool enterCalled = false;
            bool exitCalled = false;

            stateB.OnStateEnter += (state) => enterCalled = true;
            // No exit for null state

            stateMachine.Start(stateB);

            Assert.IsTrue(enterCalled, "OnStateEnter should be called when transitioning from null to a state.");
            Assert.IsFalse(exitCalled, "OnStateExit should not be called when transitioning from null.");
        }

        [Test]
        public void Transit_WithNullToState_CallsOnlyExit()
        {
            bool enterCalled = false;
            bool exitCalled = false;

            stateA.OnStateExit += (state) => exitCalled = true;
            // No enter for null state

            stateMachine.Start(stateA);
            stateMachine.Stop();

            Assert.IsFalse(enterCalled, "OnStateEnter should not be called when transitioning to null.");
            Assert.IsTrue(exitCalled, "OnStateExit should be called when transitioning to null.");
        }
    }

    // Extension method to get enum value from string
    public static class EnumExtensions
    {
        public static T GetEnumValue<T>(this string enumName) where T : struct, Enum
        {
            if (Enum.TryParse<T>(enumName, out var value))
            {
                return value;
            }
            throw new ArgumentException($"'{enumName}' is not a valid value for enum '{typeof(T).Name}'.");
        }
    }
}

using System;
using System.Collections.Generic;
using LegendaryTools.GraphV2;

namespace LegendaryTools.StateMachineV2
{
    public class AdvancedStateConnection<T> : NodeConnection, IAdvancedStateConnection<T> where T : IEquatable<T>
    {
        public string Name { get; set; }

        private int priority;
        int IAdvancedStateConnection<T>.Priority
        {
            get => priority;
            set => priority = value;
        }
        
        private ConditionOperation conditionOperation;
        ConditionOperation IAdvancedStateConnection<T>.ConditionOperation
        {
            get => conditionOperation;
            set => conditionOperation = value;
        }

        public event Action OnTransit;
        public List<Condition<T>> Conditions { get; protected set; } = new List<Condition<T>>();

        public AdvancedStateConnection(INode fromNode, INode toNode, int priority, NodeConnectionDirection direction,
            ConditionOperation conditionOperation = ConditionOperation.WhenAll) : base(fromNode, toNode, direction)
        {
            this.priority = priority;
            this.conditionOperation = conditionOperation;
        }

        public void AddCondition(T name, FloatParameterCondition parameterCondition, float value)
        {
            ValidateParam(name, ParameterType.Float);
            Conditions.Add(new FloatCondition<T>(name, parameterCondition, value));
        }
        
        public void AddCondition(T name, IntParameterCondition parameterCondition, int value)
        {
            ValidateParam(name, ParameterType.Int);
            Conditions.Add(new IntCondition<T>(name, parameterCondition, value));
        }

        public void AddCondition(T name, BoolParameterCondition parameterCondition)
        {
            ValidateParam(name, ParameterType.Bool);
            Conditions.Add(new BoolCondition<T>(name, parameterCondition));
        }
        
        public void AddCondition(T name)
        {
            ValidateParam(name, ParameterType.Trigger);
            Conditions.Add(new TriggerCondition<T>(name));
        }

        private void ValidateParam(T name, ParameterType expectedDefinition)
        {
            IGraph rootGraph = FromNode.Owner.GraphHierarchy.Length == 0 ? FromNode.Owner : FromNode.Owner.GraphHierarchy[0];
            if(rootGraph is not IAdvancedStateMachine<T> rootStateMachine) 
                throw new InvalidOperationException($"Root {nameof(AdvancedStateMachine<T>)} does not implements {nameof(IAdvancedStateMachine<T>)}.");
            AdvancedStateMachine<T>.ValidateParam(name, rootStateMachine, expectedDefinition, out ParameterState<T> parameterState);
        }

        public void RemoveCondition(Predicate<Condition<T>> predicate)
        {
            Conditions.RemoveAll(predicate);
        }

        public bool Evaluate(Dictionary<T, ParameterState<T>> parametersState)
        {
            switch (conditionOperation)
            {
                case ConditionOperation.WhenAll:
                {
                    foreach (Condition<T> condition in Conditions)
                    {
                        if (!parametersState.TryGetValue(condition.Name, out ParameterState<T> cParameterState))
                        {
                            throw new InvalidOperationException($"You are trying to test a condition called {condition.Name} that has no parameter in the {nameof(AdvancedStateMachine<T>)}.");
                        }
                        if (!condition.Evaluate(condition.Name, cParameterState)) return false;
                    }
                    return true;
                }
                case ConditionOperation.WhenAny:
                {
                    foreach (Condition<T> condition in Conditions)
                    {
                        if (!parametersState.TryGetValue(condition.Name, out ParameterState<T> cParameterState))
                        {
                            throw new InvalidOperationException($"You are trying to test a condition called {condition.Name} that has no parameter in the {nameof(AdvancedStateMachine<T>)}.");
                        }
                        if (condition.Evaluate(condition.Name, cParameterState)) return true;
                    }
                    return false;
                }
            }

            return false;
        }

        public void ConsumeTriggers(Dictionary<T, ParameterState<T>> parametersState)
        {
            foreach (Condition<T> condition in Conditions)
            {
                if (condition.Type == ParameterType.Trigger) parametersState[condition.Name].Value = 0;
            }
        }

        public virtual void OnTransited()
        {

        }
        
        void IAdvancedStateConnection<T>.InvokeOnTransit()
        {
            OnTransited();
            OnTransit?.Invoke();
        }

        public int CompareTo(IAdvancedStateConnection<T> other)
        {
            return priority.CompareTo(other.Priority);
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class EventBindingCondition
    {
        [SerializeField] private string name = "Condition";
        [SerializeField] private bool enabled = true;
        [SerializeField] private List<EventBindingConditionClause> clauses =
            new List<EventBindingConditionClause> { new EventBindingConditionClause() };
        [SerializeField] private List<EventBindingAction> actions = new List<EventBindingAction>();

        public string Name => name;

        public bool Enabled => enabled;

        public IReadOnlyList<EventBindingConditionClause> Clauses => clauses;

        public IReadOnlyList<EventBindingAction> Actions => actions;

        public bool ObservesSource(int sourceIndex)
        {
            if (clauses == null)
            {
                return false;
            }

            for (int i = 0; i < clauses.Count; i++)
            {
                EventBindingConditionClause clause = clauses[i];
                if (clause != null && clause.SourceIndex == sourceIndex)
                {
                    return true;
                }
            }

            return false;
        }

        public void InvokeActions(object oldValue, object newValue)
        {
            if (actions == null)
            {
                return;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                actions[i]?.Invoke(oldValue, newValue);
            }
        }
    }
}

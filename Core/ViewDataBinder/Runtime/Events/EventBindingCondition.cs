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

        public string Name
        {
            get => name;
            set => name = value ?? string.Empty;
        }

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public IReadOnlyList<EventBindingConditionClause> Clauses => clauses;

        public IReadOnlyList<EventBindingAction> Actions => actions;

        public int AddClause(EventBindingConditionClause clause)
        {
            clauses.Add(clause ?? throw new ArgumentNullException(nameof(clause)));
            return clauses.Count - 1;
        }

        public bool RemoveClauseAt(int clauseIndex)
        {
            if (clauseIndex < 0 || clauseIndex >= clauses.Count)
            {
                return false;
            }

            clauses.RemoveAt(clauseIndex);
            return true;
        }

        public int AddAction(EventBindingAction action)
        {
            actions.Add(action ?? throw new ArgumentNullException(nameof(action)));
            return actions.Count - 1;
        }

        public bool RemoveActionAt(int actionIndex)
        {
            if (actionIndex < 0 || actionIndex >= actions.Count)
            {
                return false;
            }

            actions[actionIndex]?.ReleaseRuntimeResources();
            actions.RemoveAt(actionIndex);
            return true;
        }

        public void ClearClauses()
        {
            clauses.Clear();
        }

        public void ClearActions()
        {
            for (int i = 0; i < actions.Count; i++)
            {
                actions[i]?.ReleaseRuntimeResources();
            }

            actions.Clear();
        }

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

        public bool TryInvokeActions(
            object oldValue,
            object newValue,
            out bool taskRunning,
            out string error)
        {
            taskRunning = false;
            error = string.Empty;

            if (actions == null)
            {
                return true;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                EventBindingAction action = actions[i];
                if (action == null)
                {
                    continue;
                }

                if (!action.TryInvoke(oldValue, newValue, out bool actionTaskRunning, out error))
                {
                    error = $"Action {i + 1}: {error}";
                    return false;
                }

                taskRunning |= actionTaskRunning;
            }

            return true;
        }

        public bool TryObserveTasks(out bool taskRunning, out string error)
        {
            taskRunning = false;
            string firstError = null;

            if (actions == null)
            {
                error = string.Empty;
                return true;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                EventBindingAction action = actions[i];
                if (action == null)
                {
                    continue;
                }

                bool actionSucceeded = action.TryObserveTask(
                    out bool actionTaskRunning,
                    out string actionError);
                taskRunning |= actionTaskRunning;
                if (!actionSucceeded && firstError == null)
                {
                    firstError = $"Action {i + 1}: {actionError}";
                }
            }

            error = firstError ?? string.Empty;
            return firstError == null;
        }


        internal void InvalidateRuntimeCaches()
        {
            if (clauses == null)
            {
                return;
            }

            for (int i = 0; i < clauses.Count; i++)
            {
                clauses[i]?.InvalidateRuntimeCache();
            }
        }

        public void ReleaseRuntimeResources()
        {
            if (actions != null)
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    actions[i]?.ReleaseRuntimeResources();
                }
            }

            InvalidateRuntimeCaches();
        }

        public void ResetRuntimeState()
        {
            if (actions == null)
            {
                return;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                actions[i]?.ResetRuntimeState();
            }
        }
    }
}

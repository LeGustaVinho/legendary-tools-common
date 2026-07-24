using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class ViewDataEventCondition
    {
        [SerializeField] private string name = "Condition";
        [SerializeField] private bool enabled = true;
        [SerializeField] private BindingConditionOperator conditionOperator = BindingConditionOperator.Equal;
        [SerializeField] private BindingConditionValue comparisonValue = new BindingConditionValue();
        [SerializeField] private List<ViewDataEventAction> actions = new List<ViewDataEventAction>();

        public string Name => name;

        public bool Enabled => enabled;

        public BindingConditionOperator Operator => conditionOperator;

        public BindingConditionValue ComparisonValue => comparisonValue;

        public IReadOnlyList<ViewDataEventAction> Actions => actions;
    }
}

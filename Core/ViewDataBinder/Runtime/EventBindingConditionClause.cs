using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class EventBindingConditionClause
    {
        [SerializeField] private int sourceIndex;
        [SerializeField] private EventBindingLogicalOperator logicalOperator = EventBindingLogicalOperator.And;
        [SerializeField] private bool negate;
        [SerializeField] private EventBindingComparisonOperator comparisonOperator = EventBindingComparisonOperator.Equal;
        [SerializeField] private BindingFallbackValue comparisonValue = new BindingFallbackValue();

        public int SourceIndex => sourceIndex;

        public EventBindingLogicalOperator LogicalOperator => logicalOperator;

        public bool Negate => negate;

        public EventBindingComparisonOperator ComparisonOperator => comparisonOperator;

        public BindingFallbackValue ComparisonValue => comparisonValue;
    }
}

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

        [NonSerialized] private Type cachedComparisonType;
        [NonSerialized] private object cachedComparisonValue;
        [NonSerialized] private string cachedComparisonError;
        [NonSerialized] private bool comparisonValueCached;

        public int SourceIndex
        {
            get => sourceIndex;
            set => sourceIndex = Math.Max(0, value);
        }

        public EventBindingLogicalOperator LogicalOperator
        {
            get => logicalOperator;
            set => logicalOperator = value;
        }

        public bool Negate
        {
            get => negate;
            set => negate = value;
        }

        public EventBindingComparisonOperator ComparisonOperator
        {
            get => comparisonOperator;
            set => comparisonOperator = value;
        }

        public BindingFallbackValue ComparisonValue => comparisonValue;

        internal bool TryGetComparisonValue(Type valueType, out object value, out string error)
        {
            if (comparisonValueCached && cachedComparisonType == valueType)
            {
                value = cachedComparisonValue;
                error = cachedComparisonError;
                return string.IsNullOrEmpty(error);
            }

            cachedComparisonType = valueType;
            comparisonValueCached = true;
            if (comparisonValue == null)
            {
                cachedComparisonValue = null;
                cachedComparisonError = "Comparison value is null.";
                value = null;
                error = cachedComparisonError;
                return false;
            }

            bool success = comparisonValue.TryGetValue(
                valueType,
                out cachedComparisonValue,
                out cachedComparisonError);
            value = cachedComparisonValue;
            error = cachedComparisonError;
            return success;
        }

        internal void InvalidateRuntimeCache()
        {
            cachedComparisonType = null;
            cachedComparisonValue = null;
            cachedComparisonError = null;
            comparisonValueCached = false;
        }
    }
}

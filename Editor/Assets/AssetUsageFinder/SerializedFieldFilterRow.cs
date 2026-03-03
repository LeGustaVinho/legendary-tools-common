using System;

namespace LegendaryTools.Editor
{
    [Serializable]
    public sealed class SerializedFieldFilterRow
    {
        public bool Expanded = true;

        public LogicalOperator JoinWithPrevious = LogicalOperator.And; // Ignored for first row.
        public string TypeQuery = string.Empty;

        // For scalar: ValueType is the value type used in comparison.
        // For collection: ValueType is the ELEMENT type (UX only).
        public Type ValueType;

        public CollectionKind Collection = CollectionKind.None;

        // Comparison is user-selectable for scalar, forced to Contains for collections.
        public FieldComparison Comparison = FieldComparison.Equals;

        public SerializedFieldValueBox Value = new();

        public void EnsureDefaults()
        {
            if (ValueType == null)
                ValueType = typeof(string);

            if (Value == null)
                Value = new SerializedFieldValueBox();
        }

        public bool IsCollection => Collection != CollectionKind.None;

        public FieldComparison EffectiveComparison => IsCollection ? FieldComparison.Contains : Comparison;

        public Type EffectiveValueType => ValueType ?? typeof(string);
    }
}
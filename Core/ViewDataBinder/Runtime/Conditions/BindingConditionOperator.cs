using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    public enum BindingConditionOperator
    {
        [InspectorName("== Equal")]
        Equal = 0,

        [InspectorName("!= Not Equal")]
        NotEqual = 1,

        [InspectorName("> Greater Than")]
        GreaterThan = 2,

        [InspectorName(">= Greater Than Or Equal")]
        GreaterThanOrEqual = 3,

        [InspectorName("< Less Than")]
        LessThan = 4,

        [InspectorName("<= Less Than Or Equal")]
        LessThanOrEqual = 5,

        [InspectorName("&& Logical AND")]
        LogicalAnd = 6,

        [InspectorName("|| Logical OR")]
        LogicalOr = 7,

        [InspectorName("! Logical NOT")]
        LogicalNot = 8,

        [InspectorName("& Boolean AND")]
        BooleanAnd = 9,

        [InspectorName("| Boolean OR")]
        BooleanOr = 10,

        [InspectorName("^ Boolean XOR")]
        BooleanXor = 11,

        [InspectorName("is null")]
        IsNull = 12,

        [InspectorName("is not null")]
        IsNotNull = 13,

        [InspectorName("is true")]
        IsTrue = 14,

        [InspectorName("is false")]
        IsFalse = 15
    }
}

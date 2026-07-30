using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [CreateAssetMenu(
        fileName = "BooleanNegationBindingConverter",
        menuName = "Legendary Tools/View Binding/Converters/Boolean Negation")]
    public sealed class BooleanNegationBindingConverter : BindingConverter<bool, bool>
    {
        public override bool SupportsReverseConversion => true;

        protected override bool TryConvertValue(bool sourceValue, out bool targetValue, out string error)
        {
            targetValue = !sourceValue;
            error = string.Empty;
            return true;
        }

        protected override bool TryConvertBackValue(bool targetValue, out bool sourceValue, out string error)
        {
            sourceValue = !targetValue;
            error = string.Empty;
            return true;
        }
    }
}

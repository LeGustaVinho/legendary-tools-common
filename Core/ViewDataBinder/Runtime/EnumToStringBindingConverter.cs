using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [CreateAssetMenu(
        fileName = "EnumToStringBindingConverter",
        menuName = "Legendary Tools/View Binding/Converters/Enum To String")]
    public sealed class EnumToStringBindingConverter : BindingConverter
    {
        [SerializeField] private string format = "G";

        public override Type SourceType => typeof(Enum);

        public override Type TargetType => typeof(string);

        public override bool CanConvert(Type sourceType, Type targetType)
        {
            return sourceType != null && sourceType.IsEnum && targetType == typeof(string);
        }

        public override bool TryConvert(object sourceValue, out object targetValue, out string error)
        {
            if (!(sourceValue is Enum enumValue))
            {
                targetValue = null;
                error = "The Source value is not an enum.";
                return false;
            }

            try
            {
                targetValue = enumValue.ToString(format);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                targetValue = null;
                error = $"Enum formatting failed: {exception.Message}";
                return false;
            }
        }
    }
}

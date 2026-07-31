using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [CreateAssetMenu(
        fileName = "ToStringBindingConverter",
        menuName = "Legendary Tools/View Data Binder/Converters/To String")]
    public sealed class ToStringBindingConverter : BindingConverter
    {
        public override Type SourceType => typeof(object);

        public override Type TargetType => typeof(string);

        public override bool CanConvert(Type sourceType, Type targetType)
        {
            return sourceType != null && targetType == typeof(string);
        }

        public override bool TryConvert(object sourceValue, out object targetValue, out string error)
        {
            if (sourceValue == null)
            {
                targetValue = null;
                error = string.Empty;
                return true;
            }

            try
            {
                targetValue = sourceValue.ToString();
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                targetValue = null;
                error = $"ToString conversion failed: {exception.Message}";
                return false;
            }
        }
    }
}

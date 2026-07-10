using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    public abstract class BindingConverter : ScriptableObject
    {
        public abstract Type SourceType { get; }

        public abstract Type TargetType { get; }

        public virtual bool SupportsReverseConversion => false;

        public virtual bool CanConvert(Type sourceType, Type targetType)
        {
            return sourceType == SourceType && targetType == TargetType;
        }

        public virtual bool CanConvertBack(Type targetType, Type sourceType)
        {
            return SupportsReverseConversion &&
                   targetType == TargetType &&
                   sourceType == SourceType;
        }

        public abstract bool TryConvert(object sourceValue, out object targetValue, out string error);

        public virtual bool TryConvertBack(object targetValue, out object sourceValue, out string error)
        {
            sourceValue = null;
            error = $"Converter '{name}' does not support reverse conversion.";
            return false;
        }
    }
}

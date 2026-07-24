using System;

namespace LegendaryTools.ViewBinding
{
    public abstract class BindingConverter<TSource, TTarget> : BindingConverter
    {
        public override Type SourceType => typeof(TSource);

        public override Type TargetType => typeof(TTarget);

        public sealed override bool TryConvert(object sourceValue, out object targetValue, out string error)
        {
            if (!TryCastValue(sourceValue, out TSource typedSource))
            {
                targetValue = null;
                error = $"Converter '{name}' expected '{typeof(TSource).FullName}' but received '{GetValueTypeName(sourceValue)}'.";
                return false;
            }

            if (!TryConvertValue(typedSource, out TTarget typedTarget, out error))
            {
                targetValue = null;
                return false;
            }

            targetValue = typedTarget;
            return true;
        }

        public sealed override bool TryConvertBack(object targetValue, out object sourceValue, out string error)
        {
            if (!SupportsReverseConversion)
            {
                return base.TryConvertBack(targetValue, out sourceValue, out error);
            }

            if (!TryCastValue(targetValue, out TTarget typedTarget))
            {
                sourceValue = null;
                error = $"Converter '{name}' expected reverse input '{typeof(TTarget).FullName}' but received '{GetValueTypeName(targetValue)}'.";
                return false;
            }

            if (!TryConvertBackValue(typedTarget, out TSource typedSource, out error))
            {
                sourceValue = null;
                return false;
            }

            sourceValue = typedSource;
            return true;
        }

        protected abstract bool TryConvertValue(TSource sourceValue, out TTarget targetValue, out string error);

        protected virtual bool TryConvertBackValue(TTarget targetValue, out TSource sourceValue, out string error)
        {
            sourceValue = default;
            error = $"Converter '{name}' does not support reverse conversion.";
            return false;
        }

        private static bool TryCastValue<TValue>(object value, out TValue typedValue)
        {
            if (value is TValue matchedValue)
            {
                typedValue = matchedValue;
                return true;
            }

            if (value == null && ReferenceEquals(default(TValue), null))
            {
                typedValue = default;
                return true;
            }

            typedValue = default;
            return false;
        }

        private static string GetValueTypeName(object value)
        {
            return value == null ? "null" : value.GetType().FullName;
        }
    }
}

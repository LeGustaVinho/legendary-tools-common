using System;
using System.Globalization;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [CreateAssetMenu(
        fileName = "NumericBindingConverter",
        menuName = "Legendary Tools/View Binding/Converters/Numeric Type")]
    public sealed class NumericBindingConverter : BindingConverter
    {
        [SerializeField] private BindingNumericType sourceType = BindingNumericType.Single;
        [SerializeField] private BindingNumericType targetType = BindingNumericType.Int32;

        public override Type SourceType => ResolveType(sourceType);

        public override Type TargetType => ResolveType(targetType);

        public override bool SupportsReverseConversion => true;

        public override bool TryConvert(object sourceValue, out object targetValue, out string error)
        {
            return TryConvertNumeric(sourceValue, TargetType, out targetValue, out error);
        }

        public override bool TryConvertBack(object targetValue, out object sourceValue, out string error)
        {
            return TryConvertNumeric(targetValue, SourceType, out sourceValue, out error);
        }

        private static bool TryConvertNumeric(
            object value,
            Type destinationType,
            out object convertedValue,
            out string error)
        {
            if (value == null)
            {
                convertedValue = null;
                error = "Numeric conversion does not accept null values.";
                return false;
            }

            try
            {
                convertedValue = Convert.ChangeType(value, destinationType, CultureInfo.InvariantCulture);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                convertedValue = null;
                error = $"Numeric conversion to '{destinationType.Name}' failed: {exception.Message}";
                return false;
            }
        }

        private static Type ResolveType(BindingNumericType numericType)
        {
            switch (numericType)
            {
                case BindingNumericType.Byte:
                    return typeof(byte);
                case BindingNumericType.SByte:
                    return typeof(sbyte);
                case BindingNumericType.Int16:
                    return typeof(short);
                case BindingNumericType.UInt16:
                    return typeof(ushort);
                case BindingNumericType.Int32:
                    return typeof(int);
                case BindingNumericType.UInt32:
                    return typeof(uint);
                case BindingNumericType.Int64:
                    return typeof(long);
                case BindingNumericType.UInt64:
                    return typeof(ulong);
                case BindingNumericType.Single:
                    return typeof(float);
                case BindingNumericType.Double:
                    return typeof(double);
                case BindingNumericType.Decimal:
                    return typeof(decimal);
                default:
                    throw new ArgumentOutOfRangeException(nameof(numericType), numericType, null);
            }
        }
    }
}

using System;
using System.Globalization;

namespace FlatData
{
    public sealed class DefaultValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            Type nullableType = Nullable.GetUnderlyingType(targetType);
            Type effectiveType = nullableType ?? targetType;

            if (value == null)
            {
                if (!targetType.IsValueType || nullableType != null)
                {
                    return null;
                }

                return Activator.CreateInstance(targetType);
            }

            Type valueType = value.GetType();
            if (effectiveType.IsAssignableFrom(valueType))
            {
                return value;
            }

            if (effectiveType.IsEnum)
            {
                if (value is string)
                {
                    return Enum.Parse(effectiveType, (string)value, true);
                }

                object numericValue = System.Convert.ChangeType(
                    value,
                    Enum.GetUnderlyingType(effectiveType),
                    CultureInfo.InvariantCulture);

                return Enum.ToObject(effectiveType, numericValue);
            }

            if (effectiveType == typeof(Guid))
            {
                return value is Guid ? value : Guid.Parse(value.ToString());
            }

            if (effectiveType == typeof(DateTime))
            {
                if (value is DateTime)
                {
                    return value;
                }

                return DateTime.Parse(
                    value.ToString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
            }

            if (effectiveType == typeof(DateTimeOffset))
            {
                if (value is DateTimeOffset)
                {
                    return value;
                }

                return DateTimeOffset.Parse(
                    value.ToString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
            }

            if (effectiveType == typeof(TimeSpan))
            {
                return value is TimeSpan ? value : TimeSpan.Parse(
                    value.ToString(),
                    CultureInfo.InvariantCulture);
            }

            if (effectiveType == typeof(string))
            {
                return value.ToString();
            }

            return System.Convert.ChangeType(
                value,
                effectiveType,
                CultureInfo.InvariantCulture);
        }
    }
}

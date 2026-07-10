using System;
using System.Globalization;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public class BindingSerializedValue
    {
        [SerializeField] private string stringValue;
        [SerializeField] private bool boolValue;
        [SerializeField] private int intValue;
        [SerializeField] private long longValue;
        [SerializeField] private float floatValue;
        [SerializeField] private double doubleValue;
        [SerializeField] private Vector2 vector2Value;
        [SerializeField] private Vector3 vector3Value;
        [SerializeField] private Vector4 vector4Value;
        [SerializeField] private Color colorValue = Color.white;
        [SerializeField] private Quaternion quaternionValue = Quaternion.identity;
        [SerializeField] private UnityEngine.Object objectValue;
        [SerializeField] private string serializedValue;

        public bool TryGetValue(Type valueType, out object value, out string error)
        {
            value = null;

            if (valueType == null)
            {
                error = "Value type is unknown.";
                return false;
            }

            Type nullableType = Nullable.GetUnderlyingType(valueType);
            Type effectiveType = nullableType ?? valueType;

            try
            {
                if (effectiveType == typeof(string))
                {
                    value = stringValue;
                }
                else if (effectiveType == typeof(bool))
                {
                    value = boolValue;
                }
                else if (effectiveType == typeof(int))
                {
                    value = intValue;
                }
                else if (effectiveType == typeof(long))
                {
                    value = longValue;
                }
                else if (effectiveType == typeof(float))
                {
                    value = floatValue;
                }
                else if (effectiveType == typeof(double))
                {
                    value = doubleValue;
                }
                else if (effectiveType == typeof(Vector2))
                {
                    value = vector2Value;
                }
                else if (effectiveType == typeof(Vector3))
                {
                    value = vector3Value;
                }
                else if (effectiveType == typeof(Vector4))
                {
                    value = vector4Value;
                }
                else if (effectiveType == typeof(Color))
                {
                    value = colorValue;
                }
                else if (effectiveType == typeof(Quaternion))
                {
                    value = quaternionValue;
                }
                else if (typeof(UnityEngine.Object).IsAssignableFrom(effectiveType))
                {
                    if (objectValue != null && !effectiveType.IsInstanceOfType(objectValue))
                    {
                        error = $"Object '{objectValue.GetType().FullName}' is not assignable to '{effectiveType.FullName}'.";
                        return false;
                    }

                    value = objectValue;
                }
                else if (effectiveType.IsEnum)
                {
                    value = Enum.Parse(effectiveType, serializedValue, true);
                }
                else if (effectiveType == typeof(byte))
                {
                    value = byte.Parse(serializedValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
                else if (effectiveType == typeof(sbyte))
                {
                    value = sbyte.Parse(serializedValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
                else if (effectiveType == typeof(short))
                {
                    value = short.Parse(serializedValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
                else if (effectiveType == typeof(ushort))
                {
                    value = ushort.Parse(serializedValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
                else if (effectiveType == typeof(uint))
                {
                    value = uint.Parse(serializedValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
                else if (effectiveType == typeof(ulong))
                {
                    value = ulong.Parse(serializedValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
                else if (effectiveType == typeof(decimal))
                {
                    value = decimal.Parse(serializedValue, NumberStyles.Number, CultureInfo.InvariantCulture);
                }
                else if (effectiveType == typeof(char))
                {
                    if (string.IsNullOrEmpty(serializedValue) || serializedValue.Length != 1)
                    {
                        error = "A char value must contain exactly one character.";
                        return false;
                    }

                    value = serializedValue[0];
                }
                else
                {
                    value = JsonUtility.FromJson(serializedValue, effectiveType);
                }

                if (nullableType != null && value != null)
                {
                    value = Activator.CreateInstance(valueType, value);
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Value could not be created for '{valueType.FullName}': {exception.Message}";
                return false;
            }
        }
    }
}

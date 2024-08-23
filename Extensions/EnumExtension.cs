using System;

namespace LegendaryTools
{
    public static class EnumExtension
    {
        public static  T GetEnumValue<T>(this string str) where T : struct, Enum, IConvertible
        {
            Type enumType = typeof(T);
            if (!enumType.IsEnum)
            {
                throw new Exception("T must be an Enumeration type.");
            }
            return Enum.TryParse(str, true, out T val) ? val : default;
        }

        public static T GetEnumValue<T>(this int intValue) where T : struct, Enum, IConvertible
        {
            Type enumType = typeof(T);
            if (!enumType.IsEnum)
            {
                throw new Exception("T must be an Enumeration type.");
            }
        
            return (T)Enum.ToObject(enumType, intValue);
        }
    }
}
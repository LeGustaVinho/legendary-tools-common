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
        
        public static bool IsValid<T>(this T enumValue) where T : struct, Enum, IConvertible
        {
            return Enum.IsDefined(typeof(T), enumValue);
        }
        
        public static bool HasFlags<T>(this T enumValue, T flag) where T : struct, Enum, IConvertible
        {
            return FlagUtil.Has(enumValue, flag);
        }
        
        public static T AddFlags<T>(this T enumValue, T flag) where T : struct, Enum, IConvertible
        {
            return FlagUtil.Add(enumValue, flag);
        }
        
        public static T RemoveFlags<T>(this T enumValue, T flag) where T : struct, Enum, IConvertible
        {
            return FlagUtil.Remove(enumValue, flag);
        }
        
        public static bool IsFlag<T>(this T enumValue, T flag) where T : struct, Enum, IConvertible
        {
            return FlagUtil.Is(enumValue, flag);
        }
    }
}
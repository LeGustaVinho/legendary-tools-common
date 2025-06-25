using System;
using System.Collections.Generic;

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
        
        /// <summary>
        /// Returns all possible values of the enum type.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="enumValue">An instance of the enum (ignored, used for type inference).</param>
        /// <returns>IEnumerable of all enum values of the specified type.</returns>
        /// <exception cref="ArgumentException">Thrown if T is not an Enum type.</exception>
        public static IEnumerable<T> GetAllValues<T>(this T enumValue) where T : Enum
        {
            return (T[])Enum.GetValues(typeof(T));
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
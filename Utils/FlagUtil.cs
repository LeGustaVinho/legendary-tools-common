using System;

namespace LegendaryTools
{
    public static class FlagUtil
    {
        public static bool Has<T>(T enumValue, T flag) where T : struct, Enum, IConvertible
        {
            return Has(Convert.ToInt64(enumValue), Convert.ToInt64(flag));
        }
        
        public static T Add<T>(T enumValue, T flag) where T : struct, Enum, IConvertible
        {
            return (T)Enum.ToObject(typeof(T), Add(Convert.ToInt64(enumValue), Convert.ToInt64(flag)));
        }
        
        public static T Remove<T>(T enumValue, T flag) where T : struct, Enum, IConvertible
        {
            return (T)Enum.ToObject(typeof(T), Remove(Convert.ToInt64(enumValue), Convert.ToInt64(flag)));
        }
        
        public static bool Is<T>(T enumValue, T flag) where T : struct, Enum, IConvertible
        {
            return enumValue.Equals(flag);
        }
        
        //===== 32 bits

        public static bool Has(int lhFlag, int rhFlag)
        {
            return (lhFlag & rhFlag) != 0;
        }

        public static bool Is(int lhFlag, int rhFlag)
        {
            return lhFlag == rhFlag;
        }

        public static int Add(int lhFlag, int rhFlag)
        {
            return lhFlag | rhFlag;
        }

        public static int Remove(int lhFlag, int rhFlag)
        {
            return lhFlag & ~rhFlag;
        }

        //===== 64 bits

        public static bool Has(long lhFlag, long rhFlag)
        {
            return (lhFlag & rhFlag) != 0;
        }

        public static bool Is(long lhFlag, long rhFlag)
        {
            return lhFlag == rhFlag;
        }

        public static long Add(long lhFlag, long rhFlag)
        {
            return lhFlag | rhFlag;
        }

        public static long Remove(long lhFlag, long rhFlag)
        {
            return lhFlag & ~rhFlag;
        }

        //===== Float as 64 bits

        public static bool Has(float lhFlag, float rhFlag)
        {
            return ((long) lhFlag & (long) rhFlag) != 0;
        }

        public static bool Is(float lhFlag, float rhFlag)
        {
            return lhFlag == rhFlag;
        }

        public static long Add(float lhFlag, float rhFlag)
        {
            return (long) lhFlag | (long) rhFlag;
        }

        public static long Remove(float lhFlag, float rhFlag)
        {
            return (long) lhFlag & ~(long) rhFlag;
        }
    }
}
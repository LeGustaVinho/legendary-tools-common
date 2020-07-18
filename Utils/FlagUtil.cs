namespace LegendaryTools
{
    public static class FlagUtil
    {
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
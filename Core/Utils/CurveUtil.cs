namespace LegendaryTools
{
    public static class CurveUtil
    {
        public static float Spline(float a, float b, float c, float d, float u)
        {
            return .5f * (
                       (-a + 3f * b - 3f * c + d) * (u * u * u)
                       + (2f * a - 5f * b + 4f * c - d) * (u * u)
                       + (-a + c) * u
                       + 2f * b
                   );
        }
    }
}
using UnityEngine;

namespace LegendaryTools
{
    public static class ColorUtil
    {
        public static Color SplineInterp(Color[] p, float x)
        {
            int numSections = p.Length - 3;
            int currPt = Mathf.Min(Mathf.FloorToInt(x * numSections), numSections - 1);
            float u = x * numSections - currPt;

            return new Color(CurveUtil.Spline(p[currPt].r, p[currPt + 1].r, p[currPt + 2].r, p[currPt + 3].r, u),
                CurveUtil.Spline(p[currPt].g, p[currPt + 1].g, p[currPt + 2].g, p[currPt + 3].g, u),
                CurveUtil.Spline(p[currPt].b, p[currPt + 1].b, p[currPt + 2].b, p[currPt + 3].b, u));
        }

        public static Color BiSplineInterpolation(Color[][] points, float timeX, float timeY)
        {
            Color[] aux = new Color[points.Length];

            for (int i = 0; i < points.Length; i++)
            {
                aux[i] = SplineInterp(points[i], timeX);
            }

            return SplineInterp(aux, timeY);
        }

        public static Color BilinearColor(Color topLeft, Color topRight, Color bottomLeft, Color bottomRight, float x,
            float y)
        {
            return Color.Lerp(Color.Lerp(topLeft, topRight, Mathf.Clamp01(x)),
                Color.Lerp(bottomLeft, bottomRight, Mathf.Clamp01(x)), Mathf.Clamp01(y));
        }
    }
}
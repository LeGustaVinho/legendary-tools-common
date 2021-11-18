using UnityEngine;

namespace LegendaryTools
{
    public static class MathUtil
    {
        public static Vector2 GenerateRndPointInsideCircle(float radius)
        {
            float angle = 2 * Mathf.PI * Random.Range(0, radius);
            float u = Random.Range(0, radius) + Random.Range(0, radius);
            float r = u > 1 ? 2 - u : u;
            return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        public static Vector2 GenerateRndPointOnCircle(float radius)
        {
            float angle = 2 * Mathf.PI * Random.value;
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
        
        public static int Fibonacci(int n)
        {
            int a = 0;
            int b = 1;
            // In N steps, compute Fibonacci sequence iteratively.
            for (int i = 0; i < n; i++)
            {
                int temp = a;
                a = b;
                b = temp + b;
            }
            return a;
        }
    }
}
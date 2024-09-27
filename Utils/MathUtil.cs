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
        
        public static bool LinesIntersect2D(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            float ua = ((b2.x - b1.x) * (a1.y - b1.y) - (b2.y - b1.y) * (a1.x - b1.x)) / ((b2.y - b1.y) * (a2.x - a1.x) - (b2.x - b1.x) * (a2.y - a1.y));
            float ub = ((a2.x - a1.x) * (a1.y - b1.y) - (a2.y - a1.y) * (a1.x - b1.x)) / ((b2.y - b1.y) * (a2.x - a1.x) - (b2.x - b1.x) * (a2.y - a1.y));
    
            return ua >= 0 && ua <= 1 && ub >= 0 && ub <= 1;
        }
        
        public static bool LineIntersection2D(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 intersection)
        {
            intersection = Vector2.zero;
            float denominator = ((p4.y - p3.y) * (p2.x - p1.x)) - ((p4.x - p3.x) * (p2.y - p1.y));
            if (denominator == 0) // as retas são paralelas
                return false;

            float ua = (((p4.x - p3.x) * (p1.y - p3.y)) - ((p4.y - p3.y) * (p1.x - p3.x))) / denominator;
            float ub = (((p2.x - p1.x) * (p1.y - p3.y)) - ((p2.y - p1.y) * (p1.x - p3.x))) / denominator;

            if (ua < 0 || ua > 1 || ub < 0 || ub > 1) // o ponto de cruzamento está fora do segmento de reta
                return false;

            intersection = new Vector2(p1.x + (ua * (p2.x - p1.x)), p1.y + (ua * (p2.y - p1.y)));
            return true;
        }
        
        public static bool LinesIntersect3D(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2)
        {
            Vector3 dirA = a2 - a1;
            Vector3 dirB = b2 - b1;
            Vector3 crossAB = Vector3.Cross(dirA, dirB);
            Vector3 crossAC = Vector3.Cross(dirA, a1 - b1);

            float t = Vector3.Dot(crossAC, crossAB) / crossAB.sqrMagnitude;

            if (t >= 0 && t <= 1)
            {
                Vector3 point = b1 + dirB * t;
                if (Vector3.Dot(point - a1, point - a2) <= 0 && Vector3.Dot(point - b1, point - b2) <= 0)
                {
                    return true;
                }
            }

            return false;
        }
        
        public static bool LinesIntersect3D(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2, out Vector3 intersection)
        {
            Vector3 dirA = a2 - a1;
            Vector3 dirB = b2 - b1;
            Vector3 crossAB = Vector3.Cross(dirA, dirB);
            Vector3 crossAC = Vector3.Cross(dirA, a1 - b1);

            float t = Vector3.Dot(crossAC, crossAB) / crossAB.sqrMagnitude;

            if (t >= 0 && t <= 1)
            {
                Vector3 point = b1 + dirB * t;
                if (Vector3.Dot(point - a1, point - a2) <= 0 && Vector3.Dot(point - b1, point - b2) <= 0)
                {
                    intersection = point;
                    return true;
                }
            }

            intersection = Vector3.zero;
            return false;
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
        
        /// <summary>
        /// Retorna o índice de um número na sequência de Fibonacci.
        /// Se o número não estiver na sequência, retorna -1.
        /// </summary>
        /// <param name="number">O número inteiro a ser procurado na sequência de Fibonacci.</param>
        /// <returns>Índice do número na sequência de Fibonacci ou -1 se não estiver presente.</returns>
        public static int GetFibonacciIndex(int number)
        {
            if (number < 0)
                return -1; // Números negativos não estão na sequência de Fibonacci

            // Casos base
            if (number == 0)
                return 0;
            if (number == 1)
                return 1; // O primeiro 1 está no índice 1

            int a = 0;
            int b = 1;
            int index = 1;

            while (b < number)
            {
                int temp = a + b;
                a = b;
                b = temp;
                index++;

                // Prevenção contra overflow
                if (b < 0)
                    break;
            }

            if (b == number)
                return index;
            else
                return -1;
        }
    }
}
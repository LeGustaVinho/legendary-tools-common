using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public class QuadtreeExample : MonoBehaviour
    {
        public int numberOfPoints = 100;
        public float worldSize = 100f;
        public GameObject pointPrefab;
        public Color queryColor = Color.red;

        private Quadtree quadtree;
        private List<GameObject> points;
        private Rectangle queryRange;

        void Start()
        {
            // Define the boundary of the Quadtree (centered at (0,0) with size worldSize)
            Rectangle boundary = new Rectangle(Vector2.zero, worldSize / 2, worldSize / 2);
            quadtree = new Quadtree(boundary);

            points = new List<GameObject>();

            // Instantiate points randomly within the world
            for (int i = 0; i < numberOfPoints; i++)
            {
                Vector2 pos = new Vector2(Random.Range(-worldSize / 2, worldSize / 2),
                    Random.Range(-worldSize / 2, worldSize / 2));
                GameObject point = Instantiate(pointPrefab, new Vector3(pos.x, pos.y, 0), Quaternion.identity);
                points.Add(point);

                // Insert the point into the Quadtree
                Point qtPoint = new Point(pos, point);
                quadtree.Insert(qtPoint);
            }

            // Define a query range (for example, a rectangle around the origin)
            queryRange = new Rectangle(Vector2.zero, 20f, 20f);
        }

        void OnDrawGizmos()
        {
            if (quadtree == null || queryRange == null) return;
            quadtree.ShowGizmos();
            
            // Draw the query range
            Gizmos.color = queryColor;
            Gizmos.DrawWireCube(queryRange.Center, new Vector3(queryRange.HalfWidth * 2, queryRange.HalfHeight * 2, 0));

            // Optionally, draw points found within the query range
            if (quadtree != null)
            {
                List<Point> foundPoints = quadtree.Query(queryRange);
                Gizmos.color = Color.green;
                foreach (var p in foundPoints)
                {
                    Gizmos.DrawSphere(new Vector3(p.Position.x, p.Position.y, 0), 0.5f);
                }
            }
        }
    }
}

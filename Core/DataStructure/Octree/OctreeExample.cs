using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public class OctreeExample : MonoBehaviour
    {
        [Header("Octree Settings")]
        public int numberOfPoints = 500;
        public Vector3 worldSize = new Vector3(100f, 100f, 100f);
        public GameObject pointPrefab;
        public Color queryColor = Color.red;

        private Octree octree;
        private List<GameObject> points;
        private BoundingBox queryRange;

        void Start()
        {
            // Define the boundary of the Octree (centered at (0,0,0) with size worldSize)
            BoundingBox boundary = new BoundingBox(Vector3.zero, worldSize / 2f);
            octree = new Octree(boundary);

            points = new List<GameObject>();

            // Instantiate points randomly within the world
            for (int i = 0; i < numberOfPoints; i++)
            {
                Vector3 pos = new Vector3(
                    Random.Range(-worldSize.x / 2f, worldSize.x / 2f),
                    Random.Range(-worldSize.y / 2f, worldSize.y / 2f),
                    Random.Range(-worldSize.z / 2f, worldSize.z / 2f)
                );

                GameObject point = Instantiate(pointPrefab, pos, Quaternion.identity);
                points.Add(point);

                // Insert the point into the Octree
                Point3D octreePoint = new Point3D(pos, point);
                octree.Insert(octreePoint);
            }

            // Define a query range (for example, a bounding box around the origin)
            queryRange = new BoundingBox(Vector3.zero, new Vector3(20f, 20f, 20f));
        }

        void OnDrawGizmos()
        {
            if (octree == null || queryRange == null) return;
            if (octree != null)
            {
                octree.ShowGizmos();
            }

            // Draw the query range
            Gizmos.color = queryColor;
            Gizmos.DrawWireCube(queryRange.Center, queryRange.HalfSize * 2f);

            // Optionally, draw points found within the query range
            if (octree != null)
            {
                List<Point3D> foundPoints = octree.Query(queryRange);
                Gizmos.color = Color.green;
                foreach (var p in foundPoints)
                {
                    Gizmos.DrawSphere(p.Position, 0.5f);
                }
            }
        }
    }
}

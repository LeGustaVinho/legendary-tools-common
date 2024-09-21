using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    /// <summary>
    ///     Represents a point in 3D space with a reference to a GameObject.
    /// </summary>
    public class Point3D
    {
        public Vector3 Position { get; }
        public GameObject GameObject { get; private set; }

        public Point3D(Vector3 position, GameObject gameObject)
        {
            Position = position;
            GameObject = gameObject;
        }
    }


    /// <summary>
    ///     Represents an axis-aligned bounding box in 3D space.
    /// </summary>
    public class BoundingBox
    {
        public Vector3 Center { get; }
        public Vector3 HalfSize { get; }

        public BoundingBox(Vector3 center, Vector3 halfSize)
        {
            Center = center;
            HalfSize = halfSize;
        }

        /// <summary>
        ///     Checks if this bounding box contains a given point.
        /// </summary>
        public bool Contains(Point3D point)
        {
            return point.Position.x >= Center.x - HalfSize.x &&
                   point.Position.x <= Center.x + HalfSize.x &&
                   point.Position.y >= Center.y - HalfSize.y &&
                   point.Position.y <= Center.y + HalfSize.y &&
                   point.Position.z >= Center.z - HalfSize.z &&
                   point.Position.z <= Center.z + HalfSize.z;
        }

        /// <summary>
        ///     Checks if this bounding box intersects with another bounding box.
        /// </summary>
        public bool Intersects(BoundingBox range)
        {
            return !(range.Center.x - range.HalfSize.x > Center.x + HalfSize.x ||
                     range.Center.x + range.HalfSize.x < Center.x - HalfSize.x ||
                     range.Center.y - range.HalfSize.y > Center.y + HalfSize.y ||
                     range.Center.y + range.HalfSize.y < Center.y - HalfSize.y ||
                     range.Center.z - range.HalfSize.z > Center.z + HalfSize.z ||
                     range.Center.z + range.HalfSize.z < Center.z - HalfSize.z);
        }
    }

    /// <summary>
    ///     Octree data structure for spatial partitioning in 3D space.
    /// </summary>
    public class Octree
    {
        private const int MAX_POINTS = 8; // Maximum points per node before subdivision
        private readonly BoundingBox boundary; // Boundary of this node
        private readonly List<Point3D> points; // Points contained in this node
        private bool isDivided; // Whether this node has been subdivided

        // Children Octrees
        private readonly Octree[] children;

        public Octree(BoundingBox boundary)
        {
            this.boundary = boundary;
            points = new List<Point3D>();
            isDivided = false;
            children = new Octree[8];
        }

        /// <summary>
        ///     Subdivides the current Octree node into eight children.
        /// </summary>
        private void Subdivide()
        {
            Vector3 newHalfSize = boundary.HalfSize / 2f;
            Vector3 center = boundary.Center;

            // Define the 8 octants
            children[0] = new Octree(new BoundingBox(center + new Vector3(newHalfSize.x, newHalfSize.y, newHalfSize.z),
                newHalfSize));
            children[1] = new Octree(new BoundingBox(center + new Vector3(-newHalfSize.x, newHalfSize.y, newHalfSize.z),
                newHalfSize));
            children[2] = new Octree(new BoundingBox(center + new Vector3(newHalfSize.x, -newHalfSize.y, newHalfSize.z),
                newHalfSize));
            children[3] =
                new Octree(new BoundingBox(center + new Vector3(-newHalfSize.x, -newHalfSize.y, newHalfSize.z),
                    newHalfSize));
            children[4] = new Octree(new BoundingBox(center + new Vector3(newHalfSize.x, newHalfSize.y, -newHalfSize.z),
                newHalfSize));
            children[5] =
                new Octree(new BoundingBox(center + new Vector3(-newHalfSize.x, newHalfSize.y, -newHalfSize.z),
                    newHalfSize));
            children[6] =
                new Octree(new BoundingBox(center + new Vector3(newHalfSize.x, -newHalfSize.y, -newHalfSize.z),
                    newHalfSize));
            children[7] =
                new Octree(new BoundingBox(center + new Vector3(-newHalfSize.x, -newHalfSize.y, -newHalfSize.z),
                    newHalfSize));

            isDivided = true;
        }

        /// <summary>
        ///     Inserts a point into the Octree.
        /// </summary>
        public bool Insert(Point3D point)
        {
            // Ignore objects that do not belong in this octree
            if (!boundary.Contains(point)) return false;

            // If there is space in this octree and it doesn't have children, add the object here
            if (points.Count < MAX_POINTS && !isDivided)
            {
                points.Add(point);
                return true;
            }

            // Subdivide if not already done
            if (!isDivided) Subdivide();

            // Insert the point into appropriate children
            foreach (Octree child in children)
                if (child.Insert(point))
                    return true;

            // Should never reach here
            return false;
        }

        /// <summary>
        ///     Queries the Octree for points within a given range.
        /// </summary>
        public List<Point3D> Query(BoundingBox range, List<Point3D> found = null)
        {
            if (found == null) found = new List<Point3D>();

            // If the range does not intersect this node's boundary, return
            if (!boundary.Intersects(range))
            {
                return found;
            }

            // Check objects at this level
            foreach (Point3D p in points)
                if (range.Contains(p))
                    found.Add(p);

            // If subdivided, check the children
            if (isDivided)
                foreach (Octree child in children)
                    child.Query(range, found);

            return found;
        }

        /// <summary>
        ///     Optional: Draws the Octree boundaries for visualization in Unity.
        /// </summary>
        public void ShowGizmos()
        {
            // Draw boundary
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(boundary.Center, boundary.HalfSize * 2f);

            if (isDivided)
                foreach (Octree child in children)
                    child.ShowGizmos();
        }
    }
}
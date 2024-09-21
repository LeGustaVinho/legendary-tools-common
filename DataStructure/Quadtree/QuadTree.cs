using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    // Represents a point in 2D space with a reference to a GameObject
    public class Point
    {
        public Vector2 Position { get; }
        public GameObject GameObject { get; private set; }

        public Point(Vector2 position, GameObject gameObject)
        {
            Position = position;
            GameObject = gameObject;
        }
    }


    // Represents a rectangle area in 2D space
    public class Rectangle
    {
        public Vector2 Center { get; }
        public float HalfWidth { get; }
        public float HalfHeight { get; }

        public Rectangle(Vector2 center, float halfWidth, float halfHeight)
        {
            Center = center;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
        }

        // Checks if this rectangle contains a given point
        public bool Contains(Point point)
        {
            return point.Position.x >= Center.x - HalfWidth &&
                   point.Position.x <= Center.x + HalfWidth &&
                   point.Position.y >= Center.y - HalfHeight &&
                   point.Position.y <= Center.y + HalfHeight;
        }

        // Checks if this rectangle intersects with another rectangle
        public bool Intersects(Rectangle range)
        {
            return !(range.Center.x - range.HalfWidth > Center.x + HalfWidth ||
                     range.Center.x + range.HalfWidth < Center.x - HalfWidth ||
                     range.Center.y - range.HalfHeight > Center.y + HalfHeight ||
                     range.Center.y + range.HalfHeight < Center.y - HalfHeight);
        }
    }


// Quadtree data structure for spatial partitioning
    public class Quadtree
    {
        private const int MAX_POINTS = 4; // Maximum points per node before subdivision
        private readonly Rectangle boundary; // Boundary of this node
        private readonly List<Point> points; // Points contained in this node
        private bool divided; // Whether this node has been subdivided

        private Quadtree northeast;
        private Quadtree northwest;
        private Quadtree southeast;
        private Quadtree southwest;

        public Quadtree(Rectangle boundary)
        {
            this.boundary = boundary;
            points = new List<Point>();
            divided = false;
        }

        // Subdivides the current Quadtree node into four children
        private void Subdivide()
        {
            float x = boundary.Center.x;
            float y = boundary.Center.y;
            float hw = boundary.HalfWidth / 2;
            float hh = boundary.HalfHeight / 2;

            Rectangle ne = new Rectangle(new Vector2(x + hw, y + hh), hw, hh);
            northeast = new Quadtree(ne);

            Rectangle nw = new Rectangle(new Vector2(x - hw, y + hh), hw, hh);
            northwest = new Quadtree(nw);

            Rectangle se = new Rectangle(new Vector2(x + hw, y - hh), hw, hh);
            southeast = new Quadtree(se);

            Rectangle sw = new Rectangle(new Vector2(x - hw, y - hh), hw, hh);
            southwest = new Quadtree(sw);

            divided = true;
        }

        // Inserts a point into the Quadtree
        public bool Insert(Point point)
        {
            // Ignore objects that do not belong in this quad tree
            if (!boundary.Contains(point)) return false;

            // If there is space in this quad tree and if doesn't have subdivisions, add the object here
            if (points.Count < MAX_POINTS)
            {
                points.Add(point);
                return true;
            }

            // Subdivide if not already done
            if (!divided) Subdivide();

            // Insert the point into whichever quadrant it belongs to
            if (northeast.Insert(point)) return true;
            if (northwest.Insert(point)) return true;
            if (southeast.Insert(point)) return true;
            if (southwest.Insert(point)) return true;

            // Should never reach here
            return false;
        }

        // Queries the Quadtree for points within a given range
        public List<Point> Query(Rectangle range, List<Point> found = null)
        {
            if (found == null) found = new List<Point>();

            // If the range does not intersect this node's boundary, return
            if (!boundary.Intersects(range))
            {
                return found;
            }

            // Check objects at this level
            foreach (Point p in points)
                if (range.Contains(p))
                    found.Add(p);

            // If subdivided, check the children
            if (divided)
            {
                northeast.Query(range, found);
                northwest.Query(range, found);
                southeast.Query(range, found);
                southwest.Query(range, found);
            }

            return found;
        }

        // Optional: Draws the Quadtree boundaries for visualization in Unity
        public void ShowGizmos()
        {
            // Draw boundary
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(boundary.Center, new Vector3(boundary.HalfWidth * 2, boundary.HalfHeight * 2, 0));

            if (divided)
            {
                northeast.ShowGizmos();
                northwest.ShowGizmos();
                southeast.ShowGizmos();
                southwest.ShowGizmos();
            }
        }
    }
}
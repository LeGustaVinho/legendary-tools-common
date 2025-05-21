using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LegendaryTools.AttributeSystem.HexGrid
{
    public class HexMap : ICollection<Hex>
    {
        public Layout Layout { get; private set; }
        [SerializeField]
        private HashSet<Hex> map = new HashSet<Hex>();

        public HashSet<Hex> Hexes => new HashSet<Hex>(map);

        public HexMap(Layout.WorldPlane plane, Layout.OrientationType orientationType, Vector3 size, Vector3 origin)
        {
            Layout = new Layout(plane, orientationType, size, origin);
        }

        public void Add(Hex item)
        {
            map.Add(item);
        }

        public void Clear()
        {
            map.Clear();
        }

        public bool Contains(Hex cell)
        {
            return map.Contains(cell);
        }

        public void CopyTo(Hex[] array, int arrayIndex)
        {
            map.CopyTo(array, arrayIndex);
        }

        public bool Remove(Hex item)
        {
            return map.Remove(item);
        }

        public int Count => map.Count;
        public bool IsReadOnly => false;

        public IEnumerator<Hex> GetEnumerator()
        {
            return map.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Draw()
        {
            foreach (Hex cell in map)
            {
                DrawCellGizmos(cell);
            }
        }

        public void DrawCellGizmos(Hex cell)
        {
            Vector3[] corners = Layout.PolygonCorners(cell);

            for (int i = 0; i < corners.Length; i++)
            {
                Gizmos.DrawLine(corners[i], i != corners.Length - 1 ? corners[i + 1] : corners[0]);
            }
        }
        
        public void DrawCellDebug(Hex cell, Color color, float duration)
        {
            Vector3[] corners = Layout.PolygonCorners(cell);

            for (int i = 0; i < corners.Length; i++)
            {
                Debug.DrawLine(corners[i], i != corners.Length - 1 ? corners[i + 1] : corners[0], color, duration);
            }
        }

        public Hex[] Neighbors(Hex node)
        {
            List<Hex> neighbors = new List<Hex>(6);

            for (int i = 0; i < 6; i++)
            {
                Hex neighbor = node.Neighbor(i);
                if (map.Contains(neighbor))
                {
                    neighbors.Add(neighbor);
                }
            }

            return neighbors.ToArray();
        }

        public Hex[] Line(Hex a, Hex b)
        {
            return FractionalHex.Line(a, b);
        }

        public Hex[] InRange(Hex center, int range)
        {
            List<Hex> cellsInRange = new List<Hex>();

            Hex current;
            for (int q = -range; q <= range; q++)
            {
                for (int r = Mathf.Max(-range, -q - range); r <= Mathf.Min(range, -q + range); r++)
                {
                    current = center + new Hex(q, r);
                    if (map.Contains(current))
                    {
                        cellsInRange.Add(current);
                    }
                }
            }

            return cellsInRange.ToArray();
        }

        public Hex[] Ring(Hex center, int radius)
        {
            List<Hex> cellsInRing = new List<Hex>();
            if (radius == 0)
            {
                cellsInRing.Add(center);
                return cellsInRing.ToArray();
            }
            Hex direction = (Hex.Direction(4) * radius).Round();
            Hex hex = center + direction;
            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < radius; j++)
                {
                    cellsInRing.Add(hex);
                    hex = hex.Neighbor(i);
                }
            }

            return cellsInRing.ToArray();
        }

        public Vector2 HexToPixel(Hex h)
        {
            return Layout.HexToPixel(h);
        }
        
        public Vector3 HexToPosition(Hex h, float elevation)
        {
            return Layout.HexToPosition(h, elevation);
        }

        public Hex PixelToHex(Vector3 p)
        {
            return Layout.PixelToHex(p);
        }

        public void HexagonalShape(int size)
        {
            map.Clear();
            for (int q = -size; q <= size; q++)
            {
                for (int r = Mathf.Max(-size, -q - size); r <= Mathf.Min(size, -q + size); r++)
                {
                    map.Add(new Hex(q, r, -q - r));
                }
            }
        }

        public void RectangularShape(int width, int height)
        {
            map.Clear();
            for (int q = 0; q < width; q++)
            {
                int qOff = q >> 1;
                for (int r = -qOff; r < height - qOff; r++)
                {
                    map.Add(new Hex(q, r, -q - r));
                }
            }
        }

        public void ParallelogramShape(int width, int height)
        {
            map.Clear();
            for (int q = 0; q <= width; q++)
            {
                for (int r = 0; r <= height; r++)
                {
                    map.Add(new Hex(q, r, -q - r));
                }
            }
        }

        public void TriangleShape(int size)
        {
            map.Clear();
            for (int q = 0; q <= size; q++)
            {
                for (int r = 0; r <= size - q; r++)
                {
                    map.Add(new Hex(q, r, -q - r));
                }
            }
        }
    }
}
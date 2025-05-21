using System;
using System.Collections.Generic;
using LegendaryTools.AI.AStar;
using UnityEngine;

namespace LegendaryTools.AttributeSystem.HexGrid
{
    public class HexMapView : MonoBehaviour, IAStar<Hex>
    {
        [Header("Pathfinding")] 
        public WorldHexLine AgentStartEnd;
        public WorldHexLine[] LineWalls;

        [Header("Hex to Pixel Conversion")] 
        public Hex HexToPixelCoords;
        public Transform HexToPixelTransform;
        
        [Header("Pixel to Hex Conversion")] 
        public Hex PixelToHexCoords;
        public Transform PixelToHexTransform;

        [Header("Ring")] 
        public int RingRadius;
        public Transform RingCenterTransform;
        
        public HexMap HexMap;
        
        [Space]
        public int Radius = 10;
        public Vector2 CellSize = new Vector2(10, 10);
        public Layout.WorldPlane Plane = Layout.WorldPlane.XY;
        public Layout.OrientationType Orientation = Layout.OrientationType.Flat;

        private List<Hex> neighborsBuffer = new List<Hex>();
        private HashSet<Hex> Path = new HashSet<Hex>();
        private AStar<Hex> Pathfinding;
        private HashSet<Hex> Walls = new HashSet<Hex>();

        public int MapLocationsAmount => HexMap.Count;

        public Hex[] Neighbors(Hex node)
        {
            neighborsBuffer.Clear();

            foreach (Hex current in HexMap.Neighbors(node))
            {
                if (!Walls.Contains(current))
                {
                    neighborsBuffer.Add(current);
                }
            }

            return neighborsBuffer.ToArray();
        }

        public float Heuristic(Hex nodeA, Hex nodeB)
        {
            return 0;
        }
        
        private void OnDrawGizmos()
        {
            if (HexMap == null)
            {
                RecreateHexGrid();
            }

            if (LineWalls != null)
            {
                Walls.Clear();
                for (int i = 0; i < LineWalls.Length; i++)
                {
                    if (LineWalls[i].PointA != null && LineWalls[i].PointB != null)
                    {
                        foreach (Hex cell in HexMap.Line(HexMap.PixelToHex(LineWalls[i].PointA.position),
                            HexMap.PixelToHex(LineWalls[i].PointB.position)))
                        {
                            Walls.Add(cell);
                        }
                    }
                }
            }

            Path.Clear();
            if (AgentStartEnd.PointA != null && AgentStartEnd.PointB != null)
            {
                foreach (Hex cell in Pathfinding.FindPath(HexMap.PixelToHex(AgentStartEnd.PointA.position),
                    HexMap.PixelToHex(AgentStartEnd.PointB.position)))
                {
                    Path.Add(cell);
                }
            }

            foreach (Hex cell in HexMap)
            {
                if (Path.Contains(cell))
                {
                    Gizmos.color = Color.red;
                }
                else if (Walls.Contains(cell))
                {
                    Gizmos.color = Color.black;
                }
                else
                {
                    Gizmos.color = Color.white;
                }

                if (RingRadius > 0 && RingCenterTransform != null)
                {
                    HashSet<Hex> ringHexes =
                        new HashSet<Hex>(HexMap.Ring(HexMap.PixelToHex(RingCenterTransform.position), RingRadius));
                    if(ringHexes.Contains(cell))
                    {
                        Gizmos.color = Color.yellow;
                    }
                }

                HexMap.DrawCellGizmos(cell);
            }

            if (HexToPixelTransform != null)
            {
                HexToPixelTransform.position = HexMap.HexToPosition(HexToPixelCoords, Plane == Layout.WorldPlane.XY ? transform.position.z : transform.position.y);
            }
            
            if (PixelToHexTransform != null)
            {
                PixelToHexCoords = HexMap.PixelToHex(PixelToHexTransform.position);
            }
        }

        [ContextMenu("RecreateHexGrid")]
        private void RecreateHexGrid()
        {
            HexMap = new HexMap(Plane, Orientation, CellSize, transform.position);
            HexMap.HexagonalShape(Radius);
            Pathfinding = new AStar<Hex>(this);
        }

        [Serializable]
        public struct WorldHexLine
        {
            public Transform PointA;
            public Transform PointB;
        }
    }
}
using System.Collections.Generic;

namespace LegendaryTools.AI.AStar
{
    public interface IAStar<T>
    {
        T[] Neighbors(T node);

        float Heuristic(T nodeA, T nodeB);
    }

    public class AStar<T>
    {
        private static readonly Dictionary<T, AStarEntry> cachedNodes = new Dictionary<T, AStarEntry>();
        private readonly IAStar<T> map;

        public AStar(IAStar<T> map)
        {
            this.map = map;
        }

        public T[] FindPath(T startLocation, T endLocation)
        {
            Dictionary<AStarEntry, AStarEntry> cameFrom = new Dictionary<AStarEntry, AStarEntry>();
            Dictionary<AStarEntry, float> costSoFar = new Dictionary<AStarEntry, float>();
            PriorityQueue<AStarEntry> open = new PriorityQueue<AStarEntry>();

            cleanNodesData();

            AStarEntry startEntry = getFromCache(startLocation);
            AStarEntry endEntry = getFromCache(endLocation);

            open.Enqueue(startEntry);
            cameFrom[startEntry] = startEntry;
            costSoFar[startEntry] = 0;

            AStarEntry currentEntry;
            while (open.Count > 0)
            {
                currentEntry = open.Dequeue();

                if (currentEntry.Location.Equals(endLocation))
                {
                    List<T> path = new List<T>();
                    currentEntry = endEntry;

                    while (currentEntry != startEntry)
                    {
                        path.Add(currentEntry.Location);
                        currentEntry = cameFrom[currentEntry];
                    }

                    path.Reverse();
                    return path.ToArray();
                }

                T[] neighbours = map.Neighbors(currentEntry.Location);
                AStarEntry currentNeighborsEntry = null;
                for (int i = 0; i < neighbours.Length; i++)
                {
                    currentNeighborsEntry = getFromCache(neighbours[i]);
                    float newCost = costSoFar[currentEntry];
                    if (!costSoFar.ContainsKey(currentNeighborsEntry) || newCost < costSoFar[currentNeighborsEntry])
                    {
                        costSoFar[currentNeighborsEntry] = newCost;
                        currentNeighborsEntry.Score =
                            newCost + map.Heuristic(currentNeighborsEntry.Location, endLocation);
                        open.Enqueue(currentNeighborsEntry);
                        cameFrom[currentNeighborsEntry] = currentEntry;
                    }
                }
            }

            return null;
        }

        private static void cleanNodesData()
        {
            foreach (KeyValuePair<T, AStarEntry> pair in cachedNodes)
            {
                pair.Value.Clean();
            }
        }

        private static AStarEntry getFromCache(T node)
        {
            if (!cachedNodes.ContainsKey(node))
            {
                cachedNodes.Add(node, new AStarEntry(node));
            }

            return cachedNodes[node];
        }

        private class AStarEntry : IPriorityQueueEntry
        {
            public readonly T Location;

            public float Score;

            public AStarEntry(T location)
            {
                Location = location;
            }

            public float Priority
            {
                get => Score;
                set => Score = value;
            }

            public override int GetHashCode()
            {
                return Location.GetHashCode();
            }

            public void Clean()
            {
                Score = 0;
            }
        }
    }
}
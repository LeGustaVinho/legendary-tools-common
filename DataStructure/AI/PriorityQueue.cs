using System.Collections.Generic;

namespace LegendaryTools.AI.AStar
{
    public interface IPriorityQueueEntry
    {
        float Priority { get; set; }
    }

    public class PriorityQueue<T>
        where T : IPriorityQueueEntry
    {
        private readonly List<T> queue = new List<T>();

        public int Count => queue.Count;

        public void Enqueue(T item)
        {
            queue.Add(item);
            Sort(0, queue.Count - 1);
        }

        public T Dequeue()
        {
            T item = queue[0];
            queue.RemoveAt(0);
            return item;
        }

        //Quick sort algorithm
        private void Sort(int startIndex, int endIndex)
        {
            if (startIndex < endIndex)
            {
                float p = queue[startIndex].Priority;
                int i = startIndex + 1;
                int f = endIndex;

                while (i <= f)
                {
                    if (queue[i].Priority <= p)
                    {
                        i++;
                    }
                    else if (p < queue[f].Priority)
                    {
                        f--;
                    }
                    else
                    {
                        float swap = queue[i].Priority;
                        queue[i].Priority = queue[f].Priority;
                        queue[f].Priority = swap;
                        i++;
                        f--;
                    }
                }

                queue[startIndex].Priority = queue[f].Priority;
                queue[f].Priority = p;

                Sort(startIndex, f - 1);
                Sort(f + 1, endIndex);
            }
        }
    }
}
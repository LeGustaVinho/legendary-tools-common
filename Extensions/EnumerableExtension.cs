using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools
{
    public static class EnumerableExtension
    {
        public static void Shuffle<T>(this IList<T> list, Random rnd)
        {
            for (int i = 0; i < list.Count; i++)
            {
                list.Swap(i, rnd.Next(i, list.Count));
            }
        }

        public static void Swap<T>(this IList<T> list, int i, int j)
        {
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }

        public static void Swap(this IList list, int i, int j)
        {
            object temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }

        public static void MoveForward(this IList list)
        {
            for (int i = 0; i < list.Count - 1; i++)
            {
                list.Swap(i, i + 1);
            }
        }

        public static void MoveBackwards(this IList list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                list.Swap(i, i - 1);
            }
        }

        public static void Resize<T>(this IList<T> list, int newSize)
        {
            if (newSize == 0)
            {
                list.Clear();
            }
            else
            {
                int delta = newSize - list.Count;

                if (delta > 0)
                {
                    for (int i = 0; i < delta; i++)
                    {
                        if (list.Count > 0)
                        {
                            list.Insert(list.Count - 1, default); //insert at last
                        }
                        else
                        {
                            list.Insert(0, default);
                        }
                    }
                }
                else if (delta < 0)
                {
                    for (int i = Math.Abs(delta); i >= 0; i--)
                    {
                        if (list.Count > 0)
                        {
                            list.RemoveAt(list.Count - 1); //remove last
                        }
                    }
                }
            }
        }

        public static T GetRandom<T>(this IList<T> list)
        {
            if (list != null && list.Count > 0)
            {
                return list[UnityEngine.Random.Range(0, list.Count)];
            }

            return default;
        }

        public static T FirstOrDefault<T>(this IList<T> list)
        {
            if (list != null)
            {
                if (list.Count > 0)
                {
                    return list[0];
                }

                return default;
            }

            return default;
        }

        public static T Last<T>(this IList<T> list)
        {
            if (list != null)
            {
                if (list.Count > 0)
                {
                    return list[list.Count - 1];
                }

                return default;
            }

            return default;
        }

        public static bool Any<T>(this List<T> list, Predicate<T> match)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (match(list[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Any<T>(this T[] array, Predicate<T> match)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (match(array[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static T First<T>(this IEnumerable<T> items)
        {
            using (IEnumerator<T> iter = items.GetEnumerator())
            {
                iter.MoveNext();
                return iter.Current;
            }
        }
    }
}
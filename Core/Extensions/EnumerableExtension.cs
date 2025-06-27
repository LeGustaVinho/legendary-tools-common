using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools
{
    public interface IRandomWeight
    {
        float Weight { get; }
    }
    
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
        
        public static T GetRandomWeight<T>(this IList<T> list)
            where T : IRandomWeight
        {
            float total = 0;
            foreach (T item in list) 
            {
                total += item.Weight;
            }

            float randomPoint = UnityEngine.Random.value * total;

            foreach (T item in list)
            {
                if (randomPoint < item.Weight) 
                {
                    return item;
                }
                
                randomPoint -= item.Weight;
            }
            
            return list[list.Count-1];
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

        public static bool Any<T>(this IList<T> list, Predicate<T> match)
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

        public static bool All<T>(this IList<T> list, Predicate<T> match)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (!match(list[i]))
                {
                    return false;
                }
            }

            return true;
        }
        
        public static List<T> Clone<T>(this List<T> list)
        {
            if (!typeof(T).IsSerializable)
            {
                throw new ArgumentException("The type must be serializable.", "list");
            }
            
            List<T> result = new List<T>();

            foreach (T item in list)
            {
                result.Add(item.DeepClone());
            }
            
            return result;
        }

        public static T First<T>(this IEnumerable<T> items)
        {
            using (IEnumerator<T> iter = items.GetEnumerator())
            {
                iter.MoveNext();
                return iter.Current;
            }
        }

        public static void AddRange<T>(this HashSet<T> hashSet, IEnumerable<T> items)
        {
            foreach (T item in items)
            {
                if (!hashSet.Contains(item))
                {
                    hashSet.Add(item);
                }
            }
        }
        
        public static void RemoveRange<T>(this HashSet<T> hashSet, IEnumerable<T> items)
        {
            foreach (T item in items)
            {
                if (hashSet.Contains(item))
                {
                    hashSet.Remove(item);
                }
            }
        }
        
        /// <summary>
        /// Check is lhs contains any element of rhs
        /// </summary>
        /// <param name="hashSet1"></param>
        /// <param name="hashSet2"></param>
        /// <typeparam name="T"></typeparam>
        public static bool ContainsAny<T>(this HashSet<T> lhs,  HashSet<T> rhs)
        {
            foreach (T item in rhs)
            {
                if (lhs.Contains(item))
                {
                    return true;
                }
            }

            return false;
        }
        
        /// <summary>
        /// Adds an item to the end of the array and returns a new array.
        /// </summary>
        /// <typeparam name="T">Type of the array elements.</typeparam>
        /// <param name="source">The source array.</param>
        /// <param name="item">The item to add.</param>
        /// <returns>
        /// A new array containing all elements of the source array,
        /// followed by the new item.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if the source array is null.
        /// </exception>
        public static T[] AddItem<T>(this T[] source, T item)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            // Allocate a new array with an extra slot for the new item.
            T[] result = new T[source.Length + 1];

            // Copy existing elements to the new array.
            Array.Copy(source, result, source.Length);

            // Assign the new item to the last position.
            result[source.Length] = item;

            return result;
        }
    }
}
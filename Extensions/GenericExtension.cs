using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace LegendaryTools
{
    public static class GenericExtension
    {
        /// <summary>
        /// Perform a deep Copy of the object.
        /// </summary>
        /// <typeparam name="T">The type of object being copied.</typeparam>
        /// <param name="source">The object instance to copy.</param>
        /// <returns>The copied object.</returns>
        public static T Clone<T>(this T source)
        {
            if (!typeof(T).IsSerializable)
            {
                throw new ArgumentException("The type must be serializable.", "source");
            }

            // Don't serialize a null object, simply return the default for that object
            if (ReferenceEquals(source, null))
            {
                return default;
            }

            IFormatter formatter = new BinaryFormatter();
            Stream stream = new MemoryStream();
            using (stream)
            {
                formatter.Serialize(stream, source);
                stream.Seek(0, SeekOrigin.Begin);
                return (T) formatter.Deserialize(stream);
            }
        }

        public static bool DeepEquals(this object lhs, object rhs)
        {
            // Check for null on left side.
            if (ReferenceEquals(lhs, null))
            {
                if (ReferenceEquals(rhs, null)) // null == null = true.
                {
                    return true;
                }

                return false; // Only the left side is null.
            }

            if (lhs.GetType() != rhs.GetType())
            {
                return false;
            }

            if (lhs.GetType().IsValueType != rhs.GetType().IsValueType)
            {
                return false;
            }

            if (lhs.GetType().IsValueType)
            {
                Debug.Log(lhs.GetHashCode() + " == " + rhs.GetHashCode() + "?" +
                          (lhs.GetHashCode() == rhs.GetHashCode()));
                return lhs.GetHashCode() == rhs.GetHashCode();
            }

            return ReferenceEquals(lhs, rhs);
        }
    }
}
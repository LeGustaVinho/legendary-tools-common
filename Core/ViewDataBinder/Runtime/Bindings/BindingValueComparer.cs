using System;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    public static class BindingValueComparer
    {
        public static bool AreEqual(object left, object right)
        {
            if (IsUnityNull(left) && IsUnityNull(right))
            {
                return true;
            }

            if (IsUnityNull(left) || IsUnityNull(right))
            {
                return false;
            }

            return Equals(left, right);
        }

        public static bool IsUnityNull(object value)
        {
            if (value == null)
            {
                return true;
            }

            return value is UnityEngine.Object unityObject && unityObject == null;
        }
    }
}

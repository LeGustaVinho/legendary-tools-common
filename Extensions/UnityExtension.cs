using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools
{
    public static class UnityExtension
    {
        private static Slider.SliderEvent emptySliderEvent = new Slider.SliderEvent();

        private static Toggle.ToggleEvent emptyToggleEvent = new Toggle.ToggleEvent();

        private static InputField.OnChangeEvent emptyInputFieldEvent = new InputField.OnChangeEvent();

        public static void SetValue(this Slider instance, float value)
        {
            Slider.SliderEvent originalEvent = instance.onValueChanged;
            instance.onValueChanged = emptySliderEvent;
            instance.value = value;
            instance.onValueChanged = originalEvent;
        }

        public static void SetValue(this Toggle instance, bool value)
        {
            Toggle.ToggleEvent originalEvent = instance.onValueChanged;
            instance.onValueChanged = emptyToggleEvent;
            instance.isOn = value;
            instance.onValueChanged = originalEvent;
        }

        public static void SetValue(this InputField instance, string value)
        {
            InputField.OnChangeEvent originalEvent = instance.onValueChanged;
            instance.onValueChanged = emptyInputFieldEvent;
            instance.text = value;
            instance.onValueChanged = originalEvent;
        }

        public static Vector3 RandomInsideBox(this Bounds bounds)
        {
            Vector3[] boundsPoints = new Vector3[5];

            boundsPoints[0] = bounds.min;
            boundsPoints[1] = bounds.max;
            boundsPoints[2] = new Vector3(boundsPoints[0].x, boundsPoints[0].y, boundsPoints[1].z);
            boundsPoints[3] = new Vector3(boundsPoints[0].x, boundsPoints[1].y, boundsPoints[0].z);
            boundsPoints[4] = new Vector3(boundsPoints[1].x, boundsPoints[0].y, boundsPoints[0].z);

            Vector3 randomPoint = new Vector3(Random.Range(boundsPoints[0].x, boundsPoints[4].x),
                Random.Range(boundsPoints[0].y, boundsPoints[3].y),
                Random.Range(boundsPoints[0].z, boundsPoints[2].z));

            return randomPoint;
        }

        public static Vector3[] BoundsPoints(this Bounds bounds)
        {
            Vector3[] boundsPoints = new Vector3[8];

            boundsPoints[0] = bounds.min;
            boundsPoints[1] = bounds.max;
            boundsPoints[2] = new Vector3(boundsPoints[0].x, boundsPoints[0].y, boundsPoints[1].z);
            boundsPoints[3] = new Vector3(boundsPoints[0].x, boundsPoints[1].y, boundsPoints[0].z);
            boundsPoints[4] = new Vector3(boundsPoints[1].x, boundsPoints[0].y, boundsPoints[0].z);
            boundsPoints[5] = new Vector3(boundsPoints[0].x, boundsPoints[1].y, boundsPoints[1].z);
            boundsPoints[6] = new Vector3(boundsPoints[1].x, boundsPoints[0].y, boundsPoints[1].z);
            boundsPoints[7] = new Vector3(boundsPoints[1].x, boundsPoints[1].y, boundsPoints[0].z);

            return boundsPoints;
        }

        public static Rect RectWorld(this RectTransform rectTransform)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            return new Rect(corners[1].x, corners[1].y, Mathf.Abs(corners[2].x - corners[1].x),
                Mathf.Abs(corners[0].y - corners[1].y));
        }

        public static bool ContainBounds(this Bounds bounds, Bounds target)
        {
            return bounds.Contains(target.min) && bounds.Contains(target.max);
        }

        public static T[] FindObjectsOfType<T>(this Object unityObj, bool includeInactive = false,
            params Transform[] nonActiveParents) where T : Object
        {
            if (includeInactive)
            {
                List<T> allComponents = new List<T>();
                List<Transform> allGameObjects = new List<Transform>(Object.FindObjectsOfType<Transform>());
                allGameObjects.AddRange(nonActiveParents);
                allGameObjects.RemoveAll(item => item.parent != null);

                for (int i = 0; i < allGameObjects.Count; i++)
                {
                    allComponents.AddRange(allGameObjects[i].GetComponentsInChildren<T>(includeInactive));
                }

                return allComponents.ToArray();
            }

            return Object.FindObjectsOfType<T>();
        }

        public static bool IsSimilar(this Vector2 lhs, Vector2 rhs, float threshold)
        {
            return lhs.x.IsSimilar(rhs.x, threshold) && lhs.y.IsSimilar(rhs.y, threshold);
        }

        public static bool IsSimilar(this Vector3 lhs, Vector3 rhs, float threshold)
        {
            return lhs.x.IsSimilar(rhs.x, threshold) && lhs.y.IsSimilar(rhs.y, threshold) &&
                   lhs.z.IsSimilar(rhs.z, threshold);
        }

        public static bool PlayForward(this Animation animation, string name)
        {
            animation[name].speed = 1;

            if (!animation.isPlaying)
            {
                animation[name].normalizedTime = 0;
            }

            return animation.Play(name);
        }

        public static bool PlayBackward(this Animation animation, string name)
        {
            animation[name].speed = -1;

            if (!animation.isPlaying)
            {
                animation[name].normalizedTime = 1;
            }

            return animation.Play(name);
        }
    }
}
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public class PingObject : MonoBehaviour
    {
        [MenuItem("GameObject/Ping Selected")]
        public static void Ping()
        {
            if (!Selection.activeObject)
            {
                Debug.LogError("Select an object to ping");
                return;
            }

            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
}
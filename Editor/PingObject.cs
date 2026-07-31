using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public class PingObject : MonoBehaviour
    {
        [MenuItem("GameObject/Legendary Tools/Diagnostics/Ping Selected")]
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

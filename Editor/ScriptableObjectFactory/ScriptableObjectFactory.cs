using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// A helper class for instantiating ScriptableObjects in the editor.
    /// </summary>
    public class ScriptableObjectFactory
    {
        [MenuItem("Tools/LegendaryTools/ScriptableObject Factory")]
        public static void CreateScriptableObject()
        {
            Assembly assembly = GetAssembly();

            // Get all classes derived from ScriptableObject
            Type[] allScriptableObjects = (from t in assembly.GetTypes()
                where t.IsSubclassOf(typeof(ScriptableObject)) && !t.IsAbstract
                select t).ToArray();

            // Show the selection window.
            ScriptableObjectWindow.Init(allScriptableObjects);
        }

        /// <summary>
        /// Returns the assembly that contains the script code for this project (currently hard coded)
        /// </summary>
        private static Assembly GetAssembly()
        {
            return Assembly.Load(new AssemblyName("Assembly-CSharp"));
        }
    }
}
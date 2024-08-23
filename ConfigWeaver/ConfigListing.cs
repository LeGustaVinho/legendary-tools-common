using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public class ConfigListing<T> : ScriptableObject, IWeaveExec
        where T : ScriptableObject
    {
        public WeaveExecType weaveExecType;
        public WeaveExecType WeaveExecType => weaveExecType;

        public List<T> Configs = new List<T>();
        

        [ContextMenu("RunWeaver")]
        public void RunWeaver()
        {
#if UNITY_EDITOR
            Configs = EditorExtensions.FindAssetsByType<T>();
#endif
        }

    }
}
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
#if !ODIN_INSPECTOR
    [Serializable]
#endif
    public class TypeLogLevel
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ReadOnly]
#else
        [Header("Full type name is: Namespace + Type")]
#endif
        public string TypeFullName;
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.TypeDrawerSettings(Filter = Sirenix.OdinInspector.TypeInclusionFilter.IncludeConcreteTypes)]
        [Sirenix.OdinInspector.OnValueChanged("OnTypeChanged")]
        public Type Type;
#endif
        public DebugLogLevel LogLevel = DebugLogLevel.All;

        private void OnTypeChanged()
        {
            TypeFullName = Type.FullName;
        }
    }

    [Flags]
    public enum DebugLogLevel
    {
        None = 0,
        Trace = 1,
        Info = 2,
        Warning = 4,
        Error = 8,
        Exception = 16,
        All = Trace | Info | Warning | Error | Exception
    }
    
    [CreateAssetMenu(menuName = "Tools/LegendaryTools/DebugFilterConfig", fileName = "DebugFilterConfig", order = 0)]
    public class DebugFilterConfig : UnityObject
    {
        public DebugLogLevel DefaultLogLevel = DebugLogLevel.All;
        public bool PrintClassMethodInfo = true;
        public string ClassMethodInfoFormat = "<b>[{0}:{1}]</b> {2}";
        public string ClassInfoFormat = "<b>[{0}]</b> {1}";
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.TableList]
#endif
        public List<TypeLogLevel> TypeLogLevels = new List<TypeLogLevel>();
    }
}
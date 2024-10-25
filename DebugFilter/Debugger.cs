using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools
{
    public static class Debugger
    {
        public static DebugFilterConfig DebugFilterConfig;
        public static Dictionary<string, TypeLogLevel> TypeLogLevelsLookup = new Dictionary<string, TypeLogLevel>();
        private static bool isInitialized;

        public static void Initialize()
        {
            if (isInitialized) return;
            if (DebugFilterConfig == null) return;
            
            TypeLogLevelsLookup.Clear();
            foreach (TypeLogLevel typeLogLevel in DebugFilterConfig.TypeLogLevels)
            {
                TypeLogLevelsLookup.Add(typeLogLevel.TypeFullName, typeLogLevel);
            }
            isInitialized = true;
        }
        
        public static bool CanLog<T>(DebugLogLevel targetLogLevel)
        {
            if (DebugFilterConfig == null) return true;
            Initialize();
            Type dataType = typeof(T);
            string dataTypeFullName = dataType.FullName;
            if (TypeLogLevelsLookup.TryGetValue(dataTypeFullName, out TypeLogLevel typeLogLevel))
            {
                return typeLogLevel.LogLevel.HasFlags(targetLogLevel);
            }

            TypeLogLevel found = DebugFilterConfig.TypeLogLevels.Find(item => item.TypeFullName == dataTypeFullName);
            if (found == null) return DebugFilterConfig.DefaultLogLevel.HasFlags(targetLogLevel);
            
            TypeLogLevelsLookup.Add(dataTypeFullName, found);
            return found.LogLevel.HasFlags(targetLogLevel);
        }

        public static void Trace<T>(string msg, Object context = null)
        {
            if(CanLog<T>(DebugLogLevel.Trace)) Debug.Log(msg, context);
        }
        
        public static void Log<T>(string msg, Object context = null)
        {
            if(CanLog<T>(DebugLogLevel.Info)) Debug.Log(msg, context);
        }
        
        public static void LogWarning<T>(string msg, Object context = null)
        {
            if(CanLog<T>(DebugLogLevel.Warning)) Debug.LogWarning(msg, context);
        }
        
        public static void LogError<T>(string msg, Object context = null)
        {
            if(CanLog<T>(DebugLogLevel.Error)) Debug.LogError(msg, context);
        }
    }
}
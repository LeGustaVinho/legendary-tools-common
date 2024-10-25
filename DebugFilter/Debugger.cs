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
        private static string TRACE = "<b>[Trace]</b>";
        private const string EMPTY = "";

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
            string dataTypeFullName = typeof(T).FullName;
            if (TypeLogLevelsLookup.TryGetValue(dataTypeFullName, out TypeLogLevel typeLogLevel))
                return typeLogLevel.LogLevel.HasFlags(targetLogLevel);

            TypeLogLevel found = DebugFilterConfig.TypeLogLevels.Find(item => item.TypeFullName == dataTypeFullName);
            if (found == null) return DebugFilterConfig.DefaultLogLevel.HasFlags(targetLogLevel);
            
            TypeLogLevelsLookup.Add(dataTypeFullName, found);
            return found.LogLevel.HasFlags(targetLogLevel);
        }

        public static void Trace<T>(string msg, Object context = null, string method = EMPTY)
        {
            if (CanLog<T>(DebugLogLevel.Trace))
            {
                if (DebugFilterConfig == null)
                    Debug.Log(msg, context);
                else
                {
                    if (DebugFilterConfig.PrintClassMethodInfo)
                    {
                        string className = typeof(T).Name;
                        Debug.Log(TRACE + (string.IsNullOrEmpty(method) 
                            ? string.Format(DebugFilterConfig.ClassInfoFormat, className, msg) 
                            : string.Format(DebugFilterConfig.ClassMethodInfoFormat, className, method, msg)), context);
                    }
                    else
                        Debug.Log(msg, context);
                }
            }
        }
        
        public static void Log<T>(string msg, Object context = null, string method = EMPTY)
        {
            if (CanLog<T>(DebugLogLevel.Info))
            {
                if (DebugFilterConfig == null)
                    Debug.Log(msg, context);
                else
                {
                    if (DebugFilterConfig.PrintClassMethodInfo)
                    {
                        string className = typeof(T).Name;
                        Debug.Log(string.IsNullOrEmpty(method) 
                            ? string.Format(DebugFilterConfig.ClassInfoFormat, className, msg) 
                            : string.Format(DebugFilterConfig.ClassMethodInfoFormat, className, method, msg), context);
                    }
                    else
                        Debug.Log(msg, context);
                }
            }
        }
        
        public static void LogWarning<T>(string msg, Object context = null, string method = EMPTY)
        {
            if (CanLog<T>(DebugLogLevel.Warning))
            {
                if (DebugFilterConfig == null)
                    Debug.LogWarning(msg, context);
                else
                {
                    if (DebugFilterConfig.PrintClassMethodInfo)
                    {
                        string className = typeof(T).Name;
                        Debug.LogWarning(string.IsNullOrEmpty(method) 
                            ? string.Format(DebugFilterConfig.ClassInfoFormat, className, msg) 
                            : string.Format(DebugFilterConfig.ClassMethodInfoFormat, className, method, msg), context);
                    }
                    else
                        Debug.LogWarning(msg, context);
                }
            }
        }
        
        public static void LogError<T>(string msg, Object context = null, string method = EMPTY)
        {
            if (CanLog<T>(DebugLogLevel.Error))
            {
                if (DebugFilterConfig == null)
                    Debug.LogError(msg, context);
                else
                {
                    if (DebugFilterConfig.PrintClassMethodInfo)
                    {
                        string className = typeof(T).Name;
                        Debug.LogError(string.IsNullOrEmpty(method) 
                            ? string.Format(DebugFilterConfig.ClassInfoFormat, className, msg) 
                            : string.Format(DebugFilterConfig.ClassMethodInfoFormat, className, method, msg), context);
                    }
                    else
                        Debug.LogError(msg, context);
                }
            }
        }
        
        public static void LogException<T>(Exception exception, Object context = null)
        {
            if(CanLog<T>(DebugLogLevel.Exception)) Debug.LogException(exception, context);
        }
    }
}
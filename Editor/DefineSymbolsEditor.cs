using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace LegendaryTools.Mothership.Editor
{
    public class DefineSymbolsEditor : EditorWindow
    {
        public static string PlayerPrefsKey = "DefineSymbolsEditor";
        
        private static readonly List<string> defineSymbols = new List<string>()
        {
            "ENABLE_UNITASK",
            "SCREEN_FLOW_SINGLETON",
            "ENABLE_UNITY_IAP",
            "ENABLE_FIREBASE_ANALYTICS",
            "ENABLE_FIREBASE_REMOTE_CONFIG",
            "ENABLE_MAX_ADS",
            "MAX_DEBUG_LOGGING"
        };

        private readonly Dictionary<string, bool> symbolStates = new Dictionary<string, bool>();
        private BuildTargetGroup targetGroup = BuildTargetGroup.Standalone;
        private string newSymbolsName;

        [MenuItem("Tools/LegendaryTools/Define Symbols Editor")]
        public static void ShowWindow()
        {
            GetWindow<DefineSymbolsEditor>("Define Symbols Editor");
        }

        private void OnEnable()
        {
            InitializeSymbolStates();
        }

        private void InitializeSymbolStates()
        {
            BuildTarget currentTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup currentGroup = BuildPipeline.GetBuildTargetGroup(currentTarget);
            targetGroup = currentGroup;
            UpdateSymbolsState();
        }

        private void UpdateSymbolsState()
        {
            if (EditorPrefs.HasKey(PlayerPrefsKey))
            {
                string[] savedSymbols = EditorPrefs.GetString(PlayerPrefsKey).Split(';', StringSplitOptions.RemoveEmptyEntries);

                foreach (string savedSymbol in savedSymbols)
                {
                    if(!defineSymbols.Contains(savedSymbol))
                        defineSymbols.Add(savedSymbol);
                }
            }
            
            string currentSymbols =
                PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(targetGroup));
            
            List<string> symbolsList = currentSymbols.Split(';').ToList();
            symbolStates.Clear();
            foreach (string symbol in symbolsList)
            {
                symbolStates.Add(symbol, true);
            }

            foreach (string defineSymbol in defineSymbols)
            {
                symbolStates.TryAdd(defineSymbol, false);
            }
        }
        
        private void OnGUI()
        {
            GUILayout.Label("Scripting Define Symbols", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Build Target Group:", GUILayout.Width(120));
            targetGroup = (BuildTargetGroup)EditorGUILayout.EnumPopup(targetGroup);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
            
            foreach (string symbol in defineSymbols)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(symbol, GUILayout.Width(400));
                if (symbolStates.TryGetValue(symbol, out bool state))
                {
                    bool newState = EditorGUILayout.Toggle(symbolStates[symbol]);
                    if (newState != state) symbolStates[symbol] = newState;
                }
                else
                    UpdateSymbolsState();
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Space(20);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Sync")) UpdateSymbolsState();
            if (GUILayout.Button("Save")) ApplyDefineSymbols();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Add new Symbol:", GUILayout.Width(120));
            newSymbolsName = EditorGUILayout.TextField(newSymbolsName);
            if (GUILayout.Button("Add New Symbols"))
            {
                defineSymbols.Add(newSymbolsName);
                EditorPrefs.SetString(PlayerPrefsKey, string.Join(';', defineSymbols));
                UpdateSymbolsState();
                newSymbolsName = String.Empty;
            }

            if (GUILayout.Button("Reset Symbols Save"))
            {
                EditorPrefs.DeleteKey(PlayerPrefsKey);
                UpdateSymbolsState();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ApplyDefineSymbols()
        {
            List<string> newSymbols = new List<string>();

            foreach (KeyValuePair<string, bool> kvp in symbolStates)
            {
                if (kvp.Value) newSymbols.Add(kvp.Key);
            }

            string symbolsString = string.Join(";", newSymbols.ToArray());
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(targetGroup), symbolsString);
            EditorUtility.DisplayDialog("Define Symbols", "Unity will compile to apply new symbols...", "OK");
        }
    }
}
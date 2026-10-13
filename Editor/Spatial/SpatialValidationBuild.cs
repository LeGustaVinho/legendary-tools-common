using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LegendaryTools.Deterministic.Editor
{
    public static class SpatialValidationBuild
    {
        public static void BuildPlayers() => Build(true);
        public static void BuildMono() => Build(false);
        private static void Build(bool both)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project for spatial validation.");
            var named = NamedBuildTarget.Standalone; var backend = PlayerSettings.GetScriptingBackend(named);
            const string folder = "Assets/__SpatialValidation__", scene = folder + "/Validation.unity";
            try
            {
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "__SpatialValidation__");
                var empty = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(empty, scene)) throw new IOException("Cannot save the isolated validation scene.");
                foreach (bool il2cpp in both ? new[] { false, true } : new[] { false })
                {
                    PlayerSettings.SetScriptingBackend(named, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
                    string output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/SpatialValidation", il2cpp ? "IL2CPP" : "Mono");
                    Directory.CreateDirectory(output);
                    var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene }, target = BuildTarget.StandaloneWindows64,
                        locationPathName = Path.Combine(output, "SpatialValidation.exe"), options = BuildOptions.Development,
                        extraScriptingDefines = new[] { "LEGENDARY_SPATIAL_VALIDATION" } });
                    if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Spatial player build failed: " + report.summary.result + ", errors=" + report.summary.totalErrors);
                    Debug.Log("Spatial validation player: " + output);
                }
            }
            finally { PlayerSettings.SetScriptingBackend(named, backend); AssetDatabase.DeleteAsset(folder); }
        }
    }
}

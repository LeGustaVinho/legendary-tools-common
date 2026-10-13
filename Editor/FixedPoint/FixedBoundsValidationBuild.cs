using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LegendaryTools.FixedPoint.Editor
{
    /// <summary>Batch-only validation builders. Use a project created by prepare_bounds_validation.py.</summary>
    public static class FixedBoundsValidationBuild
    {
        private static string Project => Directory.GetParent(Application.dataPath).FullName;
        public static void BuildMonoBatch() => Build(false, false);
        public static void BuildIl2CppBatch() => Build(true, false);
        public static void BuildAndroidBatch() => Build(true, true);
        private static void Build(bool il2cpp, bool android)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project for bounds validation.");
            var named = android ? NamedBuildTarget.Android : NamedBuildTarget.Standalone;
            var backend = PlayerSettings.GetScriptingBackend(named); var architectures = PlayerSettings.Android.targetArchitectures;
            string identifier = PlayerSettings.GetApplicationIdentifier(named);
            const string folder = "Assets/__FixedBoundsValidation__", scene = folder + "/Validation.unity";
            try
            {
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "__FixedBoundsValidation__");
                var validation = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(validation, scene)) throw new IOException("Cannot save validation scene.");
                PlayerSettings.SetScriptingBackend(named, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
                if (android) { PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64; PlayerSettings.SetApplicationIdentifier(named, "com.legendarytools.fixedbounds.validation"); }
                string output = Path.Combine(Project, "Builds/FixedBoundsValidation", android ? "Android" : il2cpp ? "IL2CPP" : "Mono"); Directory.CreateDirectory(output);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { scene }, target = android ? BuildTarget.Android : BuildTarget.StandaloneWindows64,
                    locationPathName = Path.Combine(output, android ? "FixedBoundsValidation.apk" : "FixedBoundsValidation.exe"), options = BuildOptions.Development,
                    extraScriptingDefines = android ? new[] { "LEGENDARY_FIXEDPOINT_VALIDATION", "LEGENDARY_FIXEDBOUNDS_VALIDATION_ANDROID" } : new[] { "LEGENDARY_FIXEDPOINT_VALIDATION" }
                });
                if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Bounds validation build failed: " + report.summary.result + " errors=" + report.summary.totalErrors);
                Debug.Log("Bounds validation player: " + output);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(named, backend); PlayerSettings.Android.targetArchitectures = architectures;
                PlayerSettings.SetApplicationIdentifier(named, identifier); AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}

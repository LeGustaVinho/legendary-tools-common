using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendaryTools.FixedPoint.Editor
{
    public static class FixedValidationBuild
    {
        [Serializable] private class BuildStatus
        { public string State, Backend, Output, Error; public double DurationSeconds; }
        private static string ProjectDirectory => Directory.GetParent(Application.dataPath).FullName;
        public static string StatusPath => Path.Combine(ProjectDirectory, "Logs/FixedPointValidation/build-status.json");
        public static void BuildMonoBatch() => BuildWindows(false, Path.Combine(ProjectDirectory, "Builds/FixedPointValidation/Mono"));
        public static void BuildIl2CppBatch() => BuildWindows(true, Path.Combine(ProjectDirectory, "Builds/FixedPointValidation/IL2CPP"));
        public static void BuildAndroidBatch() => BuildAndroidArm64(Path.Combine(ProjectDirectory, "Builds/FixedPointValidation/Android"));

        public static void BuildAndroidArm64(string outputDirectory)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run Android validation in the isolated batch project.");
            var target = NamedBuildTarget.Android;
            ScriptingImplementation previousBackend = PlayerSettings.GetScriptingBackend(target);
            AndroidArchitecture previousArchitectures = PlayerSettings.Android.targetArchitectures;
            string previousIdentifier = PlayerSettings.GetApplicationIdentifier(target);
            var status = new BuildStatus { State = "building", Backend = "Android ARM64 IL2CPP", Output = Path.GetFullPath(outputDirectory) };
            const string folder = "Assets/__FixedPointValidation__", scenePath = folder + "/Validation.unity";
            try
            {
                Save(status); Directory.CreateDirectory(status.Output);
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "__FixedPointValidation__");
                Scene validation = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(validation, scenePath)) throw new IOException("Could not save validation scene.");
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.SetApplicationIdentifier(target, "com.legendarytools.fixedpoint.validation");
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { scenePath }, target = BuildTarget.Android,
                    locationPathName = Path.Combine(status.Output, "FixedPointValidation.apk"),
                    options = BuildOptions.Development,
                    extraScriptingDefines = new[] { "LEGENDARY_FIXEDPOINT_VALIDATION", "LEGENDARY_FIXEDPOINT_VALIDATION_ANDROID" }
                });
                status.DurationSeconds = report.summary.totalTime.TotalSeconds;
                status.State = report.summary.result == BuildResult.Succeeded ? "succeeded" : "failed";
                status.Error = report.summary.result == BuildResult.Succeeded ? null : report.summary.result + " errors=" + report.summary.totalErrors;
            }
            catch (Exception exception) { status.State = "failed"; status.Error = exception.ToString(); }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, previousBackend);
                PlayerSettings.Android.targetArchitectures = previousArchitectures;
                PlayerSettings.SetApplicationIdentifier(target, previousIdentifier);
                AssetDatabase.DeleteAsset(folder); Save(status);
            }
        }

        public static void BuildWindows(bool il2cpp, string outputDirectory)
        {
            if (EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("The Editor is busy.");
            string output = Path.GetFullPath(outputDirectory);
            var status = new BuildStatus { State = "building", Backend = il2cpp ? "IL2CPP" : "Mono", Output = output };
            BuildWindows(il2cpp, output, status);
        }

        public static void ScheduleWindows(bool il2cpp, string outputDirectory)
        {
            if (EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("The Editor is busy.");
            string output = Path.GetFullPath(outputDirectory);
            var status = new BuildStatus { State = "queued", Backend = il2cpp ? "IL2CPP" : "Mono", Output = output };
            Save(status);
            EditorApplication.CallbackFunction callback = null;
            callback = () => { EditorApplication.update -= callback; BuildWindows(il2cpp, output, status); };
            EditorApplication.update += callback;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void BuildWindows(bool il2cpp, string output, BuildStatus status)
        {
            var target = NamedBuildTarget.Standalone;
            ScriptingImplementation previous = PlayerSettings.GetScriptingBackend(target);
            const string folder = "Assets/__FixedPointValidation__";
            const string scenePath = folder + "/Validation.unity";
            Scene oldActive = SceneManager.GetActiveScene(); Scene validation = default;
            try
            {
                status.State = "building"; Save(status);
                Directory.CreateDirectory(output);
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "__FixedPointValidation__");
                validation = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                    Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
                if (!EditorSceneManager.SaveScene(validation, scenePath)) throw new IOException("Could not save validation scene.");
                if (!Application.isBatchMode) EditorSceneManager.CloseScene(validation, true);
                if (oldActive.IsValid() && oldActive.isLoaded) SceneManager.SetActiveScene(oldActive);
                PlayerSettings.SetScriptingBackend(target, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
                var options = new BuildPlayerOptions {
                    scenes = new[] { scenePath }, locationPathName = Path.Combine(output, "FixedPointValidation.exe"),
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development,
                    extraScriptingDefines = new[] { "LEGENDARY_FIXEDPOINT_VALIDATION" }
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                status.DurationSeconds = report.summary.totalTime.TotalSeconds;
                status.State = report.summary.result == BuildResult.Succeeded ? "succeeded" : "failed";
                status.Error = report.summary.result == BuildResult.Succeeded ? null : report.summary.result + " errors=" + report.summary.totalErrors;
            }
            catch (Exception exception) { status.State = "failed"; status.Error = exception.ToString(); }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, previous);
                if (!Application.isBatchMode && validation.IsValid() && validation.isLoaded) EditorSceneManager.CloseScene(validation, true);
                if (oldActive.IsValid() && oldActive.isLoaded) SceneManager.SetActiveScene(oldActive);
                AssetDatabase.DeleteAsset(folder);
                Save(status);
            }
        }
        private static void Save(BuildStatus status)
        { Directory.CreateDirectory(Path.GetDirectoryName(StatusPath)); File.WriteAllText(StatusPath, JsonUtility.ToJson(status, true)); }
    }
}

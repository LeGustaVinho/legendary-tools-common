using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace LegendaryTools.FixedPoint.Editor
{
    /// <summary>Async setup for the isolated Android validation project; quits only after UPM resolves.</summary>
    public static class FixedValidationPackages
    {
        private static AddRequest request;
        private static double deadline;
        public static void EnsureAndroidJniBatch()
        {
            if (!Application.isBatchMode) throw new System.InvalidOperationException("Use the isolated batch validation project.");
            request=Client.Add("com.unity.modules.androidjni@1.0.0"); deadline=EditorApplication.timeSinceStartup+120;
            EditorApplication.update+=Poll;
        }
        private static void Poll()
        {
            if (!request.IsCompleted && EditorApplication.timeSinceStartup<deadline) return;
            EditorApplication.update-=Poll;
            if (request.IsCompleted && request.Status==StatusCode.Success) { Debug.Log("Android validation module resolved: "+request.Result.name+"@"+request.Result.version); EditorApplication.Exit(0); }
            else { Debug.LogError("Android validation module failed: "+(request.Error==null ? "timeout" : request.Error.message)); EditorApplication.Exit(1); }
        }
    }
}

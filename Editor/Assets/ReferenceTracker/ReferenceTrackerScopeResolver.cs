using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Editor
{
    internal sealed class ReferenceTrackerScopeResolver
    {
        public bool IsPrefabModeAvailable
        {
            get { return PrefabStageUtility.GetCurrentPrefabStage() != null; }
        }

        public ReferenceTrackerSearchScope GetCurrentScope()
        {
            return IsPrefabModeAvailable
                ? ReferenceTrackerSearchScope.PrefabMode
                : ReferenceTrackerSearchScope.CurrentScene;
        }

        public ReferenceTrackerSearchScope Normalize(ReferenceTrackerSearchScope scopes)
        {
            if (!IsPrefabModeAvailable)
            {
                scopes &= ~ReferenceTrackerSearchScope.PrefabMode;
            }

            if (scopes == ReferenceTrackerSearchScope.None)
            {
                scopes = ReferenceTrackerSearchScope.CurrentScene;
            }

            return scopes;
        }

        public List<ReferenceTrackerScopeDescriptor> Resolve(ReferenceTrackerSearchScope scopes, out string error)
        {
            scopes = Normalize(scopes);
            error = string.Empty;

            List<ReferenceTrackerScopeDescriptor> descriptors = new List<ReferenceTrackerScopeDescriptor>();

            if ((scopes & ReferenceTrackerSearchScope.CurrentScene) != 0)
            {
                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    error = "Active scene is not valid or not loaded.";
                    return descriptors;
                }

                descriptors.Add(new ReferenceTrackerScopeDescriptor
                {
                    Scope = ReferenceTrackerSearchScope.CurrentScene,
                    Scene = activeScene,
                    Label = string.Format("Scene '{0}'", activeScene.name),
                });
            }

            if ((scopes & ReferenceTrackerSearchScope.PrefabMode) != 0)
            {
                PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage == null)
                {
                    error = "Prefab Mode is not currently open.";
                    return descriptors;
                }

                if (prefabStage.prefabContentsRoot == null)
                {
                    error = "Current Prefab Mode stage has no prefab contents root.";
                    return descriptors;
                }

                Scene prefabScene = prefabStage.prefabContentsRoot.scene;
                if (!prefabScene.IsValid() || !prefabScene.isLoaded)
                {
                    error = "Prefab Mode scene is not valid or not loaded.";
                    return descriptors;
                }

                descriptors.Add(new ReferenceTrackerScopeDescriptor
                {
                    Scope = ReferenceTrackerSearchScope.PrefabMode,
                    Scene = prefabScene,
                    Label = string.Format("Prefab Mode '{0}'", prefabStage.assetPath),
                });
            }

            if (descriptors.Count == 0)
            {
                error = "Select at least one search scope.";
            }

            return descriptors;
        }

        public string GetDescription(ReferenceTrackerSearchScope scopes)
        {
            scopes = Normalize(scopes);
            List<string> descriptions = new List<string>();

            if ((scopes & ReferenceTrackerSearchScope.CurrentScene) != 0)
            {
                Scene activeScene = SceneManager.GetActiveScene();
                descriptions.Add(activeScene.IsValid()
                    ? string.Format("Searching Current Scene: {0}", activeScene.name)
                    : "Searching Current Scene");
            }

            if ((scopes & ReferenceTrackerSearchScope.PrefabMode) != 0)
            {
                PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                descriptions.Add(prefabStage != null
                    ? string.Format("Searching Prefab Mode: {0}", prefabStage.assetPath)
                    : "Searching Prefab Mode: no prefab stage is currently open");
            }

            if (!IsPrefabModeAvailable)
            {
                descriptions.Add("Prefab Mode is available only while a prefab stage is open.");
            }

            return string.Join("\n", descriptions.ToArray());
        }
    }
}

using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Systems.AssetProvider
{
    [CreateAssetMenu(menuName = "Tools/AssetProvider/ResourcesAssetLoadableConfig", fileName = "ResourcesAssetLoadableConfig", order = 0)]
    public class ResourcesAssetLoaderConfig : AssetLoaderConfig
    {
        [SerializeField] protected ResourcePathReference ResourcePathReference;
        
        public override T Load<T>()
        {
            if (IsLoaded || IsInScene)
                return loadedAsset as T;
            
            if (ResourcePathReference.resourcePath.Length > 0)
            {
                T result = Resources.Load<T>(ResourcePathReference.resourcePath);
                loadedAsset = result;
                return result;
            }
            return null;
        }

        public override async Task<ILoadOperation> LoadAsync<T>(Action<T> onComplete = null)
        {
            if (IsLoaded || IsInScene)
            {
                onComplete?.Invoke(loadedAsset as T);
                return handle;
            }
            
            if (ResourcePathReference.resourcePath.Length > 0)
            {
                IsLoading = true;
                ResourceRequest resourcesRequest = Resources.LoadAsync<T>(ResourcePathReference.resourcePath);
                handle = new LoadOperation(resourcesRequest);
                if(onComplete != null) handle.OnCompleted += OnResourceLoadCompleted;

                while (!handle.IsDone)
                {
                    await Task.Delay(25);
                }

                loadedAsset = handle.Result;
                IsLoading = false;
                return handle;
            }

            return null;
            void OnResourceLoadCompleted(object obj)
            {
                onComplete?.Invoke(obj as T);
            }
        }

        public override ILoadOperation PrepareLoadRoutine<T>(Action<T> onComplete = null)
        {
            if (IsLoaded || IsInScene)
            {
                onComplete?.Invoke(loadedAsset as T);
                return handle;
            }
            
            IsLoading = true;
            ResourceRequest resourcesRequest = Resources.LoadAsync<T>(ResourcePathReference.resourcePath);
            handle = new LoadOperation(resourcesRequest);
            handle.OnCompleted += OnResourceLoadCompleted;
            return handle;
            void OnResourceLoadCompleted(object obj)
            {
                loadedAsset = handle.Result;
                IsLoading = false;
                onComplete?.Invoke(obj as T);
            }
        }

        public override IEnumerator WaitLoadRoutine()
        {
            if (handle == null)
            {
                Debug.LogError($"[{nameof(ResourcesAssetLoaderConfig)}:{nameof(WaitLoadRoutine)}] Handle is null, did you forget to call {nameof(PrepareLoadRoutine)}() ?");
                yield return null;
            }
            
            while (!handle.IsDone)
            {
                yield return null;
            }
        }

        public override ILoadOperation LoadWithCoroutines<T>(Action<T> onComplete)
        {
            ILoadOperation loadOperation = PrepareLoadRoutine<T>(onComplete);
            MonoBehaviourFacade.Instance.StartRoutine(WaitLoadRoutine());
            return loadOperation;
        }

        public override void Unload()
        {
            if (IsInScene) return;
            if (handle == null) return;
            
            loadedAsset = null;
            handle?.Release();
            handle = null;
            IsLoading = false;
        }
    }
}
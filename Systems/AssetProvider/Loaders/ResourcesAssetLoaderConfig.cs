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

        public override async Task<ILoadOperation> LoadAsync<T>()
        {
            if (IsLoaded || IsInScene)
                return handle;
            
            if (ResourcePathReference.resourcePath.Length > 0)
            {
                IsLoading = true;
                ResourceRequest resourcesRequest = Resources.LoadAsync<T>(ResourcePathReference.resourcePath);
                handle = new LoadOperation(resourcesRequest);

                while (!handle.IsDone)
                {
                    await Task.Delay(25);
                }

                loadedAsset = handle.Result;
                IsLoading = false;
                return handle;
            }

            return null;
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
using System;
using System.Collections;
using System.Threading;
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

        public override async Task<ILoadOperation> LoadAsync<T>(CancellationToken cancellationToken = default)
        {
            if (IsLoaded || IsInScene)
                return Handle;
            
            IsLoading = true;
            ResourceRequest resourcesRequest = Resources.LoadAsync<T>(ResourcePathReference.resourcePath);
            Handle = new LoadOperation(resourcesRequest, asyncWaitBackend, cancellationToken);
            await Handle.Await<T>();
            loadedAsset = Handle.Result;
            IsLoading = false;
            return Handle;
        }

        public override void Unload()
        {
            if (IsInScene) return;
            if (Handle == null) return;
            
            loadedAsset = null;
            Handle?.Release();
            Handle = null;
            IsLoading = false;
        }
    }
}
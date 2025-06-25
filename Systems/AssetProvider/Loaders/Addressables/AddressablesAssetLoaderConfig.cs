using UnityEngine;
using UnityEngine.AddressableAssets;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace LegendaryTools.Systems.AssetProvider
{
    [CreateAssetMenu(menuName = "Tools/AssetProvider/AddressablesAssetLoaderConfig", fileName = "AddressablesAssetLoaderConfig", order = 0)]
    public class AddressablesAssetLoaderConfig : AssetLoaderConfig
    {
        [SerializeField] protected AssetReference assetReference;
        
        public override T Load<T>()
        {
            throw new NotSupportedException();
        }

        public override async Task<ILoadOperation> LoadAsync<T>(CancellationToken cancellationToken = default)
        {
            if (IsLoaded || IsInScene)
                return Handle;
            
            IsLoading = true;
            AsyncOperationHandle<T> request = assetReference.LoadAssetAsync<T>();
            Handle = new LoadOperation(request, asyncWaitBackend, cancellationToken);
            await Handle.Await<T>();
            loadedAsset = Handle.Result;
            IsLoading = false;
            return Handle;
        }

        public override void Unload()
        {
            if (IsInScene) return;
            if (Handle == null) return;
            
            Handle.Release();
            loadedAsset = null;
            Handle = null;
            IsLoading = false;
        }
    }
    
    public class AddressablesAssetLoaderConfig<T> : AddressablesAssetLoaderConfig
        where T : UnityEngine.Object
    {
        [SerializeField] protected AssetReferenceT<T> assetReference;

        public override async Task<ILoadOperation> LoadAsync<T1>(CancellationToken cancellationToken = default)
        {
            if (IsLoaded || IsInScene)
                return Handle;
            
            IsLoading = true;
            AsyncOperationHandle<T> request = assetReference.LoadAssetAsync<T>();
            Handle = new LoadOperation(request, asyncWaitBackend);
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
            Handle.Release();
            Handle = null;
            IsLoading = false;
        }
    }
}
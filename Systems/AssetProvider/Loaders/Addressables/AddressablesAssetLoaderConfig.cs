using UnityEngine;
using UnityEngine.AddressableAssets;
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;
using LegendaryTools.Concurrency;

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

        public override async Task<ILoadOperation> LoadAsync<T>()
        {
            if (IsLoaded || IsInScene)
                return handle;
            
            IsLoading = true;
            AsyncOperationHandle<T> request = assetReference.LoadAssetAsync<T>();
            handle = new LoadOperation(request);
            await handle.Await<T>();
            loadedAsset = handle.Result;
            IsLoading = false;
            return handle;
        }

        public override void Unload()
        {
            if (IsInScene) return;
            if (handle == null) return;
            
            handle.Release();
            loadedAsset = null;
            handle = null;
            IsLoading = false;
        }
    }
    
    public class AddressablesAssetLoaderConfig<T> : AddressablesAssetLoaderConfig
        where T : UnityEngine.Object
    {
        [SerializeField] protected AssetReferenceT<T> assetReference;

        public override async Task<ILoadOperation> LoadAsync<T1>()
        {
            if (IsLoaded || IsInScene)
                return handle;
            
            IsLoading = true;
            AsyncOperationHandle<T> request = assetReference.LoadAssetAsync<T>();
            handle = new LoadOperation(request);
            await handle.Await<T>();
            loadedAsset = handle.Result;
            IsLoading = false;
            return handle;
        }

        public override void Unload()
        {
            if (IsInScene) return;
            if (handle == null) return;
            
            loadedAsset = null;
            handle.Release();
            handle = null;
            IsLoading = false;
        }
    }
}
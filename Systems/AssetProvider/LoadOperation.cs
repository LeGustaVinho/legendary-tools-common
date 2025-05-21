using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace LegendaryTools.Systems.AssetProvider
{
    public interface ILoadOperation
    {
        bool IsDone { get; }
        object Result { get; }
        float Progress { get; }
        event Action<object> OnCompleted;
        void Release();
    }

    public class LoadOperation : ILoadOperation
    {
        private enum OperationType
        {
            Addressable,
            AssetBundle,
            Resource,
        }
        
        public event Action<object> OnCompleted;

        public float Progress
        {
            get
            {
                return operationType switch
                {
                    OperationType.AssetBundle => assetBundleRequest.progress,
                    OperationType.Addressable => addressableOperation.GetDownloadStatus().Percent,
                    OperationType.Resource => resourceOperation.progress,
                    _ => 0
                };
            }
        }
        
        public bool IsDone
        {
            get
            {
                return operationType switch
                {
                    OperationType.AssetBundle => assetBundleRequest.isDone,
                    OperationType.Addressable => addressableOperation.IsDone,
                    OperationType.Resource => resourceOperation.isDone,
                    _ => false
                };
            }
        }
        
        public object Result
        {
            get
            {
                return operationType switch
                {
                    OperationType.AssetBundle => assetBundleRequest.asset,
                    OperationType.Addressable => addressableOperation.Result,
                    OperationType.Resource => resourceOperation.asset,
                    _ => false
                };
            }
        }

        private readonly OperationType operationType;
        private AsyncOperationHandle addressableOperation;
        private readonly ResourceRequest resourceOperation;
        private readonly AssetBundleRequest assetBundleRequest;

        public LoadOperation(AsyncOperationHandle operation)
        {
            addressableOperation = operation;
            operationType = OperationType.Addressable;
            addressableOperation.Completed += OnAddressableLoadCompleted;
        }

        public LoadOperation(ResourceRequest operation)
        {
            resourceOperation = operation;
            operationType = OperationType.Resource;
            resourceOperation.completed += OnResourceLoadCompleted;
        }
        
        public LoadOperation(AssetBundleRequest operation)
        {
            assetBundleRequest = operation;
            operationType = OperationType.AssetBundle;
            assetBundleRequest.completed += OnAssetBundleLoadCompleted;
        }

        public void Release()
        {
            switch (operationType)
            {
                case OperationType.Addressable: Addressables.Release(addressableOperation); break;
                case OperationType.AssetBundle: break; //Dont makes sense for AssetBundles
                case OperationType.Resource:
                    bool isGameObjectOrComponent =
                        resourceOperation.asset is GameObject || resourceOperation.asset is Component;

                    if (isGameObjectOrComponent)
                        Resources.UnloadUnusedAssets();
                    else
                        Resources.UnloadAsset(resourceOperation.asset); 
                    break;
            }
        }

        private void OnAssetBundleLoadCompleted(AsyncOperation asyncOperation)
        {
            if (assetBundleRequest.asset == null)
            {
                Debug.LogError("[LoadOperation:OnAssetBundleLoadCompleted] Asset not found");
            }
            
            OnCompleted?.Invoke(assetBundleRequest.asset as UnityEngine.Object);
        }

        private void OnAddressableLoadCompleted(AsyncOperationHandle handle)
        {
            if (handle.Result == null)
            {
                Debug.LogError("[LoadOperation:OnAddressableLoadCompleted] Asset not found");
            }
            
            OnCompleted?.Invoke(handle.Result);
        }

        private void OnResourceLoadCompleted(AsyncOperation asyncOperation)
        {
            if (resourceOperation.asset == null)
            {
                Debug.LogError("[LoadOperation:OnResourceLoadCompleted] Asset not found");
            }
            
            OnCompleted?.Invoke(resourceOperation.asset as UnityEngine.Object);
        }
    }
}
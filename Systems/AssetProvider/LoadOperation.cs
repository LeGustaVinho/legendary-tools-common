using System;
using System.Threading;
using System.Threading.Tasks;
using LegendaryTools.Concurrency;
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
        Task<T> Await<T>() where T : UnityEngine.Object;
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
                    OperationType.AssetBundle => assetBundleRequest?.progress ?? 0f,
                    OperationType.Addressable => 
                        addressableOperation.IsValid() && addressableOperation.Status != AsyncOperationStatus.Failed 
                            ? addressableOperation.GetDownloadStatus().Percent 
                            : 0f,
                    OperationType.Resource => resourceOperation?.progress ?? 0f,
                    _ => 0f
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
                    OperationType.Addressable => addressableOperation.IsDone && addressableOperation.Status == AsyncOperationStatus.Succeeded ? addressableOperation.Result : null,
                    OperationType.Resource => resourceOperation.asset,
                    _ => null
                };
            }
        }

        private readonly OperationType operationType;
        private AsyncOperationHandle addressableOperation;
        private readonly ResourceRequest resourceOperation;
        private readonly AssetBundleRequest assetBundleRequest;
        private readonly AsyncWaitBackend asyncWaitBackend;
        private readonly CancellationToken cancellationToken;

        public AssetBundle Bundle { get; }

        public LoadOperation(AsyncOperationHandle operation, AsyncWaitBackend asyncWaitBackend, 
            CancellationToken cancellationToken = default)
        {
            addressableOperation = operation;
            operationType = OperationType.Addressable;
            this.asyncWaitBackend = asyncWaitBackend;
            this.cancellationToken = cancellationToken;
            addressableOperation.Completed += OnAddressableLoadCompleted;
        }

        public LoadOperation(ResourceRequest operation, AsyncWaitBackend asyncWaitBackend, 
            CancellationToken cancellationToken = default)
        {
            resourceOperation = operation;
            operationType = OperationType.Resource;
            this.asyncWaitBackend = asyncWaitBackend;
            this.cancellationToken = cancellationToken;
            resourceOperation.completed += OnResourceLoadCompleted;
        }
        
        public LoadOperation(AssetBundleRequest operation, AssetBundle bundle, AsyncWaitBackend asyncWaitBackend, 
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            if (bundle == null)
                throw new ArgumentNullException(nameof(bundle));
            assetBundleRequest = operation;
            Bundle = bundle;
            operationType = OperationType.AssetBundle;
            this.asyncWaitBackend = asyncWaitBackend;
            this.cancellationToken = cancellationToken;
            assetBundleRequest.completed += OnAssetBundleLoadCompleted;
        }

        public async Task<T> Await<T>()
            where T : UnityEngine.Object
        {
            try
            {
                switch (operationType)
                {
                    case OperationType.Addressable:
                        await AsyncWait.ForAsync(AsyncAction, asyncWaitBackend, cancellationToken);
                        return addressableOperation.Result as T;

                    case OperationType.AssetBundle:
                        await AsyncWait.ForAsyncOperation(assetBundleRequest, asyncWaitBackend, cancellationToken);
                        return assetBundleRequest.asset as T;

                    case OperationType.Resource:
                        await AsyncWait.ForAsyncOperation(resourceOperation, asyncWaitBackend, cancellationToken);
                        return resourceOperation.asset as T;

                    default:
                        throw new InvalidOperationException("Unknown operation type.");
                }
            }
            catch (OperationCanceledException)
            {
                Release();
                throw;
            }
        }

        private Task<object> AsyncAction(AsyncWaitTaskContext context)
        {
            return addressableOperation.Task;
        }

        public void Release()
        {
            switch (operationType)
            {
                case OperationType.Addressable: 
                    Addressables.Release(addressableOperation); 
                    break;
                case OperationType.AssetBundle:
                    if (Bundle != null)
                        Bundle.Unload(false);
                    break;
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
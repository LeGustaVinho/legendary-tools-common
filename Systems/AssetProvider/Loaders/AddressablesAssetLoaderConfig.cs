using UnityEngine;
using UnityEngine.AddressableAssets;
using System;
using System.Collections;
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

        public override async Task<ILoadOperation> LoadAsync<T>(Action<T> onComplete = null)
        {
            if (IsLoaded || IsInScene)
            {
                onComplete?.Invoke(loadedAsset as T);
                return handle;
            }
            
            IsLoading = true;
            AsyncOperationHandle<T> request = assetReference.LoadAssetAsync<T>();
            handle = new LoadOperation(request);
            if(onComplete != null) handle.OnCompleted += OnAddressableLoadCompleted;
            
            while (!handle.IsDone) await Task.Delay(25);
            
            loadedAsset = handle.Result;
            IsLoading = false;
            return handle;
            
            void OnAddressableLoadCompleted(object obj)
            {
                handle.OnCompleted -= OnAddressableLoadCompleted;
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
            AsyncOperationHandle<T> request = assetReference.LoadAssetAsync<T>();
            handle = new LoadOperation(request);
            handle.OnCompleted += OnAddressableLoadCompleted;
            return handle;
            
            void OnAddressableLoadCompleted(object obj)
            {
                handle.OnCompleted -= OnAddressableLoadCompleted;
                loadedAsset = handle.Result;
                IsLoading = false;
                onComplete?.Invoke(obj as T);
            }
        }

        public override IEnumerator WaitLoadRoutine()
        {
            if (handle == null)
            {
                Debug.LogError($"[{nameof(AddressablesAssetLoaderConfig)}:{nameof(WaitLoadRoutine)}] Handle is null, did you forget to call {nameof(PrepareLoadRoutine)}() ?");
                yield return null;
            }
            
            while (!handle.IsDone) yield return null;
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
            
            handle.Release();
            loadedAsset = null;
            handle = null;
            IsLoading = false;
        }
    }
    
    public class AddressablesAssetLoaderConfig<T> : AssetLoaderConfig
        where T : UnityEngine.Object
    {
        [SerializeField] protected AssetReferenceT<T> assetReference;
        
        public override T1 Load<T1>()
        {
            throw new NotSupportedException();
        }

        public override async Task<ILoadOperation> LoadAsync<T1>(Action<T1> onComplete = null)
        {
            if (IsLoaded || IsInScene)
            {
                onComplete?.Invoke(loadedAsset as T1);
                return handle;
            }
            
            IsLoading = true;
            AsyncOperationHandle<T> request = assetReference.LoadAssetAsync<T>();
            handle = new LoadOperation(request);
            if(onComplete != null) handle.OnCompleted += OnAddressableLoadCompleted;
            
            while (!handle.IsDone) await Task.Delay(25);
            
            loadedAsset = handle.Result;
            IsLoading = false;
            return handle;
            
            void OnAddressableLoadCompleted(object obj)
            {
                handle.OnCompleted -= OnAddressableLoadCompleted;
                onComplete?.Invoke(obj as T1);
            }
        }

        public override ILoadOperation PrepareLoadRoutine<T1>(Action<T1> onComplete = null)
        {
            if (IsLoaded || IsInScene)
            {
                onComplete?.Invoke(loadedAsset as T1);
                return handle;
            }
            
            IsLoading = true;
            AsyncOperationHandle<T> request = assetReference.LoadAssetAsync<T>();
            handle = new LoadOperation(request);
            handle.OnCompleted += OnAddressableLoadCompleted;
            return handle;
            
            void OnAddressableLoadCompleted(object obj)
            {
                handle.OnCompleted -= OnAddressableLoadCompleted;
                loadedAsset = handle.Result;
                IsLoading = false;
                onComplete?.Invoke(obj as T1);
            }
        }

        public override IEnumerator WaitLoadRoutine()
        {
            if (handle == null)
            {
                Debug.LogError($"[{nameof(AddressablesAssetLoaderConfig)}:{nameof(WaitLoadRoutine)}] Handle is null, did you forget to call {nameof(PrepareLoadRoutine)}() ?");
                yield return null;
            }
            
            while (!handle.IsDone) yield return null;
        }

        public override ILoadOperation LoadWithCoroutines<T1>(Action<T1> onComplete)
        {
            ILoadOperation loadOperation = PrepareLoadRoutine<T1>(onComplete);
            MonoBehaviourFacade.Instance.StartRoutine(WaitLoadRoutine());
            return loadOperation;
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
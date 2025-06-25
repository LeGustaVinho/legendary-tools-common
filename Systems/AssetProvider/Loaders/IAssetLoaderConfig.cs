using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using LegendaryTools.Concurrency;

namespace LegendaryTools.Systems.AssetProvider
{
    public interface IAssetLoaderConfig
    {
        bool PreLoad { get; set; }
        bool DontUnloadAfterLoad { get; set; }
        AsyncWaitBackend AsyncWaitBackend { get; set; }
        bool IsInScene { get; } //Flag used to identify that this asset does not need load/unload because it is serialized in the scene
        object LoadedAsset { get; }
        bool IsLoaded { get; }
        bool IsLoading { get; }
        T Load<T>() where T : UnityEngine.Object;
        Task<ILoadOperation> LoadAsync<T>(CancellationToken cancellationToken = default)
            where T : UnityEngine.Object;

        Task<ILoadOperation> WaitForLoadingAsync<T>(CancellationToken cancellationToken = default)
            where T : UnityEngine.Object;
        
        void Unload();
        void SetAsSceneAsset(UnityEngine.Object sceneInstanceInScene);
        void ClearLoadedAssetRef();
    }
}
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using LegendaryTools.Concurrency;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Systems.AssetProvider
{
    public abstract class AssetLoaderConfig : UnityObject, IAssetLoaderConfig
    {
        [SerializeField] protected bool preload;
        [SerializeField] protected bool dontUnloadAfterLoad;
        [SerializeField] protected AsyncWaitBackend asyncWaitBackend;

        protected object loadedAsset;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
        [Sirenix.OdinInspector.ReadOnly]
#endif
        public ILoadOperation Handle { get; protected set; }

        public virtual bool PreLoad
        {
            get => preload;
            set => preload = value;
        }

        public virtual bool DontUnloadAfterLoad
        {
            get => dontUnloadAfterLoad;
            set => dontUnloadAfterLoad = value;
        }

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public AsyncWaitBackend AsyncWaitBackend
        {
            get => asyncWaitBackend;
            set => asyncWaitBackend = value;
        }

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
        [Sirenix.OdinInspector.ReadOnly]
#endif
        public virtual object LoadedAsset => loadedAsset;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
        [Sirenix.OdinInspector.ReadOnly]
#endif
        public virtual bool
            IsInScene
        {
            protected set;
            get;
        } //Flag used to identify that this asset does not need load/unload because it is serialized in the scene

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
        [Sirenix.OdinInspector.ReadOnly]
#endif
        public virtual bool IsLoaded => loadedAsset != null;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
        [Sirenix.OdinInspector.ReadOnly]
#endif
        public virtual bool IsLoading { protected set; get; } = false;

        public abstract T Load<T>() where T : Object;

        public abstract Task<ILoadOperation> LoadAsync<T>(CancellationToken cancellationToken = default)
            where T : Object;

        public virtual async Task<ILoadOperation> WaitForLoadingAsync<T>(CancellationToken cancellationToken = default)
            where T : Object
        {
            if (IsLoaded || IsInScene)
                return Handle;

            if (IsLoading && Handle != null)
            {
                await Handle.Await<T>();
                return Handle;
            }

            // Se não está carregando, inicia o carregamento
            return await LoadAsync<T>(cancellationToken);
        }

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.HideInEditorMode]
        [Sirenix.OdinInspector.Button]
#endif
        public abstract void Unload();

        public void SetAsSceneAsset(Object sceneInstanceInScene)
        {
            loadedAsset = sceneInstanceInScene;
        }

        public void ClearLoadedAssetRef()
        {
            loadedAsset = null;
        }

        private void OnLoadAssetAsync(object asset)
        {
            loadedAsset = asset;
            IsLoading = false;
        }
    }
}
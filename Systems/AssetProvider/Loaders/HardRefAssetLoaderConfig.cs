using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace LegendaryTools.Systems.AssetProvider
{
    public class HardRefLoadedAsset : ILoadOperation
    {
        public bool IsDone => true;
        public object Result { get; private set; }
        public float Progress => 1;
        #pragma warning disable CS0067
        public event Action<object> OnCompleted;
        #pragma warning restore CS0067
        
        public HardRefLoadedAsset(object result)
        {
            Result = result;
        }
        
        public void Release()
        {
        }

        public async Task<T> Await<T>() where T : Object
        {
            return Result as T;
        }
    }
    
    [CreateAssetMenu(menuName = "Tools/AssetProvider/HardRefAssetLoaderConfig", fileName = "HardRefAssetLoaderConfig", order = 0)]
    public class HardRefAssetLoaderConfig : AssetLoaderConfig
    {
        [SerializeField] protected Object HardReference;

        public override T Load<T>()
        {
            loadedAsset = HardReference;
            return loadedAsset as T;
        }

        public override async Task<ILoadOperation> LoadAsync<T>(CancellationToken cancellationToken = default)
        {
            HardRefLoadedAsset handle = new HardRefLoadedAsset(HardReference);
            loadedAsset = HardReference;
            return handle;
        }

        public override void Unload()
        {
        }
    }
    
    public class HardRefAssetLoaderConfigT<T> : AssetLoaderConfig
        where T : UnityEngine.Object
    {
        [SerializeField] protected T HardReference;

        public override T1 Load<T1>()
        {
            return HardReference as T1;
        }

        public override async Task<ILoadOperation> LoadAsync<T1>(CancellationToken cancellationToken = default)
        {
            HardRefLoadedAsset handle = new HardRefLoadedAsset(HardReference);
            loadedAsset = HardReference;
            return handle;
        }

        public override void Unload()
        {
        }
    }
}
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
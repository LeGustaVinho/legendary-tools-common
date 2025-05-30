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
        public event Action<object> OnCompleted;
        public void Release()
        {
        }

        public async Task<T> Await<T>() where T : Object
        {
            return Result as T;
        }

        public HardRefLoadedAsset(object result)
        {
            Result = result;
        }
    }
    
    [CreateAssetMenu(menuName = "Tools/AssetProvider/HardRefAssetLoaderConfig", fileName = "HardRefAssetLoaderConfig", order = 0)]
    public class HardRefAssetLoaderConfig : AssetLoaderConfig
    {
        [SerializeField] protected Object HardReference;

        public override T Load<T>()
        {
            return HardReference as T;
        }

        public override async Task<ILoadOperation> LoadAsync<T>(CancellationToken cancellationToken = default)
        {
            return new HardRefLoadedAsset(HardReference);
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
            return new HardRefLoadedAsset(HardReference);
        }

        public override void Unload()
        {
        }
    }
}
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
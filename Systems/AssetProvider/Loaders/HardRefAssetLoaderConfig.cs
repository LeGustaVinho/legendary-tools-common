using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Systems.AssetProvider
{ 
    [CreateAssetMenu(menuName = "Tools/AssetProvider/HardRefAssetLoaderConfig", fileName = "HardRefAssetLoaderConfig", order = 0)]
    public class HardRefAssetLoaderConfig : AssetLoaderConfig
    {
        [SerializeField] protected Object HardReference;

        public override T Load<T>()
        {
            return HardReference as T;
        }

        public override Task<ILoadOperation> LoadAsync<T>(Action<T> onComplete = null)
        {
            onComplete?.Invoke(HardReference as T);
            return null;
        }

        public override ILoadOperation PrepareLoadRoutine<T>(Action<T> onComplete = null)
        {
            onComplete?.Invoke(HardReference as T);
            return null;
        }

        public override IEnumerator WaitLoadRoutine()
        {
            yield return null;
        }

        public override ILoadOperation LoadWithCoroutines<T>(Action<T> onComplete)
        {
            onComplete?.Invoke(HardReference as T);
            return null;
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

        public override Task<ILoadOperation> LoadAsync<T1>(Action<T1> onComplete = null)
        {
            onComplete?.Invoke(HardReference as T1);
            return null;
        }

        public override ILoadOperation PrepareLoadRoutine<T1>(Action<T1> onComplete = null)
        {
            onComplete?.Invoke(HardReference as T1);
            return null;
        }

        public override IEnumerator WaitLoadRoutine()
        {
            yield return null;
        }

        public override ILoadOperation LoadWithCoroutines<T1>(Action<T1> onComplete)
        {
            onComplete?.Invoke(HardReference as T1);
            return null;
        }

        public override void Unload()
        {
        }
    }
}
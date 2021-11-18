using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = System.Object;

namespace LegendaryTools
{
    public class CoroutineBehaviour : MonoBehaviour
    {}
    
    public class CoroutineManager : Singleton<CoroutineManager>
    {
        private readonly CoroutineBehaviour coroutineBehaviour;

        public CoroutineManager()
        {
            coroutineBehaviour = new GameObject("CoroutineManager").AddComponent<CoroutineBehaviour>();
            UnityEngine.Object.DontDestroyOnLoad(coroutineBehaviour);
        }

        public Coroutine StartCoroutine(IEnumerator routine)
        {
            return coroutineBehaviour.StartCoroutine(routine);
        }
        
        public void StopCoroutine(IEnumerator routine)
        {
            coroutineBehaviour.StopCoroutine(routine);
        }
        
        public void StopCoroutine(Coroutine routine)
        {
            coroutineBehaviour.StopCoroutine(routine);
        }
    }
}
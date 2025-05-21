using System.Collections;
using UnityEngine;

namespace LegendaryTools.Actor
{
    public interface IMonoBehaviour : IBehaviour
    {
        void CancelInvoke();

        void CancelInvoke(string methodName);
        
        void Invoke(string methodName, float time);
        
        void InvokeRepeating(string methodName, float time, float repeatRate);
        
        bool IsInvoking(string methodName);
        
        Coroutine StartCoroutine(IEnumerator routine);
        
        Coroutine StartCoroutine(string methodName, object value = null);
        
        void StopAllCoroutines();
        
        void StopCoroutine(string methodName);
        
        void StopCoroutine(IEnumerator routine);
        
        void StopCoroutine(Coroutine routine);
    }
}
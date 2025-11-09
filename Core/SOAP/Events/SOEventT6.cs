using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.SOAP.Events
{
    /// <summary>
    /// ScriptableObject event with 6 payload parameters.
    /// Note: UnityEvent does not support 6 generic args in the Inspector. Use code listeners.
    /// </summary>
    public abstract class SOEvent<T1, T2, T3, T4, T5, T6> : SOEventBase
    {
        private readonly List<Action<T1, T2, T3, T4, T5, T6>> _listeners = new();
        private readonly List<Action<T1, T2, T3, T4, T5, T6>> _once = new();

        public override int ListenerCount => _listeners.Count + _once.Count;

        public void AddListener(Action<T1, T2, T3, T4, T5, T6> handler)
        {
            if (handler == null) return;
            if (!_listeners.Contains(handler)) _listeners.Add(handler);
        }

        public void AddListenerOnce(Action<T1, T2, T3, T4, T5, T6> handler)
        {
            if (handler == null) return;
            if (!_once.Contains(handler)) _once.Add(handler);
        }

        public void RemoveListener(Action<T1, T2, T3, T4, T5, T6> handler)
        {
            if (handler == null) return;
            _listeners.Remove(handler);
            _once.Remove(handler);
        }

        public override void RemoveAllListeners()
        {
            _listeners.Clear();
            _once.Clear();
        }

        public void Raise(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6)
        {
            using (BeginRaiseScope())
            {
                Action<T1, T2, T3, T4, T5, T6>[] a = _listeners.Count > 0
                    ? _listeners.ToArray()
                    : Array.Empty<Action<T1, T2, T3, T4, T5, T6>>();
                Action<T1, T2, T3, T4, T5, T6>[] b = _once.Count > 0
                    ? _once.ToArray()
                    : Array.Empty<Action<T1, T2, T3, T4, T5, T6>>();

                for (int i = 0; i < a.Length; i++)
                {
                    TryInvoke(a[i], a1, a2, a3, a4, a5, a6);
                }

                for (int i = 0; i < b.Length; i++)
                {
                    TryInvoke(b[i], a1, a2, a3, a4, a5, a6);
                }

                if (_once.Count > 0) _once.Clear();
            }
        }

        private static void TryInvoke(Action<T1, T2, T3, T4, T5, T6> act,
            T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6)
        {
            try
            {
                act?.Invoke(p1, p2, p3, p4, p5, p6);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
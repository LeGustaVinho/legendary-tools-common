using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// ScriptableObject event with 1 payload parameter.
    /// </summary>
    /// <typeparam name="T1">Payload type.</typeparam>
    public abstract class SOEvent<T1> : SOEventBase
    {
        private readonly List<Action<T1>> _listeners = new();
        private readonly List<Action<T1>> _once = new();

        public override int ListenerCount => _listeners.Count + _once.Count;

        public void AddListener(Action<T1> handler)
        {
            if (handler == null) return;
            if (!_listeners.Contains(handler)) _listeners.Add(handler);
        }

        public void AddListenerOnce(Action<T1> handler)
        {
            if (handler == null) return;
            if (!_once.Contains(handler)) _once.Add(handler);
        }

        public void RemoveListener(Action<T1> handler)
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

        public void Raise(T1 arg1)
        {
            using (BeginRaiseScope())
            {
                Action<T1>[] a = _listeners.Count > 0 ? _listeners.ToArray() : Array.Empty<Action<T1>>();
                Action<T1>[] b = _once.Count > 0 ? _once.ToArray() : Array.Empty<Action<T1>>();

                for (int i = 0; i < a.Length; i++)
                {
                    TryInvoke(a[i], arg1);
                }

                for (int i = 0; i < b.Length; i++)
                {
                    TryInvoke(b[i], arg1);
                }

                if (_once.Count > 0) _once.Clear();
            }
        }

        private static void TryInvoke(Action<T1> act, T1 p1)
        {
            try
            {
                act?.Invoke(p1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
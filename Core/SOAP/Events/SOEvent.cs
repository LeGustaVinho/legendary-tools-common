using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Parameterless ScriptableObject event.
    /// </summary>
    [CreateAssetMenu(fileName = "SOEvent", menuName = "Tools/SOAP/Events/Event (0)")]
    public class SOEvent : SOEventBase
    {
        private readonly List<Action> _listeners = new();
        private readonly List<Action> _once = new();

        public override int ListenerCount => _listeners.Count + _once.Count;

        /// <summary>Adds a listener.</summary>
        public void AddListener(Action handler)
        {
            if (handler == null) return;
            if (!_listeners.Contains(handler)) _listeners.Add(handler);
        }

        /// <summary>Adds a one-shot listener (removed after first raise).</summary>
        public void AddListenerOnce(Action handler)
        {
            if (handler == null) return;
            if (!_once.Contains(handler)) _once.Add(handler);
        }

        /// <summary>Removes a listener.</summary>
        public void RemoveListener(Action handler)
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

        /// <summary>Raises the event.</summary>
        public void Raise()
        {
            using (BeginRaiseScope())
            {
                // Snapshot to avoid modification during iteration
                Action[] a = _listeners.Count > 0 ? _listeners.ToArray() : Array.Empty<Action>();
                Action[] b = _once.Count > 0 ? _once.ToArray() : Array.Empty<Action>();

                for (int i = 0; i < a.Length; i++)
                {
                    TryInvoke(a[i]);
                }

                for (int i = 0; i < b.Length; i++)
                {
                    TryInvoke(b[i]);
                }

                // Clear one-shots after invocation
                if (_once.Count > 0) _once.Clear();
            }
        }

        private static void TryInvoke(Action act)
        {
            try
            {
                act?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
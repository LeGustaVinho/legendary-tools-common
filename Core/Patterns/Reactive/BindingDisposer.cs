using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Holds disposables and disposes them on OnDestroy. 
    /// Note: It does not auto-unbind on OnDisable — this is handled by BindingAnchor + BindingHandle.
    /// </summary>
    public sealed class BindingDisposer : MonoBehaviour, IDisposable
    {
        private readonly List<IDisposable> _disposables = new();
        private bool _disposed;

        public void Add(IDisposable disposable)
        {
            if (disposable == null) return;
            if (_disposed)
            {
                disposable.Dispose();
                return;
            }

            _disposables.Add(disposable);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    _disposables[i]?.Dispose();
                }
                catch
                {
                    /* swallow */
                }
            }

            _disposables.Clear();
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> servicesTable = new Dictionary<Type, object>();

        private static readonly Dictionary<Type, List<Action<object>>> onRegisterCallbacks =
            new Dictionary<Type, List<Action<object>>>();

        private static readonly Dictionary<Type, List<Action<object>>> onUnregisterCallbacks =
            new Dictionary<Type, List<Action<object>>>();

        public static void Register<T>(T service)
            where T : class, IDisposable
        {
            if (service == null)
            {
                throw new Exception("[ServiceLocator] -> You cant register a null instance");
            }

            Type serviceType = typeof(T);
            
            if (!serviceType.IsInterface)
            {
                throw new Exception("[ServiceLocator] -> You can only register service interfaces");
            }
            
            if (servicesTable.ContainsKey(serviceType))
            {
                throw new Exception("[ServiceLocator] -> " + serviceType.Name + " is already registered.");
            }

            servicesTable.Add(serviceType, service);

            if (onRegisterCallbacks.TryGetValue(serviceType, out List<Action<object>> callbacks))
            {
                ExecuteCallbacks(callbacks, service);
            }
        }

        public static T GetService<T>()
            where T : class, IDisposable
        {
            Type serviceType = typeof(T);
            if (!servicesTable.TryGetValue(serviceType, out object service))
            {
                Debug.LogWarning($"[ServiceLocator] Failed to get service of type \"{serviceType.Name}\"");
                return null;
            }

            return service as T;
        }

        public static bool IsServiceReady<T>()
            where T : class, IDisposable
        {
            Type serviceType = typeof(T);
            if (servicesTable == null || !servicesTable.TryGetValue(serviceType, out object service))
            {
                return false;
            }

            return true;
        }

        public static bool TryGetService<T>(out T service)
            where T : class, IDisposable
        {
            service = GetService<T>();
            return service != null;
        }

        public static void UnRegister<T>(bool autoDispose = true)
            where T : class, IDisposable
        {
            Type serviceType = typeof(T);
            if (!servicesTable.TryGetValue(serviceType, out object service))
            {
                return;
            }

            if (onUnregisterCallbacks.TryGetValue(serviceType, out List<Action<object>> callbacks))
            {
                ExecuteCallbacks(callbacks, service);
            }

            if (autoDispose)
            {
                (service as IDisposable)?.Dispose();
            }

            servicesTable[serviceType] = null;
            servicesTable.Remove(serviceType);

            if (onRegisterCallbacks.ContainsKey(serviceType))
            {
                onRegisterCallbacks.Remove(serviceType);
            }

            if (onUnregisterCallbacks.ContainsKey(serviceType))
            {
                onRegisterCallbacks.Remove(serviceType);
            }
        }

        public static void SubscribeRegisterFor<T>(Action<object> callback)
            where T : class, IDisposable
        {
            Type serviceType = typeof(T);
            if (!onRegisterCallbacks.ContainsKey(serviceType))
            {
                onRegisterCallbacks.Add(serviceType, new List<Action<object>>());
            }

            onRegisterCallbacks[serviceType].Add(callback);
        }

        public static void UnsubscribeRegisterFor<T>(Action<object> callback)
            where T : class, IDisposable
        {
            Type serviceType = typeof(T);
            if (onRegisterCallbacks.TryGetValue(serviceType, out List<Action<object>> callbacks))
            {
                callbacks.Remove(callback);
            }
        }

        public static void SubscribeUnregisterFor<T>(Action<object> callback)
            where T : class, IDisposable
        {
            Type serviceType = typeof(T);
            if (!onUnregisterCallbacks.ContainsKey(serviceType))
            {
                onUnregisterCallbacks.Add(serviceType, new List<Action<object>>());
            }

            onUnregisterCallbacks[serviceType].Add(callback);
        }

        public static void UnsubscribeUnregisterFor<T>(Action<object> callback)
            where T : class, IDisposable
        {
            Type serviceType = typeof(T);
            if (onUnregisterCallbacks.TryGetValue(serviceType, out List<Action<object>> callbacks))
            {
                callbacks.Remove(callback);
            }
        }

        private static void ExecuteCallbacks(List<Action<object>> callbacks, object instance)
        {
            for (int i = callbacks.Count - 1; i >= 0; i--)
            {
                try
                {
                    callbacks[i]?.Invoke(instance);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
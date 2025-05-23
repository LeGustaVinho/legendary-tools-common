using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace LegendaryTools.Concurrency.Addressables
{
    public static class AsyncWaitAddressables
    {
        public static Task ForAsyncOperationHandle(AsyncOperationHandle asyncOperationHandle,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
            if (asyncOperationHandle.IsValid() == false)
                throw new ArgumentException("Invalid AsyncOperationHandle.", nameof(asyncOperationHandle));

            AsyncWaitTaskContext context = new()
            {
                CancellationToken = cancellationToken,
                Backend = backend,
                Progress = progress
            };
            
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return UniTaskAdressablesAsyncWait.WaitForAsyncOperationHandleUniTask(context, asyncOperationHandle);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return WaitForAsyncOperationHandleNativeAsync(context, asyncOperationHandle);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return Concurrency.AsyncWait.RunCoroutine(WaitForAsyncOperationHandleCoroutine(context, asyncOperationHandle), cancellationToken);
            }
        }

        public static Task<T> ForAsyncOperationHandle<T>(AsyncOperationHandle<T> asyncOperationHandle,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
            if (asyncOperationHandle.IsValid() == false)
                throw new ArgumentException("Invalid AsyncOperationHandle.", nameof(asyncOperationHandle));

            AsyncWaitTaskContext context = new()
            {
                CancellationToken = cancellationToken,
                Backend = backend,
                Progress = progress
            };

            switch (backend)
            {
#if ENABLE_UNITASK 
                case AsyncWaitBackend.UniTask:
                    return UniTaskAdressablesAsyncWait.WaitForAsyncOperationHandleUniTask<T>(context, asyncOperationHandle);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return WaitForAsyncOperationHandleNativeAsync<T>(context, asyncOperationHandle);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return Concurrency.AsyncWait.RunCoroutineWithResult<T>(WaitForAsyncOperationHandleCoroutine(context, asyncOperationHandle), cancellationToken);
            }
        }

        private static async Task WaitForAsyncOperationHandleNativeAsync(AsyncWaitTaskContext context, AsyncOperationHandle asyncOperationHandle)
        {
            while (!asyncOperationHandle.IsDone)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.Progress?.Report(asyncOperationHandle.PercentComplete);
                await Task.Yield();
            }

            context.Progress?.Report(1f);

            if (asyncOperationHandle.Status == AsyncOperationStatus.Failed)
                throw asyncOperationHandle.OperationException ?? new Exception("AsyncOperationHandle failed.");

            if (asyncOperationHandle.Status == AsyncOperationStatus.None)
                throw new OperationCanceledException(context.CancellationToken);
        }

        private static async Task<T> WaitForAsyncOperationHandleNativeAsync<T>(AsyncWaitTaskContext context, AsyncOperationHandle<T> asyncOperationHandle)
        {
            while (!asyncOperationHandle.IsDone)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.Progress?.Report(asyncOperationHandle.PercentComplete);
                await Task.Yield();
            }

            context.Progress?.Report(1f);

            if (asyncOperationHandle.Status == AsyncOperationStatus.Failed)
                throw asyncOperationHandle.OperationException ?? new Exception("AsyncOperationHandle failed.");

            if (asyncOperationHandle.Status == AsyncOperationStatus.None)
                throw new OperationCanceledException(context.CancellationToken);

            return asyncOperationHandle.Result;
        }
        
        private static System.Collections.IEnumerator WaitForAsyncOperationHandleCoroutine(AsyncWaitTaskContext context, AsyncOperationHandle asyncOperationHandle)
        {
            while (!asyncOperationHandle.IsDone)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(context.CancellationToken);
                context.Progress?.Report(asyncOperationHandle.PercentComplete);
                yield return null;
            }

            context.Progress?.Report(1f);

            if (asyncOperationHandle.Status == AsyncOperationStatus.Failed)
                throw asyncOperationHandle.OperationException ?? new Exception("AsyncOperationHandle failed.");

            if (asyncOperationHandle.Status == AsyncOperationStatus.None)
                throw new OperationCanceledException(context.CancellationToken);
        }

        private static System.Collections.IEnumerator WaitForAsyncOperationHandleCoroutine<T>(AsyncWaitTaskContext context, AsyncOperationHandle<T> asyncOperationHandle)
        {
            while (!asyncOperationHandle.IsDone)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(context.CancellationToken);
                context.Progress?.Report(asyncOperationHandle.PercentComplete);
                yield return null;
            }

            context.Progress?.Report(1f);

            if (asyncOperationHandle.Status == AsyncOperationStatus.Failed)
                throw asyncOperationHandle.OperationException ?? new Exception("AsyncOperationHandle failed.");

            if (asyncOperationHandle.Status == AsyncOperationStatus.None)
                throw new OperationCanceledException(context.CancellationToken);

            yield return asyncOperationHandle.Result;
        }
    }
}
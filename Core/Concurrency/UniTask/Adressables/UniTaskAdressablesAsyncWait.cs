#if ENABLE_UNITASK && UNITASK_ADDRESSABLE_SUPPORT
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace LegendaryTools.Concurrency.Addressables
{
    public class UniTaskAdressablesAsyncWait
    {
        public static async Task WaitForAsyncOperationHandleUniTask(AsyncWaitTaskContext context, AsyncOperationHandle asyncOperationHandle)
        {
            await asyncOperationHandle.ToUniTask(context.Progress, cancellationToken: context.CancellationToken).AsTask();
        }

        public static async Task<T> WaitForAsyncOperationHandleUniTask<T>(AsyncWaitTaskContext context, AsyncOperationHandle<T> asyncOperationHandle)
        {
            return await asyncOperationHandle.ToUniTask(context.Progress, cancellationToken: context.CancellationToken).AsTask();
        }
    }
}
#endif
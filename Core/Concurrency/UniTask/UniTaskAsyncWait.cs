#if ENABLE_UNITASK
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace LegendaryTools.Threads
{
    public static class UniTaskAsyncWait
    {
        public static Task ForSeconds(float seconds, CancellationToken cancellationToken)
        {
            return UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: cancellationToken).AsTask();
        }

        public static Task ForFrames(int frames, CancellationToken cancellationToken)
        {
            return UniTask.DelayFrame(frames, cancellationToken: cancellationToken).AsTask();
        }

        public static Task Until(Func<bool> condition, CancellationToken cancellationToken)
        {
            return UniTask.WaitUntil(condition, cancellationToken: cancellationToken).AsTask();
        }

        public static Task While(Func<bool> condition, CancellationToken cancellationToken)
        {
            return UniTask.WaitWhile(condition, cancellationToken: cancellationToken).AsTask();
        }

        public static Task ForEndOfFrame(MonoBehaviour owner, CancellationToken cancellationToken)
        {
            return UniTask.WaitForEndOfFrame(owner, cancellationToken).AsTask();
        }

        public static Task ForFixedUpdate(CancellationToken cancellationToken)
        {
            return UniTask.WaitForFixedUpdate(cancellationToken).AsTask();
        }

        public static Task ForAsync(Func<AsyncWaitTaskContext, Task> asyncAction, AsyncWaitTaskContext context)
        {
            return UniTask.Create(async () =>
            {
                await asyncAction(context);
                return UniTask.CompletedTask;
            }).AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task<T> ForAsync<T>(Func<AsyncWaitTaskContext, Task<T>> asyncAction,
            AsyncWaitTaskContext context)
        {
            return UniTask.Create(async () => await asyncAction(context))
                .AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task ForAsync<T1>(Func<AsyncWaitTaskContext, T1, Task> asyncAction,
            AsyncWaitTaskContext context, T1 param1)
        {
            return UniTask.Create(async () =>
            {
                await asyncAction(context, param1);
                return UniTask.CompletedTask;
            }).AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task ForAsync<T1, T2>(Func<AsyncWaitTaskContext, T1, T2, Task> asyncAction,
            AsyncWaitTaskContext context, T1 param1, T2 param2)
        {
            return UniTask.Create(async () =>
            {
                await asyncAction(context, param1, param2);
                return UniTask.CompletedTask;
            }).AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task ForAsync<T1, T2, T3>(Func<AsyncWaitTaskContext, T1, T2, T3, Task> asyncAction,
            AsyncWaitTaskContext context, T1 param1, T2 param2, T3 param3)
        {
            return UniTask.Create(async () =>
            {
                await asyncAction(context, param1, param2, param3);
                return UniTask.CompletedTask;
            }).AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task ForAsync<T1, T2, T3, T4>(Func<AsyncWaitTaskContext, T1, T2, T3, T4, Task> asyncAction,
            AsyncWaitTaskContext context, T1 param1, T2 param2, T3 param3, T4 param4)
        {
            return UniTask.Create(async () =>
            {
                await asyncAction(context, param1, param2, param3, param4);
                return UniTask.CompletedTask;
            }).AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task ForAsync<T1, T2, T3, T4, T5>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, Task> asyncAction, AsyncWaitTaskContext context,
            T1 param1, T2 param2, T3 param3, T4 param4, T5 param5)
        {
            return UniTask.Create(async () =>
            {
                await asyncAction(context, param1, param2, param3, param4, param5);
                return UniTask.CompletedTask;
            }).AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task ForAsync<T1, T2, T3, T4, T5, T6>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, T6, Task> asyncAction, AsyncWaitTaskContext context,
            T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6)
        {
            return UniTask.Create(async () =>
            {
                await asyncAction(context, param1, param2, param3, param4, param5, param6);
                return UniTask.CompletedTask;
            }).AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task<T> ForAsync<T, T1>(Func<AsyncWaitTaskContext, T1, Task<T>> asyncAction,
            AsyncWaitTaskContext context, T1 param1)
        {
            return UniTask.Create(async () => await asyncAction(context, param1))
                .AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task<T> ForAsync<T, T1, T2>(Func<AsyncWaitTaskContext, T1, T2, Task<T>> asyncAction,
            AsyncWaitTaskContext context, T1 param1, T2 param2)
        {
            return UniTask.Create(async () => await asyncAction(context, param1, param2))
                .AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task<T> ForAsync<T, T1, T2, T3>(Func<AsyncWaitTaskContext, T1, T2, T3, Task<T>> asyncAction,
            AsyncWaitTaskContext context, T1 param1, T2 param2, T3 param3)
        {
            return UniTask.Create(async () => await asyncAction(context, param1, param2, param3))
                .AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task<T> ForAsync<T, T1, T2, T3, T4>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, Task<T>> asyncAction, AsyncWaitTaskContext context,
            T1 param1, T2 param2, T3 param3, T4 param4)
        {
            return UniTask.Create(async () => await asyncAction(context, param1, param2, param3, param4))
                .AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task<T> ForAsync<T, T1, T2, T3, T4, T5>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, Task<T>> asyncAction, AsyncWaitTaskContext context,
            T1 param1, T2 param2, T3 param3, T4 param4, T5 param5)
        {
            return UniTask.Create(async () => await asyncAction(context, param1, param2, param3, param4, param5))
                .AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static Task<T> ForAsync<T, T1, T2, T3, T4, T5, T6>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, T6, Task<T>> asyncAction, AsyncWaitTaskContext context,
            T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6)
        {
            return UniTask.Create(async () =>
                    await asyncAction(context, param1, param2, param3, param4, param5, param6))
                .AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        public static async Task Block(IEnumerable<Task> tasks, CancellationToken cancellationToken)
        {
            foreach (Task task in tasks)
            {
                await task.AsUniTask().AttachExternalCancellation(cancellationToken);
            }
        }

        public static async Task<T[]> Block<T>(IEnumerable<Task<T>> tasks, CancellationToken cancellationToken)
        {
            List<T> results = new();
            foreach (Task<T> task in tasks)
            {
                T result = await task.AsUniTask().AttachExternalCancellation(cancellationToken);
                results.Add(result);
            }

            return results.ToArray();
        }

        public static Task Sync(IEnumerable<Task> tasks, CancellationToken cancellationToken)
        {
            return UniTask
                .WhenAll(tasks.Select(task => task.AsUniTask().AttachExternalCancellation(cancellationToken)))
                .AsTask();
        }

        public static async Task<T[]> Sync<T>(IEnumerable<Task<T>> tasks, CancellationToken cancellationToken)
        {
            return await UniTask.WhenAll(tasks.Select(task =>
                task.AsUniTask().AttachExternalCancellation(cancellationToken)));
        }

        public static async Task Race(List<Task> tasks, CancellationToken cancellationToken)
        {
            await UniTask.WhenAny(tasks.Select(task =>
                task.AsUniTask().AttachExternalCancellation(cancellationToken)));
        }

        public static async Task<T> Race<T>(List<Task<T>> tasks, CancellationToken cancellationToken)
        {
            (int index, T result) = await UniTask.WhenAny(tasks.Select(task =>
                task.AsUniTask().AttachExternalCancellation(cancellationToken)));
            return result;
        }

        public static Task Rush(List<Task> tasks, CancellationToken cancellationToken)
        {
            return UniTask
                .WhenAny(tasks.Select(task => task.AsUniTask().AttachExternalCancellation(cancellationToken)))
                .AsTask();
        }

        public static async Task<T> Rush<T>(List<Task<T>> tasks, CancellationToken cancellationToken)
        {
            (int index, T result) = await UniTask.WhenAny(tasks.Select(task =>
                task.AsUniTask().AttachExternalCancellation(cancellationToken)));
            return result;
        }

        public static Task Branch(List<Task> tasks, CancellationToken cancellationToken)
        {
            foreach (Task task in tasks)
            {
                task.AsUniTask().AttachExternalCancellation(cancellationToken).Forget();
            }

            return Task.CompletedTask;
        }

        public static Task ForAsyncOperation(AsyncWaitTaskContext context, AsyncOperation asyncOperation)
        {
            return UniTask.Create(async () =>
            {
                await WaitForAsyncOperation(context, asyncOperation);
                return UniTask.CompletedTask;
            }).AttachExternalCancellation(context.CancellationToken).AsTask();
        }

        private static async UniTask WaitForAsyncOperation(AsyncWaitTaskContext context,
            AsyncOperation asyncOperation)
        {
            while (!asyncOperation.isDone)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.Progress?.Report(asyncOperation.progress);
                await UniTask.Yield();
            }

            context.Progress?.Report(1f);
        }
    }
}
#endif
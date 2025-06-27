using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Concurrency
{
    public static class AsyncWait
    {
        public static Task ForSeconds(float seconds,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return UniTaskAsyncWait.ForSeconds(seconds, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForSecondsCoroutine(seconds, cancellationToken), cancellationToken);
            }
        }

        public static Task ForFrames(int frames,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return UniTaskAsyncWait.ForFrames(frames, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return WaitForFramesNativeAsync(frames, cancellationToken);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine( WaitForFramesCoroutine(frames), cancellationToken);
            }
        }

        public static Task Until(Func<bool> condition,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return UniTaskAsyncWait.Until(condition, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return WaitUntilNativeAsync(condition, cancellationToken);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitUntilCoroutine(condition), cancellationToken);
            }
        }

        public static Task While(Func<bool> condition,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return UniTaskAsyncWait.While(condition, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return WaitWhileNativeAsync(condition, cancellationToken);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitWhileCoroutine(condition), cancellationToken);
            }
        }

        public static Task ForEndOfFrame(AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, 
            CancellationToken cancellationToken = default)
        {
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return UniTaskAsyncWait.ForEndOfFrame(UnityHub.Instance, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return WaitForEndOfFrameNativeAsync(cancellationToken);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForEndOfFrameCoroutine(), cancellationToken);
            }
        }

        public static Task ForFixedUpdate(AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, 
            CancellationToken cancellationToken = default)
        {
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return UniTaskAsyncWait.ForFixedUpdate(cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return WaitForFixedUpdateNativeAsync(cancellationToken);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForFixedUpdateCoroutine(), cancellationToken);
            }
        }

        public static Task ForTask(Task job,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForTask(job, context);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return WaitForTaskNativeAsync(job, context);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForTaskCoroutine(job, context), cancellationToken);
            }
        }
        
        public static Task ForAsync(Func<AsyncWaitTaskContext, Task> asyncAction,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForAsyncCoroutine(asyncAction, context), cancellationToken);
            }
        }

        public static Task<T> ForAsync<T>(Func<AsyncWaitTaskContext, Task<T>> asyncAction,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutineWithResult<T>(WaitForAsyncCoroutine(asyncAction, context),
                        cancellationToken);
            }
        }

        public static Task ForAsync<T1>(Func<AsyncWaitTaskContext, T1, Task> asyncAction,
            T1 param1,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForAsyncCoroutine(asyncAction, context, param1), cancellationToken);
            }
        }

        public static Task ForAsync<T1, T2>(Func<AsyncWaitTaskContext, T1, T2, Task> asyncAction,
            T1 param1, T2 param2, AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine,
            CancellationToken cancellationToken = default, IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1, param2);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1, param2);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForAsyncCoroutine(asyncAction, context, param1, param2),
                        cancellationToken);
            }
        }

        public static Task ForAsync<T1, T2, T3>(Func<AsyncWaitTaskContext, T1, T2, T3, Task> asyncAction, T1 param1, T2 param2, T3 param3,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1, param2, param3);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1, param2, param3);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForAsyncCoroutine(asyncAction, context, param1, param2, param3),
                        cancellationToken);
            }
        }

        public static Task ForAsync<T1, T2, T3, T4>(Func<AsyncWaitTaskContext, T1, T2, T3, T4, Task> asyncAction, T1 param1, T2 param2, T3 param3, T4 param4,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1, param2, param3, param4);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1, param2, param3, param4);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForAsyncCoroutine(asyncAction, context, param1, param2, param3, param4),
                        cancellationToken);
            }
        }

        public static Task ForAsync<T1, T2, T3, T4, T5>(Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, Task> asyncAction, T1 param1, T2 param2, T3 param3,
            T4 param4,
            T5 param5, AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine,
            CancellationToken cancellationToken = default, IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1, param2, param3, param4, param5);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1, param2, param3, param4, param5);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForAsyncCoroutine(asyncAction, context, param1, param2, param3, param4, param5),
                        cancellationToken);
            }
        }

        public static Task ForAsync<T1, T2, T3, T4, T5, T6>(Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, T6, Task> asyncAction, T1 param1, T2 param2, T3 param3,
            T4 param4, T5 param5, T6 param6, AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine,
            CancellationToken cancellationToken = default, IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1, param2, param3, param4, param5,
                        param6);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1, param2, param3, param4, param5, param6);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForAsyncCoroutine(asyncAction, context, param1, param2, param3, param4, param5, param6),
                        cancellationToken);
            }
        }

        public static Task<T> ForAsync<T, T1>(Func<AsyncWaitTaskContext, T1, Task<T>> asyncAction,
            T1 param1, AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine,
            CancellationToken cancellationToken = default, IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutineWithResult<T>(WaitForAsyncCoroutine(asyncAction, context, param1),
                        cancellationToken);
            }
        }

        public static Task<T> ForAsync<T, T1, T2>(Func<AsyncWaitTaskContext, T1, T2, Task<T>> asyncAction, T1 param1, T2 param2,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1, param2);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1, param2);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutineWithResult<T>(WaitForAsyncCoroutine(asyncAction, context, param1, param2),
                        cancellationToken);
            }
        }

        public static Task<T> ForAsync<T, T1, T2, T3>(Func<AsyncWaitTaskContext, T1, T2, T3, Task<T>> asyncAction, T1 param1, T2 param2, T3 param3,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1, param2, param3);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1, param2, param3);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutineWithResult<T>(WaitForAsyncCoroutine(asyncAction, context, param1, param2, param3),
                        cancellationToken);
            }
        }

        public static Task<T> ForAsync<T, T1, T2, T3, T4>(Func<AsyncWaitTaskContext, T1, T2, T3, T4, Task<T>> asyncAction, T1 param1, T2 param2, T3 param3, T4 param4,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1, param2, param3, param4);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1, param2, param3, param4);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutineWithResult<T>(WaitForAsyncCoroutine(asyncAction, context, param1, param2, param3, param4),
                        cancellationToken);
            }
        }

        public static Task<T> ForAsync<T, T1, T2, T3, T4, T5>(Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, Task<T>> asyncAction, T1 param1, T2 param2, T3 param3,
            T4 param4, T5 param5, AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine,
            CancellationToken cancellationToken = default, IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1, param2, param3, param4, param5);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1, param2, param3, param4, param5);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutineWithResult<T>(WaitForAsyncCoroutine(asyncAction, context, param1, param2, param3, param4, param5),
                        cancellationToken);
            }
        }

        public static Task<T> ForAsync<T, T1, T2, T3, T4, T5, T6>(Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, T6, Task<T>> asyncAction, T1 param1, T2 param2, T3 param3,
            T4 param4, T5 param5, T6 param6, AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine,
            CancellationToken cancellationToken = default, IProgress<float> progress = null)
        {
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
                    return UniTaskAsyncWait.ForAsync(asyncAction, context, param1, param2, param3, param4, param5,
                        param6);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return asyncAction(context, param1, param2, param3, param4, param5, param6);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutineWithResult<T>(WaitForAsyncCoroutine(asyncAction, context, param1, param2, param3, param4, param5, param6),
                        cancellationToken);
            }
        }

        public static async Task Block(IEnumerable<Task> tasks,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    await UniTaskAsyncWait.Block(tasks, cancellationToken);
                    break;
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    foreach (Task task in tasks)
                    {
                        await task;
                    }

                    break;
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    foreach (Task task in tasks)
                    {
                        await RunCoroutine(WaitForAsyncCoroutine(ct => task,
                                new AsyncWaitTaskContext { CancellationToken = cancellationToken, Backend = backend }),
                            cancellationToken);
                    }

                    break;
            }
        }

        public static async Task<T[]> Block<T>(IEnumerable<Task<T>> tasks,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            List<T> results = new();
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return await UniTaskAsyncWait.Block(tasks, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    foreach (Task<T> task in tasks)
                    {
                        T result = await task;
                        results.Add(result);
                    }

                    break;
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    foreach (Task<T> task in tasks)
                    {
                        T result = await RunCoroutineWithResult<T>(WaitForAsyncCoroutine(ct => task,
                                new AsyncWaitTaskContext { CancellationToken = cancellationToken, Backend = backend }),
                            cancellationToken);
                        results.Add(result);
                    }

                    break;
            }

            return results.ToArray();
        }

        public static Task Sync(IEnumerable<Task> tasks,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return UniTaskAsyncWait.Sync(tasks, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return Task.WhenAll(tasks);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForTasksCoroutine(tasks, cancellationToken), cancellationToken);
            }
        }

        public static async Task<T[]> Sync<T>(IEnumerable<Task<T>> tasks,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return await UniTaskAsyncWait.Sync(tasks, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return await Task.WhenAll(tasks);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    List<Task<T>> taskList = tasks.ToList();
                    TaskCompletionSource<T[]> tcs = new();
                    UnityHub.Instance.StartCoroutine(WrapCoroutineWithResult(WaitForTasksCoroutine(taskList.Select(t => (Task)t),
                        cancellationToken, async () =>
                        {
                            T[] results = new T[taskList.Count];
                            for (int i = 0; i < taskList.Count; i++)
                            {
                                results[i] = await taskList[i];
                            }

                            tcs.SetResult(results);
                        }), tcs, cancellationToken));
                    return await tcs.Task;
            }
        }

        public static async Task Race(IEnumerable<Task> tasks,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            List<Task> taskList = tasks.ToList();
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    await UniTaskAsyncWait.Race(taskList, cts.Token);
                    cts.Cancel();
                    break;
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    await Task.WhenAny(taskList);
                    cts.Cancel();
                    break;
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    await RunCoroutine(WaitForFirstTaskCoroutine(taskList, cts, cancellationToken),
                        cancellationToken);
                    cts.Cancel();
                    break;
            }
        }

        public static async Task<T> Race<T>(IEnumerable<Task<T>> tasks,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            List<Task<T>> taskList = tasks.ToList();
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    T result1 = await UniTaskAsyncWait.Race(taskList, cts.Token);
                    cts.Cancel();
                    return result1;
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    Task<T> completedTask = await Task.WhenAny(taskList);
                    cts.Cancel();
                    return await completedTask;
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    TaskCompletionSource<T> tcs = new();
                    UnityHub.Instance.StartCoroutine(WrapCoroutineWithResult(WaitForFirstTaskCoroutine(
                        taskList.Select(t => (Task)t), cts, cancellationToken, async () =>
                        {
                            Task<T> completedTask = await Task.WhenAny(taskList);
                            tcs.SetResult(await completedTask);
                        }), tcs, cancellationToken));
                    T result = await tcs.Task;
                    cts.Cancel();
                    return result;
            }
        }

        public static Task Rush(IEnumerable<Task> tasks,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            List<Task> taskList = tasks.ToList();
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return UniTaskAsyncWait.Rush(taskList, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return Task.WhenAny(taskList);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForFirstTaskCoroutine(taskList, null, cancellationToken),
                        cancellationToken);
            }
        }

        public static async Task<T> Rush<T>(IEnumerable<Task<T>> tasks,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            List<Task<T>> taskList = tasks.ToList();
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return await UniTaskAsyncWait.Rush(taskList, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    Task<T> completedTask = await Task.WhenAny(taskList);
                    return await completedTask;
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    TaskCompletionSource<T> tcs = new();
                    UnityHub.Instance.StartCoroutine(WrapCoroutineWithResult(WaitForFirstTaskCoroutine(
                        taskList.Select(t => (Task)t), null, cancellationToken, async () =>
                        {
                            Task<T> completedTask = await Task.WhenAny(taskList);
                            tcs.SetResult(await completedTask);
                        }), tcs, cancellationToken));
                    return await tcs.Task;
            }
        }

        public static Task Branch(IEnumerable<Task> tasks,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default)
        {
            List<Task> taskList = tasks.ToList();
            switch (backend)
            {
#if ENABLE_UNITASK
                case AsyncWaitBackend.UniTask:
                    return UniTaskAsyncWait.Branch(taskList, cancellationToken);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    foreach (Task task in taskList)
                    {
                        _ = task;
                    }

                    return Task.CompletedTask;
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(StartTasksCoroutine(taskList, cancellationToken), cancellationToken);
            }
        }

        public static Task ForAsyncOperation(AsyncOperation asyncOperation,
            AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine, CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
            if (asyncOperation == null)
                throw new ArgumentNullException(nameof(asyncOperation));

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
                    return UniTaskAsyncWait.ForAsyncOperation(context, asyncOperation);
#endif
                case AsyncWaitBackend.NativeAsyncWait:
                    return WaitForAsyncOperation(context, asyncOperation);
                case AsyncWaitBackend.UnityCoroutine:
                default:
                    return RunCoroutine(WaitForAsyncOperationCoroutine(context, asyncOperation),
                        cancellationToken);
            }
        }

        public static async Task RunCoroutine(System.Collections.IEnumerator coroutine,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource<object> tcs = new();
            Coroutine startedCoroutine = UnityHub.Instance.StartCoroutine(WrapCoroutine(coroutine, tcs, cancellationToken));

            try
            {
                await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                UnityHub.Instance.StopCoroutine(startedCoroutine);
                throw;
            }
        }

        private static async Task<T> RunCoroutineWithResult<T>(System.Collections.IEnumerator coroutine, CancellationToken cancellationToken)
        {
            TaskCompletionSource<T> tcs = new();
            Coroutine startedCoroutine =
                UnityHub.Instance.StartCoroutine(WrapCoroutineWithResult(coroutine, tcs, cancellationToken));

            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                UnityHub.Instance.StopCoroutine(startedCoroutine);
                throw;
            }
        }

        private static System.Collections.IEnumerator WrapCoroutine(
            System.Collections.IEnumerator coroutine,
            TaskCompletionSource<object> tcs,
            CancellationToken cancellationToken)
        {
            while (coroutine.MoveNext())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.SetCanceled();
                    yield break;
                }

                yield return coroutine.Current;
                if (tcs.Task.IsFaulted) yield break;
            }

            if (!tcs.Task.IsCompleted) tcs.SetResult(null);
        }
        
        private static System.Collections.IEnumerator WrapCoroutineWithResult<T>(
            System.Collections.IEnumerator coroutine,
            TaskCompletionSource<T> tcs,
            CancellationToken cancellationToken)
        {
            while (coroutine.MoveNext())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.SetCanceled();
                    yield break;
                }

                yield return coroutine.Current;
                if (tcs.Task.IsFaulted) yield break;
            }

            if (!tcs.Task.IsCompleted)
            {
                if (coroutine.Current is T result)
                    tcs.SetResult(result);
                else
                    tcs.SetException(
                        new InvalidOperationException("Coroutine did not return a value of type " + typeof(T).Name));
            }
        }

        private static System.Collections.IEnumerator WaitForSecondsCoroutine(float seconds,
            CancellationToken cancellationToken)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private static System.Collections.IEnumerator WaitForFramesCoroutine(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }
        }

        private static System.Collections.IEnumerator WaitUntilCoroutine(Func<bool> condition)
        {
            while (!condition())
            {
                yield return null;
            }
        }

        private static System.Collections.IEnumerator WaitWhileCoroutine(Func<bool> condition)
        {
            while (condition())
            {
                yield return null;
            }
        }

        private static System.Collections.IEnumerator WaitForEndOfFrameCoroutine()
        {
            //WaitForEndOfFrame yields control until rendering phase of the current frame, not the next.
            //Newly started coroutines using a single WaitForEndOfFrame will fire before the next Update.
            // Wait one frame (advance to the next frame) before waiting for its end.
            yield return null;

            // Wait until the end of the next frame's rendering.
            yield return new WaitForEndOfFrame();
        }

        private static System.Collections.IEnumerator WaitForFixedUpdateCoroutine()
        {
            yield return new WaitForFixedUpdate();
        }

        private static async Task WaitForTaskNativeAsync(Task job, AsyncWaitTaskContext context)
        {
            while (!job.IsCompleted)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.Progress?.Report(job.IsCompleted ? 1f : 0f);
                await Task.Yield();
            }

            context.Progress?.Report(1f);
            if (job.IsFaulted) throw job.Exception?.InnerException ?? new Exception("Task failed.");
            if (job.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
        }

        private static System.Collections.IEnumerator WaitForTaskCoroutine(Task job, AsyncWaitTaskContext context)
        {
            while (!job.IsCompleted)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(context.CancellationToken);
                context.Progress?.Report(job.IsCompleted ? 1f : 0f);
                yield return null;
            }

            context.Progress?.Report(1f);
            if (job.IsFaulted) throw job.Exception?.InnerException ?? new Exception("Task failed.");
            if (job.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
        }
        
        private static async Task WaitForAsyncOperation(AsyncWaitTaskContext context, AsyncOperation asyncOperation)
        {
            while (!asyncOperation.isDone)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.Progress?.Report(asyncOperation.progress);
                await Task.Yield();
            }

            context.Progress?.Report(1f);
        }

        private static System.Collections.IEnumerator WaitForAsyncOperationCoroutine(AsyncWaitTaskContext context,
            AsyncOperation asyncOperation)
        {
            while (!asyncOperation.isDone)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(context.CancellationToken);
                context.Progress?.Report(asyncOperation.progress);
                yield return null;
            }

            context.Progress?.Report(1f);
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine(
            Func<AsyncWaitTaskContext, Task> asyncAction,
            AsyncWaitTaskContext context)
        {
            Task task = asyncAction(context);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T>(
            Func<AsyncWaitTaskContext, Task<T>> asyncAction, AsyncWaitTaskContext context)
        {
            Task<T> task = asyncAction(context);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
            yield return task.Result;
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T1>(
            Func<AsyncWaitTaskContext, T1, Task> asyncAction, AsyncWaitTaskContext context, T1 param1)
        {
            Task task = asyncAction(context, param1);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T1, T2>(
            Func<AsyncWaitTaskContext, T1, T2, Task> asyncAction, AsyncWaitTaskContext context, T1 param1, T2 param2)
        {
            Task task = asyncAction(context, param1, param2);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T1, T2, T3>(
            Func<AsyncWaitTaskContext, T1, T2, T3, Task> asyncAction, AsyncWaitTaskContext context, T1 param1,
            T2 param2, T3 param3)
        {
            Task task = asyncAction(context, param1, param2, param3);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T1, T2, T3, T4>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, Task> asyncAction, AsyncWaitTaskContext context, T1 param1,
            T2 param2, T3 param3, T4 param4)
        {
            Task task = asyncAction(context, param1, param2, param3, param4);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T1, T2, T3, T4, T5>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, Task> asyncAction, AsyncWaitTaskContext context,
            T1 param1, T2 param2, T3 param3, T4 param4, T5 param5)
        {
            Task task = asyncAction(context, param1, param2, param3, param4, param5);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T1, T2, T3, T4, T5, T6>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, T6, Task> asyncAction, AsyncWaitTaskContext context,
            T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6)
        {
            Task task = asyncAction(context, param1, param2, param3, param4, param5, param6);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T, T1>(
            Func<AsyncWaitTaskContext, T1, Task<T>> asyncAction, AsyncWaitTaskContext context, T1 param1)
        {
            Task<T> task = asyncAction(context, param1);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
            yield return task.Result;
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T, T1, T2>(
            Func<AsyncWaitTaskContext, T1, T2, Task<T>> asyncAction, AsyncWaitTaskContext context, T1 param1,
            T2 param2)
        {
            Task<T> task = asyncAction(context, param1, param2);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
            yield return task.Result;
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T, T1, T2, T3>(
            Func<AsyncWaitTaskContext, T1, T2, T3, Task<T>> asyncAction, AsyncWaitTaskContext context, T1 param1,
            T2 param2, T3 param3)
        {
            Task<T> task = asyncAction(context, param1, param2, param3);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
            yield return task.Result;
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T, T1, T2, T3, T4>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, Task<T>> asyncAction, AsyncWaitTaskContext context,
            T1 param1, T2 param2, T3 param3, T4 param4)
        {
            Task<T> task = asyncAction(context, param1, param2, param3, param4);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
            yield return task.Result;
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T, T1, T2, T3, T4, T5>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, Task<T>> asyncAction, AsyncWaitTaskContext context,
            T1 param1, T2 param2, T3 param3, T4 param4, T5 param5)
        {
            Task<T> task = asyncAction(context, param1, param2, param3, param4, param5);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
            yield return task.Result;
        }

        private static System.Collections.IEnumerator WaitForAsyncCoroutine<T, T1, T2, T3, T4, T5, T6>(
            Func<AsyncWaitTaskContext, T1, T2, T3, T4, T5, T6, Task<T>> asyncAction, AsyncWaitTaskContext context,
            T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6)
        {
            Task<T> task = asyncAction(context, param1, param2, param3, param4, param5, param6);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("Async action failed.");
            if (task.IsCanceled) throw new OperationCanceledException(context.CancellationToken);
            yield return task.Result;
        }

        private static async Task WaitForFramesNativeAsync(int frames, CancellationToken cancellationToken)
        {
            float frameTime = 1f / 60f;
            await Task.Delay(TimeSpan.FromSeconds(frames * frameTime), cancellationToken);
        }

        private static async Task WaitUntilNativeAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            while (!condition())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        private static async Task WaitWhileNativeAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            while (condition())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        private static async Task WaitForEndOfFrameNativeAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
        }

        private static async Task WaitForFixedUpdateNativeAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(Time.fixedDeltaTime), cancellationToken);
        }

        private static System.Collections.IEnumerator WaitForTasksCoroutine(IEnumerable<Task> tasks,
            CancellationToken cancellationToken, Action callback = null)
        {
            List<Task> taskList = tasks.ToList();
            while (taskList.Any(t => !t.IsCompleted))
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                yield return null;
            }

            foreach (Task task in taskList)
            {
                if (task.IsFaulted) throw task.Exception?.InnerException ?? new Exception("One or more tasks failed.");
                if (task.IsCanceled) throw new OperationCanceledException(cancellationToken);
            }

            callback?.Invoke();
        }

        private static System.Collections.IEnumerator WaitForFirstTaskCoroutine(IEnumerable<Task> tasks,
            CancellationTokenSource cts, CancellationToken cancellationToken, Action callback = null)
        {
            List<Task> taskList = tasks.ToList();
            while (!taskList.Any(t => t.IsCompleted))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cts?.Cancel();
                    throw new OperationCanceledException(cancellationToken);
                }

                yield return null;
            }

            Task completedTask = taskList.First(t => t.IsCompleted);
            if (completedTask.IsFaulted)
            {
                cts?.Cancel();
                throw completedTask.Exception?.InnerException ?? new Exception("First completed task failed.");
            }

            if (completedTask.IsCanceled)
            {
                cts?.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }

            callback?.Invoke();
        }

        private static System.Collections.IEnumerator StartTasksCoroutine(IEnumerable<Task> tasks,
            CancellationToken cancellationToken)
        {
            foreach (Task task in tasks)
            {
                _ = task;
            }

            yield return null;
        }
    }
}
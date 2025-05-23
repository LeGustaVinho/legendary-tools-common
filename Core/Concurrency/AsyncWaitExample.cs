using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using LegendaryTools.Concurrency;

public class AsyncWaitExample : MonoBehaviour
{
    public AsyncWaitBackend Backend;
    private CancellationTokenSource cts = new CancellationTokenSource();
    private bool conditionMet = false;
    private int counter = 0;

    private async void Start()
    {
        try
        {
            // Demonstrate ForSeconds: Wait for 2 seconds
            Debug.Log($"[{Time.time}] Starting: Waiting for 2 seconds...");
            await AsyncWait.ForSeconds(2f, Backend, cts.Token);
            Debug.Log($"[{Time.time}] 2 seconds elapsed.");

            // Demonstrate ForFrames: Wait for 60 frames
            Debug.Log($"[{Time.time}] Waiting for 60 frames...");
            await AsyncWait.ForFrames(60, Backend, cts.Token);
            Debug.Log($"[{Time.time}] 60 frames elapsed.");

            // Demonstrate Until: Wait until a condition is met
            conditionMet = false;
            Debug.Log($"[{Time.time}] Waiting until condition is met...");
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000); // Simulate condition being met after 3 seconds
                conditionMet = true;
            });
            await AsyncWait.Until(() => conditionMet, Backend, cts.Token);
            Debug.Log($"[{Time.time}] Condition met!");

            // Demonstrate While: Wait while a condition is true
            counter = 5;
            Debug.Log($"[{Time.time}] Waiting while counter > 0...");
            await AsyncWait.While(() => counter > 0, Backend, cts.Token);
            Debug.Log($"[{Time.time}] Counter reached 0.");

            // Demonstrate ForEndOfFrame: Wait until the end of the frame
            Debug.Log($"[{Time.frameCount}] Waiting for end of frame...");
            await AsyncWait.ForEndOfFrame(Backend, cts.Token);
            Debug.Log($"[{Time.frameCount}] End of frame reached.");

            // Demonstrate ForFixedUpdate: Wait for the next fixed update
            Debug.Log($"[{Time.time}] Waiting for fixed update...");
            await AsyncWait.ForFixedUpdate(Backend, cts.Token);
            Debug.Log($"[{Time.time}] Fixed update reached.");

            // Demonstrate ForAsync (no parameters, no return)
            Debug.Log($"[{Time.time}] Running async action...");
            await AsyncWait.ForAsync(AsyncAction, Backend, cts.Token, new Progress<float>(p => Debug.Log($"[{Time.time}] Progress: {p}")));
            Debug.Log($"[{Time.time}] Async action completed.");

            // Demonstrate ForAsync with return value
            Debug.Log($"[{Time.time}] Running async action with return value...");
            int result = await AsyncWait.ForAsync<int>(async context =>
            {
                await Task.Delay(1000);
                return 42;
            }, Backend, cts.Token);
            Debug.Log($"[{Time.time}] Async action returned: {result}");

            // Demonstrate ForAsync with one parameter
            Debug.Log($"[{Time.time}] Running async action with one parameter...");
            await AsyncWait.ForAsync<string>(async (context, param) =>
            {
                Debug.Log($"[{Time.time}] Parameter received: {param}");
                await Task.Delay(1000);
            }, "TestParam", Backend, cts.Token);
            Debug.Log($"[{Time.time}] Async action with parameter completed.");

            // Demonstrate Block: Wait for all tasks to complete
            Debug.Log($"[{Time.time}] Running Block with multiple tasks...");
            List<Task> tasks = new List<Task>
            {
                AsyncWait.ForSeconds(1f, Backend, cts.Token),
                AsyncWait.ForSeconds(2f, Backend, cts.Token)
            };
            await AsyncWait.Block(tasks, Backend, cts.Token);
            Debug.Log($"[{Time.time}] All tasks in Block completed.");

            // Demonstrate Block with return values
            Debug.Log($"[{Time.time}] Running Block with return values...");
            List<Task<int>> tasksWithResult = new List<Task<int>>
            {
                AsyncWait.ForAsync<int>(async context => { await Task.Delay(1000); return 1; }, Backend, cts.Token),
                AsyncWait.ForAsync<int>(async context => { await Task.Delay(2000); return 2; }, Backend, cts.Token)
            };
            int[] results = await AsyncWait.Block(tasksWithResult, Backend, cts.Token);
            Debug.Log($"[{Time.time}] Block results: {string.Join(", ", results)}");

            // Demonstrate Sync: Run tasks concurrently and wait for all
            Debug.Log($"[{Time.time}] Running Sync with multiple tasks...");
            tasks = new List<Task>
            {
                AsyncWait.ForSeconds(1f, Backend, cts.Token),
                AsyncWait.ForSeconds(2f, Backend, cts.Token)
            };
            await AsyncWait.Sync(tasks, Backend, cts.Token);
            Debug.Log($"[{Time.time}] All tasks in Sync completed.");

            // Demonstrate Sync with return values
            Debug.Log($"[{Time.time}] Running Sync with return values...");
            tasksWithResult = new List<Task<int>>
            {
                AsyncWait.ForAsync<int>(async context => { await Task.Delay(1000); return 3; }, Backend, cts.Token),
                AsyncWait.ForAsync<int>(async context => { await Task.Delay(2000); return 4; }, Backend, cts.Token)
            };
            results = await AsyncWait.Sync(tasksWithResult, Backend, cts.Token);
            Debug.Log($"[{Time.time}] Sync results: {string.Join(", ", results)}");

            // Demonstrate Race: Wait for the first task to complete and cancel others
            Debug.Log($"[{Time.time}] Running Race with multiple tasks...");
            tasks = new List<Task>
            {
                AsyncWait.ForSeconds(3f, Backend, cts.Token),
                AsyncWait.ForSeconds(1f, Backend, cts.Token)
            };
            await AsyncWait.Race(tasks, Backend, cts.Token);
            Debug.Log($"[{Time.time}] First task in Race completed.");

            // Demonstrate Race with return values
            Debug.Log($"[{Time.time}] Running Race with return values...");
            tasksWithResult = new List<Task<int>>
            {
                AsyncWait.ForAsync<int>(async context => { await Task.Delay(3000); return 5; }, Backend, cts.Token),
                AsyncWait.ForAsync<int>(async context => { await Task.Delay(1000); return 6; }, Backend, cts.Token)
            };
            result = await AsyncWait.Race(tasksWithResult, Backend, cts.Token);
            Debug.Log($"[{Time.time}] Race result: {result}");

            // Demonstrate Rush: Wait for the first task to complete without canceling others
            Debug.Log($"[{Time.time}] Running Rush with multiple tasks...");
            tasks = new List<Task>
            {
                AsyncWait.ForSeconds(3f, Backend, cts.Token),
                AsyncWait.ForSeconds(1f, Backend, cts.Token)
            };
            await AsyncWait.Rush(tasks, Backend, cts.Token);
            Debug.Log($"[{Time.time}] First task in Rush completed.");

            // Demonstrate Rush with return values
            Debug.Log($"[{Time.time}] Running Rush with return values...");
            tasksWithResult = new List<Task<int>>
            {
                AsyncWait.ForAsync<int>(async context => { await Task.Delay(3000); return 7; }, Backend, cts.Token),
                AsyncWait.ForAsync<int>(async context => { await Task.Delay(1000); return 8; }, Backend, cts.Token)
            };
            result = await AsyncWait.Rush(tasksWithResult, Backend, cts.Token);
            Debug.Log($"[{Time.time}] Rush result: {result}");

            // Demonstrate Branch: Start tasks without waiting
            Debug.Log($"[{Time.time}] Running Branch with multiple tasks...");
            tasks = new List<Task>
            {
                AsyncWait.ForSeconds(1f, Backend, cts.Token),
                AsyncWait.ForSeconds(2f, Backend, cts.Token)
            };
            await AsyncWait.Branch(tasks, Backend, cts.Token);
            Debug.Log($"[{Time.time}] Tasks in Branch started.");

            // Demonstrate ForAsyncOperation: Wait for an async operation (e.g., scene loading)
            Debug.Log($"[{Time.time}] Running async operation...");
            AsyncOperation operation = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("SampleScene", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            await AsyncWait.ForAsyncOperation(operation, Backend, cts.Token, new Progress<float>(p => Debug.Log($"[{Time.time}] Scene load progress: {p}")));
            Debug.Log($"[{Time.time}] Async operation (scene load) completed.");
        }
        catch (OperationCanceledException)
        {
            Debug.Log($"[{Time.time}] Operation was canceled.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error: {ex.Message}");
        }
    }

    private async Task AsyncAction(AsyncWaitTaskContext context)
    {
        Debug.Log($"[{Time.time}] Inside async action.");
        context.Progress?.Report(0.5f);
        await Task.Delay(1000);
        context.Progress?.Report(1f);
    }

    private void Update()
    {
        // Simulate decrementing counter for While example
        if (counter > 0)
        {
            counter--;
            Debug.Log($"[{Time.time}] Counter: {counter}");
        }
    }

    private void OnDestroy()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
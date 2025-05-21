// namespace LegendaryTools.Threads
// {
//     using System;
//     using System.Linq;
//     using System.Threading;
//     using System.Threading.Tasks;
//     using UnityEngine;
//
//     public class AsyncWaitExample : MonoBehaviour
//     {
//         public AsyncWaitBackend backend = AsyncWaitBackend.UnityCoroutine; // Controla o backend para todos os métodos
//
//         private async void Start()
//         {
//             // Token de cancelamento para demonstrar suporte a cancelamento
//             using CancellationTokenSource cts = new();
//
//             // 1. ForSeconds: Espera 2 segundos
//             await AsyncWait.ForSeconds(this, 2f, backend, cts.Token);
//             Debug.Log($"ForSeconds concluído com backend {backend}");
//
//             // 2. ForFrames: Espera 60 frames
//             await AsyncWait.ForFrames(this, 60, backend, cts.Token);
//             Debug.Log($"ForFrames concluído com backend {backend}");
//
//             // 3. Until: Espera até que uma condição seja verdadeira
//             int counter = 0;
//             await AsyncWait.Until(this, () => counter++ > 5, backend, cts.Token);
//             Debug.Log($"Until concluído após {counter} iterações com backend {backend}");
//
//             // 4. While: Espera enquanto uma condição for verdadeira
//             counter = 0;
//             await AsyncWait.While(this, () => counter++ < 5, backend, cts.Token);
//             Debug.Log($"While concluído após {counter} iterações com backend {backend}");
//
//             // 5. ForEndOfFrame: Espera pelo final do frame atual
//             await AsyncWait.ForEndOfFrame(this, backend, cts.Token);
//             Debug.Log($"ForEndOfFrame concluído com backend {backend}");
//
//             // 6. ForFixedUpdate: Espera pelo próximo fixed update
//             await AsyncWait.ForFixedUpdate(this, backend, cts.Token);
//             Debug.Log($"ForFixedUpdate concluído com backend {backend}");
//
//             // 7. ForAsync (sem retorno, sem parâmetros): Executa uma ação assíncrona
//             await AsyncWait.ForAsync(this, async ct =>
//             {
//                 await Task.Delay(1000, ct);
//                 Debug.Log("Ação assíncrona sem retorno concluída");
//             }, backend, cts.Token);
//             Debug.Log($"ForAsync (sem retorno) concluído com backend {backend}");
//
//             // 8. ForAsync<T> (com retorno, sem parâmetros): Executa uma ação assíncrona com retorno
//             int result = await AsyncWait.ForAsync(this, async ct =>
//             {
//                 await Task.Delay(1000, ct);
//                 return 42;
//             }, backend, cts.Token);
//             Debug.Log($"ForAsync<int> retornou {result} com backend {backend}");
//
//             // 9. ForAsync<T1> (sem retorno, com 1 parâmetro): Executa uma ação assíncrona com parâmetro
//             await AsyncWait.ForAsync(this, async (ct, param) =>
//             {
//                 await Task.Delay(1000, ct);
//                 Debug.Log($"Ação assíncrona com parâmetro: {param}");
//             }, "teste", backend, cts.Token);
//             Debug.Log($"ForAsync<string> (com parâmetro) concluído com backend {backend}");
//
//             // 10. Block: Executa tarefas sequencialmente (sem retorno)
//             Task[] blockTasks = new[]
//             {
//                 AsyncWait.ForSeconds(this, 1f, backend, cts.Token),
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1000, ct);
//                     Debug.Log("Tarefa Block 1");
//                 }, backend, cts.Token),
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1000, ct);
//                     Debug.Log("Tarefa Block 2");
//                 }, backend, cts.Token)
//             };
//             await AsyncWait.Block(this, blockTasks, backend, cts.Token);
//             Debug.Log($"Block concluído com backend {backend}");
//
//             // 11. Block<T>: Executa tarefas sequencialmente (com retorno)
//             Task<int>[] blockTasksWithResult = new[]
//             {
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1000, ct);
//                     return 1;
//                 }, backend, cts.Token),
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1000, ct);
//                     return 2;
//                 }, backend, cts.Token)
//             };
//             int[] blockResults = await AsyncWait.Block(this, blockTasksWithResult, backend, cts.Token);
//             Debug.Log($"Block<int> retornou [{string.Join(", ", blockResults)}] com backend {backend}");
//
//             // 12. Sync: Executa tarefas concorrentemente, espera todas (sem retorno)
//             Task[] syncTasks = new[]
//             {
//                 AsyncWait.ForSeconds(this, 2f, backend, cts.Token),
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1000, ct);
//                     Debug.Log("Tarefa Sync 1");
//                 }, backend, cts.Token),
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1500, ct);
//                     Debug.Log("Tarefa Sync 2");
//                 }, backend, cts.Token)
//             };
//             await AsyncWait.Sync(this, syncTasks, backend, cts.Token);
//             Debug.Log($"Sync concluído com backend {backend}");
//
//             // 13. Sync<T>: Executa tarefas concorrentemente, espera todas (com retorno)
//             Task<int>[] syncTasksWithResult = new[]
//             {
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1000, ct);
//                     return 1;
//                 }, backend, cts.Token),
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1500, ct);
//                     return 2;
//                 }, backend, cts.Token)
//             };
//             int[] syncResults = await AsyncWait.Sync(this, syncTasksWithResult, backend, cts.Token);
//             Debug.Log($"Sync<int> retornou [{string.Join(", ", syncResults)}] com backend {backend}");
//
//             // 14. Rush: Executa tarefas concorrentemente, retorna na primeira, deixa as demais continuarem (sem retorno)
//             Task[] rushTasks = new[]
//             {
//                 AsyncWait.ForSeconds(this, 2f, backend, cts.Token),
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1000, ct);
//                     Debug.Log("Tarefa Rush 1");
//                 }, backend, cts.Token),
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1500, ct);
//                     Debug.Log("Tarefa Rush 2");
//                 }, backend, cts.Token)
//             };
//             await AsyncWait.Rush(this, rushTasks, backend, cts.Token);
//             Debug.Log($"Rush concluído com backend {backend}");
//
//             // 15. Rush<T>: Executa tarefas concorrentemente, retorna na primeira, deixa as demais continuarem (com retorno)
//             Task<int>[] rushTasksWithResult = new[]
//             {
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1000, ct);
//                     return 1;
//                 }, backend, cts.Token),
//                 AsyncWait.ForAsync(this, async ct =>
//                 {
//                     await Task.Delay(1500, ct);
//                     return 2;
//                 }, backend, cts.Token)
//             };
//             int rushResult = await AsyncWait.Rush(this, rushTasksWithResult, backend, cts.Token);
//             Debug.Log($"Rush<int> retornou {rushResult} com backend {backend}");
//
//             // Demonstração de cancelamento
//             cts.CancelAfter(500); // Cancela após 500ms
//             try
//             {
//                 await AsyncWait.ForSeconds(this, 2f, backend, cts.Token);
//                 Debug.Log("ForSeconds após cancelamento (não deve aparecer)");
//             }
//             catch (OperationCanceledException)
//             {
//                 Debug.Log($"ForSeconds cancelado com backend {backend}");
//             }
//         }
//     }
// }
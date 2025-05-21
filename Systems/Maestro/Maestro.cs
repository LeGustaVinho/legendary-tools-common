using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LegendaryTools.Maestro
{
    public class Maestro :  IMaestro
    {
        public event Action<Maestro, bool> OnFinished;
        public event Action<MaestroTaskInfo, bool> OnTaskFinished;

        private readonly Dictionary<IMaestroTask, List<IMaestroTask>> maestroTaskDependencyMap =
            new Dictionary<IMaestroTask, List<IMaestroTask>>();
        private readonly List<MaestroTaskInfo> allMaestroNodes = new List<MaestroTaskInfo>();

        private readonly InternetProviderChecker[] hasInternetProviders = null;
        private readonly int retryInterval;

        public Maestro(InternetProviderChecker[] hasInternetProviders = null, int retryInterval = 60)
        {
            this.hasInternetProviders = hasInternetProviders;
            this.retryInterval = retryInterval;
        }
        
        public void Add(IMaestroTask task, params IMaestroTask[] dependencies)
        {
            if (!maestroTaskDependencyMap.ContainsKey(task))
                maestroTaskDependencyMap.Add(task, new List<IMaestroTask>());

            foreach (IMaestroTask dependency in dependencies)
            {
                List<IMaestroTask> cyclePath = GetDependencyPath(dependency, task);
                if (cyclePath != null)
                {
                    List<string> cycleTasks = cyclePath.Select(t => t.GetType().Name).ToList();
                    cycleTasks.Add(cycleTasks[0]);
                    string cycleMessage = string.Join(" -> ", cycleTasks);
                     throw new InvalidOperationException(
                        $"Adding a dependency from {task.GetType().Name} to {dependency.GetType().Name} would create a cyclic dependency: {cycleMessage}");
                }
                
                if (!maestroTaskDependencyMap[task].Contains(dependency))
                    maestroTaskDependencyMap[task].Add(dependency);
            }
        }

        public void Add(params IMaestroTaskWithDependency[] tasks)
        {
            foreach (IMaestroTaskWithDependency task in tasks) Add(task, task.Dependencies);
        }

        public void Add(params IMaestroTask[] tasks)
        {
            foreach (IMaestroTask task in tasks) Add(task);
        }

        public async Task Start()
        {
            bool shouldCheckInternetConnection = BuildTaskInfos();
            bool hasInternet = false;
            if (shouldCheckInternetConnection)
            {
                if(hasInternetProviders != null)
                    hasInternet = await CheckInternetConnection();
                else
                    Debugger.LogWarning<Maestro>("Some tasks requires internet but no internet provider checker has been added.", method: nameof(Start));
            }
            await TryToRunUncompletedTasks(hasInternet);
            OnFinished?.Invoke(this, IsSuccess(allMaestroNodes));
        }

        private async Task TryToRunUncompletedTasks(bool hasInternet)
        {
            List<MaestroTaskInfo> allReady = hasInternet 
                ? allMaestroNodes.FindAll(item => item.HasPrerequisites && !item.IsDone && item.Enabled) 
                : allMaestroNodes.FindAll(item => item.HasPrerequisites && !item.IsDone && item.Enabled 
                                                  && !item.RequiresInternet);
            
            bool repeat = !IsAllDone(allMaestroNodes);
            while (repeat)
            {
                List<Task> runningTasks = new List<Task>(allReady.Count);
                foreach (MaestroTaskInfo maestroNode in allReady)
                {
                    maestroNode.OnTaskCompleted += OnTaskCompleted;
                    runningTasks.Add(maestroNode.DoTaskOperation());
                }

                await Task.WhenAll(runningTasks);
                allReady = hasInternet 
                    ? allMaestroNodes.FindAll(item => item.HasPrerequisites && !item.IsDone && item.Enabled) 
                    : allMaestroNodes.FindAll(item => item.HasPrerequisites && !item.IsDone && item.Enabled 
                                                      && !item.RequiresInternet);
                repeat = !IsAllDone(allMaestroNodes);

                if (repeat && allReady.Count == 0)
                {
                    OnFinished?.Invoke(this, false);
                    Debugger.LogWarning<Maestro>("Maestro execution could not continue because no tasks were ready, no tasks had their" +
                                               " prerequisites completed. This usually occurs due to cyclic dependencies, " +
                                               "broken/exception tasks, time outed tasks or tasks requires internet.", method: nameof(Start));
                    break;
                }
            }
            
            if (!hasInternet)
            {
                List<MaestroTaskInfo> pendingInternetTasks = allMaestroNodes.FindAll(item =>
                    item.HasPrerequisites && !item.IsDone && item.Enabled
                    && item.RequiresInternet);

                if (pendingInternetTasks.Count > 0)
                {
                    string tasksWaitingInternet =
                        string.Join(", ", pendingInternetTasks.Select(item => item.MaestroTaskObject.GetType().Name).ToArray());
                    Debugger.Log<Maestro>($"Maestro didn't finish all tasks because {tasksWaitingInternet} requires internet connection, but we will retry when available.", 
                        method: nameof(CheckInternetAvailabilityLoop));
                    CheckInternetAvailabilityLoop();
                }
            }
        }

        private bool BuildTaskInfos()
        {
            Dictionary<IMaestroTask, MaestroTaskInfo> maestroTasksLookup =
                new Dictionary<IMaestroTask, MaestroTaskInfo>();

            bool shouldCheckInternetConnection = false;
            foreach (KeyValuePair<IMaestroTask, List<IMaestroTask>> pair in maestroTaskDependencyMap)
            {
                if (!maestroTasksLookup.ContainsKey(pair.Key))
                {
                    MaestroTaskInfo taskInfo = new MaestroTaskInfo(pair.Key);
                    maestroTasksLookup.Add(pair.Key, taskInfo);
                    allMaestroNodes.Add(taskInfo);
                }
                foreach (IMaestroTask dependency in pair.Value)
                {
                    if (!maestroTasksLookup.ContainsKey(dependency))
                    {
                        MaestroTaskInfo dependencyInfo = new MaestroTaskInfo(dependency);
                        maestroTasksLookup.Add(dependency, dependencyInfo);
                        allMaestroNodes.Add(dependencyInfo);
                    }
                    maestroTasksLookup[pair.Key].DependenciesInternal.Add(maestroTasksLookup[dependency]);
                }

                if (shouldCheckInternetConnection) continue;
                if (pair.Key.RequiresInternet) shouldCheckInternetConnection = true;
            }

            return shouldCheckInternetConnection;
        }

        private async void CheckInternetAvailabilityLoop()
        {
            while (true)
            {
                await Task.Delay(retryInterval * 1000);
                bool hasInternet = await CheckInternetConnection();
                Debugger.Log<Maestro>($"Retrying to run tasks, HasInternet: {hasInternet}", method: nameof(CheckInternetAvailabilityLoop));
                if (hasInternet)
                {
                    await TryToRunUncompletedTasks(hasInternet);
                    break;
                }
            }
        }

        private List<IMaestroTask> GetDependencyPath(IMaestroTask startTask, IMaestroTask targetTask)
        {
            // Perform a depth-first search to find a path from startTask to targetTask
            HashSet<IMaestroTask> visited = new HashSet<IMaestroTask>();
            Stack<(IMaestroTask task, List<IMaestroTask> path)> stack =
                new Stack<(IMaestroTask task, List<IMaestroTask> path)>();
            stack.Push((startTask, new List<IMaestroTask> { startTask }));

            while (stack.Count > 0)
            {
                (IMaestroTask currentTask, List<IMaestroTask> path) = stack.Pop();

                if (currentTask == targetTask)
                {
                    return path;
                }

                if (!visited.Contains(currentTask))
                {
                    visited.Add(currentTask);
                    if (maestroTaskDependencyMap.TryGetValue(currentTask, out List<IMaestroTask> dependencies))
                    {
                        foreach (IMaestroTask dep in dependencies)
                        {
                            List<IMaestroTask> newPath = new List<IMaestroTask>(path) { dep };
                            stack.Push((dep, newPath));
                        }
                    }
                }
            }

            return null;
        }

        private bool IsAllDone(List<MaestroTaskInfo> allMaestroNodes)
        {
            foreach (MaestroTaskInfo task in allMaestroNodes)
            {
                if (!task.IsDone)
                    return false;
            }

            return true;
        }

        private bool IsSuccess(List<MaestroTaskInfo> allMaestroNodes)
        {
            foreach (MaestroTaskInfo task in allMaestroNodes)
            {
                if (!task.IsCompleted || task.HasError)
                    return false;
            }

            return true;
        }

        private async Task<bool> CheckInternetConnection()
        {
            if (hasInternetProviders == null) return false;
            List<Task<bool>> waitingTasks = new List<Task<bool>>(hasInternetProviders.Length);
            foreach (InternetProviderChecker internetProvider in hasInternetProviders)
            {
                waitingTasks.Add(internetProvider.HasInternetConnection());
            }

            await Task.WhenAll(waitingTasks);

            foreach (Task<bool> internetProviderTask in waitingTasks)
            {
                if (!internetProviderTask.Result) return false;
            }

            return true;
        }
        
        public void Dispose()
        {
            maestroTaskDependencyMap.Clear();
        }

        private void OnTaskCompleted(MaestroTaskInfo taskInfo, bool result)
        {
            OnTaskFinished?.Invoke(taskInfo, result);
        }
    }
}
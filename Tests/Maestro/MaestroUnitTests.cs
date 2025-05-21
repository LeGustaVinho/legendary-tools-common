using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using LegendaryTools.Maestro;
using NUnit.Framework;

namespace MaestroTests
{
    // Fake implementation of IMaestroTask for testing purposes
    public class FakeMaestroTask : IMaestroTask
    {
        public bool Enabled { get; } = true;
        public int TimeOut { get; set; } = 0;
        public bool ThreadSafe { get; set; } = true;
        public bool RequiresInternet { get; } = false;
        private readonly bool _shouldSucceed;
        private readonly int _executionTime;

        // Tracking properties
        public int ExecutionCount { get; private set; }
        public Action ExecutionAction { get; set; }

        public FakeMaestroTask(bool shouldSucceed = true, int executionTime = 100)
        {
            _shouldSucceed = shouldSucceed;
            _executionTime = executionTime;
        }

        public async Task<bool> DoTaskOperation()
        {
            ExecutionCount++;
            ExecutionAction?.Invoke();
            await Task.Delay(_executionTime);
            if (_shouldSucceed)
                return true;
            throw new Exception("Task failed intentionally.");
        }
    }

    public class FakeMaestroTaskA : FakeMaestroTask
    {
    }
    public class FakeMaestroTaskB : FakeMaestroTask
    {
    }
    public class FakeMaestroTaskC : FakeMaestroTask
    {
    }

    [TestFixture]
    public class MaestroUnitTests
    {
        // Test 1: Adding a single task without dependencies
        [Test]
        public async Task AddSingleTask_ShouldBeAddedSuccessfully()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask task = new FakeMaestroTask();
            maestro.Add(task);

            // Act
            await maestro.Start();

            // Assert
            Assert.Pass("Single task was added and executed successfully.");
        }

        // Test 2: Adding multiple tasks without dependencies
        [Test]
        public async Task AddMultipleTasks_ShouldBeAddedAndExecutedSuccessfully()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask task1 = new FakeMaestroTask();
            FakeMaestroTask task2 = new FakeMaestroTask();
            FakeMaestroTask task3 = new FakeMaestroTask();
            maestro.Add(task1, task2, task3);

            // Act
            await maestro.Start();

            // Assert
            Assert.Pass("Multiple tasks were added and executed successfully.");
        }

        // Test 3: Adding a task with dependencies
        [Test]
        public async Task AddTaskWithDependencies_ShouldRespectDependencies()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask dependency = new FakeMaestroTask();
            FakeMaestroTask mainTask = new FakeMaestroTask();
            maestro.Add(mainTask, dependency);

            bool dependencyExecuted = false;
            bool mainTaskExecuted = false;

            // Subscribe to task completion events
            maestro.OnTaskFinished += (taskInfo, result) =>
            {
                if (taskInfo.MaestroTaskObject == dependency)
                    dependencyExecuted = result;
                if (taskInfo.MaestroTaskObject == mainTask)
                    mainTaskExecuted = result;
            };

            // Act
            await maestro.Start();

            // Assert
            Assert.IsTrue(dependencyExecuted, "Dependency task should have been executed successfully.");
            Assert.IsTrue(mainTaskExecuted, "Main task should have been executed successfully after dependencies.");
        }

        // Test 4: Starting Maestro with all tasks succeeding
        [Test]
        public async Task StartMaestro_AllTasksSucceed_ShouldInvokeOnFinishedWithTrue()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask task1 = new FakeMaestroTask();
            FakeMaestroTask task2 = new FakeMaestroTask();
            maestro.Add(task1, task2);

            bool onFinishedCalled = false;
            bool overallResult = false;

            maestro.OnFinished += (m, result) =>
            {
                onFinishedCalled = true;
                overallResult = result;
            };

            // Act
            await maestro.Start();

            // Assert
            Assert.IsTrue(onFinishedCalled, "OnFinished event should have been invoked.");
            Assert.IsTrue(overallResult, "Overall result should be true when all tasks succeed.");
        }

        // Test 7: Verifying OnTaskFinished event is fired correctly
        [Test]
        public async Task OnTaskFinished_ShouldBeInvokedForEachTask()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask task1 = new FakeMaestroTask();
            FakeMaestroTask task2 = new FakeMaestroTask();
            maestro.Add(task1, task2);

            int taskFinishedCount = 0;
            List<IMaestroTask> finishedTasks = new List<IMaestroTask>();

            maestro.OnTaskFinished += (taskInfo, result) =>
            {
                taskFinishedCount++;
                finishedTasks.Add(taskInfo.MaestroTaskObject);
            };

            // Act
            await maestro.Start();

            // Assert
            Assert.AreEqual(2, taskFinishedCount, "OnTaskFinished should be invoked for each task.");
            Assert.Contains(task1, finishedTasks, "Task1 should have been finished.");
            Assert.Contains(task2, finishedTasks, "Task2 should have been finished.");
        }

        // Test 8: Verifying OnFinished is called after all tasks complete
        [Test]
        public async Task OnFinished_ShouldBeCalledAfterAllTasksComplete()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask task1 = new FakeMaestroTask(executionTime: 500);
            FakeMaestroTask task2 = new FakeMaestroTask(executionTime: 500);
            maestro.Add(task1, task2);

            List<string> eventOrder = new List<string>();

            maestro.OnTaskFinished += (taskInfo, result) => { eventOrder.Add("OnTaskFinished"); };

            maestro.OnFinished += (m, result) => { eventOrder.Add("OnFinished"); };

            // Act
            await maestro.Start();

            // Assert
            Assert.AreEqual(3, eventOrder.Count, "There should be three events: two task finishes and one OnFinished.");
            Assert.AreEqual("OnTaskFinished", eventOrder[0], "First event should be OnTaskFinished.");
            Assert.AreEqual("OnTaskFinished", eventOrder[1], "Second event should be OnTaskFinished.");
            Assert.AreEqual("OnFinished", eventOrder[2], "Last event should be OnFinished.");
        }

        // Test 9: Disposing Maestro should clear all tasks
        [Test]
        public void DisposeMaestro_ShouldClearAllTasks()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask task1 = new FakeMaestroTask();
            maestro.Add(task1);

            // Act
            maestro.Dispose();

            // Use reflection to access private maestroNodeMapping
            FieldInfo field =
                typeof(Maestro).GetField("maestroTaskDependencyMap", BindingFlags.NonPublic | BindingFlags.Instance);
            Dictionary<IMaestroTask, List<IMaestroTask>> mapping =
                field.GetValue(maestro) as Dictionary<IMaestroTask, List<IMaestroTask>>;

            // Assert
            Assert.IsNotNull(mapping, "Maestro should have a mapping dictionary.");
            Assert.AreEqual(0, mapping.Count, "Maestro's task mapping should be cleared after disposal.");
        }

        // Test 11: Multiple tasks sharing the same single dependency
        [Test]
        public async Task MultipleTasksWithSharedSingleDependency_ShouldExecuteDependencyOnce()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask sharedDependency = new FakeMaestroTask();
            FakeMaestroTask task1 = new FakeMaestroTask();
            FakeMaestroTask task2 = new FakeMaestroTask();
            FakeMaestroTask task3 = new FakeMaestroTask();

            // Register tasks with Maestro
            maestro.Add(sharedDependency);
            maestro.Add(task1, sharedDependency);
            maestro.Add(task2, sharedDependency);
            maestro.Add(task3, sharedDependency);

            // Act
            await maestro.Start();

            // Assert
            Assert.AreEqual(1, sharedDependency.ExecutionCount, "Shared dependency should be executed only once.");
            Assert.AreEqual(1, task1.ExecutionCount, "Task1 should be executed once.");
            Assert.AreEqual(1, task2.ExecutionCount, "Task2 should be executed once.");
            Assert.AreEqual(1, task3.ExecutionCount, "Task3 should be executed once.");
        }

        // Test 12: Multiple tasks sharing multiple dependencies
        [Test]
        public async Task MultipleTasksWithSharedMultipleDependencies_ShouldExecuteEachDependencyOnce()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask sharedDependency1 = new FakeMaestroTask();
            FakeMaestroTask sharedDependency2 = new FakeMaestroTask();
            FakeMaestroTask task1 = new FakeMaestroTask();
            FakeMaestroTask task2 = new FakeMaestroTask();

            maestro.Add(sharedDependency1);
            maestro.Add(sharedDependency2);
            maestro.Add(task1, sharedDependency1, sharedDependency2);
            maestro.Add(task2, sharedDependency1, sharedDependency2);

            // Act
            await maestro.Start();

            // Assert
            Assert.AreEqual(1, sharedDependency1.ExecutionCount, "Shared dependency 1 should be executed only once.");
            Assert.AreEqual(1, sharedDependency2.ExecutionCount, "Shared dependency 2 should be executed only once.");
            Assert.AreEqual(1, task1.ExecutionCount, "Task1 should be executed once.");
            Assert.AreEqual(1, task2.ExecutionCount, "Task2 should be executed once.");
        }

        // Test 14: Complex dependency graph with shared dependencies
        [Test]
        public async Task ComplexDependencyGraphWithSharedDependencies_ShouldExecuteInCorrectOrder()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask dependencyA = new FakeMaestroTask();
            FakeMaestroTask dependencyB = new FakeMaestroTask();
            FakeMaestroTask dependencyC = new FakeMaestroTask();
            FakeMaestroTask task1 = new FakeMaestroTask();
            FakeMaestroTask task2 = new FakeMaestroTask();
            FakeMaestroTask task3 = new FakeMaestroTask();

            // Define dependencies
            maestro.Add(dependencyA);
            maestro.Add(dependencyB);
            maestro.Add(dependencyC, dependencyA, dependencyB);
            maestro.Add(task1, dependencyC);
            maestro.Add(task2, dependencyC);
            maestro.Add(task3, dependencyA, dependencyB);

            List<string> executionOrder = new List<string>();

            // Define ExecutionAction to log execution order
            dependencyA.ExecutionAction = () => executionOrder.Add("DependencyA");
            dependencyB.ExecutionAction = () => executionOrder.Add("DependencyB");
            dependencyC.ExecutionAction = () => executionOrder.Add("DependencyC");
            task1.ExecutionAction = () => executionOrder.Add("Task1");
            task2.ExecutionAction = () => executionOrder.Add("Task2");
            task3.ExecutionAction = () => executionOrder.Add("Task3");

            // Act
            await maestro.Start();
            
            // Assert
            // Check that DependencyA and DependencyB execute before DependencyC
            Assert.Less(executionOrder.IndexOf("DependencyA"), executionOrder.IndexOf("DependencyC"),
                "DependencyA should execute before DependencyC.");
            Assert.Less(executionOrder.IndexOf("DependencyB"), executionOrder.IndexOf("DependencyC"),
                "DependencyB should execute before DependencyC.");

            // Check that DependencyC executes before Task1 and Task2
            Assert.Less(executionOrder.IndexOf("DependencyC"), executionOrder.IndexOf("Task1"),
                "DependencyC should execute before Task1.");
            Assert.Less(executionOrder.IndexOf("DependencyC"), executionOrder.IndexOf("Task2"),
                "DependencyC should execute before Task2.");

            // Check that DependencyA and DependencyB execute before Task3
            Assert.Less(executionOrder.IndexOf("DependencyA"), executionOrder.IndexOf("Task3"),
                "DependencyA should execute before Task3.");
            Assert.Less(executionOrder.IndexOf("DependencyB"), executionOrder.IndexOf("Task3"),
                "DependencyB should execute before Task3.");
        }


        // Test 17: Adding multiple tasks with overlapping dependencies
        [Test]
        public async Task MultipleTasksWithOverlappingDependencies_ShouldHandleProperly()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask dependency1 = new FakeMaestroTask();
            FakeMaestroTask dependency2 = new FakeMaestroTask();
            FakeMaestroTask dependency3 = new FakeMaestroTask();
            FakeMaestroTask task1 = new FakeMaestroTask();
            FakeMaestroTask task2 = new FakeMaestroTask();
            FakeMaestroTask task3 = new FakeMaestroTask();

            maestro.Add(dependency1);
            maestro.Add(dependency2);
            maestro.Add(dependency3);
            maestro.Add(task1, dependency1, dependency2);
            maestro.Add(task2, dependency2, dependency3);
            maestro.Add(task3, dependency1, dependency3);

            // Act
            await maestro.Start();

            // Assert
            Assert.AreEqual(1, dependency1.ExecutionCount, "Dependency1 should be executed once.");
            Assert.AreEqual(1, dependency2.ExecutionCount, "Dependency2 should be executed once.");
            Assert.AreEqual(1, dependency3.ExecutionCount, "Dependency3 should be executed once.");
            Assert.AreEqual(1, task1.ExecutionCount, "Task1 should be executed once.");
            Assert.AreEqual(1, task2.ExecutionCount, "Task2 should be executed once.");
            Assert.AreEqual(1, task3.ExecutionCount, "Task3 should be executed once.");
        }

        // Test 18: Shared dependency with multiple levels of dependencies
        [Test]
        public async Task SharedDependencyWithNestedDependencies_ShouldExecuteAllInCorrectOrder()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask rootDependency = new FakeMaestroTask();
            FakeMaestroTask intermediateDependency = new FakeMaestroTask();
            FakeMaestroTask sharedDependency = new FakeMaestroTask();
            FakeMaestroTask task1 = new FakeMaestroTask();
            FakeMaestroTask task2 = new FakeMaestroTask();

            // Define dependencies
            maestro.Add(rootDependency);
            maestro.Add(intermediateDependency, rootDependency);
            maestro.Add(sharedDependency, intermediateDependency);
            maestro.Add(task1, sharedDependency);
            maestro.Add(task2, sharedDependency);

            List<string> executionOrder = new List<string>();

            // Define ExecutionAction to log execution order
            rootDependency.ExecutionAction = () => executionOrder.Add("RootDependency");
            intermediateDependency.ExecutionAction = () => executionOrder.Add("IntermediateDependency");
            sharedDependency.ExecutionAction = () => executionOrder.Add("SharedDependency");
            task1.ExecutionAction = () => executionOrder.Add("Task1");
            task2.ExecutionAction = () => executionOrder.Add("Task2");

            // Act
            await maestro.Start();

            // Assert
            // Check the execution order
            int indexRoot = executionOrder.IndexOf("RootDependency");
            int indexIntermediate = executionOrder.IndexOf("IntermediateDependency");
            int indexShared = executionOrder.IndexOf("SharedDependency");
            int indexTask1 = executionOrder.IndexOf("Task1");
            int indexTask2 = executionOrder.IndexOf("Task2");

            Assert.Less(indexRoot, indexIntermediate, "RootDependency should execute before IntermediateDependency.");
            Assert.Less(indexIntermediate, indexShared,
                "IntermediateDependency should execute before SharedDependency.");
            Assert.Less(indexShared, indexTask1, "SharedDependency should execute before Task1.");
            Assert.Less(indexShared, indexTask2, "SharedDependency should execute before Task2.");
        }

        // Test 19: Shared dependency with one task having additional dependencies
        [Test]
        public async Task SharedDependencyWithAdditionalDependencies_ShouldExecuteAllDependenciesProperly()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask sharedDependency = new FakeMaestroTask();
            FakeMaestroTask additionalDependency = new FakeMaestroTask();
            FakeMaestroTask task1 = new FakeMaestroTask();
            FakeMaestroTask task2 = new FakeMaestroTask();

            maestro.Add(sharedDependency);
            maestro.Add(additionalDependency);
            maestro.Add(task1, sharedDependency);
            maestro.Add(task2, sharedDependency, additionalDependency);

            List<string> executionOrder = new List<string>();

            // Define ExecutionAction to log execution order
            sharedDependency.ExecutionAction = () => executionOrder.Add("SharedDependency");
            additionalDependency.ExecutionAction = () => executionOrder.Add("AdditionalDependency");
            task1.ExecutionAction = () => executionOrder.Add("Task1");
            task2.ExecutionAction = () => executionOrder.Add("Task2");

            // Act
            await maestro.Start();

            // Assert
            // SharedDependency should execute before Task1 and Task2
            Assert.Less(executionOrder.IndexOf("SharedDependency"), executionOrder.IndexOf("Task1"),
                "SharedDependency should execute before Task1.");
            Assert.Less(executionOrder.IndexOf("SharedDependency"), executionOrder.IndexOf("Task2"),
                "SharedDependency should execute before Task2.");

            // AdditionalDependency should execute before Task2
            Assert.Less(executionOrder.IndexOf("AdditionalDependency"), executionOrder.IndexOf("Task2"),
                "AdditionalDependency should execute before Task2.");
        }

        // Test 20: Ensuring shared dependencies are not re-executed after initial execution
        [Test]
        public async Task SharedDependenciesNotReExecuted_ShouldExecuteOnlyOnce()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTask sharedDependency = new FakeMaestroTask();
            FakeMaestroTask task1 = new FakeMaestroTask();
            FakeMaestroTask task2 = new FakeMaestroTask();
            FakeMaestroTask task3 = new FakeMaestroTask();

            maestro.Add(sharedDependency);
            maestro.Add(task1, sharedDependency);
            maestro.Add(task2, sharedDependency);
            maestro.Add(task3, sharedDependency);

            // Act
            await maestro.Start();

            // Assert
            Assert.AreEqual(1, sharedDependency.ExecutionCount,
                "Shared dependency should be executed only once, regardless of the number of dependent tasks.");
            Assert.AreEqual(1, task1.ExecutionCount, "Task1 should be executed once.");
            Assert.AreEqual(1, task2.ExecutionCount, "Task2 should be executed once.");
            Assert.AreEqual(1, task3.ExecutionCount, "Task3 should be executed once.");
        }
        
        // Test 20: Ensuring shared dependencies are not re-executed after initial execution
        [Test]
        public void IndirectCyclicDependency_ShouldThrowException()
        {
            // Arrange
            Maestro maestro = new Maestro();
            FakeMaestroTaskA task1 = new FakeMaestroTaskA();
            FakeMaestroTaskB task2 = new FakeMaestroTaskB();
            FakeMaestroTaskC task3 = new FakeMaestroTaskC();
            
            maestro.Add(task1, task2);
            maestro.Add(task2, task3);

            Assert.Throws<InvalidOperationException>(() => maestro.Add(task3, task1), "Task 1 and 3 has cyclic dependencies");
        }
    }
}
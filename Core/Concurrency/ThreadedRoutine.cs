using UnityEngine;
using System.Collections;
using System.Threading;

namespace LegendaryTools.Concurrency
{
    /// <summary>
    /// Running state of a task.
    /// </summary>
    public enum ThreadedRoutineState
    {
        /// <summary>
        /// Task has been created, but has not begun.
        /// </summary>
        Init,
        /// <summary>
        /// Task is running.
        /// </summary>
        Running,
        /// <summary>
        /// Task has finished properly.
        /// </summary>
        Done,
        /// <summary>
        /// Task has been cancelled.
        /// </summary>
        Cancelled,
        /// <summary>
        /// Task terminated by errors.
        /// </summary>
        Error
    }

    /// <summary>
    /// Represents an async task.
    /// </summary>
    public class ThreadedRoutine : IEnumerator
    {
        // inner running state used by state machine;
        private enum RunningState
        {
            Init,
            RunningAsync,
            PendingYield,
            ToBackground,
            RunningSync,
            CancellationRequested,
            Done,
            Error
        }

        /// <summary>
        /// Yield return it to switch to Unity main thread.
        /// </summary>
        public static readonly object JumpToUnity = new object();
        /// <summary>
        /// Yield return it to switch to background thread.
        /// </summary>
        public static readonly object JumpBack = new object();

        /// <summary>
        /// The current iterator yield return value.
        /// </summary>
        public object Current { get; private set; }

        // temporary stores current yield return value
        // until we think Unity coroutine engine is OK to get it;
        private object pendingUserRoutineCurrent;
        private readonly IEnumerator userRoutine; // routine user want to run;
        private RunningState runningState; // current running state;
        private RunningState previousRunningState; // last running state;

        /// <summary>
        /// Gets exception during running.
        /// </summary>
        public System.Exception Exception { get; private set; }

        /// <summary>
        /// Gets state of the task.
        /// </summary>
        public ThreadedRoutineState State
        {
            get
            {
                switch (runningState)
                {
                    case RunningState.CancellationRequested:
                        return ThreadedRoutineState.Cancelled;
                    case RunningState.Done:
                        return ThreadedRoutineState.Done;
                    case RunningState.Error:
                        return ThreadedRoutineState.Error;
                    case RunningState.Init:
                        return ThreadedRoutineState.Init;
                    default:
                        return ThreadedRoutineState.Running;
                }
            }
        }

        public ThreadedRoutine(IEnumerator routine)
        {
            userRoutine = routine;
            // runs into background first;
            runningState = RunningState.Init;
        }

        /// <summary>
        /// Cancel the task till next iteration;
        /// </summary>
        public void Cancel()
        {
            if (State == ThreadedRoutineState.Running)
            {
                GotoState(RunningState.CancellationRequested);
            }
        }

        /// <summary>
        /// A co-routine that waits the task.
        /// </summary>
        public IEnumerator Wait()
        {
            while (State == ThreadedRoutineState.Running)
                yield return null;
        }

        /// <summary>
        /// Runs next iteration.
        /// </summary>
        /// <returns><code>true</code> for continue, otherwise <code>false</code>.</returns>
        public bool MoveNext()
        {
            return OnMoveNext();
        }

        public void Reset()
        {
            // Reset method not supported by iterator;
            throw new System.NotSupportedException(
                "Not support calling Reset() on iterator.");
        }

        // thread safely switch running state;
        private void GotoState(RunningState state)
        {
            if (runningState == state) return;

            lock (this)
            {
                // maintainance the previous state;
                previousRunningState = runningState;
                runningState = state;
            }
        }

        // thread safely save yield returned value;
        private void SetPendingCurrentObject(object current)
        {
            lock (this)
            {
                pendingUserRoutineCurrent = current;
            }
        }

        // actual MoveNext method, controls running state;
        private bool OnMoveNext()
        {
            // no running for null;
            if (userRoutine == null)
                return false;

            // set current to null so that Unity not get same yield value twice;
            Current = null;

            // loops until the inner routine yield something to Unity;
            while (true)
            {
                // a simple state machine;
                switch (runningState)
                {
                    // first, goto background;
                    case RunningState.Init:
                        GotoState(RunningState.ToBackground);
                        break;
                    // running in background, wait a frame;
                    case RunningState.RunningAsync:
                        return true;

                    // runs on main thread;
                    case RunningState.RunningSync:
                        MoveNextUnity();
                        break;

                    // need switch to background;
                    case RunningState.ToBackground:
                        GotoState(RunningState.RunningAsync);
                        // call the thread launcher;
                        MoveNextAsync();
                        return true;

                    // something was yield returned;
                    case RunningState.PendingYield:
                        if (pendingUserRoutineCurrent == JumpBack)
                        {
                            // do not break the loop, switch to background;
                            GotoState(RunningState.ToBackground);
                        }
                        else if (pendingUserRoutineCurrent == JumpToUnity)
                        {
                            // do not break the loop, switch to main thread;
                            GotoState(RunningState.RunningSync);
                        }
                        else
                        {
                            // not from the this system, then Unity should get noticed,
                            // Set to Current property to achieve this;
                            Current = pendingUserRoutineCurrent;

                            // yield from background thread, or main thread?
                            if (previousRunningState == RunningState.RunningAsync)
                            {
                                // if from background thread, 
                                // go back into background in the next loop;
                                pendingUserRoutineCurrent = JumpBack;
                            }
                            else
                            {
                                // otherwise go back to main thread the next loop;
                                pendingUserRoutineCurrent = JumpToUnity;
                            }

                            // end this iteration and Unity get noticed;
                            return true;
                        }
                        break;

                    // done running, pass false to Unity;
                    case RunningState.Done:
                    case RunningState.CancellationRequested:
                    default:
                        return false;
                }
            }
        }

        // background thread launcher;
        private void MoveNextAsync()
        {
            ThreadPool.QueueUserWorkItem(BackgroundRunner);
        }

        // background thread function;
        private void BackgroundRunner(object state)
        {
            // just run the sync version on background thread;
            MoveNextUnity();
        }

        // run next iteration on main thread;
        private void MoveNextUnity()
        {
            try
            {
                // run next part of the user routine;
                var result = userRoutine.MoveNext();

                if (result)
                {
                    // something has been yield returned, handle it;
                    SetPendingCurrentObject(userRoutine.Current);
                    GotoState(RunningState.PendingYield);
                }
                else
                {
                    // user routine simple done;
                    GotoState(RunningState.Done);
                }
            }
            catch (System.Exception ex)
            {
                // exception handling, save & log it;
                this.Exception = ex;
                Debug.LogError(string.Format("{0}\n{1}", ex.Message, ex.StackTrace));
                // then terminates the task;
                GotoState(RunningState.Error);
            }
        }
    }

    public static class AsyncRoutineExtension
    {
        /// <summary>
        /// Start a co-routine on a background thread.
        /// </summary>
        /// <param name="task">Gets a task object with more control on the background thread.</param>
        /// <returns></returns>
        public static Coroutine StartCoroutineAsync(this MonoBehaviour behaviour, IEnumerator routine, 
            out ThreadedRoutine task)
        {
            task = new ThreadedRoutine(routine);
            return behaviour.StartCoroutine(task);
        }

        /// <summary>
        /// Start a co-routine on a background thread.
        /// </summary>
        public static Coroutine StartCoroutineAsync(this MonoBehaviour behaviour, IEnumerator routine)
        {
            return behaviour.StartCoroutine(new ThreadedRoutine(routine));
        }
    }
}
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools
{
    public static class TaskExtension
    {
        public static void FireAndForget(this Task task)
        {
            // Attach a continuation to observe any exceptions
            task.ContinueWith(t =>
            {
                Debug.LogException(t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
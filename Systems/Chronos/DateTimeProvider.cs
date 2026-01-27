using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Chronos
{
    [CreateAssetMenu(fileName = "DateTimeProvider", menuName = "Tools/Chronos/DateTimeProvider")]
    public class DateTimeProvider : ScriptableObject
    {
        [Tooltip("Request timeout in seconds (used by providers that perform web requests).")]
        public int TimeOut = 5;

        public virtual async Task<(bool, DateTime)> GetDateTime()
        {
            await Task.Yield();
            return (true, DateTime.Now);
        }

        public virtual async Task<(bool, DateTime)> GetDateTimeUtc()
        {
            await Task.Yield();
            return (true, DateTime.UtcNow);
        }
    }
}
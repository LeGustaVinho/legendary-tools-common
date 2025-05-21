using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Chronos
{
    [CreateAssetMenu(fileName = "DateTimeProvider", menuName = "Tools/Chronos/DateTimeProvider")]
    public class DateTimeProvider : ScriptableObject
    {
        public int TimeOut;
        
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
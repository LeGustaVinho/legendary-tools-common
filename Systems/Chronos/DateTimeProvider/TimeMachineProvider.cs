using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Chronos
{
    [CreateAssetMenu(fileName = "TimeMachineProvider", menuName = "Tools/Chronos/TimeMachineProvider")]
    public class TimeMachineProvider : DateTimeProvider
    {
        public SerializedDateTime TimeMachineTime;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        [Sirenix.OdinInspector.HorizontalGroup("Buttons")]
#endif
        public void AddYear()
        {
            TimeMachineTime.Year += 1;
        }
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        [Sirenix.OdinInspector.HorizontalGroup("Buttons")]
#endif
        public void AddMonth()
        {
            TimeMachineTime.Month += 1;
        }
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        [Sirenix.OdinInspector.HorizontalGroup("Buttons")]
#endif
        public void AddDay()
        {
            TimeMachineTime.Day += 1;
        }
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        [Sirenix.OdinInspector.HorizontalGroup("Buttons")]
#endif
        public void AddHour()
        {
            TimeMachineTime.Hour += 1;
        }
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        [Sirenix.OdinInspector.HorizontalGroup("Buttons")]
#endif
        public void AddMin()
        {
            TimeMachineTime.Minute += 1;
        }
        
        public override async Task<(bool, DateTime)> GetDateTime()
        {
            await Task.Yield();
            return (true, TimeMachineTime.DateTime);
        }
        
        public override async Task<(bool, DateTime)> GetDateTimeUtc()
        {
            await Task.Yield();
            return (true, TimeMachineTime.DateTime);
        }
    }
}
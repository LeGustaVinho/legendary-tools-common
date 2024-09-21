using System;
using UnityEngine;

namespace LegendaryTools
{
    [Serializable]
    public struct SerializedTimeSpan
    {
        [SerializeField] private int day;
        [SerializeField] private int hour;
        [SerializeField] private int minute;
        [SerializeField] private int second;
        [SerializeField] private int millisecond;

        public int Day
        {
            get => day;
            set => day = Mathf.Max(0, value);
        }

        public int Hour
        {
            get => hour;
            set => hour = Mathf.Clamp(value, 0, 23);
        }

        public int Minute
        {
            get => minute;
            set => minute = Mathf.Clamp(value, 0, 59);
        }

        public int Second
        {
            get => second;
            set => second = Mathf.Clamp(value, 0, 59);
        }

        public int Millisecond
        {
            get => millisecond;
            set => millisecond = Mathf.Clamp(value, 0, 999);
        }
        
        public TimeSpan TimeSpan
        {
            get => new TimeSpan(day, hour, minute, second, millisecond);
            set
            {
                Day = value.Days;
                Hour = value.Hours;
                Minute = value.Minutes;
                Second = value.Seconds;
                Millisecond = value.Milliseconds;
            }
        }

        public SerializedTimeSpan(int days, int hours, int minutes, int seconds, int milliseconds)
        {
            this.day = Mathf.Max(0, days);
            this.hour = Mathf.Clamp(hours, 0, 23);
            this.minute = Mathf.Clamp(minutes, 0, 59);
            this.second = Mathf.Clamp(seconds, 0, 59);
            this.millisecond = Mathf.Clamp(milliseconds, 0, 999);
        }
        
        public static implicit operator TimeSpan(SerializedTimeSpan serialized)
        {
            return new TimeSpan(serialized.Day, serialized.Hour, serialized.Minute, serialized.Second, serialized.Millisecond);
        }
        
        public static implicit operator SerializedTimeSpan(TimeSpan timeSpan)
        {
            return new SerializedTimeSpan(
                timeSpan.Days,
                timeSpan.Hours,
                timeSpan.Minutes,
                timeSpan.Seconds,
                timeSpan.Milliseconds
            );
        }
    }
}
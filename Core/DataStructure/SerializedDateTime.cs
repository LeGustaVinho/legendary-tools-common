using System;
using UnityEngine;

namespace LegendaryTools
{
    [Serializable]
    public struct SerializedDateTime
    {
        [SerializeField] private int year;
        [SerializeField] private int month;
        [SerializeField] private int day;
        [SerializeField] private int hour;
        [SerializeField] private int minute;
        [SerializeField] private int second;
        
        public int Year
        {
            get => year;
            set => year = Mathf.Clamp(value, 1, 9999);
        }
        
        public int Month
        {
            get => month;
            set => month = Mathf.Clamp(value, 1, 12);
        }

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

        public DateTime DateTime
        {
            get => new DateTime(year, month, day, hour, minute, second);
            set
            {
                year = value.Year;
                month = value.Month;
                day = value.Day;
                hour = value.Hour;
                minute = value.Minute;
                second = value.Second;
            }
        }
        
        public SerializedDateTime(int year, int month, int day, int hour, int minute, int second)
        {
            this.year = year;
            this.month = month;
            this.day = day;
            this.hour = hour;
            this.minute = minute;
            this.second = second;
        }
        
        public static implicit operator DateTime(SerializedDateTime dts)
        {
            return dts.DateTime;
        }
        
        public static implicit operator SerializedDateTime(DateTime dt)
        {
            return new SerializedDateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
        }

        public static SerializedDateTime From(DateTime dt)
        {
            return new SerializedDateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
        }
    }
}
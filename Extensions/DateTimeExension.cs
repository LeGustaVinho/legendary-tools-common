using System;

namespace LegendaryTools
{
    public static class DateTimeExtension
    {
        public static string Beautify(this TimeSpan bufferTimeSpan, string days = "{0} days",
            string hours = "{0} hours", string minutes = "{0} mins", string seconds = "{0} sec")
        {
            string result = string.Empty;

            if (Math.Abs(bufferTimeSpan.TotalDays) > 1)
            {
                result += string.Format(days, bufferTimeSpan.TotalDays);
            }
            else if (Math.Abs(bufferTimeSpan.TotalHours) > 1)
            {
                result += string.Format(hours, bufferTimeSpan.TotalHours);
            }
            else if (Math.Abs(bufferTimeSpan.TotalMinutes) > 1)
            {
                result += string.Format(minutes, bufferTimeSpan.TotalMinutes);
            }
            else
            {
                result += string.Format(seconds, bufferTimeSpan.TotalSeconds);
            }

            return result;
        }

        public static string Beautify(this TimeSpan ts, string dateFormat = "d/M/yyyy HH:mm:ss")
        {
            DateTime bufferDateTime = DateTime.MinValue;
            bufferDateTime = bufferDateTime.AddSeconds(Math.Abs(ts.TotalSeconds));
            return ts.TotalSeconds < 0
                ? "- " + bufferDateTime.ToString(dateFormat)
                : bufferDateTime.ToString(dateFormat);
        }

        public static bool IsSameDay(this DateTime lhs, DateTime rhs)
        {
            return lhs.Year == rhs.Year && lhs.Month == rhs.Month && lhs.Day == rhs.Day;
        }
    }
}
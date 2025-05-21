using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace LegendaryTools.Chronos
{
    [CreateAssetMenu(fileName = "TimeApiIoProvider", menuName = "Tools/Chronos/TimeApiIoProvider")]
    public class TimeApiIoProvider : DateTimeProvider
    {
        private const string TimeApiUtcUrl = "https://timeapi.io/api/Time/current/zone?timeZone=UTC";

        public override async Task<(bool, DateTime)> GetDateTime()
        {
            try
            {
                (bool success, DateTime utcDateTime) = await GetDateTimeUtc();
                if (!success) return (false, DateTime.MinValue);

                // Converter UTC para local, se necessário
                DateTime localDateTime = utcDateTime.ToLocalTime();
                return (true, localDateTime);
            }
            catch (Exception ex)
            {
                Debug.LogError($"GetDateTime failed: {ex.Message}");
                return (false, DateTime.MinValue);
            }
        }

        public override async Task<(bool, DateTime)> GetDateTimeUtc()
        {
            try
            {
                using (UnityWebRequest request = UnityWebRequest.Get(TimeApiUtcUrl))
                {
                    request.timeout = TimeOut;
                    // Enviar a requisição e aguardar a resposta
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();

                    while (!operation.isDone) await Task.Yield();

#if UNITY_2020_1_OR_NEWER
                    if (request.result != UnityWebRequest.Result.Success)
#else
                if (request.isNetworkError || request.isHttpError)
#endif
                    {
                        Debug.LogError($"Error fetching time: {request.error}");
                        return (false, DateTime.MinValue);
                    }

                    // Deserializar a resposta JSON
                    string jsonResponse = request.downloadHandler.text;
                    TimeApiIoResponse response = JsonConvert.DeserializeObject<TimeApiIoResponse>(jsonResponse);

                    if (response == null || string.IsNullOrEmpty(response.dateTime))
                    {
                        Debug.LogError("Invalid response from Time API.");
                        return (false, DateTime.MinValue);
                    }

                    // Converter a string de data para DateTime
                    if (DateTime.TryParse(response.dateTime, out DateTime utcDateTime))
                    {
                        // Garantir que o DateTime está no formato UTC
                        utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
                        return (true, utcDateTime);
                    }

                    Debug.LogError("Failed to parse UTC DateTime.");
                    return (false, DateTime.MinValue);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"GetDateTimeUtc failed: {ex.Message}");
                return (false, DateTime.MinValue);
            }
        }

        // Classe para modelar a resposta JSON da API
        private class TimeApiIoResponse
        {
            [JsonProperty("year")] public int Year { get; set; }
            [JsonProperty("month")] public int Month { get; set; }
            [JsonProperty("day")] public int Day { get; set; }
            [JsonProperty("hour")] public int Hour { get; set; }
            [JsonProperty("minute")] public int Minute { get; set; }
            [JsonProperty("seconds")] public int Seconds { get; set; }
            [JsonProperty("milliSeconds")] public int MilliSeconds { get; set; }
            [JsonProperty("dateTime")] public string dateTime { get; set; }
            [JsonProperty("date")] public string Date { get; set; }
            [JsonProperty("time")] public string Time { get; set; }
            [JsonProperty("timeZone")] public string TimeZone { get; set; }
            [JsonProperty("dayOfWeek")] public string DayOfWeek { get; set; }
            [JsonProperty("dstActive")] public bool DstActive { get; set; }
        }
    }
}
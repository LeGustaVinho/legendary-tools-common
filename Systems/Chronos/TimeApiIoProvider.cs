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
            (bool success, DateTime utcDateTime) = await GetDateTimeUtc();
            if (!success) return (false, DateTime.MinValue);

            return (true, utcDateTime.ToLocalTime());
        }

        public override async Task<(bool, DateTime)> GetDateTimeUtc()
        {
            try
            {
                using (UnityWebRequest request = UnityWebRequest.Get(TimeApiUtcUrl))
                {
                    request.timeout = Mathf.Max(1, TimeOut);

                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

#if UNITY_2020_1_OR_NEWER
                    if (request.result != UnityWebRequest.Result.Success)
#else
                    if (request.isNetworkError || request.isHttpError)
#endif
                    {
                        Debug.LogError($"TimeApiIoProvider: Error fetching time: {request.error}");
                        return (false, DateTime.MinValue);
                    }

                    string jsonResponse = request.downloadHandler.text;
                    TimeApiIoResponse response = JsonConvert.DeserializeObject<TimeApiIoResponse>(jsonResponse);

                    if (response == null || string.IsNullOrEmpty(response.dateTime))
                    {
                        Debug.LogError("TimeApiIoProvider: Invalid response payload.");
                        return (false, DateTime.MinValue);
                    }

                    if (!DateTime.TryParse(response.dateTime, out DateTime utcDateTime))
                    {
                        Debug.LogError("TimeApiIoProvider: Failed to parse UTC DateTime.");
                        return (false, DateTime.MinValue);
                    }

                    utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
                    return (true, utcDateTime);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"TimeApiIoProvider: GetDateTimeUtc failed: {ex.Message}");
                return (false, DateTime.MinValue);
            }
        }

        [Serializable]
        private class TimeApiIoResponse
        {
            [JsonProperty("dateTime")] public string dateTime { get; set; }
        }
    }
}
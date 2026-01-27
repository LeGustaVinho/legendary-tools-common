using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace LegendaryTools.Chronos
{
    [CreateAssetMenu(fileName = "WorldTimeApiProvider", menuName = "Tools/Chronos/WorldTimeApiProvider")]
    public class WorldTimeApiProvider : DateTimeProvider
    {
        private const string TimeApiUrl = "https://worldtimeapi.org/api/timezone/Etc/UTC";

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
                using (UnityWebRequest request = UnityWebRequest.Get(TimeApiUrl))
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
                        Debug.LogError($"WorldTimeApiProvider: Error fetching time: {request.error}");
                        return (false, DateTime.MinValue);
                    }

                    string jsonResponse = request.downloadHandler.text;
                    TimeApiResponse response = JsonConvert.DeserializeObject<TimeApiResponse>(jsonResponse);

                    if (response == null || string.IsNullOrEmpty(response.UtcDateTime))
                    {
                        Debug.LogError("WorldTimeApiProvider: Invalid response payload.");
                        return (false, DateTime.MinValue);
                    }

                    if (!DateTime.TryParse(response.UtcDateTime, out DateTime utcDateTime))
                    {
                        Debug.LogError("WorldTimeApiProvider: Failed to parse UTC DateTime.");
                        return (false, DateTime.MinValue);
                    }

                    utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
                    return (true, utcDateTime);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"WorldTimeApiProvider: GetDateTimeUtc failed: {ex.Message}");
                return (false, DateTime.MinValue);
            }
        }

        [Serializable]
        private class TimeApiResponse
        {
            [JsonProperty("utc_datetime")] public string UtcDateTime { get; set; }
        }
    }
}
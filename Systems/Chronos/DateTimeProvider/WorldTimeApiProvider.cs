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
                using (UnityWebRequest request = UnityWebRequest.Get(TimeApiUrl))
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
                    TimeApiResponse response = JsonConvert.DeserializeObject<TimeApiResponse>(jsonResponse);

                    if (response == null || string.IsNullOrEmpty(response.UtcDateTime))
                    {
                        Debug.LogError("Invalid response from Time API.");
                        return (false, DateTime.MinValue);
                    }

                    // Converter a string de data para DateTime
                    if (DateTime.TryParse(response.UtcDateTime, out DateTime utcDateTime))
                    {
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
        private class TimeApiResponse
        {
            [JsonProperty("abbreviation")] public string Abbreviation { get; set; }
            [JsonProperty("client_ip")] public string ClientIp { get; set; }
            [JsonProperty("datetime")] public string DateTime { get; set; }
            [JsonProperty("day_of_week")] public int DayOfWeek { get; set; }
            [JsonProperty("day_of_year")] public int DayOfYear { get; set; }
            [JsonProperty("dst")] public bool Dst { get; set; }
            [JsonProperty("dst_from")] public string DstFrom { get; set; }
            [JsonProperty("dst_offset")] public int DstOffset { get; set; }
            [JsonProperty("dst_until")] public string DstUntil { get; set; }
            [JsonProperty("raw_offset")] public int RawOffset { get; set; }
            [JsonProperty("timezone")] public string Timezone { get; set; }
            [JsonProperty("unixtime")] public long UnixTime { get; set; }
            [JsonProperty("utc_datetime")] public string UtcDateTime { get; set; }
            [JsonProperty("utc_offset")] public string UtcOffset { get; set; }
            [JsonProperty("week_number")] public int WeekNumber { get; set; }
        }
    }
}
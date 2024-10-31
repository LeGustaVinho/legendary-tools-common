using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools
{
    [CreateAssetMenu(menuName = "Tools/LegendaryTools/InternetProviderChecker/PingInternetProviderChecker", fileName = "PingInternetProviderChecker", order = 0)]
    public class PingInternetProviderChecker : InternetProviderChecker
    {
        public string Ip = "8.8.8.8"; //Google DNS
        
        public override async Task<bool> HasInternetConnection()
        {
            Ping ping = new Ping(Ip);
            float startTime = Time.time;

            while (!ping.isDone)
            {
                if (Time.time - startTime > TimeOut)
                {
                    return false;
                }
                await Task.Yield();
            }

            return ping.time >= 0;
        }
    }
}
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LegendaryTools
{
    [CreateAssetMenu(menuName = "Tools/LegendaryTools/InternetProviderChecker/HttpInternetProviderChecker", fileName = "HttpInternetProviderChecker", order = 0)]
    public class HttpInternetProviderChecker : InternetProviderChecker
    {
        public string Url = "https://www.google.com";
        
        public override async Task<bool> HasInternetConnection()
        {
            UnityWebRequest request = UnityWebRequest.Head(Url);
            request.timeout = TimeOut;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }
            return request.result != UnityWebRequest.Result.ConnectionError &&
                   request.result != UnityWebRequest.Result.ProtocolError;
        }
    }
}
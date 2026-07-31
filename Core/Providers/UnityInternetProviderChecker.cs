using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools
{
    [CreateAssetMenu(menuName = "Legendary Tools/Internet/Provider Checkers/Unity", fileName = "UnityInternetProviderChecker", order = 0)]
    public class UnityInternetProviderChecker : InternetProviderChecker
    {
        public override async Task<bool> HasInternetConnection()
        {
            await Task.Yield();
            return Application.internetReachability != NetworkReachability.NotReachable;
        }
    }
}

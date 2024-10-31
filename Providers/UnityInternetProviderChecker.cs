using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools
{
    [CreateAssetMenu(menuName = "Tools/LegendaryTools/InternetProviderChecker/UnityInternetProviderChecker", fileName = "UnityInternetProviderChecker", order = 0)]
    public class UnityInternetProviderChecker : InternetProviderChecker
    {
        public override async Task<bool> HasInternetConnection()
        {
            await Task.Yield();
            return Application.internetReachability != NetworkReachability.NotReachable;
        }
    }
}
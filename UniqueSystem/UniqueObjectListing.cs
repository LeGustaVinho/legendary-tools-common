using System.Collections.Generic;

namespace LegendaryTools
{
    public static class UniqueObjectListing
    {
        public static readonly Dictionary<string, IUnique> UniqueObjects = new Dictionary<string, IUnique>();
        
        public static string AllocateNewGuidFor(IUnique uniqueInstance)
        {
            string newGuid;
            do
            {
                newGuid = System.Guid.NewGuid().ToString();
            } while (UniqueObjectListing.UniqueObjects.ContainsKey(newGuid));
            
            UniqueObjectListing.UniqueObjects.Add(newGuid, uniqueInstance);
            return newGuid;
        }
    }
}
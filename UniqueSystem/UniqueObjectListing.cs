using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public static class UniqueObjectListing
    {
        private static bool ShouldResetTable = true;
        public static readonly Dictionary<string, IUnique> UniqueObjects = new Dictionary<string, IUnique>();

        public static void PrepareForValidate()
        {
            if (ShouldResetTable)
            {
                UniqueObjects.Clear();
                ShouldResetTable = false;
            }
        }
        
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
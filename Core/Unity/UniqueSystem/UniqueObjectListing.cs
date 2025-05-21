using System.Collections.Generic;

namespace LegendaryTools
{
    public static class UniqueObjectListing
    {
        private static bool ShouldResetTable = true;
        public static readonly Dictionary<string, IUnique> UniqueObjects = new Dictionary<string, IUnique>();

        public static void PrepareForValidate()
        {
            // Clear the dictionary once on domain reload or first usage.
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
            } 
            while (UniqueObjects.ContainsKey(newGuid));
            
            UniqueObjects.Add(newGuid, uniqueInstance);
            return newGuid;
        }

        /// <summary>
        /// Adds or replaces the entry for a given GUID.
        /// </summary>
        public static void AddOrUpdate(string guid, IUnique unique)
        {
            if (UniqueObjects.ContainsKey(guid))
            {
                UniqueObjects[guid] = unique;
            }
            else
            {
                UniqueObjects.Add(guid, unique);
            }
        }
    }
}
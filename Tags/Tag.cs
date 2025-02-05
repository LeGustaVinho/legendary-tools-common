using System;
using UnityEngine;

namespace LegendaryTools.TagSystem
{
    [CreateAssetMenu(menuName = "Tools/Tag System/Tag")]
    public class Tag : ScriptableObject
    {
        public string Name;
        public string Description;

        /// <summary>
        /// Checks if the target contains all the specified tags.
        /// </summary>
        /// <param name="tags">Array of tags to verify.</param>
        /// <param name="target">The object that implements ITaggable.</param>
        /// <returns>True if the target contains all the tags; otherwise, false.</returns>
        public static bool HasTags(Tag[] tags, ITaggable target)
        {
            // Validate parameters
            if (tags == null || target == null) return false;

            // Optional: If the tags array is empty, consider that all tags are present
            if (tags.Length == 0) return true;

            // Iterate through each tag and check if the target contains it
            foreach (Tag tag in tags)
            {
                if (tag == null) continue; // Optionally, you can choose to return false if a tag is null
                if (!target.ContainsTag(tag)) return false; // Return false immediately if any tag is not found
            }

            // All tags were found in the target
            return true;
        }
        
        private void Reset()
        {
            Name = name;
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEditor;

namespace LegendaryTools.Editor
{
    [FilePath("ProjectSettings/UIPaletteState.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class UIPaletteState : ScriptableSingleton<UIPaletteState>
    {
        public string LabelFilter = "ui";
        public string Search = string.Empty;

        public float ThumbnailSize = 72f;

        public Tab CurrentTab = Tab.All;

        public List<string> FavoriteGuids = new();
        public List<string> RecentGuids = new();

        public int MaxRecent = 40;

        [NonSerialized] public float LastRefreshTime;

        public enum Tab
        {
            All = 0,
            Favorites = 1,
            Recent = 2
        }

        public void Save()
        {
            Save(true);
        }
    }
}
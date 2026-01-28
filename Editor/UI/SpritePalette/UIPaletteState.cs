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

        public Tab CurrentTab = Tab.Palette;

        /// <summary>
        /// User curated palette list.
        /// </summary>
        public List<string> PaletteGuids = new();

        public List<string> FavoriteGuids = new();
        public List<string> RecentGuids = new();

        public int MaxRecent = 40;

        public ThumbnailMode ThumbnailDrawMode = ThumbnailMode.Fit;

        [NonSerialized] public float LastRefreshTime;

        public enum Tab
        {
            All = 0,
            Favorites = 1,
            Recent = 2,
            Palette = 3
        }

        public enum ThumbnailMode
        {
            PreserveAspect = 0,
            Fit = 1,
            Fill = 2
        }

        public void Save()
        {
            Save(true);
        }
    }
}
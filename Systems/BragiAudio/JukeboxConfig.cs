using UnityEngine;

namespace LegendaryTools.Bragi
{
    public enum JukeboxPlayMode
    {
        Sequential,
        Random,
        RandomReSeed,
    }
    
    public enum JukeboxTransition
    {
        Hard,
        Fade
    }
    
    [CreateAssetMenu(menuName = "Legendary Tools/Bragi/Configuration/Jukebox")]
    public class JukeboxConfig : ScriptableObject
    {
        public bool AutoStart;
        public AudioConfigBase[] Tracks;

        [Header("Settings")] 
        public JukeboxPlayMode PlayMode = JukeboxPlayMode.Sequential;
        public JukeboxTransition Transition = JukeboxTransition.Hard;
        public bool Repeat;
        public bool CircularTracks = true;
    }
}

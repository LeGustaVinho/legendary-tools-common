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
    
    [CreateAssetMenu(menuName = "Tools/Bragi/JukeboxConfig")]
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
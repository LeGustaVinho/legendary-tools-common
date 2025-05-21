using System;
using UnityEngine;
using UnityEngine.Audio;

namespace LegendaryTools.Bragi
{
    [Serializable]
    public class AudioSettings
    {
        [Header("Settings")] 
        public AudioMixerGroup AudioMixerGroup;
        public bool BypassEffects;
        public bool BypassListenerEffects;
        public bool BypassReverbZones;
        public bool Loop;

        [Range(0, 256)] public int Priority = 128;
        [Range(0, 1)] public float Volume = 1;
        [Range(-3, 3)] public float Pitch = 1;
        [Range(-1, 1)] public float StereoPan;
        [Range(0, 1)] public float SpatialBlend;
        [Range(0, 1.1f)] public float ReverbZoneMix = 1;

        [Header("3D Sound Settings"), Range(0, 5)]
        public float DopplerLevel = 1;

        [Range(0, 360)] public float Spread;
        public float MinDistance = 1;
        public float MaxDistance = 500;

        [Header("Misc")] 
        public float FadeInDuration;
        public float FadeOutDuration;

        public void CopyFrom(AudioSettings audioSettings)
        {
            AudioMixerGroup = audioSettings.AudioMixerGroup;
            BypassEffects = audioSettings.BypassEffects;
            BypassListenerEffects = audioSettings.BypassListenerEffects;
            BypassReverbZones = audioSettings.BypassReverbZones;
            Loop = audioSettings.Loop;

            Priority = audioSettings.Priority;
            Volume = audioSettings.Volume;
            Pitch = audioSettings.Pitch;
            StereoPan = audioSettings.StereoPan;
            SpatialBlend = audioSettings.SpatialBlend;
            ReverbZoneMix = audioSettings.ReverbZoneMix;

            DopplerLevel = audioSettings.DopplerLevel;

            Spread = audioSettings.Spread;
            MinDistance = audioSettings.MinDistance;
            MaxDistance = audioSettings.MaxDistance;

            FadeInDuration = audioSettings.FadeInDuration;
            FadeOutDuration = audioSettings.FadeOutDuration;
        }
    }
    
    [CreateAssetMenu(menuName = "Tools/Bragi/AudioConfig")]
    public class AudioConfig : AudioConfigBase
    {
        public AudioSettings AudioSettings;
        
        public override AudioHandler[] Play(AudioSettings overrideSettings = null, bool allowFading = true)
        {
            return new[] {Bragi.Instance.Play(this, overrideSettings, allowFading)};
        }

        public override AudioHandler[] Play(Vector3 position, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            return new[] {Bragi.Instance.Play(position, this, overrideSettings, allowFading)};
        }

        public override AudioHandler[] Play(Transform parent, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            return new[] {Bragi.Instance.Play(parent, this, overrideSettings, allowFading)};
        }
        
#if UNITY_EDITOR
        [ContextMenu("ClearLoadedRef")]
        public void ClearLoadedRef()
        {
        }
#endif
    }
}
using System;
using System.Collections;
using UnityEngine;

namespace LegendaryTools.Bragi
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioHandler : MonoBehaviour, IPoolable, IPlayable
    {
        public Transform VirtualParent;
        public bool AllowFading = true;
        
        public bool IsInitialized { private set; get; }
        public AudioConfig Config { private set; get; }
        public AudioSettings Settings { private set; get; }
        public Vector3 Position
        {
            set => transform.position = value;
            get => transform.position;
        }
        
        public bool IsMuted => audioSource.mute;
        public bool IsPlaying => audioSource.isPlaying;
        public bool IsPaused { private set; get; }

        public float Time
        {
            get => audioSource.time;
            set => audioSource.time = value;
        }

        public int TimeSamples
        {
            get => audioSource.timeSamples;
            set => audioSource.timeSamples = value;
        }

        public float Pitch
        {
            get => audioSource.pitch;
            set => audioSource.pitch = value;
        }

        public event Action<AudioHandler> OnPlay;
        public event Action<AudioHandler> OnFinished;
        public event Action<AudioHandler> OnStop;
        public event Action<AudioHandler, bool> OnPause;
        public event Action<AudioHandler, bool> OnMute;
        public event Action<AudioHandler> OnDispose;
        
        private AudioSource audioSource;
        private Coroutine playingRoutine;
        private Coroutine loadingRoutine;
        private Coroutine fadeRoutine;

        protected void Awake()
        {
            EnsureHasAudioSource();
        }

        public void OnConstruct()
        {
            EnsureHasAudioSource();
        }

        public void OnCreate()
        {
        }

        public void OnRecycle()
        {
            IsInitialized = false;
            AllowFading = true;
            Config = null;
            Settings = null;
            IsPaused = false;
            VirtualParent = null;

            RemoveRefs();
            
            OnPlay = null;
            OnFinished  = null;
            OnStop = null;
            OnPause = null;
            OnMute = null;
            OnDispose = null;

            if (playingRoutine != null)
            {
                MonoBehaviourFacade.Instance.StopRoutine(playingRoutine);
                playingRoutine = null;
            }
            
            if (loadingRoutine != null)
            {
                MonoBehaviourFacade.Instance.StopRoutine(loadingRoutine);
                loadingRoutine = null;
            }
            
            if (fadeRoutine != null)
            {
                MonoBehaviourFacade.Instance.StopRoutine(fadeRoutine);
                fadeRoutine = null;
            }
        }

        public void Initialize(AudioConfig audioConfig, AudioSettings settings, bool allowFading = true)
        {
            AllowFading = allowFading;
            Config = audioConfig;
            Settings = settings;
            ApplyAudioSettingsToSource(audioSource, settings);
            IsInitialized = true;

            if (Config.AssetLoadableConfig.IsLoaded)
            {
                OnAudioLoaded();
            }
            else
            {
                loadingRoutine = MonoBehaviourFacade.Instance.StartRoutine(WaitAudioLoad(OnAudioLoaded));
            }
        }

        [ContextMenu("Play")]
        public void Play()
        {
            Play(0);
        }

        public void Play(float fadeTime = 0)
        {
            audioSource.Play();

            if (AllowFading)
            {
                if (Settings.FadeInDuration > 0 || fadeTime > 0)
                {
                    audioSource.volume = 0;
                    fadeRoutine = MonoBehaviourFacade.Instance.StartRoutine(FadeVolume(0, Settings.Volume,
                        fadeTime > 0 ? fadeTime : Settings.FadeInDuration));
                }
            }

            playingRoutine = MonoBehaviourFacade.Instance.StartRoutine(WaitAudioFinish());
            OnPlay?.Invoke(this);
        }

        /// <summary>
        /// Stop without any fade
        /// </summary>
        public void StopNow()
        {
            audioSource.Stop();
            OnStop?.Invoke(this);
            Dispose();
        }
        
        [ContextMenu("Stop")]
        public void Stop()
        {
            Stop(0);
        }

        public void Stop(float fadeTime = 0)
        {
            if (fadeTime == 0 && Settings.FadeOutDuration == 0)
            {
                StopNow();
            }
            else
            {
                if (AllowFading)
                {
                    fadeRoutine = MonoBehaviourFacade.Instance.StartRoutine(
                        FadeVolume(audioSource.volume, 0,
                            fadeTime > 0 ? fadeTime : Settings.FadeOutDuration, StopNow));
                }
                else
                {
                    StopNow();
                }
            }
        }

        [ContextMenu("Pause")]
        public void Pause()
        {
            audioSource.Pause();
            IsPaused = true;
            OnPause?.Invoke(this, true);
        }
        
        [ContextMenu("UnPause")]
        public void UnPause()
        {
            audioSource.UnPause();
            IsPaused = false;
            OnPause?.Invoke(this, false);
        }
        
        [ContextMenu("Mute")]
        public void Mute()
        {
            audioSource.mute = true;
            OnMute?.Invoke(this, true);
        }
        
        [ContextMenu("UnMute")]
        public void UnMute()
        {
            audioSource.mute = false;
            OnMute?.Invoke(this, false);
        }

        private void OnAudioLoaded()
        {
            audioSource.clip = (AudioClip)Config.AssetLoadableConfig.LoadedAsset;

            if (!IsPaused)
            {
                Play();
            }
        }
        
        private IEnumerator WaitAudioFinish()
        {
            yield return new WaitUntil(() =>!audioSource.isPlaying);
            playingRoutine = null;
            OnFinished?.Invoke(this);
            Dispose();
        }
        
        private IEnumerator WaitAudioLoad(Action onAudioLoadCompleted)
        {
            if (!Config.AssetLoadableConfig.IsLoaded)
            {
                if (!Config.AssetLoadableConfig.IsLoading) //Prevents loading, because the asset is being loaded
                {
                    Config.AssetLoadableConfig.PrepareLoadRoutine<AudioClip>();
                    yield return Config.AssetLoadableConfig.WaitLoadRoutine();
                }
                else
                {
                    yield return new WaitUntil(() => Config.AssetLoadableConfig.IsLoaded);
                }
            }
            
            onAudioLoadCompleted.Invoke();
        }

        private IEnumerator FadeVolume(float from, float to, float duration, Action onCompleted = null)
        {
            float time = 0;
            while (time < duration)
            {
                audioSource.volume = Mathf.Lerp(from, to, time / duration);
                time += UnityEngine.Time.deltaTime;
                yield return null; //wait a frame
            }
            audioSource.volume = to;
            onCompleted?.Invoke();
            fadeRoutine = null;
        }
                
        private void ApplyAudioSettingsToSource(AudioSource audioSource, AudioSettings overrideSettings)
        {
            audioSource.outputAudioMixerGroup = overrideSettings.AudioMixerGroup;
            audioSource.bypassEffects = overrideSettings.BypassEffects;
            audioSource.bypassListenerEffects = overrideSettings.BypassListenerEffects;
            audioSource.bypassReverbZones = overrideSettings.BypassReverbZones;
            audioSource.loop = overrideSettings.Loop;
            audioSource.priority = overrideSettings.Priority;
            audioSource.volume = overrideSettings.Volume;
            audioSource.pitch = overrideSettings.Pitch;
            audioSource.panStereo = overrideSettings.StereoPan;
            audioSource.spatialBlend = overrideSettings.SpatialBlend;
            audioSource.reverbZoneMix = overrideSettings.ReverbZoneMix;
            audioSource.dopplerLevel = overrideSettings.DopplerLevel;
            audioSource.spread = overrideSettings.Spread;
            audioSource.minDistance = overrideSettings.MinDistance;
            audioSource.maxDistance = overrideSettings.MaxDistance;
            audioSource.playOnAwake = false;
        }

        private void Update()
        {
            if (VirtualParent != null)
            {
                Position = VirtualParent.position;
            }

            if (IsInitialized)
            {
                if (IsPlaying)
                {
                    if (Settings.FadeOutDuration > 0)
                    {
                        if (Time >= ((AudioClip)Config.AssetLoadableConfig.LoadedAsset).length - Settings.FadeOutDuration)
                        {
                            if (fadeRoutine == null)
                            {
                                fadeRoutine = MonoBehaviourFacade.Instance.StartRoutine(
                                    FadeVolume(audioSource.volume, 0, Settings.FadeOutDuration));
                            }
                        }
                    }
                }
            }
        }

        private void RemoveRefs()
        {
            if (audioSource != null)
            {
                audioSource.clip = null;
                audioSource.outputAudioMixerGroup = null;
            }
        }

        private void Dispose()
        {
            OnDispose?.Invoke(this);
            
            if (!Config.AssetLoadableConfig.DontUnloadAfterLoad)
            {
                RemoveRefs();
                Config.AssetLoadableConfig.Unload();
            }

            Pool.Destroy(this);
        }

        private void EnsureHasAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
    }
}
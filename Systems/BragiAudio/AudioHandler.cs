using System;
using System.Threading;
using System.Threading.Tasks;
using LegendaryTools.Concurrency;
using UnityEngine;

namespace LegendaryTools.Bragi
{
    /// <summary>
    /// Manages audio playback with support for fading, pooling, and async operations.
    /// Requires an AudioSource component to function.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioHandler : MonoBehaviour, IPoolable, IPlayable
    {
        /// <summary>
        /// Virtual parent transform to follow, if set.
        /// </summary>
        public Transform VirtualParent;

        /// <summary>
        /// Determines whether fading effects (in/out) are allowed.
        /// </summary>
        public bool AllowFading = true;

        /// <summary>
        /// Indicates whether the AudioHandler is initialized.
        /// </summary>
        public bool IsInitialized { private set; get; }

        /// <summary>
        /// Configuration settings for the audio clip.
        /// </summary>
        public AudioConfig Config { private set; get; }

        /// <summary>
        /// Audio playback settings.
        /// </summary>
        public AudioSettings Settings { private set; get; }

        /// <summary>
        /// Gets or sets the position of the AudioHandler in world space.
        /// </summary>
        public Vector3 Position
        {
            get => transform.position;
            set => transform.position = value;
        }

        /// <summary>
        /// Gets whether the audio is muted.
        /// </summary>
        public bool IsMuted => audioSource != null && audioSource.mute;

        /// <summary>
        /// Gets whether the audio is currently playing.
        /// </summary>
        public bool IsPlaying => audioSource != null && audioSource.isPlaying;

        /// <summary>
        /// Gets whether the audio is paused.
        /// </summary>
        public bool IsPaused { private set; get; }

        /// <summary>
        /// Gets or sets the current playback time in seconds.
        /// </summary>
        public float Time
        {
            get => audioSource != null ? audioSource.time : 0f;
            set { if (audioSource != null) audioSource.time = value; }
        }

        /// <summary>
        /// Gets or sets the current playback time in samples.
        /// </summary>
        public int TimeSamples
        {
            get => audioSource != null ? audioSource.timeSamples : 0;
            set { if (audioSource != null) audioSource.timeSamples = value; }
        }

        /// <summary>
        /// Gets or sets the pitch of the audio.
        /// </summary>
        public float Pitch
        {
            get => audioSource != null ? audioSource.pitch : 1f;
            set { if (audioSource != null) audioSource.pitch = value; }
        }

        public bool IsFading { get; private set; }

        /// <summary>
        /// Event triggered when audio playback starts.
        /// </summary>
        public event Action<AudioHandler> OnPlay;

        /// <summary>
        /// Event triggered when audio playback finishes naturally.
        /// </summary>
        public event Action<AudioHandler> OnFinished;

        /// <summary>
        /// Event triggered when audio playback is stopped manually.
        /// </summary>
        public event Action<AudioHandler> OnStop;

        /// <summary>
        /// Event triggered when the audio is paused or unpaused.
        /// </summary>
        public event Action<AudioHandler, bool> OnPause;

        /// <summary>
        /// Event triggered when the audio is muted or unmuted.
        /// </summary>
        public event Action<AudioHandler, bool> OnMute;

        /// <summary>
        /// Event triggered when the AudioHandler is disposed.
        /// </summary>
        public event Action<AudioHandler> OnDispose;

        private AudioSource audioSource;
        private float clipLength; // Cache for audio clip length to optimize fading checks

        /// <summary>
        /// Called when the MonoBehaviour is created.
        /// Ensures an AudioSource component is attached.
        /// </summary>
        protected void Awake()
        {
            EnsureHasAudioSource();
        }

        /// <summary>
        /// Called when the object is constructed from a pool.
        /// </summary>
        public void OnConstruct()
        {
            EnsureHasAudioSource();
        }

        /// <summary>
        /// Called when the object is created in the pool.
        /// </summary>
        public void OnCreate()
        {
        }

        /// <summary>
        /// Resets the AudioHandler to its initial state for recycling in the pool.
        /// </summary>
        public void OnRecycle()
        {
            try
            {
                IsInitialized = false;
                AllowFading = true;
                Config = null;
                Settings = null;
                IsPaused = false;
                VirtualParent = null;
                clipLength = 0f;

                RemoveRefs();

                OnPlay = null;
                OnFinished = null;
                OnStop = null;
                OnPause = null;
                OnMute = null;
                OnDispose = null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error recycling AudioHandler: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Initializes the AudioHandler with the provided audio configuration and settings.
        /// </summary>
        /// <param name="audioConfig">The audio configuration to use.</param>
        /// <param name="settings">The audio settings to apply.</param>
        /// <param name="allowFading">Whether fading effects are allowed.</param>
        /// <param name="cancellationToken">Token to cancel the initialization.</param>
        /// <exception cref="ArgumentNullException">Thrown if audioConfig or settings is null.</exception>
        public async Task Initialize(AudioConfig audioConfig, AudioSettings settings, bool allowFading = true, CancellationToken cancellationToken = default)
        {
            if (audioConfig == null) throw new ArgumentNullException(nameof(audioConfig));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (audioSource == null)
            {
                Debug.LogError("AudioSource is missing in AudioHandler.");
                return;
            }

            try
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
                    if (!Config.AssetLoadableConfig.IsLoading)
                    {
                        await Config.AssetLoadableConfig.LoadAsync<AudioClip>(cancellationToken);
                    }
                    else
                    {
                        await AsyncWait.Until(() => Config.AssetLoadableConfig.IsLoaded,
                            Config.AssetLoadableConfig.AsyncWaitBackend, cancellationToken);
                    }

                    OnAudioLoaded();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("AudioHandler initialization cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize AudioHandler: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Starts audio playback immediately.
        /// </summary>
        [ContextMenu("Play")]
        public void Play()
        {
            _ = PlayAsync(0, CancellationToken.None);
        }

        /// <summary>
        /// Starts audio playback asynchronously with optional fading.
        /// </summary>
        /// <param name="fadeTime">Duration of the fade-in effect, if allowed.</param>
        /// <param name="cancellationToken">Token to cancel the playback.</param>
        public async Task PlayAsync(float fadeTime = 0, CancellationToken cancellationToken = default)
        {
            if (!IsInitialized || audioSource == null || Config?.AssetLoadableConfig?.LoadedAsset == null)
            {
                Debug.LogWarning("Cannot play audio: AudioHandler not initialized or missing required components.");
                return;
            }

            try
            {
                audioSource.Play();
                OnPlay?.Invoke(this);

                if (AllowFading && (Settings.FadeInDuration > 0 || fadeTime > 0))
                {
                    audioSource.volume = 0;
                    await FadeVolumeAsync(0, Settings.Volume, fadeTime > 0 ? fadeTime : Settings.FadeInDuration, cancellationToken: cancellationToken);
                }

                await WaitAudioFinishAsync(cancellationToken);
                OnFinished?.Invoke(this);
                Dispose();
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("Audio playback cancelled.");
                StopNow();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during audio playback: {ex.Message}");
                StopNow();
            }
        }

        /// <summary>
        /// Stops the audio with an optional fade-out effect.
        /// </summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            _ = StopAsync(0);
        }
        
        /// <summary>
        /// Stops audio playback immediately without fading.
        /// </summary>
        public void StopNow()
        {
            if (audioSource != null)
            {
                audioSource.Stop();
            }
            OnStop?.Invoke(this);
            Dispose();
        }

        /// <summary>
        /// Stops audio playback with optional fading.
        /// </summary>
        [ContextMenu("Stop")]
        public async Task StopAsync(float fadeTime = 0, CancellationToken cancellationToken = default)
        {
            if (!IsInitialized || audioSource == null)
            {
                Debug.LogWarning("Cannot stop audio: AudioHandler not initialized or missing AudioSource.");
                return;
            }

            try
            {
                if (fadeTime == 0 && Settings.FadeOutDuration == 0)
                {
                    StopNow();
                }
                else if (AllowFading)
                {
                    await FadeVolumeAsync(audioSource.volume, 0, fadeTime > 0 ? fadeTime : Settings.FadeOutDuration, StopNow, cancellationToken);
                }
                else
                {
                    StopNow();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("Audio stop cancelled.");
                StopNow();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error stopping audio: {ex.Message}");
                StopNow();
            }
        }

        /// <summary>
        /// Pauses audio playback.
        /// </summary>
        [ContextMenu("Pause")]
        public void Pause()
        {
            if (audioSource == null) return;
            audioSource.Pause();
            IsPaused = true;
            OnPause?.Invoke(this, true);
        }

        /// <summary>
        /// Resumes audio playback.
        /// </summary>
        [ContextMenu("UnPause")]
        public void UnPause()
        {
            if (audioSource == null) return;
            audioSource.UnPause();
            IsPaused = false;
            OnPause?.Invoke(this, false);
        }

        /// <summary>
        /// Mutes the audio.
        /// </summary>
        [ContextMenu("Mute")]
        public void Mute()
        {
            if (audioSource == null) return;
            audioSource.mute = true;
            OnMute?.Invoke(this, true);
        }

        /// <summary>
        /// Unmutes the audio.
        /// </summary>
        [ContextMenu("UnMute")]
        public void UnMute()
        {
            if (audioSource == null) return;
            audioSource.mute = false;
            OnMute?.Invoke(this, false);
        }

        /// <summary>
        /// Handles the audio clip loading completion.
        /// </summary>
        private void OnAudioLoaded()
        {
            if (Config?.AssetLoadableConfig?.LoadedAsset == null || audioSource == null)
            {
                Debug.LogWarning("Cannot load audio: Missing configuration or AudioSource.");
                return;
            }

            if (Config.AssetLoadableConfig.LoadedAsset is AudioClip clip)
            {
                audioSource.clip = clip;
                clipLength = audioSource.clip.length; // Cache clip length
                if (!IsPaused)
                {
                    _ = PlayAsync(0, CancellationToken.None);
                }
            }
            else
            {
                Debug.LogError($"Loaded asset is not an AudioClip. Type: {Config.AssetLoadableConfig.LoadedAsset.GetType().Name}");
            }
        }

        /// <summary>
        /// Waits for the audio to finish playing and handles cleanup.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the wait operation.</param>
        private async Task WaitAudioFinishAsync(CancellationToken cancellationToken)
        {
            if (audioSource == null) return;

            try
            {
                await AsyncWait.Until(() => !audioSource.isPlaying, Bragi.Instance.AsyncWaitBackend, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("Audio finish wait cancelled.");
                throw;
            }
        }

        /// <summary>
        /// Fades the audio volume from one value to another over a specified duration.
        /// </summary>
        /// <param name="from">Starting volume.</param>
        /// <param name="to">Target volume.</param>
        /// <param name="duration">Duration of the fade effect.</param>
        /// <param name="onCompleted">Action to invoke when fading is complete.</param>
        /// <param name="cancellationToken">Token to cancel the fade operation.</param>
        private async Task FadeVolumeAsync(float from, float to, float duration, Action onCompleted = null, CancellationToken cancellationToken = default)
        {
            if (audioSource == null) return;

            try
            {
                float time = 0;
                while (time < duration)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    audioSource.volume = Mathf.Lerp(from, to, time / duration);
                    time += UnityEngine.Time.deltaTime;
                    await AsyncWait.ForEndOfFrame(Bragi.Instance.AsyncWaitBackend, cancellationToken);
                }
                audioSource.volume = to;
                onCompleted?.Invoke();
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("Audio fade cancelled.");
                throw;
            }
        }

        /// <summary>
        /// Handles automatic fading near the end of the audio clip.
        /// </summary>
        private async Task HandleFadingAsync()
        {
            if (!IsInitialized || !IsPlaying || Settings == null || Settings.FadeOutDuration <= 0 || clipLength <= 0 || IsFading)
                return;

            if (Time >= clipLength - Settings.FadeOutDuration)
            {
                IsFading = true;
                try
                {
                    await FadeVolumeAsync(audioSource.volume, 0, Settings.FadeOutDuration, cancellationToken: CancellationToken.None);
                }
                finally
                {
                    IsFading = false;
                }
            }
        }

        /// <summary>
        /// Updates the AudioHandler, handling virtual parent tracking and fading.
        /// </summary>
        private async void Update()
        {
            if (VirtualParent != null)
            {
                Position = VirtualParent.position;
            }

            if (IsPlaying && AllowFading)
            {
                await HandleFadingAsync();
            }
        }

        /// <summary>
        /// Applies audio settings to the AudioSource component.
        /// </summary>
        /// <param name="audioSource">The AudioSource to configure.</param>
        /// <param name="settings">The settings to apply.</param>
        private void ApplyAudioSettingsToSource(AudioSource audioSource, AudioSettings settings)
        {
            if (audioSource == null || settings == null) return;

            audioSource.outputAudioMixerGroup = settings.AudioMixerGroup;
            audioSource.bypassEffects = settings.BypassEffects;
            audioSource.bypassListenerEffects = settings.BypassListenerEffects;
            audioSource.bypassReverbZones = settings.BypassReverbZones;
            audioSource.loop = settings.Loop;
            audioSource.priority = settings.Priority;
            audioSource.volume = settings.Volume;
            audioSource.pitch = settings.Pitch;
            audioSource.panStereo = settings.StereoPan;
            audioSource.spatialBlend = settings.SpatialBlend;
            audioSource.reverbZoneMix = settings.ReverbZoneMix;
            audioSource.dopplerLevel = settings.DopplerLevel;
            audioSource.spread = settings.Spread;
            audioSource.minDistance = settings.MinDistance;
            audioSource.maxDistance = settings.MaxDistance;
            audioSource.playOnAwake = false;
        }

        /// <summary>
        /// Clears references to audio resources.
        /// </summary>
        private void RemoveRefs()
        {
            if (audioSource != null)
            {
                audioSource.clip = null;
                audioSource.outputAudioMixerGroup = null;
            }
        }

        /// <summary>
        /// Disposes of the AudioHandler, releasing resources and returning it to the pool.
        /// </summary>
        private void Dispose()
        {
            OnDispose?.Invoke(this);

            if (Config?.AssetLoadableConfig != null && !Config.AssetLoadableConfig.DontUnloadAfterLoad)
            {
                RemoveRefs();
                Config.AssetLoadableConfig.Unload();
            }

            Pool.Destroy(this);
        }

        /// <summary>
        /// Ensures the AudioSource component is attached to the GameObject.
        /// </summary>
        private void EnsureHasAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                Debug.LogWarning($"AudioSource was missing on {gameObject.name} and was automatically added.");
            }
        }
    }
}
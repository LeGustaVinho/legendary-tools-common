using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace LegendaryTools.Bragi
{
    public class Jukebox : SingletonBehaviour<Jukebox>, IPlayable
    {
        public JukeboxConfig Config;
        
        public bool IsMuted => currentHandlers?.Any(item => item.IsMuted) ?? false;
        public bool IsPlaying => currentHandlers?.Any(item => item.IsPlaying) ?? false;
        public bool IsPaused => currentHandlers?.Any(item => item.IsPaused) ?? false;

        private List<AudioConfigBase> randomOrderTracks;
        private int currentTrackIndex;
        private AudioHandler[] currentHandlers;
        private readonly Random rnGod = new Random();
        
        [ContextMenu("Play")]
        public void Play()
        {
            switch (Config.PlayMode)
            {
                case JukeboxPlayMode.Sequential:
                {
                    currentHandlers = Config.Tracks[currentTrackIndex].Play(allowFading: Config.Transition == JukeboxTransition.Fade);
                    break;
                }
                case JukeboxPlayMode.Random:
                {
                    if (randomOrderTracks == null)
                    {
                        GenerateShuffledTracks();
                    }
                    else
                    {
                        currentHandlers = randomOrderTracks[currentTrackIndex].Play(allowFading: Config.Transition == JukeboxTransition.Fade);
                    }
                    break;
                }
            }

            foreach (AudioHandler audioHandler in currentHandlers)
            {
                audioHandler.OnFinished += OnAudioHandlerFinished;
            }
        }

        [ContextMenu("Next")]
        public void Next()
        {
            StopNowAllCurrentHandlers();
            currentTrackIndex = (currentTrackIndex + 1) % Config.Tracks.Length;
            Play();
        }
        
        [ContextMenu("Prev")]
        public void Prev()
        {
            StopNowAllCurrentHandlers();
            currentTrackIndex--;
            if (currentTrackIndex < 0)
            {
                currentTrackIndex = Config.Tracks.Length - 1;
            }
            Play();
        }

        [ContextMenu("Stop")]
        public void Stop()
        {
            foreach (AudioHandler audioHandler in currentHandlers)
            {
                if (audioHandler.IsPlaying)
                {
                    audioHandler.Stop();
                }
            }
        }

        [ContextMenu("Pause")]
        public void Pause()
        {
            foreach (AudioHandler audioHandler in currentHandlers)
            {
                if (audioHandler.IsPlaying)
                {
                    audioHandler.Pause();
                }
            }
        }

        [ContextMenu("UnPause")]
        public void UnPause()
        {
            foreach (AudioHandler audioHandler in currentHandlers)
            {
                if (audioHandler.IsPaused)
                {
                    audioHandler.UnPause();
                }
            }
        }

        [ContextMenu("Mute")]
        public void Mute()
        {
            foreach (AudioHandler audioHandler in currentHandlers)
            {
                if (!audioHandler.IsMuted)
                {
                    audioHandler.Mute();
                }
            }
        }

        [ContextMenu("UnMute")]
        public void UnMute()
        {
            foreach (AudioHandler audioHandler in currentHandlers)
            {
                if (audioHandler.IsMuted)
                {
                    audioHandler.UnMute();
                }
            }
        }
        
        protected override void Start()
        {
            base.Start();
            if (Config != null)
            {
                if (Config.AutoStart)
                {
                    Play();
                }
            }
        }
        
        void GenerateShuffledTracks()
        {
            randomOrderTracks = new List<AudioConfigBase>(Config.Tracks);
            randomOrderTracks.Shuffle(rnGod);
        }

        void OnAudioHandlerFinished(AudioHandler audioHandler)
        {
            audioHandler.OnFinished -= OnAudioHandlerFinished;
            if (currentHandlers.All(item => !item.IsPlaying))
            {
                currentHandlers = null;

                if (Config.Repeat)
                {
                    Play();
                    return;
                }
                
                currentTrackIndex = (currentTrackIndex + 1) % Config.Tracks.Length;
                if (currentTrackIndex == 0)
                {
                    if (Config.CircularTracks)
                    {
                        if (Config.PlayMode == JukeboxPlayMode.RandomReSeed)
                        {
                            GenerateShuffledTracks();
                        }
                        
                        Play();
                    }
                }
                else
                {
                    Play();
                }
            }
        }
        
        void StopNowAllCurrentHandlers()
        {
            foreach (AudioHandler audioHandler in currentHandlers)
            {
                if (audioHandler.IsPlaying)
                {
                    audioHandler.StopNow();
                }
            }
        }
    }
}
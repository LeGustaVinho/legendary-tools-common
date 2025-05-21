using System;
using System.Collections;
using UnityEngine;

namespace LegendaryTools.Bragi
{
    [Serializable]
    public struct AudioWeight : IRandomWeight
    {
        public AudioConfig AudioConfig;
        public float Weight => WeightField;
        public float WeightField;
    }

    public enum AudioGroupPlayMode
    {
        Sequential,
        SequentialChained,
        Random,
        Simultaneous,
    }

    [CreateAssetMenu(menuName = "Tools/Bragi/AudioGroup")]
    public class AudioGroup : AudioConfigBase
    {
        public AudioGroupPlayMode PlayMode = AudioGroupPlayMode.Random;
        public AudioWeight[] Audios;

        int sequenceIndex;

        public AudioHandler PlaySequence(AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler handler =
                Bragi.Instance.Play(Audios[Mathf.Clamp(sequenceIndex, 0, Audios.Length - 1)].AudioConfig,
                    overrideSettings, allowFading);
            sequenceIndex = (sequenceIndex + 1) % Audios.Length;
            return handler;
        }

        public AudioHandler PlaySequence(Vector3 position, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler handler = Bragi.Instance.Play(position,
                Audios[Mathf.Clamp(sequenceIndex, 0, Audios.Length - 1)].AudioConfig, overrideSettings, allowFading);
            sequenceIndex = (sequenceIndex + 1) % Audios.Length;
            return handler;
        }

        public AudioHandler PlaySequence(Transform parent, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler handler = Bragi.Instance.Play(parent,
                Audios[Mathf.Clamp(sequenceIndex, 0, Audios.Length - 1)].AudioConfig, overrideSettings, allowFading);
            sequenceIndex = (sequenceIndex + 1) % Audios.Length;
            return handler;
        }
        
        public AudioHandler PlaySequenceChained(AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler handler = PlaySequence(overrideSettings, allowFading);
            return ProcessSequenceChained(ref handler);
        }
        
        public AudioHandler PlaySequenceChained(Vector3 position, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler handler = PlaySequence(position, overrideSettings, allowFading);
            return ProcessSequenceChained(ref handler);
        }
        
        public AudioHandler PlaySequenceChained(Transform parent, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler handler = PlaySequence(parent, overrideSettings, allowFading);
            return ProcessSequenceChained(ref handler);
        }

        public AudioHandler[] PlaySimultaneous(AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler[] handlers = new AudioHandler[Audios.Length];
            for(int i = 0; i < Audios.Length; i++)
            {
                handlers[i] = Bragi.Instance.Play(Audios[i].AudioConfig, overrideSettings, allowFading);
            }
            return handlers;
        }
        
        public AudioHandler[] PlaySimultaneous(Vector3 position, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler[] handlers = new AudioHandler[Audios.Length];
            for(int i = 0; i < Audios.Length; i++)
            {
                Bragi.Instance.Play(position, Audios[i].AudioConfig, overrideSettings, allowFading);
            }
            return handlers;
        }
        
        public AudioHandler[] PlaySimultaneous(Transform parent, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler[] handlers = new AudioHandler[Audios.Length];
            for(int i = 0; i < Audios.Length; i++)
            {
                Bragi.Instance.Play(parent, Audios[i].AudioConfig, overrideSettings, allowFading);
            }
            return handlers;
        }

        public AudioConfig GetRandom()
        {
            return Audios.GetRandomWeight().AudioConfig;
        }

        private AudioHandler ProcessSequenceChained(ref AudioHandler handler)
        {
            handler.OnFinished += PlayNext;
            return handler;

            void PlayNext(AudioHandler playedHandler)
            {
                playedHandler.OnFinished -= PlayNext;
                PlayNextSequenceChained(ref playedHandler);
            }
        }
        
        private void PlayNextSequenceChained(ref AudioHandler handler)
        {
            if (sequenceIndex != 0)
            {
                handler = PlaySequence(handler.Settings, handler.AllowFading);
            }
        }

        public override AudioHandler[] Play(AudioSettings overrideSettings = null, bool allowFading = true)
        {
            return Bragi.Instance.Play(this, PlayMode, allowFading: allowFading);
        }

        public override AudioHandler[] Play(Vector3 position, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            return Bragi.Instance.Play(this, PlayMode, allowFading: allowFading);
        }

        public override AudioHandler[] Play(Transform parent, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            return Bragi.Instance.Play(this, PlayMode, allowFading: allowFading);
        }
    }
}
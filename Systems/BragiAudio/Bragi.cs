using UnityEngine;

namespace LegendaryTools.Bragi
{
    public class Bragi : Singleton<Bragi>
    {
        private readonly AudioHandler audioHandlerPrefab;

        public Bragi()
        {
            audioHandlerPrefab = new GameObject("PoolableAudioSource").AddComponent<AudioHandler>();
            audioHandlerPrefab.gameObject.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(audioHandlerPrefab);
        }

        public AudioHandler Play(AudioConfig audioConfig, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler handler = Pool.Instantiate(audioHandlerPrefab);
            handler.Initialize(audioConfig, overrideSettings ?? audioConfig.AudioSettings, allowFading);
            return handler;
        }

        public AudioHandler Play(Vector3 position, AudioConfig audioConfig, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler handler = Pool.Instantiate(audioHandlerPrefab);
            handler.Initialize(audioConfig, overrideSettings ?? audioConfig.AudioSettings, allowFading);
            handler.Position = position;
            return handler;
        }

        public AudioHandler Play(Transform parent, AudioConfig audioConfig, AudioSettings overrideSettings = null, bool allowFading = true)
        {
            AudioHandler handler = Pool.Instantiate(audioHandlerPrefab);
            handler.Initialize(audioConfig, overrideSettings ?? audioConfig.AudioSettings, allowFading);
            handler.VirtualParent = parent;
            return handler;
        }

        public AudioHandler[] Play(AudioGroup audioGroup, AudioGroupPlayMode playMode,
            AudioSettings overrideSettings = null, bool allowFading = true)
        {
            switch (playMode)
            {
                case AudioGroupPlayMode.Sequential: return new[] {audioGroup.PlaySequence(overrideSettings, allowFading)};
                case AudioGroupPlayMode.SequentialChained:
                    return new[] {audioGroup.PlaySequenceChained(overrideSettings, allowFading)};
                case AudioGroupPlayMode.Random: return new[] {Play(audioGroup.GetRandom(), overrideSettings, allowFading)};
                case AudioGroupPlayMode.Simultaneous: return audioGroup.PlaySimultaneous(overrideSettings, allowFading);
            }

            return null;
        }

        public AudioHandler[] Play(Vector3 position, AudioGroup audioGroup, AudioGroupPlayMode playMode,
            AudioSettings overrideSettings = null, bool allowFading = true)
        {
            switch (playMode)
            {
                case AudioGroupPlayMode.Sequential: return new[] {audioGroup.PlaySequence(position, overrideSettings, allowFading)};
                case AudioGroupPlayMode.SequentialChained:
                    return new[] {audioGroup.PlaySequenceChained(position, overrideSettings, allowFading)};
                case AudioGroupPlayMode.Random: return new[] {Play(position, audioGroup.GetRandom(), overrideSettings, allowFading)};
                case AudioGroupPlayMode.Simultaneous: return audioGroup.PlaySimultaneous(position, overrideSettings, allowFading);
            }

            return null;
        }

        public AudioHandler[] Play(Transform parent, AudioGroup audioGroup, AudioGroupPlayMode playMode,
            AudioSettings overrideSettings = null, bool allowFading = true)
        {
            switch (playMode)
            {
                case AudioGroupPlayMode.Sequential: return new[] {audioGroup.PlaySequence(parent, overrideSettings, allowFading)};
                case AudioGroupPlayMode.SequentialChained:
                    return new[] {audioGroup.PlaySequenceChained(parent, overrideSettings, allowFading)};
                case AudioGroupPlayMode.Random: return new[] {Play(parent, audioGroup.GetRandom(), overrideSettings, allowFading)};
                case AudioGroupPlayMode.Simultaneous: return audioGroup.PlaySimultaneous(parent, overrideSettings, allowFading);
            }

            return null;
        }
    }
}
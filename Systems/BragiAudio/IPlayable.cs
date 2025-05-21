namespace LegendaryTools.Bragi
{
    public interface IPlayable
    {
        bool IsMuted { get; }
        bool IsPlaying { get; }
        bool IsPaused { get; }
        void Play();
        void Stop();
        void Pause();
        void UnPause();
        void Mute();
        void UnMute();
    }
}
using System;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Chronos
{
    public interface IChronos : IDisposable
    {
        bool Verbose { get; set; }
        bool IsInitialized { get; }
        bool WasFirstStart { get; }
        SerializedTimeSpan LastElapsedTimeWhileAppIsClosed { get; }
        SerializedDateTime LastRecordedDateTimeUtc { get; }
        SerializedDateTime NowUtc { get; }
        event Action<TimeSpan> ElapsedTimeWhileAppWasPause;
        event Action<TimeSpan> ElapsedTimeWhileAppLostFocus;
        Task Initialize();
        void Sync();
        Task<(bool, DateTime)> RequestDateTime();
        Task<(bool, DateTime)> RequestDateTimeUtc();
    }

    public class Chronos : IChronos
    {
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public bool Verbose { get; set; }
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public bool IsInitialized => isInitialized;
        
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public bool WasFirstStart { get; private set; }
        
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.DrawWithUnity]
#endif
        public SerializedTimeSpan LastElapsedTimeWhileAppIsClosed { get; private set; }
        
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.DrawWithUnity]
#endif
        public SerializedDateTime LastRecordedDateTimeUtc
        {
            get => DateTime.ParseExact(
                PlayerPrefs.GetString(LastRecordedDateTimeKey,
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)), "o", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            private set
            {
                lastUnscaledTimeAsDouble = Time.unscaledTimeAsDouble;
                PlayerPrefs.SetString(LastRecordedDateTimeKey, value.DateTime.ToString("o", CultureInfo.InvariantCulture));
            }
        }

#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.DrawWithUnity]
#endif
        public SerializedDateTime NowUtc => LastRecordedDateTimeUtc.DateTime.AddSeconds(Time.unscaledTimeAsDouble - lastUnscaledTimeAsDouble);

        private readonly ChronosConfig config;
        private readonly IMonoBehaviourFacade monoBehaviourFacade;
        private static readonly string LastRecordedDateTimeKey = "LastRecordedDateTimeKey";
        private static readonly string FirstStartKey = "FirstStart";
        
        private double lastUnscaledTimeAsDouble;
        private double lastUnscaledTimeAsDoubleSinceLoseFocus;
        private double lastUnscaledTimeAsDoubleSinceGamePaused;
        
        private bool isInitialized;

        public event Action<TimeSpan> ElapsedTimeWhileAppWasPause;
        public event Action<TimeSpan> ElapsedTimeWhileAppLostFocus;
        
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        private bool FirstStart
        {
            get => System.Convert.ToBoolean(PlayerPrefs.GetInt(FirstStartKey, 1));
            set => PlayerPrefs.SetInt(FirstStartKey, System.Convert.ToInt32(value));
        }

        public Chronos(ChronosConfig config, IMonoBehaviourFacade monoBehaviourFacade)
        {
            this.config = config;
            this.monoBehaviourFacade = monoBehaviourFacade;
            
            monoBehaviourFacade.OnApplicationFocused += OnApplicationFocus;
            monoBehaviourFacade.OnApplicationPaused += OnApplicationPause;
        }

        public async Task Initialize()
        {
            (bool, DateTime) currentTime = await RequestDateTimeUtc();

            if (FirstStart)
            {
                LastElapsedTimeWhileAppIsClosed = TimeSpan.Zero;
                LastRecordedDateTimeUtc = currentTime.Item2;
                FirstStart = false;
                WasFirstStart = true;
                isInitialized = true;
            }
            else
            {
                if (currentTime.Item2 > LastRecordedDateTimeUtc)
                {
                    LastElapsedTimeWhileAppIsClosed = currentTime.Item2 - LastRecordedDateTimeUtc;
                    LastRecordedDateTimeUtc = currentTime.Item2;
                    isInitialized = true;
                }
            }
        }
        
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.Button]
#endif
        public async void Sync()
        {
            (bool, DateTime) result = await RequestDateTimeUtc();
            if (result.Item2 > LastRecordedDateTimeUtc)
            {
                LastRecordedDateTimeUtc = result.Item2;
            }
        }

#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.Button]
#endif
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                if(Verbose)
                    Debug.Log($"OnApplicationFocus({hasFocus}) Time between lose focus {Time.unscaledTimeAsDouble - lastUnscaledTimeAsDoubleSinceLoseFocus} seconds");
                
                if(IsInitialized)
                    ElapsedTimeWhileAppLostFocus?.Invoke(TimeSpan.FromSeconds(Time.unscaledTimeAsDouble - lastUnscaledTimeAsDoubleSinceLoseFocus));
            }
            else
            {
                lastUnscaledTimeAsDoubleSinceLoseFocus = Time.unscaledTimeAsDouble;
            }
            
            if(Verbose)
                Debug.Log($"OnApplicationFocus({hasFocus}) -> {NowUtc}");
        }

#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.Button]
#endif
        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                lastUnscaledTimeAsDoubleSinceGamePaused = Time.unscaledTimeAsDouble;
                
            }
            else
            {
                if(Verbose)
                    Debug.Log($"OnApplicationPause({isPaused}) Time between pause {Time.unscaledTimeAsDouble - lastUnscaledTimeAsDoubleSinceGamePaused} seconds");
                if(IsInitialized)
                    ElapsedTimeWhileAppWasPause?.Invoke(TimeSpan.FromSeconds(Time.unscaledTimeAsDouble - lastUnscaledTimeAsDoubleSinceGamePaused));
            }
            
            if(Verbose)
                Debug.Log($"OnApplicationPause({isPaused}) -> {NowUtc}");
        }

        public async Task<(bool, DateTime)> RequestDateTime()
        {
            foreach (DateTimeProvider provider in config.WaterfallProviders)
            {
                (bool, DateTime) result = await provider.GetDateTime();
                if (result.Item1) return result;
            }

            return (false, default);
        }

        public async Task<(bool, DateTime)> RequestDateTimeUtc()
        {
            foreach (DateTimeProvider provider in config.WaterfallProviders)
            {
                (bool, DateTime) result = await provider.GetDateTimeUtc();
                if (result.Item1) return result;
            }

            return (false, default);
        }

        public static void ClearPersistentData()
        {
            PlayerPrefs.DeleteKey(LastRecordedDateTimeKey);
            PlayerPrefs.DeleteKey(FirstStartKey);
        }
        
        public void Dispose()
        {
            monoBehaviourFacade.OnApplicationFocused -= OnApplicationFocus;
            monoBehaviourFacade.OnApplicationPaused -= OnApplicationPause;
        }
    }
}
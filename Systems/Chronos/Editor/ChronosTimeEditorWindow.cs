#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Chronos.Editor
{
    public sealed class ChronosTimeEditorWindow : EditorWindow
    {
        private ChronosConfig config;
        private TimeMachineProvider timeMachineProvider;

        private bool autoRun;
        private double lastEditorTime;

        private int stepAmount = 10;
        private TimeUnit stepUnit = TimeUnit.Minutes;

        private string setUtcIso = "";
        private int offlineAmount = 2;
        private TimeUnit offlineUnit = TimeUnit.Hours;

        private Vector2 scroll;

        [MenuItem("Tools/Chronos/Time Control")]
        public static void Open()
        {
            GetWindow<ChronosTimeEditorWindow>("Chronos Time Control");
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            lastEditorTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!autoRun || timeMachineProvider == null)
            {
                lastEditorTime = EditorApplication.timeSinceStartup;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double delta = now - lastEditorTime;
            lastEditorTime = now;

            if (delta <= 0)
                return;

            // Advance the TimeMachine clock by editor delta.
            DateTime current = timeMachineProvider.TimeMachineTime.DateTime;
            DateTime next = current.AddSeconds(delta);
            SetProviderUtc(next);

            Repaint();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawHeader();
            EditorGUILayout.Space(8);

            DrawAssetSelection();
            EditorGUILayout.Space(8);

            DrawProviderControls();
            EditorGUILayout.Space(8);

            DrawLiveChronosStatus();
            EditorGUILayout.Space(8);

            DrawOfflineSimulation();
            EditorGUILayout.Space(8);

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Chronos Time Control", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This editor tool manipulates time through a TimeMachineProvider.\n" +
                "For live Play Mode control, register your IChronos instance using ChronosEditorBridge.Register(chronos).",
                MessageType.Info);
        }

        private void DrawAssetSelection()
        {
            EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);

            config = (ChronosConfig)EditorGUILayout.ObjectField("ChronosConfig", config, typeof(ChronosConfig), false);
            timeMachineProvider = (TimeMachineProvider)EditorGUILayout.ObjectField("TimeMachineProvider",
                timeMachineProvider, typeof(TimeMachineProvider), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Find Config in Project"))
                    config = FindFirstAsset<ChronosConfig>();

                if (GUILayout.Button("Find TimeMachineProvider in Project"))
                    timeMachineProvider = FindFirstAsset<TimeMachineProvider>();
            }

            if (config == null || timeMachineProvider == null)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Enable Time Machine (Insert Provider First)"))
                    EnableTimeMachine();

                if (GUILayout.Button("Disable Time Machine (Remove Provider)"))
                    DisableTimeMachine();
            }
        }

        private void DrawProviderControls()
        {
            EditorGUILayout.LabelField("Time Machine Controls", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(timeMachineProvider == null))
            {
                autoRun = EditorGUILayout.ToggleLeft("Auto Run (Provider ticks in Editor)", autoRun);

                DateTime providerUtc =
                    timeMachineProvider != null ? timeMachineProvider.TimeMachineTime.DateTime : default;
                EditorGUILayout.LabelField("Provider UTC",
                    providerUtc == default ? "-" : providerUtc.ToString("o", CultureInfo.InvariantCulture));

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Pause"))
                    {
                        // Pause here means: stop advancing the TimeMachineProvider automatically.
                        autoRun = false;
                        TrySnapProviderToChronosUtc();
                    }

                    if (GUILayout.Button("Resume"))
                    {
                        autoRun = true;
                        lastEditorTime = EditorApplication.timeSinceStartup;
                    }
                }

                EditorGUILayout.Space(6);

                using (new EditorGUILayout.HorizontalScope())
                {
                    stepAmount = EditorGUILayout.IntField("Step", stepAmount);
                    stepUnit = (TimeUnit)EditorGUILayout.EnumPopup(stepUnit);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"Back {stepAmount} {stepUnit}"))
                        AddDeltaToProvider(-ToTimeSpan(stepAmount, stepUnit));

                    if (GUILayout.Button($"Forward {stepAmount} {stepUnit}"))
                        AddDeltaToProvider(ToTimeSpan(stepAmount, stepUnit));
                }

                EditorGUILayout.Space(6);

                EditorGUILayout.LabelField("Set Absolute UTC (ISO 8601 Roundtrip)", EditorStyles.boldLabel);
                setUtcIso = EditorGUILayout.TextField("UTC (o)", setUtcIso);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Set Provider UTC"))
                    {
                        if (TryParseUtcIso(setUtcIso, out DateTime utc))
                            SetProviderUtc(utc);
                        else
                            Debug.LogWarning("Invalid UTC ISO string. Use DateTime.ToString(\"o\").");
                    }

                    if (GUILayout.Button("Copy Provider UTC"))
                        if (timeMachineProvider != null)
                            EditorGUIUtility.systemCopyBuffer =
                                timeMachineProvider.TimeMachineTime.DateTime.ToString("o",
                                    CultureInfo.InvariantCulture);

                    if (GUILayout.Button("Set Now (System UTC)"))
                        SetProviderUtc(DateTime.UtcNow);

                    if (GUILayout.Button("Set Now (Chronos UtcNow)"))
                        TrySnapProviderToChronosUtc(true);
                }
            }
        }

        private void DrawLiveChronosStatus()
        {
            EditorGUILayout.LabelField("Live Chronos (Play Mode)", EditorStyles.boldLabel);

            IChronos chronos = ChronosEditorBridge.ChronosInstance;
            if (!EditorApplication.isPlaying || chronos == null)
            {
                EditorGUILayout.HelpBox(
                    "No live Chronos instance registered.\n" +
                    "To enable live control, call ChronosEditorBridge.Register(chronos) in your composition root while in Play Mode.",
                    MessageType.Warning);
                return;
            }

            ChronosStatus status = chronos.Status;

            EditorGUILayout.LabelField("IsInitialized", status.IsInitialized.ToString());
            EditorGUILayout.LabelField("IsTimeTrusted", status.IsTimeTrusted.ToString());
            EditorGUILayout.LabelField("TamperDetected", status.TamperDetected.ToString());
            EditorGUILayout.LabelField("FailureCount", status.FailureCount.ToString());
            EditorGUILayout.LabelField("LastProviderName", status.LastProviderName ?? string.Empty);

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("UtcNow", chronos.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("LocalNow", chronos.LocalNow.ToString("o", CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("Unix Seconds", chronos.UtcNowUnixSeconds.ToString());
            EditorGUILayout.LabelField("Unix Milliseconds", chronos.UtcNowUnixMilliseconds.ToString());

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Sync (Manual)"))
                    _ = chronos.SyncAsync();

                if (GUILayout.Button("Clear Persistent Data"))
                    chronos.ClearPersistentData();
            }
        }

        private void DrawOfflineSimulation()
        {
            EditorGUILayout.LabelField("Offline Simulation", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying ||
                                               ChronosEditorBridge.ChronosInstance == null))
            {
                EditorGUILayout.HelpBox(
                    "Simulate the app being minimized/closed by:\n" +
                    "1) Persist anchor (as if app paused/lost focus)\n" +
                    "2) Advance TimeMachineProvider time\n" +
                    "3) Trigger resume/focus gained sync (bypasses cooldown)\n\n" +
                    "Requires: Play Mode + TimeMachineProvider enabled as first provider + Chronos registered.",
                    MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    offlineAmount = EditorGUILayout.IntField("Offline", offlineAmount);
                    offlineUnit = (TimeUnit)EditorGUILayout.EnumPopup(offlineUnit);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Simulate Pause -> Resume"))
                        SimulatePauseResume(ToTimeSpan(offlineAmount, offlineUnit));

                    if (GUILayout.Button("Simulate Focus Lost -> Focus Gained"))
                        SimulateFocusLostGained(ToTimeSpan(offlineAmount, offlineUnit));
                }
            }
        }

        private void EnableTimeMachine()
        {
            if (config == null || timeMachineProvider == null)
                return;

            Undo.RecordObject(config, "Enable Time Machine");

            if (config.WaterfallProviders == null)
                config.WaterfallProviders = new System.Collections.Generic.List<DateTimeProvider>();

            config.WaterfallProviders.Remove(timeMachineProvider);
            config.WaterfallProviders.Insert(0, timeMachineProvider);

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private void DisableTimeMachine()
        {
            if (config == null || timeMachineProvider == null)
                return;

            Undo.RecordObject(config, "Disable Time Machine");

            if (config.WaterfallProviders != null)
                config.WaterfallProviders.Remove(timeMachineProvider);

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private void AddDeltaToProvider(TimeSpan delta)
        {
            if (timeMachineProvider == null)
                return;

            DateTime current = timeMachineProvider.TimeMachineTime.DateTime;
            SetProviderUtc(current.Add(delta));
        }

        private void SetProviderUtc(DateTime utc)
        {
            if (timeMachineProvider == null)
                return;

            if (utc.Kind != DateTimeKind.Utc)
                utc = DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc);

            Undo.RecordObject(timeMachineProvider, "Set TimeMachineProvider Time");

            // Avoid relying on implicit conversions. Assign fields used by TimeMachineProvider.
            SerializedDateTime sdt = timeMachineProvider.TimeMachineTime;
            sdt.Year = utc.Year;
            sdt.Month = utc.Month;
            sdt.Day = utc.Day;
            sdt.Hour = utc.Hour;
            sdt.Minute = utc.Minute;
            sdt.Second = utc.Second;

            timeMachineProvider.TimeMachineTime = sdt;

            EditorUtility.SetDirty(timeMachineProvider);
        }

        private void TrySnapProviderToChronosUtc(bool force = false)
        {
            IChronos chronos = ChronosEditorBridge.ChronosInstance;
            if (EditorApplication.isPlaying && chronos != null)
            {
                SetProviderUtc(chronos.UtcNow);
                return;
            }

            if (force)
                SetProviderUtc(DateTime.UtcNow);
        }

        private void SimulatePauseResume(TimeSpan offline)
        {
            if (timeMachineProvider == null)
            {
                Debug.LogWarning("TimeMachineProvider is required for offline simulation.");
                return;
            }

            if (!IsTimeMachineEnabled())
            {
                Debug.LogWarning(
                    "TimeMachineProvider must be enabled (inserted as first provider) for offline simulation.");
                return;
            }

            IChronos chronos = ChronosEditorBridge.ChronosInstance;
            if (chronos == null)
                return;

            autoRun = false;

            // 1) Snap provider to current Chronos time (baseline).
            TrySnapProviderToChronosUtc();

            // 2) Persist anchor BEFORE advancing time (as if app paused now).
            if (chronos is Chronos concrete)
                concrete.EditorPersistRuntimeAnchor();

            // 3) Advance provider time while "offline".
            AddDeltaToProvider(offline);

            // 4) Resume path (bypass cooldown).
            if (chronos is Chronos concrete2)
                _ = concrete2.EditorSimulateResumeFromPauseAsync();
            else
                _ = chronos.SyncAsync();
        }

        private void SimulateFocusLostGained(TimeSpan offline)
        {
            if (timeMachineProvider == null)
            {
                Debug.LogWarning("TimeMachineProvider is required for offline simulation.");
                return;
            }

            if (!IsTimeMachineEnabled())
            {
                Debug.LogWarning(
                    "TimeMachineProvider must be enabled (inserted as first provider) for offline simulation.");
                return;
            }

            IChronos chronos = ChronosEditorBridge.ChronosInstance;
            if (chronos == null)
                return;

            autoRun = false;

            // 1) Snap provider to current Chronos time (baseline).
            TrySnapProviderToChronosUtc();

            // 2) Persist anchor BEFORE advancing time (as if focus lost now).
            if (chronos is Chronos concrete)
                concrete.EditorPersistRuntimeAnchor();

            // 3) Advance provider time while "offline".
            AddDeltaToProvider(offline);

            // 4) Focus gained path (bypass cooldown).
            if (chronos is Chronos concrete2)
                _ = concrete2.EditorSimulateFocusGainedAsync();
            else
                _ = chronos.SyncAsync();
        }

        private bool IsTimeMachineEnabled()
        {
            if (config == null || timeMachineProvider == null || config.WaterfallProviders == null)
                return false;

            return config.WaterfallProviders.Count > 0 &&
                   ReferenceEquals(config.WaterfallProviders[0], timeMachineProvider);
        }

        private static bool TryParseUtcIso(string iso, out DateTime utc)
        {
            utc = default;

            if (string.IsNullOrEmpty(iso))
                return false;

            if (!DateTime.TryParseExact(iso, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                    out DateTime parsed))
                return false;

            if (parsed.Kind != DateTimeKind.Utc)
                parsed = DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc);

            utc = parsed;
            return true;
        }

        private static TimeSpan ToTimeSpan(int amount, TimeUnit unit)
        {
            if (amount < 0)
                amount = 0;

            switch (unit)
            {
                case TimeUnit.Seconds:
                    return TimeSpan.FromSeconds(amount);
                case TimeUnit.Minutes:
                    return TimeSpan.FromMinutes(amount);
                case TimeUnit.Hours:
                    return TimeSpan.FromHours(amount);
                case TimeUnit.Days:
                    return TimeSpan.FromDays(amount);
                default:
                    return TimeSpan.FromSeconds(amount);
            }
        }

        private static T FindFirstAsset<T>() where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids == null || guids.Length == 0)
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private enum TimeUnit
        {
            Seconds = 0,
            Minutes = 1,
            Hours = 2,
            Days = 3
        }
    }
}
#endif
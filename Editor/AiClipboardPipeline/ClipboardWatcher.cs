#if UNITY_EDITOR_WIN
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Watches Windows clipboard updates using a message-only Win32 window and WM_CLIPBOARDUPDATE.
    /// This version reads clipboard text with retry to avoid false negatives when clipboard is not ready.
    /// </summary>
    [InitializeOnLoad]
    public static class ClipboardWatcher
    {
        public static event Action<string> ClipboardChanged;

        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int PM_REMOVE = 0x0001;

        private static IntPtr _hwnd = IntPtr.Zero;
        private static IntPtr _hInstance = IntPtr.Zero;
        private static bool _running;
        private static string _className;

        private static WndProc _wndProcDelegate;

        // Retry state (clipboard can be temporarily locked or not populated yet).
        private static bool _pendingRead;
        private static int _readAttempts;
        private static double _nextReadAt;
        private static string _lastRead;
        private static int _stableReads;

        private const int MaxReadAttempts = 8;
        private const double ReadRetryIntervalSeconds = 0.04;
        private const int RequiredStableReads = 2;

        static ClipboardWatcher()
        {
            EditorApplication.delayCall += Start;
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }

        public static bool IsRunning => _running;

        public static void Start()
        {
            if (_running)
                return;

            try
            {
                _wndProcDelegate = WindowProc;

                CreateMessageOnlyWindowOrThrow();

                if (!AddClipboardFormatListener(_hwnd))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "AddClipboardFormatListener failed.");

                _running = true;
                EditorApplication.update += PumpMessages;
                EditorApplication.update += RetryClipboardReadUpdate;

                Debug.Log("[ClipboardWatcher] Started successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClipboardWatcher] Failed to start: {ex}");
                Stop();
            }
        }

        public static void Stop()
        {
            if (!_running && _hwnd == IntPtr.Zero)
                return;

            try
            {
                EditorApplication.update -= PumpMessages;
                EditorApplication.update -= RetryClipboardReadUpdate;

                if (_hwnd != IntPtr.Zero)
                {
                    RemoveClipboardFormatListener(_hwnd);
                    DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }

                if (!string.IsNullOrEmpty(_className) && _hInstance != IntPtr.Zero)
                    UnregisterClass(_className, _hInstance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClipboardWatcher] Stop encountered an error: {ex}");
            }
            finally
            {
                _running = false;
                _wndProcDelegate = null;
                _className = null;
                _hInstance = IntPtr.Zero;

                _pendingRead = false;
                _readAttempts = 0;
                _nextReadAt = 0;
                _lastRead = null;
                _stableReads = 0;
            }
        }

        private static void PumpMessages()
        {
            while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CLIPBOARDUPDATE)
                // Schedule the read instead of reading immediately.
                // Clipboard content may not be ready in the same tick.
                ScheduleClipboardRead();

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private static void ScheduleClipboardRead()
        {
            _pendingRead = true;
            _readAttempts = 0;
            _stableReads = 0;
            _lastRead = null;
            _nextReadAt = EditorApplication.timeSinceStartup;

            // Also try one delayed call quickly.
            EditorApplication.delayCall += () =>
            {
                if (_pendingRead)
                    _nextReadAt = Math.Min(_nextReadAt, EditorApplication.timeSinceStartup);
            };
        }

        private static void RetryClipboardReadUpdate()
        {
            if (!_pendingRead)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextReadAt)
                return;

            _readAttempts++;
            _nextReadAt = now + ReadRetryIntervalSeconds;

            string text = string.Empty;
            try
            {
                text = EditorGUIUtility.systemCopyBuffer ?? string.Empty;
            }
            catch
            {
                // Clipboard might be locked by another process.
                text = string.Empty;
            }

            // Stabilization: require the same read twice to avoid partial reads.
            if (_lastRead != null && string.Equals(text, _lastRead, StringComparison.Ordinal))
                _stableReads++;
            else
                _stableReads = 0;

            _lastRead = text;

            bool isStableEnough = _stableReads >= RequiredStableReads;
            bool attemptsExceeded = _readAttempts >= MaxReadAttempts;

            if (!isStableEnough && !attemptsExceeded)
                return;

            _pendingRead = false;

            try
            {
                ClipboardChanged?.Invoke(text ?? string.Empty);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClipboardWatcher] ClipboardChanged handler threw: {ex}");
            }
        }

        private static void CreateMessageOnlyWindowOrThrow()
        {
            _hInstance = GetModuleHandle(null);
            if (_hInstance == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetModuleHandle failed.");

            _className = "UnityClipboardWatcherWindow_" + Guid.NewGuid().ToString("N");

            WNDCLASSEX wc = new()
            {
                cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
                lpfnWndProc = _wndProcDelegate,
                hInstance = _hInstance,
                lpszClassName = _className
            };

            ushort atom = RegisterClassEx(ref wc);
            if (atom == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed.");

            IntPtr HWND_MESSAGE = new(-3);

            _hwnd = CreateWindowEx(
                0,
                _className,
                "UnityClipboardWatcher",
                0,
                0, 0, 0, 0,
                HWND_MESSAGE,
                IntPtr.Zero,
                _hInstance,
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed.");
        }

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax,
            uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);
    }
}
#endif
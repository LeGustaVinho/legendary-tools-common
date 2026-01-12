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
    /// Important: WM_CLIPBOARDUPDATE can arrive before EditorGUIUtility.systemCopyBuffer is populated.
    /// This watcher uses a short retry loop to read clipboard text reliably.
    /// </summary>
    [InitializeOnLoad]
    public static class ClipboardWatcher
    {
        public static event Action<string> ClipboardChanged;

        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int PM_REMOVE = 0x0001;

        private const int MaxClipboardReadAttempts = 8;
        private const double ClipboardReadRetryDelaySeconds = 0.05;

        private static IntPtr _hwnd = IntPtr.Zero;
        private static IntPtr _hInstance = IntPtr.Zero;
        private static bool _running;
        private static string _className;

        private static WndProc _wndProcDelegate;

        // Pending clipboard read (retry loop)
        private static bool _pendingRead;
        private static int _readAttempts;
        private static double _nextReadTime;
        private static string _lastDeliveredText;

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
                _nextReadTime = 0;
            }
        }

        private static void PumpMessages()
        {
            // Unity Editor does not automatically pump messages for our hidden Win32 window.
            while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            PumpPendingClipboardRead();
        }

        private static void PumpPendingClipboardRead()
        {
            if (!_pendingRead)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextReadTime)
                return;

            _readAttempts++;

            string text = string.Empty;

            try
            {
                // Unity-managed way to read text clipboard in Editor.
                text = EditorGUIUtility.systemCopyBuffer ?? string.Empty;
            }
            catch
            {
                // Clipboard can be temporarily locked by another process.
                text = string.Empty;
            }

            // If clipboard is empty, it might still be updating; retry shortly.
            if (string.IsNullOrEmpty(text))
            {
                if (_readAttempts >= MaxClipboardReadAttempts)
                {
                    _pendingRead = false;
                    return;
                }

                _nextReadTime = now + ClipboardReadRetryDelaySeconds;
                return;
            }

            // Avoid re-delivering identical text spam.
            if (string.Equals(text, _lastDeliveredText, StringComparison.Ordinal))
            {
                _pendingRead = false;
                return;
            }

            _lastDeliveredText = text;

            _pendingRead = false;

            try
            {
                ClipboardChanged?.Invoke(text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClipboardWatcher] ClipboardChanged handler threw: {ex}");
            }
        }

        private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                // Schedule a delayed read + retry loop.
                _pendingRead = true;
                _readAttempts = 0;
                _nextReadTime = EditorApplication.timeSinceStartup + ClipboardReadRetryDelaySeconds;
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
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

            // HWND_MESSAGE creates a message-only window.
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

        // -------------------------
        // Self-test helpers
        // -------------------------

        [MenuItem("Tools/AI Clipboard Pipeline/Clipboard Watcher/Start")]
        private static void MenuStart()
        {
            Start();
        }

        [MenuItem("Tools/AI Clipboard Pipeline/Clipboard Watcher/Stop")]
        private static void MenuStop()
        {
            Stop();
        }

        [MenuItem("Tools/AI Clipboard Pipeline/Clipboard Watcher/Print Status")]
        private static void MenuStatus()
        {
            Debug.Log(
                $"[ClipboardWatcher] Running={_running}, HWND={_hwnd}, hInstance={_hInstance}, class={_className}");
        }

        // -------------------------
        // Win32 Interop
        // -------------------------

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

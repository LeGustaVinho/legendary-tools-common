#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Reactive.Editor
{
    /// <summary>
    /// Editor Window that lists all live bindings and provides inspection & control actions.
    /// </summary>
    public sealed class BindingsDebuggerWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _search = string.Empty;
        private bool _autoRefresh = true;
        private double _nextRefreshTime;
        private const double AutoRefreshInterval = 0.5;

        [MenuItem("Tools/LegendaryTools/UI/Reactive Bindings Debugger")]
        public static void ShowWindow()
        {
            BindingsDebuggerWindow wnd = GetWindow<BindingsDebuggerWindow>("Bindings Debugger");
            wnd.minSize = new Vector2(720, 340);
            wnd.Focus();
            wnd.Repaint();
        }

        private void OnEnable()
        {
            _nextRefreshTime = EditorApplication.timeSinceStartup + AutoRefreshInterval;
            EditorApplication.update += EditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
        }

        private void EditorUpdate()
        {
            if (!_autoRefresh) return;

            if (EditorApplication.timeSinceStartup >= _nextRefreshTime)
            {
                _nextRefreshTime = EditorApplication.timeSinceStartup + AutoRefreshInterval;
                Repaint();
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Search", GUILayout.Width(48));
                _search = GUILayout.TextField(_search ?? string.Empty, EditorStyles.toolbarTextField);

                GUILayout.FlexibleSpace();

                _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto Refresh", EditorStyles.toolbarButton);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    Repaint();
            }

            BindingHandle[] items = BindingRegistry.Snapshot();

            if (!string.IsNullOrEmpty(_search))
            {
                string s = _search.ToLowerInvariant();

                // Local helper to safely normalize strings for search.
                string Norm(string x)
                {
                    return (x ?? string.Empty).ToLowerInvariant();
                }

                items = items.Where(h =>
                {
                    BindingInfo i = h.Info;

                    bool match =
                        Norm(i?.Kind).Contains(s) ||
                        Norm(i?.Direction).Contains(s) ||
                        Norm(i?.Description).Contains(s) ||
                        Norm(i?.Target != null ? i.Target.name : null).Contains(s) ||
                        Norm(i?.Owner != null ? i.Owner.name : null).Contains(s) ||
                        Norm(i?.Anchor != null ? i.Anchor.name : null).Contains(s) ||
                        (i?.Tags != null && i.Tags.Any(t => Norm(t).Contains(s)));

                    return match;
                }).ToArray();
            }

            DrawSummary(items);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (BindingHandle handle in items.OrderByDescending(h => h.Info.CreatedUtc))
            {
                DrawBindingCard(handle);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSummary(BindingHandle[] items)
        {
            int total = items.Length;
            int subscribed = items.Count(h => h.IsSubscribed);
            int suspended = items.Count(h => h.IsSuspended);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Total: {total}    Subscribed: {subscribed}    Suspended: {suspended}",
                EditorStyles.boldLabel);
            EditorGUILayout.Space(2);
        }

        private static GUIStyle _card;
        private static GUIStyle CardStyle => _card ??= new GUIStyle("box") { padding = new RectOffset(8, 8, 6, 6) };

        private void DrawBindingCard(BindingHandle handle)
        {
            BindingInfo info = handle.Info;

            EditorGUILayout.BeginVertical(CardStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(info.Kind ?? "Binding", EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                string state = handle.IsSubscribed ? handle.IsSuspended ? "Suspended" : "Active" : "Unsubscribed";
                Color stateColor = handle.IsSubscribed ? handle.IsSuspended ? Color.yellow : Color.green : Color.gray;

                Color prevColor = GUI.color;
                GUI.color = stateColor;
                GUILayout.Label(state, EditorStyles.miniBoldLabel, GUILayout.Width(100));
                GUI.color = prevColor;
            }

            EditorGUILayout.LabelField("Description", info.Description ?? "-");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField("Target", info.Target, typeof(UnityEngine.Object), true);
                if (GUILayout.Button("Ping", GUILayout.Width(60)) && info.Target)
                    EditorGUIUtility.PingObject(info.Target);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField("Owner", info.Owner, typeof(UnityEngine.Object), true);
                if (GUILayout.Button("Ping", GUILayout.Width(60)) && info.Owner)
                    EditorGUIUtility.PingObject(info.Owner);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField("Anchor", info.Anchor, typeof(UnityEngine.Object), true);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Created (UTC): {info.CreatedUtc:HH:mm:ss}", EditorStyles.miniLabel,
                    GUILayout.Width(160));
            }

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
                BindingOptions opts = info.Options;
                if (opts != null)
                {
                    EditorGUILayout.LabelField("Unbind On Disable", opts.UnbindOnDisable.ToString());
                    EditorGUILayout.LabelField("Resync On Enable", opts.ResyncOnEnable.ToString());
                    EditorGUILayout.LabelField("Update In Edit Mode", opts.UpdateInEditMode.ToString());
                    EditorGUILayout.LabelField("Placeholder", opts.NullOrInvalidPlaceholder ?? "<null>");
                    EditorGUILayout.LabelField("Format Provider", opts.FormatProvider?.GetType().Name ?? "<null>");
                }
                else
                {
                    EditorGUILayout.LabelField("<no options>");
                }
            }

            if (!string.IsNullOrEmpty(info.Direction) || (info.Tags != null && info.Tags.Length > 0))
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Meta", EditorStyles.boldLabel);
                    if (!string.IsNullOrEmpty(info.Direction))
                        EditorGUILayout.LabelField("Direction", info.Direction);
                    if (info.Tags != null && info.Tags.Length > 0)
                        EditorGUILayout.LabelField("Tags", string.Join(", ", info.Tags));
                }

            if (info.GetState != null)
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Live State", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(info.GetState() ?? "-");
                }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = handle.IsSubscribed && !handle.IsSuspended;
                if (GUILayout.Button("Resync", GUILayout.Width(80))) handle.Resync();
                GUI.enabled = true;

                if (!handle.IsSuspended)
                {
                    if (GUILayout.Button("Suspend", GUILayout.Width(80))) handle.Suspend();
                }
                else
                {
                    if (GUILayout.Button("Resume", GUILayout.Width(80))) handle.Resume(true);
                }

                GUILayout.FlexibleSpace();

                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("Dispose", GUILayout.Width(90))) handle.Dispose();
                GUI.backgroundColor = prev;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }
    }
}
#endif
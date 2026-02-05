#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

#if UNITY_EDITOR_WIN
using Microsoft.Win32;
#endif

#if UNITY_EDITOR_OSX
using System.Diagnostics;
using System.Text.RegularExpressions;
#endif

namespace LegendaryTools.Editor
{
    public class PlayerPrefsEditor : EditorWindow
    {
        private Vector2 scrollPos;

        private readonly Dictionary<string, string> prefs = new();
        private readonly Dictionary<string, string> editedValues = new();

        private string newKey = string.Empty;
        private string newValue = string.Empty;
        private PrefsType newType = PrefsType.String;

        // UI state.
        private string search = string.Empty;
        private bool sortAscending = true;
        private bool showOnlyModified = false;

        // Deferred actions to avoid modifying collections while iterating in IMGUI.
        private string pendingSaveKey;
        private string pendingDeleteKey;
        private string pendingCopyKey;
        private string pendingCopyValueKey;

        private string statusText = string.Empty;
        private double statusUntilTime = 0;

        // Cached styles/icons (initialized lazily inside OnGUI).
        private bool uiInitialized;
        private GUIStyle headerStyle;
        private GUIStyle toolbarSearchField;
        private GUIStyle toolbarSearchCancel;
        private GUIStyle rowStyle;
        private GUIStyle rowAltStyle;
        private GUIStyle keyStyle;
        private GUIStyle valueStyle;
        private GUIStyle badgeStyle;
        private GUIStyle footerStyle;

        private Texture2D iconSave;
        private Texture2D iconDelete;
        private Texture2D iconCopy;
        private Texture2D iconRefresh;
        private Texture2D iconPlus;

        private enum PrefsType
        {
            Int,
            Float,
            String
        }

        private enum ValueKind
        {
            Int,
            Float,
            String,
            Unknown
        }

        [MenuItem("Tools/LegendaryTools/PlayerPrefs/Viewer")]
        public static void ShowWindow()
        {
            PlayerPrefsEditor window = GetWindow<PlayerPrefsEditor>("PlayerPrefs Viewer");
            window.minSize = new Vector2(760, 420);
        }

        private void OnEnable()
        {
            LoadPlayerPrefs();
            CacheIconsOnly();
            uiInitialized = false;
        }

        private void CacheIconsOnly()
        {
            // EditorGUIUtility.IconContent is safe outside OnGUI.
            iconSave = EditorGUIUtility.IconContent("SaveActive").image as Texture2D;
            iconDelete = EditorGUIUtility.IconContent("TreeEditor.Trash").image as Texture2D;
            iconCopy = EditorGUIUtility.IconContent("Clipboard").image as Texture2D;
            iconRefresh = EditorGUIUtility.IconContent("Refresh").image as Texture2D;
            iconPlus = EditorGUIUtility.IconContent("Toolbar Plus").image as Texture2D;

            iconSave ??= EditorGUIUtility.IconContent("d_SaveAs").image as Texture2D;
            iconDelete ??= EditorGUIUtility.IconContent("d_TreeEditor.Trash").image as Texture2D;
            iconCopy ??= EditorGUIUtility.IconContent("d_TreeEditor.Duplicate").image as Texture2D;
            iconRefresh ??= EditorGUIUtility.IconContent("d_Refresh").image as Texture2D;
            iconPlus ??= EditorGUIUtility.IconContent("d_Toolbar Plus").image as Texture2D;
        }

        private void EnsureUiInitialized()
        {
            if (uiInitialized)
                return;

            // Safe: we are inside OnGUI when this gets called.
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13
            };

            toolbarSearchField = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField;
            toolbarSearchCancel = GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? GUIStyle.none;

            rowStyle = new GUIStyle("CN EntryBackEven");
            rowAltStyle = new GUIStyle("CN EntryBackOdd");

            keyStyle = new GUIStyle(EditorStyles.label)
            {
                richText = false,
                clipping = TextClipping.Clip
            };

            valueStyle = new GUIStyle(EditorStyles.textField)
            {
                clipping = TextClipping.Clip
            };

            badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 2, 2)
            };

            footerStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 6, 6)
            };

            uiInitialized = true;
        }

        private void LoadPlayerPrefs()
        {
            prefs.Clear();
            editedValues.Clear();

#if UNITY_EDITOR_WIN
            LoadWindowsPrefs();
#elif UNITY_EDITOR_OSX
            LoadMacPrefs();
#else
            Debug.LogWarning("PlayerPrefsEditor: Unsupported platform.");
#endif
        }

#if UNITY_EDITOR_WIN
        private void LoadWindowsPrefs()
        {
            string companyName = Application.companyName;
            string productName = Application.productName;

            string path = $"Software\\Unity\\UnityEditor\\{companyName}\\{productName}";
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(path);

            if (key == null)
                return;

            foreach (string valueName in key.GetValueNames())
            {
                string cleanedKey = RemoveHashedSuffix(valueName);

                if (prefs.ContainsKey(cleanedKey))
                    continue;

                object rawObj = key.GetValue(valueName);

                RegistryValueKind kind;
                try
                {
                    kind = key.GetValueKind(valueName);
                }
                catch
                {
                    kind = RegistryValueKind.Unknown;
                }

                string displayValue = ConvertRegistryValueToString(rawObj, kind);

                prefs[cleanedKey] = displayValue;
                editedValues[cleanedKey] = displayValue;
            }
        }

        private static string RemoveHashedSuffix(string key)
        {
            // Unity commonly appends "_h" for hashed keys in Windows registry.
            if (!string.IsNullOrEmpty(key) && key.EndsWith("_h", StringComparison.Ordinal))
                return key.Substring(0, key.Length - 2);

            return key;
        }

        private static string ConvertRegistryValueToString(object value, RegistryValueKind kind)
        {
            if (value == null)
                return string.Empty;

            // Prefer direct runtime type checks first.
            if (value is string strValue)
                return strValue;

            if (value is int intValue)
                return intValue.ToString(CultureInfo.InvariantCulture);

            if (value is byte[] bytesValue)
                return ConvertBinaryToBestString(bytesValue);

            // Fallback based on kind when runtime type isn't what we expect.
            if (kind == RegistryValueKind.String)
                return value.ToString() ?? string.Empty;

            if (kind == RegistryValueKind.DWord)
            {
                if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int parsedInt))
                    return parsedInt.ToString(CultureInfo.InvariantCulture);

                return value.ToString() ?? string.Empty;
            }

            return value.ToString() ?? string.Empty;
        }

        private static string ConvertBinaryToBestString(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            // 1) Prefer "real" UTF-8 strings when the payload looks like text.
            if (TryDecodeLikelyUtf8String(bytes, out string text))
                return text;

            // 2) If it's 4 bytes and not text, it is commonly a float in Unity PlayerPrefs.
            if (bytes.Length == 4)
            {
                float f = BitConverter.ToSingle(bytes, 0);
                if (!float.IsNaN(f) && !float.IsInfinity(f))
                    return f.ToString("R", CultureInfo.InvariantCulture);
            }

            // 3) Fallback: show as hex so we never lie about unknown binary formats.
            return "0x" + BytesToHex(bytes);
        }

        private static bool TryDecodeLikelyUtf8String(byte[] bytes, out string result)
        {
            result = null;

            // Trim trailing null bytes (common for registry-stored strings).
            int length = bytes.Length;
            while (length > 0 && bytes[length - 1] == 0)
            {
                length--;
            }

            if (length <= 0)
            {
                result = string.Empty;
                return true;
            }

            byte[] trimmed = new byte[length];
            Buffer.BlockCopy(bytes, 0, trimmed, 0, length);

            string decoded;
            try
            {
                UTF8Encoding utf8Strict = new(false, true);
                decoded = utf8Strict.GetString(trimmed);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            if (decoded.Length == 0)
            {
                result = decoded;
                return true;
            }

            int printable = 0;
            int controls = 0;

            for (int i = 0; i < decoded.Length; i++)
            {
                char c = decoded[i];

                if (c == '\t' || c == '\n' || c == '\r' || c == ' ')
                {
                    printable++;
                    continue;
                }

                if (char.IsControl(c))
                {
                    controls++;
                    continue;
                }

                printable++;
            }

            if (controls > 0)
            {
                double ratioWithControls = (double)printable / (printable + controls);
                if (ratioWithControls < 0.95)
                    return false;
            }

            if (decoded.Length == 1 && !char.IsLetterOrDigit(decoded[0]))
                return false;

            result = decoded;
            return true;
        }

        private static string BytesToHex(byte[] bytes)
        {
            const string hex = "0123456789ABCDEF";
            char[] chars = new char[bytes.Length * 2];

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                chars[i * 2] = hex[b >> 4];
                chars[i * 2 + 1] = hex[b & 0x0F];
            }

            return new string(chars);
        }
#endif

#if UNITY_EDITOR_OSX
        private void LoadMacPrefs()
        {
            // Use 'defaults read' which can read both XML and binary plists.
            // Domain: unity.<CompanyNoSpaces>.<ProductNoSpaces>
            string company = Application.companyName.Replace(" ", "");
            string product = Application.productName.Replace(" ", "");
            string domain = $"unity.{company}.{product}";

            string output = RunProcess("/usr/bin/defaults", $"read {domain}");
            if (string.IsNullOrWhiteSpace(output))
            {
                // Fallback: check file existence to provide a helpful warning.
                string plistPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    $"Library/Preferences/{domain}.plist"
                );

                if (!File.Exists(plistPath))
                    Debug.LogWarning($"PlayerPrefsEditor: plist not found: {plistPath}");
                else
                    Debug.LogWarning($"PlayerPrefsEditor: Unable to read defaults for domain: {domain}");

                return;
            }

            ParseDefaultsReadOutput(output);
        }

        private static string RunProcess(string fileName, string arguments)
        {
            try
            {
                using Process p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                p.Start();
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (!string.IsNullOrWhiteSpace(stderr) && string.IsNullOrWhiteSpace(stdout))
                    return string.Empty;

                return stdout;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PlayerPrefsEditor: Process failed: {ex.Message}");
                return string.Empty;
            }
        }

        private void ParseDefaultsReadOutput(string output)
        {
            // defaults read output usually looks like:
            // {
            //     key = value;
            //     otherKey = "string";
            // }
            // This parser covers simple top-level "key = value;" lines.
            using StringReader reader = new StringReader(output);
            string line;

            Regex rx = new Regex(@"^\s*([^=]+?)\s*=\s*(.+?)\s*;\s*$");

            while ((line = reader.ReadLine()) != null)
            {
                Match m = rx.Match(line);
                if (!m.Success)
                    continue;

                string key = m.Groups[1].Value.Trim().Trim('"');
                string value = m.Groups[2].Value.Trim().Trim('"');

                if (prefs.ContainsKey(key))
                    continue;

                prefs[key] = value;
                editedValues[key] = value;
            }
        }
#endif

        private void OnGUI()
        {
            EnsureUiInitialized();

            DrawToolbar();

            EditorGUILayout.Space(6);
            DrawAddSection();

            EditorGUILayout.Space(8);
            DrawTable();

            DrawFooter();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("PlayerPrefs Viewer", EditorStyles.toolbarButton);

                GUILayout.Space(8);

                GUILayout.Label("Search", GUILayout.Width(46));
                search = GUILayout.TextField(search, toolbarSearchField, GUILayout.MinWidth(180));

                if (GUILayout.Button(GUIContent.none, toolbarSearchCancel))
                {
                    search = string.Empty;
                    GUI.FocusControl(null);
                }

                GUILayout.FlexibleSpace();

                showOnlyModified = GUILayout.Toggle(showOnlyModified, "Only Modified", EditorStyles.toolbarButton);
                sortAscending = GUILayout.Toggle(sortAscending, sortAscending ? "Sort A→Z" : "Sort Z→A",
                    EditorStyles.toolbarButton);

                GUILayout.Space(8);

                GUIContent reloadContent = new(" Reload", iconRefresh, "Reload");
                if (GUILayout.Button(reloadContent, EditorStyles.toolbarButton))
                {
                    LoadPlayerPrefs();
                    SetStatus("Reloaded.");
                }
            }
        }

        private void DrawTable()
        {
            List<string> keys = new(prefs.Keys);
            ApplySearchFilter(keys);
            ApplyModifiedFilter(keys);
            ApplySorting(keys);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Key", EditorStyles.boldLabel, GUILayout.Width(320));
                GUILayout.Label("Value", EditorStyles.boldLabel);
                GUILayout.Label("Type", EditorStyles.boldLabel, GUILayout.Width(70));
                GUILayout.Label("Actions", EditorStyles.boldLabel, GUILayout.Width(150));
            }

            pendingSaveKey = null;
            pendingDeleteKey = null;
            pendingCopyKey = null;
            pendingCopyValueKey = null;

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                string original = prefs[key];

                if (!editedValues.TryGetValue(key, out string current))
                    current = original;

                bool isModified = !string.Equals(original ?? string.Empty, current ?? string.Empty,
                    StringComparison.Ordinal);

                GUIStyle bg = i % 2 == 0 ? rowStyle : rowAltStyle;

                using (new EditorGUILayout.HorizontalScope(bg))
                {
                    GUILayout.Label(key, keyStyle, GUILayout.Width(320));

                    EditorGUI.BeginChangeCheck();
                    string edited = EditorGUILayout.TextField(current ?? string.Empty, valueStyle);
                    if (EditorGUI.EndChangeCheck())
                        editedValues[key] = edited;

                    ValueKind kind = GuessValueKind(editedValues[key]);
                    DrawKindBadge(kind, GUILayout.Width(70));

                    using (new EditorGUILayout.HorizontalScope(GUILayout.Width(150)))
                    {
                        GUI.enabled = isModified;
                        if (IconButton(iconSave, "Save"))
                            pendingSaveKey = key;
                        GUI.enabled = true;

                        if (IconButton(iconDelete, "Delete"))
                            pendingDeleteKey = key;

                        if (IconButton(iconCopy, "Copy Key"))
                            pendingCopyKey = key;

                        if (IconButton(iconCopy, "Copy Value"))
                            pendingCopyValueKey = key;
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(pendingSaveKey))
                ApplySave(pendingSaveKey);

            if (!string.IsNullOrEmpty(pendingDeleteKey))
                ApplyDeleteWithConfirm(pendingDeleteKey);

            if (!string.IsNullOrEmpty(pendingCopyKey))
                CopyToClipboard(pendingCopyKey, false);

            if (!string.IsNullOrEmpty(pendingCopyValueKey))
                CopyToClipboard(pendingCopyValueKey, true);

            if (keys.Count == 0)
                EditorGUILayout.HelpBox(
                    "No PlayerPrefs found for the current project (or the current filter hides them).",
                    MessageType.Info);
        }

        private void ApplySearchFilter(List<string> keys)
        {
            if (string.IsNullOrWhiteSpace(search))
                return;

            string s = search.Trim();
            keys.RemoveAll(k =>
            {
                string value = prefs.TryGetValue(k, out string v) ? v : string.Empty;
                return (k?.IndexOf(s, StringComparison.OrdinalIgnoreCase) ?? -1) < 0 &&
                       (value?.IndexOf(s, StringComparison.OrdinalIgnoreCase) ?? -1) < 0;
            });
        }

        private void ApplyModifiedFilter(List<string> keys)
        {
            if (!showOnlyModified)
                return;

            keys.RemoveAll(k =>
            {
                string original = prefs.TryGetValue(k, out string o) ? o : string.Empty;
                string current = editedValues.TryGetValue(k, out string c) ? c : original;
                return string.Equals(original ?? string.Empty, current ?? string.Empty, StringComparison.Ordinal);
            });
        }

        private void ApplySorting(List<string> keys)
        {
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            if (!sortAscending)
                keys.Reverse();
        }

        private void DrawFooter()
        {
            using (new EditorGUILayout.HorizontalScope(footerStyle))
            {
                GUILayout.Label($"Entries: {prefs.Count}", EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                if (EditorApplication.timeSinceStartup < statusUntilTime)
                    GUILayout.Label(statusText, EditorStyles.miniBoldLabel);
            }
        }

        private static ValueKind GuessValueKind(string text)
        {
            if (text == null)
                return ValueKind.Unknown;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return ValueKind.Int;

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return ValueKind.Float;

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && text.Length > 2)
                return ValueKind.Unknown;

            return ValueKind.String;
        }

        private void DrawKindBadge(ValueKind kind, params GUILayoutOption[] options)
        {
            string label;
            MessageType mt;

            switch (kind)
            {
                case ValueKind.Int:
                    label = "INT";
                    mt = MessageType.None;
                    break;
                case ValueKind.Float:
                    label = "FLOAT";
                    mt = MessageType.None;
                    break;
                case ValueKind.String:
                    label = "STR";
                    mt = MessageType.None;
                    break;
                default:
                    label = "?";
                    mt = MessageType.Warning;
                    break;
            }

            Rect r = GUILayoutUtility.GetRect(new GUIContent(label), badgeStyle, options);

            Color bg = GetBadgeColor(mt);
            EditorGUI.DrawRect(r, bg);
            GUI.Label(r, label, badgeStyle);
        }

        private static Color GetBadgeColor(MessageType mt)
        {
            if (EditorGUIUtility.isProSkin)
            {
                if (mt == MessageType.Warning) return new Color(0.65f, 0.55f, 0.10f, 0.28f);
                return new Color(0.20f, 0.20f, 0.20f, 0.45f);
            }

            if (mt == MessageType.Warning) return new Color(0.90f, 0.75f, 0.15f, 0.30f);
            return new Color(0.85f, 0.85f, 0.85f, 0.60f);
        }

        private bool IconButton(Texture2D icon, string tooltip)
        {
            GUIContent content = new(icon, tooltip);
            return GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(32), GUILayout.Height(18));
        }

        private void CopyToClipboard(string key, bool isValue)
        {
            if (isValue)
            {
                string value = editedValues.TryGetValue(key, out string v) ? v :
                    prefs.TryGetValue(key, out string o) ? o : string.Empty;
                EditorGUIUtility.systemCopyBuffer = value ?? string.Empty;
                SetStatus("Value copied to clipboard.");
            }
            else
            {
                EditorGUIUtility.systemCopyBuffer = key ?? string.Empty;
                SetStatus("Key copied to clipboard.");
            }
        }

        private void SetStatus(string message)
        {
            statusText = message ?? string.Empty;
            statusUntilTime = EditorApplication.timeSinceStartup + 2.0;
            Repaint();
        }

        private void ApplySave(string key)
        {
            string text = editedValues.TryGetValue(key, out string v) ? v : string.Empty;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
                PlayerPrefs.SetInt(key, intVal);
            else if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatVal))
                PlayerPrefs.SetFloat(key, floatVal);
            else
                PlayerPrefs.SetString(key, text);

            PlayerPrefs.Save();
            LoadPlayerPrefs();
            SetStatus("Saved.");
        }

        private void ApplyDeleteWithConfirm(string key)
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Delete PlayerPref",
                $"Are you sure you want to delete:\n\n{key}",
                "Delete",
                "Cancel"
            );

            if (!confirm)
            {
                SetStatus("Delete canceled.");
                return;
            }

            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            LoadPlayerPrefs();
            SetStatus("Deleted.");
        }

        private void DrawAddSection()
        {
            EditorGUILayout.LabelField("Add New PlayerPref", headerStyle);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Key", GUILayout.Width(32));
                    newKey = EditorGUILayout.TextField(newKey, GUILayout.MinWidth(240));

                    GUILayout.Space(8);

                    EditorGUILayout.LabelField("Type", GUILayout.Width(36));
                    newType = (PrefsType)EditorGUILayout.EnumPopup(newType, GUILayout.Width(110));

                    GUILayout.Space(8);

                    EditorGUILayout.LabelField("Value", GUILayout.Width(40));
                    newValue = EditorGUILayout.TextField(newValue);

                    GUILayout.Space(8);

                    GUI.enabled = !string.IsNullOrWhiteSpace(newKey);
                    GUIContent addContent = new(" Add", iconPlus, "Add a new PlayerPref");
                    if (GUILayout.Button(addContent, GUILayout.Width(80), GUILayout.Height(20))) AddNewPref();
                    GUI.enabled = true;
                }

                EditorGUILayout.LabelField("Tip: Float values use '.' as decimal separator (InvariantCulture).",
                    EditorStyles.miniLabel);
            }
        }

        private void AddNewPref()
        {
            switch (newType)
            {
                case PrefsType.Int:
                    if (int.TryParse(newValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
                        PlayerPrefs.SetInt(newKey, intVal);
                    else
                        Debug.LogWarning("PlayerPrefsEditor: Invalid value for int (use integers only).");
                    break;

                case PrefsType.Float:
                    if (float.TryParse(newValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatVal))
                        PlayerPrefs.SetFloat(newKey, floatVal);
                    else
                        Debug.LogWarning(
                            "PlayerPrefsEditor: Invalid value for float (use '.' as decimal separator, e.g. 1.5).");
                    break;

                case PrefsType.String:
                    PlayerPrefs.SetString(newKey, newValue ?? string.Empty);
                    break;
            }

            PlayerPrefs.Save();

            newKey = string.Empty;
            newValue = string.Empty;

            LoadPlayerPrefs();
            SetStatus("Added.");
        }
    }
}
#endif
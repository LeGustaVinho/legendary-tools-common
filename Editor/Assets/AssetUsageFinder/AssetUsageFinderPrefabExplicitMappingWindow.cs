using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderPrefabExplicitMappingWindow : EditorWindow
    {
        private Object _fromPrefab;
        private Object _toPrefab;
        private Vector2 _scrollPosition;
        private AssetUsageFinderPrefabExplicitMappingProfile _profile;
        private List<AssetUsageFinderPrefabSubobjectDescriptor> _fromOptions = new();
        private List<AssetUsageFinderPrefabSubobjectDescriptor> _toOptions = new();
        private string _statusMessage = string.Empty;

        public static void ShowWindow(Object fromPrefab, Object toPrefab)
        {
            AssetUsageFinderPrefabExplicitMappingWindow window =
                GetWindow<AssetUsageFinderPrefabExplicitMappingWindow>("Prefab Mapping");
            window.minSize = new Vector2(760f, 420f);
            window.SetPrefabs(fromPrefab, toPrefab);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshProfile();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Explicit Prefab Subobject Mapping", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Use explicit mappings when the target prefab does not preserve the same hierarchy path or component order. " +
                    "These mappings are applied before the automatic fallback remap.",
                    MessageType.Info);

                using (EditorGUI.ChangeCheckScope change = new())
                {
                    _fromPrefab = EditorGUILayout.ObjectField("From Prefab", _fromPrefab, typeof(GameObject), false);
                    _toPrefab = EditorGUILayout.ObjectField("To Prefab", _toPrefab, typeof(GameObject), false);

                    if (change.changed)
                        RefreshProfile();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(!HasValidPrefabPair());
                    if (GUILayout.Button("Add Mapping", GUILayout.Height(24)))
                        AddMapping();
                    EditorGUI.EndDisabledGroup();

                    GUILayout.FlexibleSpace();

                    EditorGUI.BeginDisabledGroup(!HasValidPrefabPair());
                    if (GUILayout.Button("Refresh", GUILayout.Width(90), GUILayout.Height(24)))
                        RefreshProfile();
                    EditorGUI.EndDisabledGroup();
                }

                if (!string.IsNullOrEmpty(_statusMessage))
                    EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
            }

            if (!HasValidPrefabPair())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox("Select a valid source prefab and target prefab to edit explicit mappings.",
                        MessageType.Warning);
                }

                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            if (_profile == null)
                RefreshProfile();

            List<AssetUsageFinderPrefabExplicitRemapEntry> entries = _profile?.Entries;
            if (entries == null || entries.Count == 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "No explicit mapping rows yet. Click 'Add Mapping' to create one.",
                        MessageType.None);
                }
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    AssetUsageFinderPrefabExplicitRemapEntry entry = entries[i];
                    if (entry == null)
                        continue;

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField($"Mapping #{i + 1}", EditorStyles.miniBoldLabel);

                        bool changed = false;
                        changed |= DrawFromDescriptorPopup(entry);
                        changed |= DrawToDescriptorPopup(entry);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();

                            if (GUILayout.Button("Remove", GUILayout.Width(90)))
                            {
                                entries.RemoveAt(i);
                                SaveProfile("Mapping removed.");
                                GUIUtility.ExitGUI();
                            }
                        }

                        if (changed)
                            SaveProfile("Mapping updated.");
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void SetPrefabs(Object fromPrefab, Object toPrefab)
        {
            _fromPrefab = fromPrefab;
            _toPrefab = toPrefab;
            RefreshProfile();
        }

        private bool HasValidPrefabPair()
        {
            return AssetUsageFinderPrefabExplicitMappingStore.IsValidPrefabAsset(_fromPrefab) &&
                   AssetUsageFinderPrefabExplicitMappingStore.IsValidPrefabAsset(_toPrefab);
        }

        private void RefreshProfile()
        {
            if (!HasValidPrefabPair())
            {
                _profile = null;
                _fromOptions = new List<AssetUsageFinderPrefabSubobjectDescriptor>();
                _toOptions = new List<AssetUsageFinderPrefabSubobjectDescriptor>();
                _statusMessage = string.Empty;
                return;
            }

            _fromOptions = AssetUsageFinderPrefabExplicitMappingStore.GetAvailableDescriptors(_fromPrefab);
            _toOptions = AssetUsageFinderPrefabExplicitMappingStore.GetAvailableDescriptors(_toPrefab);
            _profile = AssetUsageFinderPrefabExplicitMappingStore.GetOrCreateProfile(_fromPrefab, _toPrefab);
            _profile ??= new AssetUsageFinderPrefabExplicitMappingProfile();
            _profile.Entries ??= new List<AssetUsageFinderPrefabExplicitRemapEntry>();
            _statusMessage = $"{_profile.Entries.Count} explicit mapping(s) configured.";
        }

        private void AddMapping()
        {
            if (_profile == null)
                RefreshProfile();

            if (_fromOptions.Count == 0 || _toOptions.Count == 0)
            {
                _statusMessage = "No valid subobjects were found to build a mapping row.";
                return;
            }

            AssetUsageFinderPrefabSubobjectDescriptor defaultFrom = _fromOptions[0].Clone();
            AssetUsageFinderPrefabSubobjectDescriptor defaultTo = GetDefaultToDescriptor(defaultFrom);
            if (defaultTo == null)
            {
                _statusMessage = "The selected 'From' item has no compatible target types in the destination prefab.";
                return;
            }

            _profile.Entries.Add(new AssetUsageFinderPrefabExplicitRemapEntry
            {
                From = defaultFrom,
                To = defaultTo
            });

            SaveProfile("Mapping added.");
        }

        private bool DrawFromDescriptorPopup(AssetUsageFinderPrefabExplicitRemapEntry entry)
        {
            if (_fromOptions == null || _fromOptions.Count == 0 || entry == null)
                return false;

            AssetUsageFinderPrefabSubobjectDescriptor current = entry.From ?? _fromOptions[0].Clone();

            string[] labels = new string[_fromOptions.Count];
            int currentIndex = 0;

            for (int i = 0; i < _fromOptions.Count; i++)
            {
                labels[i] = AssetUsageFinderPrefabExplicitMappingStore.GetDescriptorDisplayLabel(_fromOptions[i]);
                if (_fromOptions[i].Matches(current))
                    currentIndex = i;
            }

            using (EditorGUI.ChangeCheckScope change = new())
            {
                int nextIndex = EditorGUILayout.Popup("From", currentIndex, labels);
                if (!change.changed)
                    return false;

                entry.From = _fromOptions[nextIndex].Clone();
                return true;
            }
        }

        private bool DrawToDescriptorPopup(AssetUsageFinderPrefabExplicitRemapEntry entry)
        {
            if (entry == null)
                return false;

            List<AssetUsageFinderPrefabSubobjectDescriptor> compatibleOptions =
                AssetUsageFinderPrefabExplicitMappingCompatibility.GetCompatibleTargets(entry.From, _toOptions);

            if (compatibleOptions.Count == 0)
            {
                bool hadSelection = entry.To != null;
                if (hadSelection)
                    entry.To = null;

                EditorGUILayout.HelpBox("No compatible target types are available for the selected 'From' item.",
                    MessageType.Warning);
                return hadSelection;
            }

            bool changed = false;
            AssetUsageFinderPrefabSubobjectDescriptor current = entry.To;
            int currentIndex = 0;

            if (current == null)
            {
                entry.To = compatibleOptions[0].Clone();
                current = entry.To;
                changed = true;
            }

            string[] labels = new string[compatibleOptions.Count];

            for (int i = 0; i < compatibleOptions.Count; i++)
            {
                labels[i] = AssetUsageFinderPrefabExplicitMappingStore.GetDescriptorDisplayLabel(compatibleOptions[i]);
                if (compatibleOptions[i].Matches(current))
                    currentIndex = i;
            }

            if (!compatibleOptions[currentIndex].Matches(current))
            {
                entry.To = compatibleOptions[0].Clone();
                currentIndex = 0;
                changed = true;
            }

            using (EditorGUI.ChangeCheckScope change = new())
            {
                int nextIndex = EditorGUILayout.Popup("To", currentIndex, labels);
                if (!change.changed)
                    return changed;

                entry.To = compatibleOptions[nextIndex].Clone();
                return true;
            }
        }

        private AssetUsageFinderPrefabSubobjectDescriptor GetDefaultToDescriptor(
            AssetUsageFinderPrefabSubobjectDescriptor fromDescriptor)
        {
            List<AssetUsageFinderPrefabSubobjectDescriptor> compatibleOptions =
                AssetUsageFinderPrefabExplicitMappingCompatibility.GetCompatibleTargets(fromDescriptor, _toOptions);

            return compatibleOptions.Count > 0 ? compatibleOptions[0].Clone() : null;
        }

        private void SaveProfile(string statusMessage)
        {
            if (_profile == null)
                return;

            AssetUsageFinderPrefabExplicitMappingStore.SaveProfile(_profile);
            _statusMessage = statusMessage;
        }
    }
}
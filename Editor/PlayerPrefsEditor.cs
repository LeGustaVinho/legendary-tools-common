#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.IO;

#if UNITY_EDITOR_WIN
using Microsoft.Win32;
#endif

#if UNITY_EDITOR_OSX
using System.Xml;
#endif

namespace LegendaryTools.Editor
{
    public class PlayerPrefsEditor : EditorWindow
    {
        private Vector2 scrollPos;
        private Dictionary<string, string> prefs = new();
        private Dictionary<string, string> newValues = new();

        private string newKey = "";
        private string newValue = "";
        private PrefsType newType = PrefsType.String;

        private enum PrefsType
        {
            Int,
            Float,
            String
        }

        [MenuItem("Tools/PlayerPrefs/Viewer")]
        public static void ShowWindow()
        {
            GetWindow<PlayerPrefsEditor>("PlayerPrefs Viewer");
        }

        private void OnEnable()
        {
            LoadPlayerPrefs();
        }

        private void LoadPlayerPrefs()
        {
            prefs.Clear();
            newValues.Clear();

#if UNITY_EDITOR_WIN
            LoadWindowsPrefs();
#elif UNITY_EDITOR_OSX
        LoadMacPrefs();
#else
        Debug.LogWarning("PlayerPrefsEditor: Plataforma não suportada.");
#endif
        }

#if UNITY_EDITOR_WIN
        private void LoadWindowsPrefs()
        {
            string companyName = Application.companyName;
            string productName = Application.productName;

            string path = $"Software\\Unity\\UnityEditor\\{companyName}\\{productName}";
            RegistryKey key = Registry.CurrentUser.OpenSubKey(path);

            if (key != null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    string rawValue = key.GetValue(valueName)?.ToString();
                    string cleanedKey = valueName.Replace("_h", "");
                    if (!prefs.ContainsKey(cleanedKey))
                    {
                        prefs[cleanedKey] = rawValue;
                        newValues[cleanedKey] = rawValue;
                    }
                }
            }
        }
#endif

#if UNITY_EDITOR_OSX
    private void LoadMacPrefs()
    {
        string company = Application.companyName.Replace(" ", "");
        string product = Application.productName.Replace(" ", "");
        string plistPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            $"Library/Preferences/unity.{company}.{product}.plist"
        );

        if (!File.Exists(plistPath))
        {
            Debug.LogWarning($"Arquivo plist não encontrado: {plistPath}");
            return;
        }

        XmlDocument xml = new XmlDocument();
        xml.Load(plistPath);

        XmlNodeList keys = xml.GetElementsByTagName("key");
        foreach (XmlNode keyNode in keys)
        {
            string key = keyNode.InnerText;
            XmlNode valueNode = keyNode.NextSibling;

            while (valueNode != null && valueNode.NodeType != XmlNodeType.Element)
                valueNode = valueNode.NextSibling;

            if (valueNode == null) continue;

            string value = valueNode.InnerText;

            if (!prefs.ContainsKey(key))
            {
                prefs[key] = value;
                newValues[key] = value;
            }
        }
    }
#endif

        private void OnGUI()
        {
            EditorGUILayout.LabelField("PlayerPrefs Viewer", EditorStyles.boldLabel);

            DrawAddSection();

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Recarregar")) LoadPlayerPrefs();

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            foreach (var kvp in prefs)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(kvp.Key, GUILayout.Width(200));
                newValues[kvp.Key] = EditorGUILayout.TextField(newValues[kvp.Key]);

                if (GUILayout.Button("Salvar", GUILayout.Width(60)))
                {
                    if (int.TryParse(newValues[kvp.Key], out int intVal))
                        PlayerPrefs.SetInt(kvp.Key, intVal);
                    else if (float.TryParse(newValues[kvp.Key], out float floatVal))
                        PlayerPrefs.SetFloat(kvp.Key, floatVal);
                    else
                        PlayerPrefs.SetString(kvp.Key, newValues[kvp.Key]);

                    PlayerPrefs.Save();
                    LoadPlayerPrefs();
                }

                if (GUILayout.Button("Deletar", GUILayout.Width(60)))
                {
                    PlayerPrefs.DeleteKey(kvp.Key);
                    PlayerPrefs.Save();
                    LoadPlayerPrefs();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawAddSection()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Adicionar novo PlayerPref", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            newKey = EditorGUILayout.TextField("Chave", newKey);
            newType = (PrefsType)EditorGUILayout.EnumPopup("Tipo", newType);
            newValue = EditorGUILayout.TextField("Valor", newValue);
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(newKey));
            if (GUILayout.Button("Adicionar"))
            {
                switch (newType)
                {
                    case PrefsType.Int:
                        if (int.TryParse(newValue, out int intVal))
                            PlayerPrefs.SetInt(newKey, intVal);
                        else
                            Debug.LogWarning("Valor inválido para int.");
                        break;
                    case PrefsType.Float:
                        if (float.TryParse(newValue, out float floatVal))
                            PlayerPrefs.SetFloat(newKey, floatVal);
                        else
                            Debug.LogWarning("Valor inválido para float.");
                        break;
                    case PrefsType.String:
                        PlayerPrefs.SetString(newKey, newValue);
                        break;
                }

                PlayerPrefs.Save();
                newKey = "";
                newValue = "";
                LoadPlayerPrefs();
            }

            EditorGUI.EndDisabledGroup();
        }
    }
}
#endif
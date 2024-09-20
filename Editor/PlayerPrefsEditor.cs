﻿using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public static class PlayerPrefsTools
    {
        [MenuItem("Tools/PlayerPrefs/Clear")]
        private static void ClearPlayerPrefs()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            
            Debug.Log("PlayerPrefs data was deleted !");
        }
    }
}
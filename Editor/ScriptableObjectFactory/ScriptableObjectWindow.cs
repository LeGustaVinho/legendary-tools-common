using System;
using System.Linq;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal class EndNameEdit : EndNameEditAction
    {
        #region implemented abstract members of EndNameEditAction

        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            AssetDatabase.CreateAsset(EditorUtility.InstanceIDToObject(instanceId),
                AssetDatabase.GenerateUniqueAssetPath(pathName));
        }

        #endregion
    }

    /// <summary>
    /// Scriptable object window.
    /// </summary>
    public class ScriptableObjectWindow : EditorWindow
    {
        private static string[] names;

        private static Type[] types;
        private int selectedIndex;

        private static Type[] Types
        {
            get => types;
            set
            {
                types = value;
                names = types.Select(t => t.FullName).ToArray();
            }
        }

        public static void Init(Type[] scriptableObjects)
        {
            Types = scriptableObjects;

            ScriptableObjectWindow window =
                GetWindow<ScriptableObjectWindow>(true, "Create a new ScriptableObject", true);
            window.ShowPopup();
        }

        public void OnGUI()
        {
            GUILayout.Label("ScriptableObject Class");
            selectedIndex = EditorGUILayout.Popup(selectedIndex, names);

            if (GUILayout.Button("Create"))
            {
                ScriptableObject asset = CreateInstance(types[selectedIndex]);
                ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                    asset.GetInstanceID(),
                    CreateInstance<EndNameEdit>(),
                    string.Format("{0}.asset", names[selectedIndex]),
                    AssetPreview.GetMiniThumbnail(asset),
                    null);

                Close();
            }
        }
    }
}
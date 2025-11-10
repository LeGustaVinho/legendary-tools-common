#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.SOAP.Editor
{
    /// <summary>Creates a ScriptableObject Variant for the currently selected ScriptableObject.</summary>
    public static class VariantCreationMenu
    {
        [MenuItem("Assets/Create/ScriptableObject Variant", priority = 201)]
        private static void CreateVariant()
        {
            ScriptableObject obj = Selection.activeObject as ScriptableObject;
            if (obj == null || obj is ScriptableObjectVariant)
            {
                EditorUtility.DisplayDialog("Create Variant",
                    "Please select a ScriptableObject asset in the Project window (not a Variant).", "OK");
                return;
            }

            string path = AssetDatabase.GetAssetPath(obj);
            string folder = System.IO.Path.GetDirectoryName(path);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);

            ScriptableObjectVariant variant = ScriptableObject.CreateInstance<ScriptableObjectVariant>();
            variant.BaseAsset = obj;

            string newPath = AssetDatabase.GenerateUniqueAssetPath(
                System.IO.Path.Combine(folder!, $"{fileName}_Variant.asset"));

            AssetDatabase.CreateAsset(variant, newPath);

            // Initialize payload only now (on creation)
            variant.__Editor_EnsurePayload(true);

            EditorUtility.SetDirty(variant);
            AssetDatabase.SaveAssets();
            Selection.activeObject = variant;
        }

        [MenuItem("Assets/Create/ScriptableObject Variant", true)]
        private static bool CreateVariantValidation()
        {
            return Selection.activeObject is ScriptableObject
                   && !(Selection.activeObject is ScriptableObjectVariant);
        }
    }
}
#endif
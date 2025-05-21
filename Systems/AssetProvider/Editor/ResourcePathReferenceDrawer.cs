using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Systems.AssetProvider.Editor
{
    [CustomPropertyDrawer(typeof(ResourcePathReference))]
    public class ResourcePathReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Divide o rect em dois, um para o ObjectField e outro para o caminho read-only
            Rect objectFieldRect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            Rect pathRect = new(position.x,
                position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
                position.width, EditorGUIUtility.singleLineHeight);

            SerializedProperty resourcePathProperty =
                property.FindPropertyRelative(nameof(ResourcePathReference.resourcePath));

            // Tenta carregar o objeto atual com base no caminho salvo, se houver
            Object currentAsset = null;
            if (!string.IsNullOrEmpty(resourcePathProperty.stringValue))
                currentAsset = Resources.Load(resourcePathProperty.stringValue);

            // Utiliza EditorGUI.ObjectField para permitir a seleção de objetos
            Object newAsset = EditorGUI.ObjectField(objectFieldRect, label, currentAsset, typeof(Object), false);

            // Atualiza o caminho se um novo objeto foi selecionado
            if (newAsset != currentAsset)
            {
                string assetPath = AssetDatabase.GetAssetPath(newAsset);

                if (assetPath.Contains("/Resources/"))
                {
                    string resourcesPath = "/Resources/";
                    int index = assetPath.IndexOf(resourcesPath);
                    if (index >= 0)
                    {
                        string relativePath = assetPath.Substring(index + resourcesPath.Length);

                        int extIndex = relativePath.LastIndexOf('.');
                        if (extIndex >= 0) relativePath = relativePath.Substring(0, extIndex);

                        resourcePathProperty.stringValue = relativePath;
                    }
                    else
                    {
                        Debug.LogWarning("The selected object must be within a 'Resources' folder.");
                    }
                }
                else
                {
                    Debug.LogWarning("The selected object must be within a 'Resources' folder.");
                }
            }

            // Exibe o caminho atual de forma read-only
            GUI.enabled = false; // Desabilita a interação com o campo seguinte
            EditorGUI.TextField(pathRect, "Resource Path", resourcePathProperty.stringValue);
            GUI.enabled = true; // Reabilita a interação para os próximos campos

            property.serializedObject.ApplyModifiedProperties();

            EditorGUI.EndProperty();
        }
    }
}
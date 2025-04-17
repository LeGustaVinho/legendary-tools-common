using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace LegendaryTools.Editor
{
    public class CopySerializedValuesWindow : EditorWindow
    {
        private Component sourceComponent;
        private Component destinationComponent;

        [MenuItem("Tools/Copy Serialized Values")]
        public static void ShowWindow()
        {
            GetWindow<CopySerializedValuesWindow>("Copiar Valores Serializados");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Componente Fonte", EditorStyles.boldLabel);
            sourceComponent = EditorGUILayout.ObjectField(sourceComponent, typeof(Component), true) as Component;

            EditorGUILayout.LabelField("Componente Destino", EditorStyles.boldLabel);
            destinationComponent =
                EditorGUILayout.ObjectField(destinationComponent, typeof(Component), true) as Component;

            if (sourceComponent == null || destinationComponent == null)
            {
                EditorGUILayout.HelpBox("Por favor, defina ambos os componentes.", MessageType.Warning);
                return;
            }

            if (GUILayout.Button("Copiar Valores Serializados"))
            {
                CopySerializedValues(sourceComponent, destinationComponent);
            }
        }

        private void CopySerializedValues(Component source, Component destination)
        {
            // Obtem os tipos dos componentes
            System.Type sourceType = source.GetType();
            System.Type destType = destination.GetType();

            // Obtem todos os campos (públicos e privados) do componente fonte
            FieldInfo[] sourceFields =
                sourceType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            // Permite desfazer a operação
            Undo.RecordObject(destination, "Copiar Valores Serializados");

            foreach (FieldInfo field in sourceFields)
            {
                // Pula o campo m_Script, que é gerado automaticamente
                if (field.Name == "m_Script")
                    continue;

                // Verifica se o campo é serializado:
                // - Deve ser público ou ter o atributo [SerializeField]
                // - E não pode ter o atributo [NonSerialized]
                bool isPublic = field.IsPublic;
                bool hasSerializeField = field.GetCustomAttribute(typeof(SerializeField)) != null;
                bool isNotSerialized = field.GetCustomAttribute(typeof(System.NonSerializedAttribute)) != null;
                if ((!isPublic && !hasSerializeField) || isNotSerialized)
                    continue;

                // Procura um campo no componente destino com o mesmo nome e tipo
                FieldInfo destField = destType.GetField(field.Name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (destField == null)
                    continue;
                if (destField.FieldType != field.FieldType)
                    continue;

                // Copia o valor do campo
                object value = field.GetValue(source);
                destField.SetValue(destination, value);
            }

            // Marca o objeto destino como modificado para que o Unity salve a alteração
            EditorUtility.SetDirty(destination);
            Debug.Log("Valores copiados de " + source.name + " para " + destination.name);
        }
    }
}
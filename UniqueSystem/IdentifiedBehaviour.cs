using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace LegendaryTools
{
    public class IdentifiedBehaviour<TEnum> : 
#if ODIN_INSPECTOR
        Sirenix.OdinInspector.SerializedMonoBehaviour,
#else
        MonoBehaviour,
#endif
        IWeaveExec
        where TEnum : struct, Enum, IConvertible
    {
        public WeaveExecType WeaveExecType => WeaveExecType.AfterCompile;

#if ODIN_INSPECTOR
        [HideInInspector]
#endif
        [SerializeField] private TEnum type;
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowIf("IsValidType")]
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public TEnum Type
        {
            get => type;
            set => type = value;
        }

        public bool IsValidType => Type.ToString() == name.FilterEnumName();
        
        private static readonly Dictionary<TEnum, IdentifiedBehaviour<TEnum>> GameObjectsByType = 
            new Dictionary<TEnum, IdentifiedBehaviour<TEnum>>();

        public static bool TryGetValue(TEnum enumName, out IdentifiedBehaviour<TEnum> uniqueBehaviour)
        {
            return GameObjectsByType.TryGetValue(enumName, out uniqueBehaviour);
        }
        
        public static bool Contains(TEnum enumName)
        {
            return GameObjectsByType.ContainsKey(enumName);
        }
        
        protected virtual void Awake()
        {
            if (!GameObjectsByType.ContainsKey(Type)) GameObjectsByType.Add(Type, this);
        }

#if UNITY_EDITOR
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        [Sirenix.OdinInspector.HideIf("IsValidType")]
#else
        [ContextMenu("CreateTypeForMe")]
#endif
        public void CreateTypeForMe()
        {
            List<(IdentifiedBehaviour<TEnum>, GameObject)> prefabs =
                EditorExtensions.FindPrefabsOfType<IdentifiedBehaviour<TEnum>>();
            List<(IdentifiedBehaviour<TEnum>, GameObject)> sceneObjects =
                EditorExtensions.FindSceneObjectsOfType<IdentifiedBehaviour<TEnum>>();
            List<(IdentifiedBehaviour<TEnum>, GameObject)> allGameObjects = new(prefabs.Count + sceneObjects.Count);
            allGameObjects.AddRange(prefabs);
            allGameObjects.AddRange(sceneObjects);
            
            List<string> configEnumNames = new List<string>();
            foreach ((IdentifiedBehaviour<TEnum>, GameObject) curGameObject in allGameObjects)
            {
                string enumName = curGameObject.Item2.name.FilterEnumName();
                if(!configEnumNames.Contains(enumName))
                    configEnumNames.Add(enumName);
            }

            Type enumType = typeof(TEnum);
            List<MonoScript> enumCodeFile = EditorExtensions.FindAssetsByName<MonoScript>(enumType.Name);
            string configPath = AssetDatabase.GetAssetPath(this);
            if (enumCodeFile.Count > 0)
            {
                configPath = AssetDatabase.GetAssetPath(enumCodeFile[0]);
            }
            string configFolder = Path.GetDirectoryName(configPath);
            WeaverUtils.Enum(configEnumNames.ToArray(), enumType.Namespace, enumType.Name, configFolder);
        }
#endif

        protected void OnValidate()
        {
            if (!IsValidType)
            {
                RunWeaver();
            }
        }

        public void RunWeaver()
        {
            Type = name.FilterEnumName().GetEnumValue<TEnum>();
        }
    }
}
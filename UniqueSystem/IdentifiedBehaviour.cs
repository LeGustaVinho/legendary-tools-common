using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LegendaryTools
{
    public class IdentifiedBehaviour<TEnum> : UnityBehaviour, IWeaveExec
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
        
        private static readonly Dictionary<TEnum, IdentifiedBehaviour<TEnum>> GameObjectsByType 
            = new Dictionary<TEnum, IdentifiedBehaviour<TEnum>>();

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
            // Register in dictionary if not present
            if (!GameObjectsByType.ContainsKey(Type))
                GameObjectsByType.Add(Type, this);
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
            List<(IdentifiedBehaviour<TEnum>, GameObject)> allGameObjects =
                new(prefabs.Count + sceneObjects.Count);
            
            allGameObjects.AddRange(prefabs);
            allGameObjects.AddRange(sceneObjects);
            
            List<string> configEnumNames = new List<string>();
            foreach ((IdentifiedBehaviour<TEnum> identified, GameObject go) in allGameObjects)
            {
                string enumName = go.name.FilterEnumName();
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
            WeaverUtils.Enum(configEnumNames.ToArray(), enumType.Namespace, enumType.Name, configFolder, true);
        }
#endif

        /// <summary>
        /// If the <c>Type</c> isn't valid, we run the weaver and potentially change <c>Type</c>.
        /// We then remove the old entry from the dictionary and re-add the new one if needed.
        /// </summary>
        protected void OnValidate()
        {
            // If name doesn't match the stored Type
            if (!IsValidType)
            {
                TEnum oldType = Type;
                
                // Recompute 'Type' from the name
                RunWeaver();

                // If the Type actually changed, remove from dictionary under the old key
                // and re-add under the new key if not present.
                if (!EqualityComparer<TEnum>.Default.Equals(oldType, Type))
                {
                    if (GameObjectsByType.TryGetValue(oldType, out var existing) && existing == this)
                    {
                        GameObjectsByType.Remove(oldType);
                    }

                    if (!GameObjectsByType.ContainsKey(Type))
                    {
                        GameObjectsByType.Add(Type, this);
                    }
                }
            }
        }

        public void RunWeaver()
        {
            Type = name.FilterEnumName().GetEnumValue<TEnum>();
        }

        /// <summary>
        /// Clean up the static dictionary when this object is destroyed.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (GameObjectsByType.TryGetValue(Type, out var existing) && existing == this)
            {
                GameObjectsByType.Remove(Type);
            }
        }
    }
}
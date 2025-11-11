using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#endif

namespace LegendaryTools
{
    public static class WeaverUtils
    {
#pragma warning disable 0414
        private static string WEAVER_CLASS_FILE_TEMPLATE = @"namespace {NAMESPACE}
{
    /// <summary>
    /// Generated code, dont manually change this file !
    /// </summary>
    public static class {CLASS_NAME}
    {
{CLASS_MEMBERS}
    }
}";

        private static string WEAVER_ENUM_FILE_TEMPLATE = @"namespace {NAMESPACE}
{
    /// <summary>
    /// Generated code, dont manually change this file !
    /// </summary>
    public enum {ENUM_NAME}
    {
{ENUM_MEMBERS}
    }
}";

        private static string WEAVER_ENUM_MEMBER_FORMAT = @"        {0},";
        private static string WEAVER_ENUM_MEMBER_WIH_VALUE_FORMAT = @"        {0} = {1},";

        private static string WEAVE_STATIC_READONLY_STRING_MEMBER_FORMAT =
            "        public static readonly string {0} = \"{1}\";";

        private static string CSHARP_CLASS_EXT = ".cs";
#pragma warning restore 0414

#if UNITY_EDITOR
        /// <summary>
        /// Creates a static class with string constants from a list of names.
        /// </summary>
        public static void ClassConstantNameListing(string[] names, string namespaceName, string className,
            string folderPath = "")
        {
            StringBuilder sb = new();

            foreach (string s in names)
            {
                sb.AppendLine(string.Format(WEAVE_STATIC_READONLY_STRING_MEMBER_FORMAT,
                    s.CamelCaseToAllUpperWithUnderscores(),
                    s));
            }

            string file = WEAVER_CLASS_FILE_TEMPLATE.Replace("{NAMESPACE}", namespaceName);
            file = file.Replace("{CLASS_NAME}", className);
            file = file.Replace("{CLASS_MEMBERS}", sb.ToString());

            File.WriteAllText(Path.Combine(folderPath, className + CSHARP_CLASS_EXT), file);

            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Creates an enum file from a list of names. Optionally assigns hashed values to each member.
        /// </summary>
        public static void Enum(string[] names, string namespaceName, string enumName, string folderPath = "",
            bool generateValue = false)
        {
            StringBuilder sb = new();
            HashSet<int> enumValuesInt = new();

            foreach (string s in names)
            {
                string enumEntryName = s.Replace(" ", "");
                string enumValueHash = string.Empty;
                if (generateValue)
                {
                    int enumHashValueInt = GetSHA256HashAsInt(enumEntryName);
                    if (!enumValuesInt.Add(enumHashValueInt))
                    {
                        Debug.LogError(
                            $"[{nameof(WeaverUtils)}:{nameof(Enum)}] Collision detected with enum name {enumEntryName} for enum type {namespaceName}.{enumName} in folder {folderPath}, try to use another name.");
                        continue;
                    }

                    enumValueHash = enumHashValueInt.ToString();
                }

                sb.AppendLine(generateValue
                    ? string.Format(WEAVER_ENUM_MEMBER_WIH_VALUE_FORMAT, enumEntryName, enumValueHash)
                    : string.Format(WEAVER_ENUM_MEMBER_FORMAT, enumEntryName));
            }

            string file = WEAVER_ENUM_FILE_TEMPLATE.Replace("{NAMESPACE}", namespaceName);
            file = file.Replace("{ENUM_NAME}", enumName);
            file = file.Replace("{ENUM_MEMBERS}", sb.ToString());

            File.WriteAllText(Path.Combine(folderPath, enumName + CSHARP_CLASS_EXT), file);

            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
        }

        public static int GetSHA256HashAsInt(string str)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(str);
                byte[] hashBytes = sha256.ComputeHash(bytes);
                return BitConverter.ToInt32(hashBytes, 0);
            }
        }

        /// <summary>
        /// Descriptor for each selected UI component to be generated inside the class.
        /// </summary>
        public class UIBindingItem
        {
            /// <summary> The final field name in the generated class. </summary>
            public string FieldName;

            /// <summary> The actual component reference (used for type discovery and validation). </summary>
            public Component Component;

            /// <summary> Selected property indices according to ComponentProperties map. </summary>
            public List<int> SelectedPropertyIndices = new();

            /// <summary> Selected event indices according to ComponentEvents map. </summary>
            public List<int> SelectedEventIndices = new();
        }

        /// <summary>
        /// Property map per component type used to autogenerate get/set wrappers.
        /// </summary>
        private static readonly Dictionary<Type, List<(string propertyName, string propertyType)>> ComponentProperties =
            new()
            {
                {
                    typeof(Text),
                    new List<(string propertyName, string propertyType)>
                        { ("text", "string"), ("color", "Color"), ("enabled", "bool") }
                },
                {
                    typeof(TextMeshProUGUI),
                    new List<(string propertyName, string propertyType)>
                        { ("text", "string"), ("color", "Color"), ("enabled", "bool") }
                },
                {
                    typeof(TMP_Text),
                    new List<(string propertyName, string propertyType)>
                        { ("text", "string"), ("color", "Color"), ("enabled", "bool") }
                },
                { typeof(TMP_InputField), new List<(string propertyName, string propertyType)> { ("text", "string") } },
                {
                    typeof(Image),
                    new List<(string propertyName, string propertyType)>
                    {
                        ("color", "Color"), ("sprite", "Sprite"), ("fillAmount", "float"), ("enabled", "bool")
                    }
                },
                {
                    typeof(RawImage),
                    new List<(string propertyName, string propertyType)>
                        { ("color", "Color"), ("texture", "Texture"), ("enabled", "bool") }
                },
                { typeof(Button), new List<(string propertyName, string propertyType)> { ("interactable", "bool") } },
                { typeof(Toggle), new List<(string propertyName, string propertyType)> { ("isOn", "bool") } },
                { typeof(Slider), new List<(string propertyName, string propertyType)> { ("value", "float") } },
                { typeof(Scrollbar), new List<(string propertyName, string propertyType)> { ("value", "float") } },
                { typeof(Dropdown), new List<(string propertyName, string propertyType)> { ("value", "int") } },
                { typeof(InputField), new List<(string propertyName, string propertyType)> { ("text", "string") } },
                {
                    typeof(ScrollRect),
                    new List<(string propertyName, string propertyType)> { ("normalizedPosition", "Vector2") }
                },
                { typeof(Mask), new List<(string propertyName, string propertyType)> { } }
            };

        /// <summary>
        /// Event map per component type used to autogenerate listeners and handlers.
        /// </summary>
        private static readonly Dictionary<Type, List<(string eventName, string eventType, string handlerSignature)>>
            ComponentEvents =
                new()
                {
                    {
                        typeof(Button),
                        new List<(string eventName, string eventType, string handlerSignature)>
                            { ("onClick", "UnityEngine.Events.UnityEvent", "()") }
                    },
                    {
                        typeof(Toggle),
                        new List<(string eventName, string eventType, string handlerSignature)>
                            { ("onValueChanged", "UnityEngine.Events.UnityEvent<bool>", "(bool value)") }
                    },
                    {
                        typeof(Slider),
                        new List<(string eventName, string eventType, string handlerSignature)>
                            { ("onValueChanged", "UnityEngine.Events.UnityEvent<float>", "(float value)") }
                    },
                    {
                        typeof(Scrollbar),
                        new List<(string eventName, string eventType, string handlerSignature)>
                            { ("onValueChanged", "UnityEngine.Events.UnityEvent<float>", "(float value)") }
                    },
                    {
                        typeof(Dropdown),
                        new List<(string eventName, string eventType, string handlerSignature)>
                            { ("onValueChanged", "UnityEngine.Events.UnityEvent<int>", "(int value)") }
                    },
                    {
                        typeof(InputField), new List<(string eventName, string eventType, string handlerSignature)>
                        {
                            ("onValueChanged", "UnityEngine.Events.UnityEvent<string>", "(string value)"),
                            ("onEndEdit", "UnityEngine.Events.UnityEvent<string>", "(string value)")
                        }
                    },
                    {
                        typeof(TMP_InputField), new List<(string eventName, string eventType, string handlerSignature)>
                        {
                            ("onValueChanged", "UnityEngine.Events.UnityEvent<string>", "(string value)"),
                            ("onEndEdit", "UnityEngine.Events.UnityEvent<string>", "(string value)"),
                            ("onSubmit", "UnityEngine.Events.UnityEvent<string>", "(string value)")
                        }
                    },
                    {
                        typeof(ScrollRect),
                        new List<(string eventName, string eventType, string handlerSignature)>
                            { ("onValueChanged", "UnityEngine.Events.UnityEvent<Vector2>", "(Vector2 value)") }
                    },
                    { typeof(Text), new List<(string eventName, string eventType, string handlerSignature)> { } },
                    {
                        typeof(TextMeshProUGUI),
                        new List<(string eventName, string eventType, string handlerSignature)> { }
                    },
                    { typeof(TMP_Text), new List<(string eventName, string eventType, string handlerSignature)> { } },
                    { typeof(Image), new List<(string eventName, string eventType, string handlerSignature)> { } },
                    { typeof(RawImage), new List<(string eventName, string eventType, string handlerSignature)> { } },
                    { typeof(Mask), new List<(string eventName, string eventType, string handlerSignature)> { } }
                };

        /// <summary>
        /// Builds the C# class content for a MonoBehaviour that binds UI components, properties and events.
        /// </summary>
        public static string BuildUIBindingClassContent(
            string namespaceName,
            string className,
            IList<UIBindingItem> items,
            bool useSerializeField,
            bool useBackingFields)
        {
            // Collect required using statements based on selected items.
            List<string> usingStatements = new() { "UnityEngine", "UnityEngine.UI" };
            if (items.Any(c => c.Component != null && (
                    c.Component.GetType() == typeof(TMP_Text) ||
                    c.Component.GetType() == typeof(TextMeshProUGUI) ||
                    c.Component.GetType() == typeof(TMP_InputField))))
                usingStatements.Add("TMPro");
            if (items.Any(c => c.SelectedEventIndices.Any())) usingStatements.Add("UnityEngine.Events");

            StringBuilder sb = new();
            foreach (string us in usingStatements.Distinct())
            {
                sb.AppendLine($"using {us};");
            }

            sb.AppendLine();

            if (!string.IsNullOrEmpty(namespaceName))
                sb.AppendLine($"namespace {namespaceName}\n{{");

            sb.AppendLine($"    public class {className} : MonoBehaviour");
            sb.AppendLine("    {");

            // Fields
            foreach (UIBindingItem item in items)
            {
                if (item.Component == null) continue;
                string typeName = item.Component.GetType().Name;
                string accessModifier = useSerializeField ? "[SerializeField] private" : "public";
                sb.AppendLine($"        {accessModifier} {typeName} {item.FieldName};");
            }

            sb.AppendLine();

            // Properties
            foreach (UIBindingItem item in items)
            {
                if (item.Component == null) continue;
                Type compType = item.Component.GetType();
                if (!ComponentProperties.ContainsKey(compType) || ComponentProperties[compType].Count == 0)
                    continue;

                foreach (int propIndex in item.SelectedPropertyIndices)
                {
                    (string propertyName, string propertyType) prop = ComponentProperties[compType][propIndex];
                    string propName = prop.propertyName;
                    string propType = prop.propertyType;
                    string capitalizedProp = char.ToUpper(propName[0]) + propName.Substring(1);
                    string backingFieldName = $"_{item.FieldName}{capitalizedProp}";

                    if (useBackingFields)
                    {
                        sb.AppendLine($"        [SerializeField] private {propType} {backingFieldName};");
                        sb.AppendLine($"        public {propType} {item.FieldName}{capitalizedProp}");
                        sb.AppendLine("        {");
                        sb.AppendLine($"            get => {backingFieldName};");
                        sb.AppendLine(
                            $"            set {{ {backingFieldName} = value; if ({item.FieldName} != null) {item.FieldName}.{propName} = value; }}");
                        sb.AppendLine("        }");
                    }
                    else
                    {
                        sb.AppendLine($"        public {propType} {item.FieldName}{capitalizedProp}");
                        sb.AppendLine("        {");
                        sb.AppendLine(
                            $"            get => {item.FieldName} != null ? {item.FieldName}.{propName} : default;");
                        sb.AppendLine(
                            $"            set {{ if ({item.FieldName} != null) {item.FieldName}.{propName} = value; }}");
                        sb.AppendLine("        }");
                    }
                }
            }

            sb.AppendLine();

            // Events
            bool hasEvents = items.Any(c => c.SelectedEventIndices.Any());
            if (hasEvents)
            {
                sb.AppendLine("        private void Awake()");
                sb.AppendLine("        {");
                foreach (UIBindingItem item in items.Where(c => c.SelectedEventIndices.Any()))
                {
                    Type compType = item.Component.GetType();
                    foreach (int eventIndex in item.SelectedEventIndices)
                    {
                        (string eventName, string eventType, string handlerSignature) eventInfo =
                            ComponentEvents[compType][eventIndex];
                        string handlerName = $"On{item.FieldName}{eventInfo.eventName}";
                        sb.AppendLine(
                            $"            if ({item.FieldName} != null) {item.FieldName}.{eventInfo.eventName}.AddListener({handlerName});");
                    }
                }

                sb.AppendLine("        }");
                sb.AppendLine();

                sb.AppendLine("        private void OnDestroy()");
                sb.AppendLine("        {");
                foreach (UIBindingItem item in items.Where(c => c.SelectedEventIndices.Any()))
                {
                    Type compType = item.Component.GetType();
                    foreach (int eventIndex in item.SelectedEventIndices)
                    {
                        (string eventName, string eventType, string handlerSignature) eventInfo =
                            ComponentEvents[compType][eventIndex];
                        string handlerName = $"On{item.FieldName}{eventInfo.eventName}";
                        sb.AppendLine(
                            $"            if ({item.FieldName} != null) {item.FieldName}.{eventInfo.eventName}.RemoveListener({handlerName});");
                    }
                }

                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Handlers
            foreach (UIBindingItem item in items.Where(c => c.SelectedEventIndices.Any()))
            {
                Type compType = item.Component.GetType();
                foreach (int eventIndex in item.SelectedEventIndices)
                {
                    (string eventName, string eventType, string handlerSignature) eventInfo =
                        ComponentEvents[compType][eventIndex];
                    string handlerName = $"On{item.FieldName}{eventInfo.eventName}";
                    sb.AppendLine($"        private void {handlerName}{eventInfo.handlerSignature}");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            // TODO: Implement {eventInfo.eventName} handler for {item.FieldName}");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("    }");
            if (!string.IsNullOrEmpty(namespaceName))
                sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Builds and writes the UI binding class file. Returns the relative asset path (Assets/...) or null if cancelled/invalid.
        /// </summary>
        public static string GenerateUIBindingClassFile(
            string namespaceName,
            string className,
            IList<UIBindingItem> items,
            bool useSerializeField,
            bool useBackingFields)
        {
            string classContent =
                BuildUIBindingClassContent(namespaceName, className, items, useSerializeField, useBackingFields);

            string path = EditorUtility.SaveFilePanel("Save Script", "Assets", className + ".cs", "cs");
            if (string.IsNullOrEmpty(path))
                return null;

            string normPath = path.Replace("\\", "/");
            string normAssets = Application.dataPath.Replace("\\", "/");
            if (!normPath.StartsWith(normAssets))
            {
                EditorUtility.DisplayDialog("Invalid Path",
                    "Please save the script inside the project's Assets folder.", "OK");
                return null;
            }

            File.WriteAllText(path, classContent);
            string relativePath = "Assets" + path.Substring(Application.dataPath.Length);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return relativePath;
        }

        // Expose maps for external use if needed (read-only)
        public static bool TryGetPropertyMap(Type type, out List<(string propertyName, string propertyType)> list)
        {
            return ComponentProperties.TryGetValue(type, out list);
        }

        public static bool TryGetEventMap(Type type,
            out List<(string eventName, string eventType, string handlerSignature)> list)
        {
            return ComponentEvents.TryGetValue(type, out list);
        }
#endif
    }
}
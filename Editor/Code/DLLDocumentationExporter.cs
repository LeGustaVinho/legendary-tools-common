using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public class DLLDocumentationExporter : EditorWindow
    {
        private string dllPath = "";
        private string xmlPath = "";
        private string outputFolder = "";
        private bool exportSingleFile = false;
        private string filterTypes = "";

        [MenuItem("Tools/LegendaryTools/Code/Export DLL API")]
        public static void ShowWindow()
        {
            GetWindow<DLLDocumentationExporter>("Export DLL API");
        }

        private void OnGUI()
        {
            GUILayout.Label("Select DLL and XML Documentation", EditorStyles.boldLabel);
        
            if (GUILayout.Button("Select DLL"))
            {
                string path = EditorUtility.OpenFilePanel("Select DLL", "", "dll");
                if (!string.IsNullOrEmpty(path))
                    dllPath = path;
            }
            GUILayout.Label("DLL: " + (string.IsNullOrEmpty(dllPath) ? "Not selected" : dllPath));

            if (GUILayout.Button("Select XML Documentation"))
            {
                string path = EditorUtility.OpenFilePanel("Select XML Documentation", "", "xml");
                if (!string.IsNullOrEmpty(path))
                    xmlPath = path;
            }
            GUILayout.Label("XML: " + (string.IsNullOrEmpty(xmlPath) ? "Not selected" : xmlPath));

            if (GUILayout.Button("Select Output Folder"))
            {
                string path = EditorUtility.OpenFolderPanel("Select Output Folder", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path))
                    outputFolder = path;
            }
            GUILayout.Label("Output Folder: " + (string.IsNullOrEmpty(outputFolder) ? "Not selected" : outputFolder));

            exportSingleFile = EditorGUILayout.Toggle("Export as single file", exportSingleFile);
            filterTypes = EditorGUILayout.TextField("Types to Export (comma separated)", filterTypes);

            if (GUILayout.Button("Export API"))
            {
                if (string.IsNullOrEmpty(dllPath) || string.IsNullOrEmpty(xmlPath) || string.IsNullOrEmpty(outputFolder))
                {
                    EditorUtility.DisplayDialog("Error", "Please select the DLL, XML documentation, and output folder.", "OK");
                    return;
                }

                try
                {
                    ExportAPI(dllPath, xmlPath, outputFolder, exportSingleFile, filterTypes);
                    EditorUtility.DisplayDialog("Export Completed", "API exported successfully.", "OK");
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("Error", "An error occurred:\n" + ex.Message, "OK");
                }
            }
        }

        private static void ExportAPI(string dllPath, string xmlPath, string outputFolder, bool exportSingleFile, string filterTypes)
        {
            // Load the assembly.
            Assembly assembly = Assembly.LoadFrom(dllPath);

            // Load and parse XML documentation.
            XDocument xmlDoc = XDocument.Load(xmlPath);
            Dictionary<string, string> docs = xmlDoc.Descendants("member")
                .ToDictionary(x => x.Attribute("name").Value,
                    x => x.Element("summary") != null ? x.Element("summary").Value.Trim() : "");

            // Gather eligible types: classes, structs, enums, and interfaces.
            var allTypes = assembly.GetExportedTypes().Where(t => t.IsClass || t.IsValueType || t.IsEnum || t.IsInterface);
            List<string> filterList = new List<string>();
            if (!string.IsNullOrEmpty(filterTypes))
            {
                filterList = filterTypes.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToList();
            }
            // If filter list is non-empty, only export types whose simple name is in the filter.
            IEnumerable<Type> types = (filterList.Count > 0) ?
                allTypes.Where(t => filterList.Contains(GetSimpleTypeName(t))) :
                allTypes;

            if (exportSingleFile)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (Type type in types)
                {
                    sb.AppendLine(GenerateTypeCode(type, docs));
                    sb.AppendLine(); // extra space between types
                }
                string filePath = Path.Combine(outputFolder, "APIDocumentation.cs");
                File.WriteAllText(filePath, sb.ToString());
            }
            else
            {
                foreach (Type type in types)
                {
                    string code = GenerateTypeCode(type, docs);
                    string safeFileName = GetSafeFileName(type) + ".cs";
                    string filePath = Path.Combine(outputFolder, safeFileName);
                    File.WriteAllText(filePath, code);
                }
            }
            AssetDatabase.Refresh();
        }

        private static string GenerateTypeCode(Type type, Dictionary<string, string> docs)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            // If the type is in a namespace, add a namespace block.
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                sb.AppendLine("namespace " + type.Namespace);
                sb.AppendLine("{");
            }

            // Write XML documentation for the type if available.
            string typeKey = "T:" + GetTypeFullName(type);
            if (docs.TryGetValue(typeKey, out string typeSummary) && !string.IsNullOrEmpty(typeSummary))
            {
                WriteXmlSummary(sb, typeSummary, "    ");
            }

            // Build the type declaration.
            string typeDecl = "";
            if (type.IsEnum)
                typeDecl = "public enum " + type.Name;
            else if (type.IsInterface)
                typeDecl = "public interface " + GetTypeDeclarationName(type);
            else if (type.IsValueType)
                typeDecl = "public struct " + GetTypeDeclarationName(type);
            else if (type.IsClass)
                typeDecl = "public class " + GetTypeDeclarationName(type);

            sb.AppendLine((!string.IsNullOrEmpty(type.Namespace) ? "    " : "") + typeDecl);
            sb.AppendLine((!string.IsNullOrEmpty(type.Namespace) ? "    " : "") + "{");

            bool isInterface = type.IsInterface;

            if (type.IsEnum)
            {
                // For enums, list enum members.
                string[] names = Enum.GetNames(type);
                for (int i = 0; i < names.Length; i++)
                {
                    string memberName = names[i];
                    string memberKey = "F:" + GetTypeFullName(type) + "." + memberName;
                    if (docs.TryGetValue(memberKey, out string enumSummary) && !string.IsNullOrEmpty(enumSummary))
                    {
                        WriteXmlSummary(sb, enumSummary, "        ");
                    }
                    sb.AppendLine("        " + memberName + (i < names.Length - 1 ? "," : ""));
                }
            }
            else if (isInterface)
            {
                // For interfaces, export only properties and methods (no fields, no access modifiers).
                // Properties:
                PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                foreach (PropertyInfo prop in properties)
                {
                    if (prop.GetIndexParameters().Length > 0)
                        continue;

                    string propKey = "P:" + GetTypeFullName(type) + "." + prop.Name;
                    if (docs.TryGetValue(propKey, out string propSummary) && !string.IsNullOrEmpty(propSummary))
                    {
                        WriteXmlSummary(sb, propSummary, "        ");
                    }
                    string accessor = "";
                    if (prop.CanRead && prop.CanWrite)
                        accessor = " { get; set; }";
                    else if (prop.CanRead)
                        accessor = " { get; }";
                    else if (prop.CanWrite)
                        accessor = " { set; }";

                    sb.AppendLine("        " + GetFriendlyName(prop.PropertyType) + " " + prop.Name + accessor);
                }

                // Methods:
                MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => !m.IsSpecialName).ToArray();
                foreach (MethodInfo method in methods)
                {
                    string methodKey = "M:" + GetTypeFullName(type) + "." + method.Name;
                    string docText = "";
                    if (docs.TryGetValue(methodKey, out string temp))
                        docText = temp;
                    else
                    {
                        var keyMatch = docs.Keys.FirstOrDefault(k => k.StartsWith(methodKey + "("));
                        if (!string.IsNullOrEmpty(keyMatch))
                            docText = docs[keyMatch];
                    }
                    if (!string.IsNullOrEmpty(docText))
                    {
                        WriteXmlSummary(sb, docText, "        ");
                    }
                    // Generate the method signature with generic parameters and constraints.
                    sb.AppendLine("        " + GenerateMethodSignature(method, isInterface: true));
                }
            }
            else
            {
                // For classes and structs: export fields, properties, and methods.
                // Fields:
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                foreach (FieldInfo field in fields)
                {
                    if (field.IsSpecialName)
                        continue;
                    string fieldKey = "F:" + GetTypeFullName(type) + "." + field.Name;
                    if (docs.TryGetValue(fieldKey, out string fieldSummary) && !string.IsNullOrEmpty(fieldSummary))
                    {
                        WriteXmlSummary(sb, fieldSummary, "        ");
                    }
                    string modifiers = field.IsStatic ? "static " : "";
                    sb.AppendLine("        public " + modifiers + GetFriendlyName(field.FieldType) + " " + field.Name + ";");
                }

                // Properties:
                PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                foreach (PropertyInfo prop in properties)
                {
                    if (prop.GetIndexParameters().Length > 0)
                        continue;
                    string propKey = "P:" + GetTypeFullName(type) + "." + prop.Name;
                    if (docs.TryGetValue(propKey, out string propSummary) && !string.IsNullOrEmpty(propSummary))
                    {
                        WriteXmlSummary(sb, propSummary, "        ");
                    }
                    string modifiers = ((prop.GetGetMethod() != null && prop.GetGetMethod().IsStatic) ||
                                        (prop.GetSetMethod() != null && prop.GetSetMethod().IsStatic)) ? "static " : "";
                    string accessor = "";
                    if (prop.CanRead && prop.CanWrite)
                        accessor = " { get; set; }";
                    else if (prop.CanRead)
                        accessor = " { get; }";
                    else if (prop.CanWrite)
                        accessor = " { set; }";

                    sb.AppendLine("        public " + modifiers + GetFriendlyName(prop.PropertyType) + " " + prop.Name + accessor);
                }

                // Methods:
                MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => !m.IsSpecialName).ToArray();
                foreach (MethodInfo method in methods)
                {
                    string methodKey = "M:" + GetTypeFullName(type) + "." + method.Name;
                    string docText = "";
                    if (docs.TryGetValue(methodKey, out string temp))
                        docText = temp;
                    else
                    {
                        var keyMatch = docs.Keys.FirstOrDefault(k => k.StartsWith(methodKey + "("));
                        if (!string.IsNullOrEmpty(keyMatch))
                            docText = docs[keyMatch];
                    }
                    if (!string.IsNullOrEmpty(docText))
                    {
                        WriteXmlSummary(sb, docText, "        ");
                    }
                    sb.AppendLine("        public " + GenerateMethodSignature(method, isInterface: false));
                }
            }

            sb.AppendLine((!string.IsNullOrEmpty(type.Namespace) ? "    " : "") + "}");

            if (!string.IsNullOrEmpty(type.Namespace))
                sb.AppendLine("}");

            return sb.ToString();
        }

        // Generates a method signature including generic parameters and constraints.
        private static string GenerateMethodSignature(MethodInfo method, bool isInterface)
        {
            string modifiers = method.IsStatic ? "static " : "";
            // Return type: if void then "void"
            string returnType = method.ReturnType == typeof(void) ? "void" : GetFriendlyName(method.ReturnType);
            // Parameters:
            string parameters = string.Join(", ", method.GetParameters().Select(p => {
                string typeName = p.ParameterType.IsByRef 
                    ? GetFriendlyName(p.ParameterType.GetElementType()) 
                    : GetFriendlyName(p.ParameterType);
                string prefix = "";
                if (p.IsOut)
                    prefix = "out ";
                else if (p.ParameterType.IsByRef)
                    prefix = "ref ";
                return prefix + typeName + " " + p.Name;
            }));
        
            // Handle generic method parameters and constraints.
            string genericParameters = "";
            string genericConstraints = "";
            if (method.IsGenericMethod)
            {
                var genericArgs = method.GetGenericArguments();
                genericParameters = "<" + string.Join(", ", genericArgs.Select(a => a.Name)) + ">";
                List<string> constraintsClauses = new List<string>();
                foreach (var arg in genericArgs)
                {
                    List<string> constraintsList = new List<string>();
                    // struct constraint (if set, then must be a value type)
                    if (arg.GenericParameterAttributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
                        constraintsList.Add("struct");
                    else if (arg.GenericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
                        constraintsList.Add("class");

                    // Specific type constraints.
                    foreach (Type constraint in arg.GetGenericParameterConstraints())
                    {
                        constraintsList.Add(GetFriendlyName(constraint));
                    }
                    // new() constraint.
                    if (arg.GenericParameterAttributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) &&
                        !arg.GenericParameterAttributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
                    {
                        constraintsList.Add("new()");
                    }
                    if (constraintsList.Count > 0)
                    {
                        constraintsClauses.Add("where " + arg.Name + " : " + string.Join(", ", constraintsList));
                    }
                }
                if (constraintsClauses.Count > 0)
                    genericConstraints = " " + string.Join(" ", constraintsClauses);
            }

            string methodName = method.Name + genericParameters;
            string signature = "";
            if (isInterface)
            {
                // For interfaces, no access modifiers.
                signature = returnType + " " + methodName + "(" + parameters + ") " + genericConstraints + ";";
            }
            else
            {
                signature = modifiers + returnType + " " + methodName + "(" + parameters + ") " + genericConstraints + ";";
            }
            return signature;
        }

        // Returns the full type name for XML doc keys.
        private static string GetTypeFullName(Type type)
        {
            return type.FullName.Replace('+', '.');
        }

        // Returns a friendly type name, resolving generics and arrays.
        private static string GetFriendlyName(Type type)
        {
            if (type == typeof(void))
                return "void";
            if (type.IsGenericType)
            {
                string name = type.Name;
                int index = name.IndexOf('`');
                if (index > 0)
                    name = name.Substring(0, index);
                string genericArgs = string.Join(", ", type.GetGenericArguments().Select(t => GetFriendlyName(t)));
                return name + "<" + genericArgs + ">";
            }
            else if (type.IsArray)
            {
                return GetFriendlyName(type.GetElementType()) + "[]";
            }
            else
            {
                return type.Name;
            }
        }

        // Generates a type declaration name including generic parameters.
        private static string GetTypeDeclarationName(Type type)
        {
            string name = type.Name;
            int index = name.IndexOf('`');
            if (index > 0)
            {
                name = name.Substring(0, index);
                Type[] args = type.GetGenericArguments();
                name += "<" + string.Join(", ", args.Select(t => t.Name)) + ">";
            }
            return name;
        }

        // Returns a safe file name for the type (removes generic notation and invalid characters).
        private static string GetSafeFileName(Type type)
        {
            string name = type.Name;
            int backtickIndex = name.IndexOf('`');
            if (backtickIndex > 0)
            {
                name = name.Substring(0, backtickIndex);
            }
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        // Returns the simple type name (without generic notation) for filtering.
        private static string GetSimpleTypeName(Type type)
        {
            string name = type.Name;
            int index = name.IndexOf('`');
            if (index > 0)
            {
                name = name.Substring(0, index);
            }
            return name;
        }

        // Writes a multi-line XML summary with the given indent.
        private static void WriteXmlSummary(System.Text.StringBuilder sb, string summary, string indent)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return;
            sb.AppendLine(indent + "/// <summary>");
            var lines = summary.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                sb.AppendLine(indent + "/// " + line.Trim());
            }
            sb.AppendLine(indent + "/// </summary>");
        }
    }
}
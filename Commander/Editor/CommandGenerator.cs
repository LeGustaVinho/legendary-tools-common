// CommandGenerator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CommandPattern
{
    /// <summary>
    /// Editor script to generate command classes based on methods of a selected class.
    /// </summary>
    public class CommandGenerator : EditorWindow
    {
        private string outputPath = "Assets/Commands";
        private List<Type> availableClasses = new List<Type>();
        private Vector2 scrollPos;
        private string searchFilter = "";
        private int selectedClassIndex = 0;

        [MenuItem("Tools/Command Generator")]
        public static void ShowWindow()
        {
            GetWindow<CommandGenerator>("Command Generator");
        }

        private void OnEnable()
        {
            RefreshAvailableClasses();
        }

        private void OnGUI()
        {
            GUILayout.Label("Generate Command Classes for a Specific Class", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Output Path Field
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Output Path:", GUILayout.Width(100));
            outputPath = EditorGUILayout.TextField(outputPath);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Search Field
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Search Class:", GUILayout.Width(100));
            searchFilter = EditorGUILayout.TextField(searchFilter);
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                RefreshAvailableClasses();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Class Selection Dropdown
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Select Class:", GUILayout.Width(100));
            if (availableClasses.Count > 0)
            {
                string[] classNames = availableClasses
                    .Where(t => string.IsNullOrEmpty(searchFilter) || t.FullName.ToLower().Contains(searchFilter.ToLower()))
                    .Select(t => t.FullName)
                    .ToArray();

                if (classNames.Length == 0)
                {
                    GUILayout.Label("No classes match the search filter.");
                }
                else
                {
                    // Adjust selectedClassIndex if it exceeds the available classes after filtering
                    if (selectedClassIndex >= classNames.Length)
                        selectedClassIndex = 0;

                    selectedClassIndex = EditorGUILayout.Popup(selectedClassIndex, classNames);
                }
            }
            else
            {
                GUILayout.Label("No classes available.");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Generate Commands Button
            if (GUILayout.Button("Generate Commands"))
            {
                if (availableClasses.Count == 0)
                {
                    EditorUtility.DisplayDialog("No Classes Found", "There are no available classes to generate commands for.", "OK");
                    return;
                }

                string selectedClassName = availableClasses
                    .Where(t => string.IsNullOrEmpty(searchFilter) || t.FullName.ToLower().Contains(searchFilter.ToLower()))
                    .Select(t => t.FullName)
                    .ElementAtOrDefault(selectedClassIndex);

                if (string.IsNullOrEmpty(selectedClassName))
                {
                    EditorUtility.DisplayDialog("Invalid Selection", "Please select a valid class.", "OK");
                    return;
                }

                Type selectedType = availableClasses.FirstOrDefault(t => t.FullName == selectedClassName);
                if (selectedType == null)
                {
                    EditorUtility.DisplayDialog("Class Not Found", "The selected class could not be found.", "OK");
                    return;
                }

                GenerateCommandsForClass(selectedType);
            }

            EditorGUILayout.Space();

            // Display Logs or Messages
            // (Optional: Implement if you want to show logs within the window)
        }

        /// <summary>
        /// Refreshes the list of available classes by scanning all loaded assemblies.
        /// </summary>
        private void RefreshAvailableClasses()
        {
            availableClasses.Clear();

            // Get all loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                // Skip dynamic and system assemblies
                if (assembly.IsDynamic || assembly.FullName.StartsWith("System") || assembly.FullName.StartsWith("Unity"))
                    continue;

                foreach (var type in assembly.GetTypes())
                {
                    // Include only public, non-abstract classes
                    if (type.IsClass && type.IsPublic && !type.IsAbstract)
                    {
                        availableClasses.Add(type);
                    }
                }
            }

            // Sort classes alphabetically
            availableClasses = availableClasses.OrderBy(t => t.FullName).ToList();
        }

        /// <summary>
        /// Generates command classes for the specified class.
        /// </summary>
        /// <param name="type">The class type to generate commands for.</param>
        private void GenerateCommandsForClass(Type type)
        {
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            // Get eligible methods based on filters:
            // - Public methods
            // - Static or Instance methods
            // - Any return type (including void)
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                              .Where(m => m.IsPublic);

            int commandCount = 0;

            foreach (var method in methods)
            {
                // Additional filtering can be applied here if needed
                // For example, skip property getters/setters, constructors, etc.
                if (method.IsSpecialName)
                    continue;

                GenerateCommandClass(type, method);
                commandCount++;
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Command Generation Complete", $"Generated {commandCount} command(s) for class {type.FullName}.", "OK");
            Debug.Log($"CommandGenerator: Generated {commandCount} command(s) for class {type.FullName}.");
        }

        /// <summary>
        /// Generates a command class for a specific method of a class.
        /// </summary>
        /// <param name="type">The class type containing the method.</param>
        /// <param name="method">The method to create a command for.</param>
        private void GenerateCommandClass(Type type, MethodInfo method)
        {
            string namespaceName = type.Namespace ?? "DefaultNamespace";
            string commandNamespace = $"{namespaceName}.Commands";
            string className = $"{type.Name}_{method.Name}Command";
            string receiverType = type.FullName;
            bool hasReturnValue = method.ReturnType != typeof(void);
            bool isStatic = method.IsStatic;

            // Collect namespaces from parameters and return type
            HashSet<string> namespaces = new HashSet<string>();
            foreach (var param in method.GetParameters())
            {
                CollectNamespaces(param.ParameterType, namespaces);
            }

            if (hasReturnValue)
            {
                CollectNamespaces(method.ReturnType, namespaces);
            }

            // Remove the namespace of the class itself to avoid redundant using
            if (!string.IsNullOrEmpty(namespaceName))
            {
                namespaces.Remove(namespaceName);
            }

            // Add necessary namespaces for Unity and system types
            namespaces.Add("System");
            namespaces.Add("UnityEngine");
            namespaces.Add("LegendaryTools.Commander");

            // Start building the class
            StringBuilder sb = new StringBuilder();

            // Add using declarations
            foreach (var ns in namespaces.OrderBy(n => n))
            {
                sb.AppendLine($"using {ns};");
            }
            sb.AppendLine();

            sb.AppendLine($"namespace {commandNamespace}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// Command to execute the method {method.Name} of class {type.Name}.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    [Serializable]");
            sb.AppendLine($"    public class {className} : ICommand");
            sb.AppendLine("    {");

            // Serialize parameters
            var parameters = method.GetParameters();
            foreach (var param in parameters)
            {
                sb.AppendLine($"        public {GetTypeName(param.ParameterType)} {param.Name};");
            }

            if (hasReturnValue)
            {
                sb.AppendLine();
                sb.AppendLine($"        public {GetTypeName(method.ReturnType)} Result;");
            }

            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Executes the command on the given receiver.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"receiver\">The receiver that will execute the command.</param>");
            sb.AppendLine("        public void Execute(object receiver)");
            sb.AppendLine("        {");
            if (!isStatic)
            {
                sb.AppendLine($"            var typedReceiver = receiver as {type.Name};");
                sb.AppendLine("            if (typedReceiver == null)");
                sb.AppendLine("            {");
                sb.AppendLine($"                Debug.LogError(\"Receiver is not of type {type.Name}\");");
                sb.AppendLine("                return;");
                sb.AppendLine("            }");
            }

            string parameterList = string.Join(", ", parameters.Select(p => p.Name));

            if (hasReturnValue)
            {
                if (isStatic)
                {
                    sb.AppendLine($"            Result = {type.Name}.{method.Name}({parameterList});");
                }
                else
                {
                    sb.AppendLine($"            Result = typedReceiver.{method.Name}({parameterList});");
                }
            }
            else
            {
                if (isStatic)
                {
                    sb.AppendLine($"            {type.Name}.{method.Name}({parameterList});");
                }
                else
                {
                    sb.AppendLine($"            typedReceiver.{method.Name}({parameterList});");
                }
            }

            sb.AppendLine("        }");

            // Overloaded Execute method with specific receiver type
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Executes the command on the given receiver without casting.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        /// <param name=\"receiver\">The receiver of type {type.Name}.</param>");
            sb.AppendLine($"        public void Execute({type.Name} receiver)");
            sb.AppendLine("        {");

            string executeCall;
            if (hasReturnValue)
            {
                executeCall = isStatic
                    ? $"Result = {type.Name}.{method.Name}({parameterList});"
                    : $"Result = receiver.{method.Name}({parameterList});";
            }
            else
            {
                executeCall = isStatic
                    ? $"{type.Name}.{method.Name}({parameterList});"
                    : $"receiver.{method.Name}({parameterList});";
            }

            sb.AppendLine($"            {executeCall}");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            // Define the file path and write the generated class to a .cs file
            string filePath = Path.Combine(outputPath, $"{className}.cs");

            try
            {
                File.WriteAllText(filePath, sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogError($"CommandGenerator: Failed to write command class {className}.cs. Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Recursively collects namespaces from a given type, including generic type arguments and array element types.
        /// </summary>
        /// <param name="type">The type to collect namespaces from.</param>
        /// <param name="namespaces">The set of namespaces to add to.</param>
        private void CollectNamespaces(Type type, HashSet<string> namespaces)
        {
            if (type == null)
                return;

            if (type.IsGenericType)
            {
                // For generic types, add the generic type definition's namespace
                namespaces.Add(type.GetGenericTypeDefinition().Namespace);

                // Recursively collect namespaces from generic type arguments
                foreach (var arg in type.GetGenericArguments())
                {
                    CollectNamespaces(arg, namespaces);
                }
            }
            else if (type.IsArray)
            {
                // For array types, collect namespaces from the element type
                CollectNamespaces(type.GetElementType(), namespaces);
            }
            else
            {
                // For non-generic, non-array types, add the namespace
                if (!string.IsNullOrEmpty(type.Namespace))
                {
                    namespaces.Add(type.Namespace);
                }
            }
        }

        /// <summary>
        /// Returns the C# type name, handling generic types and arrays appropriately.
        /// </summary>
        /// <param name="type">The type to get the name for.</param>
        /// <returns>The C# type name as a string.</returns>
        private string GetTypeName(Type type)
        {
            if (type.IsGenericType)
            {
                var genericType = type.GetGenericTypeDefinition();
                var genericArgs = type.GetGenericArguments();
                string typeName = genericType.Name;
                typeName = typeName.Substring(0, typeName.IndexOf('`'));
                string genericArgsNames = string.Join(", ", genericArgs.Select(t => GetTypeName(t)));
                return $"{typeName}<{genericArgsNames}>";
            }
            else if (type.IsArray)
            {
                return $"{GetTypeName(type.GetElementType())}[]";
            }
            else
            {
                // Use the type's name without namespace if possible
                return type.Name;
            }
        }
    }
}
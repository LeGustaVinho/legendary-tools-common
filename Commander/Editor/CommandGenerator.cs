// CommandGenerator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CommandPattern
{
    /// <summary>
    /// Editor script to generate command classes based on the methods of a selected class.
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
                    // Adjust selectedClassIndex if it exceeds the number of available classes after filtering
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

            // Button to Generate Commands
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
        /// Updates the list of available classes by scanning all loaded assemblies.
        /// </summary>
        private void RefreshAvailableClasses()
        {
            availableClasses.Clear();

            // Get all loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                // Ignore dynamic and system assemblies
                if (assembly.IsDynamic || assembly.FullName.StartsWith("System") || assembly.FullName.StartsWith("Unity"))
                    continue;

                foreach (var type in assembly.GetTypes())
                {
                    // Include only public and non-abstract classes
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
        /// <param name="type">The type of the class to generate commands for.</param>
        private void GenerateCommandsForClass(Type type)
        {
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            // Get eligible methods based on filters:
            // - Public methods
            // - Static or instance methods
            // - Any return type (including void)
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                              .Where(m => m.IsPublic);

            int commandCount = 0;

            foreach (var method in methods)
            {
                // Additional filtering can be applied here if necessary
                // For example, ignore property getters/setters, constructors, etc.
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
        /// <param name="type">The type of the class that contains the method.</param>
        /// <param name="method">The method to create a command for.</param>
        private void GenerateCommandClass(Type type, MethodInfo method)
        {
            string namespaceName = type.Namespace ?? "DefaultNamespace";
            string commandNamespace = $"{namespaceName}.Commands";
            string className = $"{type.Name}_{method.Name}Command";
            string receiverType = type.FullName;
            bool hasReturnValue = method.ReturnType != typeof(void);
            bool isStatic = method.IsStatic;

            // Handle generic methods by adding generic arguments to the class name
            if (method.IsGenericMethod)
            {
                var genericArgs = method.GetGenericArguments();
                string genericArgsNames = string.Join("_", genericArgs.Select(t => t.Name));
                className += $"_{genericArgsNames}";
            }

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

            // Remove the class's own namespace to avoid redundant 'using' statements
            if (!string.IsNullOrEmpty(namespaceName))
            {
                namespaces.Remove(namespaceName);
            }

            // Add necessary namespaces for system and Unity types
            namespaces.Add("System");
            namespaces.Add("UnityEngine");
            namespaces.Add("LegendaryTools.Commander");

            // Handle generic type constraints
            string genericConstraints = "";
            if (method.IsGenericMethod)
            {
                var genericArgs = method.GetGenericArguments();
                List<string> constraintsList = new List<string>();
                foreach (var arg in genericArgs)
                {
                    var constraints = new List<string>();
                    if (arg.GenericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
                        constraints.Add("class");
                    if (arg.GenericParameterAttributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
                        constraints.Add("struct");
                    var constraintTypes = arg.GetGenericParameterConstraints();
                    foreach (var ct in constraintTypes)
                    {
                        constraints.Add(ct.Name);
                    }
                    if (constraints.Count > 0)
                    {
                        constraintsList.Add($"where {arg.Name} : {string.Join(", ", constraints)}");
                    }
                }
                if (constraintsList.Count > 0)
                {
                    genericConstraints = " " + string.Join(" ", constraintsList);
                }
            }

            // Start building the class
            StringBuilder sb = new StringBuilder();

            // Add 'using' statements
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
            sb.AppendLine($"    public class {className}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")} : ICommand{(IsAsyncMethod(method) ? ", IAsyncCommand" : "")}");
            sb.AppendLine($"    {genericConstraints}");
            sb.AppendLine("    {");

            // Serialize parameters
            var parameters = method.GetParameters();
            foreach (var param in parameters)
            {
                string paramTypeName = GetTypeName(param.ParameterType);
                string paramName = param.Name;
                if (param.IsOut)
                {
                    sb.AppendLine($"        public out {paramTypeName} {paramName};");
                }
                else if (param.ParameterType.IsByRef)
                {
                    sb.AppendLine($"        public ref {paramTypeName} {paramName};");
                }
                else
                {
                    sb.AppendLine($"        public {paramTypeName} {paramName};");
                }
            }

            if (hasReturnValue)
            {
                string returnTypeName = GetTypeName(method.ReturnType);
                sb.AppendLine();
                sb.AppendLine($"        public {returnTypeName} Result;");
            }

            sb.AppendLine();

            // Execute(object receiver) method
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

            string parameterList = string.Join(", ", parameters.Select(p =>
            {
                if (p.IsOut)
                    return $"out {p.Name}";
                else if (p.ParameterType.IsByRef)
                    return $"ref {p.Name}";
                else
                    return p.Name;
            }));

            if (hasReturnValue)
            {
                if (isStatic)
                {
                    sb.AppendLine($"            Result = {type.Name}.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});");
                }
                else
                {
                    sb.AppendLine($"            Result = typedReceiver.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});");
                }
            }
            else
            {
                if (isStatic)
                {
                    sb.AppendLine($"            {type.Name}.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});");
                }
                else
                {
                    sb.AppendLine($"            typedReceiver.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});");
                }
            }

            sb.AppendLine("        }");

            // Execute(T receiver) method
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
                    ? $"Result = {type.Name}.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});"
                    : $"Result = receiver.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});";
            }
            else
            {
                executeCall = isStatic
                    ? $"{type.Name}.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});"
                    : $"receiver.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});";
            }

            sb.AppendLine($"            {executeCall}");
            sb.AppendLine("        }");

            // Asynchronous methods, if applicable
            if (IsAsyncMethod(method))
            {
                sb.AppendLine();
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// Asynchronously executes the command on the given receiver.");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine("        /// <param name=\"receiver\">The receiver that will execute the command.</param>");
                sb.AppendLine("        /// <returns>A Task representing the asynchronous operation.</returns>");
                sb.AppendLine("        public async Task ExecuteAsync(object receiver)");
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

                if (hasReturnValue)
                {
                    if (isStatic)
                    {
                        sb.AppendLine($"            Result = await {type.Name}.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});");
                    }
                    else
                    {
                        sb.AppendLine($"            Result = await typedReceiver.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});");
                    }
                }
                else
                {
                    if (isStatic)
                    {
                        sb.AppendLine($"            await {type.Name}.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});");
                    }
                    else
                    {
                        sb.AppendLine($"            await typedReceiver.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});");
                    }
                }

                sb.AppendLine("        }");

                // ExecuteAsync(T receiver) method
                sb.AppendLine();
                sb.AppendLine("        /// <summary>");
                sb.AppendLine($"        /// Asynchronously executes the command on the given receiver without casting.");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine($"        /// <param name=\"receiver\">The receiver of type {type.Name}.</param>");
                sb.AppendLine($"        /// <returns>A Task representing the asynchronous operation.</returns>");
                sb.AppendLine($"        public async Task ExecuteAsync({type.Name} receiver)");
                sb.AppendLine("        {");

                string asyncExecuteCall;
                if (hasReturnValue)
                {
                    asyncExecuteCall = isStatic
                        ? $"Result = await {type.Name}.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});"
                        : $"Result = await receiver.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});";
                }
                else
                {
                    asyncExecuteCall = isStatic
                        ? $"await {type.Name}.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});"
                        : $"await receiver.{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">" : "")}({parameterList});";
                }

                sb.AppendLine($"            {asyncExecuteCall}");
                sb.AppendLine("        }");
            }

            // Unexecute(object receiver) method
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Undoes the command on the given receiver.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"receiver\">The receiver that will undo the command.</param>");
            sb.AppendLine("        public void Unexecute(object receiver)");
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

            // Placeholder for undo logic
            sb.AppendLine("            // TODO: Implement undo logic here");
            sb.AppendLine("            Debug.Log(\"Undo operation not implemented.\");");
            sb.AppendLine("        }");

            // Unexecute(T receiver) method
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Undoes the command on the given receiver without casting.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        /// <param name=\"receiver\">The receiver of type {type.Name}.</param>");
            sb.AppendLine($"        public void Unexecute({type.Name} receiver)");
            sb.AppendLine("        {");
            sb.AppendLine("            // TODO: Implement undo logic here");
            sb.AppendLine("            Debug.Log(\"Undo operation not implemented.\");");
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
                // For generic types, add the namespace of the generic type definition
                namespaces.Add(type.GetGenericTypeDefinition().Namespace);

                // Recursively collect namespaces from the generic type arguments
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
                // For non-generic and non-array types, add the namespace directly
                if (!string.IsNullOrEmpty(type.Namespace))
                {
                    namespaces.Add(type.Namespace);
                }
            }
        }

        /// <summary>
        /// Determines if a method is asynchronous.
        /// </summary>
        /// <param name="method">The method to check.</param>
        /// <returns>True if the method is asynchronous; otherwise, false.</returns>
        private bool IsAsyncMethod(MethodInfo method)
        {
            // A method is considered asynchronous if it returns Task or Task<T>
            if (method.ReturnType == typeof(Task) || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)))
                return true;
            return false;
        }

        /// <summary>
        /// Returns the C# type name, properly handling generic types and arrays.
        /// </summary>
        /// <param name="type">The type to get the name of.</param>
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
                // Use the type name without namespace, if possible
                return type.Name;
            }
        }
    }
}
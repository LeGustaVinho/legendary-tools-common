using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Editor window for importing CSV spreadsheets into ScriptableObject assets or collections.
    /// </summary>
    public class SpreadsheetImporterWindow : EditorWindow
    {
        #region OAuth2 Fields

        private string clientId = "";
        private string clientSecret = "";
        private string authCode = "";
        private string accessToken = "";
        private string refreshToken = "";
        private DateTime tokenExpiry = DateTime.MinValue;

        #endregion

        #region CSV Import Fields

        private string csvPathOrUrl = "";
        private List<string[]> csvData = null;

        private string[] csvHeaders = null;

        // Output folder defined via the UI
        private string outputFolderPath = "";

        #endregion

        #region Individual Asset Import Fields

        private string typeName = "";
        private Type scriptableObjectType = null;
        private readonly List<FieldMapping> fieldMappings = new();

        // New variable to map the asset name column
        private int assetNameColumnIndex = -1;

        // ImportMode – using the enum defined in the configuration
        private ImportMode importerMode = ImportMode.IndividualAssets;

        #endregion

        #region Collection Import Fields

        private ScriptableObject collectionTarget;
        private MemberInfo[] collectionMembers;
        private int selectedCollectionMemberIndex = -1;
        private Type elementType = null; // Type of the collection elements
        private List<FieldMapping> collectionFieldMappings = new();

        #endregion

        #region Notification and UI Fields

        private string notificationMessage = "";
        private NotificationType notificationType = NotificationType.None;
        private Vector2 scrollPos;

        /// <summary>
        /// Enum for notification types used in the UI.
        /// </summary>
        private enum NotificationType
        {
            None,
            Info,
            Warning,
            Error
        }

        /// <summary>
        /// Enum for the main tab selection in the window.
        /// </summary>
        private enum MainTab
        {
            OAuth,
            Import
        }

        private MainTab currentTab = MainTab.Import;

        // Configuration template reference
        private SpreadsheetImportConfiguration configTemplate;

        #endregion

        #region Menu Item and OnGUI

        /// <summary>
        /// Adds a menu item to show the Spreadsheet Importer window.
        /// </summary>
        [MenuItem("Tools/LegendaryTools/Spreadsheet Importer")]
        public static void ShowWindow()
        {
            GetWindow<SpreadsheetImporterWindow>("Spreadsheet Importer");
        }

        /// <summary>
        /// Implements the OnGUI callback to render the editor window UI.
        /// </summary>
        private void OnGUI()
        {
            GUILayout.Space(10);

            // --- Configuration Template Section ---
            GUILayout.BeginVertical("box");
            GUILayout.Label("Configuration Template", EditorStyles.boldLabel);

            configTemplate =
                EditorGUILayout.ObjectField("Config Template", configTemplate, typeof(SpreadsheetImportConfiguration),
                    false) as SpreadsheetImportConfiguration;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load Configuration"))
            {
                if (configTemplate != null)
                {
                    // Populate importer variables with data from the template
                    csvPathOrUrl = configTemplate.csvPathOrUrl;
                    outputFolderPath = configTemplate.outputFolderPath;
                    importerMode = configTemplate.importMode;
                    typeName = configTemplate.scriptableObjectTypeName;
                    scriptableObjectType = GetTypeByName(typeName);

                    if (importerMode == ImportMode.IndividualAssets)
                    {
                        assetNameColumnIndex = configTemplate.assetNameColumnIndex;
                        // Apply saved mappings for individual assets
                        foreach (FieldMapping mapping in fieldMappings)
                        {
                            FieldMappingConfig cfg = configTemplate.fieldMappings.Find(x =>
                                x.fieldName.Equals(mapping.field.Name, StringComparison.OrdinalIgnoreCase));
                            if (cfg != null)
                                mapping.selectedColumnIndex = cfg.csvColumnIndex;
                        }
                    }
                    else // Collections mode: load the target instance
                    {
                        collectionTarget = configTemplate.collectionTarget;
                    }

                    Repaint();
                }
                else
                {
                    EditorUtility.DisplayDialog("Configuration Not Found", "Please assign a configuration template.",
                        "OK");
                }
            }

            if (GUILayout.Button("Save Configuration"))
            {
                if (configTemplate == null)
                {
                    string path = EditorUtility.SaveFilePanelInProject("Save Config Template",
                        "SpreadsheetImportConfiguration", "asset", "Enter a file name for the configuration");
                    if (!string.IsNullOrEmpty(path))
                    {
                        configTemplate = CreateInstance<SpreadsheetImportConfiguration>();
                        AssetDatabase.CreateAsset(configTemplate, path);
                    }
                }

                if (configTemplate != null)
                {
                    // Save current importer values to the configuration
                    configTemplate.csvPathOrUrl = csvPathOrUrl;
                    configTemplate.outputFolderPath = outputFolderPath;
                    configTemplate.importMode = importerMode;
                    configTemplate.scriptableObjectTypeName = typeName;
                    configTemplate.assetNameColumnIndex = assetNameColumnIndex;
                    // Save mappings for individual assets
                    configTemplate.fieldMappings.Clear();
                    foreach (FieldMapping mapping in fieldMappings)
                    {
                        FieldMappingConfig fmConfig = new();
                        fmConfig.fieldName = mapping.field.Name;
                        fmConfig.csvColumnIndex = mapping.selectedColumnIndex;
                        configTemplate.fieldMappings.Add(fmConfig);
                    }

                    // Save mappings for collection
                    configTemplate.collectionFieldMappings.Clear();
                    foreach (FieldMapping mapping in collectionFieldMappings)
                    {
                        FieldMappingConfig fmConfig = new();
                        fmConfig.fieldName = mapping.field.Name;
                        fmConfig.csvColumnIndex = mapping.selectedColumnIndex;
                        configTemplate.collectionFieldMappings.Add(fmConfig);
                    }

                    // Save target instance for Collections mode
                    if (importerMode == ImportMode.CollectionsInAsset)
                        configTemplate.collectionTarget = collectionTarget;

                    EditorUtility.SetDirty(configTemplate);
                    AssetDatabase.SaveAssets();
                    EditorUtility.DisplayDialog("Configuration Saved", "Configuration template saved successfully.",
                        "OK");
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(10);

            // Main tab toolbar for switching between OAuth and Import UI
            currentTab = (MainTab)GUILayout.Toolbar((int)currentTab, new string[] { "OAuth", "Import" });
            GUILayout.Space(10);

            switch (currentTab)
            {
                case MainTab.OAuth:
                    GUILayout.BeginVertical("box");
                    DrawOAuthSection();
                    GUILayout.EndVertical();
                    break;
                case MainTab.Import:
                    GUILayout.BeginVertical("box");
                    GUILayout.Label("Import Mode", EditorStyles.boldLabel);
                    // Update the importerMode based on the selection
                    int modeIndex = importerMode == ImportMode.IndividualAssets ? 0 : 1;
                    int newModeIndex = GUILayout.Toolbar(modeIndex,
                        new string[] { "ScriptableObject Instances", "ScriptableObject Collections" });
                    importerMode = newModeIndex == 0 ? ImportMode.IndividualAssets : ImportMode.CollectionsInAsset;
                    GUILayout.Space(10);

                    if (importerMode == ImportMode.IndividualAssets)
                        DrawMode1Content();
                    else
                        DrawMode2Content();

                    GUILayout.EndVertical();
                    break;
            }
        }

        #endregion

        #region OAuth Section (Common to Both Modes)

        /// <summary>
        /// Draws the OAuth2 section for Google authentication.
        /// </summary>
        private void DrawOAuthSection()
        {
            GUILayout.Label("OAuth2: Google Authentication", EditorStyles.boldLabel);
            clientId = EditorGUILayout.TextField("Client ID", clientId);
            clientSecret = EditorGUILayout.TextField("Client Secret", clientSecret);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Start Authentication"))
            {
                notificationMessage = "";
                StartOAuthFlow();
            }

            if (GUILayout.Button("Instruction How To Setup OAuth"))
                GoogleOAuthInstructionsWindow.ShowInstructions();

            GUILayout.EndHorizontal();

            GUILayout.Label("After authorizing, copy the generated code and paste below:");
            authCode = EditorGUILayout.TextField("Authorization Code", authCode);

            if (GUILayout.Button("Exchange Code for Token"))
            {
                notificationMessage = "";
                EditorCoroutineUtility.StartCoroutine(ExchangeAuthCodeForToken(), this);
            }

            if (!string.IsNullOrEmpty(accessToken))
            {
                EditorGUILayout.LabelField("Access Token:", accessToken);
                EditorGUILayout.LabelField("Refresh Token:", refreshToken);
                EditorGUILayout.LabelField("Expires at:", tokenExpiry.ToLocalTime().ToString());
                if (GUILayout.Button("Refresh Token"))
                {
                    notificationMessage = "";
                    EditorCoroutineUtility.StartCoroutine(RefreshAccessToken(), this);
                }
            }
        }

        /// <summary>
        /// Starts the OAuth2 authentication flow by opening the Google authorization URL.
        /// </summary>
        private void StartOAuthFlow()
        {
            string authUrl = "https://accounts.google.com/o/oauth2/v2/auth" +
                             "?client_id=" + UnityWebRequest.EscapeURL(clientId) +
                             "&redirect_uri=" + UnityWebRequest.EscapeURL("urn:ietf:wg:oauth:2.0:oob") +
                             "&response_type=code" +
                             "&scope=" + UnityWebRequest.EscapeURL(
                                 "https://www.googleapis.com/auth/spreadsheets.readonly");
            Application.OpenURL(authUrl);
            notificationMessage = "Browser opened for authentication. After authorizing, copy the generated code.";
            notificationType = NotificationType.Info;
        }

        /// <summary>
        /// Exchanges the authorization code for access and refresh tokens.
        /// </summary>
        private IEnumerator ExchangeAuthCodeForToken()
        {
            if (string.IsNullOrEmpty(authCode) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                notificationMessage = "Please fill in the authorization code, Client ID, and Client Secret.";
                notificationType = NotificationType.Error;
                Debug.LogError(notificationMessage);
                yield break;
            }

            string tokenUrl = "https://oauth2.googleapis.com/token";
            WWWForm form = new();
            form.AddField("code", authCode);
            form.AddField("client_id", clientId);
            form.AddField("client_secret", clientSecret);
            form.AddField("redirect_uri", "urn:ietf:wg:oauth:2.0:oob");
            form.AddField("grant_type", "authorization_code");

            UnityWebRequest www = UnityWebRequest.Post(tokenUrl, form);
            yield return www.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (www.result == UnityWebRequest.Result.ConnectionError ||
                www.result == UnityWebRequest.Result.ProtocolError)
#else
        if (www.isNetworkError || www.isHttpError)
#endif
            {
                notificationMessage = "Error obtaining token: " + www.error;
                notificationType = NotificationType.Error;
                Debug.LogError(notificationMessage);
            }
            else
            {
                OAuthTokenResponse tokenResponse = JsonUtility.FromJson<OAuthTokenResponse>(www.downloadHandler.text);
                if (!string.IsNullOrEmpty(tokenResponse.access_token))
                {
                    accessToken = tokenResponse.access_token;
                    refreshToken = tokenResponse.refresh_token;
                    tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
                    notificationMessage = "Token obtained successfully!";
                    notificationType = NotificationType.Info;
                    Debug.Log(notificationMessage);
                }
                else
                {
                    notificationMessage = "Failed to parse token response.";
                    notificationType = NotificationType.Error;
                    Debug.LogError(notificationMessage);
                }
            }
        }

        /// <summary>
        /// Refreshes the access token using the refresh token.
        /// </summary>
        private IEnumerator RefreshAccessToken()
        {
            if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(clientId) ||
                string.IsNullOrEmpty(clientSecret))
            {
                notificationMessage = "Refresh Token, Client ID, or Client Secret is missing.";
                notificationType = NotificationType.Error;
                Debug.LogError(notificationMessage);
                yield break;
            }

            string tokenUrl = "https://oauth2.googleapis.com/token";
            WWWForm form = new();
            form.AddField("refresh_token", refreshToken);
            form.AddField("client_id", clientId);
            form.AddField("client_secret", clientSecret);
            form.AddField("grant_type", "refresh_token");

            UnityWebRequest www = UnityWebRequest.Post(tokenUrl, form);
            yield return www.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (www.result == UnityWebRequest.Result.ConnectionError ||
                www.result == UnityWebRequest.Result.ProtocolError)
#else
        if (www.isNetworkError || www.isHttpError)
#endif
            {
                notificationMessage = "Error refreshing token: " + www.error;
                notificationType = NotificationType.Error;
                Debug.LogError(notificationMessage);
            }
            else
            {
                OAuthTokenResponse tokenResponse = JsonUtility.FromJson<OAuthTokenResponse>(www.downloadHandler.text);
                if (!string.IsNullOrEmpty(tokenResponse.access_token))
                {
                    accessToken = tokenResponse.access_token;
                    tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
                    notificationMessage = "Token refreshed successfully!";
                    notificationType = NotificationType.Info;
                    Debug.Log(notificationMessage);
                }
                else
                {
                    notificationMessage = "Failed to parse token refresh response.";
                    notificationType = NotificationType.Error;
                    Debug.LogError(notificationMessage);
                }
            }
        }

        /// <summary>
        /// Data structure for deserializing OAuth token responses.
        /// </summary>
        [Serializable]
        private class OAuthTokenResponse
        {
            public string access_token;
            public string refresh_token;
            public int expires_in;
            public string token_type;
            public string scope;
        }

        #endregion

        #region Individual Asset Import

        /// <summary>
        /// Draws the UI for Individual Asset Import: Import CSV data as individual assets.
        /// </summary>
        private void DrawMode1Content()
        {
            GUILayout.Label("CSV Import Settings", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal("box");
            csvPathOrUrl = EditorGUILayout.TextField("CSV Path/URL", csvPathOrUrl);
            if (GUILayout.Button("Browse", GUILayout.Width(75)))
            {
                string selectedFile = EditorUtility.OpenFilePanel("Select CSV File", "", "csv");
                if (!string.IsNullOrEmpty(selectedFile))
                    csvPathOrUrl = selectedFile;
            }

            GUILayout.EndHorizontal();

            // --- Extra Output Folder field (for both modes) ---
            GUILayout.BeginHorizontal("box");
            GUILayout.Label("Output Folder (relative to Assets)", GUILayout.Width(200));
            outputFolderPath = EditorGUILayout.TextField(outputFolderPath);
            if (GUILayout.Button("Browse", GUILayout.Width(75)))
            {
                string selected = EditorUtility.OpenFolderPanel("Select Output Folder", Application.dataPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    if (selected.StartsWith(Application.dataPath))
                        outputFolderPath = "Assets" + selected.Substring(Application.dataPath.Length);
                    else
                        EditorUtility.DisplayDialog("Error", "Selected folder must be inside the Assets folder.", "OK");
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal("box");
            typeName = EditorGUILayout.TextField("ScriptableObject Type", typeName);
            if (GUILayout.Button("Load Spreadsheet", GUILayout.Width(200)))
            {
                notificationMessage = "";
                LoadCSVData();
            }

            GUILayout.EndHorizontal();

            if (csvHeaders != null)
            {
                GUILayout.Space(10);
                GUILayout.Label("Asset Name Mapping", EditorStyles.boldLabel);
                List<string> assetNameOptions = new() { "Default Name" };
                assetNameOptions.AddRange(csvHeaders);
                int newAssetNameIndex = EditorGUILayout.Popup(assetNameColumnIndex + 1, assetNameOptions.ToArray()) - 1;
                assetNameColumnIndex = newAssetNameIndex;

                GUILayout.Space(10);
                GUILayout.Label("Mapping: Columns -> Fields", EditorStyles.boldLabel);
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
                foreach (FieldMapping mapping in fieldMappings)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(mapping.field.Name, GUILayout.Width(150));
                    List<string> options = new() { "Do not map" };
                    options.AddRange(csvHeaders);
                    int newIndex = EditorGUILayout.Popup(mapping.selectedColumnIndex + 1, options.ToArray()) - 1;
                    mapping.selectedColumnIndex = newIndex;
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }

            if (csvData != null && csvData.Count > 1 && scriptableObjectType != null)
                // Use the outputFolderPath defined in the Inspector (does not open folder selection dialog)
                if (GUILayout.Button("Import"))
                {
                    notificationMessage = "";
                    ImportData();
                }
        }

        /// <summary>
        /// Retrieves a ScriptableObject type by name from all loaded assemblies.
        /// </summary>
        /// <param name="typeName">Name of the type.</param>
        /// <returns>Type if found; otherwise, null.</returns>
        private Type GetTypeByName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                Type t = assembly.GetType(typeName, false, true);
                if (t != null && t.IsSubclassOf(typeof(ScriptableObject)))
                    return t;
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) &&
                        type.IsSubclassOf(typeof(ScriptableObject)))
                        return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Imports CSV data as individual assets into the output folder defined in the Inspector.
        /// </summary>
        private void ImportData()
        {
            if (string.IsNullOrEmpty(outputFolderPath))
            {
                EditorUtility.DisplayDialog("Error", "Output Folder not defined. Please set it in the Inspector.",
                    "OK");
                return;
            }

            if (!outputFolderPath.StartsWith("Assets"))
            {
                EditorUtility.DisplayDialog("Error", "The output folder must be inside the Assets folder.", "OK");
                return;
            }

            string folderPath = outputFolderPath;
            int errorCount = 0;
            int importedCount = 0;
            for (int i = 1; i < csvData.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Importing CSV", "Importing data...",
                    (float)(i - 1) / (csvData.Count - 1));
                string[] row = csvData[i];
                ScriptableObject asset = CreateInstance(scriptableObjectType);
                bool rowError = false;
                foreach (FieldMapping mapping in fieldMappings)
                {
                    if (mapping.selectedColumnIndex >= 0 && mapping.selectedColumnIndex < row.Length)
                    {
                        string valueStr = row[mapping.selectedColumnIndex];
                        try
                        {
                            object value = ConvertValue(valueStr, mapping.field.FieldType);
                            mapping.field.SetValue(asset, value);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(
                                $"Error converting field '{mapping.field.Name}' on row {i + 1}: {ex.Message}");
                            rowError = true;
                        }
                    }
                }

                if (rowError)
                    errorCount++;
                string assetName;
                if (assetNameColumnIndex >= 0 && assetNameColumnIndex < row.Length)
                {
                    assetName = SanitizeAssetName(row[assetNameColumnIndex]);
                    if (string.IsNullOrEmpty(assetName))
                        assetName = $"{scriptableObjectType.Name}_{i}";
                }
                else
                {
                    assetName = $"{scriptableObjectType.Name}_{i}";
                }

                string assetPath = $"{folderPath}/{assetName}.asset";
                try
                {
                    AssetDatabase.CreateAsset(asset, assetPath);
                    importedCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error creating asset on row {i + 1}: {ex.Message}");
                    errorCount++;
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            string resultMsg = $"Import completed: {importedCount} assets created. {errorCount} error(s) encountered.";
            notificationMessage = resultMsg;
            notificationType = errorCount > 0 ? NotificationType.Warning : NotificationType.Info;
            Debug.Log(resultMsg);
            EditorUtility.DisplayDialog("Import Completed", resultMsg, "OK");
        }

        #endregion

        #region Import into Collection

        /// <summary>
        /// Draws the UI for importing CSV data into a collection field.
        /// </summary>
        private void DrawMode2Content()
        {
            GUILayout.Label("Mode 2: Import into a Collection", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal("box");
            csvPathOrUrl = EditorGUILayout.TextField("CSV Path/URL", csvPathOrUrl);
            if (GUILayout.Button("Browse", GUILayout.Width(75)))
            {
                string selectedFile = EditorUtility.OpenFilePanel("Select CSV File", "", "csv");
                if (!string.IsNullOrEmpty(selectedFile))
                    csvPathOrUrl = selectedFile;
            }

            GUILayout.EndHorizontal();

            // --- Extra Output Folder field (for both modes) ---
            GUILayout.BeginHorizontal("box");
            GUILayout.Label("Output Folder (relative to Assets)", GUILayout.Width(200));
            outputFolderPath = EditorGUILayout.TextField(outputFolderPath);
            if (GUILayout.Button("Browse", GUILayout.Width(75)))
            {
                string selected = EditorUtility.OpenFolderPanel("Select Output Folder", Application.dataPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    if (selected.StartsWith(Application.dataPath))
                        outputFolderPath = "Assets" + selected.Substring(Application.dataPath.Length);
                    else
                        EditorUtility.DisplayDialog("Error", "Selected folder must be inside the Assets folder.", "OK");
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal("box");
            collectionTarget =
                EditorGUILayout.ObjectField("Target Object", collectionTarget, typeof(ScriptableObject), false) as
                    ScriptableObject;
            if (collectionTarget != null)
            {
                collectionMembers = GetCollectionMembers(collectionTarget);
                if (collectionMembers != null && collectionMembers.Length > 0)
                {
                    List<string> memberNames = new();
                    foreach (MemberInfo member in collectionMembers)
                    {
                        memberNames.Add(member.Name + " (" + GetMemberType(member).Name + ")");
                    }

                    selectedCollectionMemberIndex = EditorGUILayout.Popup("Collection Field",
                        selectedCollectionMemberIndex,
                        memberNames.ToArray());
                    if (selectedCollectionMemberIndex >= 0 && selectedCollectionMemberIndex < collectionMembers.Length)
                    {
                        Type colType = GetMemberType(collectionMembers[selectedCollectionMemberIndex]);
                        if (colType.IsArray)
                            elementType = colType.GetElementType();
                        else if (colType.IsGenericType && colType.GetGenericTypeDefinition() == typeof(List<>))
                            elementType = colType.GetGenericArguments()[0];
                        else
                            elementType = null;

                        if (elementType != null)
                        {
                            GUILayout.Label("Mapping: Columns -> Fields (" + elementType.Name + ")",
                                EditorStyles.boldLabel);
                            if (collectionFieldMappings == null || collectionFieldMappings.Count == 0 ||
                                (collectionFieldMappings.Count > 0 &&
                                 collectionFieldMappings[0].field.DeclaringType != elementType))
                                SetupCollectionFieldMappings(elementType);

                            // If a template is defined for Collections, apply saved mappings
                            if (configTemplate != null && importerMode == ImportMode.CollectionsInAsset)
                                foreach (FieldMapping mapping in collectionFieldMappings)
                                {
                                    FieldMappingConfig cfg = configTemplate.collectionFieldMappings.Find(x =>
                                        x.fieldName.Equals(mapping.field.Name, StringComparison.OrdinalIgnoreCase));
                                    if (cfg != null)
                                        mapping.selectedColumnIndex = cfg.csvColumnIndex;
                                }

                            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
                            foreach (FieldMapping mapping in collectionFieldMappings)
                            {
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField(mapping.field.Name, GUILayout.Width(150));
                                List<string> options = new() { "Do not map" };
                                if (csvHeaders != null)
                                    options.AddRange(csvHeaders);
                                int newIndex =
                                    EditorGUILayout.Popup(mapping.selectedColumnIndex + 1, options.ToArray()) -
                                    1;
                                mapping.selectedColumnIndex = newIndex;
                                EditorGUILayout.EndHorizontal();
                            }

                            EditorGUILayout.EndScrollView();
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("The selected field is not an Array or List.", MessageType.Error);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Select a collection field.", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("No collection fields (Array or List) found in the target object.",
                        MessageType.Warning);
                }
            }

            if (GUILayout.Button("Load Spreadsheet", GUILayout.Width(200)))
            {
                notificationMessage = "";
                LoadCSVData();
            }

            GUILayout.EndHorizontal();

            if (csvData != null && csvData.Count > 1 && collectionTarget != null && elementType != null)
                if (GUILayout.Button("Import into Collection"))
                {
                    notificationMessage = "";
                    ImportDataToCollection();
                }
        }

        /// <summary>
        /// Retrieves collection fields (arrays or lists) from the target ScriptableObject.
        /// </summary>
        /// <param name="target">The ScriptableObject target.</param>
        /// <returns>An array of MemberInfo representing the collection fields.</returns>
        private MemberInfo[] GetCollectionMembers(ScriptableObject target)
        {
            List<MemberInfo> members = new();
            Type targetType = target.GetType();
            FieldInfo[] fields =
                targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                Type t = field.FieldType;
                if (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)))
                    members.Add(field);
            }

            PropertyInfo[] properties =
                targetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (PropertyInfo prop in properties)
            {
                Type t = prop.PropertyType;
                if ((t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))) &&
                    prop.CanRead && prop.CanWrite)
                    members.Add(prop);
            }

            return members.ToArray();
        }

        /// <summary>
        /// Gets the type of a member (field or property).
        /// </summary>
        /// <param name="member">The MemberInfo object.</param>
        /// <returns>The type of the member if recognized; otherwise, null.</returns>
        private Type GetMemberType(MemberInfo member)
        {
            if (member is FieldInfo)
                return ((FieldInfo)member).FieldType;
            else if (member is PropertyInfo)
                return ((PropertyInfo)member).PropertyType;
            return null;
        }

        /// <summary>
        /// Sets up the field mappings for collection elements by scanning the element type for serializable fields.
        /// </summary>
        /// <param name="elementType">The type of collection element.</param>
        private void SetupCollectionFieldMappings(Type elementType)
        {
            collectionFieldMappings.Clear();
            FieldInfo[] fields =
                elementType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                if (field.IsPublic || field.GetCustomAttribute(typeof(SerializeField)) != null)
                {
                    FieldMapping mapping = new();
                    mapping.field = field;
                    mapping.selectedColumnIndex = -1;
                    collectionFieldMappings.Add(mapping);
                }
            }
        }

        /// <summary>
        /// Imports CSV data and adds each row as an element in the target collection.
        /// </summary>
        private void ImportDataToCollection()
        {
            MemberInfo member = collectionMembers[selectedCollectionMemberIndex];
            object collectionValue = null;
            Type memberType = GetMemberType(member);
            if (member is FieldInfo)
                collectionValue = ((FieldInfo)member).GetValue(collectionTarget);
            else if (member is PropertyInfo)
                collectionValue = ((PropertyInfo)member).GetValue(collectionTarget, null);

            Type listType = typeof(List<>).MakeGenericType(elementType);
            IList tempList = Activator.CreateInstance(listType) as IList;

            if (collectionValue != null)
            {
                if (memberType.IsArray)
                {
                    Array existingArray = collectionValue as Array;
                    if (existingArray != null)
                        foreach (object item in existingArray)
                        {
                            tempList.Add(item);
                        }
                }
                else if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    IList existingList = collectionValue as IList;
                    if (existingList != null)
                        foreach (object item in existingList)
                        {
                            tempList.Add(item);
                        }
                }
            }

            int errorCount = 0;
            int importedCount = 0;
            for (int i = 1; i < csvData.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Importing CSV", "Importing data...",
                    (float)(i - 1) / (csvData.Count - 1));
                string[] row = csvData[i];
                object elementInstance = Activator.CreateInstance(elementType);
                bool rowError = false;
                foreach (FieldMapping mapping in collectionFieldMappings)
                {
                    if (mapping.selectedColumnIndex >= 0 && mapping.selectedColumnIndex < row.Length)
                    {
                        string valueStr = row[mapping.selectedColumnIndex];
                        try
                        {
                            object value = ConvertValue(valueStr, mapping.field.FieldType);
                            mapping.field.SetValue(elementInstance, value);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(
                                $"Error converting field '{mapping.field.Name}' on row {i + 1}: {ex.Message}");
                            rowError = true;
                        }
                    }
                }

                if (rowError)
                    errorCount++;
                tempList.Add(elementInstance);
                importedCount++;
            }

            EditorUtility.ClearProgressBar();
            if (memberType.IsArray)
            {
                Array newArray = Array.CreateInstance(elementType, tempList.Count);
                tempList.CopyTo(newArray, 0);
                if (member is FieldInfo)
                    ((FieldInfo)member).SetValue(collectionTarget, newArray);
                else if (member is PropertyInfo)
                    ((PropertyInfo)member).SetValue(collectionTarget, newArray, null);
            }
            else
            {
                if (member is FieldInfo)
                    ((FieldInfo)member).SetValue(collectionTarget, tempList);
                else if (member is PropertyInfo)
                    ((PropertyInfo)member).SetValue(collectionTarget, tempList, null);
            }

            EditorUtility.SetDirty(collectionTarget);
            string resultMsg =
                $"Import completed: {importedCount} items added to the collection. {errorCount} error(s) encountered.";
            notificationMessage = resultMsg;
            notificationType = errorCount > 0 ? NotificationType.Warning : NotificationType.Info;
            Debug.Log(resultMsg);
            EditorUtility.DisplayDialog("Import Completed", resultMsg, "OK");
        }

        #endregion

        #region Common Methods

        /// <summary>
        /// Loads CSV data either by downloading from a URL or reading from a file.
        /// </summary>
        private void LoadCSVData()
        {
            if (csvPathOrUrl.StartsWith("http"))
            {
                EditorCoroutineUtility.StartCoroutine(DownloadCSV(), this);
            }
            else
            {
                if (File.Exists(csvPathOrUrl))
                {
                    try
                    {
                        string content = File.ReadAllText(csvPathOrUrl);
                        csvData = ParseCSV(content);
                        scriptableObjectType = GetTypeByName(typeName);
                        if (csvData != null && csvData.Count > 0)
                        {
                            csvHeaders = csvData[0];
                            SetupFieldMappings();
                            // If a template is defined for the IndividualAssets mode, apply saved mappings
                            if (configTemplate != null && importerMode == ImportMode.IndividualAssets)
                            {
                                assetNameColumnIndex = configTemplate.assetNameColumnIndex;
                                foreach (FieldMapping mapping in fieldMappings)
                                {
                                    FieldMappingConfig cfg = configTemplate.fieldMappings.Find(x =>
                                        x.fieldName.Equals(mapping.field.Name, StringComparison.OrdinalIgnoreCase));
                                    if (cfg != null)
                                        mapping.selectedColumnIndex = cfg.csvColumnIndex;
                                }
                            }

                            notificationMessage = "CSV loaded successfully.";
                            notificationType = NotificationType.Info;
                        }
                    }
                    catch (Exception ex)
                    {
                        notificationMessage = "Error reading CSV file: " + ex.Message;
                        notificationType = NotificationType.Error;
                        Debug.LogError(notificationMessage);
                    }
                }
                else
                {
                    notificationMessage = "File not found: " + csvPathOrUrl;
                    notificationType = NotificationType.Error;
                    Debug.LogError(notificationMessage);
                }
            }
        }

        /// <summary>
        /// Downloads the CSV file from a remote URL.
        /// </summary>
        private IEnumerator DownloadCSV()
        {
            string downloadUrl = GetCsvDownloadUrl(csvPathOrUrl);
            UnityWebRequest www = UnityWebRequest.Get(downloadUrl);
            if (!string.IsNullOrEmpty(accessToken))
                www.SetRequestHeader("Authorization", "Bearer " + accessToken);
            UnityWebRequestAsyncOperation operation = www.SendWebRequest();
            while (!operation.isDone)
            {
                EditorUtility.DisplayProgressBar("Downloading CSV", "Downloading data...", www.downloadProgress);
                yield return null;
            }

            EditorUtility.ClearProgressBar();
#if UNITY_2020_1_OR_NEWER
            if (www.result == UnityWebRequest.Result.ConnectionError ||
                www.result == UnityWebRequest.Result.ProtocolError)
#else
        if (www.isNetworkError || www.isHttpError)
#endif
            {
                notificationMessage = "Error downloading CSV: " + www.error;
                notificationType = NotificationType.Error;
                Debug.LogError(notificationMessage);
            }
            else
            {
                string content = www.downloadHandler.text;
                csvData = ParseCSV(content);
                if (csvData != null && csvData.Count > 0)
                {
                    csvHeaders = csvData[0];
                    SetupFieldMappings();
                    if (configTemplate != null && importerMode == ImportMode.IndividualAssets)
                    {
                        assetNameColumnIndex = configTemplate.assetNameColumnIndex;
                        foreach (FieldMapping mapping in fieldMappings)
                        {
                            FieldMappingConfig cfg = configTemplate.fieldMappings.Find(x =>
                                x.fieldName.Equals(mapping.field.Name, StringComparison.OrdinalIgnoreCase));
                            if (cfg != null)
                                mapping.selectedColumnIndex = cfg.csvColumnIndex;
                        }
                    }

                    notificationMessage = "CSV downloaded and processed successfully.";
                    notificationType = NotificationType.Info;
                    Debug.Log(notificationMessage);
                }
            }
        }

        /// <summary>
        /// Converts a Google Sheets URL into a CSV export URL if necessary.
        /// </summary>
        /// <param name="url">The original URL.</param>
        /// <returns>The URL adjusted for CSV export if applicable.</returns>
        private string GetCsvDownloadUrl(string url)
        {
            if (url.Contains("docs.google.com/spreadsheets") && url.Contains("/edit"))
            {
                int editIndex = url.IndexOf("/edit");
                string baseUrl = url.Substring(0, editIndex);
                Uri uri;
                if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                {
                    string query = uri.Query;
                    string gidParam = "";
                    if (!string.IsNullOrEmpty(query))
                    {
                        string[] parameters = query.TrimStart('?').Split('&');
                        foreach (string param in parameters)
                        {
                            if (param.StartsWith("gid="))
                            {
                                gidParam = param;
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(gidParam))
                        return baseUrl + "/export?format=csv&" + gidParam;
                }

                return baseUrl + "/export?format=csv";
            }

            return url;
        }

        /// <summary>
        /// Parses CSV content into a list of string arrays.
        /// </summary>
        /// <param name="content">The full CSV content as a string.</param>
        /// <returns>A list where each element represents a row as a string array.</returns>
        private List<string[]> ParseCSV(string content)
        {
            List<string[]> data = new();
            try
            {
                using (StringReader reader = new(content))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            Debug.LogWarning("Empty line found during CSV parsing.");
                            continue;
                        }

                        string[] values = ParseCSVLine(line);
                        data.Add(values);
                    }
                }
            }
            catch (Exception ex)
            {
                notificationMessage = "Error parsing CSV: " + ex.Message;
                notificationType = NotificationType.Error;
                Debug.LogError(notificationMessage);
            }

            return data;
        }

        /// <summary>
        /// Parses a single line of CSV and returns the fields.
        /// </summary>
        /// <param name="line">The CSV line.</param>
        /// <returns>An array of field values.</returns>
        private string[] ParseCSVLine(string line)
        {
            List<string> fields = new();
            bool inQuotes = false;
            System.Text.StringBuilder field = new();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }

            fields.Add(field.ToString());
            return fields.ToArray();
        }

        /// <summary>
        /// Sets up field mappings for individual asset import based on the specified ScriptableObject type.
        /// </summary>
        private void SetupFieldMappings()
        {
            fieldMappings.Clear();
            if (scriptableObjectType == null)
                return;
            FieldInfo[] fields =
                scriptableObjectType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                if (field.IsPublic || field.GetCustomAttribute(typeof(SerializeField)) != null)
                {
                    FieldMapping mapping = new();
                    mapping.field = field;
                    mapping.selectedColumnIndex = -1;
                    fieldMappings.Add(mapping);
                }
            }
        }

        /// <summary>
        /// Converts a string value to the specified target type.
        /// </summary>
        /// <param name="valueStr">The input string.</param>
        /// <param name="targetType">The target conversion type.</param>
        /// <returns>The converted value.</returns>
        private object ConvertValue(string valueStr, Type targetType)
        {
            try
            {
                if (targetType == typeof(string))
                {
                    return valueStr;
                }
                else if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
                {
                    if (!string.IsNullOrEmpty(valueStr) && valueStr.StartsWith("Assets/"))
                    {
                        UnityEngine.Object assetAtPath = AssetDatabase.LoadAssetAtPath(valueStr, targetType);
                        if (assetAtPath != null) return assetAtPath;
                        Debug.LogWarning($"Asset not found at path: {valueStr}");
                        return null;
                    }

                    string assetPathFromGuid = AssetDatabase.GUIDToAssetPath(valueStr);
                    if (!string.IsNullOrEmpty(assetPathFromGuid))
                    {
                        UnityEngine.Object assetFromGuid = AssetDatabase.LoadAssetAtPath(assetPathFromGuid, targetType);
                        if (assetFromGuid != null) return assetFromGuid;
                        Debug.LogWarning($"Asset not found for GUID: {valueStr}");
                        return null;
                    }

                    return FindAssetByName(valueStr, targetType);
                }
                else if (targetType.IsEnum)
                {
                    return Enum.Parse(targetType, valueStr, true);
                }
                else if (targetType == typeof(int))
                {
                    if (int.TryParse(valueStr, out int intValue))
                        return intValue;
                    else
                        throw new Exception($"Value '{valueStr}' cannot be converted to int.");
                }
                else if (targetType == typeof(float))
                {
                    if (float.TryParse(valueStr, out float floatValue))
                        return floatValue;
                    else
                        throw new Exception($"Value '{valueStr}' cannot be converted to float.");
                }
                else if (targetType == typeof(bool))
                {
                    if (bool.TryParse(valueStr, out bool boolValue))
                        return boolValue;
                    else
                        throw new Exception($"Value '{valueStr}' cannot be converted to bool.");
                }
                else
                {
                    return Convert.ChangeType(valueStr, targetType);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error converting value '{valueStr}' to type {targetType}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Finds and returns an asset by its name and type.
        /// </summary>
        /// <param name="assetName">The name of the asset.</param>
        /// <param name="targetType">The type of the asset.</param>
        /// <returns>The asset if found; otherwise, null.</returns>
        private UnityEngine.Object FindAssetByName(string assetName, Type targetType)
        {
            if (string.IsNullOrEmpty(assetName))
                return null;
            string typeFilter = $"t:{targetType.Name}";
            string query = $"{assetName} {typeFilter}";
            string[] guids = AssetDatabase.FindAssets(query);
            if (guids.Length == 0)
            {
                Debug.LogWarning($"Asset not found for name '{assetName}' of type {targetType}");
                return null;
            }
            else if (guids.Length > 1)
            {
                List<UnityEngine.Object> matches = new();
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(assetPath, targetType);
                    if (asset != null && asset.name.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                        matches.Add(asset);
                }

                if (matches.Count > 1)
                {
                    Debug.LogWarning(
                        $"Multiple assets found for name '{assetName}' of type {targetType}. Using the first match.");
                    return matches[0];
                }
                else if (matches.Count == 1)
                {
                    return matches[0];
                }
                else
                {
                    Debug.LogWarning($"No asset found with exact name '{assetName}' for type {targetType}");
                    return null;
                }
            }
            else
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(assetPath, targetType);
                if (asset != null && !asset.name.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    Debug.LogWarning(
                        $"Asset found for type {targetType} does not exactly match the name '{assetName}'.");
                return asset;
            }
        }

        /// <summary>
        /// Sanitizes the asset name by removing invalid characters.
        /// </summary>
        /// <param name="name">The original name.</param>
        /// <returns>The sanitized asset name.</returns>
        private string SanitizeAssetName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c.ToString(), "");
            }

            return name;
        }

        /// <summary>
        /// Private class representing a mapping from a ScriptableObject field to a CSV column index.
        /// </summary>
        private class FieldMapping
        {
            public FieldInfo field;
            public int selectedColumnIndex;
        }

        #endregion
    }

    /// <summary>
    /// Window providing step-by-step instructions for obtaining the Client ID and Client Secret from Google.
    /// </summary>
    public class GoogleOAuthInstructionsWindow : EditorWindow
    {
        private Vector2 scrollPos;

        /// <summary>
        /// Opens the Google OAuth2 Instructions window.
        /// </summary>
        public static void ShowInstructions()
        {
            GetWindow<GoogleOAuthInstructionsWindow>("Google OAuth2 Instructions");
        }

        /// <summary>
        /// Implements the OnGUI callback to render the instructions.
        /// </summary>
        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            GUILayout.Label("Step-by-Step Instructions for Obtaining Client ID and Client Secret from Google",
                EditorStyles.boldLabel);
            GUILayout.Space(10);
            GUILayout.Label("1. Go to the Google Cloud Console (https://console.cloud.google.com/).",
                EditorStyles.wordWrappedLabel);
            GUILayout.Label("2. Sign in with your Google account.", EditorStyles.wordWrappedLabel);
            GUILayout.Label("3. Click 'Select a project' and then 'New Project'.", EditorStyles.wordWrappedLabel);
            GUILayout.Label("4. Enter the project name and click 'Create'.", EditorStyles.wordWrappedLabel);
            GUILayout.Label("5. Once the project is created, in the side menu, select 'APIs & Services' > 'Library'.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Label("6. Search for 'Google Sheets API' and enable the API.", EditorStyles.wordWrappedLabel);
            GUILayout.Label("7. Go back to 'APIs & Services' and click on 'Credentials'.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Label("8. Click on 'Create Credentials' and select 'OAuth client ID'.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Label(
                "9. Choose the application type 'Desktop app' or 'Other' (depending on the available option).",
                EditorStyles.wordWrappedLabel);
            GUILayout.Label("10. After creation, you will see the Client ID and Client Secret values.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Label(
                "11. Copy these values and paste them into the corresponding fields in the 'Spreadsheet Importer' window.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Label("12. Set the redirect URI to: urn:ietf:wg:oauth:2.0:oob", EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);
            GUILayout.Label(
                "These are the basic steps to register your application and obtain the required credentials.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
    }
}
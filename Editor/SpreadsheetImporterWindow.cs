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
    public class SpreadsheetImporterWindow : EditorWindow
    {
        // OAuth2 fields (used in both modes)
        private string clientId = "";
        private string clientSecret = "";
        private string authCode = "";
        private string accessToken = "";
        private string refreshToken = "";
        private DateTime tokenExpiry = DateTime.MinValue;

        // CSV import fields (used in both modes)
        private string csvPathOrUrl = "";
        private List<string[]> csvData = null;
        private string[] csvHeaders = null;

        // Mapping fields for Individual Asset Import
        private string typeName = "";
        private System.Type scriptableObjectType = null;
        private readonly List<FieldMapping> fieldMappings = new List<FieldMapping>();

        // Nova variável para mapear a coluna de nome do asset
        private int assetNameColumnIndex = -1;

        // Fields for Mode 2 (Import into Collection)
        private enum ImportMode
        {
            IndividualAssets, // Create individual assets
            CollectionsInAsset // Populate a collection (Array or List) in a ScriptableObject
        }

        private ImportMode currentMode = ImportMode.IndividualAssets;

        // Mode 2 specific fields
        private ScriptableObject collectionTarget;
        private MemberInfo[] collectionMembers;
        private int selectedCollectionMemberIndex = -1;
        private Type elementType = null; // Type of the collection elements
        private List<FieldMapping> collectionFieldMappings = new List<FieldMapping>();

        // Notification and progress fields
        private string notificationMessage = "";
        private NotificationType notificationType = NotificationType.None;
        private Vector2 scrollPos;

        private enum NotificationType
        {
            None,
            Info,
            Warning,
            Error
        }

        [MenuItem("Tools/Spreadsheet Importer")]
        public static void ShowWindow()
        {
            GetWindow<SpreadsheetImporterWindow>("Spreadsheet Importer");
        }

        private void OnGUI()
        {
            // Draw common OAuth section
            DrawOAuthSection();

            GUILayout.Space(10);
            GUILayout.Label("Import Mode", EditorStyles.boldLabel);
            currentMode = (ImportMode)GUILayout.Toolbar((int)currentMode,
                new string[] { "ScriptableObject Instances", "ScriptableObject Collections" });
            GUILayout.Space(10);

            if (currentMode == ImportMode.IndividualAssets)
                DrawMode1Content();
            else
                DrawMode2Content();
        }

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
            {
                GoogleOAuthInstructionsWindow.ShowInstructions();
            }

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
            WWWForm form = new WWWForm();
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
            WWWForm form = new WWWForm();
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
            csvPathOrUrl = EditorGUILayout.TextField("CSV Path/URL", csvPathOrUrl);
            if (GUILayout.Button("Select Local File"))
            {
                string selectedFile = EditorUtility.OpenFilePanel("Select CSV File", "", "csv");
                if (!string.IsNullOrEmpty(selectedFile))
                    csvPathOrUrl = selectedFile;
            }

            typeName = EditorGUILayout.TextField("ScriptableObject Type", typeName);

            if (GUILayout.Button("Load Spreadsheet"))
            {
                notificationMessage = "";
                scriptableObjectType = GetTypeByName(typeName);
                if (scriptableObjectType == null)
                {
                    notificationMessage = $"ScriptableObject type not found: {typeName}";
                    notificationType = NotificationType.Error;
                    Debug.LogError(notificationMessage);
                }
                else
                {
                    LoadCSVData();
                }
            }

            if (csvHeaders != null)
            {
                GUILayout.Space(10);
                // Nova UI para mapear a coluna que servirá como nome do asset
                GUILayout.Label("Asset Name Mapping", EditorStyles.boldLabel);
                List<string> assetNameOptions = new List<string>() { "Default Name" };
                assetNameOptions.AddRange(csvHeaders);
                int newAssetNameIndex = EditorGUILayout.Popup(assetNameColumnIndex + 1, assetNameOptions.ToArray()) - 1;
                assetNameColumnIndex = newAssetNameIndex;

                GUILayout.Space(10);
                GUILayout.Label("Mapping: Columns -> Fields", EditorStyles.boldLabel);
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
                foreach (var mapping in fieldMappings)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(mapping.field.Name, GUILayout.Width(150));
                    List<string> options = new List<string>() { "Do not map" };
                    options.AddRange(csvHeaders);
                    int newIndex = EditorGUILayout.Popup(mapping.selectedColumnIndex + 1, options.ToArray()) - 1;
                    mapping.selectedColumnIndex = newIndex;
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }

            if (csvData != null && csvData.Count > 1 && scriptableObjectType != null)
            {
                if (GUILayout.Button("Import"))
                {
                    notificationMessage = "";
                    ImportData();
                }
            }
        }

        /// <summary>
        /// Retrieves a ScriptableObject type by name from all loaded assemblies.
        /// </summary>
        private System.Type GetTypeByName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                var t = assembly.GetType(typeName);
                if (t != null && t.IsSubclassOf(typeof(ScriptableObject)))
                    return t;
            }

            return null;
        }

        /// <summary>
        /// Imports CSV data as individual assets into a user-selected folder.
        /// </summary>
        private void ImportData()
        {
            // Prompt the user to select the destination folder for the assets.
            string selectedFolder =
                EditorUtility.OpenFolderPanel("Select Destination Folder for Assets", Application.dataPath, "");
            if (string.IsNullOrEmpty(selectedFolder))
            {
                notificationMessage = "No folder selected for saving assets.";
                notificationType = NotificationType.Warning;
                return;
            }

            if (!selectedFolder.StartsWith(Application.dataPath))
            {
                notificationMessage = "The selected folder must be inside the 'Assets' folder.";
                notificationType = NotificationType.Error;
                return;
            }

            string folderPath = "Assets" + selectedFolder.Substring(Application.dataPath.Length);

            int errorCount = 0;
            int importedCount = 0;
            for (int i = 1; i < csvData.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Importing CSV", "Importing data...",
                    (float)(i - 1) / (csvData.Count - 1));
                string[] row = csvData[i];
                ScriptableObject asset = ScriptableObject.CreateInstance(scriptableObjectType);
                bool rowError = false;
                foreach (var mapping in fieldMappings)
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

                // Determina o nome do asset baseado na coluna mapeada, se configurada
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
        /// Draws the UI for Collection field: Import CSV data into a collection field.
        /// </summary>
        private void DrawMode2Content()
        {
            GUILayout.Label("Mode 2: Import into a Collection", EditorStyles.boldLabel);

            // CSV configuration (same as Mode 1)
            csvPathOrUrl = EditorGUILayout.TextField("CSV Path/URL", csvPathOrUrl);
            if (GUILayout.Button("Select Local File"))
            {
                string selectedFile = EditorUtility.OpenFilePanel("Select CSV File", "", "csv");
                if (!string.IsNullOrEmpty(selectedFile))
                    csvPathOrUrl = selectedFile;
            }

            if (GUILayout.Button("Load Spreadsheet"))
            {
                notificationMessage = "";
                LoadCSVData();
            }

            GUILayout.Space(10);
            // Selection of target object and collection field
            collectionTarget =
                EditorGUILayout.ObjectField("Target Object", collectionTarget, typeof(ScriptableObject), false) as
                    ScriptableObject;
            if (collectionTarget != null)
            {
                collectionMembers = GetCollectionMembers(collectionTarget);
                if (collectionMembers != null && collectionMembers.Length > 0)
                {
                    List<string> memberNames = new List<string>();
                    foreach (var member in collectionMembers)
                    {
                        memberNames.Add(member.Name + " (" + GetMemberType(member).Name + ")");
                    }

                    selectedCollectionMemberIndex = EditorGUILayout.Popup("Collection Field",
                        selectedCollectionMemberIndex, memberNames.ToArray());

                    if (selectedCollectionMemberIndex >= 0 && selectedCollectionMemberIndex < collectionMembers.Length)
                    {
                        Type colType = GetMemberType(collectionMembers[selectedCollectionMemberIndex]);
                        // Determine the type of elements in the collection
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
                            {
                                SetupCollectionFieldMappings(elementType);
                            }

                            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
                            foreach (var mapping in collectionFieldMappings)
                            {
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField(mapping.field.Name, GUILayout.Width(150));
                                List<string> options = new List<string>() { "Do not map" };
                                if (csvHeaders != null)
                                    options.AddRange(csvHeaders);
                                int newIndex =
                                    EditorGUILayout.Popup(mapping.selectedColumnIndex + 1, options.ToArray()) - 1;
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

            if (csvData != null && csvData.Count > 1 && collectionTarget != null && elementType != null)
            {
                if (GUILayout.Button("Import into Collection"))
                {
                    notificationMessage = "";
                    ImportDataToCollection();
                }
            }
        }

        /// <summary>
        /// Retrieves members (fields and properties) of the target object that are Arrays or List&lt;T&gt;.
        /// </summary>
        private MemberInfo[] GetCollectionMembers(ScriptableObject target)
        {
            List<MemberInfo> members = new List<MemberInfo>();
            Type targetType = target.GetType();
            FieldInfo[] fields =
                targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                Type t = field.FieldType;
                if (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)))
                    members.Add(field);
            }

            PropertyInfo[] properties =
                targetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                Type t = prop.PropertyType;
                if ((t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))) &&
                    prop.CanRead && prop.CanWrite)
                    members.Add(prop);
            }

            return members.ToArray();
        }

        /// <summary>
        /// Returns the type of the member (field or property).
        /// </summary>
        private Type GetMemberType(MemberInfo member)
        {
            if (member is FieldInfo)
                return ((FieldInfo)member).FieldType;
            else if (member is PropertyInfo)
                return ((PropertyInfo)member).PropertyType;
            return null;
        }

        /// <summary>
        /// Sets up the field mappings for the collection element type.
        /// </summary>
        private void SetupCollectionFieldMappings(Type elementType)
        {
            collectionFieldMappings.Clear();
            FieldInfo[] fields =
                elementType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.IsPublic || field.GetCustomAttribute(typeof(SerializeField)) != null)
                {
                    FieldMapping mapping = new FieldMapping();
                    mapping.field = field;
                    mapping.selectedColumnIndex = -1;
                    collectionFieldMappings.Add(mapping);
                }
            }
        }

        /// <summary>
        /// Imports CSV data into the selected collection field of the target object.
        /// </summary>
        private void ImportDataToCollection()
        {
            // Get the selected collection member
            MemberInfo member = collectionMembers[selectedCollectionMemberIndex];
            object collectionValue = null;
            Type memberType = GetMemberType(member);
            if (member is FieldInfo)
                collectionValue = ((FieldInfo)member).GetValue(collectionTarget);
            else if (member is PropertyInfo)
                collectionValue = ((PropertyInfo)member).GetValue(collectionTarget, null);

            // Create a temporary list to accumulate imported items
            var listType = typeof(List<>).MakeGenericType(elementType);
            var tempList = Activator.CreateInstance(listType) as System.Collections.IList;

            // Add existing items if any
            if (collectionValue != null)
            {
                if (memberType.IsArray)
                {
                    Array existingArray = collectionValue as Array;
                    if (existingArray != null)
                    {
                        foreach (var item in existingArray)
                            tempList.Add(item);
                    }
                }
                else if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var existingList = collectionValue as System.Collections.IList;
                    if (existingList != null)
                    {
                        foreach (var item in existingList)
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
                foreach (var mapping in collectionFieldMappings)
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

            // If the member is an array, convert the list to an array; otherwise, assign the list directly.
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
        /// Loads the CSV data either from a URL or from a local file.
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
                        if (csvData != null && csvData.Count > 0)
                        {
                            csvHeaders = csvData[0];
                            SetupFieldMappings();
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
        /// Downloads the CSV data from a URL. If the URL is a public Google Spreadsheet, converts it to the CSV export URL.
        /// </summary>
        private IEnumerator DownloadCSV()
        {
            string downloadUrl = GetCsvDownloadUrl(csvPathOrUrl);
            UnityWebRequest www = UnityWebRequest.Get(downloadUrl);
            if (!string.IsNullOrEmpty(accessToken))
                www.SetRequestHeader("Authorization", "Bearer " + accessToken);
            var operation = www.SendWebRequest();
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
                    notificationMessage = "CSV downloaded and processed successfully.";
                    notificationType = NotificationType.Info;
                    Debug.Log(notificationMessage);
                }
            }
        }

        /// <summary>
        /// Converts a public Google Spreadsheet URL to its CSV export URL if applicable.
        /// </summary>
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
                        foreach (var param in parameters)
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
        private List<string[]> ParseCSV(string content)
        {
            List<string[]> data = new List<string[]>();
            try
            {
                using (StringReader reader = new StringReader(content))
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
        /// Parses a single CSV line, handling fields enclosed in quotes.
        /// </summary>
        private string[] ParseCSVLine(string line)
        {
            List<string> fields = new List<string>();
            bool inQuotes = false;
            System.Text.StringBuilder field = new System.Text.StringBuilder();
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
        /// Sets up field mappings for Mode 1 based on the ScriptableObject type.
        /// </summary>
        private void SetupFieldMappings()
        {
            fieldMappings.Clear();
            if (scriptableObjectType == null)
                return;
            FieldInfo[] fields =
                scriptableObjectType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.IsPublic || field.GetCustomAttribute(typeof(SerializeField)) != null)
                {
                    FieldMapping mapping = new FieldMapping();
                    mapping.field = field;
                    mapping.selectedColumnIndex = -1;
                    fieldMappings.Add(mapping);
                }
            }
        }

        /// <summary>
        /// Converts a string value to the specified target type.
        /// </summary>
        private object ConvertValue(string valueStr, Type targetType)
        {
            try
            {
                if (targetType == typeof(string))
                    return valueStr;
                // Nova verificação para tipos que herdam de UnityEngine.Object
                else if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
                    return FindAssetByName(valueStr, targetType);
                else if (targetType.IsEnum)
                    return Enum.Parse(targetType, valueStr, true); // Adicionado reconhecimento de enum
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
                    return Convert.ChangeType(valueStr, targetType);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error converting value '{valueStr}' to type {targetType}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Searches for an asset within the project by name and type.
        /// Se houver múltiplos ativos com mesmo nome, emite um aviso e retorna o primeiro encontrado.
        /// Se não encontrar nenhum, emite um aviso e retorna null.
        /// </summary>
        private UnityEngine.Object FindAssetByName(string assetName, Type targetType)
        {
            if (string.IsNullOrEmpty(assetName))
                return null;

            // Faz a busca usando um filtro com o nome e o tipo (usando apenas o nome do tipo)
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
                // Tenta filtrar pela correspondência exata do nome do asset
                List<UnityEngine.Object> matches = new List<UnityEngine.Object>();
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
            else // Apenas um asset encontrado
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(assetPath, targetType);
                // Caso o nome do asset não corresponda exatamente, emite um aviso mas utiliza o asset encontrado
                if (asset != null && !asset.name.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning(
                        $"Asset found for type {targetType} does not exactly match the name '{assetName}'.");
                }

                return asset;
            }
        }

        /// <summary>
        /// Helper class representing a mapping between a field and a CSV column index.
        /// </summary>
        private class FieldMapping
        {
            public FieldInfo field;
            public int selectedColumnIndex;
        }

        /// <summary>
        /// Sanitizes the asset name by removing invalid file name characters.
        /// </summary>
        private string SanitizeAssetName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c.ToString(), "");
            }

            return name;
        }

        #endregion
    }

    /// <summary>
    /// Window with step-by-step instructions for obtaining the Client ID and Client Secret from Google.
    /// </summary>
    public class GoogleOAuthInstructionsWindow : EditorWindow
    {
        private Vector2 scrollPos;

        public static void ShowInstructions()
        {
            GetWindow<GoogleOAuthInstructionsWindow>("Google OAuth2 Instructions");
        }

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
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#endif

namespace LegendaryTools
{
    public static class WeaverUtils
    {
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
        private static string WEAVE_STATIC_READONLY_STRING_MEMBER_FORMAT = "        public static readonly string {0} = \"{1}\";";
        private static string CSHARP_CLASS_EXT = ".cs";
    
#if UNITY_EDITOR
        public static void ClassConstantNameListing(string[] names, string namespaceName, string className, string folderPath = "")
        {
            StringBuilder sb = new StringBuilder();

            foreach (var s in names)
            {
                sb.AppendLine(string.Format(WEAVE_STATIC_READONLY_STRING_MEMBER_FORMAT,
                    s.CamelCaseToAllUpperWithUnderscores(),
                    s));
            }

            string file = WEAVER_CLASS_FILE_TEMPLATE.Replace("{NAMESPACE}", namespaceName);
            file = file.Replace("{CLASS_NAME}", className);
            file = file.Replace("{CLASS_MEMBERS}", sb.ToString());

            File.WriteAllText(Path.Combine(folderPath, className + CSHARP_CLASS_EXT),
                file);

            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
        }

        public static void Enum(string[] names, string namespaceName, string enumName, string folderPath = "", bool generateValue = false)
        {
            StringBuilder sb = new StringBuilder();
            HashSet<int> enumValuesInt = new HashSet<int>();
            HashSet<long> enumValuesLong = new HashSet<long>();
            
            foreach (string s in names)
            {
                string enumEntryName = s.Replace(" ", "");
                string enumValueHash = string.Empty;
                if (generateValue)
                {
                    int enumHashValueInt = GetSHA256HashAsInt(enumEntryName);
                    if (!enumValuesInt.Add(enumHashValueInt))
                    {
                        Debug.LogError($"[{nameof(WeaverUtils)}:{nameof(Enum)}] Collision detected with enum name {enumEntryName} for enum type {namespaceName}.{enumName} in folder {folderPath}, try to use another name.");
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
            
            File.WriteAllText(Path.Combine(folderPath, enumName + CSHARP_CLASS_EXT),
                file);
            
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
#endif
    }
}
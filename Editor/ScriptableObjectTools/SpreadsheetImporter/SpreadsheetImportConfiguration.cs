using UnityEngine;
using System;
using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Enum defining the import modes: Individual assets or collections stored in a ScriptableObject.
    /// </summary>
    public enum ImportMode
    {
        IndividualAssets,
        CollectionsInAsset
    }

    /// <summary>
    /// ScriptableObject that holds the configuration for importing a spreadsheet.
    /// </summary>
    [CreateAssetMenu(fileName = "SpreadsheetImportConfiguration",
        menuName = "Import Configuration/Spreadsheet Import Configuration", order = 1)]
    public class SpreadsheetImportConfiguration : ScriptableObject
    {
        [Header("General Settings")] [Tooltip("CSV file path or Spreadsheet URL.")]
        public string csvPathOrUrl;

        [Tooltip("Output folder path (relative to Assets).")]
        public string outputFolderPath;

        [Tooltip("Import mode to use (Individual Assets or Collections).")]
        public ImportMode importMode;

        [Header("ScriptableObject Settings")] [Tooltip("Type of the target ScriptableObject (as text).")]
        public string scriptableObjectTypeName;

        [Header("Mapping for Individual Assets (Mode 1)")]
        [Tooltip("Mapping between the ScriptableObject field name and the CSV column index.")]
        public List<FieldMappingConfig> fieldMappings = new List<FieldMappingConfig>();

        [Tooltip("Column index used for asset name mapping.")]
        public int assetNameColumnIndex = -1;

        [Header("Mapping for Collections (Mode 2)")]
        [Tooltip("Instance of the target ScriptableObject to import the collection into.")]
        public ScriptableObject collectionTarget;

        [Tooltip("Mapping between the element field name (for collection elements) and the CSV column index.")]
        public List<FieldMappingConfig> collectionFieldMappings = new List<FieldMappingConfig>();
    }

    /// <summary>
    /// Serializable class holding the mapping between a field name and its corresponding CSV column index.
    /// </summary>
    [Serializable]
    public class FieldMappingConfig
    {
        [Tooltip("Name of the field (must match the ScriptableObject field name).")]
        public string fieldName;

        [Tooltip("CSV column index mapped to this field.")]
        public int csvColumnIndex;
    }
}
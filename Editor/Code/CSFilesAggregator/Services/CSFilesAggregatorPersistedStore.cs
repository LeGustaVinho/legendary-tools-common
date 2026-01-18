using System.Collections.Generic;
using CSharpRegexStripper;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// ScriptableSingleton store backing persisted settings and selection.
    /// </summary>
    [FilePath("ProjectSettings/LegendaryTools.CSFilesAggregator.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class CSFilesAggregatorPersistedStore : ScriptableSingleton<CSFilesAggregatorPersistedStore>
    {
        [SerializeField]
        private List<string> _paths = new List<string>();

        [SerializeField]
        private bool _includeSubfolders;

        [SerializeField]
        private bool _removeUsings;

        [SerializeField]
        private bool _appendDelimiters = true;

        [SerializeField]
        private bool _useImplementationStripper;

        [SerializeField]
        private MethodBodyMode _stripMethodBodyMode = StripOptions.Default.MethodBodyMode;

        [SerializeField]
        private bool _stripConvertNonAutoProperties = StripOptions.Default.ConvertNonAutoGetSetPropertiesToAutoProperties;

        [SerializeField]
        private bool _stripMaskStringsAndComments = StripOptions.Default.MaskStringsAndCommentsBeforeStripping;

        [SerializeField]
        private bool _stripSkipInterfaceMembers = StripOptions.Default.SkipInterfaceMembers;

        [SerializeField]
        private bool _stripSkipAbstractMembers = StripOptions.Default.SkipAbstractMembers;

        [SerializeField]
        private bool _includeDependencies;

        [SerializeField]
        private int _dependencyMaxDepth = 3;

        [SerializeField]
        private bool _dependencyIgnorePackagesFolder = true;

        [SerializeField]
        private bool _dependencyIgnorePackageCache = true;

        [SerializeField]
        private bool _dependencyIgnoreUnresolvedTypes = true;

        [SerializeField]
        private bool _dependencyIncludeInputFilesInResult;

        [SerializeField]
        private bool _dependencyIncludeInMemoryVirtualPathsInResult = true;

        /// <summary>
        /// Reads the current store data.
        /// </summary>
        public CSFilesAggregatorPersistedData ToData()
        {
            return new CSFilesAggregatorPersistedData
            {
                Paths = _paths != null ? new List<string>(_paths) : new List<string>(),
                IncludeSubfolders = _includeSubfolders,
                RemoveUsings = _removeUsings,
                AppendDelimiters = _appendDelimiters,

                UseImplementationStripper = _useImplementationStripper,
                StripMethodBodyMode = _stripMethodBodyMode,
                StripConvertNonAutoProperties = _stripConvertNonAutoProperties,
                StripMaskStringsAndComments = _stripMaskStringsAndComments,
                StripSkipInterfaceMembers = _stripSkipInterfaceMembers,
                StripSkipAbstractMembers = _stripSkipAbstractMembers,

                IncludeDependencies = _includeDependencies,
                DependencyMaxDepth = _dependencyMaxDepth,
                DependencyIgnorePackagesFolder = _dependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache = _dependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes = _dependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult = _dependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult = _dependencyIncludeInMemoryVirtualPathsInResult,
            };
        }

        /// <summary>
        /// Overwrites the store with new data.
        /// </summary>
        public void Apply(CSFilesAggregatorPersistedData data)
        {
            if (data == null)
            {
                _paths = new List<string>();
                _includeSubfolders = false;
                _removeUsings = false;
                _appendDelimiters = true;

                _useImplementationStripper = false;
                _stripMethodBodyMode = StripOptions.Default.MethodBodyMode;
                _stripConvertNonAutoProperties = StripOptions.Default.ConvertNonAutoGetSetPropertiesToAutoProperties;
                _stripMaskStringsAndComments = StripOptions.Default.MaskStringsAndCommentsBeforeStripping;
                _stripSkipInterfaceMembers = StripOptions.Default.SkipInterfaceMembers;
                _stripSkipAbstractMembers = StripOptions.Default.SkipAbstractMembers;

                _includeDependencies = false;
                _dependencyMaxDepth = 3;
                _dependencyIgnorePackagesFolder = true;
                _dependencyIgnorePackageCache = true;
                _dependencyIgnoreUnresolvedTypes = true;
                _dependencyIncludeInputFilesInResult = false;
                _dependencyIncludeInMemoryVirtualPathsInResult = true;
                return;
            }

            _paths = data.Paths != null ? new List<string>(data.Paths) : new List<string>();
            _includeSubfolders = data.IncludeSubfolders;
            _removeUsings = data.RemoveUsings;
            _appendDelimiters = data.AppendDelimiters;

            _useImplementationStripper = data.UseImplementationStripper;
            _stripMethodBodyMode = data.StripMethodBodyMode;
            _stripConvertNonAutoProperties = data.StripConvertNonAutoProperties;
            _stripMaskStringsAndComments = data.StripMaskStringsAndComments;
            _stripSkipInterfaceMembers = data.StripSkipInterfaceMembers;
            _stripSkipAbstractMembers = data.StripSkipAbstractMembers;

            _includeDependencies = data.IncludeDependencies;
            _dependencyMaxDepth = data.DependencyMaxDepth;
            _dependencyIgnorePackagesFolder = data.DependencyIgnorePackagesFolder;
            _dependencyIgnorePackageCache = data.DependencyIgnorePackageCache;
            _dependencyIgnoreUnresolvedTypes = data.DependencyIgnoreUnresolvedTypes;
            _dependencyIncludeInputFilesInResult = data.DependencyIncludeInputFilesInResult;
            _dependencyIncludeInMemoryVirtualPathsInResult = data.DependencyIncludeInMemoryVirtualPathsInResult;
        }

        /// <summary>
        /// Writes the store to disk.
        /// </summary>
        public void SaveToDisk()
        {
            Save(true);
        }
    }
}

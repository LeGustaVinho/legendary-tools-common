// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/InMemoryTypeIndex.cs
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using LegendaryTools.CSFilesAggregator.TypeIndex;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// A lightweight type index built from in-memory sources.
    /// </summary>
    internal sealed class InMemoryTypeIndex : ITypeIndexLookup
    {
        private readonly Dictionary<string, List<TypeIndexEntry>> _byFullName;

        private InMemoryTypeIndex(Dictionary<string, List<TypeIndexEntry>> byFullName)
        {
            _byFullName = byFullName ?? new Dictionary<string, List<TypeIndexEntry>>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Builds an in-memory type index from the provided sources.
        /// </summary>
        public static InMemoryTypeIndex Build(InMemorySource[] sources, CSharpParseOptions parseOptions)
        {
            var map = new Dictionary<string, List<TypeIndexEntry>>(StringComparer.Ordinal);

            if (sources == null || sources.Length == 0)
            {
                return new InMemoryTypeIndex(map);
            }

            for (int i = 0; i < sources.Length; i++)
            {
                InMemorySource src = sources[i];
                if (src == null || string.IsNullOrEmpty(src.Code))
                {
                    continue;
                }

                string virtualPath = string.IsNullOrEmpty(src.VirtualProjectRelativePath)
                    ? (string.IsNullOrEmpty(src.InMemorySourceId) ? "InMemorySource" : src.InMemorySourceId)
                    : src.VirtualProjectRelativePath;

                SyntaxTree tree;
                try
                {
                    tree = CSharpSyntaxTree.ParseText(src.Code, parseOptions, path: virtualPath);
                }
                catch
                {
                    continue;
                }

                SyntaxNode root;
                try
                {
                    root = tree.GetRoot();
                }
                catch
                {
                    continue;
                }

                foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: false))
                {
                    if (!TryCreateEntry(tree, node, virtualPath, out TypeIndexEntry entry))
                    {
                        continue;
                    }

                    if (!map.TryGetValue(entry.FullName, out List<TypeIndexEntry> list))
                    {
                        list = new List<TypeIndexEntry>(1);
                        map.Add(entry.FullName, list);
                    }

                    list.Add(entry);
                }
            }

            return new InMemoryTypeIndex(map);
        }

        /// <inheritdoc />
        public bool TryGet(string fullName, out IReadOnlyList<TypeIndexEntry> entries)
        {
            entries = null;

            if (string.IsNullOrEmpty(fullName))
            {
                return false;
            }

            if (_byFullName.TryGetValue(fullName, out List<TypeIndexEntry> list) && list != null && list.Count > 0)
            {
                entries = list;
                return true;
            }

            return false;
        }

        private static bool TryCreateEntry(SyntaxTree tree, SyntaxNode node, string projectRelativeFilePath, out TypeIndexEntry entry)
        {
            entry = null;

            TypeDeclarationKind kind;
            SyntaxToken identifierToken;

            switch (node)
            {
                case ClassDeclarationSyntax cds:
                    kind = TypeDeclarationKind.Class;
                    identifierToken = cds.Identifier;
                    break;

                case StructDeclarationSyntax sds:
                    kind = TypeDeclarationKind.Struct;
                    identifierToken = sds.Identifier;
                    break;

                case InterfaceDeclarationSyntax ids:
                    kind = TypeDeclarationKind.Interface;
                    identifierToken = ids.Identifier;
                    break;

                case EnumDeclarationSyntax eds:
                    kind = TypeDeclarationKind.Enum;
                    identifierToken = eds.Identifier;
                    break;

                case RecordDeclarationSyntax rds:
                    kind = TypeDeclarationKind.Record;
                    identifierToken = rds.Identifier;
                    break;

                default:
                    return false;
            }

            string fullName = TypeNameBuilder.GetFullName(node);
            if (string.IsNullOrEmpty(fullName))
            {
                return false;
            }

            FileLinePositionSpan span;
            try
            {
                span = tree.GetLineSpan(identifierToken.Span);
            }
            catch
            {
                span = default;
            }

            int line = span.StartLinePosition.Line + 1;
            int column = span.StartLinePosition.Character + 1;

            entry = new TypeIndexEntry
            {
                FullName = fullName,
                Kind = kind,
                FilePath = projectRelativeFilePath,
                Line = line,
                Column = column,
            };

            return true;
        }
    }
}

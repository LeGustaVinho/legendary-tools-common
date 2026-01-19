using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegendaryTools.CSFilesAggregator.TypeIndex
{
    /// <summary>
    /// Scans a C# file using Roslyn and extracts all declared types.
    /// </summary>
    internal static class RoslynTypeScanner
    {
        /// <summary>
        /// Extracts all declared types from the given file.
        /// </summary>
        /// <param name="absoluteFilePath">Absolute file path to scan.</param>
        /// <param name="projectRelativeFilePath">Project-relative file path to store in entries.</param>
        /// <param name="parseOptions">Roslyn parse options.</param>
        /// <param name="entries">Output entries list.</param>
        public static void ScanFile(
            string absoluteFilePath,
            string projectRelativeFilePath,
            CSharpParseOptions parseOptions,
            List<TypeIndexEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            string code;
            try
            {
                code = File.ReadAllText(absoluteFilePath);
            }
            catch
            {
                // Ignore unreadable files.
                return;
            }

            SyntaxTree tree;
            try
            {
                tree = CSharpSyntaxTree.ParseText(code, parseOptions, absoluteFilePath);
            }
            catch
            {
                // If parsing fails, ignore this file.
                return;
            }

            SyntaxNode root;
            try
            {
                root = tree.GetRoot();
            }
            catch
            {
                return;
            }

            foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: false))
            {
                if (!TryCreateEntry(tree, node, projectRelativeFilePath, out TypeIndexEntry entry)) continue;

                entries.Add(entry);
            }
        }

        private static bool TryCreateEntry(SyntaxTree tree, SyntaxNode node, string projectRelativeFilePath,
            out TypeIndexEntry entry)
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
            if (string.IsNullOrEmpty(fullName)) return false;

            FileLinePositionSpan span = tree.GetLineSpan(identifierToken.Span);
            int line = span.StartLinePosition.Line + 1;
            int column = span.StartLinePosition.Character + 1;

            entry = new TypeIndexEntry
            {
                FullName = fullName,
                Kind = kind,
                FilePath = projectRelativeFilePath,
                Line = line,
                Column = column
            };

            return true;
        }
    }
}
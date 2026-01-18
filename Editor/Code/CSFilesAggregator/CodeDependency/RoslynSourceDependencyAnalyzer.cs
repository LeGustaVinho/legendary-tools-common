// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/RoslynSourceDependencyAnalyzer.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Extracts type reference candidates from a single C# source using Roslyn syntax analysis.
    /// </summary>
    internal sealed class RoslynSourceDependencyAnalyzer
    {
        private const string FileScopedNamespaceSyntaxFullName = "Microsoft.CodeAnalysis.CSharp.Syntax.FileScopedNamespaceDeclarationSyntax";

        /// <summary>
        /// Parses the given code and collects referenced type name candidates.
        /// </summary>
        /// <param name="code">C# source code.</param>
        /// <param name="parseOptions">Roslyn parse options.</param>
        /// <param name="sourcePathForDiagnostics">Optional path used by Roslyn for diagnostics.</param>
        /// <param name="context">Output file context (namespace/usings/aliases).</param>
        /// <param name="result">Output list of referenced type name candidates.</param>
        public void CollectTypeReferenceCandidates(
            string code,
            CSharpParseOptions parseOptions,
            string sourcePathForDiagnostics,
            SourceFileContext context,
            List<TypeReferenceCandidate> result)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            SyntaxTree tree;
            try
            {
                tree = CSharpSyntaxTree.ParseText(code, parseOptions, sourcePathForDiagnostics);
            }
            catch
            {
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

            CompilationUnitSyntax cus = root as CompilationUnitSyntax;
            if (cus != null)
            {
                ExtractUsingsAndAliases(cus, context);
            }

            context.Namespace = GetDeclaredNamespace(root);

            // Collect from TypeSyntax nodes (covers Roslyn guarantees these are "type positions").
            foreach (SyntaxNode n in root.DescendantNodes(descendIntoTrivia: false))
            {
                if (n is TypeSyntax typeSyntax)
                {
                    if (typeSyntax is PredefinedTypeSyntax)
                    {
                        continue;
                    }

                    if (typeSyntax is IdentifierNameSyntax ||
                        typeSyntax is GenericNameSyntax ||
                        typeSyntax is QualifiedNameSyntax ||
                        typeSyntax is AliasQualifiedNameSyntax)
                    {
                        AddCandidateFromTypeSyntax(tree, typeSyntax, result);
                    }
                }
            }

            // Attributes are not TypeSyntax; they contain NameSyntax.
            foreach (SyntaxNode n in root.DescendantNodes(descendIntoTrivia: false))
            {
                if (n is AttributeSyntax attr)
                {
                    if (attr?.Name == null)
                    {
                        continue;
                    }

                    AddCandidateFromNameSyntax(tree, attr.Name, result);
                }
            }

            // typeof(...) uses TypeSyntax; already covered. Constraints/base lists also use TypeSyntax; already covered.
        }

        private static void ExtractUsingsAndAliases(CompilationUnitSyntax root, SourceFileContext context)
        {
            foreach (UsingDirectiveSyntax u in root.Usings)
            {
                if (u == null)
                {
                    continue;
                }

                if (u.StaticKeyword.Kind() != SyntaxKind.None)
                {
                    // using static ...; is not a namespace import for type resolution (for simple names).
                    continue;
                }

                string name = u.Name?.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (u.Alias != null && !string.IsNullOrEmpty(u.Alias.Name?.Identifier.ValueText))
                {
                    string alias = u.Alias.Name.Identifier.ValueText;
                    if (!context.UsingAliases.ContainsKey(alias))
                    {
                        context.UsingAliases.Add(alias, name);
                    }

                    continue;
                }

                if (!context.Usings.Contains(name))
                {
                    context.Usings.Add(name);
                }
            }
        }

        private static string GetDeclaredNamespace(SyntaxNode root)
        {
            // Supports nested namespaces and file-scoped namespaces (via reflection for compatibility).
            var parts = new Stack<string>();

            foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: false))
            {
                if (node is NamespaceDeclarationSyntax nds)
                {
                    string name = nds.Name?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        parts.Push(name);
                    }
                }
                else if (TryGetFileScopedNamespaceName(node, out string fsn))
                {
                    parts.Push(fsn);
                }

                // Only the first namespace declaration determines the file namespace context.
                // This is best-effort for resolution. If multiple namespaces exist, resolution may be ambiguous.
                if (parts.Count > 0)
                {
                    break;
                }
            }

            if (parts.Count == 0)
            {
                return string.Empty;
            }

            // For the common case, this is just one item. Keep join for completeness.
            return string.Join(".", parts);
        }

        private static bool TryGetFileScopedNamespaceName(SyntaxNode node, out string namespaceName)
        {
            namespaceName = null;

            if (node == null)
            {
                return false;
            }

            Type nodeType = node.GetType();
            if (!string.Equals(nodeType.FullName, FileScopedNamespaceSyntaxFullName, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                PropertyInfo nameProp = nodeType.GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
                if (nameProp == null)
                {
                    return false;
                }

                object value = nameProp.GetValue(node, null);
                if (value == null)
                {
                    return false;
                }

                namespaceName = value.ToString();
                return !string.IsNullOrEmpty(namespaceName);
            }
            catch
            {
                return false;
            }
        }

        private static void AddCandidateFromTypeSyntax(SyntaxTree tree, TypeSyntax typeSyntax, List<TypeReferenceCandidate> result)
        {
            if (typeSyntax == null)
            {
                return;
            }

            string normalized = NormalizeTypeSyntax(typeSyntax);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            bool isQualified = normalized.IndexOf('.', StringComparison.Ordinal) >= 0;

            FileLinePositionSpan span;
            try
            {
                span = tree.GetLineSpan(typeSyntax.Span);
            }
            catch
            {
                span = default;
            }

            result.Add(new TypeReferenceCandidate
            {
                NormalizedName = normalized,
                IsQualified = isQualified,
                Line = span.StartLinePosition.Line + 1,
                Column = span.StartLinePosition.Character + 1,
            });
        }

        private static void AddCandidateFromNameSyntax(SyntaxTree tree, NameSyntax nameSyntax, List<TypeReferenceCandidate> result)
        {
            if (nameSyntax == null)
            {
                return;
            }

            string normalized = NormalizeNameSyntax(nameSyntax);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            bool isQualified = normalized.IndexOf('.', StringComparison.Ordinal) >= 0;

            FileLinePositionSpan span;
            try
            {
                span = tree.GetLineSpan(nameSyntax.Span);
            }
            catch
            {
                span = default;
            }

            result.Add(new TypeReferenceCandidate
            {
                NormalizedName = normalized,
                IsQualified = isQualified,
                Line = span.StartLinePosition.Line + 1,
                Column = span.StartLinePosition.Character + 1,
            });
        }

        private static string NormalizeTypeSyntax(TypeSyntax typeSyntax)
        {
            // Peel wrappers.
            while (true)
            {
                if (typeSyntax is NullableTypeSyntax nts)
                {
                    typeSyntax = nts.ElementType;
                    continue;
                }

                if (typeSyntax is ArrayTypeSyntax ats)
                {
                    typeSyntax = ats.ElementType;
                    continue;
                }

                if (typeSyntax is PointerTypeSyntax pts)
                {
                    typeSyntax = pts.ElementType;
                    continue;
                }

                break;
            }

            if (typeSyntax is PredefinedTypeSyntax)
            {
                return null;
            }

            if (typeSyntax is IdentifierNameSyntax ins)
            {
                return ins.Identifier.ValueText;
            }

            if (typeSyntax is GenericNameSyntax gns)
            {
                string name = gns.Identifier.ValueText;
                int arity = gns.TypeArgumentList?.Arguments.Count ?? 0;
                return arity > 0 ? (name + "`" + arity) : name;
            }

            if (typeSyntax is QualifiedNameSyntax qns)
            {
                return NormalizeQualifiedName(qns);
            }

            if (typeSyntax is AliasQualifiedNameSyntax aqns)
            {
                // global::System.String => System.String
                string right = NormalizeNameSyntax(aqns.Name);
                return right;
            }

            // Fallback best-effort.
            return typeSyntax.ToString();
        }

        private static string NormalizeNameSyntax(NameSyntax nameSyntax)
        {
            if (nameSyntax is IdentifierNameSyntax ins)
            {
                return ins.Identifier.ValueText;
            }

            if (nameSyntax is GenericNameSyntax gns)
            {
                string name = gns.Identifier.ValueText;
                int arity = gns.TypeArgumentList?.Arguments.Count ?? 0;
                return arity > 0 ? (name + "`" + arity) : name;
            }

            if (nameSyntax is QualifiedNameSyntax qns)
            {
                return NormalizeQualifiedName(qns);
            }

            if (nameSyntax is AliasQualifiedNameSyntax aqns)
            {
                return NormalizeNameSyntax(aqns.Name);
            }

            return nameSyntax.ToString();
        }

        private static string NormalizeQualifiedName(QualifiedNameSyntax qns)
        {
            // Rebuild as segments without type args formatting differences, using CLR-style arity.
            string left = NormalizeNameSyntax(qns.Left);
            string right = NormalizeNameSyntax(qns.Right);

            if (string.IsNullOrEmpty(left))
            {
                return right;
            }

            if (string.IsNullOrEmpty(right))
            {
                return left;
            }

            return left + "." + right;
        }
    }
}

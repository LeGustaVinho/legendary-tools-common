using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegendaryTools.CSFilesAggregator.TypeIndex
{
    /// <summary>
    /// Builds stable, deterministic fully qualified type names using only syntax information.
    /// </summary>
    internal static class TypeNameBuilder
    {
        private const string FileScopedNamespaceSyntaxFullName =
            "Microsoft.CodeAnalysis.CSharp.Syntax.FileScopedNamespaceDeclarationSyntax";

        /// <summary>
        /// Gets the fully qualified type name for a declaration node.
        /// </summary>
        /// <param name="declaration">The declaration node.</param>
        /// <returns>The fully qualified type name (namespace + containing types + name + arity).</returns>
        public static string GetFullName(SyntaxNode declaration)
        {
            string ns = GetNamespace(declaration);
            string typeChain = GetContainingTypeChain(declaration);

            if (string.IsNullOrEmpty(ns)) return typeChain;

            if (string.IsNullOrEmpty(typeChain)) return ns;

            return ns + "." + typeChain;
        }

        private static string GetNamespace(SyntaxNode node)
        {
            // Handles nested namespaces and (when available) file-scoped namespaces.
            Stack<string> parts = new();

            SyntaxNode current = node;
            while (current != null)
            {
                if (current is NamespaceDeclarationSyntax nds)
                    parts.Push(nds.Name.ToString());
                else if (TryGetFileScopedNamespaceName(current, out string fileScopedNamespace))
                    parts.Push(fileScopedNamespace);

                current = current.Parent;
            }

            if (parts.Count == 0) return string.Empty;

            return string.Join(".", parts);
        }

        private static bool TryGetFileScopedNamespaceName(SyntaxNode node, out string namespaceName)
        {
            namespaceName = null;

            // Unity/Roslyn versions prior to C# 10 do not include FileScopedNamespaceDeclarationSyntax.
            // To keep compatibility, detect it via reflection at runtime instead of referencing the type directly.
            if (node == null) return false;

            Type nodeType = node.GetType();
            if (!string.Equals(nodeType.FullName, FileScopedNamespaceSyntaxFullName, StringComparison.Ordinal))
                return false;

            // Expected property: "Name" (a NameSyntax). We only need its textual form.
            try
            {
                PropertyInfo nameProp = nodeType.GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
                if (nameProp == null) return false;

                object value = nameProp.GetValue(node, null);
                if (value == null) return false;

                namespaceName = value.ToString();
                return !string.IsNullOrEmpty(namespaceName);
            }
            catch
            {
                return false;
            }
        }

        private static string GetContainingTypeChain(SyntaxNode node)
        {
            // Build from outermost to innermost, including the node itself if it is a type declaration.
            List<string> typeParts = new(4);

            // Collect containing types by walking up and then reversing.
            Stack<string> stack = new();

            SyntaxNode current = node;
            while (current != null)
            {
                if (TryGetTypePart(current, out string part)) stack.Push(part);

                current = current.Parent;
            }

            while (stack.Count > 0)
            {
                typeParts.Add(stack.Pop());
            }

            return typeParts.Count == 0 ? string.Empty : string.Join(".", typeParts);
        }

        private static bool TryGetTypePart(SyntaxNode node, out string part)
        {
            part = null;

            switch (node)
            {
                case ClassDeclarationSyntax cds:
                    part = WithArity(cds.Identifier.ValueText, cds.TypeParameterList);
                    return true;

                case StructDeclarationSyntax sds:
                    part = WithArity(sds.Identifier.ValueText, sds.TypeParameterList);
                    return true;

                case InterfaceDeclarationSyntax ids:
                    part = WithArity(ids.Identifier.ValueText, ids.TypeParameterList);
                    return true;

                case EnumDeclarationSyntax eds:
                    part = eds.Identifier.ValueText;
                    return true;

                case RecordDeclarationSyntax rds:
                    part = WithArity(rds.Identifier.ValueText, rds.TypeParameterList);
                    return true;

                default:
                    return false;
            }
        }

        private static string WithArity(string name, TypeParameterListSyntax typeParameters)
        {
            if (typeParameters == null) return name;

            int count = typeParameters.Parameters.Count;
            if (count <= 0) return name;

            // Use CLR-style arity marker for determinism (avoids formatting and whitespace issues).
            return name + "`" + count;
        }
    }
}
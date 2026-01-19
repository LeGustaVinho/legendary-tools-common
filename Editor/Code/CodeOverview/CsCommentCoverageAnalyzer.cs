using System;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class CsCommentCoverageAnalyzer
{
    public static CsFileMetrics Analyze(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath)) return new CsFileMetrics(-1, 0, 0);

            string text = ReadAllTextShared(fullPath);
            int lineCount = CountLines(text);

            SyntaxTree tree = CSharpSyntaxTree.ParseText(text);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            BaseTypeDeclarationSyntax primaryType = FindPrimaryType(root);
            bool typeDocumented = primaryType != null && HasDocComment(primaryType);

            int totalMethods = 0;
            int documentedMethods = 0;

            int totalPublicFields = 0;
            int documentedPublicFields = 0;

            int totalPublicProperties = 0;
            int documentedPublicProperties = 0;

            foreach (SyntaxNode node in root.DescendantNodes())
            {
                if (node is MethodDeclarationSyntax method)
                {
                    totalMethods++;
                    if (HasDocComment(method)) documentedMethods++;

                    continue;
                }

                if (node is ConstructorDeclarationSyntax ctor)
                {
                    totalMethods++;
                    if (HasDocComment(ctor)) documentedMethods++;

                    continue;
                }

                if (node is PropertyDeclarationSyntax prop)
                {
                    if (!IsPublic(prop.Modifiers)) continue;

                    totalPublicProperties++;
                    if (HasDocComment(prop)) documentedPublicProperties++;

                    continue;
                }

                if (node is FieldDeclarationSyntax fieldDecl)
                {
                    if (!IsPublic(fieldDecl.Modifiers)) continue;

                    int count = fieldDecl.Declaration?.Variables.Count ?? 0;
                    if (count <= 0) continue;

                    totalPublicFields += count;

                    if (HasDocComment(fieldDecl))
                        // If the declaration has XML docs, count all declared vars as documented.
                        documentedPublicFields += count;

                    continue;
                }
            }

            int denom = 1 + totalMethods + totalPublicFields + totalPublicProperties; // +1 for the primary type
            int numer =
                (typeDocumented ? 1 : 0) +
                documentedMethods +
                documentedPublicFields +
                documentedPublicProperties;

            return new CsFileMetrics(lineCount, numer, denom);
        }
        catch (Exception)
        {
            return new CsFileMetrics(-1, 0, 0);
        }
    }

    private static string ReadAllTextShared(string fullPath)
    {
        using FileStream fs = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(fs);
        return reader.ReadToEnd();
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') lines++;
        }

        return lines;
    }

    private static BaseTypeDeclarationSyntax FindPrimaryType(CompilationUnitSyntax root)
    {
        foreach (SyntaxNode node in root.DescendantNodes())
        {
            if (node is ClassDeclarationSyntax cls) return cls;

            if (node is StructDeclarationSyntax st) return st;

            if (node is InterfaceDeclarationSyntax itf) return itf;

            if (node is EnumDeclarationSyntax en) return en;
        }

        return null;
    }

    private static bool IsPublic(SyntaxTokenList modifiers)
    {
        for (int i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].IsKind(SyntaxKind.PublicKeyword)) return true;
        }

        return false;
    }

    private static bool HasDocComment(SyntaxNode node)
    {
        // XML doc comments are stored as structured trivia in the leading trivia list.
        SyntaxTriviaList trivia = node.GetLeadingTrivia();

        for (int i = 0; i < trivia.Count; i++)
        {
            if (!trivia[i].HasStructure) continue;

            SyntaxNode structure = trivia[i].GetStructure();
            if (structure is DocumentationCommentTriviaSyntax) return true;
        }

        return false;
    }
}
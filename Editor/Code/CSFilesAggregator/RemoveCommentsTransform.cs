using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Removes C# comments (single-line, multi-line and documentation comments) using Roslyn.
    /// </summary>
    public sealed class RemoveCommentsTransform : ITextTransform
    {
        /// <inheritdoc />
        public TextDocument Transform(TextDocument document, List<Diagnostic> diagnostics)
        {
            if (document == null) return document;

            try
            {
                string input = document.Text ?? string.Empty;

                // Parse as-is; we want to preserve most original formatting/trivia except comments.
                SyntaxTree tree = CSharpSyntaxTree.ParseText(input);
                SyntaxNode root = tree.GetRoot();

                SyntaxNode updatedRoot = new CommentTriviaRemover().Visit(root);

                // Keep original line endings as much as possible; do not NormalizeWhitespace() here.
                string output = updatedRoot?.ToFullString() ?? string.Empty;

                return document.WithText(output);
            }
            catch (Exception ex)
            {
                diagnostics?.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Failed to remove comments for {document.DisplayPath}: {ex.Message}",
                    document.DisplayPath));

                return document;
            }
        }

        private sealed class CommentTriviaRemover : CSharpSyntaxRewriter
        {
            public override SyntaxToken VisitToken(SyntaxToken token)
            {
                SyntaxTriviaList leading = FilterTrivia(token.LeadingTrivia);
                SyntaxTriviaList trailing = FilterTrivia(token.TrailingTrivia);

                return token.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);
            }

            private static SyntaxTriviaList FilterTrivia(SyntaxTriviaList triviaList)
            {
                if (triviaList.Count == 0) return triviaList;

                List<SyntaxTrivia> kept = new(triviaList.Count);

                for (int i = 0; i < triviaList.Count; i++)
                {
                    SyntaxTrivia trivia = triviaList[i];

                    if (IsCommentTrivia(trivia))
                        // Drop comment trivia but keep surrounding whitespace/newlines (separate trivia).
                        continue;

                    kept.Add(trivia);
                }

                return SyntaxFactory.TriviaList(kept);
            }

            private static bool IsCommentTrivia(SyntaxTrivia trivia)
            {
                // Covers: //, /* */, ///, /** */
                // Also remove the "documentation exterior" (the "///" prefix token trivia).
                return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                       || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                       || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                       || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
                       || trivia.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia);
            }
        }
    }
}
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using CS = Microsoft.CodeAnalysis.CSharp;
using VB = Microsoft.CodeAnalysis.VisualBasic;

namespace Modernization.Analyzers.Tests;

[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public sealed class CommentLanguageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [OutcomeAnalyzer.English];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(Analyze);
    }

    private static void Analyze(SyntaxTreeAnalysisContext context)
    {
        foreach (var trivia in context.Tree.GetRoot(context.CancellationToken).DescendantTrivia())
        {
            var doc = context.Tree.Options.Language == LanguageNames.CSharp
                ? trivia.IsKind(CS.SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                  trivia.IsKind(CS.SyntaxKind.MultiLineDocumentationCommentTrivia)
                : trivia.IsKind(VB.SyntaxKind.DocumentationCommentTrivia);
            var comment = context.Tree.Options.Language == LanguageNames.CSharp
                ? trivia.IsKind(CS.SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(CS.SyntaxKind.MultiLineCommentTrivia)
                : trivia.IsKind(VB.SyntaxKind.CommentTrivia);
            if (!doc && !comment) continue;
            var prose = trivia.ToFullString();
            if (doc)
            {
                prose = Regex.Replace(prose, @"(?m)^\s*(///|'''|\* ?)", "");
                prose = prose.Replace("/**", "").Replace("*/", "");
                prose = ProseDetector.XmlText("<root>" + prose + "</root>");
            }
            if (ProseDetector.Detect(prose) is { } evidence)
                context.ReportDiagnostic(Diagnostic.Create(OutcomeAnalyzer.English, trivia.GetLocation(), evidence));
        }
    }
}

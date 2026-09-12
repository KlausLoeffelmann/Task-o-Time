using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "AnalyzerUnit")]
public sealed class MigrationToolTests
{
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Concat(new[] { typeof(Compilation).Assembly.Location, typeof(CSharpCompilation).Assembly.Location,
            typeof(VisualBasicCompilation).Assembly.Location }).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(p => MetadataReference.CreateFromFile(p)).ToArray();
    internal const string CSharpTool = """
        using System;
        using System.Linq;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp;
        using VB = Microsoft.CodeAnalysis.VisualBasic;
        public static class ReusablePorter {
          public static string Convert(string input, MetadataReference[] references) {
            var tree = VB.VisualBasicSyntaxTree.ParseText(input);
            var source = VB.VisualBasicCompilation.Create("Input", new[] { tree }, references,
              new VB.VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            if (source.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
              throw new InvalidOperationException("Invalid input");
            var model = source.GetSemanticModel(tree);
            var declaration = tree.GetRoot().DescendantNodes().OfType<VB.Syntax.ClassStatementSyntax>().First();
            var symbol = model.GetDeclaredSymbol(declaration);
            var generated = SyntaxFactory.CompilationUnit().AddMembers(SyntaxFactory.ClassDeclaration(symbol.Name));
            var output = CSharpCompilation.Create("Output", new[] { CSharpSyntaxTree.Create(generated) }, references,
              new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            if (output.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
              throw new InvalidOperationException("Invalid output");
            return generated.NormalizeWhitespace().ToFullString();
          }
        }
        """;
    private const string VisualBasicTool = """
        Imports System
        Imports System.Linq
        Imports Microsoft.CodeAnalysis
        Imports CS = Microsoft.CodeAnalysis.CSharp
        Imports VB = Microsoft.CodeAnalysis.VisualBasic
        Public Class ReusablePorter
          Public Shared Function Convert(input As String, references As MetadataReference()) As String
            Dim tree = VB.VisualBasicSyntaxTree.ParseText(input)
            Dim source = VB.VisualBasicCompilation.Create("Input", {tree}, references,
                New VB.VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            If source.GetDiagnostics().Any(Function(d) d.Severity = DiagnosticSeverity.Error) Then
              Throw New InvalidOperationException("Invalid input")
            End If
            Dim model = source.GetSemanticModel(tree)
            Dim declaration = tree.GetRoot().DescendantNodes().OfType(Of VB.Syntax.ClassStatementSyntax)().First()
            Dim symbol = model.GetDeclaredSymbol(declaration)
            Dim generated = CS.SyntaxFactory.CompilationUnit().AddMembers(CS.SyntaxFactory.ClassDeclaration(symbol.Name))
            Dim output = CS.CSharpCompilation.Create("Output", {CS.CSharpSyntaxTree.Create(generated)}, references,
                New CS.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            If output.GetDiagnostics().Any(Function(d) d.Severity = DiagnosticSeverity.Error) Then
              Throw New InvalidOperationException("Invalid output")
            End If
            Return generated.NormalizeWhitespace().ToFullString()
          End Function
        End Class
        """;
    internal const string Input = """
        Public Class Representative(Of T)
          Public Property Value As T
          Public Event Changed As System.EventHandler
          Public Sub Visit(items As T())
            For Each item In items
              Value = item
            Next
          End Sub
        End Class
        """;
    internal const string Output = """
        public class Representative<T> {
          public T Value { get; set; }
          public event System.EventHandler Changed;
          public void Visit(T[] items) { foreach (var item in items) Value = item; }
        }
        """;
    private static Compilation Compile(string language, string code)
    {
        Compilation compilation = language == LanguageNames.CSharp
            ? CSharpCompilation.Create("IndependentPorter", [CSharpSyntaxTree.ParseText(code, path: @"C:\tooling\Converter.cs")],
                References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            : VisualBasicCompilation.Create("IndependentPorter", [VisualBasicSyntaxTree.ParseText(code, path: @"C:\tooling\Converter.vb")],
                References, new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On));
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        return compilation;
    }
    private static async Task<ImmutableArray<Diagnostic>> Analyze(Compilation tool, bool fixtures = true, string? output = null)
    {
        var app = AnalyzerTests.Compile(LanguageNames.CSharp, "public class Entry {}");
        var analyzer = new OutcomeAnalyzer([new("", app), new(@"C:\tooling\Porter.csproj", tool, Tooling: true)]);
        var options = new AnalyzerOptions(fixtures ? [new InputFile(@"C:\tooling\fixtures\Representative.vb", Input),
            new InputFile(@"C:\tooling\fixtures\Representative.cs", output ?? Output)] : []);
        var found = await app.WithAnalyzers([analyzer], options).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(found, d => d.Id == "AD0001");
        return found;
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Static_shape_is_only_evidence_and_not_actual_conversion_acceptance(string language)
    {
        var tool = Compile(language, language == LanguageNames.CSharp ? CSharpTool : VisualBasicTool);
        Assert.DoesNotContain(await Analyze(tool), d => d.Id == "TOOL001");
        Assert.Contains(await Analyze(tool, fixtures: false), d => d.Id == "TOOL001");
        Assert.Contains(await Analyze(tool, output: Output.Replace("public T Value", "public MissingType Value")), d => d.Id == "TOOL001");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Imports_constructions_and_constant_output_do_not_count_as_migration(string language)
    {
        var importsOnly = language == LanguageNames.CSharp
            ? "using Microsoft.CodeAnalysis; using Microsoft.CodeAnalysis.VisualBasic; using Microsoft.CodeAnalysis.CSharp; public class Empty {}"
            : "Imports Microsoft.CodeAnalysis\nImports Microsoft.CodeAnalysis.VisualBasic\nImports Microsoft.CodeAnalysis.CSharp\nPublic Class Empty\nEnd Class";
        Assert.Contains(await Analyze(Compile(language, importsOnly)), d => d.Id == "TOOL001");
        var disconnected = language == LanguageNames.CSharp ? CSharpTool.Replace("symbol.Name", "\"Dummy\"") :
            VisualBasicTool.Replace("symbol.Name", "\"Dummy\"");
        Assert.Contains(await Analyze(Compile(language, disconnected)), d => d.Id == "TOOL001");
        var uncheckedTool = language == LanguageNames.CSharp ? CSharpTool.Replace("output.GetDiagnostics()", "source.GetDiagnostics()") :
            VisualBasicTool.Replace("output.GetDiagnostics()", "source.GetDiagnostics()");
        Assert.Contains(await Analyze(Compile(language, uncheckedTool)), d => d.Id == "TOOL001");
    }

    public static IEnumerable<object[]> GuardCases()
    {
        foreach (var language in new[] { LanguageNames.CSharp, LanguageNames.VisualBasic })
        foreach (var variant in new[] { "false-predicate", "negated-rejects-success", "warning-predicate",
            "partial-exit", "conditional-guard", "negated-error-else", "where-errors", "local-error-flag" })
            yield return [language, variant, variant is "negated-error-else" or "where-errors" or "local-error-flag"];
    }

    [Theory]
    [MemberData(nameof(GuardCases))]
    public async Task Diagnostic_guards_must_reject_the_error_branch_before_emission(string language, string variant, bool healthy)
    {
        var csharp = language == LanguageNames.CSharp;
        var code = csharp ? CSharpTool : VisualBasicTool;
        var predicate = csharp ? "d => d.Severity == DiagnosticSeverity.Error" : "Function(d) d.Severity = DiagnosticSeverity.Error";
        foreach (var name in new[] { "source", "output" })
        {
            var expression = name + ".GetDiagnostics().Any(" + predicate + ")";
            switch (variant)
            {
                case "false-predicate":
                    code = code.Replace(expression, name + ".GetDiagnostics().Any(" + (csharp ? "d => false" : "Function(d) False") + ")");
                    break;
                case "warning-predicate":
                    code = code.Replace(expression, expression.Replace("DiagnosticSeverity.Error", "DiagnosticSeverity.Warning"));
                    break;
                case "negated-rejects-success":
                    code = code.Replace(expression, (csharp ? "!" : "Not ") + expression);
                    break;
                case "negated-error-else":
                    code = csharp ? code.Replace("if (" + expression + ")", "if (!" + expression + ") { } else") :
                        code.Replace("If " + expression + " Then", "If Not " + expression + " Then\nElse");
                    break;
                case "where-errors":
                    code = code.Replace(expression, name + ".GetDiagnostics().Where(" + predicate + ").Any()");
                    break;
                case "local-error-flag":
                    code = csharp ? code.Replace("if (" + expression + ")", "var " + name + "HasErrors = " + expression + ";\nif (" + name + "HasErrors)") :
                        code.Replace("If " + expression + " Then", "Dim " + name + "HasErrors = " + expression + "\nIf " + name + "HasErrors Then");
                    break;
            }
        }
        if (variant == "partial-exit")
            code = csharp ? code.Replace("""throw new InvalidOperationException("Invalid input");""",
                """if (input.Length == 0) throw new InvalidOperationException("Invalid input");""") :
                code.Replace("""Throw New InvalidOperationException("Invalid input")""",
                    "If input.Length = 0 Then\nThrow New InvalidOperationException(\"Invalid input\")\nEnd If");
        if (variant == "conditional-guard")
        {
            var condition = "source.GetDiagnostics().Any(" + predicate + ")";
            code = csharp ? code.Replace("if (" + condition + ")", "if (input.Length == 0) { if (" + condition + ")")
                .Replace("""throw new InvalidOperationException("Invalid input");""", """throw new InvalidOperationException("Invalid input"); }""") :
                code.Replace("If " + condition + " Then", "If input.Length = 0 Then\nIf " + condition + " Then")
                    .Replace("""Throw New InvalidOperationException("Invalid input")""", "Throw New InvalidOperationException(\"Invalid input\")\nEnd If");
        }
        Assert.Equal(!healthy, (await Analyze(Compile(language, code))).Any(d => d.Id == "TOOL001"));
    }
}

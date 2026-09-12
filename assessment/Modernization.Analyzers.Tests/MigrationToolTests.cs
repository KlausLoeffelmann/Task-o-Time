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
    private const string ExternalAdapter = """
        using System;
        using System.IO;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using ICSharpCode.CodeConverter.Common;
        using ICSharpCode.CodeConverter.CSharp;
        using Microsoft.CodeAnalysis;
        public static class DocumentPorter {
          public static async Task Convert(IReadOnlyCollection<Document> documents, string output) {
            var emitted = new Dictionary<string, string>();
            await foreach (var result in ProjectConversion.ConvertDocumentsAsync<VBToCSConversion>(documents, new ConversionOptions())) {
              if (!result.Success) throw new InvalidOperationException("Conversion failed");
              var code = Repair(result.ConvertedCode);
              emitted.Add(result.TargetPathOrNull, code);
            }
            foreach (var (path, code) in emitted)
              await File.WriteAllTextAsync(Path.Combine(output, path), code);
          }
          private static string Repair(string code) => code;
        }
        """;

    private static Compilation CompileAdapter(string source)
    {
        var references = References.Concat(new[] {
            MetadataReference.CreateFromFile(typeof(Document).Assembly.Location),
            MetadataReference.CreateFromFile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "compiler-engine-reference.txt")).Trim())
        }).DistinctBy(r => r.Display);
        var compilation = CSharpCompilation.Create("ExternalPorter", [CSharpSyntaxTree.ParseText(source)],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        return compilation;
    }

    [Fact]
    public async Task Guarded_external_compiler_engine_flows_through_postprocessor_and_async_emission()
    {
        Assert.DoesNotContain(await Analyze(CompileAdapter(ExternalAdapter)), d => d.Id == "TOOL001");
    }

    [Fact]
    public async Task Source_defined_engine_lookalike_is_not_a_verified_external_contract()
    {
        var lookalike = """
            namespace ICSharpCode.CodeConverter.Common {
              public static class ProjectConversion {
                public static async IAsyncEnumerable<FakeResult> ConvertDocumentsAsync<T>(
                    IReadOnlyCollection<Document> documents, ConversionOptions options) {
                  await Task.Yield();
                  yield return new FakeResult();
                }
              }
              public sealed class FakeResult {
                public bool Success => true;
                public string ConvertedCode => "public class Empty {}";
                public string TargetPathOrNull => "Empty.cs";
              }
            }
            """;
        Assert.Contains(await Analyze(CompileAdapter(ExternalAdapter + lookalike)), d => d.Id == "TOOL001");
    }

    [Fact]
    public async Task Discarded_engine_stream_does_not_authenticate_fake_result_elements()
    {
        var source = ExternalAdapter.Replace(
            "ProjectConversion.ConvertDocumentsAsync<VBToCSConversion>(documents, new ConversionOptions())",
            "Discard(ProjectConversion.ConvertDocumentsAsync<VBToCSConversion>(documents, new ConversionOptions()))")
            .Replace("private static string Repair", """
                private static async IAsyncEnumerable<FakeResult> Discard(object ignored) {
                    await Task.Yield();
                    yield return new FakeResult();
                }
                private sealed class FakeResult {
                    public bool Success => true;
                    public string ConvertedCode => "public class Empty {}";
                    public string TargetPathOrNull => "Empty.cs";
                }
                private static string Repair
                """);
        Assert.Contains(await Analyze(CompileAdapter(source)), d => d.Id == "TOOL001");
    }

    [Theory]
    [InlineData("")]
    [InlineData("base.Add(path, \"public class Empty {}\");")]
    [InlineData("if (false) base.Add(path, code);")]
    [InlineData("base.Add(path, code); base.Clear();")]
    public async Task Hidden_dictionary_add_that_discards_content_is_not_emission(string body)
    {
        var source = ExternalAdapter.Replace("new Dictionary<string, string>()", "new Sink()") + $$"""
            public class Sink : Dictionary<string, string> {
                public new void Add(string path, string code) { {{body}} }
            }
            """;
        Assert.Contains(await Analyze(CompileAdapter(source)), d => d.Id == "TOOL001");
    }

    [Fact]
    public async Task Source_dictionary_forwarder_requires_real_base_insertion()
    {
        var source = ExternalAdapter.Replace("new Dictionary<string, string>()", "new Sink()") + """
            public class Sink : Dictionary<string, string> {
                public new void Add(string path, string code) => base.Add(path, code);
            }
            """;
        Assert.DoesNotContain(await Analyze(CompileAdapter(source)), d => d.Id == "TOOL001");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Returned_stream_forwarders_preserve_elements_not_just_argument_dependencies(bool discard)
    {
        var source = ExternalAdapter.Replace(
            "ProjectConversion.ConvertDocumentsAsync<VBToCSConversion>(documents, new ConversionOptions())",
            "Forward(ProjectConversion.ConvertDocumentsAsync<VBToCSConversion>(documents, new ConversionOptions()))")
            .Replace("private static string Repair", (discard ? """
                private static async IAsyncEnumerable<T> Forward<T>(IAsyncEnumerable<T> source) {
                    await Task.Yield();
                    yield return default(T);
                }
                """ : """
                private static IAsyncEnumerable<T> Forward<T>(IAsyncEnumerable<T> source) => source;
                """) + "\nprivate static string Repair");
        Assert.Equal(discard, (await Analyze(CompileAdapter(source))).Any(d => d.Id == "TOOL001"));
    }

    [Fact]
    public async Task Engine_stream_aliases_retain_verified_element_provenance()
    {
        const string engine = "ProjectConversion.ConvertDocumentsAsync<VBToCSConversion>(documents, new ConversionOptions())";
        var source = ExternalAdapter.Replace(engine, "stream")
            .Replace("await foreach", "var stream = " + engine + "; await foreach");
        Assert.DoesNotContain(await Analyze(CompileAdapter(source)), d => d.Id == "TOOL001");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_engine_requires_document_content_not_only_workspace_paths(bool discardedContent)
    {
        var source = ExternalAdapter.Replace("using System.IO;", "using System.IO; using System.Linq; using Microsoft.CodeAnalysis.Text;")
            .Replace("IReadOnlyCollection<Document> documents", "string input")
            .Replace("var emitted =", """
                using var workspace = new AdhocWorkspace();
                Populate(workspace, input);
                var selected = workspace.CurrentSolution.Projects.OrderBy(p => p.FilePath).ToArray();
                var emitted =
                """)
            .Replace("await foreach", """
                foreach (var project in selected) {
                var documents = project.Documents.Where(d => d.FilePath != null).ToArray();
                await foreach
                """)
            .Replace("foreach (var (path, code)", "} foreach (var (path, code)")
            .Replace("private static string Repair", """
                private static void Populate(AdhocWorkspace workspace, string input) {
                    var id = ProjectId.CreateNewId();
                    var documents = System.Collections.Immutable.ImmutableArray.Create(input).Select(path => {
                        using var stream = File.OpenRead(path);
                        var text = SourceText.From(stream);
                        return DocumentInfo.Create(DocumentId.CreateNewId(id), "Input.vb",
                            loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create())), filePath: path);
                    }).ToArray();
                    workspace.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "Source", "Source",
                        LanguageNames.VisualBasic, documents: documents));
                }
                private static string Repair
                """);
        if (discardedContent) source = source.Replace("var text = SourceText.From(stream);", "var text = SourceText.From(\"Public Class Empty\\nEnd Class\");");
        var found = await Analyze(CompileAdapter(source));
        Assert.Equal(discardedContent, found.Any(d => d.Id == "TOOL001"));
    }

    [Theory]
    [InlineData("if (!result.Success)", "if (result.Success)")]
    [InlineData("if (!result.Success)", "if (!result.Success && output.Length < 0)")]
    [InlineData("throw new InvalidOperationException(\"Conversion failed\");", "Console.WriteLine(\"Conversion failed\");")]
    [InlineData("private static string Repair(string code) => code;", "private static string Repair(string code) => \"public class Empty {}\";")]
    [InlineData("var code = Repair(result.ConvertedCode);", "var code = Repair(result.ConvertedCode); code = \"public class Empty {}\";")]
    [InlineData("emitted.Add(result.TargetPathOrNull, code);", "emitted.Add(code, \"public class Empty {}\");")]
    [InlineData("var code = Repair(result.ConvertedCode);", "Repair(result.ConvertedCode); var code = Repair(\"public class Empty {}\");")]
    [InlineData("ConvertDocumentsAsync<VBToCSConversion>(documents,", "ConvertDocumentsAsync<VBToCSConversion>(Array.Empty<Document>(),")]
    [InlineData("public static async Task Convert(", "private static async Task Convert(")]
    [InlineData("await foreach", "if (false) await foreach")]
    [InlineData("await File.WriteAllTextAsync", "if (false) await File.WriteAllTextAsync")]
    [InlineData("foreach (var (path, code)", "emitted.Clear(); foreach (var (path, code)")]
    [InlineData("foreach (var (path, code)", "emitted = new(); foreach (var (path, code)")]
    [InlineData("emitted.Add(result.TargetPathOrNull, code);", "emitted.Add(result.TargetPathOrNull, code); emitted[result.TargetPathOrNull] = \"public class Empty {}\";")]
    public async Task External_engine_dependencies_dead_results_stubs_and_invalid_guards_do_not_count(string oldValue, string replacement)
    {
        Assert.Contains(await Analyze(CompileAdapter(ExternalAdapter.Replace(oldValue, replacement))), d => d.Id == "TOOL001");
    }
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
    private static async Task<ImmutableArray<Diagnostic>> Analyze(Compilation tool, bool fixtures = true, string? output = null,
        string? driver = null)
    {
        var app = AnalyzerTests.Compile(LanguageNames.CSharp, "public class Entry {}");
        var corpus = new List<AssessmentProject> { new("", app), new(@"C:\tooling\Porter.csproj", tool, Tooling: true) };
        if (driver != null)
        {
            var test = CSharpCompilation.Create("Driver", [CSharpSyntaxTree.ParseText(driver)],
                References.Append(tool.ToMetadataReference()), new CSharpCompilationOptions(OutputKind.ConsoleApplication));
            Assert.DoesNotContain(test.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
            corpus.Add(new(@"C:\tooling\tests\Driver.csproj", test, Test: true));
        }
        var analyzer = new OutcomeAnalyzer(corpus);
        var options = new AnalyzerOptions(driver != null ? [new InputFile(@"C:\tooling\tests\fixtures\Legacy.vb", Input)] :
            fixtures ? [new InputFile(@"C:\tooling\fixtures\Representative.vb", Input),
            new InputFile(@"C:\tooling\fixtures\Representative.cs", output ?? Output)] : []);
        var found = await app.WithAnalyzers([analyzer], options).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(found, d => d.Id == "AD0001");
        return found;
    }
    private const string CanaryDriver = """
        using System;
        using System.IO;
        using System.Diagnostics;
        class Driver {
          static void Main() {
            var before = Run("input");
            var conversion = Convert();
            Require(conversion == 0);
            var code = File.ReadAllText(@"output\Converted.cs");
            Require(code.Contains("Value"));
            var after = Run("output");
            Require(after.Code == 0 && before.Text == after.Text);
          }
          static int Convert() {
            var start = new ProcessStartInfo("dotnet");
            start.ArgumentList.Add("ExternalPorter.dll");
            using var process = Process.Start(start);
            process.WaitForExit();
            return process.ExitCode;
          }
          static (int Code, string Text) Run(string root) {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(root, "Canary.exe")) { RedirectStandardOutput = true });
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, text);
          }
          static void Require(bool success) { if (!success) throw new Exception("Failed"); }
        }
        """;

    [Fact]
    public async Task Compiling_executable_canary_and_actual_output_assertions_replace_basename_fixture_conventions()
    {
        Assert.DoesNotContain(await Analyze(CompileAdapter(ExternalAdapter), driver: CanaryDriver), d => d.Id == "TOOL001");
    }

    [Fact]
    public async Task Top_level_canary_local_functions_are_reachable()
    {
        var driver = CanaryDriver.Replace("\r\n", "\n").Replace("class Driver {\n  static void Main() {", "")
            .Replace("\n  }\n  static int Convert()", "\n  static int Convert()");
        driver = driver[..driver.LastIndexOf('}')];
        Assert.DoesNotContain(await Analyze(CompileAdapter(ExternalAdapter), driver: driver), d => d.Id == "TOOL001");
    }

    [Fact]
    public async Task Async_canary_tracks_argument_arrays_streams_and_tuple_results()
    {
        var driver = """
            using System;
            using System.IO;
            using System.Diagnostics;
            using System.Threading.Tasks;
            var tool = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var root = Path.Combine(tool, "artifacts", "tests-" + Guid.NewGuid().ToString("N"));
            var input = Path.Combine(root, "input");
            var output = Path.Combine(root, "output");
            var cli = Path.Combine("tool", "ExternalPorter.dll");
            var before = await Run(Path.Combine(input, "Consumer", "bin", "Debug", "net472", "Consumer.exe"), input);
            var conversion = await Convert("input", "output");
            Require(conversion.Code == 0, conversion.Text);
            var code = File.ReadAllText(@"output\Converted.cs");
            Require(code.Contains("Value"), "output construct");
            var after = await Run(Path.Combine(output, "Consumer", "bin", "Debug", "net472", "Consumer.exe"), output);
            Require(after.Code == 0 && before.Text == after.Text, "behavior");
            async Task<(int Code, string Text)> Convert(string from, string to, params string[] extra) =>
                await Run("dotnet", "tool", ["exec", cli, "convert", "--input", from, "--output", to, .. extra]);
            static async Task<(int Code, string Text)> Run(string file, string directory, params string[] args) {
                var start = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in args) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!;
                var streams = await Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
                await process.WaitForExitAsync();
                return (process.ExitCode, string.Concat(streams));
            }
            static void Require(bool success, string message) {
                if (!success) throw new InvalidOperationException(message);
            }
            """;
        Assert.DoesNotContain(await Analyze(CompileAdapter(ExternalAdapter), driver: driver), d => d.Id == "TOOL001");
    }

    [Theory]
    [InlineData("input", true)]
    [InlineData("output", false)]
    public async Task Canary_process_origins_follow_wrapper_returns_not_helper_names(string root, bool rejected)
    {
        var driver = CanaryDriver.Replace("var after = Run(\"output\");", "var after = OtherRun();")
            .Replace("static int Convert()", $"static (int Code, string Text) OtherRun() => Run(\"{root}\");\nstatic int Convert()");
        Assert.Equal(rejected, (await Analyze(CompileAdapter(ExternalAdapter), driver: driver)).Any(d => d.Id == "TOOL001"));
    }

    [Theory]
    [InlineData("return (process.ExitCode, text);", "return (0, \"Always fine\");")]
    [InlineData("before.Text == after.Text", "before.Code == after.Code")]
    [InlineData("before.Text == after.Text", "before.Text == before.Text")]
    [InlineData("Require(code.Contains(\"Value\"));", "Require(true);")]
    [InlineData("if (!success) throw new Exception(\"Failed\");", "Console.WriteLine(success);")]
    [InlineData("start.ArgumentList.Add(\"ExternalPorter.dll\");", "start.ArgumentList.Add(\"Unrelated.dll\");")]
    [InlineData("static void Main() {", "static void Main() { } static void Unused() {")]
    [InlineData("if (!success) throw new Exception(\"Failed\");", "void NeverCalled() { if (!success) throw new Exception(\"Failed\"); }")]
    [InlineData("var after = Run(\"output\");", "var after = before;")]
    [InlineData("var after = Run(\"output\");", "var after = Run(\"input\");")]
    [InlineData("var after = Run(\"output\");", "var alias = before; var after = alias;")]
    [InlineData("var after = Run(\"output\");", "var sameInput = \"input\"; var after = Run(sameInput);")]
    [InlineData("if (!success) throw new Exception(\"Failed\");", "if (false) { if (!success) throw new Exception(\"Failed\"); }")]
    public async Task Canary_dependencies_stubs_dead_checks_and_unrelated_processes_are_not_fixture_evidence(string oldValue, string replacement)
    {
        Assert.Contains(await Analyze(CompileAdapter(ExternalAdapter), driver: CanaryDriver.Replace(oldValue, replacement)), d => d.Id == "TOOL001");
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

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using ExternalEvaluation;

namespace Modernization.Analyzers.Tests;

public sealed record ScanDiagnostic(string Id, string Severity, string Project, string Path, int Line, int Column, string Message);
public sealed record ScanReport(bool CompilationValid, string[] Projects, ScanDiagnostic[] Diagnostics)
{
    public string[] ArchitectureTypes { get; init; } = [];
    public string[] SourcePaths { get; init; } = [];
    public string[] AdditionalPaths { get; init; } = [];
    public SortedDictionary<string, CriterionMetric> Metrics { get; init; } = new(StringComparer.Ordinal);
}

public sealed class RepositoryTests
{
    private static readonly Lazy<Task<ScanReport>> Scan = new(LoadAndScan);

    [Fact]
    [Trait("Category", "RepositoryScan")]
    public async Task Repository_compiles_and_report_is_written()
    {
        var report = await Scan.Value;
        Assert.True(report.CompilationValid, Evidence(report.Diagnostics.Where(d => d.Severity == "Error")));
    }

    [Fact]
    [Trait("Category", "Modernization")]
    public async Task Production_meets_all_modernization_and_business_criteria()
    {
        var report = await Scan.Value;
        Assert.True(report.CompilationValid, Evidence(report.Diagnostics.Where(d => d.Severity == "Error")));
        Assert.True(report.Diagnostics.Length == 0, Evidence(report.Diagnostics));
    }

    private static string Evidence(IEnumerable<ScanDiagnostic> diagnostics) => string.Join(Environment.NewLine,
        diagnostics.Select(d => $"{d.Id} {d.Path}({d.Line},{d.Column}): {d.Message}"));

    private static async Task<ScanReport> LoadAndScan()
    {
        try
        {
            return await LoadWorkspace();
        }
        catch (Exception exception)
        {
            return await WriteReport(FindRoot(), new(false, [],
                [new("LOAD001", "Error", "", "", 0, 0, exception.GetType().Name + ": " + exception.Message)]));
        }
    }

    private static async Task<ScanReport> LoadWorkspace()
    {
        var root = FindRoot();
        var diagnostics = new List<ScanDiagnostic>();
        var projects = new List<string>();
        var architectureTypes = new List<string>();
        var sourcePaths = new List<string>();
        var additionalPaths = new List<string>();
        var metrics = new SortedDictionary<string, CriterionMetric>(StringComparer.Ordinal);
        var valid = false;
        try
        {
            var referenceRoot = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
                "framework-reference-root.txt"))).Trim();
            if (!Directory.Exists(Path.Combine(referenceRoot, "v4.6.1")))
                throw new InvalidOperationException("Restored .NET Framework 4.6.1 reference assemblies are missing: " + referenceRoot);
            var loader = new CompilerInputLoader(Path.Combine(EvaluatorConfiguration.ArtifactRoot, "compiler-inputs"));
            foreach (var directory in new[] { "TaskOTime.App" })
            {
                var projectPath = Directory.EnumerateFiles(Path.Combine(root, directory), "*.*proj")
                    .Single(p => Path.GetExtension(p) is ".csproj" or ".vbproj");
                await loader.Load(projectPath);
            }
            foreach (var project in EvaluatorConfiguration.DiscoveryRoots.SelectMany(DiscoverProjects)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
                await loader.Load(project);
            var corpus = loader.Projects.Where(p => !p.IsTest).ToArray();
            foreach (var project in corpus)
            {
                projects.Add(Relative(root, project.Path));
                sourcePaths.AddRange(project.Compilation.SyntaxTrees.Select(t => Relative(root, t.FilePath)));
                additionalPaths.AddRange(project.AdditionalFiles.Select(f => Relative(root, f.Path)));
                architectureTypes.AddRange(new PresentationScope(project.Compilation).Types
                    .Where(EvaluatorConfiguration.Selection.Includes).Select(t => t.ToDisplayString()));
                diagnostics.AddRange(project.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => Convert(root, project.Name, d)));
            }
            valid = !diagnostics.Any(d => d.Severity == "Error") && corpus.Length > 0;
            if (valid)
            {
                foreach (var project in corpus.Where(p => !p.IsTooling))
                {
                    var found = await project.Compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(
                            new ModernizationAnalyzer(EvaluatorConfiguration.Selection.Includes)))
                        .GetAnalyzerDiagnosticsAsync();
                    diagnostics.AddRange(found.Select(d => Convert(root, project.Name, d)));
                }
                var analyzer = new OutcomeAnalyzer(corpus.Select(p => new AssessmentProject(p.Path, p.Compilation,
                    p.IsTooling, p.IsTest, p.GeneratedPaths)).ToArray(), EvaluatorConfiguration.Selection.Includes,
                    EvaluatorConfiguration.Selection.IncludesSource, contracts: true);
                var scenario = new InputFile(Path.Combine(EvaluatorConfiguration.AssessmentRoot, "ScenarioScope.xml.assessment"),
                    await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ScenarioScope.xml")));
                var options = new AnalyzerOptions(corpus.SelectMany(p => p.AdditionalFiles).Append(scenario).DistinctBy(f => f.Path).ToImmutableArray());
                additionalPaths.Add(Relative(root, scenario.Path));
                var outcomeDiagnostics = await corpus.Last().Compilation.WithAnalyzers([analyzer], options).GetAnalyzerDiagnosticsAsync();
                diagnostics.AddRange(outcomeDiagnostics.Select(d => Convert(root, "corpus", d)));
                foreach (var entry in analyzer.Metrics) metrics.Add(entry.Key, entry.Value);
                foreach (var rule in new[] { "MOD001", "MOD002", "MOD003", "MOD004", "MOD005", "MOD006", "BUS001", "BUS002" })
                    metrics[rule] = new(sourcePaths.Count, 0, 0, diagnostics.Count(d => d.Id == rule));
                if (diagnostics.Any(d => d.Id == "AD0001")) valid = false;
                if (diagnostics.Any(d => d.Id == "ASM001")) valid = false;
            }
        }
        catch (SourceCompilationException exception)
        {
            valid = false;
            diagnostics.AddRange(exception.Diagnostics.Select(d => Convert(root, exception.Project, d)));
        }
        catch (Exception exception)
        {
            valid = false;
            diagnostics.Add(new("LOAD001", "Error", "", "", 0, 0, exception.GetType().Name + ": " + exception.Message));
        }
        var report = new ScanReport(valid, projects.Order(StringComparer.Ordinal).ToArray(),
            diagnostics.Distinct().OrderBy(d => d.Id, StringComparer.Ordinal).ThenBy(d => d.Path, StringComparer.Ordinal)
                .ThenBy(d => d.Line).ThenBy(d => d.Column).ThenBy(d => d.Message, StringComparer.Ordinal).ToArray())
        {
            ArchitectureTypes = architectureTypes.Distinct().Order(StringComparer.Ordinal).ToArray(),
            SourcePaths = sourcePaths.Distinct().Order(StringComparer.Ordinal).ToArray(),
            AdditionalPaths = additionalPaths.Distinct().Order(StringComparer.Ordinal).ToArray(),
            Metrics = metrics
        };
        return await WriteReport(root, report);
    }

    private static async Task<ScanReport> WriteReport(string root, ScanReport report)
    {
        var output = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "Reports");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "roslyn-diagnostics.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return report;
    }

    internal static ScanDiagnostic Convert(string root, string project, Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        return new(diagnostic.Id, diagnostic.Id == "AD0001" ? "Error" : diagnostic.Severity.ToString(), project,
            !string.IsNullOrEmpty(span.Path) ? Relative(root, span.Path) : "",
            !string.IsNullOrEmpty(span.Path) ? span.StartLinePosition.Line + 1 : 0,
            !string.IsNullOrEmpty(span.Path) ? span.StartLinePosition.Character + 1 : 0,
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }
    private static string Relative(string root, string path) =>
        string.IsNullOrEmpty(path) ? "" : Path.GetRelativePath(root, path).Replace('/', '\\');
    private static string FindRoot() => EvaluatorConfiguration.SourceRoot;
    private static IEnumerable<string> DiscoverProjects(string root)
    {
        if (Path.GetFullPath(root).TrimEnd('\\').Equals(EvaluatorConfiguration.AssessmentRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            yield break;
        foreach (var file in Directory.EnumerateFiles(root, "*.*proj").Order(StringComparer.Ordinal))
            if (Path.GetExtension(file) is ".csproj" or ".vbproj" &&
                !Path.GetFileNameWithoutExtension(file).Split('.').Any(s => s.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)))
                yield return file;
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.') || name is "bin" or "obj" or "Artifacts" or "packages" ||
                name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var project in DiscoverProjects(directory)) yield return project;
        }
    }
}

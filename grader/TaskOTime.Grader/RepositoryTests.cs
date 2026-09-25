using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ExternalEvaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Modernization.Analyzers.Tests;

public sealed record ScanDiagnostic(
    string Id,
    string Severity,
    string Project,
    string Path,
    int Line,
    int Column,
    string Message);

public sealed record ScanReport(bool CompilationValid, string[] Projects, ScanDiagnostic[] Diagnostics)
{
    public string RubricVersion { get; init; } = "universal-v1";
    public bool EvaluationValid { get; init; }
    public bool DefinitionOfDonePassed { get; init; }
    public double OverallScore { get; init; }
    public int UnverifiedCount { get; init; }
    public bool SourceOverrideUsed { get; init; }
    public ProjectState[] EvaluatedProjects { get; init; } = [];
    public CriterionResult[] Criteria { get; init; } = [];
    public ScanDiagnostic[] DefinitionOfDoneDiagnostics { get; init; } = [];
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
    public async Task Repository_compiles_and_grade_is_written()
    {
        var report = await Scan.Value;
        Assert.True(report.CompilationValid, Evidence(report.Diagnostics.Where(d => d.Severity == "Error")));
        Assert.True(report.EvaluationValid, Evidence(report.Diagnostics));
        Assert.InRange(report.OverallScore, 0, 1);
        Assert.True(File.Exists(Path.Combine(ReportRoot, "repository-assessment.csv")));
        Assert.True(File.Exists(Path.Combine(ReportRoot, "roslyn-diagnostics.json")));
    }

    [Fact]
    [Trait("Category", "DefinitionOfDone")]
    public async Task Production_meets_the_shared_definition_of_done()
    {
        var report = await Scan.Value;
        Assert.True(report.EvaluationValid, Evidence(report.Diagnostics.Where(d => d.Severity == "Error")));
        Assert.True(report.DefinitionOfDonePassed, Evidence(report.DefinitionOfDoneDiagnostics));
        Assert.Equal(1d, report.OverallScore, 6);
    }

    private static string Evidence(IEnumerable<ScanDiagnostic> diagnostics) => string.Join(
        Environment.NewLine,
        diagnostics.Select(d => $"{d.Id} {d.Path}({d.Line},{d.Column}): {d.Message}"));

    private static async Task<ScanReport> LoadAndScan()
    {
        try
        {
            return await LoadWorkspace();
        }
        catch (Exception exception)
        {
            return await WriteReport(new(false, [],
                [new("LOAD001", "Error", "", "", 0, 0, exception.GetType().Name + ": " + exception.Message)]));
        }
    }

    private static async Task<ScanReport> LoadWorkspace()
    {
        var root = EvaluatorConfiguration.SourceRoot;
        var diagnostics = new List<ScanDiagnostic>();
        var projects = new List<string>();
        var architectureTypes = new List<string>();
        var sourcePaths = new List<string>();
        var additionalPaths = new List<string>();
        var metrics = new SortedDictionary<string, CriterionMetric>(StringComparer.Ordinal);
        var evaluated = new List<ProjectState>();

        try
        {
            var loader = new CompilerInputLoader(
                Path.Combine(EvaluatorConfiguration.ArtifactRoot, "compiler-inputs"),
                EvaluatorConfiguration.BuildConfiguration,
                "universal-final");
            var projectPaths = SolutionProjectPaths(root);
            if (projectPaths.Length == 0)
                throw new InvalidDataException("The application solution project graph is empty: " + root);

            foreach (var projectPath in projectPaths) await loader.Load(projectPath);
            var productionRoots = loader.Projects.Where(project =>
                    !project.IsTest && !Segments(Path.GetRelativePath(root, project.Path))
                        .Any(segment => segment.EndsWith("Tests", StringComparison.Ordinal)))
                .Select(project => project.Path)
                .ToArray();
            var loaded = CompilerInputLoader.ClassifyTestSupport(
                loader.Projects, root, productionRoots: productionRoots);
            evaluated.AddRange(loaded.Select(project => project.State!).Where(state => state != null));

            foreach (var project in loaded)
            {
                var compilerErrors = project.Compilation.GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => Convert(root, project.Name, diagnostic));
                diagnostics.AddRange(compilerErrors);
            }

            var corpus = loaded.Where(project => !project.IsTest && !project.IsTooling).ToArray();
            if (corpus.Length == 0)
                diagnostics.Add(new("SCP001", "Error", "", "", 0, 0, "The production project graph is empty."));

            foreach (var project in corpus)
            {
                projects.Add(Relative(root, project.Path));
                sourcePaths.AddRange(project.Compilation.SyntaxTrees.Select(tree => Relative(root, tree.FilePath)));
                additionalPaths.AddRange(project.AdditionalFiles.Select(file => Relative(root, file.Path)));
                architectureTypes.AddRange(new PresentationScope(project.Compilation).Types
                    .Where(EvaluatorConfiguration.Selection.Includes)
                    .Select(type => type.ToDisplayString()));
            }

            var compilationValid = !diagnostics.Any(diagnostic => diagnostic.Severity == "Error") &&
                corpus.Length > 0;
            if (compilationValid)
            {
                foreach (var project in corpus)
                {
                    var found = await project.Compilation.WithAnalyzers(
                            ImmutableArray.Create<DiagnosticAnalyzer>(
                                new ModernizationAnalyzer(EvaluatorConfiguration.Selection.Includes),
                                new CommentLanguageAnalyzer()))
                        .GetAnalyzerDiagnosticsAsync();
                    diagnostics.AddRange(found.Select(diagnostic => Convert(root, project.Name, diagnostic)));
                }

                var analyzer = new OutcomeAnalyzer(
                    corpus.Select(project => new AssessmentProject(
                        project.Path, project.Compilation, false, false, project.GeneratedPaths)).ToArray(),
                    EvaluatorConfiguration.Selection.Includes,
                    EvaluatorConfiguration.Selection.IncludesSource,
                    contracts: true);
                var scenario = new InputFile(
                    Path.Combine(AppContext.BaseDirectory, "ScenarioScope.xml.assessment"),
                    await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ScenarioScope.xml")));
                var options = new AnalyzerOptions(corpus
                    .SelectMany(project => project.AdditionalFiles)
                    .Append(scenario)
                    .DistinctBy(file => file.Path)
                    .ToImmutableArray());
                additionalPaths.Add(Relative(root, scenario.Path));
                var outcomeDiagnostics = await corpus.Last().Compilation
                    .WithAnalyzers([analyzer], options)
                    .GetAnalyzerDiagnosticsAsync();
                diagnostics.AddRange(outcomeDiagnostics.Select(diagnostic => Convert(root, "corpus", diagnostic)));
                foreach (var entry in analyzer.Metrics) metrics[entry.Key] = entry.Value;
                foreach (var rule in new[]
                         {
                             "MOD001", "MOD002", "MOD003", "MOD004", "MOD005", "MOD006", "BUS001", "BUS002"
                         })
                    metrics[rule] = new(1, diagnostics.Any(d => d.Id == rule) ? 0 : 1, 0,
                        diagnostics.Count(d => d.Id == rule));
            }

            var orderedDiagnostics = diagnostics.Distinct()
                .OrderBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Line)
                .ThenBy(diagnostic => diagnostic.Column)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ToArray();
            return await WriteReport(new(compilationValid,
                projects.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray(),
                orderedDiagnostics)
            {
                ArchitectureTypes = architectureTypes.Distinct().Order(StringComparer.Ordinal).ToArray(),
                SourcePaths = sourcePaths.Distinct().Order(StringComparer.Ordinal).ToArray(),
                AdditionalPaths = additionalPaths.Distinct().Order(StringComparer.Ordinal).ToArray(),
                EvaluatedProjects = evaluated.OrderBy(project => project.Path, StringComparer.Ordinal).ToArray(),
                Metrics = metrics
            });
        }
        catch (SourceCompilationException exception)
        {
            diagnostics.AddRange(exception.Diagnostics.Select(diagnostic =>
                Convert(root, exception.Project, diagnostic)));
            return await WriteReport(new(false, projects.ToArray(), diagnostics.ToArray()));
        }
    }

    private static string[] SolutionProjectPaths(string root)
    {
        var solution = XDocument.Load(Path.Combine(root, "TaskOTime.slnx"));
        var pending = new Queue<string>(solution.Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.Combine(root, path!))));
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.TryDequeue(out var project))
        {
            var relative = Path.GetRelativePath(root, project);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.GetExtension(project) is not (".csproj" or ".vbproj") || !File.Exists(project) ||
                !projects.Add(project))
                continue;

            var directory = Path.GetDirectoryName(project)!;
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants()
                         .Where(element => element.Name.LocalName == "ProjectReference")
                         .Select(element => element.Attribute("Include")?.Value)
                         .Where(path => !string.IsNullOrWhiteSpace(path)))
                pending.Enqueue(Path.GetFullPath(Path.Combine(directory, reference!)));
        }

        return projects.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<ScanReport> WriteReport(ScanReport report)
    {
        Directory.CreateDirectory(ReportRoot);
        var valid = IsValid(report);
        var rows = Rubric(report.Diagnostics, valid);
        var definitionDiagnostics = report.Diagnostics;
        var score = valid
            ? rows.Sum(row => row.Weight * row.Score) / rows.Sum(row => row.Weight)
            : 0d;
        var criteria = rows.Select(row => new CriterionResult(
            row.Id,
            row.Name,
            row.Weight,
            valid ? row.Score : null,
            !valid ? "INVALID" : row.Score == 1 ? "PASS" : row.Score == 0 ? "FAIL" : "PARTIAL",
            row.Evidence)).ToArray();
        report = report with
        {
            EvaluationValid = valid,
            DefinitionOfDonePassed = valid && definitionDiagnostics.Length == 0 && rows.All(row => row.Score == 1),
            DefinitionOfDoneDiagnostics = definitionDiagnostics,
            Criteria = criteria,
            OverallScore = score,
            SourceOverrideUsed = EvaluatorConfiguration.SourceOverrideUsed,
            UnverifiedCount = Math.Max(
                report.Diagnostics.Count(diagnostic => diagnostic.Id == "THM002"),
                report.Metrics.Values.Sum(metric => metric.Unverified))
        };

        await File.WriteAllTextAsync(
            Path.Combine(ReportRoot, "roslyn-diagnostics.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
            Environment.NewLine);
        var csv = new StringBuilder(
            "rubric_version,criterion_id,criterion_name,weight,score,weighted_points,status,evidence\r\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(",", new[]
            {
                report.RubricVersion,
                row.Id,
                row.Name,
                row.Weight.ToString("0.0", CultureInfo.InvariantCulture),
                row.Score.ToString("0.000", CultureInfo.InvariantCulture),
                (row.Weight * row.Score).ToString("0.000", CultureInfo.InvariantCulture),
                criteria.Single(criterion => criterion.Id == row.Id).Status,
                row.Evidence
            }.Select(Csv)));
        csv.AppendLine(string.Join(",", new[]
        {
            report.RubricVersion,
            "OVERALL",
            "Universal final-outcome score",
            rows.Sum(row => row.Weight).ToString("0.0", CultureInfo.InvariantCulture),
            report.OverallScore.ToString("0.000", CultureInfo.InvariantCulture),
            report.OverallScore.ToString("0.000", CultureInfo.InvariantCulture),
            !report.EvaluationValid ? "INVALID" : report.DefinitionOfDonePassed ? "PASS" : "FAIL",
            $"evaluation-valid={report.EvaluationValid}; definition-of-done={report.DefinitionOfDonePassed}; " +
            $"unverified={report.UnverifiedCount}; source-override={report.SourceOverrideUsed}"
        }.Select(Csv)));
        await File.WriteAllTextAsync(Path.Combine(ReportRoot, "repository-assessment.csv"), csv.ToString());
        return report;
    }

    private sealed record RubricRow(string Id, string Name, double Weight, double Score, string Evidence);

    internal static bool IsValid(ScanReport report) =>
        report.CompilationValid &&
        report.Projects.Length > 0 &&
        !report.Diagnostics.Any(diagnostic =>
            diagnostic.Id is "ASM001" or "AD0001" or "LOAD001" or "SCP001");

    internal static double FullScore(ScanDiagnostic[] diagnostics, bool valid)
    {
        var rows = Rubric(diagnostics, valid);
        return valid ? rows.Sum(row => row.Weight * row.Score) / rows.Sum(row => row.Weight) : 0d;
    }

    private static RubricRow[] Rubric(ScanDiagnostic[] diagnostics, bool valid)
    {
        int Count(params string[] ids) =>
            diagnostics.Count(diagnostic => ids.Contains(diagnostic.Id, StringComparer.Ordinal));
        double Clear(params string[] ids) => valid && Count(ids) == 0 ? 1d : 0d;
        double Average(params string[] ids) => ids.Select(id => Clear(id)).Average();
        string EvidenceFor(params string[] ids) =>
            string.Join("; ", ids.Select(id => id + "=" + Count(id)));

        return
        [
            new("BUS", "Business correctness", 28,
                Average("BUS001", "BUS002"), EvidenceFor("BUS001", "BUS002")),
            new("MVVM", "WPF MVVM architecture", 18,
                Average("MOD001", "MOD002", "MOD003", "MOD004", "MOD005", "MOD006", "COR001"),
                EvidenceFor("MOD001", "MOD002", "MOD003", "MOD004", "MOD005", "MOD006", "COR001")),
            new("LOC", "Microsoft.Extensions.Localization and required UI", 14,
                Average("LOC001", "LOC002"), EvidenceFor("LOC001", "LOC002")),
            new("LNG", "Production VB to C#", 9,
                Clear("LNG001"), EvidenceFor("LNG001")),
            new("THM", "Theme coverage", 9,
                Average("THM001", "THM002"), EvidenceFor("THM001", "THM002")),
            new("ENG", "English comments and documentation", 5,
                Clear("ENG001") * .7 + Clear("ENG002") * .3, EvidenceFor("ENG001", "ENG002")),
            new("NAM", "Main Data naming", 5,
                Clear("NAM001"), EvidenceFor("NAM001")),
            new("SDK", "SDK-style projects", 3.5,
                Clear("PRJ001"), EvidenceFor("PRJ001")),
            new("NET10", ".NET 10 target", 3.5,
                Clear("PRJ002"), EvidenceFor("PRJ002"))
        ];
    }

    internal static ScanDiagnostic Convert(string root, string project, Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        return new(
            diagnostic.Id,
            diagnostic.Id == "AD0001" ? "Error" : diagnostic.Severity.ToString(),
            project,
            !string.IsNullOrEmpty(span.Path) ? Relative(root, span.Path) : "",
            !string.IsNullOrEmpty(span.Path) ? span.StartLinePosition.Line + 1 : 0,
            !string.IsNullOrEmpty(span.Path) ? span.StartLinePosition.Character + 1 : 0,
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    private static string[] Segments(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    private static string Relative(string root, string path) =>
        string.IsNullOrEmpty(path) ? "" : Path.GetRelativePath(root, path).Replace('/', '\\');

    private static string ReportRoot => Path.Combine(
        EvaluatorConfiguration.ArtifactRoot,
        "Reports",
        EvaluatorConfiguration.BuildConfiguration);

    internal static async Task LoadProjectSet(
        IEnumerable<string> discovered,
        IEnumerable<string> declared,
        Func<string, Task<LoadedProject>> load,
        IEnumerable<FixtureDataRole>? fixtureRoles = null)
    {
        var required = declared.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = (fixtureRoles ?? []).Where(role => Directory.Exists(role.Root)).ToArray();
        foreach (var role in roles)
        {
            if (!File.Exists(role.Owner))
                throw new InvalidDataException("Missing fixture-data owner: " + role.Owner);
            var owner = await load(role.Owner);
            var errors = owner.Compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            if (errors.Length > 0) throw new SourceCompilationException(role.Owner, errors);
            role.Validate(owner);
        }

        foreach (var project in discovered.Concat(required)
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.Ordinal))
        {
            if (!required.Contains(project) &&
                roles.Any(role => FixtureDataRole.Contains(role.Root, project)))
                continue;
            if (!File.Exists(project) ||
                Path.GetExtension(project).ToLowerInvariant() is not (".csproj" or ".vbproj"))
                throw new InvalidDataException("Missing or unsupported project: " + project);
            var loaded = await load(project);
            if (!required.Contains(project)) continue;
            var errors = loaded.Compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            if (errors.Length > 0) throw new SourceCompilationException(project, errors);
        }
    }

    internal static IEnumerable<string> DiscoverProjects(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.*proj").Order(StringComparer.Ordinal))
            if (Path.GetExtension(file) is ".csproj" or ".vbproj")
                yield return file;
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.') ||
                new[] { "bin", "obj", "Artifacts", "packages", "grader" }
                    .Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            foreach (var project in DiscoverProjects(directory)) yield return project;
        }
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}

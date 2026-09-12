using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using ExternalEvaluation;

namespace Modernization.Analyzers.Tests;

public sealed record ScanDiagnostic(string Id, string Severity, string Project, string Path, int Line, int Column, string Message);
public sealed record ScanReport(bool CompilationValid, string[] Projects, ScanDiagnostic[] Diagnostics)
{
    public string RubricVersion { get; init; } = "2026-09-stage-v4";
    public bool EvaluationValid { get; init; }
    public bool HardGatePassed { get; init; }
    public bool FullModernizationPassed { get; init; }
    public int UnverifiedCount { get; init; }
    public double OverallScore { get; init; }
    public double? RemainingWorkScore { get; init; }
    public double ApplicableWeight { get; init; }
    public string StartingStage { get; init; } = "";
    public string Mode { get; init; } = "";
    public bool StageIntegrityPassed { get; init; }
    public string[] StageIntegrityFailures { get; init; } = [];
    public ProjectState[] EvaluatedProjects { get; init; } = [];
    public CriterionResult[] Criteria { get; init; } = [];
    public ScanDiagnostic[] AcceptanceDiagnostics { get; init; } = [];
    public ReplayResult? Replay { get; init; }
    public string AcceptanceBasis { get; init; } = "static-quality-only";
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
        Assert.True(report.EvaluationValid, Evidence(report.Diagnostics));
        Assert.InRange(report.OverallScore, 0, 1);
        Assert.True(File.Exists(Path.Combine(ReportRoot, "repository-assessment.csv")));
    }

    [Fact]
    [Trait("Category", "StagePreservation")]
    public async Task Starting_point_preserves_stage_contract_and_intentional_defects()
    {
        Assert.Equal(EvaluationMode.StartingPointIntegrity, StagePolicy.Current.Mode);
        var report = await Scan.Value;
        Assert.True(report.EvaluationValid, Evidence(report.Diagnostics));
        Assert.True(report.StageIntegrityPassed, string.Join(Environment.NewLine, report.StageIntegrityFailures));
    }

    [Fact]
    [Trait("Category", "Modernization")]
    public async Task Production_meets_all_modernization_and_business_criteria()
    {
        var report = await Scan.Value;
        Assert.Equal(EvaluationMode.FinalDelivery, StagePolicy.Current.Mode);
        Assert.True(report.EvaluationValid, Evidence(report.Diagnostics.Where(d => d.Severity == "Error")));
        Assert.True(report.AcceptanceDiagnostics.Length == 0, Evidence(report.AcceptanceDiagnostics));
        Assert.True(report.HardGatePassed);
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
        var evaluated = new List<ProjectState>();
        var valid = false;
        try
        {
            var policy = StagePolicy.Current;
            ReferenceReplay.ValidateBeforeProjectEvaluation();
            var loader = new CompilerInputLoader(Path.Combine(EvaluatorConfiguration.ArtifactRoot, "compiler-inputs"),
                EvaluatorConfiguration.BuildConfiguration, policy.Identity);
            foreach (var directory in new[] { "TaskOTime.App" })
            {
                var projectPath = Directory.EnumerateFiles(Path.Combine(root, directory), "*.*proj")
                    .Single(p => Path.GetExtension(p) is ".csproj" or ".vbproj");
                await loader.Load(projectPath);
            }
            await LoadProjectSet(EvaluatorConfiguration.DiscoveryRoots.SelectMany(DiscoverProjects),
                ToolReplay.DeclaredProjects, loader.Load,
                FixtureDataRole.Read(System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ScenarioScope.xml")), root));
            var loaded = CompilerInputLoader.ClassifyTestSupport(loader.Projects, root);
            evaluated.AddRange(loaded.Select(p => p.State!).Where(p => p != null));
            diagnostics.AddRange(loaded.Where(p => p.IsTest).SelectMany(p =>
                p.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => Convert(root, p.Name, d))));
            var corpus = loaded.Where(p => !p.IsTest).ToArray();
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
                            new ModernizationAnalyzer(EvaluatorConfiguration.Selection.Includes),
                            new CommentLanguageAnalyzer()))
                        .GetAnalyzerDiagnosticsAsync();
                    diagnostics.AddRange(found.Select(d => Convert(root, project.Name, d)));
                }
                var analyzer = new OutcomeAnalyzer(corpus.Select(p => new AssessmentProject(p.Path, p.Compilation,
                    p.IsTooling, p.IsTest, p.GeneratedPaths)).ToArray(), EvaluatorConfiguration.Selection.Includes,
                    EvaluatorConfiguration.Selection.IncludesSource, contracts: true);
                var scenario = new InputFile(Path.Combine(EvaluatorConfiguration.AssessmentRoot, "ScenarioScope.xml.assessment"),
                    ScenarioText(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ScenarioScope.xml")), policy));
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
            EvaluatedProjects = evaluated.OrderBy(p => p.Path, StringComparer.Ordinal).ToArray(),
            Metrics = metrics
        };
        return await WriteReport(root, report);
    }

    private static async Task<ScanReport> WriteReport(string root, ScanReport report)
    {
        var output = ReportRoot;
        Directory.CreateDirectory(output);
        var policy = StagePolicy.Current;
        var replay = report.CompilationValid ? await ToolReplay.Run(policy, EvaluatorConfiguration.ArtifactRoot) :
            new ReplayResult(false, [], "Input invalid; replay not executed.");
        var allDiagnostics = report.Diagnostics.ToList();
        var toolAccepted = replay.Verified || replay.ReferenceVerified;
        if (policy.ProjectReplay && !toolAccepted)
            allDiagnostics.Add(new("TOOL002", "Warning", "replay", "", 0, 0,
                replay.Message + " " + string.Join("; ", replay.Cases.Where(c => !c.Passed).Select(c => c.Name + ": " + c.Message))));
        report = report with { Diagnostics = allDiagnostics.ToArray(), Replay = replay };
        // TOOL001 remains visible as bounded static evidence. Actual replay, not a
        // converter's source shape, determines tool acceptance (including genuine wrappers).
        var acceptance = report.Diagnostics.Where(d => d.Id != "TOOL001").ToArray();
        if (policy.Mode == EvaluationMode.FinalDelivery)
        {
            var stateFailures = new StagePolicy(StartingStage.S4, policy.Mode).CheckIntegrity(report.EvaluatedProjects, []);
            acceptance = acceptance.Concat(stateFailures.Select(message =>
                new ScanDiagnostic("STG001", "Warning", "profile", "", 0, 0, message))).ToArray();
            report = report with { Diagnostics = report.Diagnostics.Concat(acceptance.Where(d => d.Id == "STG001")).ToArray() };
        }
        var valid = IsValid(report);
        var rows = Rubric(report.Diagnostics, valid, toolAccepted);
        var criteria = CriteriaFor(report.Diagnostics, policy, valid, toolAccepted);
        var applicableWeight = criteria.Where(c => c.Applicability == "applicable").Sum(c => c.Weight);
        var integrity = policy.CheckIntegrity(report.EvaluatedProjects, acceptance);
        report = report with
        {
            EvaluationValid = valid,
            AcceptanceBasis = replay.ReferenceVerified ? "owned-reference-reviewed; NOT formal candidate isolation" :
                replay.Verified ? "external-replay-attested; whole-scan isolation requires executor custody" : "static-quality-only",
            HardGatePassed = valid && policy.Mode == EvaluationMode.FinalDelivery && acceptance.Length == 0,
            FullModernizationPassed = valid && acceptance.Length == 0 && rows.All(r => r.Score == 1),
            AcceptanceDiagnostics = acceptance,
            StartingStage = policy.Stage.ToString(), Mode = policy.Mode.ToString(),
            StageIntegrityPassed = valid && integrity.Length == 0, StageIntegrityFailures = integrity,
            Criteria = criteria, ApplicableWeight = applicableWeight,
            RemainingWorkScore = valid && applicableWeight > 0 ?
                criteria.Where(c => c.Applicability == "applicable").Sum(c => c.Weight * c.Score!.Value) / applicableWeight : null,
            UnverifiedCount = Math.Max(report.Diagnostics.Count(d => d.Id == "THM002"),
                report.Metrics.Values.Sum(m => m.Unverified)),
            OverallScore = FullScore(report.Diagnostics, valid, toolAccepted)
        };
        await File.WriteAllTextAsync(Path.Combine(output, "roslyn-diagnostics.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        var csv = new StringBuilder("rubric_version,criterion_id,criterion_name,weight,full_outcome_score,full_weighted_contribution,status,evidence,applicability,starting_state,acceptance_basis\r\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(",", new[] { report.RubricVersion, row.Id, row.Name,
                row.Weight.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                row.Score.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                (row.Weight * row.Score / 100d).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                criteria.Single(c => c.Id == row.Id).Status, row.Evidence,
                policy.Applicability(row.Id), policy.StartingState(row.Id), report.AcceptanceBasis }.Select(Csv)));
        csv.AppendLine(string.Join(",", new[] { report.RubricVersion, "OVERALL", "Normalized overall score", "100.0",
            report.OverallScore.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            report.OverallScore.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
            !report.EvaluationValid ? "INVALID" : report.HardGatePassed ? "PASS" : "FAIL",
            $"evaluation-valid={report.EvaluationValid}; hard-gate={report.HardGatePassed}; unverified={report.UnverifiedCount}; stage={policy.Stage}; mode={policy.Mode}; applicable-weight={applicableWeight}; remaining-score={report.RemainingWorkScore}",
            "full-outcome", policy.Stage.ToString(), report.AcceptanceBasis }.Select(Csv)));
        await File.WriteAllTextAsync(Path.Combine(output, "repository-assessment.csv"), csv.ToString());
        return report;
    }

    private sealed record RubricRow(string Id, string Name, double Weight, double Score, string Evidence);
    internal static bool IsValid(ScanReport report) => report.CompilationValid && report.Projects.Length > 0 &&
        !report.Diagnostics.Any(d => d.Id is "ASM001" or "AD0001" or "LOAD001" or "SCP001");
    internal static double FullScore(ScanDiagnostic[] diagnostics, bool valid, bool replayVerified) =>
        Rubric(diagnostics, valid, replayVerified).Sum(r => r.Weight * r.Score) / 100d;
    internal static CriterionResult[] CriteriaFor(ScanDiagnostic[] diagnostics, StagePolicy policy, bool valid, bool replayVerified) =>
        Rubric(diagnostics, valid, replayVerified).Select(r =>
        {
            var applicability = policy.Applicability(r.Id);
            return new CriterionResult(r.Id, r.Weight, valid ? r.Score : null, applicability,
                !valid ? "INVALID" : applicability == "deferred" ? "DEFERRED" :
                applicability == "not-applicable" ? "NOT_APPLICABLE" :
                applicability == "pre-satisfied" ? r.Score == 1 ? "PRE_SATISFIED" : "PRE_SATISFIED_REGRESSION" :
                r.Score == 1 ? "PASS" : r.Score == 0 ? "FAIL" : "PARTIAL", policy.StartingState(r.Id));
        }).ToArray();
    internal static string ScenarioText(string text, StagePolicy policy)
    {
        var document = System.Xml.Linq.XDocument.Parse(text);
        if (policy.Mode == EvaluationMode.FinalDelivery || policy.Stage >= StartingStage.S3)
            document.Root!.Element("EF6")!.SetAttributeValue("PackageVersion", "6.5.2");
        return document.ToString();
    }
    private static RubricRow[] Rubric(ScanDiagnostic[] diagnostics, bool valid, bool replayVerified)
    {
        int Count(params string[] ids) => diagnostics.Count(d => ids.Contains(d.Id, StringComparer.Ordinal));
        double Clear(params string[] ids) => valid && Count(ids) == 0 ? 1d : 0d;
        string EvidenceFor(params string[] ids)
        {
            var counts = ids.Select(id => id + "=" + diagnostics.Count(d => d.Id == id));
            return string.Join("; ", counts);
        }
        return
        [
            new("BUS", "Business correctness", 28, (Clear("BUS001") + Clear("BUS002")) / 2, EvidenceFor("BUS001", "BUS002")),
            new("MVVM", "WPF MVVM architecture", 18, Clear("MOD001", "MOD002", "MOD003", "MOD004", "MOD005", "MOD006", "COR001"),
                EvidenceFor("MOD001", "MOD002", "MOD003", "MOD004", "MOD005", "MOD006", "COR001")),
            new("LOC", "Microsoft.Extensions.Localization and required UI", 14, Clear("LOC001", "LOC002"), EvidenceFor("LOC001", "LOC002")),
            new("LNG", "Production VB to C#", 9, Clear("LNG001"), EvidenceFor("LNG001")),
            new("THM", "Theme coverage", 9, Clear("THM001", "THM002"), EvidenceFor("THM001", "THM002")),
            new("ENG", "English comments and documentation", 5,
                Clear("ENG001") * .7 + Clear("ENG002") * .3, EvidenceFor("ENG001", "ENG002")),
            new("NAM", "Main Data naming", 5, Clear("NAM001"), EvidenceFor("NAM001")),
            new("TOOL", "Independently replayed reusable migration tool", 5, valid && replayVerified ? 1 : 0, EvidenceFor("TOOL001", "TOOL002")),
            new("SDK", "SDK-style projects", 3.5, Clear("PRJ001"), EvidenceFor("PRJ001")),
            new("NET10", ".NET 10 target", 3.5, Clear("PRJ002"), EvidenceFor("PRJ002"))
        ];
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

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
    private static string ReportRoot => Path.Combine(EvaluatorConfiguration.ArtifactRoot, "Reports", StagePolicy.Current.Identity);
    internal static async Task LoadProjectSet(IEnumerable<string> discovered, IEnumerable<string> declared,
        Func<string, Task<LoadedProject>> load, IEnumerable<FixtureDataRole>? fixtureRoles = null)
    {
        var required = declared.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = (fixtureRoles ?? []).Where(role => Directory.Exists(role.Root)).ToArray();
        foreach (var role in roles)
        {
            if (!File.Exists(role.Owner)) throw new InvalidDataException("Missing trusted fixture-data owner: " + role.Owner);
            var owner = await load(role.Owner);
            var errors = owner.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0) throw new SourceCompilationException(role.Owner, errors);
            role.Validate(owner);
        }
        foreach (var project in discovered.Concat(required).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            // Only standalone discovery is suppressed. The loader still follows every
            // project-reference edge, and declared producers are never fixture data.
            if (!required.Contains(project) && roles.Any(role => FixtureDataRole.Contains(role.Root, project))) continue;
            if (!File.Exists(project) || Path.GetExtension(project).ToLowerInvariant() is not (".csproj" or ".vbproj"))
                throw new InvalidDataException("Missing or unsupported declared/discovered project: " + project);
            var loaded = await load(project);
            if (required.Contains(project))
            {
                var errors = loaded.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                if (errors.Length > 0) throw new SourceCompilationException(project, errors);
            }
        }
    }
    internal static IEnumerable<string> DiscoverProjects(string root)
    {
        if (Path.GetFullPath(root).TrimEnd('\\').Equals(EvaluatorConfiguration.AssessmentRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            yield break;
        foreach (var file in Directory.EnumerateFiles(root, "*.*proj").Order(StringComparer.Ordinal))
            if (Path.GetExtension(file) is ".csproj" or ".vbproj")
                yield return file;
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.') || new[] { "bin", "obj", "Artifacts", "packages" }.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var project in DiscoverProjects(directory)) yield return project;
        }
    }
}

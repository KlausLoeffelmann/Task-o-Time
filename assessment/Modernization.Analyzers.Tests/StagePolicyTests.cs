using System.Text.Json;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "AnalyzerUnit")]
public sealed class StagePolicyTests
{
    [Theory]
    [InlineData("S0", "LNG", "applicable")]
    [InlineData("S1", "SDK", "applicable")]
    [InlineData("S2", "LNG", "pre-satisfied")]
    [InlineData("S2", "TOOL", "applicable")]
    [InlineData("S2a", "SDK", "pre-satisfied")]
    [InlineData("S3", "NET10", "pre-satisfied")]
    [InlineData("S3", "TOOL", "not-applicable")]
    [InlineData("S4", "TOOL", "applicable")]
    public void Final_applicability_is_not_a_fabricated_pass(string stage, string criterion, string expected) =>
        Assert.Equal(expected, StagePolicy.Parse(stage, "final-delivery").Applicability(criterion));

    [Theory]
    [InlineData("S0")]
    [InlineData("S1")]
    [InlineData("S2")]
    [InlineData("S2a")]
    [InlineData("S3")]
    public void Integrity_defers_quality_but_still_requires_preservation(string stage)
    {
        var policy = StagePolicy.Parse(stage, "starting-point-integrity");
        Assert.Equal("deferred", policy.Applicability("BUS"));
        Assert.Contains(policy.CheckIntegrity([], []), s => s.Contains("BUS001"));
        Assert.Contains(policy.CheckIntegrity([], []), s => s.Contains("BUS002"));
    }

    [Theory]
    [InlineData("S0", "v4.6.1", "Visual Basic", false)]
    [InlineData("S1", "v4.7.2", "Visual Basic", false)]
    [InlineData("S2", "v4.7.2", "C#", false)]
    [InlineData("S2a", "v4.7.2", "C#", true)]
    [InlineData("S3", "v10.0", "C#", true)]
    [InlineData("S4", "v10.0", "C#", true)]
    public void Integrity_validates_evaluated_language_style_targets_and_tests(string stage, string version, string language, bool sdk)
    {
        var policy = StagePolicy.Parse(stage, "starting-point-integrity");
        var identifier = version == "v10.0" ? ".NETCoreApp" : ".NETFramework";
        var project = new ProjectState("Renamed.proj", language, false, false, sdk, "", identifier, version, "", "Debug", "Debug;Release");
        var diagnostics = stage == "S4" ? [] : new[] { "BUS001", "BUS002", "MOD001", "LOC001", "ENG001", "NAM001", "THM001" }
            .Select(id => new ScanDiagnostic(id, "Warning", "", "", 0, 0, "")).ToArray();
        var projects = new[] { project, project with { Path = "Desktop.csproj", SdkStyle = true, Language = "C#", FrameworkVersion = version == "v4.6.1" ? "v4.7.2" : version } };
        Assert.Empty(policy.CheckIntegrity(projects, diagnostics));
        Assert.NotEmpty(policy.CheckIntegrity(projects.Append(project with { Path = "Tests.vbproj", Test = true, FrameworkVersion = "v2.0" }).ToArray(), diagnostics));
        if (stage is "S2" or "S2a" or "S3" or "S4")
            Assert.Contains(policy.CheckIntegrity([project with { Language = "Visual Basic" }], diagnostics), s => s.Contains("Production must"));
    }

    [Fact]
    public void Invalid_empty_or_failed_evaluations_never_get_perfect_scores()
    {
        Assert.False(RepositoryTests.IsValid(new(true, [], [])));
        Assert.False(RepositoryTests.IsValid(new(false, ["App"], [])));
        Assert.False(RepositoryTests.IsValid(new(true, ["App"], [new("SCP001", "Warning", "", "", 0, 0, "")])));
        Assert.Equal(0, RepositoryTests.FullScore([], false, true));
        Assert.Equal(1, RepositoryTests.FullScore([], true, true));
        Assert.True(RepositoryTests.FullScore([], true, false) < 1);
    }

    [Fact]
    public void Scoring_excludes_precompleted_work_from_denominator_without_hiding_regressions()
    {
        var diagnostics = new[] { new ScanDiagnostic("LNG001", "Warning", "", "", 0, 0, "VB reintroduced") };
        var policy = StagePolicy.Parse("S3", "final-delivery");
        var criteria = RepositoryTests.CriteriaFor(diagnostics, policy, true, false);
        Assert.Equal("PRE_SATISFIED_REGRESSION", criteria.Single(c => c.Id == "LNG").Status);
        Assert.Equal(79, criteria.Where(c => c.Applicability == "applicable").Sum(c => c.Weight));
        Assert.Equal("NOT_APPLICABLE", criteria.Single(c => c.Id == "TOOL").Status);
        Assert.DoesNotContain(criteria.Where(c => c.Applicability != "applicable"), c => c.Status == "PASS");
        Assert.True(RepositoryTests.FullScore(diagnostics, true, false) < .95);
        Assert.All(RepositoryTests.CriteriaFor([], policy, false, false), c =>
        {
            Assert.Null(c.Score);
            Assert.Equal("INVALID", c.Status);
        });
        Assert.All(RepositoryTests.CriteriaFor([], StagePolicy.Parse("S0", "starting-point-integrity"), true, false),
            c => Assert.Equal("DEFERRED", c.Status));
    }

    [Fact]
    public void Configuration_and_project_identity_isolate_compiler_caches()
    {
        var identities = new[] {
            CompilerInputLoader.CacheIdentity(@"C:\a\Same.csproj", "Debug", "S0"),
            CompilerInputLoader.CacheIdentity(@"C:\b\Same.csproj", "Debug", "S0"),
            CompilerInputLoader.CacheIdentity(@"C:\a\Same.csproj", "Release", "S0"),
            CompilerInputLoader.CacheIdentity(@"C:\a\Same.csproj", "Debug", "S3") };
        Assert.Equal(4, identities.Distinct().Count());
    }

    [Theory]
    [InlineData("", ".NETFramework", "v4.7.2", "false")]
    [InlineData("net10.0-windows", ".NETCoreApp", "v10.0", "true")]
    public void Framework_metadata_preserves_legacy_and_sdk_evaluation(string tfm, string identifier, string version, string sdk)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string> {
            ["TargetFramework"] = tfm, ["TargetFrameworkIdentifier"] = identifier, ["TargetFrameworkVersion"] = version,
            ["UsingMicrosoftNETSdk"] = sdk, ["Configuration"] = "Release", ["Configurations"] = "Debug;Release",
            ["TargetPlatformIdentifier"] = "Windows" }));
        var state = CompilerInputLoader.ReadState("app", "C#", false, false, json.RootElement);
        Assert.Equal(identifier, state.FrameworkIdentifier);
        Assert.Equal(version, state.FrameworkVersion);
        Assert.Equal(tfm, state.TargetFramework);
        Assert.Equal("Release", state.Configuration);
        Assert.Equal("Windows", state.Platform);
    }

    [Fact]
    public void Candidate_cannot_supply_unknown_profile_or_replay_expectations()
    {
        Assert.Throws<InvalidDataException>(() => StagePolicy.Parse("disable-business", "final-delivery"));
        Assert.Throws<InvalidDataException>(() => StagePolicy.Parse("S0", "skip"));
        Assert.Throws<InvalidDataException>(() => ToolReplay.TrustedPath(@"C:\candidate\replay.json"));
    }

    [Fact]
    public void Production_dependencies_cannot_hide_in_test_support_directories()
    {
        const string root = @"C:\candidate";
        var compilation = AnalyzerTests.Compile("C#", "public class AnyName {}");
        var support = new LoadedProject(root + @"\Feature.Tests\Doubles\Support.csproj", "Support", "", compilation,
            false, false, [], []);
        var app = new LoadedProject(root + @"\App\App.csproj", "App", "", compilation, false, false, [],
            [new InputFile(root + @"\App\App.csproj.assessment",
                $"""<Project><ProjectReference Path="{support.Path}"/></Project>""")]);
        var test = new LoadedProject(root + @"\Feature.Tests\Fixture.csproj", "Fixture.Tests", "", compilation,
            true, false, [], []);
        Assert.False(CompilerInputLoader.ClassifyTestSupport([app, support, test], root).Single(p => p.Path == support.Path).IsTest);
        Assert.True(CompilerInputLoader.ClassifyTestSupport([support, test], root).Single(p => p.Path == support.Path).IsTest);
    }

    [Fact]
    public void Roslyn_accepts_net10_csharp14_compiler_syntax()
    {
        var compilation = AnalyzerTests.Compile("C#", "public class Candidate { public int Value { get; set => field = value; } }");
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_only_test_helpers_are_classified_by_edges_and_shared_production_dependencies_stay_production(bool shared)
    {
        const string root = @"C:\candidate";
        var compilation = AnalyzerTests.Compile("C#", "public class AnyName {}");
        LoadedProject Project(string directory, bool test, params (string Path, bool Compiler)[] references)
        {
            var path = root + "\\" + directory + @"\Module.csproj";
            var xml = new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Test", test),
                references.Select(r => new System.Xml.Linq.XElement("ProjectReference",
                    new System.Xml.Linq.XAttribute("Path", r.Path), new System.Xml.Linq.XAttribute("ReferenceOutputAssembly", r.Compiler))));
            return new(path, directory, "", compilation, test, false, [], [new InputFile(path + ".assessment", xml.ToString())],
                State: new(path, "C#", test, false, true, "net10.0", ".NETCoreApp", "v10.0", "", "Debug", "Debug"));
        }
        var helper = Project("Unrelated.Process", false);
        var checks = Project("Checks", true, (helper.Path, false));
        var misleadingName = Project("RealProduction.TestHost", false);
        var app = shared ? Project("Application", false, (helper.Path, false)) : Project("Application", false);
        var classified = CompilerInputLoader.ClassifyTestSupport([helper, checks, misleadingName, app], root, [app.Path]);
        Assert.Equal(!shared, classified.Single(p => p.Path == helper.Path).IsTest);
        Assert.Equal(!shared, classified.Single(p => p.Path == helper.Path).State!.Test);
        Assert.False(classified.Single(p => p.Path == misleadingName.Path).IsTest);
        Assert.False(classified.Single(p => p.Path == app.Path).IsTest);
        Assert.False(CompilerInputLoader.ClassifyTestSupport([helper, checks], root, [helper.Path])
            .Single(p => p.Path == helper.Path).IsTest);
    }
}

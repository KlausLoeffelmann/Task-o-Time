using ExternalEvaluation;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "AnalyzerUnit")]
public sealed class ProjectRolesTests : IDisposable
{
    private readonly string root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "project-role-tests", Guid.NewGuid().ToString("N"));
    private string Project(string relative)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project/>");
        return path;
    }

    [Fact]
    public void Discovery_ignores_generated_directories_case_insensitively_but_keeps_fixture_projects()
    {
        var tool = Project(@"tools\Utility.csproj");
        var fixture = Project(@"tools\tests\Fixtures\Legacy.vbproj");
        Project(@"tools\artifacts\copy\ShouldNotLoad.csproj");
        Project(@"tools\ARTIFACTS\Other.csproj");
        Project(@"tools\OBJ\Generated.csproj");
        Project(@"tools\BIN\Generated.csproj");
        Assert.Equal(new[] { tool, fixture }.Order(StringComparer.Ordinal),
            RepositoryTests.DiscoverProjects(Path.Combine(root, "tools")).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Exact_trusted_roles_keep_external_fixtures_out_of_application_stage_targets()
    {
        var app = Project(@"src\App.csproj");
        var fixture = Project(@"tools\tests\Fixtures\Legacy.vbproj");
        var roles = ProjectRoles.Read(new([], [], ProjectRoles: [new(fixture, "fixture")]));
        ProjectRoles.ValidateInventory([app, fixture], Path.Combine(root, "src"), roles);
        ProjectState State(string path, string language, bool test, bool tool, string framework, string version) =>
            new(path, language, test, tool, true, "", framework, version, "", "Debug", "Debug;Release");
        Assert.Empty(StagePolicy.Parse("S4", "final-delivery").CheckIntegrity([
            State(app, "C#", false, false, ".NETCoreApp", "v10.0"),
            State(fixture, "Visual Basic", true, true, ".NETFramework", "v4.7.2") with { Role = "fixture" }
        ], []));
    }

    [Theory]
    [InlineData("unknown-external")]
    [InlineData("application-exemption")]
    [InlineData("missing-fixture")]
    [InlineData("glob")]
    [InlineData("duplicate")]
    [InlineData("candidate-skip")]
    [InlineData("reserved-property")]
    [InlineData("property-injection")]
    [InlineData("duplicate-property")]
    public void Invalid_roles_do_not_become_skips(string variant)
    {
        var app = Project(@"src\App.csproj");
        var fixture = Project(@"tools\Fixture.vbproj");
        var declaration = new ProjectRoleDeclaration(fixture, "fixture");
        if (variant == "application-exemption") declaration = declaration with { Project = app };
        if (variant == "missing-fixture") File.Delete(fixture);
        if (variant == "glob") declaration = declaration with { Project = Path.Combine(root, "tools", "*.vbproj") };
        if (variant == "candidate-skip") declaration = declaration with { Role = "skip" };
        if (variant == "reserved-property") declaration = declaration with { BuildProperties = new() { ["SkipCompilerExecution"] = "true" } };
        if (variant == "property-injection") declaration = declaration with { BuildProperties = new() { ["Flavor"] = "A;SkipCompilerExecution=true" } };
        if (variant == "duplicate-property") declaration = declaration with { BuildProperties = new() { ["Flavor"] = "A", ["flavor"] = "B" } };
        Assert.Throws<InvalidDataException>(() =>
        {
            var roles = ProjectRoles.Read(new([], [], ProjectRoles: variant == "unknown-external" ? [] :
                variant == "duplicate" ? [declaration, declaration] : [declaration]));
            ProjectRoles.ValidateInventory([app, fixture], Path.Combine(root, "src"), roles);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Declared_fixtures_still_require_existing_compilable_source(bool missing)
    {
        var fixture = Project(@"tools\Fixture.csproj");
        var roles = ProjectRoles.Read(new([], [], ProjectRoles: [new(fixture, "fixture")]));
        if (missing) File.Delete(fixture);
        var broken = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Broken",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class Fixture { UnknownType Value; }")]);
        Task Load() => RepositoryTests.LoadProjectSet([], roles.Keys, path =>
            Task.FromResult(new LoadedProject(path, "Fixture", "", broken, true, true, [], [])));
        if (missing) await Assert.ThrowsAsync<InvalidDataException>(Load);
        else await Assert.ThrowsAsync<SourceCompilationException>(Load);
    }

    [Fact]
    public void Production_dependency_cannot_be_hidden_by_fixture_role()
    {
        var app = Project(@"src\App.csproj"); var fixture = Project(@"tools\Fixture.csproj");
        var compilation = AnalyzerTests.Compile("C#", "public class Example {}");
        var xml = new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XElement("ProjectReference",
            new System.Xml.Linq.XAttribute("Path", fixture))).ToString();
        var production = new LoadedProject(app, "App", "", compilation, false, false, [], [new InputFile(app + ".assessment", xml)]);
        var dependency = new LoadedProject(fixture, "Fixture", "", compilation, true, true, [], [],
            State: new(fixture, "C#", true, true, true, "net472", ".NETFramework", "v4.7.2", "", "Debug", "Debug")
            { Role = "fixture" });
        Assert.Throws<InvalidDataException>(() => ProjectRoles.ValidateDependencies([production, dependency]));
    }

    [Fact]
    public void Role_build_properties_isolate_compiler_caches()
    {
        var path = Project(@"tools\Harness.csproj");
        var framework = new ProjectRoleDeclaration(path, "validation", new() { ["ValidationFramework"] = "net472" });
        var modern = framework with { BuildProperties = new() { ["ValidationFramework"] = "net10.0-windows" } };
        Assert.NotEqual(CompilerInputLoader.CacheIdentity(path, "Debug", "S4", framework),
            CompilerInputLoader.CacheIdentity(path, "Debug", "S4", modern));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

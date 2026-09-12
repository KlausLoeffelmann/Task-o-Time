using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "AnalyzerUnit")]
public sealed class SourceReferenceTests
{
    private const string ProjectPath = @"C:\fixture\Library\RenamedLibrary.csproj";
    private const string ImplementationPath = @"C:\fixture\Library\bin\Debug\net10.0\Porter.Library.dll";
    private const string CompilerRefPath = @"C:\fixture\Library\obj\Debug\net10.0\ref\Porter.Library.dll";
    private const string IsolatedRefPath = @"C:\assessment\compiler-inputs\RenamedLibrary\ref\Porter.Library.dll";
    private static LoadedProject Library() => new(ProjectPath, "Porter.Library", ImplementationPath,
        AnalyzerTests.Compile(LanguageNames.CSharp,
            """public static class ConvertedValue { public static string English => "Source-compiled value"; }""", "Porter.Library"),
        false, true, [], [], IsolatedRefPath);

    [Theory]
    [InlineData(OutputKind.ConsoleApplication, false)]
    [InlineData(OutputKind.WindowsApplication, false)]
    [InlineData(OutputKind.ConsoleApplication, true)]
    [InlineData(OutputKind.WindowsApplication, true)]
    public void Integration_test_build_edge_does_not_reclassify_standalone_product(OutputKind kind, bool testDirectory)
    {
        var product = Library() with {
            IsTooling = false,
            Path = testDirectory ? @"C:\fixture\Integration.Tests\Product\Product.csproj" : ProjectPath,
            Compilation = ((CSharpCompilation)AnalyzerTests.Compile(LanguageNames.CSharp,
                "public static class Program { public static void Main() {} }")).WithOptions(new CSharpCompilationOptions(kind))
        };
        var testPath = @"C:\fixture\Integration.csproj";
        var test = new LoadedProject(testPath, "Assertions", "", product.Compilation, true, false, [],
            [new InputFile(testPath + ".assessment",
                $"""<Project><ProjectReference Path="{product.Path}" ReferenceOutputAssembly="false"/></Project>""")]);
        Assert.False(CompilerInputLoader.ClassifyTestSupport([product, test], @"C:\fixture").Single(p => p.Path == product.Path).IsTest);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"ReferenceOutputAssembly\":\"\"}", true)]
    [InlineData("{\"ReferenceOutputAssembly\":\"true\"}", true)]
    [InlineData("{\"ReferenceOutputAssembly\":\"FALSE\"}", false)]
    public void Only_evaluated_false_suppresses_the_compiler_reference_requirement(string json, bool expected)
    {
        using var metadata = JsonDocument.Parse(json);
        Assert.Equal(expected, CompilerInputLoader.RequiresCompilerReference(metadata.RootElement));
    }

    [Trait("Category", "AnalyzerUnit")]
    public sealed class FixtureRoleTests : IDisposable
    {
        private readonly string root = Path.Combine(AppContext.BaseDirectory, "test-results", nameof(FixtureRoleTests), Guid.NewGuid().ToString("N"));
        private string FileIn(string path, string content = "<Project/>")
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            return full;
        }
        private LoadedProject Project(string path, bool test = false, string[]? data = null) =>
            new(path, "Independent", "", AnalyzerTests.Compile(LanguageNames.CSharp, "public class Valid {}"),
                test, false, [], [], SourceDataPaths: data);

        [Theory]
        [InlineData("data")]
        [InlineData("declared")]
        [InlineData("referenced")]
        public async Task Trusted_owned_fixture_data_is_not_a_standalone_producer_but_real_roots_and_edges_override(string use)
        {
            var ownerPath = FileIn(@"Harness\Harness.csproj");
            var fixturePath = FileIn(@"Harness\Samples\Input\Input.vbproj");
            var dataPath = FileIn(@"Harness\Samples\Input\Input.vb", "Intentionally invalid conversion input");
            var producerPath = FileIn(@"Producer\Producer.csproj");
            var role = new FixtureDataRole(ownerPath, Path.Combine(root, @"Harness\Samples"));
            var owner = Project(ownerPath, test: true, data: [dataPath]);
            var fixture = Project(fixturePath) with {
                Compilation = AnalyzerTests.Compile(LanguageNames.CSharp, "public class Valid {}")
                    .AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Broken { MissingType value; }"))
            };
            var calls = new List<string>();
            async Task<LoadedProject> Load(string path)
            {
                calls.Add(path);
                if (path == ownerPath) return owner;
                if (path == fixturePath) return fixture;
                if (use == "referenced")
                {
                    var dependency = await Load(fixturePath);
                    var errors = dependency.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
                    throw new SourceCompilationException(fixturePath, errors);
                }
                return Project(path);
            }
            Task Scan() => RepositoryTests.LoadProjectSet([fixturePath, ownerPath, producerPath],
                use == "declared" ? [fixturePath] : [], Load, [role]);
            if (use == "data")
            {
                await Scan();
                Assert.DoesNotContain(fixturePath, calls);
                Assert.Contains(ownerPath, calls);
                Assert.Contains(producerPath, calls);
            }
            else
            {
                await Assert.ThrowsAsync<SourceCompilationException>(Scan);
                Assert.Contains(fixturePath, calls);
            }
        }

        [Theory]
        [InlineData("no-policy")]
        [InlineData("no-data")]
        [InlineData("compiled-source")]
        [InlineData("not-owner")]
        public async Task Fixture_names_alone_or_invalid_role_evidence_cannot_hide_projects(string variant)
        {
            var ownerPath = FileIn(@"Tests\Tests.csproj");
            var fixturePath = FileIn(@"Tests\Fixtures\Input\Input.csproj");
            var data = FileIn(@"Tests\Fixtures\Input\Input.cs", "public class Input {}");
            var role = new FixtureDataRole(ownerPath, Path.Combine(root, @"Tests\Fixtures"));
            var owner = Project(ownerPath, test: variant != "not-owner", data: variant == "no-data" ? [] : [data]);
            if (variant == "compiled-source") owner = owner with {
                Compilation = owner.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Input {}", path: data))
            };
            var calls = new List<string>();
            Task<LoadedProject> Load(string path) { calls.Add(path); return Task.FromResult(path == ownerPath ? owner : Project(path)); }
            Task Scan() => RepositoryTests.LoadProjectSet([ownerPath, fixturePath], [], Load, variant == "no-policy" ? [] : [role]);
            if (variant == "no-policy")
            {
                await Scan();
                Assert.Contains(fixturePath, calls);
            }
            else await Assert.ThrowsAsync<InvalidDataException>(Scan);
        }

        [Fact]
        public async Task Artifact_discovery_is_case_insensitive_but_explicit_producers_are_always_loaded()
        {
            var rootProject = FileIn("Root.csproj");
            var artifact = FileIn(@"artifacts\Saved\Input.csproj");
            var fixture = FileIn(@"Fixtures\Real.csproj");
            var found = RepositoryTests.DiscoverProjects(root).ToArray();
            Assert.Contains(rootProject, found);
            Assert.Contains(fixture, found);
            Assert.DoesNotContain(artifact, found);
            var calls = new List<string>();
            await RepositoryTests.LoadProjectSet(found, [artifact], path => { calls.Add(path); return Task.FromResult(Project(path)); });
            Assert.Contains(artifact, calls);
        }

        [Fact]
        public void Fixture_roles_are_explicit_trusted_policy_with_canonical_paths()
        {
            var roles = FixtureDataRole.Read(XDocument.Parse("""
              <ScenarioScope><Discovery><FixtureData Owner="Harness\Runner.csproj" Root="Harness\Samples"/></Discovery></ScenarioScope>
              """), root);
            Assert.Equal(Path.Combine(root, @"Harness\Runner.csproj"), Assert.Single(roles).Owner);
            Assert.False(FixtureDataRole.Contains(Path.Combine(root, "Samples"), Path.Combine(root, @"SamplesOther\Input.csproj")));
            Assert.Throws<InvalidDataException>(() => FixtureDataRole.Read(XDocument.Parse(
                "<ScenarioScope><Discovery><FixtureData Root=\"Samples\"/></Discovery></ScenarioScope>"), root));
        }
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_only_test_harness_role_does_not_hide_production_consumers(bool productionConsumer)
    {
        const string root = @"C:\fixture";
        var helper = Library() with {
            IsTooling = false,
            Compilation = ((CSharpCompilation)AnalyzerTests.Compile(LanguageNames.CSharp,
                "public static class Program { public static void Main() {} }"))
                .WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication))
        };
        LoadedProject Consumer(string name, bool test, bool outputAssembly) =>
            new(root + "\\" + name + ".csproj", name, "", helper.Compilation, test, false, [],
                [new InputFile(root + "\\" + name + ".csproj.assessment",
                    $"""<Project><ProjectReference Path="{helper.Path}" ReferenceOutputAssembly="{outputAssembly.ToString().ToLowerInvariant()}"/></Project>""")]);
        var test = Consumer("Assertions", true, false);
        var projects = productionConsumer ? new[] { helper, test, Consumer("Product", false, true) } : [helper, test];
        Assert.Equal(!productionConsumer, CompilerInputLoader.ClassifyTestSupport(projects, root,
            [new(test.Path, helper.Path)]).Single(p => p.Path == helper.Path).IsTest);
        Assert.False(CompilerInputLoader.ClassifyTestSupport(projects, root, [new(test.Path, helper.Path)],
            [helper.Path]).Single(p => p.Path == helper.Path).IsTest);
        // A suggestive name alone does not confer a helper role.
        var standalone = helper with { Path = root + @"\CaptureHost\CaptureHost.csproj" };
        Assert.False(CompilerInputLoader.ClassifyTestSupport([standalone], root)[0].IsTest);
    }

    [Theory]
    [InlineData("TargetPath")]
    [InlineData("TargetRefPath")]
    [InlineData("ReferenceAssembly")]
    [InlineData("MSBuildSourceProjectFile")]
    [InlineData("OriginalProjectReferenceItemSpec")]
    [InlineData("ReferencePathWithRefAssemblies")]
    public void Net10_executable_library_compiler_metadata_resolves_to_source_before_emission(string evidence)
    {
        var library = Library();
        var compilerPath = evidence switch { "TargetPath" => ImplementationPath, "TargetRefPath" => IsolatedRefPath, _ => CompilerRefPath };
        var item = new Dictionary<string, string>();
        var itemName = "ReferencePath";
        switch (evidence)
        {
            case "ReferenceAssembly":
                item["FullPath"] = ImplementationPath;
                item["ReferenceAssembly"] = compilerPath;
                break;
            case "MSBuildSourceProjectFile":
                item["Identity"] = compilerPath;
                item["MSBuildSourceProjectFile"] = ProjectPath;
                break;
            case "OriginalProjectReferenceItemSpec":
                item["FullPath"] = compilerPath;
                item["OriginalProjectReferenceItemSpec"] = @"..\Library\RenamedLibrary.csproj";
                break;
            case "ReferencePathWithRefAssemblies":
                itemName = evidence;
                item["Identity"] = compilerPath;
                item["OriginalItemSpec"] = ImplementationPath;
                item["MSBuildSourceProjectFile"] = ProjectPath;
                break;
        }
        using var metadata = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object> { [itemName] = new[] { item } }));
        var references = CompilerInputLoader.ReadEvaluatedReferences(metadata.RootElement, @"C:\fixture\Executable");
        var selected = CompilerInputLoader.ResolveSourceProject(compilerPath, [library], references);
        Assert.Same(library, selected);

        // The compiler input names a reference DLL; its on-disk contents are never read.
        var loader = new CompilerInputLoader(@"C:\assessment\unused");
        var sourceReference = loader.SourceReference(selected!, MetadataReferenceProperties.Assembly.WithAliases(["global", "Porter"]), compilerPath);
        Assert.Equal(compilerPath, sourceReference.FilePath);
        Assert.Contains("Porter", sourceReference.Properties.Aliases);
        var executable = (CSharpCompilation)AnalyzerTests.Compile(LanguageNames.CSharp, "public class Placeholder {}", "Porter.Executable")
            .RemoveAllSyntaxTrees().AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
                public static class Program {
                  public static void Main() { System.Console.WriteLine(ConvertedValue.English); }
                }
                """)).AddReferences(sourceReference);
        executable = executable.WithOptions(executable.Options.WithOutputKind(OutputKind.ConsoleApplication));
        Assert.DoesNotContain(executable.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        using var image = new MemoryStream();
        Assert.True(executable.Emit(image).Success);
    }

    [Fact]
    public void Assembly_filename_is_not_source_project_identity_and_ambiguous_metadata_fails_closed()
    {
        var library = Library();
        Assert.Null(CompilerInputLoader.ResolveSourceProject(@"C:\unrelated\Porter.Library.dll", [library], []));
        var other = library with { Path = @"C:\fixture\Other\Other.csproj", OutputPath = @"C:\other\Porter.Library.dll", TargetRefPath = null };
        EvaluatedAssemblyReference[] ambiguous = [new([CompilerRefPath], [library.Path, other.Path])];
        Assert.Throws<InvalidOperationException>(() => CompilerInputLoader.ResolveSourceProject(CompilerRefPath, [library, other], ambiguous));
        Assert.Throws<InvalidOperationException>(() => CompilerInputLoader.ResolveSourceProject(CompilerRefPath, [library],
            [new([CompilerRefPath], [other.Path])]));
    }
}

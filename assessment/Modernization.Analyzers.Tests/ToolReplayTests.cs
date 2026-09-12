using ExternalEvaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "ReplayUnit")]
public sealed class ToolReplayTests : IDisposable
{
    private readonly string root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "replay-tests", Guid.NewGuid().ToString("N"));
    private string Folder(string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }
    private ReplayCommand BuildCli(string source)
    {
        var directory = Folder("cli");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(new[] { typeof(Compilation).Assembly.Location, typeof(CSharpCompilation).Assembly.Location,
                typeof(VisualBasicCompilation).Assembly.Location }).Distinct().Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("FixtureCli", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        var assembly = Path.Combine(directory, "FixtureCli.dll");
        using (var stream = File.Create(assembly))
        {
            var emit = compilation.Emit(stream);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        }
        File.WriteAllText(Path.Combine(directory, "FixtureCli.runtimeconfig.json"), ToolReplay.RuntimeConfig);
        foreach (var dependency in new[] { typeof(Compilation).Assembly.Location, typeof(CSharpCompilation).Assembly.Location,
            typeof(VisualBasicCompilation).Assembly.Location })
            File.Copy(dependency, Path.Combine(directory, Path.GetFileName(dependency)), overwrite: true);
        return new("dotnet", [assembly, "{input}", "{output}"]);
    }

    [Fact]
    public async Task Converter_shaped_empty_class_cli_fails_despite_independent_rich_fixture_files()
    {
        var input = Folder("input"); var expected = Folder("expected");
        File.WriteAllText(Path.Combine(input, "Representative.vb"), MigrationToolTests.Input);
        File.WriteAllText(Path.Combine(expected, "Representative.cs"), MigrationToolTests.Output);
        var cli = BuildCli(MigrationToolTests.CSharpTool + """

            public static class Entry {
              public static int Main(string[] args) {
                var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                  .Split(System.IO.Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p)).ToArray();
                var output = ReusablePorter.Convert(System.IO.File.ReadAllText(System.IO.Path.Combine(args[0], "Representative.vb")), references);
                System.IO.Directory.CreateDirectory(args[1]);
                System.IO.File.WriteAllText(System.IO.Path.Combine(args[1], "Representative.cs"), output);
                return 0;
              }
            }
            """);
        var result = await ToolReplay.RunCase(new("empty-class", "language", cli, input, expected), root);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Passed);
        Assert.Contains("Emitted content differs", result.Message);
    }

    [Fact]
    public async Task Actual_process_output_compilation_behavior_and_determinism_are_verified_without_symbol_names()
    {
        var input = Folder("input"); var expected = Folder("expected");
        const string code = "public class Arbitrary { public static int Sum(int[] values) { int sum=0; foreach(var v in values) sum+=v; return sum; } }";
        File.WriteAllText(Path.Combine(input, "Arbitrary.cs"), code);
        File.WriteAllText(Path.Combine(expected, "Arbitrary.cs"), code);
        var behavior = Path.Combine(root, "Behavior.cs");
        File.WriteAllText(behavior, "public class Entry { public static int Main() => Arbitrary.Sum(new[]{2,3,7}) == 12 ? 0 : 1; }");
        // A fixture CLI for harness mechanics, not a submitted language converter.
        var cli = BuildCli("""
            public class Entry {
              public static int Main(string[] args) {
                System.IO.Directory.CreateDirectory(args[1]);
                foreach(var p in System.IO.Directory.GetFiles(args[0]))
                  System.IO.File.Copy(p, System.IO.Path.Combine(args[1], System.IO.Path.GetFileName(p)));
                return 0;
              }
            }
            """);
        var result = await ToolReplay.RunCase(new("identity-project", "project", cli, input, expected, behavior, Idempotent: true), root);
        Assert.True(result.Passed, result.Message + result.StandardError);
        Assert.NotEmpty(result.OutputHash);
        Assert.Contains(result.ToolArtifactHashes.Keys, path => path.EndsWith("FixtureCli.dll", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() => ToolReplay.VerifyBehavior(expected,
            "public class Entry { public static int Main() => Arbitrary.Sum(new[]{2,3}) == 99 ? 0 : 1; }"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unsupported_inputs_require_nonzero_diagnostics_and_no_partial_output(bool reject)
    {
        var input = Folder("input");
        File.WriteAllText(Path.Combine(input, "unsupported.vb"), "Public Class");
        var cli = BuildCli(reject ?
            "public class Entry { public static int Main(string[] args) { System.Console.Error.WriteLine(\"Unsupported syntax\"); return 2; } }" :
            "public class Entry { public static int Main(string[] args) { return 0; } }");
        var result = await ToolReplay.RunCase(new("unsupported", "language", cli, input, null, Unsupported: true), root);
        Assert.Equal(reject, result.Passed);
    }

    [Fact]
    public async Task Changed_behavior_or_compiler_errors_do_not_pass_on_matching_class_names()
    {
        var output = Folder("output");
        File.WriteAllText(Path.Combine(output, "Output.cs"), "public class Representative { public int Value => 0; }");
        await Assert.ThrowsAsync<InvalidDataException>(() => ToolReplay.VerifyBehavior(output,
            "public class Entry { public static int Main() => new Representative().Value == 4 ? 0 : 1; }"));
        File.WriteAllText(Path.Combine(output, "Output.cs"), "public class Representative { public MissingType Value; }");
        await Assert.ThrowsAsync<InvalidDataException>(() => ToolReplay.VerifyBehavior(output,
            "public class Entry { public static int Main() => 0; }"));
    }

    [Theory]
    [InlineData("source-mutation", "modified its input")]
    [InlineData("nondeterministic", "not deterministic")]
    [InlineData("no-op-language", "file set differs")]
    [InlineData("partial-unsupported", "no partial output")]
    public async Task Replay_fails_closed_for_real_cli_contract_violations(string variant, string expectedMessage)
    {
        var input = Folder("input"); var expected = Folder("expected");
        File.WriteAllText(Path.Combine(input, variant == "no-op-language" ? "Input.vb" : "Input.cs"), "public class Example {}");
        File.WriteAllText(Path.Combine(expected, "Input.cs"), "public class Example {}");
        var extra = variant switch
        {
            "source-mutation" => "System.IO.File.WriteAllText(System.IO.Path.Combine(args[0], \"mutation.txt\"), \"changed\");",
            "nondeterministic" => "if(args[1].EndsWith(\"repeated\")) System.IO.File.WriteAllText(System.IO.Path.Combine(args[1], \"Input.cs\"), \"public class Different {}\");",
            "partial-unsupported" => "System.Console.Error.WriteLine(\"Unsupported\"); return 2;",
            _ => ""
        };
        var cli = BuildCli("""
            public class Entry {
              public static int Main(string[] args) {
                System.IO.Directory.CreateDirectory(args[1]);
                foreach(var path in System.IO.Directory.GetFiles(args[0]))
                  System.IO.File.Copy(path, System.IO.Path.Combine(args[1], System.IO.Path.GetFileName(path)));
            """ + extra + "\nreturn 0; } }");
        var result = await ToolReplay.RunCase(new(variant, "language", cli, input, expected,
            Unsupported: variant == "partial-unsupported"), root);
        Assert.False(result.Passed);
        Assert.Contains(expectedMessage, result.Message);
    }

    [Fact]
    public void Replay_coverage_distinguishes_language_project_and_precompleted_work()
    {
        var command = new ReplayCommand("tool", ["{input}", "{output}"]);
        var cases = new[] { "language", "project" }.SelectMany(kind => new[] {
            new ReplayCase(kind + "-positive", kind, command, "input", "expected", "behavior.cs", Idempotent: true),
            new ReplayCase(kind + "-unsupported", kind, command, "input", null, Unsupported: true),
            new ReplayCase(kind + "-checkpoint", kind, command, "baseline", "checkpoint", Checkpoint: true)
        }).ToArray();
        var plan = new ReplayPlan(["Utility.csproj"], cases);
        Assert.True(ToolReplay.HasRequiredCoverage(plan, StagePolicy.Parse("S0", "final-delivery")));
        Assert.False(ToolReplay.HasRequiredCoverage(plan with { Cases = cases.Where(c => !c.Checkpoint).ToArray() },
            StagePolicy.Parse("S0", "final-delivery")));
        var projectOnly = plan with { Cases = cases.Where(c => c.Kind == "project").ToArray() };
        Assert.True(ToolReplay.HasRequiredCoverage(projectOnly, StagePolicy.Parse("S2", "final-delivery")));
        Assert.False(ToolReplay.HasRequiredCoverage(projectOnly, StagePolicy.Parse("S0", "final-delivery")));
        Assert.False(ToolReplay.HasRequiredCoverage(plan, StagePolicy.Parse("S3", "final-delivery")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

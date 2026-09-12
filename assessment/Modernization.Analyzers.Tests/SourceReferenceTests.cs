using System.Text.Json;
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

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData(" false ", false)]
    public void Evaluated_build_only_metadata_does_not_relax_required_compiler_references(string? value, bool required)
    {
        var item = new Dictionary<string, string> { ["FullPath"] = ProjectPath };
        if (value != null) item["ReferenceOutputAssembly"] = value;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { ProjectReference = new[] { item } }));
        var declared = CompilerInputLoader.ReadProjectReferences(json.RootElement, @"C:\fixture");
        Assert.Equal(required, Assert.Single(declared).ReferenceOutputAssembly);
        var dependency = Library();
        void Validate() => CompilerInputLoader.ValidateCompilerReferences("Consumer", declared, [dependency], []);
        if (required) Assert.Throws<InvalidOperationException>(Validate);
        else Validate();
        CompilerInputLoader.ValidateCompilerReferences("Consumer", declared, [dependency], [dependency]);
    }

    [Fact]
    public void Invalid_evaluated_reference_metadata_and_mixed_duplicate_declarations_fail_closed()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            ProjectReference = new[] { new { FullPath = ProjectPath, ReferenceOutputAssembly = "not-a-boolean" } }
        }));
        Assert.Throws<InvalidDataException>(() => CompilerInputLoader.ReadProjectReferences(json.RootElement, @"C:\fixture"));
        Assert.Throws<InvalidOperationException>(() => CompilerInputLoader.ValidateCompilerReferences("Consumer",
            [new(ProjectPath, false), new(ProjectPath, true)], [Library()], []));
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public async Task Actual_evaluated_build_only_reference_loads_source_without_injecting_host_assembly(string framework)
    {
        var root = Path.Combine(ExternalEvaluation.EvaluatorConfiguration.ArtifactRoot, "build-only-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            void Write(string path, string text)
            {
                path = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            Write("Directory.Build.props", "<Project/>");
            Write("Directory.Build.targets", "<Project/>");
            var package = framework == "net472" ?
                """<ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net472" Version="1.0.3" PrivateAssets="all"/></ItemGroup>""" : "";
            Write(@"Helper\Utility.csproj", """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType><AssemblyName>Independent.Process</AssemblyName></PropertyGroup></Project>
                """.Replace("net10.0", framework).Replace("</Project>", package + "</Project>"));
            Write(@"Helper\Program.cs", "public static class Program { public static int Main() => 0; }");
            Write(@"Checks\Fixture.vbproj", """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>
                <IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup>
                <ProjectReference Include="..\Helper\Utility.csproj">
                  <ReferenceOutputAssembly Condition="'$(Configuration)' == 'Debug'">false</ReferenceOutputAssembly>
                </ProjectReference></ItemGroup></Project>
                """.Replace("net10.0", framework).Replace("</Project>", package + "</Project>"));
            Write(@"Checks\Fixture.vb", "Public Class Fixture\nEnd Class");
            var path = Path.Combine(root, "Checks", "Fixture.vbproj");
            var start = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "build", path, "--verbosity", "quiet", "-nodeReuse:false", "-p:UseSharedCompilation=false" })
                start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
            var loader = new CompilerInputLoader(Path.Combine(root, "compiler-inputs"));
            var loaded = await loader.Load(path);
            Assert.Equal(2, loader.Projects.Count);
            Assert.EndsWith(framework == "net472" ? ".exe" : ".dll",
                Assert.Single(loader.Projects, p => p.Name == "Independent.Process").OutputPath);
            Assert.All(loader.Projects, p => Assert.DoesNotContain(p.Compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error));
            Assert.DoesNotContain(loaded.Compilation.ReferencedAssemblyNames, name => name.Name == "Independent.Process");
            var metadata = Evidence.Xml(Assert.Single(loaded.AdditionalFiles, file => file.Path == path + ".assessment"));
            Assert.Equal("false", Assert.Single(metadata.Descendants("ProjectReference")).Attribute("ReferenceOutputAssembly")!.Value);
            Assert.All(CompilerInputLoader.ClassifyTestSupport(loader.Projects, root), p => Assert.True(p.IsTest));
        }
        finally { Directory.Delete(root, true); }
    }
}

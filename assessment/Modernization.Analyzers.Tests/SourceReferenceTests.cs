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
}

using System.Security.Cryptography;
using System.Diagnostics;
using System.Xml.Linq;
using ExternalEvaluation;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "ReplayUnit")]
public sealed class ReferenceReplayTests : IDisposable
{
    private readonly string root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "reference-tests", Guid.NewGuid().ToString("N"));
    private string Write(string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
    private (ReplayPlan Plan, ReferenceApproval Approval) Fixture()
    {
        var project = Write(@"producer\OwnedFixture.csproj",
            """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>""");
        Write(@"producer\Program.cs", """
            using System;
            using System.IO;
            using System.Xml.Linq;
            public static class Program {
              public static int Main(string[] args) {
                var projects = Directory.GetFiles(args[0], "*.csproj");
                if(projects.Length == 0) { Console.Error.WriteLine("Unsupported input"); return 2; }
                Directory.CreateDirectory(args[1]);
                foreach(var path in projects) {
                  var document = XDocument.Load(path);
                  document.Root.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");
                  document.Root.Element("PropertyGroup").Element("TargetFramework").Value = "net472";
                  File.WriteAllText(Path.Combine(args[1], Path.GetFileName(path)), document.ToString());
                }
                return 0;
              }
            }
            """);
        var input = Path.GetDirectoryName(Write(@"input\App.csproj",
            "<Project><PropertyGroup><TargetFramework>net461</TargetFramework></PropertyGroup></Project>"))!;
        var expected = Path.GetDirectoryName(Write(@"expected\App.csproj",
            XDocument.Parse("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup></Project>""").ToString()))!;
        var unsupported = Path.GetDirectoryName(Write(@"unsupported\Unknown.txt", "unsupported fixture"))!;
        // Submitted/stale binary is deliberately not executable and must NEVER be used.
        var binary = Write(@"producer\bin\Debug\net10.0\OwnedFixture.dll", "stale supplied artifact");
        var command = new ReplayCommand("dotnet", [binary, "{input}", "{output}"]);
        var plan = new ReplayPlan([project], [
            new("positive", "project", command, input, expected, Idempotent: true),
            new("unsupported", "project", command, unsupported, null, Unsupported: true),
            new("checkpoint", "project", command, input, expected, Checkpoint: true)
        ], [Path.GetDirectoryName(project)!], [Path.GetDirectoryName(binary)!]);
        var approval = ReferenceReplay.CreateReviewRequest(plan) with
        {
            Purpose = "owned-reference", ReviewId = "assessor-owned-fixture-review",
            Reviewer = "test fixture author", SourceRevision = "assessor-owned synthetic test source"
        };
        return (plan, approval);
    }

    [Fact]
    public async Task Reviewed_build_outputs_can_be_diagnosed_but_not_attested_as_compiler_produced()
    {
        var (plan, approval) = Fixture();
        var result = await ReferenceReplay.Run(plan, StagePolicy.Parse("S2", "final-delivery"), root, approval);
        Assert.False(result.ReferenceVerified);
        Assert.True(result.LocalEvidencePassed, result.Message);
        Assert.False(result.Verified);
        Assert.Contains("NOT isolated", result.ExecutionBoundary);
        Assert.NotNull(result.ReferenceBuild);
        Assert.Equal(approval.SourceHash, result.ReferenceBuild.ApprovedSourceHash);
        Assert.False(result.ReferenceBuild.CompilerProvenanceVerified);
        Assert.Single(result.ReferenceBuild.ReportedCompilerArgumentHashes);
        var built = Assert.Single(result.ReferenceBuild.ObservedBuildArtifacts, p => p.Key.EndsWith("OwnedFixture.dll", StringComparison.Ordinal));
        var suppliedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(plan.Cases[0].Command.Arguments[0])));
        Assert.NotEqual(suppliedHash, built.Value);
        Assert.All(result.Cases, c => Assert.True(c.Passed, c.Message));
    }

    [Fact]
    public async Task Fixture_copying_source_cannot_replace_reviewed_producer_or_self_approve()
    {
        var (plan, approval) = Fixture();
        Write(@"producer\Program.cs", """
            public class Program {
              public static int Main(string[] args) {
                System.IO.Directory.CreateDirectory(args[1]);
                foreach(var file in System.IO.Directory.GetFiles(args[2]))
                  System.IO.File.Copy(file, System.IO.Path.Combine(args[1], System.IO.Path.GetFileName(file)));
                return 0;
              }

              [Fact]
              public async Task Trusted_tool_build_properties_reach_the_actual_fresh_producer()
              {
                  var (plan, approval) = Fixture();
                  var path = Path.Combine(root, "producer", "Program.cs");
                  File.WriteAllText(path, "#if !REVIEWED\n#error Missing trusted producer configuration\n#endif\n" + File.ReadAllText(path));
                  plan = plan with { ProjectRoles = [new(plan.Projects[0], "tool", new() { ["DefineConstants"] = "REVIEWED" })] };
                  approval = approval with { SourceHash = ExternalReplay.HashSourceTree(plan.SourceRoots![0]) };
                  var result = await ReferenceReplay.Run(plan, StagePolicy.Parse("S2", "final-delivery"), root, approval);
                  Assert.True(result.ReferenceVerified, result.Message);
              }
            }
            """);
        var result = await ReferenceReplay.Run(plan, StagePolicy.Parse("S2", "final-delivery"), root, approval);
        Assert.False(result.ReferenceVerified);
        Assert.Empty(result.Cases);
        Assert.Contains("exact owned source snapshot", result.Message);
        Assert.Throws<InvalidDataException>(() => ReferenceReplay.ValidateApproval(plan, ReferenceReplay.CreateReviewRequest(plan)));
    }

    [Fact]
    public async Task Absolute_expected_paths_cannot_be_passed_to_a_reference_producer()
    {
        var (plan, approval) = Fixture();
        plan = plan with { Cases = plan.Cases.Select(c => c with {
            Command = c.Command with { Arguments = c.Command.Arguments.Append(Path.Combine(root, "expected")).ToArray() }
        }).ToArray() };
        var result = await ReferenceReplay.Run(plan, StagePolicy.Parse("S2", "final-delivery"), root, approval);
        Assert.False(result.ReferenceVerified);
        Assert.Contains("no absolute fixture/expected paths", result.Message);
        Assert.Empty(result.Cases);
    }

    [Fact]
    public async Task Approved_but_uncompilable_source_cannot_fall_back_to_supplied_artifacts()
    {
        var (plan, approval) = Fixture();
        Write(@"producer\Program.cs", "public class Program { public static MissingType Main() => null; }");
        approval = approval with { SourceHash = ExternalReplay.HashSourceTree(plan.SourceRoots![0]) };
        var result = await ReferenceReplay.Run(plan, StagePolicy.Parse("S2", "final-delivery"), root, approval);
        Assert.False(result.ReferenceVerified);
        Assert.Contains("producer build failed", result.Message);
        Assert.Empty(result.Cases);
    }

    [Fact]
    public async Task A_post_build_substitute_is_not_the_compiled_producer()
    {
        var (plan, approval) = Fixture();
        Write(@"producer\payload.dll", "uncompiled replacement artifact");
        var project = XDocument.Load(plan.Projects[0]);
        project.Root!.Add(new XElement("Target", new XAttribute("Name", "ReplaceOutput"),
            new XAttribute("AfterTargets", "Build"), new XElement("Copy",
                new XAttribute("SourceFiles", @"$(MSBuildProjectDirectory)\payload.dll"),
                new XAttribute("DestinationFiles", "$(TargetPath)"))));
        File.WriteAllText(plan.Projects[0], project.ToString());
        approval = approval with { SourceHash = ExternalReplay.HashSourceTree(plan.SourceRoots![0]) };
        var result = await ReferenceReplay.Run(plan, StagePolicy.Parse("S2", "final-delivery"), root, approval);
        Assert.False(result.ReferenceVerified);
        Assert.Contains("neither is a trusted compiler capture", result.Message);
        Assert.Empty(result.Cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Matching_mutable_build_outputs_do_not_establish_compiler_provenance(bool replaceBoth)
    {
        var (plan, approval) = Fixture();
        if (replaceBoth)
        {
            // Compile the owned fixture once to obtain a real, runnable tracked payload.
            var source = plan.SourceRoots![0];
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
                Write(Path.Combine("producer", name), "<Project/>");
            File.Copy(Path.Combine(EvaluatorConfiguration.AssessmentRoot, "global.json"), Path.Combine(source, "global.json"));
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = source, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            ToolReplay.FilterEnvironment(start);
            ToolReplay.ConfigureRuntimeState(start, Path.Combine(root, "payload-build-state"));
            foreach (var argument in new[] { "build", plan.Projects[0], "--nologo", "--verbosity", "quiet",
                "-p:UseSharedCompilation=false", "-p:EnableSourceControlManagerQueries=false",
                "-p:NuGetAudit=false", "-nodeReuse:false" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try { await Task.WhenAll(process.WaitForExitAsync(), output, error).WaitAsync(TimeSpan.FromMinutes(2)); }
            catch (TimeoutException) { if (!process.HasExited) process.Kill(true); throw; }
            Assert.True(process.ExitCode == 0, await output + await error);
            File.Copy(plan.Cases[0].Command.Arguments[0], Path.Combine(source, "payload.dll"));
            // The compiled authored source now differs from the runnable payload.
            Write(@"producer\Program.cs", "public class Program { public static int Main(string[] args) => 91; }");
            var project = XDocument.Load(plan.Projects[0]);
            project.Root!.Add(new XElement("Target", new XAttribute("Name", "ReplaceBothOutputs"),
                new XAttribute("AfterTargets", "Build"),
                new XElement("Copy", new XAttribute("SourceFiles", @"$(MSBuildProjectDirectory)\payload.dll"),
                    new XAttribute("DestinationFiles", "$(IntermediateOutputPath)$(TargetFileName)")),
                new XElement("Copy", new XAttribute("SourceFiles", @"$(MSBuildProjectDirectory)\payload.dll"),
                    new XAttribute("DestinationFiles", "$(TargetPath)"))));
            File.WriteAllText(plan.Projects[0], project.ToString());
            approval = approval with { SourceHash = ExternalReplay.HashSourceTree(source) };
        }
        var result = await ReferenceReplay.Run(plan, StagePolicy.Parse("S2", "final-delivery"), root, approval);
        Assert.False(result.ReferenceVerified, "Post-build /out and TargetPath equality is not trusted compiler provenance.");
        Assert.False(result.Verified);
        Assert.True(result.LocalEvidencePassed, result.Message);
        Assert.Contains("compiler provenance is unverified", result.Message);
    }

    [Theory]
    [InlineData("review-required")]
    [InlineData("different-project")]
    [InlineData("missing-reviewer")]
    public void A_configuration_flag_is_not_source_producer_approval(string variant)
    {
        var (plan, approval) = Fixture();
        approval = variant switch {
            "review-required" => ReferenceReplay.CreateReviewRequest(plan),
            "different-project" => approval with { Projects = ["Other.csproj"] },
            _ => approval with { Reviewer = "" }
        };
        Assert.Throws<InvalidDataException>(() => ReferenceReplay.ValidateApproval(plan, approval));
    }
    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

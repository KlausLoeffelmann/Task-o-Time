using System.Security.Cryptography;
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
    public async Task Exact_reviewed_source_is_freshly_built_and_replayed_instead_of_supplied_binary()
    {
        var (plan, approval) = Fixture();
        var result = await ReferenceReplay.Run(plan, StagePolicy.Parse("S2", "final-delivery"), root, approval);
        Assert.True(result.ReferenceVerified, result.Message);
        Assert.False(result.Verified);
        Assert.Contains("NOT isolated", result.ExecutionBoundary);
        Assert.NotNull(result.ReferenceBuild);
        Assert.Equal(approval.SourceHash, result.ReferenceBuild.ApprovedSourceHash);
        Assert.Single(result.ReferenceBuild.CompilerInputs);
        var built = Assert.Single(result.ReferenceBuild.BuiltArtifacts, p => p.Key.EndsWith("OwnedFixture.dll", StringComparison.Ordinal));
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
        Assert.Contains("does not match the freshly compiled producer output", result.Message);
        Assert.Empty(result.Cases);
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

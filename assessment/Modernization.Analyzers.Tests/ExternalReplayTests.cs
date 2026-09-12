using System.Security.Cryptography;
using System.Text.Json;
using ExternalEvaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "ReplayUnit")]
public sealed class ExternalReplayTests : IDisposable
{
    private readonly string root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "external-contract-tests", Guid.NewGuid().ToString("N"));
    private readonly StagePolicy policy = StagePolicy.Parse("S0", "final-delivery");
    private string FileIn(string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
    private ReplayPlan Plan()
    {
        var input = Path.GetDirectoryName(FileIn(@"input\Source.vb", "Public Class Example\nEnd Class"))!;
        var expected = Path.GetDirectoryName(FileIn(@"expected\Source.cs", "public class Example {}"))!;
        var project = FileIn(@"outside-discovery\Utility.csproj", "<Project/>");
        FileIn(@"outside-discovery\Utility.cs", "public class Utility {}");
        // Deliberately NOT executable. Receipt tests must never launch a submission.
        var tool = FileIn(@"binary\Dummy.dll", "contract-only non-executable artifact");
        FileIn(@"binary\Engine.dll", "contract-only dependency artifact");
        var behavior = FileIn("Behavior.cs", "public class Entry { public static int Main() => 0; }");
        var command = new ReplayCommand("dotnet", [tool, "{input}", "{output}"]);
        var cases = new[] { "language", "project" }.SelectMany(kind => new[] {
            new ReplayCase(kind + "-positive", kind, command, input, expected, behavior, Idempotent: true),
            new ReplayCase(kind + "-unsupported", kind, command, input, null, Unsupported: true),
            new ReplayCase(kind + "-checkpoint", kind, command, input, expected, Checkpoint: true)
        }).ToArray();
        return new([project], cases, [Path.GetDirectoryName(project)!], [Path.GetDirectoryName(tool)!]);
    }
    private ReplayAttestation Attestation(ReplayPlan plan, string challenge) => new(ExternalReplay.Policy,
        challenge, ExternalReplay.RequestHash(plan, policy, challenge), "test-only-trusted-executor",
        DateTimeOffset.UtcNow.AddMinutes(10), plan.Cases.Select(c => new ExternalCaseEvidence(
            new(c.Name, true, "Contract fixture; no execution.", ToolReplay.HashTree(c.InputDirectory),
                c.Unsupported ? "" : ToolReplay.HashTree(c.ExpectedDirectory!), "dummy", c.Unsupported ? 2 : 0,
                "", c.Unsupported ? "Unsupported" : ""),
            ["isolated-process", "tool-source-build", "input-immutable", "unsupported-diagnostic",
                "no-partial-output", "frozen-expectation-comparison", "deterministic-rerun",
                "output-compilation", "behavior", "idempotent-rerun"])).ToArray());
    private static string Sign(ReplayAttestation receipt, RSA key)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(receipt);
        return JsonSerializer.Serialize(new SignedReplayReceipt(Convert.ToBase64String(payload),
            Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))));
    }

    [Fact]
    public void Pinned_key_receipt_contract_is_verified_without_executing_dummy_artifact()
    {
        var plan = Plan(); var challenge = Guid.NewGuid().ToString("N");
        using var key = RSA.Create(3072);
        var result = ExternalReplay.Verify(plan, policy, challenge, Sign(Attestation(plan, challenge), key),
            key.ExportSubjectPublicKeyInfoPem());
        Assert.True(result.Verified, result.Message);
        Assert.Equal("trusted-external-receipt", result.ExecutionBoundary);
        Assert.False(result.LocalEvidencePassed);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("wrong-challenge")]
    [InlineData("mutated-input")]
    [InlineData("mutated-expectation")]
    [InlineData("changed-tool")]
    [InlineData("changed-behavior")]
    [InlineData("missing-check")]
    [InlineData("expired")]
    [InlineData("candidate-sandbox-flag")]
    [InlineData("wrong-profile")]
    [InlineData("missing-project")]
    [InlineData("changed-project")]
    [InlineData("changed-source")]
    [InlineData("changed-dependency")]
    [InlineData("overlong-expiry")]
    [InlineData("missing-source-roots")]
    [InlineData("missing-artifact-roots")]
    [InlineData("producer-only-policy")]
    public void Unbound_or_candidate_controlled_receipts_fail_closed(string variant)
    {
        var plan = Plan(); var challenge = Guid.NewGuid().ToString("N");
        using var key = RSA.Create(3072);
        var attestation = Attestation(plan, challenge);
        if (variant == "missing-check")
            attestation = attestation with { Cases = attestation.Cases.Select(c => c with { Checks = [] }).ToArray() };
        if (variant == "expired") attestation = attestation with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        if (variant == "overlong-expiry") attestation = attestation with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        if (variant == "producer-only-policy") attestation = attestation with { Policy = "taskotime-sandbox-producer-v1" };
        var envelope = Sign(attestation, key);
        var publicKey = key.ExportSubjectPublicKeyInfoPem();
        switch (variant)
        {
            case "wrong-key":
                using (var other = RSA.Create(3072)) publicKey = other.ExportSubjectPublicKeyInfoPem();
                break;
            case "wrong-challenge": challenge = Guid.NewGuid().ToString("N"); break;
            case "mutated-input": FileIn(@"input\Source.vb", "changed"); break;
            case "mutated-expectation": FileIn(@"expected\Source.cs", "changed"); break;
            case "changed-tool": FileIn(@"binary\Dummy.dll", "changed"); break;
            case "changed-behavior": FileIn("Behavior.cs", "changed"); break;
            case "candidate-sandbox-flag": envelope = """{"sandbox":true,"isolated":true,"Verified":true}"""; break;
            case "missing-project": File.Delete(plan.Projects[0]); break;
            case "changed-project": FileIn(@"outside-discovery\Utility.csproj", "<Project Sdk=\"Different\"/>"); break;
            case "changed-source": FileIn(@"outside-discovery\Utility.cs", "public class Different {}"); break;
            case "changed-dependency": FileIn(@"binary\Engine.dll", "changed dependency"); break;
            case "missing-source-roots": plan = plan with { SourceRoots = null }; break;
            case "missing-artifact-roots": plan = plan with { ArtifactRoots = null }; break;
        }
        var result = ExternalReplay.Verify(plan, variant == "wrong-profile" ? StagePolicy.Parse("S1", "final-delivery") : policy,
            challenge, envelope, publicKey);
        Assert.False(result.Verified);
    }

    [Fact]
    public async Task Declared_project_outside_discovery_is_loaded_exactly_once()
    {
        var outside = FileIn(@"outside-discovery\Utility.csproj", "<Project/>");
        var calls = new List<string>();
        var compilation = AnalyzerTests.Compile("C#", "public class Utility {}");
        await RepositoryTests.LoadProjectSet([], [outside, outside], path =>
        {
            calls.Add(path);
            return Task.FromResult(new LoadedProject(path, "Utility", "", compilation, false, true, [], []));
        });
        Assert.Equal([outside], calls);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing-check", false)]
    [InlineData("missing-record", false)]
    [InlineData("duplicate-run", false)]
    [InlineData("failed-status", false)]
    [InlineData("bad-hash", false)]
    [InlineData("wrong-basis", false)]
    [InlineData("changed-contract", false)]
    public void Signed_evidence_contract_binds_schema_and_every_execution_record(string variant, bool verified)
    {
        var plan = Plan();
        var contract = new EvidenceFileContract("run.json", "Status", "succeeded");
        plan = plan with { Cases = plan.Cases.Select(c => c.Unsupported ? c : c with { EvidenceFile = contract }).ToArray() };
        var challenge = Guid.NewGuid().ToString("N");
        var attestation = Attestation(plan, challenge);
        attestation = attestation with
        {
            Cases = attestation.Cases.Select(c =>
            {
                var fixture = plan.Cases.Single(f => f.Name == c.Evidence.Name);
                if (fixture.Unsupported) return c;
                var runs = fixture.Idempotent ? new[] { "initial", "repeat", "idempotent" } : ["initial", "repeat"];
                var artifacts = runs.Select(run => new ExecutionArtifact("run.json", run, new string('A', 64), "succeeded")).ToArray();
                if (variant == "missing-record") artifacts = artifacts.Skip(1).ToArray();
                if (variant == "duplicate-run") artifacts[1] = artifacts[0];
                if (variant == "failed-status") artifacts[0] = artifacts[0] with { Status = "failed" };
                if (variant == "bad-hash") artifacts[0] = artifacts[0] with { Sha256 = "invalid" };
                return c with
                {
                    Checks = variant == "missing-check" ? c.Checks : c.Checks.Append("declared-evidence-schema").ToArray(),
                    Evidence = c.Evidence with
                    {
                        ExecutionArtifacts = artifacts,
                        OutputHashBasis = variant == "wrong-basis" ? "all-emitted-files" : ToolReplay.OutputBasis(contract)
                    }
                };
            }).ToArray()
        };
        using var key = RSA.Create(3072);
        var signed = Sign(attestation, key);
        if (variant == "changed-contract")
            plan = plan with { Cases = plan.Cases.Select(c => c with { EvidenceFile = contract with { FileName = "other.json" } }).ToArray() };
        var result = ExternalReplay.Verify(plan, policy, challenge, signed, key.ExportSubjectPublicKeyInfoPem());
        Assert.Equal(verified, result.Verified);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Absent_or_uncompilable_declared_projects_fail_before_replay(bool missing)
    {
        var outside = FileIn(@"outside-discovery\Utility.csproj", "<Project/>");
        if (missing) File.Delete(outside);
        var compilation = CSharpCompilation.Create("Broken", [CSharpSyntaxTree.ParseText("class Tool { MissingType value; }")]);
        Task Load() => RepositoryTests.LoadProjectSet([], [outside], path =>
            Task.FromResult(new LoadedProject(path, "Utility", "", compilation, false, true, [], [])));
        if (missing) await Assert.ThrowsAsync<InvalidDataException>(Load);
        else await Assert.ThrowsAsync<SourceCompilationException>(Load);
    }
    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

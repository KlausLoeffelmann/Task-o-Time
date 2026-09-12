using System.Security.Cryptography;
using System.Text.Json;
using ExternalEvaluation;

namespace Modernization.Analyzers.Tests;

internal sealed record SignedReplayReceipt(string PayloadBase64, string SignatureBase64);
internal sealed record ExternalCaseEvidence(ReplayEvidence Evidence, string[] Checks);
internal sealed record ReplayAttestation(string Policy, string Challenge, string RequestHash,
    string Executor, DateTimeOffset ExpiresAt, ExternalCaseEvidence[] Cases);
internal sealed record ReplayRequest(string Protocol, string Profile, string Challenge, ReplayPlan Plan,
    SortedDictionary<string, string> Hashes);

// A verifier, NOT an isolation implementation. Only the separately provisioned,
// trusted executor owns the private signing key and attests the execution boundary.
internal static class ExternalReplay
{
    internal const string Policy = "taskotime-isolated-replay-v1";

    internal static ReplayResult VerifyConfigured(ReplayPlan plan, StagePolicy policy)
    {
        try
        {
            var receipt = Environment.GetEnvironmentVariable("ASSESSMENT_REPLAY_RECEIPT");
            var key = Environment.GetEnvironmentVariable("ASSESSMENT_REPLAY_PUBLIC_KEY");
            var challenge = Environment.GetEnvironmentVariable("ASSESSMENT_REPLAY_CHALLENGE");
            if (challenge == null)
                return new(false, [], "Formal replay requires a trusted external executor receipt, pinned public key and fresh challenge. No candidate process was executed.");
            WriteRequest(plan, policy, challenge);
            if (receipt == null || key == null)
                return new(false, [], "Exported external-replay-request.json; formal acceptance is blocked until a trusted isolated executor returns a signed receipt. No candidate process was executed.");
            return Verify(plan, policy, challenge,
                File.ReadAllText(ToolReplay.TrustedPath(receipt)),
                File.ReadAllText(ToolReplay.TrustedPath(key)));
        }
        catch (Exception error)
        {
            return new(false, [], "External replay evidence invalid: " + error.Message);
        }
    }

    internal static string WriteRequest(ReplayPlan plan, StagePolicy policy, string challenge)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(BuildRequest(plan, policy, challenge));
        var output = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "Reports", policy.Identity);
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, "external-replay-request.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            PayloadBase64 = Convert.ToBase64String(payload),
            Sha256 = Convert.ToHexString(SHA256.HashData(payload))
        }, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    internal static string RequestHash(ReplayPlan plan, StagePolicy policy, string challenge)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(BuildRequest(plan, policy, challenge))));

    internal static ReplayRequest BuildRequest(ReplayPlan plan, StagePolicy policy, string challenge)
    {
        if (!Guid.TryParseExact(challenge, "N", out _))
            throw new InvalidDataException("The assessor must issue a fresh GUID-N challenge per evaluation.");
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (plan.SourceRoots is not { Length: > 0 } || plan.ArtifactRoots is not { Length: > 0 })
            throw new InvalidDataException("Formal replay requires reviewed complete source and binary/dependency roots.");
        foreach (var root in plan.SourceRoots)
            hashes["source-tree:" + Path.GetFullPath(root)] = HashSourceTree(root);
        foreach (var root in plan.ArtifactRoots)
        {
            if (!Path.IsPathFullyQualified(root)) throw new InvalidDataException("Artifact roots must be absolute.");
            hashes["artifact-tree:" + Path.GetFullPath(root)] = ToolReplay.HashTree(root);
        }
        bool Within(string path, string root) => Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        foreach (var project in ProjectRoles.Read(plan).Keys)
        {
            if (!Path.IsPathFullyQualified(project) || !File.Exists(project) ||
                !plan.SourceRoots.Any(root => Within(project, root)))
                throw new InvalidDataException("Declared tool project is missing: " + project);
            hashes["project:" + Path.GetFullPath(project)] = HashFile(project);
        }
        foreach (var fixture in plan.Cases)
        {
            ToolReplay.ValidateEvidenceDeclaration(fixture);
            hashes["input:" + fixture.Name] = ToolReplay.HashTree(ToolReplay.TrustedPath(fixture.InputDirectory));
            if (!fixture.Unsupported)
                hashes["expected:" + fixture.Name] = ToolReplay.HashTree(ToolReplay.TrustedPath(
                    fixture.ExpectedDirectory ?? throw new InvalidDataException("Expected directory is missing.")));
            if (fixture.BehaviorFile != null)
                hashes["behavior:" + fixture.Name] = HashFile(ToolReplay.TrustedPath(fixture.BehaviorFile));
            var artifacts = fixture.Command.Arguments.Prepend(fixture.Command.Executable)
                .Where(Path.IsPathFullyQualified).ToArray();
            if (artifacts.Length == 0)
                throw new InvalidDataException("Formal replay requires an explicit executable/DLL/script artifact path.");
            if (!artifacts.Any(artifact => plan.ArtifactRoots.Any(root => Within(artifact, root))))
                throw new InvalidDataException("The command must identify an artifact in the reviewed binary/dependency roots.");
            foreach (var artifact in artifacts)
                hashes["tool:" + Path.GetFullPath(artifact)] = HashFile(artifact);
        }
        return new(Policy, policy.Identity, challenge, plan, hashes);
    }

    internal static ReplayResult Verify(ReplayPlan plan, StagePolicy policy, string challenge,
        string receiptJson, string publicKeyPem)
    {
        try
        {
            if (!ToolReplay.HasRequiredCoverage(plan, policy))
                throw new InvalidDataException("Required trusted fixture/checkpoint coverage is incomplete.");
            var envelope = JsonSerializer.Deserialize<SignedReplayReceipt>(receiptJson)
                ?? throw new InvalidDataException("Missing signed envelope.");
            var payload = Convert.FromBase64String(envelope.PayloadBase64);
            using var key = RSA.Create();
            key.ImportFromPem(publicKeyPem);
            if (key.KeySize < 3072 || !key.VerifyData(payload, Convert.FromBase64String(envelope.SignatureBase64),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw new InvalidDataException("Receipt signature is not from the pinned executor key.");
            var attestation = JsonSerializer.Deserialize<ReplayAttestation>(payload)
                ?? throw new InvalidDataException("Missing executor attestation.");
            if (attestation.Policy != Policy || attestation.Challenge != challenge ||
                attestation.RequestHash != RequestHash(plan, policy, challenge) ||
                attestation.ExpiresAt <= DateTimeOffset.UtcNow || attestation.ExpiresAt > DateTimeOffset.UtcNow.AddHours(1) ||
                string.IsNullOrWhiteSpace(attestation.Executor))
                throw new InvalidDataException("Receipt policy, challenge, immutable request, executor or expiry mismatch.");
            if (attestation.Cases.Length != plan.Cases.Length ||
                attestation.Cases.Select(c => c.Evidence.Name).Distinct(StringComparer.Ordinal).Count() != plan.Cases.Length)
                throw new InvalidDataException("Receipt case set is incomplete or duplicated.");
            foreach (var fixture in plan.Cases)
            {
                var result = attestation.Cases.Single(c => c.Evidence.Name == fixture.Name);
                var required = new List<string> { "isolated-process", "tool-source-build", "input-immutable" };
                if (fixture.Unsupported) required.AddRange(["unsupported-diagnostic", "no-partial-output"]);
                else
                {
                    required.AddRange(["frozen-expectation-comparison", "deterministic-rerun", "output-compilation"]);
                    if (fixture.BehaviorFile != null) required.Add("behavior");
                    if (fixture.Idempotent) required.Add("idempotent-rerun");
                    if (fixture.EvidenceFile != null) required.Add("declared-evidence-schema");
                }
                if (!result.Evidence.Passed || required.Except(result.Checks, StringComparer.Ordinal).Any() ||
                    result.Evidence.InputHash != ToolReplay.HashTree(ToolReplay.TrustedPath(fixture.InputDirectory)) ||
                    fixture.Unsupported && (result.Evidence.ExitCode == 0 ||
                        string.IsNullOrWhiteSpace(result.Evidence.StandardError + result.Evidence.StandardOutput) ||
                        !string.IsNullOrEmpty(result.Evidence.OutputHash)) ||
                    !fixture.Unsupported && (result.Evidence.ExitCode != 0 || result.Evidence.OutputHash.Length != 64 ||
                        !result.Evidence.OutputHash.All(Uri.IsHexDigit)))
                    throw new InvalidDataException("Executor checks failed or missing for " + fixture.Name);
                if (!fixture.Unsupported && fixture.EvidenceFile is { } contract)
                {
                    var runs = fixture.Idempotent ? new[] { "initial", "repeat", "idempotent" } : ["initial", "repeat"];
                    var artifacts = result.Evidence.ExecutionArtifacts;
                    if (result.Evidence.OutputHashBasis != ToolReplay.OutputBasis(contract) ||
                        artifacts.Length != runs.Length || artifacts.Select(a => a.Run).Distinct(StringComparer.Ordinal).Count() != runs.Length ||
                        artifacts.Any(a => !runs.Contains(a.Run, StringComparer.Ordinal) || a.FileName != contract.FileName ||
                            a.Status != contract.SuccessValue || a.Sha256.Length != 64 || !a.Sha256.All(Uri.IsHexDigit)))
                        throw new InvalidDataException("Missing or invalid declared execution-evidence records for " + fixture.Name);
                }
            }
            return new(true, attestation.Cases.Select(c => c.Evidence).ToArray(),
                "Verified signed external replay receipt; executor=" + attestation.Executor +
                "; key-sha256=" + Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())))
                { ExecutionBoundary = "trusted-external-receipt" };
        }
        catch (Exception error)
        {
            return new(false, [], "External replay evidence invalid: " + error.Message);
        }
    }
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    internal static string HashSourceTree(string root)
    {
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root))
            throw new InvalidDataException("Source roots must be existing absolute paths.");
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Visit(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Source snapshot cannot traverse a reparse point.");
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Source snapshot cannot contain a reparse point.");
                files[Path.GetRelativePath(root, file)] = HashFile(file);
            }
            foreach (var child in Directory.EnumerateDirectories(directory))
                if (Path.GetFileName(child).ToLowerInvariant() is not ("bin" or "obj" or "artifacts" or ".git"))
                    Visit(child);
        }
        Visit(root);
        if (files.Count == 0) throw new InvalidDataException("Source snapshot is empty.");
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(files)));
    }
}

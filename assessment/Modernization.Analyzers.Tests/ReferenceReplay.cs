using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ExternalEvaluation;

namespace Modernization.Analyzers.Tests;

internal sealed record ReferenceApproval(string Purpose, string ReviewId, string Reviewer,
    string SourceRevision, string SourceHash, string[] Projects);
public sealed record ReferenceBuildEvidence(string ReviewId, string Reviewer, string SourceRevision,
    string ApprovedSourceHash, string BuildInputHash, string[] Commands, SortedDictionary<string, string> CompilerInputs,
    SortedDictionary<string, string> BuiltArtifacts);

// A reviewed-owned-source trust model, not adversarial isolation. An approval is
// independent assessor custody and binds exact source; it is never inferred from a flag.
internal static class ReferenceReplay
{
    internal static void ValidateBeforeProjectEvaluation()
    {
        if (Environment.GetEnvironmentVariable("ASSESSMENT_REPLAY_EXECUTION") != "reference-reviewed") return;
        var plan = ToolReplay.Plan ?? throw new InvalidDataException("Owned-reference evaluation requires a trusted replay plan.");
        var path = Environment.GetEnvironmentVariable("ASSESSMENT_REFERENCE_APPROVAL")
            ?? throw new InvalidDataException("Export and independently approve the source review request before any project evaluation.");
        var approval = JsonSerializer.Deserialize<ReferenceApproval>(File.ReadAllText(ToolReplay.TrustedPath(path)))
            ?? throw new InvalidDataException("Missing reference approval.");
        ValidateApproval(plan, approval);
    }

    internal static async Task<ReplayResult> RunConfigured(ReplayPlan plan, StagePolicy policy, string artifactRoot)
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("ASSESSMENT_REFERENCE_APPROVAL");
            if (path == null)
            {
                var output = Path.Combine(artifactRoot, "Reports", policy.Identity);
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "reference-review-request.json"),
                    JsonSerializer.Serialize(CreateReviewRequest(plan), new JsonSerializerOptions { WriteIndented = true }));
                return new(false, [], "Exported reference-review-request.json for independent source review; no producer executed and no approval inferred.");
            }
            var approval = JsonSerializer.Deserialize<ReferenceApproval>(File.ReadAllText(ToolReplay.TrustedPath(path)))
                ?? throw new InvalidDataException("Missing reference approval.");
            return await Run(plan, policy, artifactRoot, approval);
        }
        catch (Exception error) { return new(false, [], "Reference validation failed: " + error.Message); }
    }

    internal static ReferenceApproval CreateReviewRequest(ReplayPlan plan)
    {
        if (plan.SourceRoots is not { Length: 1 })
            throw new InvalidDataException("Declare one complete source root for owned-reference review.");
        return new("REVIEW_REQUIRED", "", "", "", ExternalReplay.HashSourceTree(plan.SourceRoots[0]),
            plan.Projects.Select(p => Path.GetRelativePath(plan.SourceRoots[0], p)).Order(StringComparer.Ordinal).ToArray());
    }

    internal static void ValidateApproval(ReplayPlan plan, ReferenceApproval approval)
    {
        if (plan.SourceRoots is not { Length: 1 })
            throw new InvalidDataException("Reference builds require one complete reviewed source root, including linked projects/imports.");
        var root = Path.GetFullPath(plan.SourceRoots[0]);
        if (approval.Purpose != "owned-reference" || string.IsNullOrWhiteSpace(approval.ReviewId) ||
            string.IsNullOrWhiteSpace(approval.Reviewer) || string.IsNullOrWhiteSpace(approval.SourceRevision) ||
            approval.SourceHash != ExternalReplay.HashSourceTree(root))
            throw new InvalidDataException("No matching independent approval for the exact owned source snapshot.");
        var projects = plan.Projects.Select(p =>
        {
            var relative = Path.GetRelativePath(root, Path.GetFullPath(p));
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative) || !File.Exists(p))
                throw new InvalidDataException("Project is outside the approved source snapshot: " + p);
            return relative;
        }).Order(StringComparer.Ordinal).ToArray();
        if (!projects.SequenceEqual(approval.Projects.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Review approval project set differs from the replay plan.");
        foreach (var fixture in plan.Cases)
        {
            ToolReplay.ValidateEvidenceDeclaration(fixture);
            foreach (var path in new[] { fixture.InputDirectory, fixture.ExpectedDirectory, fixture.BehaviorFile }.OfType<string>())
                if (Path.GetFullPath(path).StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Reviewed producer source must not contain private replay inputs/expectations.");
            if (fixture.Command.Executable != "dotnet")
                throw new InvalidDataException("Owned-reference builds currently support managed dotnet CLI producers only.");
            var binaries = fixture.Command.Arguments.Where(Path.IsPathFullyQualified).ToArray();
            if (binaries.Length != 1 || Path.GetExtension(binaries[0]) != ".dll" ||
                !Path.GetFullPath(binaries[0]).StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Command must name exactly one managed producer under the reviewed root; no absolute fixture/expected paths.");
        }
    }

    internal static async Task<ReplayResult> Run(ReplayPlan plan, StagePolicy policy, string artifactRoot, ReferenceApproval approval)
    {
        var work = Path.Combine(artifactRoot, "reference-builds", Guid.NewGuid().ToString("N"));
        try
        {
            ValidateApproval(plan, approval);
            if (!ToolReplay.HasRequiredCoverage(plan, policy))
                throw new InvalidDataException("Owned-reference validation still requires all applicable replay/checkpoint fixtures.");
            var original = Path.GetFullPath(plan.SourceRoots![0]);
            var source = Path.Combine(work, "source");
            CopySource(original, source);
            if (ExternalReplay.HashSourceTree(source) != approval.SourceHash)
                throw new InvalidDataException("Copied source differs from the approved snapshot.");
            // Stop accidental imports from the enclosing private assessor workspace.
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
                if (!File.Exists(Path.Combine(source, name))) File.WriteAllText(Path.Combine(source, name), "<Project/>");
            if (!File.Exists(Path.Combine(source, "global.json")))
                File.Copy(Path.Combine(EvaluatorConfiguration.AssessmentRoot, "global.json"), Path.Combine(source, "global.json"));
            var buildInputHash = ExternalReplay.HashSourceTree(source);
            var commands = new List<string>();
            var compilerInputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in plan.Projects)
            {
                var copied = Path.Combine(source, Path.GetRelativePath(original, project));
                var sdk = Directory.GetParent(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "evaluator-sdk.txt")).Trim())!.FullName;
                if (!Path.GetFileName(sdk).StartsWith("10.", StringComparison.Ordinal))
                    throw new InvalidDataException("Reference producer build requires the pinned .NET 10 SDK.");
                var arguments = new[] { Path.Combine(sdk, "MSBuild.dll"), copied, "-restore", "-target:Build",
                    "-nologo", "-verbosity:quiet", "-nodeReuse:false", "-property:ContinuousIntegrationBuild=true",
                    "-property:UseSharedCompilation=false",
                    "-property:ProvideCommandLineArgs=true",
                    "-property:EnableSourceControlManagerQueries=false", "-property:EnableSourceLink=false",
                    "-property:SourceRevisionId=" + approval.SourceRevision,
                    "-property:Configuration=" + EvaluatorConfiguration.BuildConfiguration,
                    "-getProperty:TargetPath,TargetFramework,SkipCompilerExecution",
                    "-getItem:CscCommandLineArgs,VbcCommandLineArgs" };
                commands.Add("dotnet " + string.Join(" ", arguments));
                var result = await Build(source, arguments);
                if (result.ExitCode != 0) throw new InvalidDataException("Approved producer build failed: " + result.Error + result.Output);
                using var json = JsonDocument.Parse(result.Output);
                var path = Path.GetFullPath(json.RootElement.GetProperty("Properties").GetProperty("TargetPath").GetString()!);
                if (!path.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    throw new InvalidDataException("Build did not produce its evaluated target inside the fresh source workspace.");
                var items = json.RootElement.GetProperty("Items");
                var args = new[] { "CscCommandLineArgs", "VbcCommandLineArgs" }
                    .SelectMany(name => items.GetProperty(name).EnumerateArray())
                    .Select(item => item.GetProperty("Identity").GetString()!).ToArray();
                var compiled = args.SingleOrDefault(argument => argument.StartsWith("/out:", StringComparison.OrdinalIgnoreCase));
                if (compiled == null || json.RootElement.GetProperty("Properties").GetProperty("SkipCompilerExecution").GetString()
                    ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                    throw new InvalidDataException("Build has no executed managed compiler producer.");
                var compilerOutput = Path.GetFullPath(compiled[5..].Trim('"'), Path.GetDirectoryName(copied)!);
                if (!compilerOutput.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(compilerOutput) || HashFile(compilerOutput) != HashFile(path))
                    throw new InvalidDataException("Evaluated target does not match the freshly compiled producer output.");
                compilerInputs[Path.GetRelativePath(source, copied)] =
                    Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(args)));
                if (!outputs.TryAdd(Path.GetFileName(path), path))
                    throw new InvalidDataException("Ambiguous producer assembly names.");
            }
            if (ExternalReplay.HashSourceTree(source) != buildInputHash ||
                ExternalReplay.HashSourceTree(original) != approval.SourceHash)
                throw new InvalidDataException("A producer build changed approved source.");
            var artifacts = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var artifactTrees = outputs.Values.Select(p => Path.GetDirectoryName(p)!).Distinct()
                .ToDictionary(directory => directory, ToolReplay.HashTree, StringComparer.OrdinalIgnoreCase);
            foreach (var directory in artifactTrees.Keys)
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                    artifacts[Path.GetRelativePath(source, file)] = HashFile(file);
            var cases = plan.Cases.Select(fixture =>
            {
                var args = fixture.Command.Arguments.Select(argument =>
                {
                    if (!Path.IsPathFullyQualified(argument)) return argument;
                    if (!outputs.TryGetValue(Path.GetFileName(argument), out var produced))
                        throw new InvalidDataException("Command artifact has no declared freshly built source producer: " + argument);
                    return produced;
                }).ToArray();
                return fixture with { Command = fixture.Command with { Arguments = args } };
            }).ToArray();
            var local = await ToolReplay.RunLocal(plan with { Cases = cases }, policy, artifactRoot);
            if (artifacts.Any(a => !File.Exists(Path.Combine(source, a.Key)) ||
                HashFile(Path.Combine(source, a.Key)) != a.Value) ||
                artifactTrees.Any(a => ToolReplay.HashTree(a.Key) != a.Value) ||
                ExternalReplay.HashSourceTree(source) != buildInputHash ||
                ExternalReplay.HashSourceTree(original) != approval.SourceHash)
                throw new InvalidDataException("Producer source or binary/dependencies changed during replay.");
            return local with
            {
                ReferenceVerified = local.LocalEvidencePassed,
                ExecutionBoundary = "owned-reference-reviewed; NOT isolated",
                Message = local.LocalEvidencePassed
                    ? "Exact reviewed source was freshly built; only evaluated produced binaries were replayed. Owned-reference acceptance, NOT formal candidate isolation."
                    : "Reviewed producer build succeeded but actual replay failed; TOOL002 remains mandatory.",
                ReferenceBuild = new(approval.ReviewId, approval.Reviewer, approval.SourceRevision,
                    approval.SourceHash, buildInputHash, commands.ToArray(), compilerInputs, artifacts)
            };
        }
        catch (Exception error)
        {
            return new(false, [], "Owned-reference producer verification failed: " + error.Message);
        }
        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
    }

    private static async Task<(int ExitCode, string Output, string Error)> Build(string workingDirectory, string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        ToolReplay.FilterEnvironment(start);
        ToolReplay.ConfigureRuntimeState(start, Path.Combine(Path.GetDirectoryName(workingDirectory)!, "build-runtime-state"));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start approved producer build.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await Task.WhenAll(process.WaitForExitAsync(), output, error).WaitAsync(TimeSpan.FromMinutes(5)); }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            throw;
        }
        return (process.ExitCode, await output, await error);
    }
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void CopySource(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Reviewed source cannot contain reparse points.");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reviewed source cannot contain reparse points.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            if (Path.GetFileName(directory).ToLowerInvariant() is not ("bin" or "obj" or "artifacts" or ".git"))
                CopySource(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}

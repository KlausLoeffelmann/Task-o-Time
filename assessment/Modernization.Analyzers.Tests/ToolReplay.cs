using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ExternalEvaluation;

namespace Modernization.Analyzers.Tests;

internal sealed record ReplayCommand(string Executable, string[] Arguments);
internal sealed record ReplayCase(string Name, string Kind, ReplayCommand Command, string InputDirectory,
    string? ExpectedDirectory, string? BehaviorFile = null, bool Unsupported = false,
    bool Idempotent = false, bool Checkpoint = false);
internal sealed record ReplayPlan(string[] Projects, ReplayCase[] Cases,
    string[]? SourceRoots = null, string[]? ArtifactRoots = null);
public sealed record ReplayEvidence(string Name, bool Passed, string Message, string InputHash,
    string OutputHash, string Command, int ExitCode, string StandardOutput, string StandardError)
{
    public SortedDictionary<string, string> ToolArtifactHashes { get; init; } = new(StringComparer.Ordinal);
}
public sealed record ReplayResult(bool Verified, ReplayEvidence[] Cases, string Message)
{
    public bool LocalEvidencePassed { get; init; }
    public bool ReferenceVerified { get; init; }
    public ReferenceBuildEvidence? ReferenceBuild { get; init; }
    public string ExecutionBoundary { get; init; } = "not-executed";
}

// Execution is deliberately outside DiagnosticAnalyzer callbacks. Plans and expected
// outputs belong to the assessor; candidates supply only an executable/interface.
internal static class ToolReplay
{
    internal static ReplayPlan? Plan
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("ASSESSMENT_REPLAY_PLAN");
            if (path == null) return null;
            TrustedPath(path);
            return JsonSerializer.Deserialize<ReplayPlan>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Empty trusted replay plan.");
        }
    }
    internal static string[] DeclaredProjects => Plan?.Projects.Select(Path.GetFullPath).ToArray() ?? [];
    internal static string TrustedPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(EvaluatorConfiguration.AssessmentRoot).TrimEnd('\\') + "\\";
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Replay fixtures/plans must be in the private assessor tree: " + full);
        for (var current = full; current.Length >= root.Length; current = Path.GetDirectoryName(current)!)
            if (File.Exists(current) || Directory.Exists(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Replay input cannot traverse a reparse point.");
        return full;
    }

    internal static async Task<ReplayResult> Run(StagePolicy policy, string artifactRoot)
    {
        if (!policy.ProjectReplay && !policy.LanguageReplay)
            return new(false, [], "Migration replay is not applicable to an S3 starting point.");
        var plan = Plan;
        if (plan == null) return new(false, [], "No trusted CLI replay plan supplied.");
        var execution = Environment.GetEnvironmentVariable("ASSESSMENT_REPLAY_EXECUTION") ?? "external-receipt";
        if (execution == "external-receipt")
            return ExternalReplay.VerifyConfigured(plan, policy);
        if (execution == "reference-reviewed")
            return await ReferenceReplay.RunConfigured(plan, policy, artifactRoot);
        if (execution != "local-reviewed")
            return new(false, [], "Unknown execution policy; no candidate command was executed.");
        return await RunLocal(plan, policy, artifactRoot);
    }
    internal static async Task<ReplayResult> RunLocal(ReplayPlan plan, StagePolicy policy, string artifactRoot)
    {
        var results = new List<ReplayEvidence>();
        foreach (var fixture in plan.Cases)
            results.Add(await RunCase(fixture, artifactRoot));
        var complete = HasRequiredCoverage(plan, policy);
        return new(false, results.ToArray(),
            "Local reviewed-source development replay is NOT isolated submission verification; TOOL002 remains mandatory. " +
            (complete ? "" : "Required fixture/checkpoint coverage is incomplete."))
        {
            LocalEvidencePassed = complete && results.Count > 0 && results.All(r => r.Passed),
            ExecutionBoundary = "local-reviewed-unisolated"
        };
    }
    internal static bool HasRequiredCoverage(ReplayPlan plan, StagePolicy policy)
    {
        var required = policy.LanguageReplay ? new[] { "language", "project" } :
            policy.ProjectReplay ? new[] { "project" } : [];
        return required.Length > 0 && plan.Projects.Length > 0 &&
            plan.Cases.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() == plan.Cases.Length &&
            required.All(kind =>
            plan.Cases.Any(c => c.Kind == kind && !c.Unsupported && !c.Checkpoint &&
                (kind != "language" || c.BehaviorFile != null) && (kind != "project" || c.Idempotent)) &&
            plan.Cases.Any(c => c.Kind == kind && c.Unsupported) &&
            plan.Cases.Any(c => c.Kind == kind && c.Checkpoint && !c.Unsupported));
    }

    internal static async Task<ReplayEvidence> RunCase(ReplayCase fixture, string artifactRoot)
    {
        var work = Path.Combine(artifactRoot, "replay", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var input = Path.Combine(work, "input");
        var output = Path.Combine(work, "output");
        string before = "", after = "";
        (int Code, string Output, string Error) run = (-1, "", "");
        var command = fixture.Command.Executable + " " + string.Join(" ", fixture.Command.Arguments);
        var tools = new SortedDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            // Freeze expectations before invoking even reviewed local code. This catches
            // persistent corruption, not read access or a modify-and-restore attack.
            var expected = fixture.Unsupported ? null :
                TrustedPath(fixture.ExpectedDirectory ?? throw new InvalidDataException("Expected output required."));
            var expectedHash = expected == null ? "" : HashTree(expected);
            var behavior = fixture.BehaviorFile == null ? null : File.ReadAllText(TrustedPath(fixture.BehaviorFile));
            var expectedSnapshot = expected == null ? null : Snapshot(expected);
            void CheckExpectations()
            {
                if (expected != null && HashTree(expected) != expectedHash ||
                    fixture.BehaviorFile != null && File.ReadAllText(TrustedPath(fixture.BehaviorFile)) != behavior)
                    throw new InvalidDataException("Trusted expectations changed during local replay.");
            }
            foreach (var file in fixture.Command.Arguments.Prepend(fixture.Command.Executable)
                .Where(p => Path.IsPathFullyQualified(p) && File.Exists(p)))
                tools[Path.GetFullPath(file)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            CopyTree(TrustedPath(fixture.InputDirectory), input);
            before = HashTree(input);
            run = await Execute(fixture.Command, input, output, work);
            CheckExpectations();
            if (HashTree(input) != before) throw new InvalidDataException("CLI modified its input.");
            if (fixture.Unsupported)
            {
                if (run.Code == 0 || string.IsNullOrWhiteSpace(run.Output + run.Error) ||
                    Directory.Exists(output) && Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Any())
                    throw new InvalidDataException("Unsupported input must fail with diagnostics and no partial output.");
            }
            else
            {
                if (run.Code != 0) throw new InvalidDataException("CLI failed.");
                if (!Directory.Exists(output)) throw new InvalidDataException("CLI emitted no output.");
                CompareSnapshot(expectedSnapshot!, output);
                after = HashTree(output);
                if (fixture.BehaviorFile != null)
                    await VerifyBehavior(output, behavior!);
                var repeated = Path.Combine(work, "repeated");
                var second = await Execute(fixture.Command, input, repeated, work);
                if (second.Code != 0 || HashTree(repeated) != after)
                    throw new InvalidDataException("Repeated conversion is not deterministic.");
                if (HashTree(input) != before) throw new InvalidDataException("Repeated CLI modified its input.");
                if (fixture.Idempotent)
                {
                    var idempotent = Path.Combine(work, "idempotent");
                    var third = await Execute(fixture.Command, output, idempotent, work);
                    if (third.Code != 0 || HashTree(idempotent) != after || HashTree(output) != after)
                        throw new InvalidDataException("Project migration is not idempotent.");
                }
            }
            if (tools.Any(file => !File.Exists(file.Key) ||
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Key))) != file.Value))
                throw new InvalidDataException("CLI executable artifacts changed during replay.");
            CheckExpectations();
            return new(fixture.Name, true, "Local output checks passed; no isolation claim.", before, after, command, run.Code, run.Output, run.Error)
                { ToolArtifactHashes = tools };
        }
        catch (Exception error)
        {
            return new(fixture.Name, false, error.GetType().Name + ": " + error.Message,
                before, after, command, run.Code, run.Output, run.Error) { ToolArtifactHashes = tools };
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static async Task<(int Code, string Output, string Error)> Execute(ReplayCommand command,
        string input, string output, string workingDirectory)
    {
        if (!command.Arguments.Any(a => a.Contains("{input}", StringComparison.Ordinal)) ||
            !command.Arguments.Any(a => a.Contains("{output}", StringComparison.Ordinal)))
            throw new InvalidDataException("CLI contract requires explicit {input} and {output} arguments.");
        var start = new ProcessStartInfo(command.Executable)
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        FilterEnvironment(start);
        ConfigureRuntimeState(start, Path.Combine(workingDirectory, "runtime-state"));
        foreach (var argument in command.Arguments)
            start.ArgumentList.Add(argument.Replace("{input}", input).Replace("{output}", output));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("CLI did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        return (process.ExitCode, await stdout, await stderr);
    }
    internal static void FilterEnvironment(ProcessStartInfo start)
    {
        var allowed = new HashSet<string>(["PATH", "SystemRoot", "WINDIR", "COMSPEC", "PATHEXT",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_MULTILEVEL_LOOKUP", "ProgramFiles", "ProgramFiles(x86)",
            "ProgramW6432", "ProgramData", "ALLUSERSPROFILE", "PROCESSOR_ARCHITECTURE", "OS"], StringComparer.OrdinalIgnoreCase);
        foreach (var key in start.Environment.Keys.ToArray())
            if (!allowed.Contains(key)) start.Environment.Remove(key);
    }
    internal static void ConfigureRuntimeState(ProcessStartInfo start, string directory)
    {
        Directory.CreateDirectory(directory);
        start.Environment["TEMP"] = directory;
        start.Environment["TMP"] = directory;
        start.Environment["DOTNET_CLI_HOME"] = directory;
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["UseSharedCompilation"] = "false";
        var profile = Path.Combine(directory, "profile");
        foreach (var (name, path) in new[] {
            ("USERPROFILE", profile), ("HOME", profile),
            ("APPDATA", Path.Combine(profile, "AppData", "Roaming")),
            ("LOCALAPPDATA", Path.Combine(profile, "AppData", "Local")),
            ("NUGET_PACKAGES", Path.Combine(directory, "packages")),
            ("NUGET_HTTP_CACHE_PATH", Path.Combine(directory, "http-cache")) })
        {
            Directory.CreateDirectory(path);
            start.Environment[name] = path;
        }
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        if (Directory.Exists(fallback)) start.Environment["NUGET_FALLBACK_PACKAGES"] = fallback;
    }

    private static SortedDictionary<string, string> Files(string directory)
    {
        if (!Directory.Exists(directory)) throw new InvalidDataException("Missing replay directory: " + directory);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<string>([directory]);
        while (pending.TryPop(out var current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Replay trees cannot contain reparse points.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Replay trees cannot contain reparse points.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else result.Add(Path.GetRelativePath(directory, entry), entry);
            }
        }
        if (result.Count == 0) throw new InvalidDataException("Empty replay tree.");
        return result;
    }
    private static void CopyTree(string source, string destination)
    {
        foreach (var (relative, path) in Files(source))
        {
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
    }
    internal static string HashTree(string directory) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Files(directory).Select(f => f.Key + ":" +
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Value))))))));
    internal static void CompareTrees(string expected, string actual)
    {
        CompareSnapshot(Snapshot(expected), actual);
    }
    private static SortedDictionary<string, byte[]> Snapshot(string directory) =>
        new(Files(directory).ToDictionary(f => f.Key, f => File.ReadAllBytes(f.Value)), StringComparer.Ordinal);
    private static void CompareSnapshot(SortedDictionary<string, byte[]> a, string actual)
    {
        var b = Files(actual);
        if (!a.Keys.SequenceEqual(b.Keys)) throw new InvalidDataException("Emitted file set differs from trusted checkpoint.");
        foreach (var key in a.Keys)
        {
            if (Path.GetExtension(key) == ".cs")
            {
                using var reader = new StreamReader(new MemoryStream(a[key]), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var left = CSharpSyntaxTree.ParseText(reader.ReadToEnd()).GetRoot().NormalizeWhitespace().ToFullString();
                var right = CSharpSyntaxTree.ParseText(File.ReadAllText(b[key])).GetRoot().NormalizeWhitespace().ToFullString();
                if (left != right) throw new InvalidDataException("Emitted content differs from trusted checkpoint: " + key);
            }
            else if (!a[key].SequenceEqual(File.ReadAllBytes(b[key])))
                throw new InvalidDataException("Emitted content differs from trusted checkpoint: " + key);
        }
    }
    internal static async Task VerifyBehavior(string output, string behavior)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var trees = Files(output).Where(f => Path.GetExtension(f.Key) == ".cs")
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f.Value), path: f.Key))
            .Append(CSharpSyntaxTree.ParseText(behavior, path: "TrustedBehavior.cs"));
        var compilation = CSharpCompilation.Create("Replay_" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        if (compilation.GetEntryPoint(CancellationToken.None)?.ReturnType.SpecialType != SpecialType.System_Int32)
            throw new InvalidDataException("Trusted behavior must have an integer-returning Main.");
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        if (!emit.Success) throw new InvalidDataException("Emitted output/behavior compilation failed: " +
            string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var directory = Path.Combine(Path.GetDirectoryName(output)!, "behavior-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, "Behavior.dll");
            await File.WriteAllBytesAsync(executable, image.ToArray());
            await File.WriteAllTextAsync(Path.Combine(directory, "Behavior.runtimeconfig.json"), RuntimeConfig);
            var result = await Execute(new("dotnet", [executable, "{input}", "{output}"]), output,
                Path.Combine(directory, "unused"), directory);
            if (result.Code != 0) throw new InvalidDataException("Behavior failed: " + result.Output + result.Error);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    internal const string RuntimeConfig = """{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""";
}

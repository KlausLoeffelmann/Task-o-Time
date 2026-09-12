using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using ICSharpCode.CodeConverter.Common;
using ICSharpCode.CodeConverter.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace TaskOTime.Migration;

public static class Migration
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static async Task<int> RunAsync(string[] args)
    {
        RunManifest? manifest = null;
        string? staging = null;
        try {
            var options = Options.Parse(args);
            var input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Input));
            var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Output));
            if (!Directory.Exists(input)) throw new MigrationException("INPUT", $"Directory not found: {input}");
            RejectReparseAncestors(input);
            RejectReparseAncestors(output);
            if (Within(input, output) || Within(output, input))
                throw new MigrationException("COLLISION", "Input and output must be disjoint directories.");
            staging = output + ".incomplete";
            if (new[] { output, staging, output + ".publishing" }.Any(p => Directory.Exists(p) || File.Exists(p)))
                throw new MigrationException("OUTPUT_EXISTS", "Output and its .incomplete/.publishing staging paths must not exist.");
            var files = Files(input).ToArray();
            var projects = options.Projects.Count > 0
                ? options.Projects.Select(p => SafeRelative(input, p)).Order(StringComparer.Ordinal).ToArray()
                : files.Where(p => p.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (projects.Length == 0) throw new MigrationException("NO_VB", "No eligible VB projects. Already converted trees require no language conversion.");
            foreach (var project in projects) {
                if (!project.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(input, project)))
                    throw new MigrationException("PROJECT", $"Expected an existing .vbproj: {project}");
                ProjectEdits.Check(Path.Combine(input, project));
            }
            ProjectEdits.ValidateDestinations(input, projects.ToDictionary(p => p, p => Path.ChangeExtension(p, ".csproj"), StringComparer.OrdinalIgnoreCase));
            manifest = new RunManifest {
                Configuration = options.Configuration, Projects = projects,
                InputFiles = HashFiles(input, files), Status = options.DryRun ? "planned" : "incomplete",
                Rules = ["ICSharpCode project-context VBToCS", "structured project/reference edits", "WPF root namespace qualification"]
            };
            if (options.DryRun) {
                Console.WriteLine(JsonSerializer.Serialize(manifest, JsonOptions));
                return 0;
            }
            Directory.CreateDirectory(staging);
            foreach (var file in files) {
                var target = Path.Combine(staging, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(input, file), target);
            }
            await SaveManifestAsync(staging, manifest);
            manifest.SdkVersion = await CompilerCapture.CheckSdkAsync(staging);
            foreach (var project in projects)
                await BuildAsync(staging, project, options.Configuration, options.Restore, "input", manifest);

            using var workspace = new AdhocWorkspace();
            workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), filePath: Path.Combine(staging, "Migration.slnx")));
            foreach (var project in projects)
                manifest.CompilerInputs.Add(await CompilerCapture.LoadAsync(workspace, staging, project, options.Configuration));
            var selected = workspace.CurrentSolution.Projects.OrderBy(p => p.FilePath, StringComparer.Ordinal).ToArray();
            var emitted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var expectedOutputs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in selected) {
                var compilation = await project.GetCompilationAsync() ?? throw new MigrationException("COMPILATION", project.Name);
                var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                if (errors.Length > 0) throw new MigrationException("INPUT_COMPILATION", string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
                var documents = project.Documents.Where(d => d.FilePath is not null && !IsGenerated(Path.GetRelativePath(staging, d.FilePath))).ToArray();
                if (documents.Length == 0) throw new MigrationException("EMPTY", $"No source documents: {project.Name}");
                foreach (var doc in documents) SafeRelative(staging, doc.FilePath!);
                manifest.Compilations.Add(new(project.Name, ((VisualBasicCompilationOptions)compilation.Options).RootNamespace,
                    documents.Length, project.Documents.Count() - documents.Length,
                    compilation.References.Select(r => r.Display ?? "").Order(StringComparer.Ordinal).Select(p => Normalize(staging, p)).ToArray()));
                await Console.Error.WriteLineAsync($"Converting {project.Name}: {documents.Length} source documents, {project.Documents.Count() - documents.Length} generated context documents.");
                await foreach (var result in ProjectConversion.ConvertDocumentsAsync<VBToCSConversion>(documents,
                    new ConversionOptions { AbandonOptionalTasksAfter = TimeSpan.FromMinutes(30) })) {
                    if (!result.Success || !string.IsNullOrWhiteSpace(result.GetExceptionsAsString()))
                        throw new MigrationException("ENGINE", $"{result.SourcePathOrNull}: {result.GetExceptionsAsString()}");
                    if (result.SourcePathOrNull is null || result.TargetPathOrNull is null)
                        throw new MigrationException("ENGINE_PATH", "Engine emitted a document without an owned path.");
                    var source = SafeRelative(staging, result.SourcePathOrNull);
                    var target = SafeRelative(staging, result.TargetPathOrNull);
                    if (IsGenerated(target)) throw new MigrationException("GENERATED_OUTPUT", target);
                    var sourceDocument = documents.SingleOrDefault(d => string.Equals(d.FilePath, result.SourcePathOrNull, StringComparison.OrdinalIgnoreCase))
                        ?? throw new MigrationException("ENGINE_DOCUMENT", result.SourcePathOrNull);
                    var code = await EngineRepairs.ApplyAsync(result.ConvertedCode, sourceDocument, compilation, manifest.AppliedRepairs);
                    if (!emitted.TryAdd(target, code)) throw new MigrationException("DUPLICATE_OUTPUT", target);
                    mapping.Add(source, target);
                }
                foreach (var doc in documents)
                    if (!mapping.ContainsKey(Path.GetRelativePath(staging, doc.FilePath!)))
                        throw new MigrationException("MISSING_OUTPUT", doc.FilePath!);
                mapping.Add(Path.GetRelativePath(staging, project.FilePath!), Path.ChangeExtension(Path.GetRelativePath(staging, project.FilePath!), ".csproj"));
                expectedOutputs.Add(Path.GetRelativePath(staging, project.FilePath!),
                    documents.Select(d => mapping[Path.GetRelativePath(staging, d.FilePath!)]).ToArray());
            }
            ProjectEdits.ValidateDestinations(staging, mapping);
            foreach (var project in selected)
                await WpfEdits.ApplyAsync(project, manifest.AppliedRepairs);
            ProjectEdits.Apply(staging, mapping, manifest.Compilations);
            foreach (var (target, code) in emitted) {
                if (File.Exists(Path.Combine(staging, target))) throw new MigrationException("FILE_COLLISION", target);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(staging, target))!);
                await File.WriteAllTextAsync(Path.Combine(staging, target), code);
            }
            foreach (var source in mapping.Keys) File.Delete(Path.Combine(staging, source));
            foreach (var path in Directory.GetDirectories(staging, "*", SearchOption.AllDirectories)
                         .Where(p => Path.GetFileName(p) is "obj" or "bin").OrderByDescending(p => p.Length))
                if (Directory.Exists(path)) Directory.Delete(path, true);
            workspace.Dispose();
            foreach (var project in projects) {
                await BuildAsync(staging, mapping[project], options.Configuration, true, "output", manifest);
                manifest.CompiledOutputs.Add(await CompilerCapture.VerifyOutputAsync(staging, mapping[project], expectedOutputs[project], options.Configuration));
            }
            var currentHashes = HashFiles(input, Files(input));
            if (!manifest.InputFiles.SequenceEqual(currentHashes)) throw new MigrationException("INPUT_CHANGED", "Input changed during conversion.");
            manifest.OutputFiles = HashFiles(staging, Files(staging));
            manifest.Changes = mapping.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new Change(p.Key, p.Value)).ToArray();
            var inputHashes = manifest.InputFiles.ToDictionary(f => f.Path, f => f.Sha256, StringComparer.OrdinalIgnoreCase);
            var outputHashes = manifest.OutputFiles.ToDictionary(f => f.Path, f => f.Sha256, StringComparer.OrdinalIgnoreCase);
            manifest.ChangedFiles = inputHashes.Keys.Union(outputHashes.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(p => inputHashes.GetValueOrDefault(p) != outputHashes.GetValueOrDefault(p)).Order(StringComparer.Ordinal).ToArray();
            manifest.Status = "validated-build-workspace";
            await SaveManifestAsync(staging, manifest);
            manifest.Status = "succeeded";
            await PublishAsync(staging, output, manifest);
            Console.WriteLine($"Converted {projects.Length} projects, {emitted.Count} documents. Manifest: {Path.Combine(output, "migration-manifest.json")}");
            return 0;
        }
        catch (Exception exception) {
            var code = exception is MigrationException known ? known.Code : "UNEXPECTED";
            await Console.Error.WriteLineAsync($"{code}: {exception.Message}");
            if (manifest is not null && staging is not null && Directory.Exists(staging)) {
                manifest.Status = "failed";
                manifest.Diagnostics.Add(new(code, Normalize(staging, exception.ToString())));
                await SaveManifestAsync(staging, manifest);
            }
            return 1;
        }
    }

    internal static async Task BuildAsync(string root, string project, string configuration, bool restore, string phase, RunManifest manifest)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "build", project, "--configuration", configuration, "--verbosity", "quiet", "--nologo", "-nr:false", "-p:UseSharedCompilation=false", "-p:NuGetAudit=false" })
            start.ArgumentList.Add(arg);
        if (!restore) start.ArgumentList.Add("--no-restore");
        using var process = Process.Start(start) ?? throw new MigrationException("BUILD", "Cannot start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var text = Normalize(root, await stdout + await stderr);
        manifest.Builds.Add(new(phase, project, process.ExitCode, text));
        if (process.ExitCode != 0)
            throw new MigrationException("BUILD", $"{phase} build failed for {project}. Use --restore if dependencies/assets are missing.\n{text}");
    }

    private static async Task PublishAsync(string staging, string output, RunManifest manifest)
    {
        // Build hosts can retain directory handles after exit on Windows.
        // Publish only validated source bytes from a directory no build host used.
        var publishing = output + ".publishing";
        Directory.CreateDirectory(publishing);
        foreach (var file in manifest.OutputFiles) {
            var target = Path.Combine(publishing, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(staging, file.Path), target);
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) != file.Sha256)
                throw new MigrationException("PUBLICATION_HASH", file.Path);
        }
        await SaveManifestAsync(publishing, manifest);
        Directory.Move(publishing, output);
    }

    internal static bool Within(string root, string path) =>
        string.Equals(root, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void RejectReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new MigrationException("REPARSE_POINT", $"Linked input/output ancestry is unsupported: {directory.FullName}");
    }
    internal static string SafeRelative(string root, string path)
    {
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!Within(root, full)) throw new MigrationException("OUTSIDE_INPUT", path);
        return Path.GetRelativePath(root, full);
    }
    internal static bool IsGenerated(string relative) => relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(p => p.Equals("obj", StringComparison.OrdinalIgnoreCase) || p.Equals("bin", StringComparison.OrdinalIgnoreCase));
    internal static IEnumerable<string> Files(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal)) {
            var name = Path.GetFileName(path);
            if (name is ".git" or ".vs" or "bin" or "obj" or "artifacts" or "migration-manifest.json") continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new MigrationException("REPARSE_POINT", $"Linked files/directories are unsupported: {path}");
            if (Directory.Exists(path)) {
                foreach (var child in Files(path)) yield return Path.Combine(name, child);
            } else yield return name;
        }
    }
    internal static FileHash[] HashFiles(string root, IEnumerable<string> files) => files.Order(StringComparer.Ordinal)
        .Select(p => new FileHash(p, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, p)))))).ToArray();
    internal static string Normalize(string root, string text) => text.Replace(root, "<workspace>", StringComparison.OrdinalIgnoreCase);
    private static Task SaveManifestAsync(string root, RunManifest manifest) =>
        File.WriteAllTextAsync(Path.Combine(root, "migration-manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
}

public sealed class MigrationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record FileHash(string Path, string Sha256);
public sealed record Change(string Source, string Target);
public sealed record RunDiagnostic(string Code, string Message);
public sealed record BuildEvidence(string Phase, string Project, int ExitCode, string Log);
public sealed record CompilationEvidence(string Project, string RootNamespace, int SourceDocuments, int GeneratedContextDocuments, string[] References);
public sealed record CompilerInputEvidence(string Project, string[] Arguments, FileHash[] References);
public sealed class RunManifest
{
    public int SchemaVersion { get; } = 1;
    public string ToolVersion { get; } = "1.0.1";
    public string Engine { get; } = "ICSharpCode.CodeConverter/10.0.1.923 (MIT)";
    public string Roslyn { get; } = "4.14.0";
    public string SdkVersion { get; set; } = "10.0.401 (required; not yet evaluated)";
    public string ToolSha256 { get; } = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Migration).Assembly.Location)));
    public string EngineSha256 { get; } = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(VBToCSConversion).Assembly.Location)));
    public string Status { get; set; } = "";
    public string Configuration { get; set; } = "";
    public string[] Projects { get; set; } = [];
    public string[] Rules { get; set; } = [];
    public FileHash[] InputFiles { get; set; } = [];
    public FileHash[] OutputFiles { get; set; } = [];
    public Change[] Changes { get; set; } = [];
    public string[] ChangedFiles { get; set; } = [];
    public List<CompilationEvidence> Compilations { get; } = [];
    public List<CompilerInputEvidence> CompilerInputs { get; } = [];
    public List<OutputCompilationEvidence> CompiledOutputs { get; } = [];
    public List<BuildEvidence> Builds { get; } = [];
    public List<RunDiagnostic> Diagnostics { get; } = [];
    public List<string> AppliedRepairs { get; } = [];
}

public sealed record OutputCompilationEvidence(string Project, string[] EmittedSources);

internal sealed record Options(string Input, string Output, List<string> Projects, string Configuration, bool DryRun, bool Restore)
{
    public static Options Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "convert-language")
            throw new MigrationException("USAGE", "convert-language --input <directory> --output <new-directory> [--project <relative.vbproj>] [--configuration Debug|Release] [--dry-run] [--restore]");
        string? input = null, output = null;
        var projects = new List<string>();
        var configuration = "Debug";
        bool dryRun = false, restore = false;
        for (var i = 1; i < args.Length; i++) {
            string Value() => ++i < args.Length ? args[i] : throw new MigrationException("USAGE", "Missing option value.");
            switch (args[i]) {
                case "--input": input = Value(); break;
                case "--output": output = Value(); break;
                case "--project": projects.Add(Value()); break;
                case "--configuration": configuration = Value(); break;
                case "--dry-run": dryRun = true; break;
                case "--restore": restore = true; break;
                case "--from": if (Value() != "vb") throw new MigrationException("LANGUAGE", "Only VB -> C# is supported."); break;
                case "--to": if (Value() != "cs") throw new MigrationException("LANGUAGE", "Only VB -> C# is supported."); break;
                default: throw new MigrationException("USAGE", $"Unknown argument: {args[i]}");
            }
        }
        if (input is null || output is null) throw new MigrationException("USAGE", "--input and --output are required.");
        if (configuration is not ("Debug" or "Release")) throw new MigrationException("CONFIGURATION", "Only Debug and Release are currently supported.");
        return new(input, output, projects, configuration, dryRun, restore);
    }
}

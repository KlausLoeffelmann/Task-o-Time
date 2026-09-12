using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace TaskOTime.Migration;

internal static class CompilerCapture
{
    internal static async Task<string> CheckSdkAsync(string root)
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", "--version") {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true
        }) ?? throw new MigrationException("SDK", "Cannot start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var version = (await stdout).Trim();
        if (process.ExitCode != 0 || version != "10.0.401")
            throw new MigrationException("SDK", $"SDK 10.0.401 is required in the output workspace; selected '{version}'. Place output below this tool's global.json or pin the input graph. {await stderr}");
        return version;
    }

    internal static async Task<CompilerInputEvidence> LoadAsync(AdhocWorkspace workspace, string root, string project, string configuration)
    {
        var fullPath = Path.Combine(root, project);
        var directory = Path.GetDirectoryName(fullPath)!;
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "msbuild", fullPath, "-t:Rebuild", "-p:ProvideCommandLineArgs=true", "-p:BuildProjectReferences=false",
                     "-p:Configuration=" + configuration, "-getItem:VbcCommandLineArgs", "-nr:false", "-p:UseSharedCompilation=false", "-nologo", "-verbosity:quiet" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new MigrationException("CAPTURE", "Cannot start dotnet msbuild.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var text = await stdout;
        if (process.ExitCode != 0) throw new MigrationException("CAPTURE", text + await stderr);
        var jsonStart = text.IndexOf('{');
        if (jsonStart < 0) throw new MigrationException("CAPTURE", "MSBuild returned no compiler arguments.");
        using var json = JsonDocument.Parse(text[jsonStart..]);
        var arguments = json.RootElement.GetProperty("Items").GetProperty("VbcCommandLineArgs")
            .EnumerateArray().Select(e => e.GetProperty("Identity").GetString()!).ToArray();
        if (arguments.Length == 0) throw new MigrationException("CAPTURE", $"No Vbc compiler invocation captured for {project}.");
        var parsed = VisualBasicCommandLineParser.Default.Parse(arguments, directory, directory);
        var errors = parsed.Errors.Where(e => e.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0) throw new MigrationException("CAPTURE_PARSE", string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        var references = parsed.MetadataReferences.Select(r => {
            var path = Path.GetFullPath(Path.Combine(directory, r.Reference));
            if (!File.Exists(path)) throw new MigrationException("REFERENCE", $"Missing compiler reference: {path}");
            // File-backed metadata can keep Framework DLLs mapped and prevent
            // atomic workspace publication on Windows until this process exits.
            return MetadataReference.CreateFromImage(ImmutableArray.Create(File.ReadAllBytes(path)), r.Properties, filePath: path);
        }).ToArray();
        var id = ProjectId.CreateNewId();
        var documents = parsed.SourceFiles.Select(source => {
            var path = Path.GetFullPath(Path.Combine(directory, source.Path));
            Migration.SafeRelative(root, path);
            using var stream = File.OpenRead(path);
            var content = SourceText.From(stream, parsed.Encoding ?? Encoding.UTF8);
            return DocumentInfo.Create(DocumentId.CreateNewId(id), Path.GetFileName(path),
                loader: TextLoader.From(TextAndVersion.Create(content, VersionStamp.Create(), path)), filePath: path);
        }).ToArray();
        workspace.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), Path.GetFileNameWithoutExtension(fullPath),
            parsed.CompilationName ?? Path.GetFileNameWithoutExtension(fullPath), LanguageNames.VisualBasic,
            filePath: fullPath, compilationOptions: parsed.CompilationOptions, parseOptions: parsed.ParseOptions,
            documents: documents, metadataReferences: references));
        return new(project, arguments.Select(a => Migration.Normalize(root, a)).ToArray(),
            references.Select(r => new FileHash(Migration.Normalize(root, r.FilePath!),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(r.FilePath!))))).ToArray());
    }
}

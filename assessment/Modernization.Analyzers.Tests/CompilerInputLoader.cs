using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Security.Cryptography;
using ExternalEvaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Modernization.Analyzers.Tests;

internal sealed record LoadedProject(string Path, string Name, string OutputPath, Compilation Compilation,
    bool IsTest, bool IsTooling, string[] GeneratedPaths, AdditionalText[] AdditionalFiles, string? TargetRefPath = null,
    ProjectState? State = null, string[]? SourceDataPaths = null);
internal sealed record FixtureDataRole(string Owner, string Root)
{
    internal static bool Contains(string root, string path) => Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static FixtureDataRole[] Read(XDocument policy, string sourceRoot) =>
        (policy.Root?.Element("Discovery")?.Elements("FixtureData") ?? []).Select(element =>
        {
            string PathAttribute(string name) => !string.IsNullOrWhiteSpace(element.Attribute(name)?.Value)
                ? Path.GetFullPath(element.Attribute(name)!.Value, sourceRoot)
                : throw new InvalidDataException("Trusted fixture-data role is missing " + name + ".");
            return new FixtureDataRole(PathAttribute("Owner"), PathAttribute("Root"));
        }).ToArray();

    internal void Validate(LoadedProject owner)
    {
        if (!string.Equals(Owner, owner.Path, StringComparison.OrdinalIgnoreCase) || !owner.IsTest && !owner.IsTooling ||
            !Contains(Path.GetDirectoryName(Owner)!, Root) || Contains(Root, Owner))
            throw new InvalidDataException("Fixture data must be a proper subtree of its evaluated test/tool owner: " + Root);
        if (!(owner.SourceDataPaths ?? []).Any(path => Contains(Root, path)) ||
            owner.Compilation.SyntaxTrees.Any(tree => Contains(Root, tree.FilePath)))
            throw new InvalidDataException("Fixture-data role lacks evaluated source data or overlaps its owner's compiled source: " + Root);
    }
}
internal sealed record TestSupportRole(string Owner, string Project)
{
    internal static TestSupportRole[] Read(XDocument policy, string sourceRoot) =>
        (policy.Root?.Element("Discovery")?.Elements("TestSupport") ?? []).Select(element =>
            new TestSupportRole(Path.GetFullPath(element.Attribute("Owner")?.Value ??
                throw new InvalidDataException("Test-support role is missing Owner."), sourceRoot),
                Path.GetFullPath(element.Attribute("Project")?.Value ??
                throw new InvalidDataException("Test-support role is missing Project."), sourceRoot))).ToArray();
}
internal sealed record EvaluatedAssemblyReference(string[] AssemblyPaths, string[] SourceProjects);
internal sealed class SourceCompilationException(string project, IEnumerable<Diagnostic> diagnostics)
    : Exception("Source compilation failed for " + project)
{
    internal string Project { get; } = project;
    internal Diagnostic[] Diagnostics { get; } = diagnostics.ToArray();
}

// MSBuild supplies the exact compiler arguments, including WPF-generated VB and framework references.
// Project references are replaced by source compilations; stale frontend DLLs cannot decide the verdict.
internal sealed class CompilerInputLoader(string intermediateRoot, string configuration = "Debug", string profile = "unit")
{
    internal List<LoadedProject> Projects { get; } = [];
    private readonly HashSet<string> loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> metadataImages = new(StringComparer.OrdinalIgnoreCase);

    internal async Task<LoadedProject> Load(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        var cached = Projects.FirstOrDefault(p => string.Equals(p.Path, projectPath, StringComparison.OrdinalIgnoreCase));
        if (cached is not null) return cached;
        if (!loading.Add(projectPath)) throw new InvalidOperationException("Cyclic project reference: " + projectPath);
        var directory = Path.GetDirectoryName(projectPath)!;
        var intermediate = Path.Combine(intermediateRoot, CacheIdentity(projectPath, configuration, profile));
        Directory.CreateDirectory(intermediate);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        var sdkDirectory = Directory.GetParent(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "evaluator-sdk.txt")).Trim())!.FullName;
        if (!Path.GetFileName(sdkDirectory).StartsWith("10.", StringComparison.Ordinal))
            throw new InvalidOperationException("Compiler extraction requires the trusted .NET 10 SDK.");
        start.Environment["MSBuildSDKsPath"] = Path.Combine(sdkDirectory, "Sdks");
        start.Environment["DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR"] = Path.GetDirectoryName(Path.GetDirectoryName(sdkDirectory)!)!;
        start.Environment["DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR"] = Path.Combine(sdkDirectory, "Sdks");
        start.Environment["DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER"] = Path.GetFileName(sdkDirectory);
        foreach (var argument in new[]
        {
            Path.Combine(sdkDirectory, "MSBuild.dll"), projectPath, "-target:PrepareResourceNames;Compile", "-verbosity:quiet", "-nologo", "-nodeReuse:false",
            "-property:Configuration=" + configuration, "-property:BuildProjectReferences=false",
            "-property:IntermediateOutputPath=" + intermediate + Path.DirectorySeparatorChar,
            "-property:DesignTimeBuild=true", "-property:SkipCompilerExecution=true",
            "-property:ProvideCommandLineArgs=true",
            "-property:CustomAfterMicrosoftCommonTargets=" + Path.Combine(AppContext.BaseDirectory, "CompilerInputs.targets"),
            "-getItem:CscCommandLineArgs,VbcCommandLineArgs,ProjectReference,Compile,Page,ApplicationDefinition,EmbeddedResource,ReferencePath,ReferencePathWithRefAssemblies,PackageReference,None,Content",
            "-getProperty:TargetPath,TargetRefPath,AssemblyName,IsTestProject,TargetFramework,TargetFrameworkIdentifier,TargetFrameworkVersion,TargetPlatformIdentifier,Configuration,Configurations,RootNamespace,UsingMicrosoftNETSdk"
        }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dotnet MSBuild.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await Task.WhenAll(process.WaitForExitAsync(), outputTask, errorTask).WaitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Compiler input extraction timed out for " + projectPath);
        }
        var output = await outputTask;
        var errors = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"MSBuild compiler input extraction failed for {projectPath} (exit {process.ExitCode}):\n{errors}\n{output}");
        using var json = JsonDocument.Parse(output);
        var items = json.RootElement.GetProperty("Items");
        var properties = json.RootElement.GetProperty("Properties");
        var dependencies = new List<LoadedProject>();
        var compilerDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in items.GetProperty("ProjectReference").EnumerateArray())
        {
            var dependency = await Load(reference.GetProperty("FullPath").GetString()!);
            dependencies.Add(dependency);
            if (RequiresCompilerReference(reference)) compilerDependencies.Add(dependency.Path);
        }
        var vb = Path.GetExtension(projectPath).Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
        var arguments = items.GetProperty(vb ? "VbcCommandLineArgs" : "CscCommandLineArgs")
            .EnumerateArray().Select(a => a.GetProperty("Identity").GetString()!).ToArray();
        if (arguments.Length == 0) throw new InvalidOperationException("MSBuild returned no compiler arguments for " + projectPath);
        var sdkPath = arguments.FirstOrDefault(a => a.StartsWith("/sdkpath:", StringComparison.OrdinalIgnoreCase))?
            ["/sdkpath:".Length..].Trim('"');
        CommandLineArguments parsed = vb
            ? VisualBasicCommandLineParser.Default.Parse(arguments, directory, sdkPath)
            : CSharpCommandLineParser.Default.Parse(arguments, directory, sdkDirectory: null);
        var argumentErrors = parsed.Errors.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (argumentErrors.Length > 0)
            throw new InvalidOperationException("Compiler argument errors for " + projectPath + ":\n" + string.Join("\n", argumentErrors.Select(d => d.ToString())));
        var references = new List<MetadataReference>();
        var replaced = new HashSet<LoadedProject>();
        var evaluatedReferences = ReadEvaluatedReferences(items, directory);
        foreach (var reference in parsed.MetadataReferences)
        {
            var referencePath = Path.GetFullPath(reference.Reference, directory);
            var dependency = ResolveSourceProject(referencePath, Projects, evaluatedReferences);
            if (dependency is not null)
            {
                references.Add(SourceReference(dependency, reference.Properties, referencePath));
                replaced.Add(dependency);
            }
            else references.Add(MetadataReference.CreateFromFile(referencePath, reference.Properties));
        }
        foreach (var dependency in dependencies.Except(replaced).Where(d => compilerDependencies.Contains(d.Path)))
        {
            // A missing compiler reference is evidence of broken inputs, not permission to guess a reference.
            throw new InvalidOperationException($"Compiler input for {projectPath} omits project reference {dependency.OutputPath}");
        }
        // Unlike C#, VB's /nostdlib removes System.dll, not its implicit core library.
        // The command-line driver adds mscorlib from /sdkpath after parsing the reference switches.
        if (vb && properties.GetProperty("TargetFrameworkIdentifier").GetString() == ".NETFramework" &&
            !references.OfType<PortableExecutableReference>().Any(r =>
                string.Equals(Path.GetFileName(r.FilePath), "mscorlib.dll", StringComparison.OrdinalIgnoreCase)))
        {
            if (sdkPath is null) throw new InvalidOperationException("VB compiler inputs have no /sdkpath for the implicit core library.");
            references.Add(MetadataReference.CreateFromFile(Path.Combine(sdkPath, "mscorlib.dll")));
        }
        var trees = new List<SyntaxTree>();
        foreach (var source in parsed.SourceFiles)
        {
            var path = Path.GetFullPath(source.Path, directory);
            var text = SourceText.From(await File.ReadAllTextAsync(path), Encoding.UTF8);
            // Retain documentation syntax even when the candidate does not request a /doc output.
            trees.Add(vb ? VisualBasicSyntaxTree.ParseText(text, ((VisualBasicParseOptions)parsed.ParseOptions).WithDocumentationMode(DocumentationMode.Parse), path)
                : CSharpSyntaxTree.ParseText(text, ((CSharpParseOptions)parsed.ParseOptions).WithDocumentationMode(DocumentationMode.Parse), path));
        }
        var name = properties.GetProperty("AssemblyName").GetString()!;
        Compilation compilation = vb
            ? VisualBasicCompilation.Create(name, trees, references, (VisualBasicCompilationOptions)parsed.CompilationOptions)
            : CSharpCompilation.Create(name, trees, references, (CSharpCompilationOptions)parsed.CompilationOptions);
        var test = properties.GetProperty("IsTestProject").GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
            compilation.ReferencedAssemblyNames.Any(a => a.Name is "xunit.core" or "Microsoft.VisualStudio.TestPlatform.TestFramework" or "nunit.framework") ||
            name.Split('.').Any(s => s.EndsWith("Tests", StringComparison.Ordinal));
        var tooling = compilation.ReferencedAssemblyNames.Any(a => a.Name == "Microsoft.CodeAnalysis.VisualBasic") && !test;
        // A trusted replay declaration may identify a wrapper or project-only CLI without direct VB references.
        tooling |= ToolReplay.DeclaredProjects.Contains(projectPath, StringComparer.OrdinalIgnoreCase) && !test;
        var generated = items.GetProperty("Compile").EnumerateArray().Where(a =>
            a.TryGetProperty("AutoGen", out var v) && v.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            .Select(a => a.GetProperty("FullPath").GetString()!).ToArray();
        var additional = new List<AdditionalText>();
        foreach (var itemName in new[] { "Page", "ApplicationDefinition", "EmbeddedResource", "None", "Content" })
        foreach (var item in items.GetProperty(itemName).EnumerateArray())
        {
            var path = item.GetProperty("FullPath").GetString()!;
            if (Path.GetExtension(path).ToLowerInvariant() is ".xaml" or ".resx" ||
                tooling && Path.GetExtension(path).ToLowerInvariant() is ".vb" or ".cs")
            {
                if (!File.Exists(path)) throw new InvalidOperationException("Missing evaluated AdditionalFile: " + path);
                additional.Add(new InputFile(path, await File.ReadAllTextAsync(path)));
            }
        }
        var metadata = new XElement("Project", new XAttribute("Path", projectPath), new XAttribute("Name", name),
            new XAttribute("Test", test), new XAttribute("Tooling", tooling),
            new XAttribute("SdkStyle", properties.TryGetProperty("UsingMicrosoftNETSdk", out var sdk) &&
                sdk.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true),
            new XAttribute("TargetFramework", properties.TryGetProperty("TargetFramework", out var framework)
                ? framework.GetString() ?? "" : ""),
            new[] { "TargetFrameworkIdentifier", "TargetFrameworkVersion", "TargetPlatformIdentifier", "Configuration", "Configurations" }
                .Select(key => new XAttribute(key, properties.GetProperty(key).GetString() ?? "")),
            items.GetProperty("ReferencePath").EnumerateArray().Select(a =>
                new XElement("Reference", new XAttribute("Name", Path.GetFileNameWithoutExtension(a.GetProperty("Identity").GetString()!)))),
            items.GetProperty("PackageReference").EnumerateArray().Select(a =>
                new XElement("Package", new XAttribute("Name", a.GetProperty("Identity").GetString()!),
                    new XAttribute("Version", a.TryGetProperty("Version", out var v) ? v.GetString() ?? "" : ""))),
            dependencies.Select(d => new XElement("ProjectReference", new XAttribute("Path", d.Path),
                new XAttribute("ReferenceOutputAssembly", compilerDependencies.Contains(d.Path)))),
            items.GetProperty("EmbeddedResource").EnumerateArray().Where(a =>
                Path.GetExtension(a.GetProperty("FullPath").GetString()!).Equals(".resx", StringComparison.OrdinalIgnoreCase))
                .Select(a => new XElement("Resource", new XAttribute("Path", a.GetProperty("FullPath").GetString()!),
                    new XAttribute("BaseName", a.TryGetProperty("LogicalName", out var logical) && !string.IsNullOrEmpty(logical.GetString())
                        ? Path.ChangeExtension(logical.GetString(), null)!
                        : a.TryGetProperty("ManifestResourceName", out var manifest) ? manifest.GetString() ?? "" : ""))));
        additional.Add(new InputFile(projectPath + ".assessment", metadata.ToString()));
        var loaded = new LoadedProject(projectPath, name,
            Path.GetFullPath(properties.GetProperty("TargetPath").GetString()!, directory),
            compilation, test, tooling, generated, additional.DistinctBy(f => f.Path).ToArray(),
            properties.TryGetProperty("TargetRefPath", out var targetRef) && !string.IsNullOrWhiteSpace(targetRef.GetString())
                ? Path.GetFullPath(targetRef.GetString()!, directory) : null,
            ReadState(projectPath, compilation.Language, test, tooling, properties),
            new[] { "None", "Content" }.SelectMany(itemName => items.GetProperty(itemName).EnumerateArray())
                .Select(item => item.GetProperty("FullPath").GetString()!)
                .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".vb" or ".xaml")
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        Projects.Add(loaded);
        loading.Remove(projectPath);
        return loaded;
    }

    internal static string CacheIdentity(string path, string configuration, string profile) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(path).ToUpperInvariant() + "\n" + configuration + "\n" + profile)));

    internal static bool RequiresCompilerReference(JsonElement reference) =>
        !reference.TryGetProperty("ReferenceOutputAssembly", out var value) ||
        !string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase);

    internal static ProjectState ReadState(string path, string language, bool test, bool tooling, JsonElement properties)
    {
        string Value(string key) => properties.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
        return new(path, language, test, tooling, Value("UsingMicrosoftNETSdk").Equals("true", StringComparison.OrdinalIgnoreCase),
            Value("TargetFramework"), Value("TargetFrameworkIdentifier"), Value("TargetFrameworkVersion"),
            Value("TargetPlatformIdentifier"), Value("Configuration"), Value("Configurations"));
    }

    internal static LoadedProject[] ClassifyTestSupport(IEnumerable<LoadedProject> projects, string sourceRoot,
        IEnumerable<TestSupportRole>? supportRoles = null, IEnumerable<string>? productionRoots = null)
    {
        var all = projects.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        bool InTestDirectory(string path) => Path.GetRelativePath(sourceRoot, path).Split(Path.DirectorySeparatorChar)
            .SkipLast(1).Any(s => s.EndsWith("Tests", StringComparison.Ordinal));
        XElement[] References(LoadedProject p) => p.AdditionalFiles.Where(f => f.Path == p.Path + ".assessment")
            .SelectMany(f => Evidence.Xml(f).Descendants("ProjectReference")).ToArray();
        // Integration tests also launch real products. A build-only edge is not a
        // helper declaration; executable helpers need an affirmative trusted role.
        var roots = (productionRoots ?? []).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var helpers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in supportRoles ?? [])
        {
            if (!all.TryGetValue(role.Project, out _) || !all.TryGetValue(role.Owner, out var owner)) continue;
            if (!owner.IsTest || !References(owner).Any(e =>
                string.Equals(e.Attribute("Path")?.Value, role.Project, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Attribute("ReferenceOutputAssembly")?.Value, "false", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Trusted test-support role lacks an evaluated build-only test owner: " + role.Project);
            if (!roots.Contains(role.Project)) helpers.Add(role.Project);
        }
        static bool Executable(LoadedProject p) => p.Compilation.Options.OutputKind is
            OutputKind.ConsoleApplication or OutputKind.WindowsApplication or OutputKind.WindowsRuntimeApplication;
        var production = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<LoadedProject>(all.Values.Where(p => roots.Contains(p.Path) ||
            !p.IsTest && (!InTestDirectory(p.Path) || Executable(p)) && !helpers.Contains(p.Path)));
        while (pending.TryPop(out var current))
        {
            if (!production.Add(current.Path)) continue;
            foreach (var reference in References(current))
                if (reference.Attribute("Path")?.Value is { } path && all.TryGetValue(path, out var dependency))
                    pending.Push(dependency);
        }
        return all.Values.Select(p =>
        {
            var test = !production.Contains(p.Path) && (p.IsTest || helpers.Contains(p.Path) || !Executable(p) && InTestDirectory(p.Path));
            return p with { IsTest = test, State = p.State == null ? null : p.State with { Test = test } };
        }).ToArray();
    }

    internal static EvaluatedAssemblyReference[] ReadEvaluatedReferences(JsonElement items, string directory)
    {
        var result = new List<EvaluatedAssemblyReference>();
        foreach (var itemName in new[] { "ReferencePath", "ReferencePathWithRefAssemblies" })
        {
            if (!items.TryGetProperty(itemName, out var references)) continue;
            foreach (var item in references.EnumerateArray())
            {
                string[] Paths(string[] names, string[] extensions) => names
                    .Select(name => item.TryGetProperty(name, out var value) ? value.GetString() : null)
                    .OfType<string>().Where(value => extensions.Contains(Path.GetExtension(value), StringComparer.OrdinalIgnoreCase))
                    .Select(value => Path.GetFullPath(value, directory)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                result.Add(new(Paths(["Identity", "FullPath", "ReferenceAssembly", "OriginalItemSpec"], [".dll", ".exe", ".winmd"]),
                    Paths(["MSBuildSourceProjectFile", "OriginalProjectReferenceItemSpec", "ProjectReferenceOriginalItemSpec"], [".csproj", ".vbproj"])));
            }
        }
        return result.ToArray();
    }

    internal static LoadedProject? ResolveSourceProject(string compilerPath, IEnumerable<LoadedProject> projects,
        IEnumerable<EvaluatedAssemblyReference> references)
    {
        static bool Same(string? a, string? b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var matches = references.Where(r => r.AssemblyPaths.Any(p => Same(p, compilerPath))).ToArray();
        var candidates = projects.Where(p => Same(p.OutputPath, compilerPath) || Same(p.TargetRefPath, compilerPath) ||
            matches.Any(r => r.SourceProjects.Any(source => Same(source, p.Path)) ||
                r.AssemblyPaths.Any(path => Same(path, p.OutputPath) || Same(path, p.TargetRefPath)))).Distinct().ToArray();
        if (candidates.Length > 1) throw new InvalidOperationException("Ambiguous evaluated source-project identity for " + compilerPath);
        if (candidates.Length == 0 && matches.Any(r => r.SourceProjects.Length > 0))
            throw new InvalidOperationException("Compiler reference names a source project absent from the loaded graph: " + compilerPath);
        return candidates.SingleOrDefault();
    }

    internal PortableExecutableReference SourceReference(LoadedProject dependency, MetadataReferenceProperties properties, string compilerPath)
    {
        if (!metadataImages.TryGetValue(dependency.Path, out var image))
        {
            using var stream = new MemoryStream();
            var emit = dependency.Compilation.Emit(stream);
            if (!emit.Success) throw new SourceCompilationException(dependency.Name,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            image = stream.ToArray();
            metadataImages.Add(dependency.Path, image);
        }
        return MetadataReference.CreateFromImage(image, properties, filePath: compilerPath);
    }
}

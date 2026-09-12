using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TaskOTime.ProjectMigration;

public sealed record Diagnostic(string Severity, string Code, string Project, string Message);
public sealed record FileHash(string Path, string Sha256);
public sealed record Change(string Path, string InputSha256, string OutputSha256, string[] Rules, string ProposedXml);
public sealed record ProjectReport(string Path, bool SdkStyle, EvaluatedProject[] Evaluations);
public sealed record Manifest(string Tool, string Version, string DotnetSdk, object Configuration,
    FileHash[] Inputs, FileHash[] Outputs, Change[] ChangedFiles, ProjectReport[] Projects,
    ProjectReport[] OutputProjects, Diagnostic[] Diagnostics, string Verification);

public sealed class Migration(Options options)
{
    private readonly List<Diagnostic> diagnostics = [];
    private readonly List<Change> changes = [];
    private readonly List<ProjectReport> projects = [];
    private readonly List<ProjectReport> outputProjects = [];
    private readonly Dictionary<string, XDocument> documents = new(StringComparer.Ordinal);
    private readonly HashSet<string> windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> removedDependencies = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ModernImplicitReferences = new(StringComparer.Ordinal)
    {
        "System", "System.Core", "System.Data", "System.Xml", "System.Xml.Linq", "Microsoft.CSharp", "System.Net.Http",
        "WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml", "System.Windows.Forms"
    };
    private static readonly HashSet<string> WpfReferences = new(StringComparer.Ordinal)
    {
        "WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml"
    };
    private SortedDictionary<string, byte[]> inputs = new(StringComparer.Ordinal);
    private SortedDictionary<string, byte[]> outputs = new(StringComparer.Ordinal);

    public Manifest Run()
    {
        var sdk = MsBuild.Version();
        if (sdk != "10.0.401")
            throw new InvalidOperationException($"Expected pinned .NET SDK 10.0.401, got {sdk}.");
        inputs = Workspace.Read(options.Source);
        outputs = new(inputs, StringComparer.Ordinal);
        var paths = inputs.Keys.Where(Workspace.IsProject).ToArray();
        if (paths.Length == 0) throw new ArgumentException("No application/test .csproj or .vbproj files found.");
        foreach (var path in paths)
        {
            var document = Load(inputs[path]);
            documents.Add(path, document);
            var root = document.Root!;
            if (root.Name.LocalName != "Project")
                throw new ArgumentException($"{path}: root must be Project.");
            EvaluatedProject[] evaluated;
            try
            {
                evaluated = options.Configurations.Select(c =>
                    MsBuild.Evaluate(Path.Combine(options.Source, path), c, options.Platform, options.Source)).ToArray();
            }
            catch (InvalidOperationException ex)
            {
                Add("error", "evaluation-failed", path, ex.Message.Replace(options.Source, "{workspace}", StringComparison.OrdinalIgnoreCase));
                continue;
            }
            projects.Add(new(path, IsSdk(root), evaluated));
            if (evaluated.Any(e => IsTrue(e, "UseWPF") || IsTrue(e, "UseWindowsForms")) ||
                Elements(root, "Reference").Any(e => ((string?)e.Attribute("Include"))?.Split(',')[0]
                    is "WindowsBase" or "PresentationCore" or "PresentationFramework" or "System.Xaml" or "System.Windows.Forms") ||
                Elements(root, "ProjectTypeGuids").Any(e => e.Value.Contains("60dc8134", StringComparison.OrdinalIgnoreCase)))
                windows.Add(path);
            Inspect(path, root, evaluated);
        }
        PropagateWindows();
        foreach (var report in projects)
        {
            if (options.Command == "inspect") continue;
            var root = documents[report.Path].Root!;
            var applied = new List<string>();
            ValidateFrameworks(report, root);
            switch (options.Command)
            {
                case "normalize-framework":
                    Normalize(report.Path, root, applied);
                    break;
                case "convert-projects":
                    if (!report.SdkStyle) ConvertProject(report, root, applied);
                    break;
                case "retarget":
                    Retarget(report, root, applied);
                    break;
            }
            if (applied.Count == 0) continue;
            var bytes = Save(documents[report.Path]);
            if (inputs[report.Path].SequenceEqual(bytes)) continue;
            outputs[report.Path] = bytes;
            changes.Add(new(report.Path, Workspace.Hash(inputs[report.Path]), Workspace.Hash(bytes),
                applied.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), Encoding.UTF8.GetString(bytes)));
        }
        var verification = options.Command == "inspect" ? "input-evaluated" : "planned-only";
        if (options.Command != "inspect" && !options.DryRun && !HasErrors)
        {
            // Keep all writes under the requested parent, publishing the workspace only after validation.
            var staging = options.Output + ".projectmigration-" + Guid.NewGuid().ToString("N");
            try
            {
                Workspace.Write(staging, outputs);
                ValidateOutput(staging);
                if (!HasErrors)
                {
                    verification = "output-evaluated";
                    var manifest = CreateManifest(sdk, verification);
                    File.WriteAllText(Path.Combine(staging, "migration-manifest.json"),
                        JsonSerializer.Serialize(manifest, Cli.JsonOptions) + "\n", new UTF8Encoding(false));
                    Workspace.PublishVerified(staging, options.Output!, outputs);
                    return manifest;
                }
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }
        return CreateManifest(sdk, verification);
    }

    private bool HasErrors => diagnostics.Any(d => d.Severity == "error");
    private Manifest CreateManifest(string sdk, string verification) => new(
        "TaskOTime.ProjectMigration", "1.0.0", sdk,
        new
        {
            options.Command,
            Target = options.Command == "normalize-framework" ? "net472" :
            options.Command == "retarget" ? "net10.0" : null,
            WpfTarget = options.Command == "retarget" ? "net10.0-windows" : null,
            options.Configurations,
            options.Platform,
            ProjectExclusions = new[] { "tools", "assessment" }
        },
        inputs.Select(f => new FileHash(f.Key, Workspace.Hash(f.Value))).ToArray(),
        outputs.Select(f => new FileHash(f.Key, Workspace.Hash(f.Value))).ToArray(),
        changes.OrderBy(c => c.Path, StringComparer.Ordinal).ToArray(),
        projects.ToArray(),
        outputProjects.ToArray(),
        diagnostics.Distinct().OrderBy(d => d.Project, StringComparer.Ordinal)
            .ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal).ToArray(),
        verification);

    private void Inspect(string path, XElement root, EvaluatedProject[] evaluated)
    {
        if (!IsSdk(root)) Add("warning", "legacy-project", path, "Classic project requires a separate SDK checkpoint before retargeting.");
        foreach (var import in Elements(root, "Import"))
            if (!KnownImport(import))
                Add("warning", "custom-import", path, $"Preserve and review import: {(string?)import.Attribute("Project")}. SDK conversion refuses unrecognized classic imports.");
        foreach (var target in Elements(root, "Target"))
            Add("warning", "custom-target", path, $"Preserved verbatim: {(string?)target.Attribute("Name")}. Review the proposed XML and test this target.");
        if (Elements(root, "EntityDeploy").Any() || evaluated.Any(e => e.Items["EntityDeploy"].Count > 0))
            Add("warning", "ef6-entitydeploy", path, "EDMX EntityDeploy metadata must retain CSDL/SSDL/MSL identity and deployment. .NET 10 requires a verified compatible metadata generator.");
        if (root.Descendants().Attributes().Any(a => a.Value.Contains("bin\\", StringComparison.OrdinalIgnoreCase) &&
                                                    (a.Value.Contains(".csdl") || a.Value.Contains(".ssdl") || a.Value.Contains(".msl"))))
            Add("warning", "hardcoded-metadata-output", path, "Custom metadata copy uses a hardcoded bin path. SDK conversion preserves legacy output paths; modern retargeting needs evaluated project outputs.");
        if (Elements(root, "PackageReference").Any(e => PackageId(e).StartsWith("Microsoft.NETFramework.ReferenceAssemblies", StringComparison.OrdinalIgnoreCase)))
            Add("warning", "framework-reference-package", path, "Framework reference-assembly packages are normalized with targets and removed only during modern retargeting.");
        if (Elements(root, "Reference").Any(e => ((string?)e.Attribute("Include"))?.StartsWith("System.Web", StringComparison.Ordinal) == true))
            Add("warning", "system-web", path, "System.Web APIs are unavailable on .NET 10. Replace the serializer with behavior coverage before retargeting, including source-linked consumers.");
        foreach (var evaluation in evaluated)
        {
            if (evaluation.Items["Compile"].Any(i => i.ContainsKey("Link") || i.ContainsKey("LinkBase") ||
                                                    i["Identity"].StartsWith("..", StringComparison.Ordinal)))
                Add("warning", "linked-source", path, "Linked compile items and their metadata are retained; compatibility changes must cover every consumer.");
            foreach (var kind in new[] { "Compile", "ProjectReference", "EntityDeploy", "EmbeddedResource", "Resource", "Page", "ApplicationDefinition", "Content", "None" })
                foreach (var item in evaluation.Items[kind])
                {
                    var identity = item["Identity"];
                    if (identity.StartsWith("{workspace}\\", StringComparison.Ordinal))
                    {
                        Add("error", "absolute-workspace-item", path, $"Use relocatable relative {kind} items instead of absolute source paths: {identity}");
                        continue;
                    }
                    var absolute = Path.GetFullPath(Path.Combine(options.Source, Path.GetDirectoryName(path)!, identity));
                    if (!Workspace.IsWithin(absolute, options.Source))
                        Add("error", "external-item", path, $"{kind} '{identity}' leaves --source. Supply a workspace containing all linked files and references.");
                    if (kind == "Compile" && File.Exists(absolute) &&
                        File.ReadAllText(absolute).Contains("JavaScriptSerializer", StringComparison.Ordinal))
                        Add("warning", "javascript-serializer", path, $"Compiled source '{identity}' uses JavaScriptSerializer; migration requires JSON compatibility tests, not XML replacement.");
                }
        }
    }

    private void PropagateWindows()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var report in projects)
                foreach (var reference in report.Evaluations.SelectMany(e => e.Items["ProjectReference"]))
                {
                    var absolute = Path.GetFullPath(Path.Combine(options.Source, Path.GetDirectoryName(report.Path)!, reference["Identity"]));
                    if (windows.Contains(Path.GetRelativePath(options.Source, absolute)))
                        changed |= windows.Add(report.Path);
                }
        } while (changed);
    }

    private void ValidateFrameworks(ProjectReport report, XElement root)
    {
        if (Elements(root, "TargetFrameworks").Any() || report.Evaluations.Any(e => e.Properties["TargetFrameworks"].Length > 0))
            Add("error", "multi-targeting", report.Path, "Multi-targeting is ambiguous; split or explicitly review the project before migration.");
        var declarations = FrameworkElements(root).ToArray();
        if (declarations.Length == 0)
            Add("error", "imported-framework", report.Path, "Framework is inherited or computed outside the project. Move an explicit literal declaration into this project before migration.");
        if (declarations.Any(e => !FrameworkLiteral(e.Value)))
            Add("error", "computed-framework", report.Path, "Framework must be a supported literal; property expressions and non-Framework/non-.NET10 targets require manual review.");
        if (Elements(root, "TargetFrameworkProfile").Any(e => !string.IsNullOrWhiteSpace(e.Value)) ||
            report.Evaluations.Any(e => e.Properties["TargetFrameworkProfile"].Length > 0))
            Add("error", "framework-profile", report.Path, "Framework profiles are not migrated implicitly.");
        if (Elements(root, "TargetFrameworkIdentifier").Any())
            Add("error", "explicit-framework-identifier", report.Path, "Explicit TargetFrameworkIdentifier requires manual review.");
        if (options.Command == "normalize-framework" && declarations.Any(e => e.Value.StartsWith("net10.", StringComparison.Ordinal)))
            Add("error", "framework-downgrade", report.Path, "Refusing to downgrade a modern application project; place modern tools under tools.");
    }

    private void Normalize(string path, XElement root, List<string> rules)
    {
        foreach (var element in FrameworkElements(root))
        {
            if (!FrameworkLiteral(element.Value) || element.Value.StartsWith("net10.", StringComparison.Ordinal)) continue;
            ChangeValue(element, element.Name.LocalName == "TargetFrameworkVersion" ? "v4.7.2" : "net472",
                "normalize-framework-net472", rules);
        }
        foreach (var package in Elements(root, "PackageReference"))
        {
            var id = PackageId(package);
            if (Regex.IsMatch(id, @"^Microsoft\.NETFramework\.ReferenceAssemblies\.net4\d{1,2}$", RegexOptions.IgnoreCase))
            {
                var attribute = package.Attribute("Include") ?? package.Attribute("Update")!;
                if (attribute.Value == "Microsoft.NETFramework.ReferenceAssemblies.net472") continue;
                attribute.Value = "Microsoft.NETFramework.ReferenceAssemblies.net472";
                rules.Add("normalize-reference-assemblies-net472");
            }
            else if (id.StartsWith("Microsoft.NETFramework.ReferenceAssemblies.", StringComparison.OrdinalIgnoreCase))
                Add("error", "unsupported-reference-package", path, $"Unsupported framework reference package '{id}'.");
        }
    }

    private void ConvertProject(ProjectReport report, XElement root, List<string> rules)
    {
        var imports = Elements(root, "Import").ToArray();
        if (imports.Any(i => !KnownImport(i)))
        {
            Add("error", "unsupported-classic-import", report.Path, "Review/custom-convert unknown imports first; no imports or custom targets have been silently removed.");
            return;
        }
        var languageTargets = imports.Where(i => IsLanguageImport(i)).ToArray();
        if (languageTargets.Length != 1 || languageTargets[0].Parent != root || languageTargets[0].Attribute("Condition") != null)
        {
            Add("error", "ambiguous-language-import", report.Path, "Expected exactly one unconditional top-level Microsoft.CSharp.targets or Microsoft.VisualBasic.targets import.");
            return;
        }
        if (Elements(root, "TargetFramework").Any() || Elements(root, "TargetFrameworkVersion").Any(e => !FrameworkLiteral(e.Value)))
        {
            Add("error", "ambiguous-classic-framework", report.Path, "Classic SDK conversion requires literal TargetFrameworkVersion declarations.");
            return;
        }
        // Explicit SDK imports retain custom target ordering relative to the language targets.
        var common = imports.Where(i => ((string?)i.Attribute("Project"))?.EndsWith("Microsoft.Common.props", StringComparison.OrdinalIgnoreCase) == true).ToArray();
        if (common.Length > 1 || common.Any(i => i.Parent != root))
        {
            Add("error", "ambiguous-common-import", report.Path, "Expected at most one top-level Microsoft.Common.props import.");
            return;
        }
        var props = new XElement(root.Name.Namespace + "Import",
            new XAttribute("Project", "Sdk.props"), new XAttribute("Sdk", "Microsoft.NET.Sdk"));
        if (common.Length == 1) common[0].ReplaceWith(props);
        else root.AddFirst(props);
        languageTargets[0].ReplaceWith(new XElement(root.Name.Namespace + "Import",
            new XAttribute("Project", "Sdk.targets"), new XAttribute("Sdk", "Microsoft.NET.Sdk")));
        root.Attribute("ToolsVersion")?.Remove();
        foreach (var element in Elements(root, "TargetFrameworkVersion").ToArray())
        {
            element.Name = root.Name.Namespace + "TargetFramework";
            element.Value = FrameworkMoniker(element.Value);
        }
        var defaults = new XElement(root.Name.Namespace + "PropertyGroup",
            new XElement(root.Name.Namespace + "EnableDefaultItems", "false"),
            new XElement(root.Name.Namespace + "EnableDefaultPageItems", "false"),
            new XElement(root.Name.Namespace + "EnableDefaultApplicationDefinition", "false"),
            new XElement(root.Name.Namespace + "GenerateAssemblyInfo", "false"),
            new XElement(root.Name.Namespace + "AppendTargetFrameworkToOutputPath", "false"),
            new XElement(root.Name.Namespace + "AppendRuntimeIdentifierToOutputPath", "false"),
            new XElement(root.Name.Namespace + "EmbeddedResourceUseDependentUponConvention", "false"));
        foreach (var property in new[] { "AssemblyName", "RootNamespace" })
            if (!Elements(root, property).Any())
            {
                var values = report.Evaluations.Select(e => e.Properties[property]).Distinct().ToArray();
                if (values.Length != 1)
                    Add("error", "conditional-default-identity", report.Path, $"{property} varies by configuration; declare it explicitly.");
                else defaults.Add(new XElement(root.Name.Namespace + property, values[0]));
            }
        if (windows.Contains(report.Path) && !Elements(root, "UseWPF").Any() &&
            Elements(root, "Reference").Any(e => ((string?)e.Attribute("Include"))?.Split(',')[0] == "PresentationFramework"))
            defaults.Add(new XElement(root.Name.Namespace + "UseWPF", "true"));
        props.AddAfterSelf(defaults);
        // Modern SDK project XML has no legacy MSBuild default namespace.
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Name.NamespaceName == "http://schemas.microsoft.com/developer/msbuild/2003")
                element.Name = element.Name.LocalName;
            foreach (var attribute in element.Attributes().Where(a => a.IsNamespaceDeclaration &&
                         a.Value == "http://schemas.microsoft.com/developer/msbuild/2003").ToArray())
                attribute.Remove();
        }
        rules.AddRange(["sdk-explicit-imports-preserve-target-order", "sdk-preserve-explicit-items",
            "sdk-preserve-assembly-resource-identity", "sdk-preserve-output-paths"]);
    }

    private void Retarget(ProjectReport report, XElement root, List<string> rules)
    {
        if (!report.SdkStyle)
        {
            Add("error", "sdk-checkpoint-required", report.Path, "Run convert-projects --sdk-style before retargeting.");
            return;
        }
        foreach (var diagnostic in diagnostics.Where(d => d.Project == report.Path &&
                     d.Code is "system-web" or "javascript-serializer" or "ef6-entitydeploy" or "hardcoded-metadata-output").ToArray())
            Add("error", "retarget-blocked-" + diagnostic.Code, report.Path, diagnostic.Message);
        foreach (var hint in Elements(root, "HintPath"))
            if (Regex.IsMatch(hint.Value, @"[\\/]net4\d{1,2}[\\/]", RegexOptions.IgnoreCase))
                Add("error", "retarget-framework-hintpath", report.Path, "Replace Framework-specific assembly HintPath with a compatible PackageReference before retargeting: " + hint.Value);
        EstablishDesktopFlags(report, root, rules);
        var target = windows.Contains(report.Path) ? "net10.0-windows" : "net10.0";
        foreach (var element in FrameworkElements(root))
            if (element.Name.LocalName == "TargetFramework" && FrameworkLiteral(element.Value))
                ChangeValue(element, target, "retarget-framework", rules);
        foreach (var package in Elements(root, "PackageReference").ToArray())
            if (PackageId(package).StartsWith("Microsoft.NETFramework.ReferenceAssemblies", StringComparison.OrdinalIgnoreCase))
            {
                RecordRemoval(report.Path, "PackageReference", PackageId(package));
                package.Remove();
                rules.Add("remove-framework-reference-assemblies");
            }
        foreach (var reference in Elements(root, "Reference").ToArray())
        {
            var name = ((string?)reference.Attribute("Include"))?.Split(',')[0];
            if (name != null && ModernImplicitReferences.Contains(name))
            {
                if (reference.HasElements || reference.Attributes().Any(a => a.Name.LocalName != "Include") ||
                    report.Evaluations.SelectMany(e => e.Items["Reference"]).Where(r => r["Identity"] == name)
                        .Any(r => r.Keys.Any(k => k is not ("Identity" or "DefiningProjectFullPath")) && !AutomaticFrameworkReference(r)))
                    Add("error", "retarget-reference-metadata", report.Path, $"Framework reference '{name}' has custom semantics; review before removal.");
                else
                {
                    RecordRemoval(report.Path, "Reference", name);
                    reference.Remove();
                    rules.Add("remove-implicit-framework-reference");
                }
            }
            if (name == "System.Configuration" && !Elements(root, "PackageReference").Any(p => PackageId(p) == "System.Configuration.ConfigurationManager"))
                Add("error", "retarget-configuration-manager", report.Path, "Add a reviewed compatible System.Configuration.ConfigurationManager PackageReference before retargeting.");
        }
    }

    private void EstablishDesktopFlags(ProjectReport report, XElement root, List<string> rules)
    {
        foreach (var flag in new[] { "UseWPF", "UseWindowsForms" })
        {
            bool NeedsFlag(string identity) => flag == "UseWPF"
                ? WpfReferences.Contains(identity.Split(',')[0]) : identity.Split(',')[0] == "System.Windows.Forms";
            var references = Elements(root, "Reference")
                .Where(e => NeedsFlag((string?)e.Attribute("Include") ?? "")).ToArray();
            var active = report.Evaluations.Any(e => e.Items["Reference"].Any(r => NeedsFlag(r["Identity"])));
            if (references.Length == 0 && !active) continue;
            if (Elements(root, flag).Any() || report.Evaluations.Any(e => e.Properties[flag].Length > 0))
            {
                if (report.Evaluations.Any(e => e.Items["Reference"].Any(r => NeedsFlag(r["Identity"])) && !IsTrue(e, flag)))
                    Add("error", "retarget-desktop-flag", report.Path, $"{flag} is false or conditional while desktop references are active. Set an explicit compatible flag before retargeting.");
                continue;
            }
            if (references.Length == 0)
            {
                Add("error", "retarget-desktop-flag", report.Path, $"Imported desktop references require an explicitly reviewed {flag} property and compatible references.");
                continue;
            }
            if (references.Any(e => e.AncestorsAndSelf().Any(a => a.Attribute("Condition") != null)))
            {
                Add("error", "retarget-desktop-flag", report.Path, $"Conditional desktop references require an explicitly reviewed {flag} property.");
                continue;
            }
            var group = new XElement(root.Name.Namespace + "PropertyGroup",
                new XElement(root.Name.Namespace + flag, "true"));
            var props = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Import" &&
                (string?)e.Attribute("Project") == "Sdk.props");
            if (props == null) root.AddFirst(group);
            else props.AddAfterSelf(group);
            rules.Add("establish-" + flag.ToLowerInvariant());
        }
    }

    private void ValidateOutput(string root)
    {
        foreach (var report in projects)
        {
            var outputEvaluations = new List<EvaluatedProject>();
            foreach (var before in report.Evaluations)
            {
                EvaluatedProject after;
                try { after = MsBuild.Evaluate(Path.Combine(root, report.Path), before.Configuration, options.Platform, root); }
                catch (InvalidOperationException ex)
                {
                    Add("error", "output-evaluation-failed", report.Path, ex.Message.Replace(root, "{workspace}", StringComparison.OrdinalIgnoreCase));
                    continue;
                }
                outputEvaluations.Add(after);
                var expected = options.Command == "normalize-framework" ? "net472" :
                    options.Command == "retarget" ? windows.Contains(report.Path) ? "net10.0-windows" : "net10.0" :
                    EffectiveFramework(before);
                var actual = EffectiveFramework(after);
                if (options.Command == "retarget" && expected == "net10.0-windows" && actual.StartsWith("net10.0-windows", StringComparison.Ordinal))
                    actual = expected;
                if (actual != expected)
                    Add("error", "output-framework-mismatch", report.Path, $"{before.Configuration}: expected {expected}, evaluated {actual}.");
                foreach (var property in new[] { "AssemblyName", "RootNamespace", "OutputType", "StartupObject",
                         "ApplicationIcon", "SignAssembly", "AssemblyOriginatorKeyFile", "OptionStrict",
                         "OptionExplicit", "OptionInfer", "OptionCompare", "MyType" })
                    if (ComparableProperty(property, before.Properties[property]) != ComparableProperty(property, after.Properties[property]))
                        Add("error", "output-identity-mismatch", report.Path, $"{before.Configuration}: {property} changed from '{before.Properties[property]}' to '{after.Properties[property]}'.");
                var expectedOutputPath = before.Properties["OutputPath"];
                if (options.Command == "normalize-framework" && IsTrue(before, "AppendTargetFrameworkToOutputPath"))
                {
                    var suffix = EffectiveFramework(before) + Path.DirectorySeparatorChar;
                    if (expectedOutputPath.EndsWith(suffix, StringComparison.Ordinal))
                        expectedOutputPath = expectedOutputPath[..^suffix.Length] + "net472" + Path.DirectorySeparatorChar;
                }
                if (options.Command != "retarget" && expectedOutputPath != after.Properties["OutputPath"])
                    Add("error", "output-path-mismatch", report.Path, $"{before.Configuration}: OutputPath changed from '{before.Properties["OutputPath"]}' to '{after.Properties["OutputPath"]}'.");
                foreach (var kind in new[] { "Compile", "EmbeddedResource", "Resource", "Page", "ApplicationDefinition", "EntityDeploy", "Content", "None", "ProjectReference" })
                    if (ComparableItems(before.Items[kind]) != ComparableItems(after.Items[kind]))
                        Add("error", "output-item-mismatch", report.Path, $"{before.Configuration}: evaluated {kind} items/metadata changed; output was not published.");
                foreach (var kind in new[] { "Reference", "PackageReference" })
                    if (ComparableDependencies(report.Path, before.Items[kind], kind, true) != ComparableDependencies(report.Path, after.Items[kind], kind, false))
                        Add("error", "output-dependency-mismatch", report.Path, $"{before.Configuration}: evaluated {kind} dependencies/metadata changed beyond the stage's explicit rename/removal rules. Review framework-dependent conditions and imports; output was not published.");
            }
            outputProjects.Add(new(report.Path, IsSdk(documents[report.Path].Root!), outputEvaluations.ToArray()));
        }
    }

    private void Add(string severity, string code, string path, string message) =>
        diagnostics.Add(new(severity, code, path, message));
    private static string ComparableProperty(string name, string value) =>
        name == "SignAssembly" && value.Length == 0 ? "false" : value;
    private static string ComparableItems(List<SortedDictionary<string, string>> items) =>
        JsonSerializer.Serialize(items.Where(i => Path.GetFileName(i["Identity"]) != "migration-manifest.json"));
    private void RecordRemoval(string path, string kind, string identity)
    {
        if (!removedDependencies.TryGetValue(path, out var removed))
            removedDependencies.Add(path, removed = new(StringComparer.Ordinal));
        removed.Add(kind + "\0" + identity);
    }

    private string ComparableDependencies(string path, List<SortedDictionary<string, string>> items, string kind, bool input)
    {
        var expected = new List<SortedDictionary<string, string>>();
        foreach (var item in items)
        {
            if (kind == "Reference" && AutomaticFrameworkReference(item)) continue;
            var row = new SortedDictionary<string, string>(item, StringComparer.Ordinal);
            var identity = row["Identity"];
            if (input && options.Command == "normalize-framework" && kind == "PackageReference" &&
                Regex.IsMatch(identity, @"^Microsoft\.NETFramework\.ReferenceAssemblies\.net4\d{1,2}$", RegexOptions.IgnoreCase))
                row["Identity"] = "Microsoft.NETFramework.ReferenceAssemblies.net472";
            if (input && options.Command == "retarget" &&
                row.GetValueOrDefault("DefiningProjectFullPath") == "{workspace}\\" + path &&
                removedDependencies.TryGetValue(path, out var removed) && removed.Contains(kind + "\0" + identity))
                continue;
            expected.Add(row);
        }
        return JsonSerializer.Serialize(expected.OrderBy(r => r["Identity"], StringComparer.Ordinal)
            .ThenBy(r => JsonSerializer.Serialize(r), StringComparer.Ordinal));
    }

    private static bool AutomaticFrameworkReference(SortedDictionary<string, string> item)
    {
        if (item.Keys.Any(k => k is not ("Identity" or "Pack" or "IsImplicitlyDefined" or "DefiningProjectFullPath" or "RequiredTargetFramework"))) return false;
        if (item.TryGetValue("Pack", out var pack) && pack != "false") return false;
        if (item.TryGetValue("RequiredTargetFramework", out var required) && (item["Identity"] != "System.Xaml" || required != "4.0")) return false;
        var origin = item.GetValueOrDefault("DefiningProjectFullPath", "");
        return origin == "{sdk}\\Sdks\\Microsoft.NET.Sdk\\targets\\Microsoft.NET.Sdk.BeforeCommon.targets" &&
               item.GetValueOrDefault("IsImplicitlyDefined") == "true" ||
               item["Identity"] == "mscorlib" && Regex.IsMatch(origin,
                   @"^\{nuget\}\\microsoft\.netframework\.referenceassemblies\.net4\d{1,2}\\[^\\]+\\build\\Microsoft\.NETFramework\.ReferenceAssemblies\.net4\d{1,2}\.targets$",
                   RegexOptions.IgnoreCase);
    }
    private static bool IsTrue(EvaluatedProject e, string name) => e.Properties[name].Equals("true", StringComparison.OrdinalIgnoreCase);
    private static string EffectiveFramework(EvaluatedProject e) => e.Properties["TargetFramework"].Length > 0 ?
        e.Properties["TargetFramework"] : FrameworkMoniker(e.Properties["TargetFrameworkVersion"]);
    private static bool FrameworkLiteral(string value) =>
        Regex.IsMatch(value, @"^(net4\d{1,2}|v4\.\d(?:\.\d)?|net10\.0(?:-windows(?:\d+\.\d+)?)?)$");
    private static string FrameworkMoniker(string value) => value.StartsWith('v') ? "net" + value[1..].Replace(".", "") : value;
    private static IEnumerable<XElement> Elements(XElement root, string name) =>
        root.Descendants().Where(e => e.Name.LocalName == name &&
            !e.Ancestors().Any(a => a.Name.LocalName == "Target"));
    private static IEnumerable<XElement> FrameworkElements(XElement root) =>
        root.Descendants().Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworkVersion" &&
                                     !e.Ancestors().Any(a => a.Name.LocalName == "Target"));
    private static string PackageId(XElement e) => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? "";
    private static bool IsSdk(XElement root) => root.Attribute("Sdk") != null ||
        Elements(root, "Import").Any(i => i.Attribute("Sdk") != null);
    private static bool IsLanguageImport(XElement import) =>
        ((string?)import.Attribute("Project")) is "$(MSBuildToolsPath)\\Microsoft.CSharp.targets" or
            "$(MSBuildToolsPath)\\Microsoft.VisualBasic.targets";
    private static bool KnownImport(XElement import) => IsLanguageImport(import) ||
        ((string?)import.Attribute("Project")) == "$(MSBuildExtensionsPath)\\$(MSBuildToolsVersion)\\Microsoft.Common.props" ||
        ((string?)import.Attribute("Sdk")) == "Microsoft.NET.Sdk";
    private static void ChangeValue(XElement element, string value, string rule, List<string> rules)
    {
        if (element.Value == value) return;
        element.Value = value;
        rules.Add(rule);
    }
    private static XDocument Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
    private static byte[] Save(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            OmitXmlDeclaration = document.Declaration == null
        }))
            document.Save(writer);
        return stream.ToArray();
    }
}

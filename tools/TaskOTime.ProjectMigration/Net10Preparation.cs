using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace TaskOTime.ProjectMigration;

public sealed partial class Migration
{
    private const string MetadataTemplatePath = "build\\EntityFramework.Metadata.targets";
    private readonly Dictionary<string, List<SortedDictionary<string, string>>> preparedPackages = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (string Old, string New)> PreparedVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EntityFramework"] = ("6.5.1", "6.5.2"),
        ["Microsoft.NET.Test.Sdk"] = ("17.2.0", "17.14.1"),
        ["MSTest.TestAdapter"] = ("2.2.10", "3.6.4"),
        ["MSTest.TestFramework"] = ("2.2.10", "3.6.4")
    };

    private void PrepareNet10(ProjectReport report, XElement root, List<string> rules)
    {
        if (!report.SdkStyle)
        {
            Add("error", "sdk-checkpoint-required", report.Path, "SDK conversion must precede .NET 10 preparation.");
            return;
        }
        foreach (var package in Elements(root, "PackageReference"))
        {
            if (!PreparedVersions.TryGetValue(PackageId(package), out var versions)) continue;
            var attribute = package.Attribute("Version");
            var element = package.Elements().FirstOrDefault(e => e.Name.LocalName == "Version");
            var version = attribute?.Value ?? element?.Value;
            if (version == versions.New) continue;
            if (version != versions.Old)
            {
                Add("error", "unreviewed-package-version", report.Path, $"Review {PackageId(package)} version '{version}' before automatic preparation.");
                continue;
            }
            if (attribute != null) attribute.Value = versions.New;
            else element!.Value = versions.New;
            rules.Add("prepare-package-" + PackageId(package).ToLowerInvariant());
        }
        foreach (var reference in Elements(root, "Reference").ToArray())
        {
            var name = (string?)reference.Attribute("Include") ?? "";
            if (name == "System.Configuration")
                AddPreparedPackage(report, root, "System.Configuration.ConfigurationManager", "10.0.0", rules);
            var ef = name is "EntityFramework" or "EntityFramework.SqlServer" &&
                Elements(root, "PackageReference").Any(p => PackageId(p) == "EntityFramework");
            if (name is not ("System.Configuration" or "System.Web.Extensions") && !ef) continue;
            if (name == "System.Web.Extensions" && diagnostics.Any(d => d.Project == report.Path && d.Code == "javascript-serializer"))
            {
                Add("error", "serializer-api-change-required", report.Path, "Replace the unavailable serializer and validate JSON behavior before preparation.");
                continue;
            }
            if (name == "System.Web.Extensions")
                AddPreparedPackage(report, root, "Newtonsoft.Json", "13.0.3", rules);
            if (reference.Attributes().Any(a => a.Name.LocalName != "Include") ||
                reference.Elements().Any(e => !ef || e.Name.LocalName is not ("HintPath" or "Private")) ||
                reference.Elements().Any(e => e.Name.LocalName == "Private" && !e.Value.Equals("true", StringComparison.OrdinalIgnoreCase)) ||
                reference.Ancestors().Any(e => e.Attribute("Condition") != null))
            {
                Add("error", "unreviewed-reference-removal", report.Path, $"Reference '{name}' has conditional/custom semantics requiring review.");
                continue;
            }
            if (!CanRemoveEvaluatedReference(report, name, ef)) continue;
            RecordRemoval(report.Path, "Reference", name);
            reference.Remove();
            rules.Add("prepare-package-backed-reference");
        }
        foreach (var model in Elements(root, "EntityDeploy").ToArray())
        {
            if (!HasFilesystemMetadata(report, model)) continue;
            var logicalPath = (string?)model.Attribute("Link") ?? model.Elements().FirstOrDefault(e => e.Name.LocalName == "Link")?.Value ??
                (string?)model.Attribute("Include") ?? "";
            if (logicalPath.Length == 0 || logicalPath.Contains('$') || logicalPath.IndexOfAny(['*', ';']) >= 0 ||
                Path.IsPathRooted(logicalPath) || logicalPath.Split('\\', '/').Contains(".."))
            {
                Add("error", "unreviewed-edmx-layout", report.Path, "EntityDeploy requires a literal workspace-relative Include or Link metadata path.");
                continue;
            }
            model.Name = root.Name.Namespace + "EntityModel";
            model.Add(new XElement(root.Name.Namespace + "MetadataPath", Path.ChangeExtension(logicalPath, null)));
            EnsureMetadataTemplate();
            var projectDirectory = Path.GetDirectoryName(report.Path);
            var import = Path.GetRelativePath(string.IsNullOrEmpty(projectDirectory) ? "." : projectDirectory, MetadataTemplatePath);
            if (!Elements(root, "Import").Any(e => (string?)e.Attribute("Project") == import))
                root.Add(new XElement(root.Name.Namespace + "Import", new XAttribute("Project", import)));
            rules.Add("prepare-edmx-structured-runtime-schemas");
        }
        foreach (var target in Elements(root, "Target").ToArray())
        {
            var metadata = target.Descendants().Attributes("Include").Select(a => a.Value).ToArray();
            if (!metadata.Any(v => v.Contains("bin\\", StringComparison.OrdinalIgnoreCase) && v.Contains("\\Model\\", StringComparison.OrdinalIgnoreCase))) continue;
            var modelNames = documents.Values.SelectMany(d => d.Root!.Descendants())
                .Where(e => e.Name.LocalName is "EntityDeploy" or "EntityModel")
                .Select(e => Path.GetFileNameWithoutExtension((string?)e.Attribute("Include") ?? ""))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var safe = (string?)target.Attribute("AfterTargets") == "Build" && target.Attribute("Condition") == null &&
                target.Attributes().All(a => a.Name.LocalName is "Name" or "AfterTargets") &&
                target.Elements().All(e => e.Name.LocalName is "ItemGroup" or "Copy" or "MakeDir") &&
                target.Elements().Where(e => e.Name.LocalName == "ItemGroup").All(g => !g.HasAttributes &&
                    g.Elements().All(i => !i.HasElements && i.Attributes().All(a => a.Name.LocalName == "Include"))) &&
                target.Elements().Where(e => e.Name.LocalName == "Copy").All(c =>
                    c.Attributes().All(a => a.Name.LocalName is "SourceFiles" or "DestinationFolder" or "SkipUnchangedFiles") &&
                    !c.HasElements && target.Descendants().Any(i => (string?)c.Attribute("SourceFiles") == "@(" + i.Name.LocalName + ")")) &&
                target.Elements().Where(e => e.Name.LocalName == "MakeDir").All(m =>
                    !m.HasElements && m.Attributes().All(a => a.Name.LocalName == "Directories")) &&
                metadata.Length > 0 && metadata.All(v => v.Contains("\\Model\\", StringComparison.OrdinalIgnoreCase) &&
                    modelNames.Contains(Path.GetFileNameWithoutExtension(v)) &&
                    new[] { ".csdl", ".ssdl", ".msl", ".*" }.Any(suffix => v.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) &&
                PreservesMetadataCopyLayout(report, target);
            if (!safe)
            {
                Add("error", "unreviewed-metadata-target", report.Path, "Metadata target source/destination is not guaranteed by the consumer's evaluated content-propagating reference graph or identical local EDMX generation. Review reference metadata, configuration overrides, and copy controls before removing this target.");
                continue;
            }
            target.Remove();
            rules.Add("prepare-metadata-project-output-propagation");
        }
    }

    private void AddPreparedPackage(ProjectReport report, XElement root, string id, string version, List<string> rules)
    {
        if (Elements(root, "PackageReference").Any(p => PackageId(p) == id)) return;
        root.Add(new XElement(root.Name.Namespace + "ItemGroup",
            new XElement(root.Name.Namespace + "PackageReference", new XAttribute("Include", id), new XAttribute("Version", version))));
        if (!preparedPackages.TryGetValue(report.Path, out var packages)) preparedPackages.Add(report.Path, packages = []);
        packages.Add(new(StringComparer.Ordinal) { ["Identity"] = id, ["Version"] = version, ["DefiningProjectFullPath"] = "{workspace}\\" + report.Path });
        rules.Add("prepare-package-" + id.ToLowerInvariant());
    }

    private void EnsureMetadataTemplate()
    {
        using var stream = typeof(Migration).Assembly.GetManifestResourceStream("TaskOTime.ProjectMigration.Templates.EntityFramework.Metadata.targets")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        if (outputs.TryGetValue(MetadataTemplatePath, out var existing))
        {
            if (!existing.SequenceEqual(bytes)) throw new InvalidOperationException("Existing metadata template differs; review before replacement.");
            return;
        }
        outputs.Add(MetadataTemplatePath, bytes);
        changes.Add(new(MetadataTemplatePath, "", Workspace.Hash(bytes), ["prepare-edmx-build-template"], Encoding.UTF8.GetString(bytes)));
    }

    private static void ApplyPreparedVersion(SortedDictionary<string, string> row)
    {
        if (PreparedVersions.TryGetValue(row["Identity"], out var versions) && row.GetValueOrDefault("Version") == versions.Old)
            row["Version"] = versions.New;
    }

    private void ValidatePreparedModels(string path, EvaluatedProject before, EvaluatedProject after)
    {
        var actual = after.Items["EntityModel"].Select(i =>
        {
            var row = new SortedDictionary<string, string>(i, StringComparer.Ordinal);
            row.Remove("MetadataPath");
            return row;
        }).ToList();
        var expected = before.Items["EntityDeploy"].Concat(before.Items["EntityModel"].Select(i =>
        {
            var row = new SortedDictionary<string, string>(i, StringComparer.Ordinal);
            row.Remove("MetadataPath");
            return row;
        })).OrderBy(i => i["Identity"], StringComparer.Ordinal);
        if (after.Items["EntityDeploy"].Count != 0 || JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(actual.OrderBy(i => i["Identity"], StringComparer.Ordinal)))
            Add("error", "output-entity-model-mismatch", path, "EDMX item identity or metadata changed unexpectedly during preparation.");
    }
}

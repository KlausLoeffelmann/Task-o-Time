using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TaskOTime.ProjectMigration;

public sealed partial class Migration
{
    private static readonly string[] SchemaExtensions = [".csdl", ".ssdl", ".msl"];
    private sealed record MetadataLayout(string Source, string ModelFile, string Target);

    private bool CanRemoveEvaluatedReference(ProjectReport report, string name, bool entityFramework)
    {
        var valid = true;
        foreach (var evaluation in report.Evaluations)
        {
            var references = evaluation.Items["Reference"].Where(i => i["Identity"] == name).ToArray();
            var version = evaluation.Items["PackageReference"].FirstOrDefault(p => p["Identity"] == "EntityFramework")?.GetValueOrDefault("Version");
            var safe = references.Length > 0 && references.All(item =>
                item.Keys.All(key => key is "Identity" or "DefiningProjectFullPath" ||
                    entityFramework && key is "HintPath" or "Private") &&
                (!item.TryGetValue("Private", out var copyLocal) || copyLocal.Equals("true", StringComparison.OrdinalIgnoreCase)) &&
                (!item.TryGetValue("HintPath", out var hint) || entityFramework &&
                    (hint.Equals($"{{nuget}}\\entityframework\\{version}\\lib\\net45\\{name}.dll", StringComparison.OrdinalIgnoreCase) ||
                     evaluation.NuGetRoot.Length == 0 && hint.Equals($"entityframework\\{version}\\lib\\net45\\{name}.dll", StringComparison.OrdinalIgnoreCase))));
            if (safe) continue;
            Add("error", "unreviewed-reference-removal", report.Path,
                $"{evaluation.Configuration}: evaluated '{name}' has aliases, custom metadata, an overridden assembly path, or missing/conditional items. These semantics cannot be discarded when replacing the reference.");
            valid = false;
        }
        return valid;
    }

    private bool HasFilesystemMetadata(ProjectReport report, XElement model)
    {
        var include = (string?)model.Attribute("Include") ?? "";
        if (include.Length == 0 || include.IndexOfAny(['$', '@', '%', '*', '?', ';']) >= 0 || Path.IsPathRooted(include))
        {
            Add("error", "unreviewed-edmx-layout", report.Path, "Inspect a literal, workspace-relative EDMX Include before changing metadata deployment.");
            return false;
        }
        var fullPath = Path.GetFullPath(Path.Combine(options.Source, Path.GetDirectoryName(report.Path)!, include));
        var relative = Path.GetRelativePath(options.Source, fullPath);
        var bytes = inputs.FirstOrDefault(f => f.Key.Equals(relative, StringComparison.OrdinalIgnoreCase)).Value;
        if (!Workspace.IsWithin(fullPath, options.Source) || bytes == null)
        {
            Add("error", "unreviewed-edmx-layout", report.Path, $"EDMX '{include}' is missing from the source inventory; deployment mode cannot be verified.");
            return false;
        }
        var document = Load(bytes);
        var modes = document.Descendants()
            .Where(e => e.Name.LocalName == "DesignerProperty" && (string?)e.Attribute("Name") == "MetadataArtifactProcessing")
            .Select(e => (string?)e.Attribute("Value") ?? "")
            .Concat(model.Elements().Where(e => e.Name.LocalName == "MetadataArtifactProcessing").Select(e => e.Value))
            .Concat(model.Attributes().Where(a => a.Name.LocalName == "MetadataArtifactProcessing").Select(a => a.Value))
            .Concat(report.Evaluations.SelectMany(e => e.Items["EntityDeploy"])
                .Where(i => i["Identity"].Equals(include, StringComparison.OrdinalIgnoreCase) && i.ContainsKey("MetadataArtifactProcessing"))
                .Select(i => i["MetadataArtifactProcessing"])).ToArray();
        if (modes.Any(m => m.Equals("EmbedInOutputAssembly", StringComparison.OrdinalIgnoreCase)))
        {
            Add("error", "unsupported-edmx-embedding", report.Path,
                $"EDMX '{include}' requires embedded assembly resources. Resource identities and res:// connections must be preserved; conversion to loose metadata is unsupported.");
            return false;
        }
        if (modes.Any(m => !m.Equals("CopyToOutputDirectory", StringComparison.OrdinalIgnoreCase)))
        {
            Add("error", "unsupported-edmx-processing", report.Path, $"EDMX '{include}' has an unrecognized metadata-processing mode.");
            return false;
        }
        return true;
    }

    private bool PreservesMetadataCopyLayout(ProjectReport consumer, XElement target)
    {
        var copies = target.Elements().Where(e => e.Name.LocalName == "Copy").ToArray();
        if (copies.Length == 0) return false;
        foreach (var evaluation in consumer.Evaluations)
        {
            var consumerOutput = ResolveBuildPath("$(OutDir)", consumer, evaluation);
            if (consumerOutput == null) return false;
            var layouts = MetadataOutputLayouts(projects, evaluation.Configuration).ToArray();
            var delivered = PropagatedMetadataLayouts(consumer, evaluation.Configuration).ToArray();
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var copy in copies)
            {
                if (!string.Equals((string?)copy.Attribute("SkipUnchangedFiles"), "true", StringComparison.OrdinalIgnoreCase)) return false;
                var destination = ResolveBuildPath((string?)copy.Attribute("DestinationFolder"), consumer, evaluation);
                if (destination == null) return false;
                destinations.Add(destination);
                var items = target.Elements().Where(e => e.Name.LocalName == "ItemGroup").SelectMany(g => g.Elements())
                    .Where(i => (string?)copy.Attribute("SourceFiles") == "@(" + i.Name.LocalName + ")").ToArray();
                if (items.Length == 0) return false;
                foreach (var item in items)
                {
                    var include = (string?)item.Attribute("Include") ?? "";
                    var paths = include.EndsWith(".*", StringComparison.Ordinal)
                        ? SchemaExtensions.Select(extension => include[..^2] + extension) : [include];
                    foreach (var path in paths)
                    {
                        var source = ResolveBuildPath(path, consumer, evaluation);
                        if (source == null) return false;
                        var matches = layouts.Where(l => l.Source.Equals(source, StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (matches.Length == 0 || matches.Any(l =>
                                !Path.GetFullPath(Path.Combine(destination, Path.GetFileName(source)))
                                    .Equals(Path.GetFullPath(Path.Combine(consumerOutput, l.Target)), StringComparison.OrdinalIgnoreCase) ||
                                !delivered.Any(d => d.Target.Equals(l.Target, StringComparison.OrdinalIgnoreCase) &&
                                                    d.ModelFile.Equals(l.ModelFile, StringComparison.OrdinalIgnoreCase)) ||
                                delivered.Any(d => d.Target.Equals(l.Target, StringComparison.OrdinalIgnoreCase) &&
                                                   !d.ModelFile.Equals(l.ModelFile, StringComparison.OrdinalIgnoreCase))))
                            return false;
                    }
                }
            }
            foreach (var directory in target.Elements().Where(e => e.Name.LocalName == "MakeDir"))
            {
                var path = ResolveBuildPath((string?)directory.Attribute("Directories"), consumer, evaluation);
                if (path == null || !destinations.Contains(path)) return false;
            }
        }
        return true;
    }

    private IEnumerable<MetadataLayout> PropagatedMetadataLayouts(ProjectReport consumer, string configuration)
    {
        var pending = new Stack<(ProjectReport Project, bool Recurse)>();
        var visited = new HashSet<(string Path, bool Recurse)>();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push((consumer, true));
        while (pending.TryPop(out var current))
        {
            if (!visited.Add((current.Project.Path, current.Recurse))) continue;
            if (emitted.Add(current.Project.Path))
                foreach (var layout in MetadataOutputLayouts([current.Project], configuration)) yield return layout;
            var evaluation = current.Project.Evaluations.Single(e => e.Configuration == configuration);
            if (!current.Recurse || !IsTrue(evaluation, "_GetChildProjectCopyToOutputDirectoryItems") ||
                !IsTrue(evaluation, "_GetChildProjectCopyToPublishDirectoryItems") ||
                IsTrue(evaluation, "UseCommonOutputDirectory") ||
                evaluation.Properties["_GlobalPropertiesToRemoveFromProjectReferences"].Length != 0) continue;
            var recursiveTarget = evaluation.Properties["_RecursiveTargetForContentCopying"];
            if (recursiveTarget is not ("GetCopyToOutputDirectoryItems" or "_GetCopyToOutputDirectoryItemsFromThisProject")) continue;
            foreach (var reference in evaluation.Items["ProjectReference"])
            {
                if (!DefaultContentReference(reference)) continue;
                var fullPath = ResolveBuildPath(reference["Identity"], current.Project, evaluation);
                if (fullPath == null) continue;
                var relative = Path.GetRelativePath(options.Source, fullPath);
                var child = projects.FirstOrDefault(p => p.Path.Equals(relative, StringComparison.OrdinalIgnoreCase));
                if (child != null) pending.Push((child, recursiveTarget == "GetCopyToOutputDirectoryItems"));
            }
        }
    }

    private static bool DefaultContentReference(SortedDictionary<string, string> reference)
    {
        foreach (var (name, value) in reference)
        {
            if (value.Length == 0 || name is "Identity" or "DefiningProjectFullPath" or "Name" or "Project") continue;
            if (name is "Private" or "BuildReference")
            {
                if (!value.Equals("true", StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (name == "ReferenceOutputAssembly")
            {
                // Content propagation does not depend on referencing the producer's assembly.
                if (!value.Equals("true", StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (name != "ReferenceSourceTarget" || value != "ProjectReference")
                return false;
        }
        return true;
    }

    private IEnumerable<MetadataLayout> MetadataOutputLayouts(IEnumerable<ProjectReport> producers, string configuration)
    {
        foreach (var producer in producers)
            foreach (var evaluation in producer.Evaluations.Where(e => e.Configuration == configuration))
            {
                var directory = ResolveBuildPath("$(OutDir)", producer, evaluation);
                if (directory == null) continue;
                foreach (var item in evaluation.Items["EntityDeploy"].Concat(evaluation.Items["EntityModel"]))
                {
                    var modelFile = ResolveBuildPath(item["Identity"], producer, evaluation);
                    if (modelFile == null) continue;
                    var stem = item.GetValueOrDefault("MetadataPath") ??
                        Path.ChangeExtension(item.GetValueOrDefault("Link", item["Identity"]), null);
                    if (string.IsNullOrEmpty(stem) || Path.IsPathRooted(stem) || stem.Split('\\', '/').Contains("..") ||
                        stem.IndexOfAny(['$', '@', '%', '*', '?', ';']) >= 0) continue;
                    foreach (var extension in SchemaExtensions)
                        yield return new(Path.GetFullPath(Path.Combine(directory, stem + extension)), modelFile, stem + extension);
                }
            }
    }

    private string? ResolveBuildPath(string? path, ProjectReport project, EvaluatedProject evaluation)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var projectDirectory = Path.GetFullPath(Path.Combine(options.Source, Path.GetDirectoryName(project.Path)!));
        var expanded = Regex.Replace(path, @"\$\(([^)]+)\)", match =>
        {
            var name = match.Groups[1].Value;
            if (name == "MSBuildProjectDirectory") return projectDirectory;
            if (name == "Configuration") return evaluation.Configuration;
            if (name == "Platform") return options.Platform;
            if (name == "OutDir" && evaluation.Properties.GetValueOrDefault(name, "").Length == 0) return evaluation.Properties["OutputPath"];
            return evaluation.Properties.GetValueOrDefault(name, match.Value);
        }).Replace("{workspace}\\", options.Source + "\\", StringComparison.Ordinal);
        if (expanded.IndexOfAny(['$', '@', '%', '*', '?', ';', '{', '}']) >= 0) return null;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(projectDirectory, expanded)));
    }
}

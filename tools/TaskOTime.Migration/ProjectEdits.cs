using System.Xml.Linq;

namespace TaskOTime.Migration;

internal static class ProjectEdits
{
    internal static void Check(string path)
    {
        var xml = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        foreach (var import in xml.Descendants().Where(e => e.Name.LocalName == "Import")) {
            var value = import.Attribute("Project")?.Value ?? "";
            if (!value.EndsWith("Microsoft.VisualBasic.targets", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("Microsoft.Common.props", StringComparison.OrdinalIgnoreCase))
                throw new MigrationException("CUSTOM_IMPORT", $"Review custom import before conversion: {path}: {value}");
        }
        if (xml.Descendants().Any(e => e.Name.LocalName is "TargetFrameworks" or "Target" ||
            (e.Name.LocalName == "DefineConstants" && e.Value.Contains('='))))
            throw new MigrationException("UNSUPPORTED_PROJECT", $"Multitargeting, custom targets and valued conditional constants need a reviewed adapter: {path}");
        if (Migration.Files(Path.GetDirectoryName(path)!).Any(p => p.EndsWith(".resx", StringComparison.OrdinalIgnoreCase)))
            throw new MigrationException("RESOURCE_DESIGNER", $"Review .resx resource identity/designer relocation before conversion: {path}");
    }

    internal static void Apply(string root, Dictionary<string, string> mapping, List<CompilationEvidence> compilations)
    {
        foreach (var relative in Migration.Files(root).Where(p => p.EndsWith("proj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)).ToArray()) {
            var path = Path.Combine(root, relative);
            var xml = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var changed = false;
            foreach (var attribute in xml.Descendants().Attributes().Where(a => a.Name.LocalName is "Include" or "Update" or "Remove" or "Path")) {
                if (attribute.Value.Contains("$(") || attribute.Value.Contains('*') || attribute.Value.Contains(';')) continue;
                var reference = Migration.SafeRelative(root, Path.Combine(Path.GetDirectoryName(path)!, attribute.Value.Replace('/', Path.DirectorySeparatorChar)));
                if (mapping.TryGetValue(reference, out var replacement)) {
                    attribute.Value = Path.GetRelativePath(Path.GetDirectoryName(path)!, Path.Combine(root, replacement));
                    changed = true;
                }
            }
            if (mapping.TryGetValue(relative, out var newProject) && relative.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)) {
                var ns = xml.Root!.Name.Namespace;
                foreach (var element in xml.Descendants().Where(e => e.Name.LocalName is "OptionStrict" or "OptionExplicit" or "OptionInfer" or "OptionCompare" or "MyType" or "VBRuntime").ToArray())
                    element.Remove();
                foreach (var element in xml.Descendants().Where(e => e.Name.LocalName == "Import"))
                    element.Attribute("Project")!.Value = element.Attribute("Project")!.Value.Replace("Microsoft.VisualBasic.targets", "Microsoft.CSharp.targets", StringComparison.OrdinalIgnoreCase);
                foreach (var element in xml.Descendants().Where(e => e.Name.LocalName is "DependentUpon" or "LastGenOutput"))
                    if (element.Value.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)) element.Value = Path.ChangeExtension(element.Value, ".cs");
                foreach (var element in xml.Descendants().Where(e => e.Name.LocalName == "DefineConstants"))
                    element.Value = element.Value.Replace(',', ';');
                foreach (var property in xml.Descendants().Where(e => e.Name.LocalName is "DefineDebug" or "DefineTrace").ToArray()) {
                    if (property.Value.Equals("true", StringComparison.OrdinalIgnoreCase)) {
                        var defines = property.Parent!.Element(ns + "DefineConstants");
                        if (defines is null) property.Parent.Add(defines = new XElement(ns + "DefineConstants"));
                        defines.Value += ";" + (property.Name.LocalName == "DefineDebug" ? "DEBUG" : "TRACE");
                    }
                    property.Remove();
                }
                var group = new XElement(ns + "PropertyGroup", new XElement(ns + "LangVersion", "13.0"),
                    new XElement(ns + "DefaultItemExcludes", "$(DefaultItemExcludes);**\\*.vb"));
                xml.Root.Add(group);
                if (!xml.Descendants(ns + "Reference").Any(e => e.Attribute("Include")?.Value == "Microsoft.VisualBasic"))
                    xml.Root.Add(new XElement(ns + "ItemGroup", new XElement(ns + "Reference", new XAttribute("Include", "Microsoft.VisualBasic"))));
                QualifyXaml(Path.GetDirectoryName(path)!, compilations.Single(c => c.Project == Path.GetFileNameWithoutExtension(path)).RootNamespace);
                path = Path.Combine(root, newProject);
                changed = true;
            }
            if (changed) xml.Save(path, SaveOptions.DisableFormatting);
        }
        // Legacy .sln is intentionally rejected rather than applying unstructured replacements.
        foreach (var relative in Migration.Files(root).Where(p => p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))) {
            var contents = File.ReadAllText(Path.Combine(root, relative));
            if (mapping.Keys.Where(p => p.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)).Any(p => contents.Contains(Path.GetFileName(p), StringComparison.OrdinalIgnoreCase)))
                throw new MigrationException("LEGACY_SOLUTION", $"Migrate {relative} to .slnx first; legacy solution rewriting is not supported.");
        }
    }

    private static void QualifyXaml(string projectDirectory, string rootNamespace)
    {
        if (rootNamespace.Length == 0) return;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var relative in Migration.Files(projectDirectory).Where(p => p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))) {
            var path = Path.Combine(projectDirectory, relative);
            var xml = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            if (xml.Root?.Attribute(x + "Class") is not { } attribute) continue;
            attribute.Value = rootNamespace + "." + attribute.Value;
            xml.Save(path, SaveOptions.DisableFormatting);
        }
    }
}

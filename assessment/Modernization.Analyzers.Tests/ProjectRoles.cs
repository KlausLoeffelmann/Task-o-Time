using System.Text.RegularExpressions;
using ExternalEvaluation;

namespace Modernization.Analyzers.Tests;

internal sealed record ProjectRoleDeclaration(string Project, string Role,
    SortedDictionary<string, string>? BuildProperties = null);

internal static class ProjectRoles
{
    internal static Dictionary<string, ProjectRoleDeclaration> Read(ReplayPlan? plan)
    {
        var roles = new Dictionary<string, ProjectRoleDeclaration>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in plan?.Projects ?? [])
        {
            if (!Path.IsPathFullyQualified(project)) throw new InvalidDataException("Declared tool project paths must be absolute.");
            roles.TryAdd(Path.GetFullPath(project), new(Path.GetFullPath(project), "tool"));
        }
        var explicitPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in plan?.ProjectRoles ?? [])
        {
            if (!Path.IsPathFullyQualified(declaration.Project) ||
                declaration.Role is not ("tool" or "fixture" or "validation"))
                throw new InvalidDataException("Project roles require exact absolute project paths and tool/fixture/validation roles.");
            var path = Path.GetFullPath(declaration.Project);
            if (!explicitPaths.Add(path) || roles.ContainsKey(path) && declaration.Role != "tool")
                throw new InvalidDataException("Duplicate or conflicting trusted project role: " + path);
            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in declaration.BuildProperties ?? [])
            {
                if (!propertyNames.Add(property.Key) || !Regex.IsMatch(property.Key, @"^[A-Za-z_][A-Za-z0-9_]*$") ||
                    property.Value.Contains(';') || property.Value.Contains('\r') || property.Value.Contains('\n') ||
                    new[] { "Configuration", "IntermediateOutputPath", "BaseIntermediateOutputPath", "OutputPath",
                        "BaseOutputPath", "DesignTimeBuild", "SkipCompilerExecution", "ProvideCommandLineArgs",
                        "BuildProjectReferences", "CustomAfterMicrosoftCommonTargets" }.Contains(property.Key, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid or reserved role build property: " + property.Key);
            }
            roles[path] = declaration with { Project = path };
        }
        return roles;
    }

    internal static bool InApplication(string path, string sourceRoot) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(sourceRoot).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    internal static void ValidateInventory(IEnumerable<string> projects, string sourceRoot,
        IReadOnlyDictionary<string, ProjectRoleDeclaration> roles)
    {
        foreach (var path in projects.Concat(roles.Keys).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path) || Path.GetExtension(path).ToLowerInvariant() is not (".csproj" or ".vbproj"))
                throw new InvalidDataException("Missing or unsupported inventory project: " + path);
            if (InApplication(path, sourceRoot) && roles.ContainsKey(path))
                throw new InvalidDataException("A role declaration cannot exempt an application inventory project: " + path);
            if (!InApplication(path, sourceRoot) && !roles.ContainsKey(path))
                throw new InvalidDataException("External project requires a trusted exact role declaration: " + path);
        }
    }

    internal static void ValidateDependencies(IEnumerable<LoadedProject> projects)
    {
        var all = projects.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var project in all.Values.Where(p => !p.IsTest))
        foreach (var file in project.AdditionalFiles.Where(f => f.Path == project.Path + ".assessment"))
        foreach (var reference in Evidence.Xml(file).Descendants("ProjectReference"))
            if (reference.Attribute("Path")?.Value is { } path && all.TryGetValue(path, out var dependency) &&
                dependency.State?.Role is "fixture" or "validation")
                throw new InvalidDataException("Production/tool code depends on a fixture/validation exemption: " + path);
    }
}

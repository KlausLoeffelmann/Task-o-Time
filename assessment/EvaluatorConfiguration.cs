using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace ExternalEvaluation;

internal static class EvaluatorConfiguration
{
    private static string[] Paths => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "evaluator-paths.txt"));
    internal static string SourceRoot => Path.GetFullPath(
        Environment.GetEnvironmentVariable("TASKOTIME_SOURCE_ROOT") ?? Paths[0]);
    internal static string ArtifactRoot => Path.GetFullPath(Paths[1]);
    internal static string AssessmentRoot => Path.GetFullPath(Paths[2]);
    internal static string BuildConfiguration => Environment.GetEnvironmentVariable("ASSESSMENT_CONFIGURATION") ?? "Debug";
    internal static IEnumerable<string> DiscoveryRoots =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ScenarioScope.xml")).Root!
            .Element("Discovery")!.Elements("Root")
            .Select(e => Path.GetFullPath(Path.Combine(SourceRoot, e.Value)))
            .Append(Environment.GetEnvironmentVariable("MIGRATION_TOOL_ROOT") ?? SourceRoot)
            .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    internal static readonly ScenarioSelection Selection = ScenarioSelection.Load(
        Path.Combine(AppContext.BaseDirectory, "ScenarioScope.xml"));
}

internal sealed class ScenarioSelection
{
    private readonly HashSet<string> targetTypes;
    private readonly HashSet<string> protectedTypes;
    private readonly string[] targetSources;
    private readonly string[] protectedSources;

    internal ScenarioSelection(IEnumerable<string> targets, IEnumerable<string> protectedCore,
        IEnumerable<string> sources, IEnumerable<string> coreSources)
    {
        static IEnumerable<string> Aliases(string name) =>
            new[] { name, name.Replace("MasterData", "MainData").Replace("MasterTask", "MainTask") };
        targetTypes = new HashSet<string>(targets.SelectMany(Aliases), StringComparer.Ordinal);
        protectedTypes = new HashSet<string>(protectedCore, StringComparer.Ordinal);
        targetSources = sources.SelectMany(Aliases).Select(Normalize).ToArray();
        protectedSources = coreSources.SelectMany(s => new[] { s, Path.ChangeExtension(s, ".cs") })
            .Select(Normalize).ToArray();
    }

    internal static ScenarioSelection Load(string path)
    {
        var document = XDocument.Load(path);
        IEnumerable<string> Values(string scope, string kind) =>
            (document.Root?.Element(scope) ?? throw new InvalidDataException("Missing scenario scope: " + scope))
                .Elements(kind).Select(e => e.Value);
        return new ScenarioSelection(Values("MasterData", "Type"), Values("ProtectedCore", "Type"),
            Values("MasterData", "Source"), Values("ProtectedCore", "Source"));
    }

    private static string Normalize(string path) => path.Replace('/', '\\');
    private static bool Matches(string path, IEnumerable<string> sources) =>
        sources.Any(s => ("\\" + Normalize(path)).EndsWith("\\" + s, StringComparison.OrdinalIgnoreCase));
    private static IEnumerable<INamedTypeSymbol> Owners(INamedTypeSymbol type)
    {
        for (var owner = type; owner != null; owner = owner.ContainingType) yield return owner;
    }
    internal bool IsProtected(INamedTypeSymbol type) =>
        Owners(type).Any(owner => protectedTypes.Contains(owner.ToDisplayString()) ||
            owner.Locations.Any(l => l.IsInSource && Matches(l.SourceTree.FilePath, protectedSources)));
    internal bool Includes(INamedTypeSymbol type) =>
        !IsProtected(type) && Owners(type).Any(owner => targetTypes.Contains(owner.ToDisplayString()) ||
            owner.Locations.Any(l => l.IsInSource && IncludesSource(l.SourceTree.FilePath)));
    internal bool IncludesSource(string path) =>
        !Matches(path, protectedSources) && Matches(path, targetSources);
}

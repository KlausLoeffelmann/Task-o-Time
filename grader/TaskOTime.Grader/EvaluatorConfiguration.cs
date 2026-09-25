using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace ExternalEvaluation;

internal static class EvaluatorConfiguration
{
    private static readonly string? SourceOverride = Environment.GetEnvironmentVariable("TASKOTIME_SOURCE_ROOT");
    internal static string SourceRoot { get; } = SourceOverride is { Length: > 0 }
        ? ValidateSourceRoot(SourceOverride)
        : FindSourceRoot(AppContext.BaseDirectory);
    internal static bool SourceOverrideUsed => SourceOverride is { Length: > 0 };
    internal static string ArtifactRoot { get; } = Path.Combine(FindGraderRoot(AppContext.BaseDirectory), "Artifacts");
    internal static string BuildConfiguration { get; } =
        Environment.GetEnvironmentVariable("ASSESSMENT_CONFIGURATION") ??
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "evaluator-configuration.txt")).Trim();
    internal static readonly ScenarioSelection Selection = ScenarioSelection.Load(
        Path.Combine(AppContext.BaseDirectory, "ScenarioScope.xml"));

    private static string FindSourceRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "TaskOTime");
            if (File.Exists(Path.Combine(candidate, "TaskOTime.slnx"))) return candidate;
        }
        throw new DirectoryNotFoundException(
            "Cannot find src\\TaskOTime. Copy the grader to the repository root or set TASKOTIME_SOURCE_ROOT.");
    }

    private static string FindGraderRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TaskOTime.Grader.csproj")) ||
                File.Exists(Path.Combine(directory.FullName, "Modernization.Analyzers.Tests.csproj")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Cannot find the TaskOTime.Grader project directory.");
    }

    private static string ValidateSourceRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(Path.Combine(full, "TaskOTime.slnx")))
            throw new DirectoryNotFoundException("TASKOTIME_SOURCE_ROOT does not contain TaskOTime.slnx: " + full);
        return full;
    }
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

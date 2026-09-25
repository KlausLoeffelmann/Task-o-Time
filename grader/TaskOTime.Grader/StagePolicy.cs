namespace Modernization.Analyzers.Tests;

public sealed record ProjectState(string Path, string Language, bool Test, bool Tooling, bool SdkStyle,
    string TargetFramework, string FrameworkIdentifier, string FrameworkVersion, string Platform,
    string Configuration, string Configurations);
public sealed record CriterionResult(string Id, string Name, double Weight, double? Score, string Status, string Evidence);

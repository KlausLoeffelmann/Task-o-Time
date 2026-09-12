using ExternalEvaluation;

namespace Modernization.Analyzers.Tests;

internal enum StartingStage { S0, S1, S2, S2a, S3, S4 }
internal enum EvaluationMode { StartingPointIntegrity, FinalDelivery }
public sealed record ProjectState(string Path, string Language, bool Test, bool Tooling, bool SdkStyle,
    string TargetFramework, string FrameworkIdentifier, string FrameworkVersion, string Platform,
    string Configuration, string Configurations);
public sealed record CriterionResult(string Id, double Weight, double? Score, string Applicability, string Status,
    string StartingState);

internal sealed record StagePolicy(StartingStage Stage, EvaluationMode Mode)
{
    // These selectors are supplied by the trusted assessor process, never read from a candidate project.
    internal static StagePolicy Current => Parse(
        Environment.GetEnvironmentVariable("ASSESSMENT_STAGE") ?? "S0",
        Environment.GetEnvironmentVariable("ASSESSMENT_MODE") ?? "final-delivery");

    internal static StagePolicy Parse(string stage, string mode)
    {
        if (!Enum.TryParse<StartingStage>(stage, false, out var value) || !Enum.IsDefined(value))
            throw new InvalidDataException("Unknown trusted starting stage: " + stage);
        return new(value, mode switch
        {
            "starting-point-integrity" => EvaluationMode.StartingPointIntegrity,
            "final-delivery" => EvaluationMode.FinalDelivery,
            _ => throw new InvalidDataException("Unknown evaluation mode: " + mode)
        });
    }

    internal string Identity => $"{Stage}-{Mode}-{EvaluatorConfiguration.BuildConfiguration}";
    internal bool LanguageReplay => Stage is StartingStage.S0 or StartingStage.S1 or StartingStage.S4;
    internal bool ProjectReplay => Stage != StartingStage.S3;
    internal string Applicability(string criterion)
    {
        if (criterion == "TOOL" && Stage == StartingStage.S3) return "not-applicable";
        var completed = Stage != StartingStage.S4 && (criterion == "LNG" && Stage >= StartingStage.S2 ||
            criterion == "SDK" && Stage >= StartingStage.S2a || criterion == "NET10" && Stage >= StartingStage.S3);
        if (completed) return "pre-satisfied";
        return Mode == EvaluationMode.StartingPointIntegrity && Stage != StartingStage.S4 ? "deferred" : "applicable";
    }
    internal string StartingState(string criterion) => Stage == StartingStage.S4 ? "ideal-required" :
        Applicability(criterion) == "pre-satisfied" ? "already-completed" :
        Applicability(criterion) == "not-applicable" ? "not-required" : "remaining-work";

    internal string[] CheckIntegrity(ProjectState[] projects, ScanDiagnostic[] diagnostics)
    {
        var failures = new List<string>();
        var app = projects.Where(p => !p.Tooling).ToArray();
        var production = app.Where(p => !p.Test).ToArray();
        if (production.Length == 0) failures.Add("No production projects.");
        bool Framework(ProjectState p, string version) =>
            p.FrameworkIdentifier == ".NETFramework" && p.FrameworkVersion == version;
        if (Stage == StartingStage.S0)
        {
            if (!app.Any(p => Framework(p, "v4.6.1")) || !app.Any(p => Framework(p, "v4.7.2")) ||
                app.Any(p => !Framework(p, "v4.6.1") && !Framework(p, "v4.7.2")))
                failures.Add("S0 requires original mixed net461/net472 evaluated targets.");
        }
        else if (Stage <= StartingStage.S2a)
        {
            if (app.Any(p => !Framework(p, "v4.7.2"))) failures.Add("All application/test projects must target net472.");
        }
        else if (app.Any(p => p.FrameworkIdentifier != ".NETCoreApp" || p.FrameworkVersion != "v10.0"))
            failures.Add("All application/test projects must target .NET 10.");
        if (Stage >= StartingStage.S2 && production.Any(p => p.Language != "C#"))
            failures.Add("Production must be C#; test VB is permitted.");
        if (Stage <= StartingStage.S1 && !production.Any(p => p.Language == "Visual Basic"))
            failures.Add("The VB starting point lost production VB.");
        if (Stage >= StartingStage.S2a && production.Any(p => !p.SdkStyle))
            failures.Add("All production projects must be SDK-style.");
        if (Stage < StartingStage.S2a && (!production.Any(p => !p.SdkStyle) || !production.Any(p => p.SdkStyle)))
            failures.Add("Original mixed project styles must be retained before S2a.");
        if (diagnostics.Any(d => d.Id is "SCP001" or "COR001" or "EF001" or "ASM001" or "LOAD001" or "AD0001"))
            failures.Add("Protected core, source scope, EF6, or input validity failed.");
        if (Stage != StartingStage.S4)
        {
            foreach (var id in new[] { "BUS001", "BUS002" })
                if (diagnostics.Count(d => d.Id == id) != 1) failures.Add("Preservation requires exactly one " + id + ".");
            foreach (var prefix in new[] { "MOD", "LOC", "ENG", "NAM", "THM" })
                if (!diagnostics.Any(d => d.Id.StartsWith(prefix, StringComparison.Ordinal) && d.Id != "MOD006"))
                    failures.Add("Missing intentional nonmigration defect family: " + prefix);
        }
        else if (diagnostics.Length != 0) failures.Add("Golden requires zero defects.");
        return failures.ToArray();
    }
}

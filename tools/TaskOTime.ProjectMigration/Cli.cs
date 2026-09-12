using System.Text.Json;

namespace TaskOTime.ProjectMigration;

public static class Cli
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"])
            {
                output.WriteLine("""
                    ProjectMigration 1.1.1 (local .NET 10 MSBuild; trusted workspaces only)
                    inspect --source <workspace> [--configuration Debug,Release] [--platform AnyCPU]
                    normalize-framework --target net472 --source <workspace> --output <new-workspace> [--dry-run]
                    convert-projects --sdk-style --source <workspace> --output <new-workspace> [--dry-run]
                    prepare-net10 --source <workspace> --output <new-workspace> [--dry-run]
                    retarget --framework net10.0 --wpf-framework net10.0-windows --source <workspace> --output <new-workspace> [--dry-run]
                    All commands emit deterministic JSON to stdout. Mutations save migration-manifest.json.
                    --dryrun is an alias for --dry-run. Output must not exist or overlap source.
                    Framework normalization includes C# and VB application/tests; tools/assessment are excluded.
                    Custom configurations/platforms must be supplied explicitly; defaults: Debug,Release / AnyCPU.
                    """);
                return 0;
            }

            var options = Options.Parse(args);
            var result = new Migration(options).Run();
            output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return result.Diagnostics.Any(d => d.Severity == "error") ? 2 : 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                   or System.Xml.XmlException or InvalidOperationException or JsonException)
        {
            if (Environment.GetEnvironmentVariable("PROJECT_MIGRATION_TRACE") == "1") error.WriteLine(ex);
            error.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions));
            return 2;
        }
    }
}

public sealed record Options(string Command, string Source, string? Output, bool DryRun,
    string[] Configurations, string Platform)
{
    public static Options Parse(string[] args)
    {
        var command = args[0];
        if (command is not ("inspect" or "normalize-framework" or "convert-projects" or "prepare-net10" or "retarget"))
            throw new ArgumentException($"Unknown command '{command}'. Use --help.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i] == "--dryrun" ? "--dry-run" : args[i];
            if (key is "--dry-run" or "--sdk-style")
            {
                if (!flags.Add(key)) throw new ArgumentException($"Duplicate option {key}.");
            }
            else if (key is "--source" or "--output" or "--target" or "--framework" or "--wpf-framework"
                     or "--configuration" or "--platform")
            {
                if (++i == args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing value for {key}.");
                if (!values.TryAdd(key, args[i])) throw new ArgumentException($"Duplicate option {key}.");
            }
            else throw new ArgumentException($"Unknown option '{key}'.");
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Required option: {key}.");
        if (command == "normalize-framework" && Required("--target") != "net472")
            throw new ArgumentException("normalize-framework supports only --target net472.");
        if (command == "convert-projects" && !flags.Contains("--sdk-style"))
            throw new ArgumentException("convert-projects requires --sdk-style.");
        if (command == "retarget" && (Required("--framework") != "net10.0" ||
                                      Required("--wpf-framework") != "net10.0-windows"))
            throw new ArgumentException("retarget requires --framework net10.0 --wpf-framework net10.0-windows.");
        foreach (var key in values.Keys)
            if ((key == "--target" && command != "normalize-framework") ||
                (key is "--framework" or "--wpf-framework" && command != "retarget"))
                throw new ArgumentException($"{key} does not apply to {command}.");
        if (flags.Contains("--sdk-style") && command != "convert-projects")
            throw new ArgumentException($"--sdk-style does not apply to {command}.");
        if (command == "inspect" && (values.ContainsKey("--output") || flags.Contains("--dry-run")))
            throw new ArgumentException("inspect is read-only; it takes neither --output nor --dry-run.");
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Required("--source")));
        var destination = command == "inspect" ? null :
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Required("--output")));
        if (!Directory.Exists(source)) throw new ArgumentException($"Source workspace does not exist: {source}");
        if (destination != null && (Workspace.IsWithin(destination, source) || Workspace.IsWithin(source, destination)))
            throw new ArgumentException("Source/output workspaces overlap. Use separate sibling directories.");
        if (destination != null && (Directory.Exists(destination) || File.Exists(destination)))
            throw new ArgumentException("Output workspace already exists. Supply a new path; existing data is never overwritten.");
        Workspace.RejectLinkedAncestors(source);
        if (destination != null) Workspace.RejectLinkedAncestors(destination);
        var configurations = values.GetValueOrDefault("--configuration", "Debug,Release").Split(',');
        var platform = values.GetValueOrDefault("--platform", "AnyCPU");
        if (configurations.Any(c => !SafeProperty(c)) || !SafeProperty(platform))
            throw new ArgumentException("Configuration/platform must contain only letters, digits, spaces, _, -, or .");
        return new(command, source, destination, flags.Contains("--dry-run"),
            configurations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), platform);
    }

    private static bool SafeProperty(string value) =>
        value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' or '.');
}

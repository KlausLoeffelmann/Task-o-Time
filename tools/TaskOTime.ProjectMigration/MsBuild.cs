using System.Diagnostics;
using System.Text.Json;

namespace TaskOTime.ProjectMigration;

public sealed record EvaluatedProject(string Configuration, SortedDictionary<string, string> Properties,
    SortedDictionary<string, List<SortedDictionary<string, string>>> Items);

internal static class MsBuild
{
    private static readonly string[] Properties =
    [
        "TargetFramework", "TargetFrameworks", "TargetFrameworkVersion", "TargetFrameworkIdentifier",
        "TargetFrameworkProfile", "UseWPF", "UseWindowsForms", "OutputPath", "BaseOutputPath",
        "AppendTargetFrameworkToOutputPath", "AssemblyName", "RootNamespace", "OutputType",
        "DefineConstants", "StartupObject", "ApplicationIcon", "SignAssembly", "AssemblyOriginatorKeyFile",
        "OptionStrict", "OptionExplicit", "OptionInfer", "OptionCompare", "MyType",
        "EnableDefaultItems", "GenerateAssemblyInfo", "EntityDeployDependsOn"
    ];
    private static readonly string[] ItemNames =
    [
        "Compile", "EmbeddedResource", "Resource", "Page", "ApplicationDefinition", "EntityDeploy",
        "Content", "None", "ProjectReference", "Reference", "PackageReference"
    ];
    private static readonly HashSet<string> Metadata = new(StringComparer.Ordinal)
    {
        "Identity", "Link", "LinkBase", "LogicalName", "ManifestResourceName", "DependentUpon", "Generator",
        "LastGenOutput", "AutoGen", "DesignTime", "CopyToOutputDirectory", "CopyToPublishDirectory",
        "Private", "Aliases", "HintPath", "Version", "PrivateAssets", "SubType", "TargetPath",
        "ReferenceOutputAssembly", "EmbedInteropTypes"
    };

    public static string Version() => Execute(["--version"]).Trim();

    public static EvaluatedProject Evaluate(string path, string configuration, string platform, string root)
    {
        var text = Execute(["msbuild", path, "-nologo", "-verbosity:quiet",
            $"-property:Configuration={configuration}", $"-property:Platform={platform}",
            "-getProperty:" + string.Join(',', Properties), "-getItem:" + string.Join(',', ItemNames)]);
        var start = text.IndexOf('{');
        if (start < 0) throw new InvalidOperationException("MSBuild returned no evaluated JSON.");
        using var document = JsonDocument.Parse(text[start..]);
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.GetProperty("Properties").EnumerateObject())
            properties.Add(property.Name, Normalize(property.Value.GetString() ?? "", root));
        var items = new SortedDictionary<string, List<SortedDictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var itemType in document.RootElement.GetProperty("Items").EnumerateObject())
        {
            var rows = new List<SortedDictionary<string, string>>();
            foreach (var item in itemType.Value.EnumerateArray())
            {
                var row = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var metadata in item.EnumerateObject().Where(m => Metadata.Contains(m.Name)))
                    row[metadata.Name] = Normalize(metadata.Value.GetString() ?? "", root);
                rows.Add(row);
            }
            items[itemType.Name] = rows.OrderBy(r => r["Identity"], StringComparer.Ordinal).ToList();
        }
        return new(configuration, properties, items);
    }

    private static string Normalize(string value, string root) =>
        value.Replace(root + Path.DirectorySeparatorChar, "{workspace}\\", StringComparison.OrdinalIgnoreCase)
            .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\.nuget\\packages\\",
                "{nuget}\\", StringComparison.OrdinalIgnoreCase);

    private static string Execute(string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start local dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("MSBuild evaluation timed out after 120 seconds.");
        }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Local MSBuild evaluation failed ({process.ExitCode}):\n{stdout.Result}\n{stderr.Result}");
        return stdout.Result;
    }
}

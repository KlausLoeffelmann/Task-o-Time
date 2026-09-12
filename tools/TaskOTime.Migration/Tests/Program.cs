using System.Diagnostics;
using System.Text.Json;
using TaskOTime.Migration;

var tool = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var root = Path.Combine(tool, "artifacts", "tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var input = Path.Combine(root, "input");
CopyTree(Path.Combine(tool, "Tests", "Fixtures"), input);
var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
var cli = Path.Combine(tool, "bin", configuration, "net10.0", "TaskOTime.Migration.dll");
var output = Path.Combine(root, "output");
var baseline = await Run("dotnet", input, "build", @"Consumer\Consumer.csproj", "--verbosity", "quiet");
Require(baseline.Code == 0, baseline.Text);
var before = await Run(Path.Combine(input, "Consumer", "bin", "Debug", "net472", "Consumer.exe"), input);
Require(before.Code == 0 && before.Text.Contains("CANARY_OK"), "VB baseline canary: " + before.Text);
var dryRun = await Convert(input, output, "--dry-run");
Require(dryRun.Code == 0 && !Directory.Exists(output) && !Directory.Exists(output + ".incomplete"), "dry run mutates workspace: " + dryRun.Text);
var conversion = await Convert(input, output);
Require(conversion.Code == 0, conversion.Text);
var collection = File.ReadAllText(Path.Combine(output, "Canary", "Collection.cs"));
var view = File.ReadAllText(Path.Combine(output, "Canary", "Probe.xaml.cs"));
Require(collection.Contains("IList.this[") && !collection.Contains("IList.get_Item"), "actual emitted explicit indexer");
Require(collection.Contains("IReadOnlyList<string>.this[") && !collection.Contains("IReadOnlyList<string>.get_Item"),
    "second explicit interface indexer was not repaired");
Require(collection.Contains("Keep this comment verbatim: sortering is bewust."), "comment preservation");
Require(view.Contains("InitializeComponent();") && view.Contains("Loaded +="), "actual WPF emitted code");
var afterBuild = await Run("dotnet", output, "build", @"Consumer\Consumer.csproj", "--verbosity", "quiet");
Require(afterBuild.Code == 0, afterBuild.Text);
var after = await Run(Path.Combine(output, "Consumer", "bin", "Debug", "net472", "Consumer.exe"), output);
Require(after.Code == 0 && after.Text == before.Text, "converted behavioral canary: " + after.Text);
var repeat = Path.Combine(root, "repeat");
var replay = await Convert(input, repeat);
Require(replay.Code == 0, replay.Text);
using var firstManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "migration-manifest.json")));
using var secondManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(repeat, "migration-manifest.json")));
Require(firstManifest.RootElement.GetProperty("OutputFiles").GetRawText() == secondManifest.RootElement.GetProperty("OutputFiles").GetRawText(), "repeatability output hashes");
Require(firstManifest.RootElement.GetProperty("Compilations")[0].GetProperty("GeneratedContextDocuments").GetInt32() > 0, "generated WPF semantics missing");
Require(firstManifest.RootElement.GetProperty("CompiledOutputs")[0].GetProperty("EmittedSources").GetArrayLength() == 2,
    "emitted source compilation coverage missing");
Require((await Convert(input, input)).Code != 0, "collision accepted");
Require((await Convert(output, Path.Combine(root, "no-vb"))).Code != 0, "already converted input accepted");
var unsupported = Path.Combine(root, "unsupported");
CopyTree(input, unsupported);
File.WriteAllText(Path.Combine(unsupported, "Canary", "Canary.vbproj"), "<Project><Import Project=\"unknown.targets\" /></Project>");
Require((await Convert(unsupported, Path.Combine(root, "unsupported-out"))).Code != 0, "unsupported custom import accepted");
var reassigned = Path.Combine(root, "reassigned");
CopyTree(input, reassigned);
var probePath = Path.Combine(reassigned, "Canary", "Probe.xaml.vb");
File.WriteAllText(probePath, File.ReadAllText(probePath).Replace("            Loads += 1", "            ActionButton = New Button()\n            Loads += 1"));
var rejected = await Convert(reassigned, Path.Combine(root, "reassigned-out"));
Require(rejected.Code != 0 && rejected.Text.Contains("WPF_REASSIGNMENT") && !Directory.Exists(Path.Combine(root, "reassigned-out")),
    "unsafe generated WithEvents lifetime accepted: " + rejected.Text);
using var failedManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reassigned-out.incomplete", "migration-manifest.json")));
Require(failedManifest.RootElement.GetProperty("Status").GetString() == "failed", "failed output advertised as success");
foreach (var (name, include) in new[] { ("wildcard", @"Code\*.vb"), ("property", "$(SourceFiles)"), ("semicolon", @"Code\One.vb;Code\Two.vb") }) {
    var unsupportedCompile = Path.Combine(root, name);
    Directory.CreateDirectory(Path.Combine(unsupportedCompile, "Code"));
    File.WriteAllText(Path.Combine(unsupportedCompile, "Code", "One.vb"), "Public Class One\nEnd Class");
    File.WriteAllText(Path.Combine(unsupportedCompile, "Code", "Two.vb"), "Public Class Two\nEnd Class");
    File.WriteAllText(Path.Combine(unsupportedCompile, "Example.vbproj"), $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net472</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><SourceFiles>Code\One.vb</SourceFiles></PropertyGroup>
          <ItemGroup><Compile Include="{include}" /><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net472" Version="1.0.3" /></ItemGroup>
        </Project>
        """);
    var unsupportedResult = await Convert(unsupportedCompile, unsupportedCompile + "-out");
    Require(unsupportedResult.Code != 0 && unsupportedResult.Text.Contains("UNSUPPORTED_COMPILE") &&
        !Directory.Exists(unsupportedCompile + "-out.incomplete"), "unsupported Compile expression accepted: " + unsupportedResult.Text);
}
var projectCollision = Path.Combine(root, "project-collision");
CopyTree(input, projectCollision);
var existingProject = Path.Combine(projectCollision, "Canary", "Canary.csproj");
File.WriteAllText(existingProject, "<Project><!-- existing project must not be overwritten --></Project>");
var projectCollisionResult = await Convert(projectCollision, projectCollision + "-out");
Require(projectCollisionResult.Code != 0 && projectCollisionResult.Text.Contains("FILE_COLLISION") &&
    !Directory.Exists(projectCollision + "-out.incomplete") && File.ReadAllText(existingProject).Contains("must not be overwritten"),
    "existing C# project destination overwritten");
var sourceCollision = Path.Combine(root, "source-collision");
CopyTree(input, sourceCollision);
File.WriteAllText(Path.Combine(sourceCollision, "Canary", "Collection.cs"), "// destination sentinel");
var sourceCollisionResult = await Convert(sourceCollision, sourceCollision + "-out");
Require(sourceCollisionResult.Code != 0 && sourceCollisionResult.Text.Contains("FILE_COLLISION") &&
    File.ReadAllText(Path.Combine(sourceCollision + "-out.incomplete", "Canary", "Collection.cs")) == "// destination sentinel" &&
    !File.Exists(Path.Combine(sourceCollision + "-out.incomplete", "Canary", "Canary.csproj")) &&
    File.ReadAllText(Path.Combine(sourceCollision + "-out.incomplete", "Canary", "Probe.xaml")) == File.ReadAllText(Path.Combine(sourceCollision, "Canary", "Probe.xaml")),
    "complete destination map was not checked before mutation");
var uncompiled = Path.Combine(root, "uncompiled");
CopyTree(input, uncompiled);
var uncompiledProject = Path.Combine(uncompiled, "Canary", "Canary.vbproj");
File.WriteAllText(uncompiledProject, File.ReadAllText(uncompiledProject).Replace("</Project>", "<ItemGroup><Compile Remove=\"Collection.cs\" /></ItemGroup></Project>"));
var uncompiledResult = await Convert(uncompiled, uncompiled + "-out");
Require(uncompiledResult.Code != 0 && uncompiledResult.Text.Contains("OUTPUT_COMPILE_COVERAGE") &&
    !Directory.Exists(uncompiled + "-out"), "green but incomplete C# assembly was published: " + uncompiledResult.Text);
Console.WriteLine("PASS: baseline/converted behavioral canaries, actual output, repeatability, dry-run, collisions, unsupported input, no-VB.");
Console.WriteLine("Evidence: " + root);
return 0;

async Task<(int Code, string Text)> Convert(string from, string to, params string[] extra) =>
    await Run("dotnet", tool, ["exec", cli, "convert-language", "--input", from, "--output", to, "--restore", .. extra]);

static async Task<(int Code, string Text)> Run(string file, string directory, params string[] args)
{
    var start = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in args) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!;
    var streams = await Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
    await process.WaitForExitAsync();
    return (process.ExitCode, string.Concat(streams));
}
static void CopyTree(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    foreach (var directory in Directory.GetDirectories(source).Where(d => Path.GetFileName(d) is not ("bin" or "obj")))
        CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
}
static void Require(bool success, string message)
{
    if (!success) throw new InvalidOperationException(message);
}

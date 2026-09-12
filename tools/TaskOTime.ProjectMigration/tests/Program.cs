using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using TaskOTime.ProjectMigration;

var workspace = Path.GetFullPath(Path.Combine("artifacts", "regression-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(workspace);
var passed = 0;
try
{
    var original = Path.Combine(workspace, "original");
    Write(original, "Library\\Library.csproj", """
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
          <PropertyGroup>
            <Configuration Condition="'$(Configuration)' == ''">Debug</Configuration>
            <Platform Condition="'$(Platform)' == ''">AnyCPU</Platform>
            <TargetFrameworkVersion>v4.6.1</TargetFrameworkVersion>
            <OutputType>Library</OutputType><AssemblyName>IndependentFixture</AssemblyName>
            <RootNamespace>Independent.Namespace</RootNamespace>
          </PropertyGroup>
          <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|AnyCPU'">
            <OutputPath>bin\Debug\</OutputPath><DefineConstants>DEBUG;TRACE</DefineConstants>
          </PropertyGroup>
          <PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Release|AnyCPU'">
            <OutputPath>bin\Release\</OutputPath><DefineConstants>TRACE</DefineConstants>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="..\Shared.cs"><Link>Linked\Shared.cs</Link></Compile>
            <Compile Include="AssemblyInfo.cs" />
            <Reference Include="System" /><Reference Include="System.Core" />
            <EmbeddedResource Include="payload.txt"><LogicalName>Stable.Resource</LogicalName></EmbeddedResource>
            <None Include="settings.config"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
            <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net461" Version="1.0.3" PrivateAssets="all" />
          </ItemGroup>
          <Target Name="FixtureAfterBuild" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
            <WriteLinesToFile File="$(OutputPath)custom-target.txt" Lines="preserved" Overwrite="true" />
          </Target>
          <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
        </Project>
        """);
    Write(original, "Shared.cs", """
        namespace Independent.Namespace {
            public static class Canary {
                public static string Read() {
                    using (var reader = new System.IO.StreamReader(typeof(Canary).Assembly.GetManifestResourceStream("Stable.Resource")))
                        return reader.ReadToEnd();
                }
            }
        }
        """);
    Write(original, "Library\\AssemblyInfo.cs", """
        [assembly: System.Reflection.AssemblyVersion("2.3.4.5")]
        [assembly: System.Reflection.AssemblyTitle("Stable identity")]
        """);
    Write(original, "Library\\payload.txt", "stable payload");
    Write(original, "Library\\settings.config", "<configuration />");
    Write(original, ".git", "gitdir: must-not-be-exported");
    Write(original, "RemainingTests\\RemainingTests.vbproj", """
        <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
          <TargetFramework Condition="'$(Configuration)' == 'Debug'">net461</TargetFramework>
          <TargetFramework Condition="'$(Configuration)' == 'Release'">net461</TargetFramework>
          <RootNamespace>Still.VisualBasic</RootNamespace><OptionStrict>On</OptionStrict>
        </PropertyGroup><ItemGroup>
          <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net461"><Version>1.0.3</Version><PrivateAssets>all</PrivateAssets></PackageReference>
        </ItemGroup></Project>
        """);
    Write(original, "tools\\PrivateTool\\PrivateTool.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
    var originalBytes = File.ReadAllBytes(Path.Combine(original, "Library\\Library.csproj"));

    Test("inspect evaluates both configurations and finds classic/linked semantics", () =>
    {
        var result = Run(0, "inspect", "--source", original);
        Check(result.RootElement.GetProperty("Projects").GetArrayLength() == 2, "tools must be excluded");
        Check(result.RootElement.GetProperty("Projects")[0].GetProperty("Evaluations").GetArrayLength() == 2, "configuration matrix");
        Check(result.RootElement.GetProperty("Diagnostics").EnumerateArray().Any(d => d.GetProperty("Code").GetString() == "linked-source"), "linked source diagnostic");
    });
    var normalized = Path.Combine(workspace, "normalized");
    Test("dry-run is read-only and includes proposed XML", () =>
    {
        var result = Run(0, "normalize-framework", "--source", original, "--output", normalized, "--target", "net472", "--dryrun");
        Check(!Directory.Exists(normalized), "dry run wrote output");
        Check(result.RootElement.GetProperty("ChangedFiles").GetArrayLength() == 2, "C# and remaining VB normalized");
        Check(result.RootElement.GetProperty("ChangedFiles")[0].GetProperty("ProposedXml").GetString()!.Contains("net472"), "proposed diff");
        Check(originalBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(original, "Library\\Library.csproj"))), "source changed");
    });
    Test("normalization is deterministic and idempotent; tools stay net10", () =>
    {
        Run(0, "normalize-framework", "--source", original, "--output", normalized, "--target", "net472");
        var replay = Path.Combine(workspace, "normalized-replay");
        Run(0, "normalize-framework", "--source", original, "--output", replay, "--target", "net472");
        Check(File.ReadAllBytes(Path.Combine(normalized, "migration-manifest.json")).SequenceEqual(
            File.ReadAllBytes(Path.Combine(replay, "migration-manifest.json"))), "manifests not deterministic");
        var again = Run(0, "normalize-framework", "--source", normalized, "--output", Path.Combine(workspace, "normalized-again"), "--target", "net472");
        Check(again.RootElement.GetProperty("ChangedFiles").GetArrayLength() == 0, "normalization not idempotent");
        Check(File.ReadAllText(Path.Combine(normalized, "tools\\PrivateTool\\PrivateTool.csproj")).Contains("net10.0"), "tool retargeted");
        Check(!File.Exists(Path.Combine(normalized, ".git")), "Git worktree pointer exported");
    });
    var converted = Path.Combine(workspace, "sdk");
    Test("SDK conversion preserves targets/conditions/links/resources and target framework", () =>
    {
        Run(0, "convert-projects", "--sdk-style", "--source", normalized, "--output", converted);
        var xml = XDocument.Load(Path.Combine(converted, "Library\\Library.csproj"));
        Check(xml.Root!.Attribute("ToolsVersion") == null, "legacy ToolsVersion retained");
        Check(xml.Descendants("TargetFramework").Single().Value == "net472", "SDK conversion retargeted project");
        var sourceXml = XDocument.Load(Path.Combine(normalized, "Library\\Library.csproj"));
        var sourceTarget = sourceXml.Descendants().Single(e => e.Name.LocalName == "Target");
        foreach (var element in sourceTarget.DescendantsAndSelf()) element.Name = element.Name.LocalName;
        Check(XNode.DeepEquals(sourceTarget, xml.Descendants("Target").Single()), "custom target changed");
        Check(xml.Descendants("Link").Single().Value == "Linked\\Shared.cs", "linked metadata changed");
        Check(xml.Descendants("LogicalName").Single().Value == "Stable.Resource", "resource identity changed");
        var again = Run(0, "convert-projects", "--sdk-style", "--source", converted, "--output", Path.Combine(workspace, "sdk-again"));
        Check(again.RootElement.GetProperty("ChangedFiles").GetArrayLength() == 0, "SDK conversion not idempotent");
    });
    Test("emitted SDK net472 builds, custom target executes, assembly/resource behavior survives", () =>
    {
        Build(Path.Combine(converted, "Library\\Library.csproj"));
        var assembly = Assembly.LoadFile(Path.Combine(converted, "Library\\bin\\Debug\\IndependentFixture.dll"));
        Check(assembly.GetName().Version!.ToString() == "2.3.4.5", "assembly identity changed");
        var read = assembly.GetType("Independent.Namespace.Canary")!.GetMethod("Read")!;
        Check((string)read.Invoke(null, null)! == "stable payload", "resource behavior changed");
        Check(File.Exists(Path.Combine(converted, "Library\\bin\\Debug\\custom-target.txt")), "custom target did not execute");
        Check(File.Exists(Path.Combine(converted, "Library\\bin\\Debug\\settings.config")), "None copy semantics lost");
    });
    Test("retarget emits buildable net10 and is idempotent", () =>
    {
        var modern = Path.Combine(workspace, "modern");
        Run(0, "retarget", "--framework", "net10.0", "--wpf-framework", "net10.0-windows",
            "--source", converted, "--output", modern);
        Build(Path.Combine(modern, "Library\\Library.csproj"));
        Build(Path.Combine(modern, "RemainingTests\\RemainingTests.vbproj"));
        var again = Run(0, "retarget", "--framework", "net10.0", "--wpf-framework", "net10.0-windows",
            "--source", modern, "--output", Path.Combine(workspace, "modern-again"));
        Check(again.RootElement.GetProperty("ChangedFiles").GetArrayLength() == 0, "retarget not idempotent");
    });
    Test("WPF and transitive test consumers target net10.0-windows", () =>
    {
        var desktop = Path.Combine(workspace, "desktop");
        Write(desktop, "Views\\Views.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework><UseWPF>true</UseWPF></PropertyGroup></Project>
            """);
        Write(desktop, "Tests\\Tests.vbproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup>
            <ItemGroup><ProjectReference Include="..\Views\Views.csproj" /></ItemGroup></Project>
            """);
        var output = Path.Combine(workspace, "desktop-modern");
        Run(0, "retarget", "--framework", "net10.0", "--wpf-framework", "net10.0-windows", "--source", desktop, "--output", output);
        foreach (var file in Directory.GetFiles(output, "*.*proj", SearchOption.AllDirectories))
            Check(XDocument.Load(file).Descendants("TargetFramework").Single().Value == "net10.0-windows", "Windows target not propagated");
        Build(Path.Combine(output, "Tests\\Tests.vbproj"));
    });
    Test("unsupported serializer/EDMX/multitargeting/unknown imports fail without output", () =>
    {
        var unsafeSource = Path.Combine(workspace, "unsupported");
        Write(unsafeSource, "Unsupported.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup>
            <ItemGroup><Reference Include="System.Web.Extensions"/><EntityDeploy Include="Model.edmx"/></ItemGroup></Project>
            """);
        Write(unsafeSource, "Source.cs", "// JavaScriptSerializer compatibility seam");
        var destination = Path.Combine(workspace, "unsupported-output");
        var result = Run(2, "retarget", "--framework", "net10.0", "--wpf-framework", "net10.0-windows", "--source", unsafeSource, "--output", destination);
        Check(!Directory.Exists(destination), "unsupported input published output");
        Check(result.RootElement.GetProperty("Diagnostics").EnumerateArray().Any(d => d.GetProperty("Code").GetString() == "retarget-blocked-system-web"), "missing actionable blocker");
        Write(unsafeSource, "Unsupported.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net472;net10.0</TargetFrameworks></PropertyGroup></Project>""");
        Run(2, "normalize-framework", "--target", "net472", "--source", unsafeSource, "--output", destination);
        Write(unsafeSource, "Unsupported.csproj", """
            <Project><PropertyGroup><TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion></PropertyGroup>
            <Import Project="Custom.targets" /><Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" /></Project>
            """);
        Write(unsafeSource, "Custom.targets", "<Project />");
        Run(2, "convert-projects", "--sdk-style", "--source", unsafeSource, "--output", destination);
        Check(!Directory.Exists(destination), "unknown import published output");
    });
    Test("source/output collisions, existing destinations, invalid options and outside links fail closed", () =>
    {
        Run(2, "normalize-framework", "--target", "net472", "--source", original, "--output", original);
        Run(2, "normalize-framework", "--target", "net472", "--source", original, "--output", Path.Combine(original, "child"));
        Run(2, "normalize-framework", "--target", "net472", "--source", original, "--output", workspace);
        Run(2, "normalize-framework", "--target", "net472", "--source", original, "--output", normalized);
        Run(2, "normalize-framework", "--target", "net472", "--source", original, "--output", Path.Combine(workspace, "invalid"), "--typo");
        Run(2, "inspect", "--source", Path.Combine(original, "Library"));
    });
    Test("inherited/computed frameworks are rejected and failed output validation rolls back", () =>
    {
        var inherited = Path.Combine(workspace, "inherited");
        Write(inherited, "Inherited.csproj", """<Project Sdk="Microsoft.NET.Sdk" />""");
        Write(inherited, "Directory.Build.props", """<Project><PropertyGroup><TargetFramework>net461</TargetFramework></PropertyGroup></Project>""");
        var output = Path.Combine(workspace, "inherited-output");
        Run(2, "normalize-framework", "--target", "net472", "--source", inherited, "--output", output);
        Write(inherited, "Inherited.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>$(SelectedTarget)</TargetFramework><SelectedTarget>net461</SelectedTarget></PropertyGroup></Project>""");
        Run(2, "normalize-framework", "--target", "net472", "--source", inherited, "--output", output);
        Write(inherited, "Inherited.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net461</TargetFramework></PropertyGroup></Project>""");
        Write(inherited, "Directory.Build.targets", """<Project><PropertyGroup><TargetFramework>net461</TargetFramework></PropertyGroup></Project>""");
        var result = Run(2, "normalize-framework", "--target", "net472", "--source", inherited, "--output", output);
        Check(result.RootElement.GetProperty("Diagnostics").EnumerateArray().Any(d => d.GetProperty("Code").GetString() == "output-framework-mismatch"), "missing output mismatch diagnostic");
        Check(!Directory.Exists(output), "failed verification published output");
        Check(!Directory.GetDirectories(workspace, "*.projectmigration-*").Any(), "failed verification left staging output");
    });
    Test("framework-conditioned dependencies and metadata cannot silently disappear", () =>
    {
        foreach (var kind in new[] { "Reference", "PackageReference" })
            foreach (var metadataOnly in new[] { false, true })
            {
                var source = Path.Combine(workspace, $"conditioned-{kind}-{metadataOnly}");
                var output = source + "-out";
                var item = new XElement(kind, new XAttribute("Include", "Independent.Dependency"));
                if (kind == "PackageReference") item.Add(new XAttribute("Version", "1.2.3"));
                var condition = new XAttribute("Condition", "'$(TargetFramework)' == 'net461'");
                if (metadataOnly) item.Add(new XElement("IndependentCustomMetadata", condition, "must-survive"));
                else item.Add(condition);
                var xml = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup", new XElement("TargetFramework", "net461")),
                    new XElement("ItemGroup", item));
                Write(source, "Conditioned.csproj", xml.ToString());
                var result = Run(2, "normalize-framework", "--target", "net472", "--source", source, "--output", output);
                Check(result.RootElement.GetProperty("Diagnostics").EnumerateArray().Any(d =>
                    d.GetProperty("Code").GetString() == "output-dependency-mismatch" &&
                    d.GetProperty("Message").GetString()!.Contains(kind)), $"missing {kind} mismatch diagnostic");
                Check(!Directory.Exists(output), "conditioned dependency loss published output");
            }
    });
    Test("explicit desktop assembly references establish SDK flags and compile after retargeting", () =>
    {
        foreach (var wpf in new[] { true, false })
        {
            var source = Path.Combine(workspace, wpf ? "explicit-wpf" : "explicit-forms");
            var output = source + "-modern";
            var assembly = wpf ? "PresentationFramework" : "System.Windows.Forms";
            var flag = wpf ? "UseWPF" : "UseWindowsForms";
            Write(source, "Desktop.csproj", $"""
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup>
                <ItemGroup><Reference Include="{assembly}" /></ItemGroup></Project>
                """);
            Write(source, "Desktop.cs", wpf
                ? "public class DesktopCanary : System.Windows.Window { public System.Windows.Controls.Button Value = new System.Windows.Controls.Button(); }"
                : "public class DesktopCanary : System.Windows.Forms.Form { public System.Windows.Forms.Button Value = new System.Windows.Forms.Button(); }");
            Run(0, "retarget", "--framework", "net10.0", "--wpf-framework", "net10.0-windows", "--source", source, "--output", output);
            var xml = XDocument.Load(Path.Combine(output, "Desktop.csproj"));
            Check(xml.Descendants(flag).Single().Value == "true", $"{flag} not established");
            Check(xml.Descendants("TargetFramework").Single().Value == "net10.0-windows", "desktop framework not selected");
            Check(!xml.Descendants("Reference").Any(), "obsolete desktop assembly reference retained");
            Build(Path.Combine(output, "Desktop.csproj"));
            var again = Run(0, "retarget", "--framework", "net10.0", "--wpf-framework", "net10.0-windows", "--source", output, "--output", output + "-again");
            Check(again.RootElement.GetProperty("ChangedFiles").GetArrayLength() == 0, "desktop retarget not idempotent");
            var originalXml = XDocument.Load(Path.Combine(source, "Desktop.csproj"));
            originalXml.Root!.Element("PropertyGroup")!.Add(new XElement(flag, "false"));
            Write(source, "Desktop.csproj", originalXml.ToString());
            Run(2, "retarget", "--framework", "net10.0", "--wpf-framework", "net10.0-windows", "--source", source, "--output", output + "-conflict");
        }
    });
    Test("restored MSTest adapter assets remain package-provided across normalization", () =>
    {
        var source = Path.Combine(workspace, "restored-mstest");
        var output = source + "-normalized";
        Write(source, "RestoredTests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net461</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net461" Version="1.0.3" PrivateAssets="all" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.2.0" />
                <PackageReference Include="MSTest.TestAdapter" Version="2.2.10" />
                <PackageReference Include="MSTest.TestFramework" Version="2.2.10" />
                <ProjectReference Include="Nested\Nested.vbproj" />
                <Reference Include="Microsoft.VisualStudio.TestPlatform.TestFramework">
                  <HintPath>$(NuGetPackageRoot)mstest.testframework\2.2.10\lib\net45\Microsoft.VisualStudio.TestPlatform.TestFramework.dll</HintPath>
                  <Private>True</Private>
                </Reference>
              </ItemGroup>
            </Project>
            """);
        Write(source, "Nested\\Nested.vbproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net461</TargetFramework></PropertyGroup>
            <ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net461" Version="1.0.3" PrivateAssets="all" /></ItemGroup></Project>
            """);
        Write(source, "Nested\\Canary.vb", "Public Class NestedCanary\nEnd Class");
        Write(source, "Canary.cs", """
            using Microsoft.VisualStudio.TestTools.UnitTesting;
            [TestClass] public class IndependentCanary {
                [TestMethod] public void Preserved() { Assert.AreEqual(4, 2 + 2); }
            }
            """);
        Build(Path.Combine(source, "RestoredTests.csproj"));
        var assets = Path.Combine(source, "obj\\project.assets.json");
        var assetsBefore = File.ReadAllBytes(assets);
        var result = Run(0, "normalize-framework", "--target", "net472", "--source", source, "--output", output);
        var packageItems = result.RootElement.GetProperty("Projects").EnumerateArray()
            .Single(p => p.GetProperty("Path").GetString() == "RestoredTests.csproj").GetProperty("Evaluations")[0]
            .GetProperty("Items").GetProperty("None").EnumerateArray().Where(i =>
                i.GetProperty("DefiningProjectFullPath").GetString()!.StartsWith("{nuget}\\mstest.testadapter\\", StringComparison.OrdinalIgnoreCase)).ToArray();
        Check(packageItems.Length >= 4, "fixture did not reproduce imported MSTest adapter None assets");
        Check(assetsBefore.SequenceEqual(File.ReadAllBytes(assets)), "source restore state changed");
        Check(Directory.GetFiles(output, "*.dll", SearchOption.AllDirectories).Length == 0, "package binaries copied into emitted source");
        Build(Path.Combine(output, "RestoredTests.csproj"));
        Check(Directory.GetFiles(Path.Combine(output, "bin"), "Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.dll", SearchOption.AllDirectories).Length > 0,
            "normal restore/build did not deploy the test adapter");
        var again = Run(0, "normalize-framework", "--target", "net472", "--source", output, "--output", output + "-again");
        Check(again.RootElement.GetProperty("ChangedFiles").GetArrayLength() == 0, "restored normalization not idempotent");
        var xml = XDocument.Load(Path.Combine(source, "RestoredTests.csproj"));
        xml.Root!.Add(new XElement("ItemGroup", new XElement("None",
            new XAttribute("Include", "$(NuGetPackageRoot)mstest.testadapter\\2.2.10\\build\\_common\\Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.dll"),
            new XElement("Link", "ExplicitExternal.dll"))));
        Write(source, "RestoredTests.csproj", xml.ToString());
        var rejected = Run(2, "normalize-framework", "--target", "net472", "--source", source, "--output", source + "-explicit-external");
        Check(rejected.RootElement.GetProperty("Diagnostics").EnumerateArray().Any(d => d.GetProperty("Code").GetString() == "external-item"),
            "explicit package-cache link was incorrectly treated as package-provided");
        xml.Root!.Elements("ItemGroup").Last().Remove();
        xml.Descendants("PackageReference").Single(e => (string?)e.Attribute("Include") == "MSTest.TestAdapter")
            .Add(new XAttribute("Condition", "'$(TargetFramework)' == 'net461'"));
        Write(source, "RestoredTests.csproj", xml.ToString());
        var lostAdapter = Run(2, "normalize-framework", "--target", "net472", "--source", source, "--output", source + "-lost-adapter");
        Check(lostAdapter.RootElement.GetProperty("Diagnostics").EnumerateArray().Any(d =>
            d.GetProperty("Code").GetString() == "output-dependency-mismatch" &&
            d.GetProperty("Message").GetString()!.Contains("PackageReference")),
            "package-asset exemption hid loss of the conditioned adapter dependency");
    });
    Console.WriteLine($"PASS: {passed} regression scenarios; artifacts: {workspace}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Console.Error.WriteLine($"Failure evidence retained: {workspace}");
    return 1;
}

void Test(string name, Action test)
{
    test();
    passed++;
    Console.WriteLine("PASS: " + name);
}

static JsonDocument Run(int expectedExit, params string[] arguments)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    var exit = Cli.Run(arguments, output, error);
    if (exit != expectedExit)
    {
        var details = output.ToString();
        if (details.Length > 0)
        {
            using var json = JsonDocument.Parse(details);
            if (json.RootElement.TryGetProperty("Diagnostics", out var diagnostic)) details = diagnostic.ToString();
        }
        throw new InvalidOperationException($"Expected exit {expectedExit}, got {exit}: {string.Join(' ', arguments)}\n{error}\n{details}");
    }
    return JsonDocument.Parse(output.ToString().Length > 0 ? output.ToString() : error.ToString());
}

static void Write(string root, string relative, string contents)
{
    var path = Path.Combine(root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, contents);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Build(string project)
{
    var start = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        WorkingDirectory = AppContext.BaseDirectory
    };
    foreach (var argument in new[] { "build", project, "--nologo", "--verbosity", "quiet" }) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(180_000))
    {
        process.Kill(entireProcessTree: true);
        throw new InvalidOperationException("Fixture build timed out.");
    }
    Task.WaitAll(stdout, stderr);
    Check(process.ExitCode == 0, $"Fixture build failed:\n{stdout.Result}\n{stderr.Result}");
}

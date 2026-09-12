using System.Diagnostics;
using ExternalEvaluation;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "ReplayUnit")]
public sealed class ProtectedCompilerTests
{
    [Theory]
    [InlineData("source")]
    [InlineData("generated")]
    [InlineData("resource")]
    [InlineData("compiler")]
    [InlineData("lifecycle")]
    [InlineData("extra-input")]
    [InlineData("output-escape")]
    [InlineData("plugin")]
    [InlineData("response")]
    [InlineData("unbound-output")]
    [InlineData("input-output-collision")]
    public async Task Captured_compiler_plan_rejects_changed_inputs_or_execution_surface(string mutation)
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "protected-compiler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var script = "$root='" + root.Replace("'", "''") + "'; " + """
                $buildRoot=Join-Path $root 'build'
                $authored=Join-Path $buildRoot 'payload\source'
                $export=Join-Path $root 'export'
                $capture=Join-Path $root 'capture'
                New-Item -ItemType Directory -Path $authored,(Join-Path $export 'obj') -Force | Out-Null
                [IO.File]::WriteAllText((Join-Path $authored 'Program.cs'),'public class Program {}')
                [IO.File]::WriteAllText((Join-Path $authored 'Template.targets'),'<Project/>')
                Copy-Item (Join-Path $authored '*') $export
                [IO.File]::WriteAllText((Join-Path $export 'obj\generated.cs'),'public class Generated {}')
                $args=@('/noconfig','/target:exe','/deterministic+','/out:obj\Debug\net10.0\Unit.dll',
                    'Program.cs','obj\generated.cs','/resource:Template.targets,Unit.Template')
                $metadata=@{ Items=@{CscCommandLineArgs=@($args | ForEach-Object {@{Identity=$_}});VbcCommandLineArgs=@()} }
                # Synthetic metadata is unit-test input only; no VM, build or receipt is asserted.
                $build=[pscustomobject]@{
                    HostProducerObservation=@{Export=$export};Artifacts=$buildRoot;FormalVerified=$false
                    SdkVersion='10.0.401';SandboxStopped=$true;SandboxProcessIds=@(1,2)
                    Producer=@{Projects=@(@{Project='Unit.csproj';ExitCode=0;StandardOutput=($metadata | ConvertTo-Json -Depth 8)})}
                }
                $sdk=Join-Path $env:ProgramFiles 'dotnet'
                $plan=New-CompilerPlan $build 'Unit.csproj' $sdk '' $capture
                $null=Assert-CompilerPlan $capture $sdk
                switch('__MUTATION__') {
                    'source' { Add-Content -LiteralPath (Join-Path $capture 'files\producer\Program.cs') 'changed' }
                    'generated' { Add-Content -LiteralPath (Join-Path $capture 'files\producer\obj\generated.cs') 'changed' }
                    'resource' { Add-Content -LiteralPath (Join-Path $capture 'files\producer\Template.targets') 'changed' }
                    'compiler' { $plan.CompilerSha256='0'*64 }
                    'lifecycle' { $plan.BuildSandboxStopped=$false }
                    'extra-input' { $plan.Inputs['C:\ProbeWork\restricted\producer\extra.cs']=@{Kind='source';Sha256=('0'*64)} }
                    'output-escape' {
                        $plan.Arguments[3]='/out:C:\Outside\Unit.dll'
                        $plan.Outputs=@{'C:\Outside\Unit.dll'='out'}
                    }
                    'plugin' { $plan.Arguments+=@('/analyzer:payload.dll') }
                    'response' { $plan.Arguments+=@('@payload.rsp') }
                    'unbound-output' { $plan.Outputs=@{} }
                    'input-output-collision' {
                        $guest='C:\ProbeWork\restricted\producer\obj\Debug\net10.0\Unit.dll'
                        $file=Join-Path $capture 'files\producer\obj\Debug\net10.0\Unit.dll'
                        New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force | Out-Null
                        [IO.File]::WriteAllText($file,'owned collision fixture')
                        $plan.Inputs[$guest]=@{Kind='resource';Sha256=(Get-CompilerFileHash $file)}
                        $plan.Arguments+=@('/resource:'+$guest+',Duplicate')
                    }
                }
                $plan | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $capture 'job.json') -Encoding UTF8
                try { $null=Assert-CompilerPlan $capture $sdk; throw 'UNEXPECTED_ACCEPTANCE' }
                catch {
                    if($_.Exception.Message -eq 'UNEXPECTED_ACCEPTANCE') { throw }
                    Write-Output 'rejected-mutation'
                }
                """.Replace("__MUTATION__", mutation);
            Assert.Equal("rejected-mutation", (await Run(script)).Trim());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("/analyzer:payload.dll")]
    [InlineData("@escape.rsp")]
    [InlineData("/pathmap:C:\\=D:\\")]
    [InlineData("/sourcelink:payload.json")]
    [InlineData("/keyfile:key.snk")]
    [InlineData("/win32res:payload.res")]
    [InlineData("/addmodule:payload.netmodule")]
    [InlineData("/reference:alias=payload.dll")]
    [InlineData("/out:obj\\replacement.dll")]
    [InlineData("/target:library")]
    [InlineData("Program.cs\n/analyzer:payload.dll")]
    public async Task Compiler_feature_allowlist_rejects_plugins_escapes_and_unsupported_outputs(string argument)
    {
        var escaped = argument.Replace("'", "''");
        var script = """
            $base='C:\ProbeWork\restricted\producer'
            $args=@('/noconfig','/target:exe','/deterministic+','/out:obj\Debug\net10.0\Owned.dll','Program.cs')
            $args+=('__ARGUMENT__')
            try {
                $null=ConvertTo-CompilerArguments $args $base {param($path,$kind)
                    if($path -match '=' -or -not $path.StartsWith($base+'\')) { throw 'Unapproved input' }
                } {param($path,$kind)}
                throw 'UNEXPECTED_ACCEPTANCE'
            } catch {
                if($_.Exception.Message -eq 'UNEXPECTED_ACCEPTANCE') { throw }
                Write-Output 'rejected'
            }
            """.Replace("__ARGUMENT__", escaped);
        Assert.Equal("rejected", (await Run(script)).Trim());
    }

    [Fact]
    public async Task Explicit_sources_resources_and_portable_deterministic_options_are_retained()
    {
        var output = await Run("""
            $base='C:\ProbeWork\restricted\producer'
            $args=@('/noconfig','/target:exe','/deterministic+','/out:obj\Debug\net10.0\Owned.dll',
                '/debug:portable','/resource:Templates\Metadata.targets,Owned.Metadata.targets',
                '/define:TRACE;DEBUG','Program.cs','obj\Owned.AssemblyInfo.cs')
            $actual=ConvertTo-CompilerArguments $args $base {param($path,$kind)} {param($path,$kind)}
            if($actual.Count -ne $args.Count -or
                $actual -notcontains ($base+'\Program.cs') -or
                $actual -notcontains ('/resource:'+$base+'\Templates\Metadata.targets,Owned.Metadata.targets') -or
                $actual -notcontains '/deterministic+') { throw 'Changed compiler semantics' }
            Write-Output 'accepted-explicit-data'
            """);
        Assert.Equal("accepted-explicit-data", output.Trim());
    }

    [Fact]
    public async Task All_host_and_guest_scripts_parse_without_execution()
    {
        Assert.Equal("parsed", (await Run("""
            Get-ChildItem -LiteralPath $sandbox -Filter '*.ps1' | ForEach-Object {
                $tokens=$null; $errors=$null
                $null=[Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$tokens,[ref]$errors)
                if($errors) { throw ($errors | Out-String) }
            }

            Write-Output 'parsed'
            """)).Trim());
    }

    internal static async Task<string> Run(string script)
    {
        var sandbox = Path.Combine(EvaluatorConfiguration.AssessmentRoot, "Sandbox");
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command" })
            start.ArgumentList.Add(arg);
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; $sandbox='" + sandbox.Replace("'", "''") +
            "'; . (Join-Path $sandbox 'CompilerPlan.ps1'); " + script);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await Task.WhenAll(process.WaitForExitAsync(), output, error).WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException) { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}

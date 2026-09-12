# Windows Sandbox diagnostics and bounded protected compilation

This implementation uses the **existing enabled Windows Sandbox feature**.
It does not enable/install Windows features, create Windows users, install a
container engine, or import a signing key into the guest.

`Invoke-SandboxProbe.ps1` supports an owned capability probe, source producer
builds, and actual CLI execution. Despite the historical probe filename, producer
and CLI jobs are real restricted guest processes. Host-side checks do not execute
returned binaries or evaluate submitted MSBuild projects.

**Signing is disabled, including the narrower producer-only proof.** The former
post-build `/out:`/`TargetPath` comparison checks two submitted-build-controlled
files. A target can replace both with a tracked payload. It cannot prove compiler
production, even with unchanged source and `SkipCompilerExecution=false`.
`-SignProducerProof` now fails before feature inspection, staging or VM startup.

## Protected second compilation: supported console producers

`Invoke-ProtectedProducer.ps1` adds a **different**, bounded route. It does not
re-enable the old signer or reference-reviewed acceptance:

```powershell
$producer = & .\assessment\Sandbox\Invoke-ProtectedProducer.ps1 `
  -SourceRoot 'C:\private\complete-console-producer-source' `
  -Project 'Producer.csproj' -TimeoutSeconds 240
$producer.ProtectedAssembly
$producer.Evidence
```

For a locked `net10.0` package closure, first use
`Copy-LockedPublicPackages.ps1 -LockFile <packages.lock.json> -Destination <new-private-cache> -PackageCache <existing-public-cache>`.
The pinned SDK's NuGet archive reader verifies each canonical content hash against
the lock, then extracts that verified archive. It does not trust potentially
modified loose cache DLLs. NuGet's canonical content hash can differ from the raw
signed ZIP hash; both are recorded. Only the exact 36 locked packages were exposed
for the actual language producer, not the general user cache. Supply this curated
directory as `-PublicPackageRoot` to the producer command.

The sequence is:

1. Run submitted restore/MSBuild only in the restricted first VM. Capture
   command-line metadata, authored source and generated compiler data, including
   `obj\protected-generated` source-generator output. Export referenced package
   bytes for independent host comparison with the approved package snapshot.
2. Observe the **actual VM lifecycle**, not just `WindowsSandbox.exe` exiting.
   Modern Sandbox launches `WindowsSandboxRemoteSession` and
   `WindowsSandboxServer`; the host requires a unique session and waits for all
   observed processes to terminate. Existing sessions cause refusal. A bootstrap
   timeout may terminate only the launcher's own PID and a remote-session PID
   whose command line names this exact unique `.wsb`. Server processes are never
   killed by name. A second-VM bootstrap failure permits one bounded fresh retry.
3. `CompilerPlan.ps1` validates and hashes every selected source, generated file,
   managed resource, SDK reference/configuration and package reference. Authored
   source must match the staged original. Generated inputs are explicitly labelled
   and hash-bound, not falsely presented as authored files. Package metadata must
   match the approved bytes. Compiler implementation files are also hash-bound.
4. Start a **fresh VM**, with only the selected data inputs, trusted SDK and
   optional selected package references. Do not run submitted targets or tools
   there. A trusted supervisor invokes SDK **10.0.401 `csc.dll` directly**, with a
   response file it constructs from the validated argument array. No submitted
   response file, analyzer, generator or hook is loaded. Removed build-plugin
   arguments remain in the host plan as observations. Missing generated semantics
   must cause compilation or byte comparison to fail, not a fallback to build DLLs.
5. Run two separate restricted compiler processes; terminate descendants before
   collection. Check compiler/input hashes before and after both executions.
   Compare the complete PE/PDB/reference-image outputs independently on the host.
   Fixed guest source paths preserve compiler path identity across the two VMs;
   arbitrary `/pathmap` is currently rejected rather than guessed.
6. Compare the independently produced main assembly with the claimed build target.
   A difference throws and preserves `protected-producer.json` as **rejection
   evidence**. Only the protected assembly is returned on success.

Current support is deliberately **C# `net10.0` console executables**, explicit
sources, managed embedded resources, standard SDK references and hash-matched
approved package references. VB, library/Windows executable targets, project/local
assembly dependencies requiring separate protected compilations, Framework/WPF
reference surfaces, arbitrary compiler features, source-link files, signing/native
resource options and response-file escapes are rejected.

This establishes **compilation of the exact captured input set**, not that
submitted metadata truthfully describes the earlier compiler invocation or that
the selected/generated source graph is the intended transformation producer.
The original source snapshot, selected input hashes and generated-input labels
are retained for that independent assessment. It is not complete tool replay or
application assessment, and no `tool-source-build` full-policy attestation is
issued merely because this bounded comparison passed.

### Actual protected-compiler verification

Private evidence below was produced by real SDK/VM executions, not synthetic
receipt files:

| Case | Compiler VM artifact directory | Outcome |
| --- | --- | --- |
| Actual `TaskOTime.ProjectMigration` 1.1.2 | `sandbox-probe-afe89c9a842247fbaa5b30ca66c7f9c9` | Independent PE matches build; repeated PE/PDB/reference-image hashes match |
| Actual `TaskOTime.Migration` 1.0.1 | `sandbox-probe-6b7098508bf3411c819aa45501ad9c8b` | Same, with 36 approved package archives and 206 bound compiler inputs |
| Owned double-overwrite fixture | `sandbox-probe-73f2ff0eb78e4f2baf4678a460a5f861` | Rejected: tracked payload replaced both build outputs, but differs from protected `csc` output |

These directories are beneath private `assessment\Artifacts`; each contains
`protected-producer.json`, the compiler input plan, authenticated guest result
and actual exported artifacts (the plan is in the linked first-build directory).
The two successful producer PE SHA256 values are respectively
`3CD93D7D80DE3D7C9636F72E29A674492AA4B4F445755B75FEF09AD31FDCA750` and
`9D9AE1C986B5AB30F19CE99E8027DCD74B2C2843D04F4806B4F44DF7C8E9CDC6`.
An earlier independent project compiler VM (`sandbox-probe-37743bea677b4ab18028fcce594aa093`)
also produced the first PE hash. The final runs additionally verified modern
Sandbox lifecycle termination. One first-attempt compiler VM failed to bootstrap
(`sandbox-probe-ccdff7cf4c4d43118e31813e755ba358`); it has no acceptance evidence.

Targeted validation:

```powershell
dotnet test .\assessment\Modernization.Analyzers.Tests\Modernization.Analyzers.Tests.csproj `
  --no-restore --filter 'Category=ReplayUnit|FullyQualifiedName~SandboxContractTests'
```

The compiler packet adds 24 tests for allowed compiler data, unsupported execution
features, changed authored/generated/resource inputs, compiler identity, lifecycle
requirements, unbound files and input/output collisions. Full isolated migration
cases and frozen-stage checkpoint reconciliation remain separate, incomplete
work. In particular, producer compilation must not be mistaken for output
behavior/compilation acceptance. `FormalVerified` remains false and no signing
key or formal receipt is produced by this route.

### Failure cleanup and bounded lifecycle regression

All work after launching Sandbox is enclosed in one `try`/`finally`.
`SandboxLifecycle.ps1` handles bootstrap errors, malformed handshakes, result
timeouts, export errors and lifecycle-wait timeouts through the same failure
cleanup path. It re-queries the exact `.wsb` command line and, after bootstrap,
intersects those clients with the captured session identities. It stops only
those client PIDs (and the owned launcher if needed), **never a server by name**,
then requires all observed processes to exit within a shared 60-second cleanup
budget. Missing server observation after bootstrap or a server that remains
alive fails closed. `lifecycle-cleanup.json`, outside the guest-writable export,
records the original failure and independently observed cleanup outcome.

`TimeoutSeconds` bounds bootstrap and the post-bootstrap result wait separately.
Normal lifecycle observation and failure cleanup each have a shared 60-second
budget; no loop retries indefinitely.

The owned-only negative switch cannot be combined with submitted work:

```powershell
# Expected failure after a 60-second post-bootstrap result timeout.
# The trusted guest deliberately remains active for 300 seconds unless closed.
& .\assessment\Sandbox\Invoke-SandboxProbe.ps1 `
  -OwnedControllerDelaySeconds 300 -TimeoutSeconds 60

# A fresh ordinary probe must subsequently run without disturbing another session.
& .\assessment\Sandbox\Invoke-SandboxProbe.ps1 -TimeoutSeconds 180
```

Actual negative `sandbox-probe-2317faa874864955894382142157dbe4` bootstrapped,
acknowledged the host, and wrote `owned-controller-active.txt` before the timeout.
Configuration-bound cleanup terminated its observed VM processes; the whole
negative took **88.58 seconds**, well before its 300-second guest delay could
complete naturally. Fresh retry `sandbox-probe-05ca0d31608441b6bcd92c193d516c4b`
then passed the real SDK/restricted-process canaries and shut down.
`Artifacts\lifecycle-timeout-and-retry.json` retains both outcomes. Neither is a
formal replay receipt. Unit regressions also cover an unrelated client, a
configuration mismatch, a server that does not exit, and the outer-finally/wait
loop structure.

## Protected runtime packaging and isolated case observations

`Publish-ProtectedRuntime.ps1` packages the independently compiled main DLL,
not a submitted apphost or replacement build DLL. Every dependency DLL must
match a file inside an approved hash-bound package archive; loose cache bytes
are checked again against that archive. The managed dependency graph is bounded,
rejects escaping paths and unprotected project dependencies, and must name the
approved package versions/hashes. Runtime/SDK configuration is generated by the
host for .NET 10.0.12 / SDK 10.0.401. Unknown sidecars are rejected.

```powershell
$runtime = & .\assessment\Sandbox\Publish-ProtectedRuntime.ps1 `
  -ProducerEvidence $producer.Evidence -Destination 'C:\private\new-runtime'
$runtime | ConvertTo-Json -Depth 12 | Set-Content 'C:\private\runtime.json'

# Package-bearing tools additionally require -PublicPackageRoot and the
# -PackageBindings JSON returned by Copy-LockedPublicPackages.ps1.
& .\assessment\Sandbox\Invoke-IsolatedToolCase.ps1 `
  -Name 'project-heldout' -RuntimeManifest 'C:\private\runtime.json' `
  -InputRoot 'C:\private\input' -ExpectedOutputRoot 'C:\private\expected' `
  -CommandArguments 'normalize-framework','--target','net472','--source','{input}','--output','{output}' `
  -EvidenceFile 'migration-manifest.json' -EvidenceStatusProperty Verification `
  -EvidenceSuccessValue output-evaluated -Idempotent
```

Each initial/repeat/idempotent execution uses a fresh VM. The host verifies the
runtime's complete file set/hashes, freezes input and expected snapshots before
execution, and independently compares every emitted file except the exact
declared execution JSON. The status/schema and raw JSON hash are verified on
every run. `-Unsupported` instead requires a real nonzero exit, diagnostics and
no emitted partial output; it cannot be combined with positive expectations or
idempotence. No manifest `OutputFiles` list selects comparison files.

The case result is intentionally named **`OutputChecksPassed`**, not formal
acceptance. `OutputCompilationVerified`, `BehaviorVerified` and
`BaselineCheckpointVerified` remain false: this helper does not pretend those
additional requirements have run. No whole-replay signer is implemented here.

Actual project-producer runs passed initial/repeat/idempotent normalization and
unsupported-downgrade checks:

- `Artifacts\isolated-case-project-normalize-single-line-heldout-1e7a4aed31cd4973a1f8829d865c9313\case-observation.json`
- `Artifacts\isolated-case-project-unsupported-downgrade-69b9f0ba3ba44452b1104cc3b911267c\case-observation.json`

The unmodified protected language producer also passed the no-VB unsupported
input check in
`Artifacts\isolated-case-language-unsupported-no-vb-c870f36c627e445f8fd1ff9431879041\case-observation.json`.
The final targeted suite contains **116 passing replay/Sandbox contract tests**;
its TRX is `Artifacts\trusted-replay-validation\protected-runtime-packet.trx`.

The initial multiline fixture failed strict comparison because its independently
written expected XML used CRLF whereas XML parsing normalized line endings to LF.
That failed case and oracle were preserved. A new independently authored,
single-line input/expected XML pair removed that ambiguity; comparison itself was
not relaxed and no observed output was copied into an expected directory.

### Concrete language-execution blocker

The original protected language producer was actually executed in
`sandbox-probe-0075fec7b78a485d908a087df4bbc89c`. It reached conversion of one VB
document and two generated context documents, then failed with access denied.
To obtain the exception stack without altering the real producer, a private
**diagnostic-only clone** changed just stderr exception formatting from `Message`
to `ToString`, went through both protected build/compiler phases, and reproduced
the failure in `sandbox-probe-75da8fb071b6480c9097fa8d9db01bbf`. This clone is not
used for acceptance and does not replace the original source or runtime.

The stack identifies:

```text
System.UnauthorizedAccessException
System.IO.MemoryMappedFiles.MemoryMappedFile.CreateNew
Microsoft.CodeAnalysis.Host.TemporaryStorageService.CreateTemporaryStorage
Microsoft.CodeAnalysis.SolutionCompilationState.SkeletonReferenceCache.CreateSkeletonSet
ICSharpCode.CodeConverter.CSharp.VBToCSProjectContentsConverter.InitializeSourceAsync
```

The approved Roslyn 4.14.0 package records source revision
[`8edf7bcd4f1594c3d68a6a567469f41dbd33dd1b`](https://github.com/dotnet/roslyn/blob/8edf7bcd4f1594c3d68a6a567469f41dbd33dd1b/src/Workspaces/Core/Portable/TemporaryStorage/TemporaryStorageService.cs).
That implementation constructs Windows names
`Roslyn Shared File: Size=... Id=...` and calls `MemoryMappedFile.CreateNew` in
the failing path; it does not catch this exception there. The executor has no
supported configuration that redirects those internal mapping names.

Resolving this requires a compatible, independently reviewed process/object
boundary or a reviewed producer/dependency adaptation. It is not a missing SDK,
disabled Sandbox, compiler-provenance failure, or permission to run on the host.
This implementation does **not** remove token restrictions, expose expected
files, patch approved package binaries, or reinterpret the failed conversion as
success. The exact diagnostic clone hashes and guest logs are retained beneath
`Artifacts\protected-real-tools` and `Artifacts\protected-tool-cases`.

Full language behavior, all required frozen-stage transformations/manual API
exceptions, output compilation and checkpoint reconciliation remain unaccepted.
The preserved S0/S1/S2/S2a/S3 refs are not replaced with synthetic savings or
reference-reviewed credit. TOOL002 and Golden promotion remain blocked.

## Commands

Run from the assessor worktree. An already-running Sandbox causes refusal rather
than interference with someone else's session.

```powershell
$runner = '.\assessment\Sandbox\Invoke-SandboxProbe.ps1'

# Own platform canaries only; no submitted project or CLI.
& $runner

# Inspect generated mappings/settings without launching any VM.
& $runner -PrepareOnly

# Entire producer restore/build occurs inside Sandbox.
$build = & $runner `
  -SourceRoot 'C:\private\owned-tool-source' `
  -Projects 'Utility\Utility.csproj','ProjectUtility\ProjectUtility.csproj' `
  -PublicFrameworkRoot 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework' `
  -PublicPackageRoot 'C:\private\curated-public-package-cache' `
  -TimeoutSeconds 600
$build.HostProducerObservation # CompilerProvenanceVerified=false; no acceptance

# -SignProducerProof is unsupported until compiler capture is outside build control.

# Fresh Sandbox for actual CLI execution. Expected files stay on the HOST.
$run = & $runner `
  -BinaryRoot 'C:\private\verified-producer-output' `
  -EntryAssembly 'Utility.dll' `
  -InputRoot 'C:\private\fixture-input' `
  -CommandArguments '--input','{input}','--output','{output}' `
  -ExpectedOutputRoot 'C:\private\host-only-expected-output' `
  -TimeoutSeconds 600
$run.HostExecutionVerification
```

Framework/package mappings are optional. The Framework root contains
`.NETFramework\v4.7.2` (or other required reference versions). Package roots must
be **curated to the submitted dependency closure**, not an entire user profile
or a cache containing unrelated private material. Network-disabled jobs cannot
download missing packages. The default public SDK root is
`C:\Program Files\dotnet`; SDK 10.0.401 is selected explicitly.

`SourceRoot` is staged without `.git`, `bin`, `obj`, or `Artifacts`.
Projects are explicit source-relative existing paths. Roots containing the
private assessor tree and all input reparse points are rejected. CLI binaries
and fixture inputs are separately staged readonly. `{input}` and `{output}` are
required argument placeholders. Expected output cannot be inside any staged or
mapped input root.

## Enforced execution boundary

The `.wsb` disables networking, clipboard, audio, video, printer redirection and
vGPU, and enables Protected Client. Only the per-run payload, public SDK and
optional curated public references/packages are mapped readonly. A fresh owned
export directory is the only writable host mapping.

Windows Sandbox logon commands run as an administrator. That fact alone is not
an acceptable evaluator controller boundary:

- `GuestProbe.ps1` is the trusted controller, not the submitted executable.
- `RestrictedProcess.cs` creates a low-integrity token with administrator
  membership deny-only, privileges disabled, and write-restricted SIDs.
  An explicit standard-handle allowlist prevents controller handle inheritance.
- Submitted build/CLI processes enter an owned kill-on-close job **before**
  execution. Descendants are terminated and the job is observed empty before
  trusted collection. No submitted MSBuild target is run as the controller.
- Controller files and process memory receive high mandatory labels with both
  no-read-up and no-write-up. Runtime/cache directories are guest-owned,
  low-integrity work areas. SDK/NuGet first-use initialization runs against an
  owned canary before submitted execution; migration sentinel files are not
  fabricated and the token is not relaxed to make restore pass.

The writable export is demonstrably writable by a low candidate. Therefore,
**guest `Passed`/exit JSON files are not trusted by themselves**. Before candidate
execution, the protected controller generates a one-run random transport nonce.
The host receives and removes its bootstrap copy, then acknowledges through the
readonly payload mapping. The nonce stays in protected guest memory/files until
all restricted processes are gone. It is not the host's receipt-signing private
key; no signing key is ever mapped or loaded into the guest.

The host ignores reports without the pinned one-run nonce, waits for the owned
Sandbox to terminate, rejects reparse exports, and verifies actual files.
Every preflight deliberately writes a forged guest `Success` report with an
incorrect nonce; it must not complete host verification.

## Host verification and returned evidence

Diagnostic producer mode checks authenticated process exits, unchanged authored source,
non-skipped compiler execution, a unique managed compiler `/out:` argument,
workspace-contained paths, and **byte identity of the compiler output and
evaluated TargetPath**. Both files and reported arguments are controlled by
submitted MSBuild. Returned paths/hashes are observations only, not verified
compiler outputs. `HostProducerVerification` and `SignedProducerProof` are null;
`HostProducerObservation.CompilerProvenanceVerified` is false. No signing key is
created. Neither these diagnostics nor guest-controlled metadata establishes
producer provenance.

CLI mode captures the actual restricted-process exit and exports output only
after process termination. Optional expected-output verification compares the
complete file set and SHA256 bytes against a snapshot taken before execution.
No expected file or directory is sent to the guest. This script's comparison is
byte-exact; the existing evaluator's Roslyn-normalized comparison is a separate
layer, not an implied exemption for differing manifests or source.

For a trusted declared execution manifest, PowerShell 7+ accepts
`-EvidenceFile migration-manifest.json -EvidenceStatusProperty Status -EvidenceSuccessValue succeeded`
together with `-ExpectedOutputRoot`. The filename must be exact, top-level JSON
and absent from the expected source tree. Host-only `OutputEvidence.ps1` checks
the JSON object and unique successful status; returned `ExecutionEvidence`
retains its raw SHA256. Only that file is removed from byte equality; every
other emitted file remains checked independently, not selected by `OutputFiles`.
Empty output, missing/malformed/failed evidence, or unexpected source still fail.
The declaration/expectations are never included in the guest job. This is one
execution check, not deterministic-rerun or full-receipt acceptance. The JSON
parser has matching C#/PowerShell regression coverage, including BOM, duplicate
status properties and misleading empty manifest file lists.

Inspections are bounded to 256 MiB per file, 1 GiB total and 100,000 files.
Unexpected/missing results, startup failures, timeout, stale/forged transport,
source changes and comparison failures remain failures. Consistent substitution
of both compiler/target paths is not detected; consequently no provenance is granted.
Run directories and failure evidence stay under private `assessment\Artifacts`.
The guest shuts down only its own VM; the host stops only its owned launcher PID
on timeout and never kills processes by name.

## Actual verification performed

On this machine, non-elevated `Win32_OptionalFeature` reported enabled
(`Containers-DisposableClientVM`, `InstallState=1`). Actual fresh Sandboxes proved:

- SDK **10.0.401**, Framework CLR **4.0.30319.42000**, WPF window construction,
  no default network route, and readonly payload enforcement.
- Restricted process: not administrator; own work writes succeed; controller
  file write/read and controller process-memory read fail.
- Actual restricted SDK restore/build and post-build output byte comparison,
  which is **not compiler provenance**.
- Actual **net472 WPF/XAML** producer build against readonly public Framework
  reference assemblies, with source preservation and post-build byte observations.
- An independently built owned CLI executed in a fresh Sandbox and matched
  host-only expected bytes. A successful-exit stub variant was rejected for
  incorrect actual output.
- Forged guest success reports did not complete the host handshake.

One immediate subsequent VM startup failed to bootstrap; a fresh retry passed.
Startup failure is reported, never converted to a successful case.

## Historical producer proof and remaining claims

All script results retain `FormalVerified=false`. The original single-build
probe remains diagnostic only; the separate second-compilation route above is
bounded captured-input provenance, **not a whole-assessment signer**.
The previous `taskotime-sandbox-producer-v1` proof must not be relied on even for
its narrower `producer-source-build-only` claim. A valid signature authenticates
that historical host assertion, not its truth. The full-replay verifier already
rejects that policy; current code cannot create another such proof.

On 2026-09-11 the private run
`Artifacts\sandbox-probe-78ba7fd4178547969361d99f8a5b19a5\producer-proof.json`
was found (SHA256 `FCD301FF9295B57530F98F7CD4B8D2541DE6265CC80A9F8F759A8DB53D73BF48`).
Its recorded expiry was `2026-09-11T21:59:33.5054388-07:00`. It is preserved as
historical evidence, not accepted provenance. No full replay receipt was found.

A real bounded negative run on SDK 10.0.401,
`Artifacts\sandbox-probe-18157cfa750b47a3980453cdcfd47ff2`, executed an owned
`AfterTargets="Build"` fixture inside Sandbox. The tracked text payload replaced
**both** output paths; all three SHA256 hashes were
`982EE74BB898EA1701BC6044ED437B9EAEB9484EAE0E356C69FE95C7CA6A9413`.
Authored source stayed unchanged and the restricted build exited 0. The corrected
host returned only an unverified observation, no producer verification/signature,
and `FormalVerified=false`; the VM shut down. The host diagnostic is in
`Artifacts\owned-double-overwrite\host-diagnostic.json`. These are private
diagnostic artifacts, not acceptance receipts.

The standard external replay contract
is in `..\Modernization.Analyzers.Tests\README.md` / `ExternalReplay.cs`.

Before formal use, implement trusted compiler/input/output provenance outside
submitted build control, independently review this native/controller boundary, and wire
the complete trusted host replay sequence: applicable held-out cases,
deterministic reruns, idempotence, unsupported/no-partial-output checks,
actual emitted-code behavior, checkpoint reconciliation, and signing only after
every required check. Existing `CompilerInputLoader` host MSBuild invocation must
also be routed through the isolated executor for blind submissions; this script
does not silently retrofit it.

Producer/CLI canaries are not SQL, interactive desktop, visual accessibility or
complete application acceptance. Owned-reference validation remains separately
available as diagnostics via `..\Reference-Replay.md`; it grants no TOOL002 or
Golden acceptance and must not be labelled formal isolation.

Authoritative platform references:

- https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file
- https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-cli

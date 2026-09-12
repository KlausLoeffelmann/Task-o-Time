# Executable Windows Sandbox diagnostics; producer provenance blocked

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

All script results retain `FormalVerified=false`. This is a diagnostic execution
primitive, **not a source-to-binary verifier or whole-assessment signer**.
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

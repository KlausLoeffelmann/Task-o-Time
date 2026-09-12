# Executable Windows Sandbox producer and CLI boundary

This implementation uses the **existing enabled Windows Sandbox feature**.
It does not enable/install Windows features, create Windows users, install a
container engine, or import a signing key into the guest.

`Invoke-SandboxProbe.ps1` supports an owned capability probe, source producer
builds, and actual CLI execution. Despite the historical probe filename, producer
and CLI jobs are real restricted guest processes. Host-side checks do not execute
returned binaries or evaluate submitted MSBuild projects.

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
$build.HostProducerVerification.CompilerTargets

# Optional producer-only proof, signed on the HOST after actual checks/VM shutdown.
# Requires PowerShell 7+. A fresh private key exists only in host memory.
$proof = & $runner -SourceRoot 'C:\private\owned-tool-source' `
  -Projects 'Utility\Utility.csproj' -SignProducerProof
$proof.SignedProducerProof

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

Producer mode checks authenticated process exits, unchanged authored source,
non-skipped compiler execution, a unique managed compiler `/out:` argument,
workspace-contained paths, and **byte identity of the compiler output and
evaluated TargetPath**. Returned target paths identify actual exported binaries.
Guest-controlled metadata alone does not establish success.

CLI mode captures the actual restricted-process exit and exports output only
after process termination. Optional expected-output verification compares the
complete file set and SHA256 bytes against a snapshot taken before execution.
No expected file or directory is sent to the guest. This script's comparison is
byte-exact; the existing evaluator's Roslyn-normalized comparison is a separate
layer, not an implied exemption for differing manifests or source.

Inspections are bounded to 256 MiB per file, 1 GiB total and 100,000 files.
Unexpected/missing results, startup failures, timeout, stale/forged transport,
source changes, output substitutions and comparison failures remain failures.
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
- Actual restricted SDK restore/build and host compiler/target byte comparison.
- Actual **net472 WPF/XAML** producer build against readonly public Framework
  reference assemblies, with source preservation and host byte verification.
- An independently built owned CLI executed in a fresh Sandbox and matched
  host-only expected bytes. A successful-exit stub variant was rejected for
  incorrect actual output.
- Forged guest success reports did not complete the host handshake.

One immediate subsequent VM startup failed to bootstrap; a fresh retry passed.
Startup failure is reported, never converted to a successful case.

## Precisely scoped producer proof and remaining claims

All script results retain `FormalVerified=false`. This is a working execution
and producer-verification primitive, **not a completed whole-assessment signer**.
`-SignProducerProof` can sign only the actual host-verified producer result, after
VM shutdown. It binds source-file hashes, compiler/target hashes, SDK version,
restricted-build result and observed controller protections. It generates a fresh
3072-bit RSA key only in the host process, writes the public key and its fingerprint,
self-verifies the RSA-PSS/SHA256 signature, and disposes the private key without
exporting it.

The proof policy is deliberately `taskotime-sandbox-producer-v1`, scope
`producer-source-build-only`. It is **not** the full
`taskotime-isolated-replay-v1` receipt and cannot satisfy full TOOL002 acceptance.
A regression explicitly rejects producer-only proofs at the full-replay verifier.
One actual producer run was signed and independently verified with its exported
public key; no fake run or claimed unexecuted fixture was signed.

The standard external replay contract
is in `..\Modernization.Analyzers.Tests\README.md` / `ExternalReplay.cs`.

Before formal use, independently review this native/controller boundary and wire
the complete trusted host replay sequence: applicable held-out cases,
deterministic reruns, idempotence, unsupported/no-partial-output checks,
actual emitted-code behavior, checkpoint reconciliation, and signing only after
every required check. Existing `CompilerInputLoader` host MSBuild invocation must
also be routed through the isolated executor for blind submissions; this script
does not silently retrofit it.

Producer/CLI canaries are not SQL, interactive desktop, visual accessibility or
complete application acceptance. Owned-reference validation remains separately
available via `..\Reference-Replay.md`; do not label it formal isolation.

Authoritative platform references:

- https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file
- https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-cli

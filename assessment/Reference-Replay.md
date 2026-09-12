# Owned-reference replay: diagnostic only, provenance blocked

This lane validates **independently source-reviewed, assessor-owned reference
tools** without pretending a local process is isolated. It cannot satisfy TOOL002
or authorize Golden promotion. It is not an alternative route for an untrusted
candidate submission.

**Provenance correction:** submitted MSBuild controls both the compiler `/out:`
file and evaluated `TargetPath` after build. An `AfterTargets="Build"` copy can
replace both while leaving authored source unchanged and compilation enabled.
Equal hashes establish consistency only, not compiler production. Until a trusted
compiler/output capture outside submitted build control exists, both verification
flags stay false, even if every local behavioral replay passes.

The three execution choices are deliberately different:

| `ASSESSMENT_REPLAY_EXECUTION` | Accepted claim |
| --- | --- |
| `local-reviewed` | Development checks only; never satisfies TOOL002 |
| `reference-reviewed` | Exact independently approved source, fresh build and actual local replay diagnostics; no TOOL acceptance |
| `external-receipt` (default) | Signed, request-bound replay attestation from an independently trusted isolated executor; no local CLI execution |

`Replay.Verified` remains **false** for reference-reviewed runs.
`Replay.ReferenceVerified` also remains **false**. `LocalEvidencePassed` can
describe successful diagnostics, not source-to-binary provenance or acceptance.
`ReferenceBuild.CompilerProvenanceVerified` is false; `ReportedCompilerArgumentHashes`
and `ObservedBuildArtifacts` deliberately name observations rather than attestations.
All other final quality diagnostics and the zero-defect gate remain unchanged.

## Configure the trusted plan

Use the existing `ReplayPlan` interface documented in
`Modernization.Analyzers.Tests\README.md`. Reference mode currently supports
managed `dotnet <producer.dll> ... {input} ... {output}` CLIs:

- `SourceRoots` must contain exactly one absolute root holding the complete
  reviewed tool source graph, linked projects, configuration and imports.
- `Projects` lists every producer project to build, not only a convenient
  unrelated project. Review approval binds their relative paths exactly.
- Each command names exactly one absolute DLL under that source root. Other
  arguments must not be absolute paths; in particular, private expected fixture
  paths must never be passed to a producer. Relative project selectors are fine.
- Fixtures/expectations/behavior programs must be private and outside the
  producer source root. Existing applicable positive/unsupported/checkpoint and
  behavior/idempotence requirements still apply. No manifest or diagnostic
  exemption is introduced for a particular product.

Do not point `SourceRoots` at a broad checkout containing the assessor. The
private preparation `tools` subtree is usually the appropriate complete root.
The plan and approval remain assessor-owned configuration, never candidate files.

## Exact commands

From the assessor worktree, with a reviewed-source application scan root:

```powershell
$p = '.\assessment\Modernization.Analyzers.Tests\Modernization.Analyzers.Tests.csproj'
$env:ASSESSMENT_STAGE = 'S4'
$env:ASSESSMENT_MODE = 'final-delivery'
$env:ASSESSMENT_CONFIGURATION = 'Debug'
$env:ASSESSMENT_REPLAY_PLAN = 'C:\private\assessment\owned-reference-plan.json'
$env:ASSESSMENT_REPLAY_EXECUTION = 'reference-reviewed'
Remove-Item Env:\ASSESSMENT_REFERENCE_APPROVAL -ErrorAction SilentlyContinue
dotnet test $p --filter 'Category=ReferenceReviewRequest'
```

This metadata-only selector does not build/evaluate the producer. It writes
`Artifacts\Reports\S4-FinalDelivery-Debug\reference-review-request.json` and leaves
TOOL002 unsatisfied. That request is **not approval**: it has
`Purpose=REVIEW_REQUIRED` and empty reviewer/revision fields.

An independent reviewer must inspect the exact source snapshot, its build
targets/imports, dependencies and actual conversion/project-transformation
producer path. Review specifically rejects fixture-copying, hidden expected-file
lookups, source-independent stub output, unsafe side effects and uncontrolled
external build inputs. Record the real reviewed revision and review identity.
Only then create a separate private approval file:

```json
{
  "Purpose": "owned-reference",
  "ReviewId": "<real independent review identifier>",
  "Reviewer": "<actual reviewer>",
  "SourceRevision": "<reviewed commit/revision>",
  "SourceHash": "<unchanged SHA256 from the reviewed request>",
  "Projects": [
    "Utility\\Utility.csproj",
    "ProjectUtility\\ProjectUtility.csproj"
  ]
}
```

The hash is authoritative for the exact source bytes; the revision/reviewer
strings are review records, not an automatically verified ownership certificate.
Do not fabricate them or automatically promote a generated request to approval.

```powershell
$env:ASSESSMENT_REFERENCE_APPROVAL = 'C:\private\assessment\reference-approval.json'
dotnet test $p --filter 'Category=RepositoryScan|Category=Modernization'
```

## What actually executes

1. Validate the independent approval against the current complete source hash and
   exact declared project set. Source hashes omit `bin`, `obj`, `Artifacts` and
   `.git`, and reject reparse points. Changed or unapproved source fails before
   **any repository MSBuild evaluation**, as well as before fresh build/replay.
   An approval for one producer cannot authorize a replacement
   fixture-copying stub.
2. Copy clean approved source into a fresh private workspace. No supplied `bin`
   or `obj` files enter that copy. Add empty root build/package-props boundaries
   when absent to prevent accidental imports from the enclosing assessor tree;
   supply the assessor SDK pin when the source root has none.
3. Restore/build the declared projects using the pinned .NET 10 MSBuild, with
   node reuse/shared compilation disabled. Capture evaluated `TargetPath`;
   require actual C#/VB compiler arguments and a compiler `/out:` artifact whose
   bytes match that target. Reject skipped compilation, missing/out-of-workspace
   or ambiguous targets, inconsistent outputs and source mutation. This does **not**
   detect replacement of both outputs; no provenance or acceptance is granted.
   Disable discovery of the enclosing assessor's Git repository/SourceLink and
   supply the independently reviewed source revision explicitly.
4. Map each logical command DLL to the fresh workspace's **observed build output**,
   not to the supplied `bin` tree. A build target may still substitute tracked payloads.
   Record approved source hash, actual build-input hash, exact build commands,
   reported compiler-argument hashes and hashes of observed binaries,
   dependency DLLs and runtime metadata.
5. Run diagnostic CLI replay against those outputs, only for independently
   source-reviewed owned tooling. Frozen expectations,
   full emitted-file comparisons, deterministic reruns, applicable behavioral
   compilation/execution, unsupported input and project idempotence still apply.
   Check source/binary/dependency stability afterwards.

For a nondeterministic execution manifest, use the trusted per-case
`EvidenceFile` contract in the replay README, for example
`{"FileName":"migration-manifest.json","StatusProperty":"Status","SuccessValue":"succeeded"}`.
Only that exact JSON file is separated from emitted source equality. Every run
must have a successful status; its raw hash is retained. All other output is
independently hashed/compared, never selected using a manifest's file list.
The approval, input, source and binary hashes are not narrowed by this contract.

Child processes receive a runtime/OS environment allowlist plus workspace-owned
temporary, profile, CLI and NuGet state. The existing package cache is offered
as a fallback; user credential/configuration environment variables are not
forwarded. SDK, package feeds/cache and external imports still need ordinary
trusted-build review. Build/replay failures are failures, not automatic approval
to loosen the fixtures.

The positive diagnostic regression really builds an owned synthetic XML-transform
CLI, then replays the build output while the supplied DLL is deliberately invalid.
Negative regressions reject changed fixture-copying source, unapproved requests,
different projects/reviewers and absolute expected-file arguments. Existing
empty-class and local fixture-copy negatives remain in place. A double-overwrite
regression builds a runnable tracked payload, changes the authored program to
return 91, and copies that payload over both output paths after compilation.
Before the correction it incorrectly obtained `ReferenceVerified=true`. Now the
same passing behavioral diagnostics grant neither reference nor formal acceptance.

## Limits and formal executor feasibility

Source review is a different trust assumption from adversarial isolation and
does not provide compiler provenance. A malicious source reviewer or same-identity
attacker can undermine it. The reference path must never be used for blind
submissions, accepted as TOOL002, or labelled formal isolation.

Formal protocol and exact request/receipt commands are in
`Modernization.Analyzers.Tests\README.md`, under **Minimal external executor
contract**; wire records/verifier are in `ExternalReplay.cs`.

An **already installed and independently validated Windows container/VM backend**
can host restricted build/tool jobs only if the trusted supervisor, expectations
and signing material remain outside all candidate-accessible mounts/processes.
Docker availability or a `sandbox=true` flag alone proves nothing. Do not mount
the Docker socket, host drive, private assessor tree or signing key into the job.
Hypervisor-separated jobs provide a stronger boundary than shared-kernel process
containers for hostile code.

A Linux-only container cannot establish full Windows/WPF/.NET Framework build
or desktop acceptance. A Windows image must actually provide the supported SDK,
Framework reference packs and WPF build prerequisites; interactive STA/visual
acceptance may still need a suitable desktop VM. Test the installed backend's
capabilities before claiming coverage. No users, OS components, container engine
or signing service are provisioned by this bundle.

**MSBuild evaluation itself executes project code.** For formal untrusted
grading, route compiler-input extraction and the full build/scan through the same
separated execution boundary, not just the migration CLI. Copying all private
assessor files into a same-identity container and signing its self-report is not
acceptable. Until that backend is real and independently validated, report owned
reference correctness separately from formal candidate verification.

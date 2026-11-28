# Offline Linux launch proof and one-attempt proposal

Issue [#37](https://github.com/alexispurslane/tertius/issues/37)'s offline Linux transport criterion now passes. It remains open and In Progress with draft [PR #32](https://github.com/alexispurslane/tertius/pull/32) until merge. This proof supplies no GPU measurement and authorizes no submission.

## Exact source and input identities

| Input | Identity |
|---|---|
| Measured baseline | `b002351eb48d33f008f8333e53e61345de3260cd` |
| Measured candidate | `588b6d2747560c73aac542fb25ce80eec1ba8db7` |
| Immutable harness | `6f14335c629b25e36899699006c268026d33ce7e` |
| Preparation script SHA-256 | `b34b921ce27f674de3c15fa206a10e0deda59ec218abafdd1af3e9bee29e3d21` |
| Fifteen-file manifest SHA-256 | `445a4fb94e70d6326ec0fd8bf7d0a39aace36b5794c593bf225f0137f89bea57` |
| Measurement worker SHA-256 | `0dceefd84228d562a0e23945d014f57247fcf66bc097e9ab1fe5253fab7e0c25` |
| Embedded C# fixture SHA-256 | `70cc38ab48e94728d9706609cd8d3f343eb2ab5ded8ad71f183f3e5dc11b106d` |
| Original inert config SHA-256 | `69816100815599666260236b10abf315a908b85494dc62d418a7d69263dfd39e` |
| Real handler SHA-256 | `d9f9a0cbb2bc703eedf62219b3385b4e376719dc3f8040f685ad008a36089fab` |

All three named trees retain their existing shell and protocol 3. Neither measured source nor harness is repinned. The external launch program travels as short Python source in the command argument. The 15 payload files remain in the immutable harness. Preparation verifies every payload hash before copying any file. The reconstructed payload hashes match the manifest, including the unchanged inert `config.json`.

## Linux evidence

The parent found an existing Determinate Nix Linux builder at `ssh://nixbuilder@127.0.0.1:64237`. No VM or service was provisioned. Both proofs use that loopback builder, offline Nix, Python 3.14.10 and Bash 5.3p20. Linux reports aarch64 and 4,096-byte pages.

The parent's original proof used the exact historical 409,197-byte command. Its SHA-256 is `18707842aed762dec041dae6b08802025bae8a975ce53ce34cabecdc4fe1a340`. The real `handler.run_commands` calls the real `subprocess.Popen`; Linux raises errno 7 (`E2BIG`) before its harmless child starts. The 418-byte replacement command then passes through the handler and Bash, verifies all 15 files, and reaches the unchanged worker's inert gate. Both lengths gain one byte for the terminating NUL. [Original result](original-linux-result.json), [original scripts and input hashes](original-proof-inputs.json), and the compressed exact historical command under `tools/camera-cut-pristine/proof-data/old-command.gz` preserve the proof. The original derivation is `/nix/store/l3njw5djkvdfrrz8f6ipf21kkqpb97l3-tertius-issue37-linux-launch-proof.drv`.

The [final Linux result](final-linux-result.json) and [build log](final-linux-build.log) record ten passing normal Python regressions, without skips. The log names the exact derivation and loopback builder. Its text copy trims trailing spaces; [the gzip copy](final-linux-build.log.gz) preserves the exact log bytes. The result's store path is in [final-store-output.txt](final-store-output.txt). The suite exercises:

- The exact old argument through the real handler and `Popen`, including Linux's rejection.
- The final inert command through the real handler, `Popen` and Bash. It refuses before payload copying.
- The exact conditional approved command as the handler's single command argument, with a test shell that redirects only the worker launch to a harmless gate. The unchanged worker reads the runtime config, checks its existing approval and pin guards, then stops at its first attempt to create `linux-out`. The gate forbids sockets and further subprocesses. No measurement directory, renderer, asset download or cloud request occurs. This shell substitution tests the approved handoff without running measurement. It does not test the baked GPU Nix shell or its environment.
- Refusal of changed source pins, config or manifest hashes, caps, worker count, duration, output glob, retry intent, submission count and inconsistent approval field types. Corrupt payload bytes fail before any copy. Reusing a prepared folder refuses.
- The one-attempt wrapper's real entry function: unapproved refusal, consumed-attempt refusal, and one mocked canonical child with policy restoration and refusal of a second call. These wrapper tests substitute the service and child boundaries. They do not exercise Runpod admission.
- The existing sealed-output replay using the reconstructed worker and analyzer. The actual collector returns both `linux-out/return/evidence.tar.gz` and `linux-out/return/status.json`, with zero skipped files. All required archive members, raw rows and retained diagnostics survive, and the analyzer replays 24 observation rows. These are replayed local observations, not a new Linux GPU result.

The Mac run passes nine executable regressions and skips the Linux-only boundary test. Four affected earlier transport tests also pass, including the local offline Nix shell and the real sparse three-tree checkout. Earlier unaffected transport, preparation and output tests were not repeated. No engine build, renderer suite, capture, mutation run or reviewer was started.

## Final command and size admission

[The emitted PowerShell proposal](../fsr-reference-investigation-2026-11-28/bounded-pristine-proposal/transport-proposal.ps1) still starts with an unconditional `throw`. [Its JSON metadata](../fsr-reference-investigation-2026-11-28/bounded-pristine-proposal/transport-proposal.json) contains both the actual inert record and the separately labeled representative approved record. The [exact inert command](final-command.inert.txt) and [exact conditional approved command](final-command.after-approval.txt) contain no trailing newline.

| Quantity | Inert command | Conditional approved command |
|---|---:|---:|
| UTF-8 command bytes, including NUL | 4,230 | 4,227 |
| Shell arguments, environment reserve and pointers | 102,688 bytes | 102,685 bytes |
| HTTP JSON request body | 5,222 bytes | 5,219 bytes |

The inert command SHA-256 is `5a171c31f9d693dfb5a9bcee9b40a3762913a021f93cee28ab24b7d5a2fbf705`. The conditional approved command SHA-256 is `8edcc5168bfcff9c25e9ce80b4f09224eadca0795aeb85fb9b4a90f2127f8e2c`. These hashes cover command bytes without NUL. The emitter limits each command argument to 32,768 bytes, aggregate reserved size to 131,072 bytes and request JSON to 65,536 bytes. It counts every string's NUL and 64-bit pointers. It reserves 65,536 bytes and 4,096 entries for the environment. The Linux proof observes 625 environment bytes at emission; the future GPU environment remains unmeasured. The canonical outer shell arguments match `tools/runpod/worker.py`.

The canonical command requests exactly `before`, `after` and `harness`, 1,920 command seconds and `linux-out/return/*`. Its conditional policy is 1,980 execution seconds and the existing 7,200-second queue TTL. No output glob selects a source tree or the approval record.

## External approval and the only runtime config changes

The launch program checks the pinned preparation and original config before parsing scope. The external record binds the full source config hash, payload manifest, all three source pins, durations, $0.328 maximum modeled reservation, $5 daily cap, $30 total cap, five-worker GPU maximum, exact output glob, ordinary canonical shared routing and no automatic retry. The full source config hash also binds the four-process measurement scope, GPU model and driver, film scripts, dimensions, rounds, warmup, allocator identity and retained diagnostics.

The actual record keeps both review and paid flags false, duration-policy approval false and authorized submissions zero. The hypothetical transition changes exactly four record fields: `coordinator_reviewed`, `issue4_explicit_paid_approval` and `duration_policy_approved` become `true`; `authorized_submissions` becomes `1`. All other record values must match exactly. This record is an approval handoff, not a signature or an independent source of consent.

Only these three paths differ between immutable `config.json` and the new `config.runtime.json` received by the unchanged worker:

| JSON path | Source value | Conditional runtime value |
|---|---|---|
| `approval.coordinator_reviewed` | `false` | `true` |
| `approval.issue4_explicit_paid_approval` | `false` | `true` |
| `approval.authorized_submissions` | `0` | `1` |

Every other source value stays byte-semantically equal after JSON parsing. This includes `automatic_retry`, `submitted`, source status/reason text and duration-policy metadata, which describe the immutable proposal. External duration-policy approval belongs to the external record and the parent-owned launch wrapper. The worker reads the new runtime config path; it never overwrites immutable `config.json`.

## Reviewable one-attempt launch

`/tmp/tertius-issue37-one-attempt` contains an inert external preparation. Its approval flags are false and its reviewed HEAD, authorization time and authorization source are unset. It has no consumed marker and no canonical child log. The complete [approval template](approval.template.json), [conditional canonical argv](canonical-argv.after-approval.json), [inert one-attempt PowerShell command](launch-once.proposal.ps1), [previous wrapper](run_once.py.txt) and [proposed wrapper](run_once.proposed.py.txt) are committed here for review.

The proposed wrapper retains the previous exclusive `submission-consumed.json` marker, canonical CLI, receipt collection and cleanup. It adds an early fresh-approval check and binds canonical argv to both the emitted review and the external approval's SHA-256. The marker is consumed before the sole canonical child, including failed admission. There is no retry. Canonical locking, secret scanning, image selection, budget admission, shared routing, publication and pending-job accounting remain in `gpu_job.py`.

The full proposed wrapper was also invoked locally with its actual inert approval file under an audit hook that forbids sockets and child processes. It refused at the approval check before git, policy changes or any external-effect event. [The wrapper result](external-wrapper-inert.json) records zero submissions and unchanged config bytes. This run did not consume the attempt.

After fresh explicit approval, the parent must record consent and reviewed clean HEAD in that external file, preserve the canonical argv hash, and capture fresh `audit-before.json` and `endpoints-before.json` for the inherited cleanup. The parent then uses the one-attempt wrapper, rather than invoking the canonical argv separately. The wrapper restores the exact original local policy bytes on every outcome. It restores a newly routed owned GPU endpoint from 1,980,000 to 900,000 milliseconds, then reads back defaults. Existing GPU and CPU endpoint defaults must remain 900,000 and 7,200,000 milliseconds. Normal shared routing can create an endpoint; that exposure still needs the parent's policy decision.

## Remaining decisions and evidence limits

- Fresh paid approval and duration-policy approval remain absent. The previous job's approval is consumed. The checked-in policy remains 840 command seconds and 900 execution seconds, SHA-256 `d6579e48c7fa847e54c07e2cfac3757a77451882882ea4d4856bfd2920cbc52f`. No new Runpod API call or endpoint change occurred.
- The extra third-tree fetch and the existing 60-second preparation/output margin remain unmeasured on a GPU worker. The local builder tests only transport and handoff. They do not bound remote Git fetch time, image preparation or GPU output time. The earlier capacity projection does not account for the added checkout.
- Linux aarch64 with 4 KiB pages proves this argument boundary and handoff. It supplies no RTX performance, baked RTX worker environment, GPU memory admission, measurement completion or #4 Linux acceptance result.
- The parent owns the fresh approval record and snapshots, shared-routing policy, any paid launch, PR readiness, required CI and final-head metrics, merge and issue closure. The PR remains draft. Neither this proposal nor `Closes #37` in its body closes the issue before merge.

## Repeat without provisioning or spending

The normal repository Python discovery includes `tools/test_camera_cut_launch.py`. On Linux it runs the exact argument-boundary test; other hosts skip only that test.

```powershell
nix develop --command python3 -I tools/test_camera_cut_launch.py
nix develop --command python3 -I tools/camera-cut-pristine/linux_proof.py --out '/tmp/issue37-repeat-proof'
```

The second command uses the existing configured loopback builder and already installed Linux store dependencies. Its output folder must not exist. It starts no VM, downloads no toolchain and contacts no paid service. [The retained derivation](final-proof.nix.txt) and [test runner](final-runner.py.txt) show the small source snapshot and exact installed dependencies. Missing dependencies or an unavailable builder are failures, not permission to provision a replacement.

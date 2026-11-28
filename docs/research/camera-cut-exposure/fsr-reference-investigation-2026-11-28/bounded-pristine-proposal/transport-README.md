# Short command with an immutable harness tree

The local correction replaces the encoded launch payload with existing canonical named-tree support. The proposal names `before`, `after` and `harness` as three immutable checkouts. It makes no paid submission. The failed 886d job and its oversized command remain in the [terminal report](../../bounded-pristine-linux-2026-11-28/README.md).

| Named tree | Immutable commit |
|---|---|
| `before`: measured baseline | `b002351eb48d33f008f8333e53e61345de3260cd` |
| `after`: measured candidate | `588b6d2747560c73aac542fb25ce80eec1ba8db7` |
| `harness`: runtime input files | `6f14335c629b25e36899699006c268026d33ce7e` |

The harness files live under `tools/camera-cut-pristine`, because the canonical handler excludes `docs/` from its sparse checkouts. Both measured source pins retain the reviewed allocator blob. All three tree commits have identical dev-shell objects and handler protocol 3. No engine, canonical tool, canonical config or live endpoint changed in this local stage.

The [exact inert PowerShell command](transport-proposal.ps1) retains the unconditional `throw` before canonical submission. Its conditional request is in [transport-proposal.json](transport-proposal.json). The short shell command first verifies `prepare.py`, then runs it. Preparation verifies the pinned payload manifest and all 15 input hashes before copying anything. The unchanged worker then runs from the job root, beside `before`, `after` and `harness`.

The measurement worker, fixture, analyzer, three film scripts and eight retained diagnostics have the exact reviewed bytes. The fixture's C# SHA-256 stays `70cc38ab48e94728d9706609cd8d3f343eb2ab5ded8ad71f183f3e5dc11b106d`. The worker SHA-256 stays `0dceefd84228d562a0e23945d014f57247fcf66bc097e9ab1fe5253fab7e0c25`. The [transport pin record](transport.json) lists the preparation, manifest and payload hashes. The emitter no longer embeds those file bytes in an argument.

## Size bounds

The new command occupies 419 UTF-8 bytes including its terminating NUL. The exact failed command occupied 409,198 bytes including NUL. The regression failed against the original emitter before implementation: its inert command occupied 408,686 bytes including NUL and exceeded the test's 32,768-byte budget. The authorized historical command is slightly larger because its approval and result fields differ.

`command_bounds` counts UTF-8 bytes, NUL terminators, shell arguments and 64-bit argument/environment pointers. It reserves 65,536 bytes and 4,096 entries for the worker environment. The new command's aggregate reserved size is 98,877 bytes, below the conservative 131,072-byte budget. An observed environment larger than the reserve causes refusal. The actual Linux worker environment remains unknown, so this is a conservative admission check, not a measurement of the remote process.

The conditional JSON request occupies 1,127 bytes. The emitter refuses requests above 65,536 bytes. Runpod's [documented payload limits](request-limit-source.json) list 10 MB for `/run`. This stage makes no live payload-limit test. Request size, the operating system's individual-argument limit, aggregate argument/environment size and shell parsing are separate constraints.

## Local verification

The [verification record](transport-local-verification.json) retains the red regression and the exact old command's local replay. Docker's daemon was unavailable and the existing Colima VM was stopped. No local Linux environment was started. Linux's exact execution limit remains unverified locally.

The exact historical command passed through the real handler and `Popen` on Darwin with a harmless Python shell that refused without evaluating the payload. This Mac accepted the oversized argument. That result does not reproduce Linux's E2BIG failure.

The successful local checks are:

- Eight transport tests pass. They exercise the emitted command through the actual handler and `Popen`, including a local offline Nix shell. The PowerShell review command throws before canonical submission.
- The real protocol-3 handler checks out three trees from a local bare Git repository. It checks shell equality, excludes `docs/`, preserves tree identities, runs from the job root and reaches the existing inert worker gate. A different harness shell is refused before commands run. This uses synthetic local commits for protocol mechanics; the emitted production pins are verified separately against their real git objects.
- Four preparation tests pass. Reconstructed files match every pinned hash. Corrupt input, missing input and changed manifest fail before copying. A changed preparation script also fails before its code executes.
- The affected emitter/output test passes. A targeted [sealed-output replay](transport-output-replay.json) uses the reconstructed worker and analyzer modules, retains raw rows and diagnostics, and passes the real collector with zero skipped files. The output glob remains exactly `linux-out/return/*`. The earlier 13-test output proof remains separate and unchanged.

These checks ran in the dev shell. No renderer, new diagnostic process, capture campaign, mutation run, full Rendering suite, paid service or endpoint operation ran during the correction.

## Remaining blockers

Issue [#37](https://github.com/alexispurslane/tertius/issues/37) stays open and In Progress. Its required offline Linux regression is unverified. The local correction has not established Linux acceptance for #4.

The extra harness checkout occurs before command timing, inside the existing 60-second handler allowance shared by tree setup and final output. Its Linux fetch duration and remaining output headroom are unmeasured. The earlier 1,838.57-second command-capacity projection does not measure this extra checkout. No Mac frame time supports a Linux capacity claim.

The immutable config is deliberately inert: both approval flags are false and authorized submissions are zero. A future run would require a separately reviewed external authorization step. This proposal supplies no such step. The canonical GPU defaults remain 840 command seconds and 900 execution seconds, with their original bytes. The conditional 1,920/1,980-second proposal and $0.328 modeled ceiling still require separate duration-policy and spending approval. No retry is authorized.

The parent retains readiness, required CI, final-head CI metrics and merge. There is no `Closes #37` claim while its Linux acceptance criterion remains unmet.

## Repeat the local checks

```powershell
nix develop --command python3 -I tools/test_camera_cut_transport.py
nix develop --command python3 -I tools/camera-cut-pristine/test_prepare.py
nix develop --command python3 -I docs/research/camera-cut-exposure/fsr-reference-investigation-2026-11-28/bounded-pristine-proposal/test_output_budget.py OutputBudgetTests.test_inert_emitter_names_every_input_and_the_exact_return_glob
```

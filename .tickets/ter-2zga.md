---
id: ter-2zga
status: closed
deps: []
links: [te-r7nt]
created: 2026-11-12T07:03:49Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# MoltenVK with the encoder patch, built in the flake for the Mac: 0.93 ms on the deck

Sweep addendum (/private/tmp/tertius-sweep-20261111/addendum.md) A5 and A6 proposal 1. MoltenVK 1.4.2 ends the Metal encoder at every vkCmdPipelineBarrier2 outside a render pass; the patch mvk-keep-dispatch-encoder.patch (form 'keep') keeps an open compute encoder across a barrier whose destination is compute alone, ordering with Metal fences (the three rules of A5). Measured in take 3: deck frame span 24.00 -> 23.07 ms, encoders 116 -> 79, idle time 2.25 -> 1.41 ms, images byte-identical, Metal API validation clean. flake.nix builds pkgs.moltenvk with the patch (measurement switches dropped except the stock-behavior switch; also take upstream's fix for MoltenVK issue 2805), the dev shell's VK_ICD_FILENAMES names it, and the Mac build ships it. The engine must not depend on it: Linux and stock MoltenVK render the same frame. Acceptance: full GPU test suite with the patched driver, once with MTL_DEBUG_LAYER=1, and a deck trace matching A5. Risk: we maintain a driver patch; a wrong fence shows as a rare flicker. THE MERGE WAITS FOR THE USER'S DECISION (2026-11-12 morning); building on a branch is fine.


## Notes

**2026-11-12T11:19:03Z**

Built on branch `ter-2zga-patched-moltenvk` to the point where only the merge is left (2026-11-12). The merge waits for the user's decision.

What the branch holds:

- `native/moltenvk.nix` builds the nixpkgs MoltenVK 1.4.2 with two patches. `native/moltenvk-execution-dependencies.patch` is upstream's fix of issue 2805 (pull request 2819). `native/moltenvk-keep-dispatch-encoder.patch` is our patch in the form `keep`. Its only switch is `TERTIUS_MOLTENVK_STOCK_BARRIERS=1`, which gives the stock behavior.
- `flake.nix`: the Mac's dev shell names the patched build in `VK_ICD_FILENAMES`, and `TERTIUS_STOCK_MOLTENVK_ICD` names the nixpkgs build. `nix build .#moltenvk` builds the driver alone. The derivations of the two Linux dev shells equal main's.
- One change against the measured patch. Upstream's fix lets one compute encoder hold work of several barrier stages. So the patch keeps only an open compute encoder that used the compute stage alone. Every other encoder takes the stock path. The deck's encoder count stayed at 79.
- The docs: `tools/README.md`, "The patched MoltenVK" (why, the three conditions, the three ordering rules, the two switches, the checks before a merge of a patch change). Also one paragraph in ARCHITECTURE.md, "Portable Vulkan", and lines in `native/README.md`, GUIDE.md and README.md.
- The sweep's research is in `docs/research/frame-time-sweep-2026-11-11/`. Its folder `acceptance-ter-2zga/` holds the acceptance record with every table and log.

Acceptance, on the M1 Max with the patched driver as the shell's default. "Before the merge" is commit 527cd445a with the engine code of main 58ce67f66. "After the merge" is commit 32f741740 with the engine code of main 7eed8d130.

| Check | Before the merge | After the merge |
|---|---|---|
| Frames of the deck, patched against stock | 29 frames, 0 differ | 29 frames, 0 differ |
| Frames of 24 capture scripts and the low view, patched against stock | Not run | 139 frames, 0 differ |
| Frames under `MTL_DEBUG_LAYER=1`, patched against patched | 62 frames, 0 differ | 113 frames, 0 differ |
| GPU frame span of the deck, stock to patched | 24.10 to 23.22 ms | 22.02 to 21.09 ms |
| Metal encoders of one deck frame, stock to patched | 116 to 79 | 117 to 79 |
| Idle time inside one deck frame, stock to patched | 2.23 to 1.40 ms | 2.25 to 1.40 ms |
| Every test suite in Checked | 10,757 passed, 0 failed, 26 skipped | 10,761 passed, 0 failed, 26 skipped |
| Rendering and Host tests under `MTL_DEBUG_LAYER=1` | 5,119 passed, 0 failed, 18 skipped | Not run |
| Every test suite in Release | 10,744 passed, 0 failed, 26 skipped | Not run |

The gain is 0.89 ms before the merge and 0.93 ms after it. Section A5 measured 0.93 ms, 116 to 79 encoders and 2.24 to 1.40 ms of idle time, so the deck trace matches A5. With `TERTIUS_MOLTENVK_STOCK_BARRIERS=1`, the patched build gives the stock numbers: 24.18 ms and 116 encoders before the merge, 22.15 ms and 117 encoders after it.

What the acceptance does not show:

- No test asserts on the effect of the patch, because the engine does not depend on it. The encoder count of a trace shows the effect.
- A wrong fence makes two encoders run in the wrong order only sometimes. Equal frames in every run lower that risk and do not remove it.
- The tests under Metal's API validation and the Release suites ran before the merge only. Run the Release suites once more at the merge into main, as the project's rule asks.

One part of the ticket has no work today. The ticket says that the Mac build ships the driver, and the repository has no packaging step for a Mac build. The dev shell is the only place that names a driver. Ticket t2-q0uk holds that part, and it waits for a packaging step.

Two things wait for the user: the merge of this branch, and a comment on upstream's issue 2893 with our numbers and our patch.

**2026-11-12T12:11:29Z**

Later on 2026-11-12, main gained two renderer changes, and the branch took both by a merge of main. The checks ran again on each state of the code.

| Check | After the second merge (f831de851, main 75ecde376, FSR's clear step in one dispatch) | After the third merge (05eed98ac, main 5330c7411, the hit light image) |
|---|---|---|
| Frames of 26 scripts, patched against stock | 168 frames, 0 differ | 168 frames, 0 differ |
| Frames under `MTL_DEBUG_LAYER=1`, patched against patched | 80 frames, 0 differ | 80 frames, 0 differ |
| GPU frame span of the deck, stock to patched | 21.82 to 20.81 ms | 21.56 to 20.71 ms |
| Metal encoders of one deck frame, stock to patched | 105 to 66 | 105 to 66 |
| Every test suite in Checked | 10,770 passed, 0 failed, 26 skipped | The touched test classes only: 276 passed, 0 failed |
| Every test suite in Release | 10,757 passed, 0 failed, 26 skipped | Not run |
| Tests under `MTL_DEBUG_LAYER=1` | FSR's test classes: 67 passed, 0 failed, 5 skipped | The touched test classes: 276 passed, 0 failed |

Over the four states of the code, the patch saves between 0.85 and 1.01 ms of the deck's frame. The hit light image changed 153 of the 168 frames, and both drivers give the same new frames.

The branch head holds main at 5330c7411. Main changes several times a night. At the merge into main, first merge main into the branch. Then run the suites in Checked and in Release, and one frame comparison against the stock driver (`tools/README.md`, "The patched MoltenVK").

**2026-11-12T13:43:18Z**

The merge, 2026-11-12. Alexis decided it. The branch took main twice more. The checks of `tools/README.md`, "The patched MoltenVK", ran on the last state, commit bc53e6d70 with main ad095e599 inside:

- Every suite in Checked: 10,778 passed, 0 failed, 26 skipped.
- Every suite in Release: 10,765 passed, 0 failed, 26 skipped.
- 80 frames of 5 scripts (the deck, the low view, `doorway`, `demo-surface`, `station`) equal the stock driver's, byte for byte.

Main then held two more commits, which change one ticket file and no code. The acceptance record has the section "The merge into main".

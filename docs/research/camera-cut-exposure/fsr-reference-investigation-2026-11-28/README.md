# The extreme FSR fixture exceeds the baseline meter's dark range

The local investigation explains the synthetic FSR reference difference without establishing a new candidate defect. Both source pins produce byte-identical scene HDR images. The baseline loses dark samples at its histogram's lower limit. The candidate retains those samples and agrees with an independent CPU reading. The shared tolerance stays at 1/16 stop.

Linux acceptance remains **HOLD**. The saved Linux job remains failed, with no matched timing rounds. These local results explain the reference difference. They supply no Linux performance result and authorize no spending.

## Pins and evidence

| Input | Identity |
|---|---|
| Baseline | `b002351eb48d33f008f8333e53e61345de3260cd` |
| Candidate | `588b6d2747560c73aac542fb25ce80eec1ba8db7` |
| Shared allocator blob | `f7aae7bd20d48ea3adf0d565ed476a6d2ec54594` |
| Prior Linux job | `e1315547-2360-47f4-b91d-681cf8fe10c0-u1` |
| Prior Linux synthetic FSR references | Baseline 8.584156 stops, candidate 8.914885 stops, difference 0.330729 stop |
| Local device | Apple Vulkan through MoltenVK, correctness evidence only |

The [independent analysis](analysis.json) retains all 32 automatic-exposure checkpoints. The [raw archive](local-raw-evidence.tar.gz) contains the two probes, raw float images, histograms, exposure sequences, validation results and command logs. The [manifest](raw-manifest.json) records each archived file's size and SHA-256. Extract the archive to run its `analyze.py` against the retained inputs.

The probes use separate plain source copies of the two pins. They do not change production code. Every recorded production-source hash was checked before the runs and after restoration. The original external fixture was restored afterward. [source-restoration.json](source-restoration.json) records the result. Local assemblies retain the final smoke fixture and are not claimed as Linux assemblies.

## Independent scene reading

The CPU reads the raw HDR image and distance image. It samples the top-left pixel of each 2×2 block only where distance is positive. It computes luminance with Rec. 709 weights, constructs each meter's histogram, trims 10% from each end, and computes the compensated target. A second CPU calculation uses sorted scene-luminance values without histogram bins.

| Local FSR quantity after 65 still captures | Baseline | Candidate |
|---|---:|---:|
| Applied exposure, stops | 8.576876 | 8.908857 |
| Scene samples | 1,296 | 1,296 |
| Samples in the meter's lowest bin | 610 | 0 |
| GPU histogram differs from independent CPU histogram? | No | No |
| CPU target from the scene histogram, stops | 8.907049 | 8.907049 |
| CPU target without histogram bins, stops | 8.907252 | 8.907252 |
| Applied exposure minus unbinned CPU target, stops | -0.330376 | 0.001605 |

Every one of the 32 saved GPU histograms equals its independently reconstructed CPU histogram exactly. The baseline uses 256 bins with eight bins per stop, beginning at -16 stops of pre-exposed luminance. At the observed exposure, 610 scene samples reach that lower limit and lose their individual luminances. The candidate uses 2,304 bins, also eight per stop, beginning at -160 stops of scene luminance. Its histogram retains those samples.

The baseline's `meter.glsl` contains these exact lines:

```glsl
    float stops = log2(max(y, exp2(LowestStops)));
    return uint(clamp(floor((stops - LowestStops) * BinsPerStop), 0.0, float(HistogramBins - 1u)));
```

where

- `y` is the surface sample's scene luminance multiplied by 2 to the power of its producing exposure. It is a pre-exposed numeric value.
- `stops` is the base-two logarithm of that value, after the lower limit applies.
- `LowestStops` is -16, the lowest logarithmic luminance with a separate bin.
- `BinsPerStop` is eight, the number of histogram bins per exposure stop.
- `HistogramBins` is 256, the number of counters in the baseline histogram.
- `exp2` raises two to its argument, `max` chooses the larger argument, and `log2` computes a base-two logarithm.
- `floor` rounds down, and `clamp` bounds the bin index between zero and 255.
- `float` converts the upper bound to floating point, and `uint` converts the result to an unsigned integer index.

The two pins' HDR image bytes match for both resolutions and all three exposure controls: automatic, fixed 0 stops and fixed 10 stops. Fixed 0 and fixed 10 also match each other. Automatic FSR stills and fixed FSR stills use different draw counts and jitter positions, so their images are not treated as identical controls. Native images match across all three controls.

A still capture draws twice under automatic exposure. Its first draw adapts exposure, and its shown draw adapts by zero seconds. The applied exposure therefore belongs to the first draw's histogram. The readback histogram belongs to the shown draw at a different jitter position. Their small target difference is below 1/16 stop. The analysis preserves both values rather than equating these draw boundaries.

## Active size, jitter and settling

The scene image under fixed FSR is 96×54 pixels, and its float readback contains exactly 96×54×4 values. Its distance image contains 96×54 values. The 160×90 image is the output image. Native scene and output images are both 160×90. No unused 160×90 scene region enters the CPU or GPU histogram.

The initial probe metadata incorrectly labeled the 160×90 output dimensions as allocation dimensions. Those raw metadata bytes remain in the archive. The analysis corrects that label from the complete image readback sizes and the renderer's recorded active size. The follow-up probe records output size and readback count separately.

FSR stills end at jitter (-0.25, 0.16666669) pixels in both pins. Continuous frames advance through the same jitter sequence. After the 65 still captures, the probe runs another 90 continuous frames, with one draw per frame and a simulation step of 1/60 second.

| Exposure over the final 24 continuous frames, stops | Minimum | Maximum |
|---|---:|---:|
| Baseline FSR | 8.576642 | 8.576704 |
| Candidate FSR | 8.907294 | 8.907549 |
| Baseline native | 10 | 10 |
| Candidate native | 10 | 10 |

Additional settling does not remove the FSR difference. Native reaches the +10-stop limit in both pins, which hides the histogram difference there. The saved Linux observations show the same pattern. Their exact raw HDR images and histogram counts were not returned, so the count-level diagnosis comes from the local run on the same source pins.

## What lights the extreme fixture

The nominal direct irradiance is 10^-12 W/m², with compensation of -13 stops. That number does not describe all scene light. The default scene environment map and light volume remain enabled when ambient intensity is zero.

With those defaults, the extreme fixture has scene RGB values above zero even though its direct-light and emission images are zero. Disabling the environment map, light volume and screen tracing makes the extreme fixture's scene RGB exactly zero. At an ordinary direct irradiance of 0.001 W/m², the same isolated setup produces positive scene RGB. Its HDR image equals direct diffuse plus direct specular plus emission exactly, on both pins and in both modes. All eight isolation cases emit zero Vulkan validation messages.

The extreme fixture therefore measures a dark background signal outside the baseline histogram's useful range. Equality with that clipped baseline is an invalid physical target for this case. The candidate agrees with the independent scene target within the unchanged tolerance. No production fix follows from this investigation. The parent still owns acceptance of the revised correctness interpretation.

## A bounded pristine timing proposal

The [proposal bundle](bounded-pristine-proposal/config.json) is inert. Its approval flags are false, its authorized submission count is zero, and its worker refuses execution before creating output. The [refusal check](bounded-pristine-proposal/inert-gate.json) records that gate. There is no new paid launch command.

Two designs were considered:

| Design | Process and reference lifetime | Cost and evidence |
|---|---|---|
| A: fresh process for each tree visit (proposed) | Four processes in baseline/candidate, then candidate/baseline order. Each process creates its own real cold references. | Repeats renderer and reference setup. Preserves the existing fixture's process boundaries and needs no cross-process reference store. Locally tested. |
| B: one persistent process with reusable cold references | One process switches between separately loaded engine versions and retains references across visits. | Could reduce startup and reference work. Needs an unbuilt version-isolation and lifetime mechanism, with more state that could contaminate a comparison. |

Example A runs a fresh baseline process, a fresh candidate process, a fresh candidate process, then a fresh baseline process. Example B loads both engines once and switches the active engine for those four visits.

Both designs require the following:

- The same worker, pinned assets, GPU settings and source commits for every paired visit.
- Independent physical correctness checks with the unchanged 1/16-stop and two-code RGB tolerances.
- A separate cut-observation lane and ordinary lane, with their overhead stated.
- Separate records for retained validating diagnostics and new pristine timing.

The supplied bundle proposes A for the parent's decision. It changes the measurement fixture and worker only. It keeps both engine source pins unchanged.

### Scope and local verification

Each pristine process runs native and FSR modes. Each mode observes the outward cut at tick 1417 and the return cut at tick 1462. It then runs 120 ordinary frames, discards 24 warmup frames, and retains 96 wall-time samples. The delayed GPU results supply 95 samples whose frame identities lie inside that wall interval. The final wall frame's GPU result is unavailable.

Each timed cut observes its first presentation only. A correct first presentation has the same first and first-correct latency. An incorrect first presentation is censored after zero followup frames. It supplies no recovery-duration estimate. The prior outside-cut observations through 90 followup frames remain separate correctness evidence. This proposal does not repeat those recovery loops in each timing round.

A presentation ends when the offscreen Vulkan submission's fence completes. It includes neither compositor time nor display scanout. The cut lane copies eight observation bytes per presentation and requests pixel captures. Its reported time includes those copies, and excludes PNG encoding. The ordinary lane requests no captures and copies zero observation bytes. Both lanes count every renderer draw, including hidden warmup draws.

The [local verification](bounded-pristine-proposal/local-verification.json) and [local harness archive](bounded-pristine-proposal/local-harness-evidence.tar.gz) retain the full-scope runs. The candidate passes both real first presentations in both modes. The baseline fails those first-presentation checks. Every tested cut draws and presents once. Both trees' independent real references agree within the shared tolerances. Every ordinary lane has 96 wall samples, the expected GPU frame identities, no capture requests and no observation-byte copies.

The full-scope fixture passed on both pins. The final fixture then added only derived active/output sizes to its sample payload. That exact fixture passed fresh minimal cut and ordinary smoke checks on both pins and modes. Its SHA-256 is `70cc38ab48e94728d9706609cd8d3f343eb2ab5ded8ad71f183f3e5dc11b106d`. Both source versions and hashes remain in the bundle. These local runs establish fixture behavior, not Linux speed.

### Time and cost ceiling

The [capacity model](bounded-pristine-proposal/capacity.json) uses the completed Linux job's measured phases. It uses no Mac frame times.

| Quantity | Seconds |
|---|---:|
| Prior canonical asset setup | 157.143902 |
| Two initial builds | 106.012165 |
| Two prior validating diagnostic processes, excluded from the new run | 233.561671 |
| Prior projected remaining work | 1612.708646 |
| Prior remaining command allowance | 317.072196 |
| New projected matched work, including 25% margin and final reserve | 1318.569819 |
| New total projection from measured setup/build phases | 1667.514903 |
| New total projection with setup/build hard caps | 1838.569819 |
| Proposed command / canonical execution caps | 1920 / 1980 |

The projection charges each real return prefix for 45 frames at the prior mode's maximum measured per-draw wall time. It keeps the diagnostic validation overhead in that conservative estimate. It removes repeated synthetic populations and their recovery loops from timing. It also removes both new diagnostic runs and both restoration rebuilds. Only two initial pristine builds remain. Each pristine process has a 340-second cap. Asset setup has a 220-second cap, and each initial build has a 140-second cap.

The canonical cost ceiling is **$0.328**: 1,980 execution seconds plus the existing 60-second worker-start and 10-second idle charges, at $0.00016 per second. This is a proposed ceiling, not approval or a completion guarantee. The worker can fail at a gate or timeout and still incur cost. No automatic retry is proposed.

### Gates and remaining decisions

The bundle retains the same GPU model and driver checks, minimum 2,048 MiB free-memory gate, source pins, allocator identity and NOAA mirror checks. It performs one canonical asset setup. Both trees share its pinned cooked content, with manifest `210510fe09f7195174b57d4491223a5721fc03c6a33325b4e24bbf0063319f75`. It verifies source and assets again at the end. It pins and checks each retained diagnostic artifact before using its phase costs. It measures GPU clocks during the reversed rounds and preserves the complete-phase gate. Compressed rows have a 6.5 MB gate within the existing 8 MB handler result allowance. No new cache, infrastructure or uploaded asset is assumed.

The following remain unresolved:

1. The parent must accept the independent physical target for the extreme case. The earlier shared-reference failure stays in its historical record.
2. Linux validation of the return cut, ordinary synthetic sources, second extreme source, fixed-exposure control and continuous-camera control is still missing. The proposed timing run covers the return cut without validation, and does not claim to fill the other correctness gaps. The parent must decide whether separate local correctness evidence is sufficient or a later Linux correctness scope is required.
3. The conservative projection has not been tested by a completed pristine Linux visit. New spending needs approval for the exact bundle and ceiling. The earlier paid approval is exhausted.
4. Required CI, final-head CI metrics, readiness and merge remain with the parent. PR #32 stays draft and In Progress, with F1 resolved.

The original failed job remains **FAILED**, with canonical cost $0.09488624. No new paid job, reviewer, mutation run, full Rendering suite or game PNG capture ran during this local stage.

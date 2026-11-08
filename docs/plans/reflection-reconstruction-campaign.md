# Same-data reflection reconstruction campaign

## Selected H13 baseline and two follow-ups

On November 6, 2026, the user selected H13 as the common research baseline and requested two separate experiments. H13 has the lowest measured persistent mean-error RMS among the first-wave candidates. This does not establish a solved denoiser or a production default. The actual renderer images still contain prominent halo clumps. The selected implementation remains opt-in.

The parent integrated H13's four-file source change into this research branch. All 37 files in H13's sealed source archive matched the integrated source before these planning notes. A copied, hash-verified H13 Release runtime passed 136 focused checks, including existing pure comparison, geometry and metadata checks. Integration ran zero compilations, captures or reference reports. It did not rebuild parent binary directories because old sealed reports link to them. Checked/full-suite validation, coverage and mutations remain prerequisites for any later production merge. Research checkpoint commits bypass the broad commit hook for this reason; they are not production-validation receipts. The unrelated, dirty main checkout remains untouched.

| Experiment | Change under test | What remains fixed |
| --- | --- | --- |
| A, tiunr-m0uo | Distribute reflection ray samples across pixels and advancing frames, with a literature-backed sequence at the same configured ray budget | H13 reconstruction, material response, history policy, footprint and ray-reuse policy |
| B, tiunr-463x | Retain per-pixel temporal fluctuation statistics of accepted raw samples and use them to guide existing reconstruction | Ray generation and budget, material response, geometry validity, and H13 except for the declared variance guidance |

Both agents start from the same committed H13 baseline. They do not combine A and B. B does not add temporal radiance accumulation: fast and slow histories, reprojection and short-history repair already exist. The fast-history neighborhood spread and H07's spatial residual proxy are not per-pixel temporal variance. B extends the existing temporal owner rather than adding another radiance accumulator. Correlated frames do not give independent-sample confidence. A persistent wrong clump can have low temporal variance.

A changes ray samples, so its unfiltered arrays need not match H13. B can change stored estimates and therefore later fast-history source feedback. Neither may claim unchanged arrays without checking them. Camera, geometry, materials, emitters, exposure, initial conditions and capture lengths remain fixed. The readonly probe's before/after snapshot checks still apply within each draw.

Each agent studies primary sources and their input, hardware and companion-system assumptions before implementation. Each obtains genuine assertion failures before changing behavior. Setup failures consume the compile budget but are not behavioral failures. A has at most three Rendering, two tool and four test compilations; B has at most three Rendering, three tool and four test compilations. Neither builds a baseline tool or dependencies, restores packages, runs old quadrature/full suites, tunes against the reference, or starts children. Each has at most two original-fixture capture attempts, one smooth and one rough, and one saved-data report command. Corrections stay inside those totals. A stopped experiment returns evidence, not substituted images.

Both reports compare actual draw 95, separate 32-frame means, temporal noise RMS, persistent mean-error RMS, drift, full-crop total/missing/extra reference fractions, fixed-band loss, row coverage/status and crop-limited width. They retain the fixed center-2048 reference and saved-reference sensitivity checks. They use the existing measurement owner, not another totals formula. No fitted display gain, candidate radiance normalization, weighted score, quality threshold, confidence interval or convergence claim is added. The live-feedback versus one-bounce reference mismatch remains explicit.

Integration, source archives, the corrected shared Release bootstrap and cleanup receipts are under `/private/tmp/tertius-reflection-hypothesis-campaign-20261106/parent-inspections/h13-integration-20261106`. All 18 completed campaign worktrees are archived before removal. Minimal readonly source-path aliases can remain to keep old absolute-path hash manifests usable; these aliases are not Git worktrees. Unrelated worktrees and original evidence remain untouched. Ticket tiunr-k2wn owns integration and cleanup. The parent owns inspection and any later promotion.

### First A/B attempts: no valid quality comparison

Both attempts ended and released their owned commands. Neither measures the new design's native image quality. The selected H13 baseline remains unchanged.

| Design | Verified test evidence | Native measurement outcome |
| --- | --- | --- |
| A, tiunr-m0uo | Four baseline assertion failures; final182/182 focused Release checks pass | Both capture attempts loaded H13 instead of A. The frozen tool entry was symlinked into H13, so the host selected H13's application directory. Both host traces and engine hashes establish this. The192 draws and one metrics command consumed their allocation, but produce zero valid A captures or quality metrics. |
| B, tiunr-463x | Six baseline assertion failures; attempted candidate83/147, with64 failures containing image-format validation errors | New RGBA32F statistic declarations receive RGBA16F fallback images when disabled. The off path is invalid. Rendering compile attempts3/3 are consumed; no native capture or metrics command ran. |

The A fault repeats the documented requirement to copy the executable entry locally. It is not an algorithmic null, and these duplicate H13 images are not new A results. Follow-up tiunr-oi26 records local-entry and actual-loaded-engine admission before any fresh capture allocation. Follow-up tiunr-d3la records B's disabled-resource binding defect. Neither tracking ticket authorizes a retry. B's missing red-runtime binary identity and unestablished statistic-to-probe consumption remain disclosed.

### Authorized A/B repairs

The user then requested two new repair agents and new renders. Each repair starts in a new T3 worktree from H13 commit `37e047c88e6bcc086174854912bd57107f948315` and imports only its own preserved candidate. The old candidate worktrees and evidence remain readonly inputs. The repairs do not combine A and B or tune either algorithm against the reference.

| Repair | Required proof before capture | Fresh execution bounds |
| --- | --- | --- |
| A, tiunr-oi26 | Reproduce the old entry's loaded-engine identity failure without rendering, then physically copy the entry and prove the intended tested A engine loads | Zero compilations; one existing focused-test replay; two original-fixture capture attempts; one saved-data report |
| B, tiunr-d3la | Reproduce the incompatible image binding, repair format compatibility, pass focused tests, and prove the temporal statistic reaches the measured reconstruction | At most three Rendering, three tool and four affected-test compilations; five focused test executions; two original-fixture capture attempts; one saved-data report |

These are fresh bounded continuations, not resets of historical counts. The repair brief and launch receipts are under `/private/tmp/tertius-reflection-hypothesis-campaign-20261106/parent-inspections/ab-repair-launch-20261106-v2`. All heavy phases use Nix and the existing serialized GPU queue, with fresh disk, memory and load guards. Both repairs require a physical local executable entry, actual loaded-engine admission and preserved host traces. The parent owns final inspection, integration and ticket closure.

### Verified A capture repair

The A repair loads the exact tested A engine in both new captures. The physical tool entry and host traces identify engine `dbc04d91`, rather than H13. All 60 engine resources match the intended candidate during non-rendering admission. The parent verified all 429 artifact entries, the linked-target inventory, runtime and report inputs, and replay XML with 182 passed cases and no failures, errors or skips. All four new preview hashes differ from H13.

| Material | Temporal noise RMS, H13 to A | Persistent mean-error RMS, H13 to A | Fixed outer-band missing/reference, H13 to A |
| --- | --- | --- | --- |
| Smooth | 13.34637 to 11.80329, 11.56% lower | 52.07977 to 51.67748, 0.77% lower | 3.79723% to 3.62979% |
| Rough | 21.14352 to 20.54499, 2.83% lower | 30.19907 to 33.55702, 11.12% higher | 5.55257% to 11.15734% |

RMS values use W/(m² sr). Temporal noise measures per-pixel variation over the 32 captured frames. Persistent mean error compares the separate 32-frame mean with the fixed reference. The outer-band denominator is reference radiance inside the fixed band, not the whole crop. Outer bands use 141 valid rows and 19 invalid rows; they cover 1.34152% of smooth and 13.95262% of rough whole-crop reference radiance. Full-crop totals retain every pixel.

This continuation adds zero compilations, one 182-case executable replay, two non-rendering admissions, two valid captures with 192 draws including 64 measured draws, and one saved-data metrics command. Earlier invalid capture attempts and all counts remain preserved. The actual rough image and separate mean still show clumps. A is a measured tradeoff, not a replacement that preserves rough reference accuracy. Its digitally shifted Sobol sequence is not spatiotemporal blue noise; the interaction with rotating 2x2 ray reuse limits per-pixel coverage claims. Static correlated frames and live-feedback versus one-bounce-reference differences remain limits.

Evidence is under `C/A-h13-capture-repair-v2`; parent verification is under `C/parent-inspections/A-h13-capture-repair-v2`, where C is `/private/tmp/tertius-reflection-hypothesis-campaign-20261106`. The manifest SHA256 is `ab6cbf1db1bbe7cbe6d92124f894d806aabbf5925cd9f74492e2105ea5be375d`. Heavy commands are released. H13 remains unchanged; neither A source integration nor ticket closure occurs here.

### B repair stops before native measurement

The B repair replaces incompatible half-precision fallback views with a 1x1 RGBA32F storage image. It leaves the temporal-statistic estimator and H13 reconstruction equations unchanged. The final focused run no longer reports the original format mismatch, but remains invalid: 109 of 148 cases pass, 39 fail, and none are skipped. The parent verified the XML and all 575 artifact identities plus 208 linked entries.

Of the 39 remaining failures, 37 report read-after-write synchronization hazards between the synthetic pulse and the actual comparison probe's fallback-image use. Two resize cases report invalid or destroyed image views at signal binding 4. The resize lifetime cause remains unresolved. The actual embedded-probe test that changes only the variance input passes, as do the accepted-sample statistic cases. Those passes establish a synthetic consuming path, not a valid native capture or a quality improvement.

The continuation uses three Rendering compilation attempts, two tool compilations, three test compilations, four executable runs and both correction rounds. It stops at the Rendering ceiling with zero capture-admission commands, native captures, renderer frames or metrics commands. All B image and noise/accuracy fields remain unavailable. No old H13 image substitutes for a missing B image. The parent records the remaining GPU-resource work in a follow-up ticket without authorizing a retry or reserving capacity.

Evidence is under `C/B-h13-binding-repair-v2`; parent verification is under `C/parent-inspections/B-h13-binding-repair-v2`. The manifest SHA256 is `5ce330fc1d696942a851fd6d81694aab886d9b6baffc28cdde0e423b1edcc84d`. Heavy work is released. The missing historical red-observer DLL identity and targeted-red dependency differences remain disclosed. H13 remains unchanged, and all relevant tickets remain open for parent disposition.

The user then explicitly directed the existing B agent to keep working until fixed. Ticket tiunr-pzhn owns this continuation in the same worktree. The previous artificial compilation, correction and capture-attempt ceilings no longer apply. Historical evidence and cumulative counts remain unchanged. The agent must continue through valid GPU resource ordering/lifetime, full focused green, actual loaded-candidate admission, both material captures and the existing metrics report. Nix, the unchanged serialized GPU queue, live resource guards, fixed experiment semantics and immutable failed attempts still apply. No extra agent, algorithm retuning, capacity reservation or production integration is authorized.

Parent inspection receipts under `C/parent-inspections/A-h13-sample-distribution-v1` and `C/parent-inspections/B-h13-temporal-variance-v1` verify353 and44 sealed entries respectively. Here C is `/private/tmp/tertius-reflection-hypothesis-campaign-20261106`. No parent build, test, capture, metric rerun, new agent or candidate integration occurred during inspection. No conclusion about A or B's noise-versus-accuracy tradeoff follows from these failed attempts.

### Verified B resource repair and measured tradeoff

The continuation under tiunr-pzhn completes B's resource repair. All 148 focused cases pass without skips. The corrections order the synthetic pulse's bound statistic image and recreate the custom test pass's sample image after resize. The engine and estimator remain unchanged from B's previous repair. Both native captures have zero Vulkan validation messages through renderer disposal. The actual embedded-probe variance-input test passes. Admission identifies the intended local engine and all 60 engine shader modules.

Parent inspection verifies 444 sealed entries, 156 links, 76 runtime identities, 93 report-input identities and seven compressed tables. No parent build, test, capture or metrics rerun occurs. Evidence is `C/B-h13-resource-repair-v3`. The parent receipt and labeled H13/B images are under `C/parent-inspections/B-h13-resource-repair-v3`.

| Rough observable | H13 | B |
| --- | ---: | ---: |
| Temporal noise RMS, W/(m² sr) | 21.143515 | 20.494741 |
| Persistent 32-frame mean-reference error RMS, W/(m² sr) | 30.199067 | 30.756077 |
| Missing/reference, % | 4.375675 | 4.196462 |
| Extra/reference, % | 8.746474 | 9.211141 |
| Fixed outer-band missing/band-reference, % | 5.552570 | 4.628890 |
| Column-mean 5–95% width within the crop, native pixels | 25.243299 | 25.608837 |

Temporal noise measures variation around each pixel's 32-frame mean. Persistent error measures that mean's difference from the fixed reference. B reduces rough temporal noise by 3.0684%, but increases persistent error by 1.8445%. Reference width is 24.471147 native pixels. B loses less light, but adds more excess light and widens the reflection. Smooth plate arrays match H13 across all 32 frames. Raw/emitter identities remain separate. The rough images differ, but their prominent clumps remain. This does not meet the user's accuracy-preserving improvement criterion. H13 remains selected.

The continuation uses zero Rendering, one tool and two test compilations. Cumulative compilations are 6/4/7. All 620 test cases remain recorded as 448 passes and 172 historical failures. Both captures pass, with 192 draws and 64 measured frames. Two metrics commands remain preserved, including the first input-staging failure. All owned commands are released. No integration, production acceptance, coverage, mutation run or ticket closure occurs. Static correlated frames and the live-feedback/one-bounce-reference mismatch still limit interpretation.

Date: November 6, 2026. Campaign ticket: tiunr-lmmx.

## Purpose

The user requested many independent, medium-effort experiments, followed by new
hypotheses derived from their results. Each experiment gets its own T3 worktree.
The first wave contains 16 hypotheses. A second wave can contain at most four
new hypotheses that explain measured failures or combine complementary findings.
This is a bounded investigation, not an unlimited parameter search.

The question is whether better reconstruction can reduce noise without losing
the outer reflected light or widening the reflected-light distribution.
Noise and accuracy stay separate. There is no weighted score or acceptance gate.

All experiments start at commit
96db37df965c7b35dae499a2f34410d2c3b35011 plus the same frozen dirty-source overlay.
The parent checkout remains the owner of that overlay. No experiment writes it.
No experiment commits, merges, restores packages, builds dependencies, changes
the reference, or launches another agent. Worktrees are created by T3 only.

## Existing evidence and available data

The completed distance-history experiment is
/private/tmp/tertius-unity-noise-research-20261105/live-reference-comparison/reflection-distance-history-recovery-20261106-1520.
Its 323-file manifest is
ec4225f12794a174b1c50359809bd68e5f93cd6c211b02b4d71d6cdd181a2ad9.
The frozen tool SHA256 is
275fd58cc7ca2cf36c0e32585fe51cc2df426c82c3587d71d2c64fd5256eaf20.
The completed distance-history Rendering assembly SHA256 is
347de2fc91aa276dc48a137324e0af911fa6fec7df34dd989ae4bf20cca29fef.

The final reflection-sharing shader reads outgoing or material-demodulated slow
specular RGB, slow-history length, a hit-distance first moment and valid-hit
support mass, current hit data, decoded material, normals, receiver depth,
reconstructed receiver position, view direction and camera parameters.
It does not have an independent radiance variance or a second independent noisy
estimate. A spatial residual is not a measured temporal variance.

The guide first moment has units of meters. Its support mass is dimensionless.
Their ratio estimates the distance of supported hits. Neither the support mass
nor the history-weight product is a probability or an error bound.

The reference is one-bounce outgoing visible-plate specular radiance in
W/(m^2 sr). The live source includes fast-history feedback. Compare the whole
pipeline without assigning every reference discrepancy to the new filter.
The original 26 analytic position failures remain failed diagnostics.

## Primary sources and applicability

NRD is an engine library for low-sample ray-traced signals. REBLUR uses hit
distance, roughness, lobe geometry, demodulation and history. RELAX also uses
temporal moments and multiple spatial passes. Our final shader can test selected
spatial mechanisms, but cannot claim to reproduce either complete denoiser.
Pinned NRD revision: 1b6c1865cb59cd14081f2e2eecd02e37b8ca6853.

- https://github.com/NVIDIA-RTX/NRD/blob/1b6c1865cb59cd14081f2e2eecd02e37b8ca6853/README.md
- https://github.com/NVIDIA-RTX/NRD/blob/1b6c1865cb59cd14081f2e2eecd02e37b8ca6853/Shaders/REBLUR_Common_SpatialFilter.hlsli
- https://github.com/NVIDIA-RTX/NRD/blob/1b6c1865cb59cd14081f2e2eecd02e37b8ca6853/Shaders/REBLUR_PrePass.cs.hlsl
- https://github.com/NVIDIA-RTX/NRD/blob/1b6c1865cb59cd14081f2e2eecd02e37b8ca6853/Shaders/RELAX_Atrous.cs.hlsl

FidelityFX SDK 1.1.4 contains open reflection-prefilter code. The current main
branch is not an interchangeable source for this older implementation.
Pinned 1.1.4 revision: c6efa6bf7f2027b3ec94f28578bb5965eabb9e55.
Its variance and temporal inputs exceed our spatial inputs. Preserve that gap.
Do not port firefly suppression, radiance clipping or history changes.

- https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/c6efa6bf7f2027b3ec94f28578bb5965eabb9e55/sdk/include/FidelityFX/gpu/denoiser/ffx_denoiser_reflections_prefilter.h
- https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/c6efa6bf7f2027b3ec94f28578bb5965eabb9e55/sdk/include/FidelityFX/gpu/denoiser/ffx_denoiser_reflections_common.h

Additional primary papers and author pages:

- Heitz, Sampling the GGX Distribution of Visible Normals, 2018:
  https://jcgt.org/published/0007/04/01/
- Schied et al., Spatiotemporal Variance-Guided Filtering, 2017:
  https://research.nvidia.com/publication/2017-07_spatiotemporal-variance-guided-filtering-real-time-reconstruction-path-traced
- Rousselle et al., Adaptive Rendering with Non-Local Means Filtering, 2012:
  https://cseweb.ucsd.edu/~viscomp/classes/cse274/wi18/papers/a195-rousselle.pdf
- Rousselle et al., Robust Denoising using Feature and Color Information, 2013:
  https://www.cs.umd.edu/~zwicker/publications/RDFC-CGF13.pdf
- Moon et al., Adaptive Rendering Based on Weighted Local Regression, 2014:
  https://sgga.kaist.ac.kr/WLR/
- Bitterli et al., Nonlinearly Weighted First-order Regression for Denoising
  Monte Carlo Renderings, 2016, including implementation:
  https://benedikt-bitterli.me/nfor/
- Koskela et al., Blockwise Multi-Order Feature Regression, 2019:
  https://webpages.tuni.fi/vga/publications/Blockwise_Multi-Order_Feature_Regression_for_Real-Time_Path_Tracing_Reconstruction.html
- He et al., Guided Image Filtering, 2010, including reference implementation:
  https://people.csail.mit.edu/kaiming/eccv10/index.html
- Perona and Malik, Scale-Space and Edge Detection Using Anisotropic Diffusion,
  1990: https://people.eecs.berkeley.edu/~malik/papers/MP-aniso.pdf
- Weickert, Coherence-Enhancing Diffusion Filtering, 1999:
  https://www.mia.uni-saarland.de/weickert/Abstracts/ced.html
  and https://madoc.bib.uni-mannheim.de/1837/1/2000_04.pdf

Regression and non-local methods often target offline Monte Carlo images with
independent buffers, more samples and expensive feature processing. Guided and
diffusion methods target general images, not unbiased radiance estimation.
Each experiment must explain that mismatch. None promises realtime cost.
Count neighborhood reads and solve sizes. Use bounded local neighborhoods, not
an image-wide solver. Prefer at most 49 radiance taps or 81 patch-component reads
per output. State exceptions before building. Do not import a complete library.

## First-wave hypotheses

These are mechanisms to investigate, not claims that they work. Researchers must
read their primary source and relevant engine code before implementation.

| ID | Mechanism and distinguishing question | Main source |
| --- | --- | --- |
| H01 | Project a fixed GGX angular quantile using hit distance and camera footprint. Does a physical nonlinear radius work better than the current cone approximation? | Heitz and REBLUR |
| H02 | Use a projected anisotropic lobe ellipse rather than a circular receiver radius. Can smoothing follow the lobe without broadening across it? | REBLUR kernel basis |
| H03 | Weight virtual reflected-hit positions from receiver, reflected direction and distance history. Does reflected geometry distinguish illumination structure on one flat plate? | REBLUR virtual geometry |
| H04 | Replace the current dominant-direction rational weight with an explicit symmetric reflected-lobe overlap weight. Can angular compatibility reject the wrong neighbors? | Heitz and NRD |
| H05 | Use valid distance support to admit a footprint, without multiplying that same support into the light exchange again. Is weak exchange, rather than radius, the main limitation? | NRD hit-distance reconstruction, explicitly a local ablation |
| H06 | Filter material-demodulated incoming specular, then apply the center material factor once. Does outgoing-radiance mixing confuse material response with illumination? | NRD demodulation |
| H07 | Use feature-conditioned local regression residuals to adapt the smoothing bandwidth. Does local noise evidence avoid blurring a resolved light edge? This is not full SVGF. | SVGF, feature/color denoising |
| H08 | Adapt the FidelityFX edge-aware spatial prefilter without its firefly removal or unavailable temporal variance. Does its weighting help with the same local data? | FidelityFX 1.1.4 |
| H09 | Compare small patches rather than individual brightness samples to choose donors. Can repeated local structure distinguish signal from speckles? | Non-local means |
| H10 | Fit a weighted first-order radiance field over local receiver coordinates. Can a sloped field preserve the edge better than a constant neighborhood average? | Weighted local regression |
| H11 | Weight a first-order regression with nonlinear patch similarity. Can regression and donor selection cooperate? | NFOR |
| H12 | Compare constant, linear and quadratic feature regression with a predeclared local rule. Is a higher-order reflected-light model needed? This is not full BMFR. | BMFR and weighted local regression |
| H13 | Use a local affine guided reconstruction, with a geometry-safe smoothed guide. Does guide structure protect the reflected-light edge? | Guided Image Filtering |
| H14 | Use symmetric, edge-stopping conservative light exchange rather than uniform diffusion. Can it reduce variation without exporting light across a resolved light boundary? | Perona-Malik |
| H15 | Debias patch-distance weights with an explicit local noise estimate. Does raw patch matching reject useful donors because both patches are noisy? Compare with H09. | Non-local means and feature/color denoising |
| H16 | Estimate the local radiance structure tensor and smooth preferentially along an edge, not across it. Does image-derived orientation outperform only BRDF-derived orientation? | Weickert |

## Frozen experimental contract

Each hypothesis replaces only the opt-in final specular reconstruction and its
focused tests. Keep default-off behavior. Keep receiver geometry safeguards,
material identity, source lighting, histories, exposure, ray sequence, guide
accumulation and material equations unchanged. New scalar parameters must come
from the source or a declared mathematical convention before reference viewing.
No parameter sweeps, fitted gains, radius tuning against the reference, pixel
coordinate special cases or fixture-specific emitter knowledge.

Prefer changes confined to reflection_sharing_prototype.glsl and a new test
class. No new persistent images or pipeline passes in this first wave. If a full
method needs those, test an honest spatial adaptation or report the API/data gap.
Do not disguise an adaptation as the complete published algorithm.

An immutable bootstrap bundle preserves the full dirty overlay and existing
Release build inputs. Extract it only inside the assigned child worktree.
Never symlink writable bin/obj directories into the parent checkout. Do not
restore packages or rebuild production dependencies. Builds use Nix,
Release, --no-restore and BuildProjectReferences=false. The comparison tool must
be compiled against the candidate's shared reconstruction include, then frozen.
Its entry DLL must be a local copy, not a symlink, so its runtime directory
selects the intended Rendering assembly. See the setup recovery below.

Read software-design, test-driven-development, STYLE.md and tests/STYLE.md.
Write meaningful focused behavior tests first and observe assertion reds using
the real shader path. Missing methods, compilation failures and forced throws
are not reds. Preserve common invariants: disabled identity, finite output,
nonnegative output, constant-field preservation, no cross-surface exchange,
near-mirror protection and unchanged live input images.
Document any intentionally replaced old assertion, such as exact conservative
exchange or the old support-strength rule. Do not silently delete a failing test.
Do not require old algorithm-specific assertions of a different algorithm.
At most three Rendering compilations and three affected-test compile cycles per
experiment. Stop with evidence if that budget or a substantive API gap blocks it.
No expensive quadrature tests, suites, coverage or mutations.

Freeze source, final small assemblies and shader identities before capture.
Only the already established four shader modules can differ from the uncapped
engine: generic temporal, generic spatial and the two composite modules. The
temporal/spatial modules must equal the distance-history baseline. Only the two
composite modules can change between campaign candidates. Only the tool's
reconstruction fragment module may differ from a same-worktree baseline tool
build; other probe modules and all tool C# source remain unchanged. Preserve all
controls and historical failure evidence.

### Shared setup recovery, November 6

The original bootstrap had stale Rendering compiler metadata and omitted
Physics/Simulation reference and output paths. The first v2 setup recovery
resolved the C# API failures, then failed copying the two missing output DLLs.
These setup failures are not behavioral reds or hypothesis failures. Every
original attempt and seal remains immutable; recovery counts are additional,
not reset counts.

The original frozen-tool requirement was also incorrect. ReflectionComparisonProbe
loads shaders from its own tool assembly. Both the pulse tests and live captures
use that probe, whose reflection_comparison_shared.frag includes the engine's
reflection_sharing_prototype.glsl at tool build time. Engine-only changes cannot
replace the probe's embedded filter. No candidate was captured under that rule.

The versioned reference overlay v3 adds the existing frozen Physics/Simulation
implementation bytes at their missing output paths. No dependency is compiled.
Before resuming siblings, H02 must establish one complete vertical slice using
its existing worktree. Its separate recovery permits two tool compilations
(baseline for executable assertion reds, then candidate), one candidate
Rendering compilation and two affected-test compilations. The setup attempts
remain counted separately. An unexpected build/setup failure stops this
recovery; no automatic extra compile is allowed.

The baseline and final tool builds use unchanged tool C# source and the ordinary
project route, without restore or reference builds. Archive the baseline probe
SPIR-V and compare every module with the final tool. Only
reflection_comparison_shared.frag.spv may change, and it must change for a claimed
filter change. Verify that the exact final SPIR-V occurs once in the final tool
assembly and that the final tool assembly is copied to both test and capture
runtimes. Tests and live captures then exercise the same frozen probe and shared
engine include. Record actual loaded assembly paths, runtime hashes and shader
hashes before capture. Never substitute a baseline tool after candidate green.

The original immutable protocol and brief remain evidence. The external
SETUP-AND-MEASUREMENT-v3.md addendum overrides only the faulty setup, tool-build
and explicitly itemized recovery budgets. The fixture, reference, capture caps,
source/input compatibility checks and all metric definitions remain unchanged.

H02 completed this vertical slice after a separately authorized tests-only
fixture repair: disabled guide bindings alias slow light, so synthetic guide
writes must respect the same sharing specialization as the renderer. The
candidate tool/engine remained frozen. All130 focused checks and both original
captures passed, with baseline probe byte identity established. This validates
the measurement route, not the anisotropic algorithm: rough noise decreased but
the crop-limited width increased. Other hypotheses resume in their SAME existing
worktrees under external RECOVERY-BRIEF-v4.md, with explicit additional build
budgets and cumulative old counts. No setup failure is treated as an algorithm
failure. Candidate captures remain one attempt per original material.

Each hypothesis gets one smooth and one rough candidate capture, one attempt
each. Native 1280x720, zero jitter, original camera, geometry and 80x160 crop,
roughness 0.045/0.2, exposure -6.8, one initial reset, exactly 64 advancing warm-up
draws and 32 measured draws. Reuse the proven read-only geometry audit and
surrounding copy checks. No fresh OFF/control/reference capture or integration.
Compare decoded receivers, source and unfiltered values byte-for-byte with the
retained controls. Save evidence before rejection. A mismatch stops rendering.

Every heavy phase uses unmodified tools/gpu-lock.sh inside Nix. This serializes
heavy GPU/build work even while many agents research and edit in parallel.
At most one foreground heavy command per child. Check at least 5,242,880 KiB
available before queue, after wait and between phases. Do not alter the lock,
relax guards, delete other outputs or retry captures. Explicitly release when
owned commands finish. Cap retained evidence at 64 MiB per first-wave candidate,
excluding working build caches. Keep the full campaign evidence below 2 GiB.

## One measurement owner and required report

Reuse the frozen --sharing-metrics report and its ReferenceDifference mechanism.
Do not duplicate totals or change frozen reference bands, normalization, root
statuses, source masks or excluded-row coverage accounting. Every crop pixel
remains in full-crop totals. Reference center-2048 is the anchor. Existing
128/512 means and 2048 shifts remain reference uncertainty candidates.

Return JSON and a readable dense report with:

- Whole-crop radiance sum/reference, missing and extra radiance, mean and RMS
  error, local maxima and worst pixels, without fitted brightness correction.
- Fixed reference outer-band loss, band status, row counts and covered reference
  light. Preserve reference 10/50/90 percent crossings and reasons for ambiguity.
- Crop-limited central 5-95 percent width, peak and profile changes. Report a
  wider reflection even when noise improves.
- Temporal per-pixel sample SD and its image RMS, first-eight/last-eight drift,
  persistent 32-frame mean bias and hot-pixel tails. Frames are correlated.
- Actual draw-95 renderer crops and separate 32-frame means for both materials,
  with the same exposure/Reinhard/sRGB mapping as the controls and reference.
  Include compact spatial noise/error views and common-scale profiles if the
  retained report supports them. Never substitute a mean for an actual frame.
- Saved reference level/shift sensitivity through the same metric basis, source
  feedback limitation, static-scene-only limitation, tap counts and data gaps.
- Literal builds/tests/frames, source/assembly/shader/input hashes, surface and
  snapshot checks, failures, artifact seal and final process-release state.

Agents return only their result image paths, report paths, dense numerical
summary, methodological limits and patch/source identity. Do not write a long
conversational essay. The parent owns the common dashboard and synthesis.

The parent compares the tradeoff frontier, not one weighted winner. A candidate
is dominated when another has less noise and less error without a worse light
loss/width tradeoff. Keep the actual component metrics visible when discussing
that relation. Parent inspection is not production acceptance or evidence of
motion quality. A second wave needs an explicit new causal hypothesis from the
first-wave evidence and uses the same frozen protocol, not a tuning loop.

## Parent inspection: first four measured candidates

The parent inspected actual draw-95 images, separate 32-frame means, reference
images and temporal-noise images. All values below describe the rough material
(roughness 0.2). Noise is the image RMS of per-pixel sample standard deviations
over 32 correlated frames. Mean error is the RMS difference between the 32-frame
mean and the fixed reference. Both use W/(m² sr). Width is the crop-limited
central 5–95 percent span of the column-mean profile, in pixels. Missing and
extra fractions use the whole-crop reference radiance sum; outer missing uses
only the fixed reference outer-band radiance sum.

| Candidate | Noise RMS | Mean-error RMS | Width (px) | Missing / reference | Extra / reference | Outer missing / band reference |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Circular distance-history baseline | 29.25279 | 34.04023 | 25.54744 | 5.07600% | 10.48504% | 6.91877% |
| H02: projected ellipse | 25.65283 | 32.65625 | 25.59691 | 4.61913% | 10.02806% | 5.97036% |
| H03: estimated reflected-hit similarity | 30.21795 | 34.61313 | 25.51620 | 5.18323% | 10.59239% | 7.26405% |
| H04: GGX slope-overlap weights | 29.23561 | 34.03338 | 25.54784 | 5.07363% | 10.48267% | 6.91281% |
| H10: first-order local regression | 19.60782 | 31.12719 | 25.93801 | 3.67689% | 9.48143% | 2.95485% |

Reference width is 24.47115 pixels. The rough outer bands have 141 valid rows
and 19 invalid rows. The bands contain 13.95262% of whole-crop reference
radiance; row invalidity and reasons remain in every full report. Full-crop
totals still include all 12,800 pixels.

H10 reduces noise by 32.9711% and mean-error RMS by 8.5576% relative to circular,
but widens this profile by 0.39058 pixels and changes total/reference from
105.40904% to 105.80453%. Its lower outer-band deficit is not proof of a correct
halo boundary. Neither H10 nor H02 demonstrates lower noise without widening.
H03's small width reduction comes with higher noise and mean error. H04 is
near-null in this fixture. Speckles remain visible in actual frames and means.
There is no algorithm acceptance or common weighted quality score.

Parent verified the H03 and H10 seals (321 and 309 entries), final XML greens
(127 and 129), 44 exact saved input-file identities, 1,152 unchanged surrounding
snapshot pairs and raw=plate+emitter at all 1,638,400 retained scalar positions.
Their retained uncapped/circular metric values match H04 exactly. All smooth
plate frames equal circular. This does not imply whole-output identity: H10
records 10,048 differing emitter/raw scalars, with maximum absolute change
0.0600624681 W/(m² sr). Its baseline test-DLL hash was not retained before
overwrite; source, red XML/logs and baseline engine/tool/probe identities remain.

H08 stopped on an unresolved low-pulse uploaded-history expectation. A bounded
test-only continuation must measure genuine before/after raw history around
the probe before calling this a storage-fixture issue. H09 stopped on exact
no-donor identity: the candidate returns a recomputed material response instead
of the supplied outgoing value. Its bounded continuation corrects that identity
without changing patch weights, footprint or equality assertions. H11 remains
stopped on its genuine bright-side step-preservation failure; no capture or
native quality result exists. Earlier failed evidence remains immutable.

The parent receipt and continuation scope are retained in
`/private/tmp/tertius-reflection-hypothesis-campaign-20261106/parent-inspections/four-results/inspection.json`.
These are static measured comparisons, not motion/performance or production
acceptance. The original 26 analytic position failures, one-bounce-reference
versus live-feedback difference and limited-data adaptations remain explicit.

## Second wave: matched H13 controls

All sixteen first-wave reports have arrived. Fifteen have native measurements;
H11 remains stopped on a genuine intended assertion. H08's stored-history
mismatch was measured using genuine surrounding GPU copies: the bytes were
unchanged, but CPU and stored half-float representations differed. Its exactly
representable synthetic-fixture correction keeps strict equality; the renderer
algorithm did not change. H09's exact no-donor identity correction likewise
keeps its weights and strict equality assertion. Their failed stages remain.

H13 improves rough noise, persistent error, full-crop missing/extra radiance,
fixed outer-band deficit and crop-limited width compared with circular. Its
width is still 0.77215 pixels wider than reference, and speckles remain. Its
combined benefit does not establish which part causes it. Two matched controls
therefore isolate existing terms, without tuning a radius or coefficient:

| Hypothesis | Single changed part of H13 | Question |
| --- | --- | --- |
| H17, tiunr-nqhn | Use the normalized weighted radiance mean instead of the guide-affine prediction | Is ordinary normalized averaging sufficient, or does the guide-dependent term explain the width improvement? |
| H18, tiunr-lj7t | Use raw admitted radiance instead of each geometry-safe 3x3 smoothed guide | Does guide smoothing explain the benefit beyond affine fitting alone? |

Both retain H13's exact source, history, roughness/distance footprint, receiver
admissions, angular/support weights, final support blend and metric basis.
H18 also retains its exact affine regularization. No new signal, pass or image
is introduced. The generic guided-filter paper motivates the separation of
mean and guide-dependent terms, not a quality claim for correlated radiance.

Each gets its own T3-derived worktree and medium-effort agent. Reuse the frozen
H13 tool for behavior reds; do not rebuild a baseline. Normal new builds are
one Rendering, one TOOL and two affected-test compilations, against existing
references without restore/dependency builds. One bounded correction allowance
does not authorize tuning. Each may capture smooth/rough once with the unchanged
64+32 protocol, then one frozen metrics report. The exact source archive, setup,
identities, maxima and stop conditions are in external WAVE2-H13-BRIEF.md.

## Matched-control results and saved-evidence inspection

Both matched controls are complete. The parent verified their saved inputs,
frame accounting and final focused tests without another build, test execution,
capture or metrics command. The table uses rough visible-plate specular radiance.
Noise RMS is the image-wide root-mean-square of each pixel's temporal sample
standard deviation across 32 advancing frames. Persistent error RMS compares
their mean with the fixed center2048 reference. Both use W/(m² sr).

| Saved output | Noise RMS | Persistent error RMS | Crop-limited 5–95% width, px | Missing / whole reference, % | Extra / whole reference, % | Missing / fixed outer-band reference, % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Circular sharing | 29.252787 | 34.040226 | 25.547437 | 5.076003 | 10.485042 | 6.918772 |
| H13, smoothed guide with affine prediction | 21.143515 | 30.199067 | 25.243299 | 4.375675 | 8.746474 | 5.552570 |
| H17, normalized weighted mean | 20.522575 | 30.866478 | 25.621742 | 4.211484 | 9.236395 | 4.623232 |
| H18, raw guide with affine prediction | 30.955195 | 34.776546 | 25.483498 | 5.414057 | 10.507688 | 8.190046 |

A guide is the brightness signal used to fit the prediction. Relative to H13,
H17 removes only its guide-dependent fitted term. H18 removes only the smoothing
of that guide. These controls retain the sharing window, admitted samples,
receiver checks, weights and final support blend. H17 lowers measured noise and
outer-band deficit, but increases persistent error, extra light and width.
H18 loses most of H13's gains. The fitted term therefore limits broadening in
this fixture, while guide smoothing is necessary for the observed noise benefit
of this particular fit. Neither result establishes a production winner.

The reference width is 24.471147412039272 native pixels. All 12,800 pixels remain
in full-crop totals. Fixed outer bands cover 141 valid rows, with 19 invalid rows,
and contain 13.9526219% of whole-crop reference radiance. Width remains a
crop-limited shape measurement, not a physical reflection boundary. Actual
draw95 images and separate 32-frame means retain visible speckles. All smooth
plate-frame arrays match circular and H13 exactly; raw and emitter arrays are
reported separately and are not claimed identical.

H17 finishes with 129/129 focused checks. Its cumulative build counts are one
Rendering, one tool and three tests-only builds. The malformed queue launch and
four failed assertion executions remain recorded. The last correction changes
only the CPU expected history for its two new synthetic patterns; strict whole-
array equality remains. Parent archive comparison confirms no other source
change from v2. Each matched control captured two cases, 192 original-scene
draws including 64 measured draws, and ran one existing metrics command.

H18 finishes with 128/128 focused checks, one Rendering build, one tool build and
two tests-only builds. Its original sealing script mistook a directory link for
a file. The parent preserved that script and failure, and made a separate seal
over 326 file identities and 105 links. No original H18 file changed and no
measurement was repeated. Its original report still records the failed seal;
the external recovery receipt supplies the completed seal.

H08 and H09 are also parent-inspected. Their rough noise RMS is 31.030212 and
30.345300 respectively, versus circular 29.252787. Their persistent error RMS is
34.975462 and 34.531751, versus circular 34.040226. Neither improves those two
observables in this fixture. H09 retains its three disclosed design-specific
skips. H08's two 6,144-byte raw history proofs were checked byte-for-byte, with
all 6,144 scalar records checked against their raw payloads. Its old three
CPU-expected storage mismatches remain recorded; the corrected fixture retains
strict equality.

All 15 measured first-wave candidates and both matched controls now have parent
inspection. H11 remains stopped without native quality measurements. Durable
receipts and the combined comparison are under
`/private/tmp/tertius-reflection-hypothesis-campaign-20261106/parent-inspections/`:

- `saved-results-v2/inspection.json` verifies the nine additional first-wave
  stages, H08, H09 and H18: 264 exact input-file comparisons, 6,912 saved
  surrounding snapshot hash pairs and 9,830,400 raw/plate/emitter scalar checks.
- `controls-v3/H17-inspection.json` verifies H17: 22 exact input files, 576 saved
  surrounding snapshot hash pairs and 819,200 raw/plate/emitter scalar checks.
- `H18-seal-v2/seal-receipt.json` records the external H18 seal, manifest SHA256
  `93a10ba505fdeaa6ba5b6d69ebbc9560685dcda73919218b67e7e4815ae36e12`.
- `controls-v3/comparison.html` shows existing actual frames, separate means,
  noise/error comparisons and the named observables for all 17 measured
  candidates. Its JSON/CSV values come from frozen report fields, not another
  image measurement or integration.

The parent additionally checked all 212 saved report-input hash receipts for
H08, H09, H17 and H18. All owned experiment commands are released. There is no
new child, future queue reservation, production import, commit or ticket closure.
Static correlated frames, live source feedback versus the one-bounce reference,
stale manifest algorithm prose and the original 26 failed position diagnostics
remain limits. The reference is not declared satisfactory and speckles are not
declared solved.

## Uncapped slow-history experiment (November 7, 2026)

Ticket `tiunr-ipmk` tests the user's proposal to remove the specular slow
history's age limit while retaining surface rejection, the short fast history,
clamping and the reduction of old-history influence after clamping. H13 stays
the spatial reconstruction baseline. This is a research experiment, not an
accepted renderer policy.

The causal comparison uses the same higher-precision slow-history storage and
the same fixed reflection-ray allocation for capped and uncapped accumulation.
An additional original-H13 control separates those supporting changes from the
effect of removing the age limit. Diffuse ray allocation, ray directions,
material response, source feedback and H13's spatial filter stay fixed.

Each native 1280 × 720 run first draws 1,024 advancing warm-up frames. Static
runs retain 128 scene-linear measured frames for noise and reference-error
measurements. The reference remains the saved one-bounce reference, so source
feedback is still a limit on interpretation. Actual frames and separate
temporal means must not be confused.

Two 60-frames-per-second videos compare capped and uncapped final renderer
output after the same warm-up. In one, the reflected emitter moves while the
receiver and camera remain fixed. In the other, the emitter switches off
without geometry movement. Both retain the initial stationary interval and
the following response. Events must not reset the renderer. A smaller emitter
brightness change is a further numeric response check. History-length
distributions establish whether the existing clamp still limits effective
accumulation even without an age limit.

Evidence lives under `/private/tmp/tertius-uncapped-history-20261107/`.
Build, test and capture failures remain recorded; there is no arbitrary
attempt ceiling. No acceptance, commit or integration follows merely from
successful execution.

### Measured uncapped-history outcome

All 16 captures completed with clean validation through disposal: 19,552 native
draws, 3,168 measured draws and 2,412 final-render PNGs. Every run used 1,024
advancing warm-up draws. The capped/uncapped source inputs and reflection-ray
counts match. Their dynamic pre-event radiance and history-age prefixes match
the static runs. The engine's focused suites pass 300/300 in Checked and 300/300
in Release. The seven saved-frame measurement tests pass.

Noise below is the image RMS of each pixel's temporal sample standard
deviation. Error is the RMS of the 128-frame temporal mean minus the saved
one-bounce reference. Both use outgoing visible-plate specular red radiance in
W/(m² sr), not display brightness or spatial speckle contrast.

| Material and control | Temporal noise RMS | Mean-reference error RMS |
|---|---:|---:|
| Rough original H13 | 24.446157 | 26.492000 |
| Rough matched capped | 17.741029 | 29.785164 |
| Rough matched uncapped | 5.528005 | 29.022363 |
| Smooth original H13 | 14.275161 | 51.604504 |
| Smooth matched capped | 14.283778 | 51.658591 |
| Smooth matched uncapped | 0.997452 | 51.440042 |

Removing the age limit reduces noise 68.84% for rough and 93.02% for smooth
against the matched capped control. Mean-reference error falls 2.56% and 0.42%,
respectively. Against original H13, rough noise falls 77.39% but error rises
9.55%. The full-rate-ray and precision changes must therefore remain separate
from the isolated age-limit result. Persistent spatial speckles remain.

Final uncapped median history age is 95.95 frame units on the rough plate and
1,152 on the smooth plate; the matched caps are 8.67 and 4. History age is an
accepted averaging weight, not an independent-sample count. Existing clamping
still shortens rough history.

After the emitter switches off, both controls remove 90% of aggregate receiver
light in 10 draws for rough and 9 for smooth, about 167 ms and 150 ms at 60 Hz.
The smaller 20% rough-emitter dimming change exposes the limitation: capped
completes 90% of the adjustment in 19 draws, while uncapped does not within 180
draws (3 seconds). Uncapped still retains 17.27% of the requested adjustment at
the end. The dim target is 80% of the preceding 60-frame mean, not an
independently rendered settled reference. No clamp or rejection tuning was
performed after observing this result.

The delivered `videos/moving-reflection.mp4` shows rough then smooth motion
(8 seconds). `videos/lighting-changes.mp4` shows rough off, smooth off, then
rough 20% dimming (12 seconds). Both compare matched capped on the left with
uncapped on the right, using real final-render frames and same-frame enlarged
crops. There is no temporal interpolation or image denoising. No moved-emitter
reference was rendered, so motion is not declared trail-free.

The full report, complete result JSON, actual-render PNG sheets, self-contained
comparison and video files are under the evidence root recorded above. The
initial failing-test DLL was overwritten; its XML, base revision and shader
identity proof remain, and the gap is disclosed. Float32 age stops advancing
by one at 16,777,216 accepted frames. Static correlated frames and live
source-history feedback versus the one-bounce reference remain limits. No
default-policy acceptance, commit, merge or ticket closure occurred.

### Accepted uncapped default

On November 7, 2026, the user accepted the tested uncapped mode as the next
default despite the measured illumination-response and rough-reference-error
tradeoffs. The first change only selects that existing implementation when
`TERTIUS_SPECULAR_HISTORY` is unset or empty. Its precise slow-history storage
and full-rate reflection allocation remain the same as the inspected experiment.
Fast history, surface rejection, reflected-location rejection, clamping and
history shortening remain unchanged. This does not accept an unmeasured new
rejection policy.

Explicit `original` and `precise-capped` remain historical comparison controls.
The history capture command now sets `original` explicitly for that control;
clearing the variable would select the new default and invalidate the control.
The sealed original experiment is not edited or relabeled. Default-adoption
evidence is separate, under
`/private/tmp/tertius-uncapped-default-20261107/`.

Behavior tests exercise both unset and empty settings through actual GPU
accumulation beyond the half-precision frame-count limit. Tests retain surface
rejection and fast-history clamping, explicit historical cap behavior, matching
storage formats, disabled descriptors, resize and the actual reconstruction
probe. Test shaders use the same history-format variants as the engine so their
probes do not assume historical half-precision storage. No assertion of
unchanged rejection is removed to hide an accumulation difference.

### Reference-ray rejection experiment

On November 7, 2026, the user selected the second reflected-source validation
design: recheck a matching reference ray. This is not the earlier spatial-variance
Design B. Ticket `tiunr-ax4c` owns the experiment. Its baseline is the accepted
precise/full-rate uncapped history, with existing rejection and H13 reconstruction.
Evidence lives under `/private/tmp/tertius-paired-ray-history-20261107/`.

The ordinary reflection estimator remains unchanged. An opt-in pass records a
fresh reference ray and replays the preceding frame's matched reference ray.
The comparison uses source direct light, emission, and existing closed-form
environment light, without noisy source temporal feedback or the screen-hit
fade. It does not add validation light to the estimator. Different independent
random directions do not constitute change evidence.

The model fits this renderer's low sample count and reuses its screen-space
walk and light evaluation. It does not require NRD, hardware ray tracing, or
another engine's scene representation. The cost does not inherit an external
denoiser's performance results: the implementation issues fresh-reference and
replay walks, and allocates 112 bytes per viewport pixel when enabled. It cannot
observe offscreen geometry unavailable to the existing walk.

`TERTIUS_REFLECTION_HISTORY_VALIDATION=paired-ray` enables the pass. Unset,
empty, and `off` disable it. The disabled path keeps its old image budget and
matches preserved baseline GPU readbacks in Checked and Release. Enabled mode
requires 22 screen-trace storage-image descriptors instead of 15. The seven
RGBA32F images consume 98.4375 MiB at 1280 by 720. The slow specular signal owns
their creation, resize, and disposal.

Matched evidence can reduce the retained age to the existing four-frame fast
cap before the ordinary sample blends. Existing surface checks, clamp,
anti-lag, reflection-distance guide, and H13 reconstruction remain unchanged.
The implementation's exact thresholds, reasons, failures, and GPU tests are
recorded in `engine/HANDOFF.md`. Native comparisons measure static quality,
motion, full switch-off, and 20% dimming after 1,024 warm-up draws. No default
acceptance, commit, merge, or ticket closure follows from green tests alone.

The native experiment completed on November 7, 2026. All 14 matched captures
pass Vulkan validation through disposal. They contain 17,248 renderer draws,
including 2,912 measured draws. A separate 128-draw smoke capture also passes.
The rough and smooth static outputs match the uncapped control across all 128
measured crop frames, including history age and unfiltered input. Both retained
full-render PNGs also match. The control matches the preceding uncapped
experiment, rather than a reconstructed or substituted baseline.

For 20% dimming on the rough plate, the mean crop light reaches and maintains
90% of the requested adjustment after 118 updates. The uncapped control does
not reach that threshold within 180 updates. The unfinished fraction after
180 updates falls from 17.2659% to 6.9310%. Switch-off takes the same 10 rough
or nine smooth updates in both modes. The moving-emitter crop and videos show
little additional benefit. The old crop is not an independent reference for
the moving source, so those observations do not establish a ghosting score.

The dimming evidence shortens history at 2,117 of 12,486 plate pixels on the
change draw. It shortens none in that crop during the next 179 draws. Each
reference record renews every draw. Both matched evaluations can therefore
see the new light while the long average retains old light. Ticket `tiunr-py6q`
records this limit without authorizing another design. This is a partial
response improvement, not complete validation of the retained light.

Final Rendering tests pass 2,551 Checked cases and 2,550 Release cases, with
the same ten existing skips in each configuration. All 34 focused cases pass
in both. Seven saved-data measurement tests pass. Native results, definitions,
failure evidence, screenshots, videos and runtime identities are retained in
the experiment's `report.md`, `result.json`, and `additional-checks.json`.
The check remains opt-in. Coverage, mutation testing, Linux-driver behavior,
performance and general-scene acceptance remain unestablished.

### Paired-ray removal and the drift-sum agreement test

On November 7, 2026, the user judged the reference-ray check barely better than
the uncapped control. The user selected its removal. Every file that the check
owned returned byte for byte to its state before the check. The sealed paired-ray
evidence stays as a control. `TERTIUS_REFLECTION_HISTORY_VALIDATION` no longer
exists.

Ticket `tiunr-py6q` then built an opt-in agreement test with a drift sum,
`TERTIUS_REFLECTION_HISTORY_AGREEMENT=drift`. It compares each pixel's limited
sample with its slow history, pools the 3x3 same-surface window, and keeps a
two-sided CUSUM. A trip cuts the history to the fast cap of four. It uses no
rays and takes 24 bytes per pixel. ARCHITECTURE.md describes the mechanism.
Evidence lives under `/private/tmp/tertius-drift-history-20261107/`.

Two native runs used the paired-ray matrix. With the selected allowance 0.2, the
rough dimming reached persistent 90% adjustment after 57 updates, against more
than 180 for the control. But static rough noise doubled, and 4.44% of static
plate pixels fell to the fast cap per frame, against 0.87% for the control.
With the one tuning change, allowance 1.25, static rough noise rose 29% and the
dimming response matched the control.

The cause is a biased score. On the static rough plate, the window mean over its
standard error averages -0.84, because rough rays are right-skewed. The falling
sum climbs on noise alone. In both runs, the share of plate pixels that fell
after the dimming stayed within 0.1 percentage points of the share before it. No
allowance separated the two cases. With the test off, the control output stays
byte-identical. The test remains opt-in, uncommitted and unmerged. The evidence
folder's `report.md` holds the tables, videos, test results and deviations.

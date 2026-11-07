# Unity-guided research into indirect-light noise

Date: November 5, 2026, America/New_York.
Status: research and discussion, not an accepted renderer redesign.
Research ticket: `tiunr-9b7v`, child of image-quality ticket `ter-icn3`.
Branch: `ter-icn3-unity-noise-research`.
Starting main commit: `34c28466e641a864fd6a915c029e08f07b3b9ccc`.

## 1. Purpose and authority

The user wants a slow, collaborative investigation with explanations, diagrams,
and inspected renderer images. The target is indirect diffuse and reflected
light that looks stable and plausible in the actual game. Passing tests or a
lower fluctuation statistic does not establish that result.

The user proposed three connected research directions:

1. Investigate Unity's suppression of very bright individual ray estimates.
2. Investigate reading a stabilized previous scene-color image instead of the
   short indirect-light history when evaluating light at a ray hit.
3. Investigate spatial filtering after temporal accumulation, including Unity's
   actual diffuse filter rather than only an AMD reflection-filter footprint.

These are hypotheses to investigate, not permission to install three changes
at once. This document does not select a brightness policy, change the earlier
integrated-filtering decision, or authorize an output-only production fix.

This is a direct top-level discussion thread in its own T3 worktree. It is not
part of a workflow. Do not start subagents, workflows, builds, tests, numerical
experiments, renders, commits, merges, pushes, or production edits merely from
this handoff. Begin with source reading and discussion. Ask the user before
implementation, and obtain a heavy-work allocation from the orchestrator before
any heavy command. The four existing allocations belong to other work.

## 2. Terms and the common model

| Term | Meaning in this investigation |
| --- | --- |
| Direct light | Light received directly from the engine's explicit lights. The current renderer evaluates those lights with an exact loop. |
| Indirect diffuse light | Light that reaches a receiving surface after a diffuse interaction with another surface. |
| Reflected light | The indirect specular signal, with directions concentrated according to the receiving material's roughness. |
| Ray estimate | The light contribution inferred from one traced direction, including the sampling weight. It is not a whole-pixel ground truth. |
| Fast indirect history | The retained indirect estimate with a short response time. Current ray-hit lighting reads this estimate from the previous frame. |
| Slow indirect history | The retained indirect estimate with a longer response time, used to stabilize displayed lighting. |
| Scene-color history | A previous scene image that combines several lighting components. Its exact contents, units, resolution, and capture point must be named. |
| Pre-exposed HDR light | Linear high-dynamic-range light multiplied by the renderer's exposure scale. It is not the displayed, tone-mapped image. |
| Variance | Squared variation of a light estimate. Units must be squared linear-light units, not displayed pixel code values. |
| Speckles | Small isolated bright or dark errors visible within a surface. A bright pixel is not automatically an error. |
| Mottling | Broad spatial variations that appear as patches rather than the intended lighting or material detail. |
| Temporal fluctuation | Change at a pixel across frames. A stationary wrong patch can have low temporal fluctuation. |
| Reprojection | Finding the previous image location for a current surface point, using camera/object motion and validity tests. |
| Spatial tap | A neighboring light value read by a filter. Several taps can contain copies of one underlying ray estimate. |

The common model is a sequence of light estimates with explicit dependencies.
A ray samples light at another surface. Reconstruction combines compatible
evidence across space and time. Some reconstructed light becomes an input to
future rays. Every experiment must identify what caused a change and which
image stores the result. Renaming an image does not change that dependency.

## 3. Current renderer baseline

The starting commit contains the validated tracer and History Rule A. It does
not contain the rejected, uncommitted Design B implementation. Main and
`origin/main` matched the starting commit when this research handoff began.

Relevant source paths in this worktree:

| Path | What to inspect |
| --- | --- |
| `src/Tertius.Rendering/Shaders/screen_trace.glsl` | Direction sampling, random sequence, hit lighting, environment misses, sample weights. |
| `src/Tertius.Rendering/Shaders/screen_trace_plan.comp` | Independent rays during short histories and mature compatible 2-by-2 sharing. |
| `src/Tertius.Rendering/Shaders/screen_trace_resolve.comp` | How each receiving pixel weights a ray. |
| `src/Tertius.Rendering/Shaders/accumulate_temporal.comp` | Fast/slow temporal updates and history handling. |
| `src/Tertius.Rendering/Shaders/accumulate_spatial.comp` | Short-history repair and the slow-history clamp. |
| `src/Tertius.Rendering/Frame/SignalParameters.cs` | History caps and per-signal behavior. |
| `docs/plans/temporal-renderer.md` | Existing decisions, interfaces, and acceptance criteria. |

Confirmed baseline behavior:

- Diffuse rays use a cosine-weighted hemisphere. Reflection rays use the GGX
  visible-normal distribution, which describes the rough specular lobe.
- Direction samples come from a PCG hash. They are not a designed blue-noise
  sequence, which arranges sampling error across space and time.
- Histories shorter than eight frames receive independent rays. Mature,
  compatible pixels share a ray in a 2-by-2 block, with receiving-pixel weights.
- Ray weights have a cap of four. That is not a general cap on hit-light
  brightness. The large representable-radiance bound is not an image-quality
  policy for ordinary bright outliers either.
- At a hit, diffuse tracing combines current direct diffuse light, previous
  fast diffuse history, the hit material's diffuse factor, and emission.
  Reflection tracing also includes the hit's specular terms. Invalid history
  uses the environment fallback.
- This is a multi-bounce feedback path across frames. The fast history can
  still contain noise. Its contribution to the observed artifact is unmeasured.
- Diffuse slow history is capped at 32 frames and fast history at four.
  Reflected slow history varies with roughness. A history length is not proof
  of that many independent observations.
- Baseline spatial repair applies to short histories. Mature slow history is
  constrained by a local fast-history statistical range. This is not a general
  ongoing post-temporal diffuse denoiser.

Do not change ray counts, history caps, rejection rules, trace traversal,
exposure, and denoising together. Those changes would obscure the cause.

## 4. The previous spatial-filtering attempt and its failure

Preserve this checkout without writing or building there:

`/Users/alexispurslane/.t3/worktrees/tertius/ter-gayt-integrated-spatial-denoising`

Its base remains `aaa5e776c4b33b985ca1a7cf0b9caea008b30a60`. Engine, test,
design, and ticket changes are dirty and uncommitted. Its implementation child
is `tgisd-pyop` under `ter-icn3`. Do not copy its whole worktree into this one.
If reuse is later authorized, reconcile the exact changed files against the
validated tracer and Entity changes rather than importing the old base.

The implemented sequence was:

1. Reproject raw-light temporal statistics.
2. Spatially filter current raw light.
3. Blend filtered light into both fast and slow indirect histories.
4. Apply the existing slow-history repair and clamp.

The filter used the center and 15 neighboring positions inside a 7-by-7-pixel
footprint adapted from AMD's reflection denoiser. Receiving depth, geometric
normal, previous raw-light mean, and luminance variance set the weights.
Reflected light also checked roughness. Raw statistics were separate from the
retained lighting. Neighbor reads and writes used distinct images and barriers.
No new bright-sample clipping or tile-average outlier suppression was added.

This was integrated filtering, not an offline image blur: filtered values entered
the histories that future rays read. It was not AMD's complete reflection
denoiser, and AMD's reflection pipeline is not a complete diffuse-GI recipe.

### What improved

Matched real-renderer captures used the actual `demo-exit` deck at native
1280-by-720 resolution, fixed exposure +1.57 stops, default tracer/environment,
SMAA and bloom, one frame slot, and advancing frame clocks.

In the late 32-draw window, renderer draws 161 through 192:

| Surface interior | Current filtered diffuse fluctuation reduction | Slow diffuse fluctuation reduction | Displayed fluctuation reduction |
| --- | ---: | ---: | ---: |
| Floor-left | 64.40% | 43.89% | 42.16% |
| Floor-center | 64.90% | 37.84% | 36.86% |
| Wall | 66.80% | 44.15% | 41.81% |

Displayed means shifted by about +0.24% to +0.30%. These finite windows do not
prove energy preservation. Temporal fluctuation does not measure spatial error.

### What failed

The user explicitly rejected the remaining speckles and mottling. Therefore the
attempt failed visual acceptance even though it reduced measured fluctuation.
Do not call it a successful denoiser or begin post-acceptance hardening on that
basis. An identifiable rough-paint reflection and long-motion trail acceptance
also remain open. No root cause was established by the reduction statistics.

The footprint is fixed, and its lighting guide uses previous raw statistics.
Those facts justify investigation, not a claim that the footprint or guide is
the proven defect. A filter can reduce fine noise yet leave broad patches.

### Evidence and limits

Evidence root:

`/private/tmp/gayt-history-diagnosis/integrated-spatial-20261105/`

- Read `report.md`, `manifest.md`, `measurements.json`, `evidence-checks.json`,
  `comparison.html`, `implementation.patch`, and `implementation-files.sha256`.
- Inspect `control/draw-065.png`, `integrated/draw-065.png`,
  `moving-control/draw-125.png`, and `moving-integrated/draw-125.png`.
- Matched motion videos are `moving-control/moving.mkv` and
  `moving-integrated/moving.mkv`, each 180 frames over three seconds at 60 fps.
- Focused accumulator, screen-tracer, and bloom tests passed in Checked and
  Release. Full Release Rendering passed 2,280 cases with eight existing skips.
  The initial full Checked run had two bloom-fixture failures and a recorder
  exception. Corrected focused bloom tests passed, but no new fully green full
  Checked run was claimed.
- No changed-line coverage/mutation campaign, Linux runtime acceptance, or
  controlled-clock GPU cost claim exists for this attempt.

Earlier `ter-69s8` patchwork images repeatedly reset history because their
fixture reused snapshot time. They are withdrawn as warmed-history evidence.
That fixture error does not invalidate the advancing-clock Design B captures.

Fresh baseline deck/reflection captures and provenance are under:

`/private/tmp/tertius-integration-20261105/merge-gayt/`

## 5. What Unity actually does

The primary implementation reference is Unity-Technologies/Graphics at immutable
commit `0efc28ea742b1966b9dd9cc4489e58b986a13faf`, committed October 26, 2026.
Do not silently mix that implementation with a different HDRP release's
defaults. User documentation from HDRP 17 is supporting context, not proof that
every current-master parameter has the same name or default.

### Sampling and hit-light reconstruction

The SSGI compute shader uses a blue-noise sequence and cosine-weighted diffuse
directions. For a valid screen hit it reprojects the hit, checks previous depth,
and samples mip level one of the previous color pyramid. A color pyramid stores
progressively spatially filtered scene-color images. The shader compensates
previous exposure and applies current exposure before it limits HSV value,
the brightness component in hue/saturation/value color coordinates, to seven.

That number is a value in Unity's exposed working signal. It is not seven
physical light units and is not a proposed Tertius default. Limiting samples
can remove real light and change color or mean brightness. Those costs require
measurement, especially when small emissive surfaces supply real illumination.

### Temporal and spatial schedule

`DenoiseSSGI` explicitly calls temporal filtering followed by diffuse spatial
filtering. An optional second pair temporally filters that result using another
history, then spatially filters with half the first radius. Temporal history
is copied inside `HDTemporalFilter.Denoise`, before the subsequent spatial call.
Thus the two temporal histories and the scene-color feedback are distinct paths.
Do not describe every spatially filtered result as automatically stored in the
first temporal history.

### The actual diffuse spatial filter

Unity's filter is not a fixed 7-by-7 reflection footprint. It distributes sample
points on the receiving surface's tangent plane, projects them into the image,
and weights compatible geometry with a distance falloff. Its radius depends on
camera distance, a user setting, and projected pixel footprint. The full filter
uses 16 taps. Its reduced-work mode uses four taps per pixel with a gather step.
The schedule can vary the point subset across frames.

These are neighboring reads, not 16 newly traced rays. Inspection of U8 confirms
geometry weights, without light-color or variance weights. See the research log
for the limits of this finding. No Tertius policy follows from it.

### What the color pyramid is not

The traced producer builds the relevant color pyramid during scene rendering,
before later refractive transparents and post-processing. It is not the final
displayed image, and the inspection does not justify calling it TAA-cleaned.
Its exact included lighting/components need a producer-to-consumer diagram.
Previous scene color can still contain previous indirect-light noise.

Unity is a close feasibility reference, not evidence that our inputs, material
decomposition, settings, or cost match it. It depends on HDR scene color, depth,
normals, motion, exposure management, and fallback lighting. The source targets
HDRP's GPU pipeline, not a measured performance target on our Vulkan drivers.

## 6. The research program

### R0. Establish the signal and the failure

First explain the complete light flow to the user. Mark current-frame and
previous-frame reads, stored images, material factors, exposure conversions,
reprojection checks, and which filtered values future rays actually read.
Compare that drawing with the pinned Unity producer/consumer schedule.

When heavy experiments are authorized, reproduce the accepted baseline settings
and the rejected candidate. Keep clocks advancing during frozen scenes. Record
renderer draw index, sampling draw index, resets, render/output size, exposure,
AA, bloom, scene time, camera, and exact source/build identity.

Capture raw samples, filtered samples, fast history, slow history, composed HDR,
and displayed output separately for diffuse and reflected light. Establish an
independent comparison for spatial error, such as a bounded high-sample run or
an analytically simple scene. Its cost and validity must be agreed first.

Deliverable: a stage-by-stage artifact map. Distinguish speckles, broad spatial
error, temporal fluctuation, and genuine lighting detail. A flat wall is not
necessarily uniformly lit, and a static wrong patch is not a denoising success.

### R1. Test bright-ray estimates as a cause

Hypothesis: a small fraction of very bright ray estimates supplies much of the
remaining error, which the current spatial filter spreads or retains.

1. Measure sample-brightness distributions and the share of total light carried
   by the upper tail. Separate diffuse/specular, direct-hit light, emission,
   and previous indirect light. Measure after receiving-pixel weighting too.
2. Relate visible speckles and patches to those samples and their subsequent
   fast/slow histories. A histogram alone does not prove the visible cause.
3. After discussion, isolate a reversible brightness-suppression experiment.
   Do not combine it with a new source image, new ray count, or new filter order.
4. Compare noise reduction with mean/total-light loss, emitter visibility,
   reflected detail, exposure changes, and response to switched lights.

A useful result reduces the unwanted artifacts without concealing unacceptable
lost illumination. A dimmer image with lower fluctuation is not sufficient.
If exposed thresholds change the lighting when only camera exposure changes,
report that behavior rather than compensating it with a different exposure.

No hard threshold or robust weighting scheme is selected here. Those policies
trade bias against variance and are a real user design choice.

### R2. Test the source of hit light and feedback

Hypothesis: the previous short indirect-light history is a sufficiently noisy
source that later bounces retain or redistribute its error. A better
reconstructed source can improve the result without changing the ray budget.

The user's proposed previous whole-frame source needs this correction: a scene
image still contains indirect light, and therefore still creates feedback.
Filtering it can reduce noise. It cannot guarantee that noise never returns.

Before an experiment, answer these source questions:

- Is the candidate a linear HDR image before tone mapping, bloom, and display
  conversion? Is it native render resolution or upscaled output?
- Does it already include direct light, indirect diffuse/specular light,
  emission, material color, and transparency? Which parts are view-dependent?
- If it already contains direct light, how does the experiment avoid adding
  that light again beside current direct light?
- If it already contains the hit material's factors, how does the experiment
  avoid applying those factors twice?
- How does diffuse tracing avoid importing an inappropriate view-dependent
  specular highlight from the camera's view at the hit?
- Which temporal/spatial reconstruction precedes capture? What frame of delay,
  exposure conversion, valid extent, and miss fallback apply?
- How does the source respond when a lamp moves or turns off? A longer history
  can look quieter while producing unacceptable old-light trails.

Compare the existing source against one clearly specified scene-color source,
with all other settings fixed. Never implement 'current direct + previous final
color' without resolving double counting. A frozen indirect source can be a
diagnostic control, but is not a production solution for changing light.

Deliverable: the measured change in raw/retained noise, brightness, bounce
response, and motion lag attributable to source choice. Do not assume Unity's
scene-color path is completely free of multi-bounce feedback.

### R3. Test spatial reconstruction after temporal accumulation

Hypothesis: filtering a temporally improved diffuse estimate, with a footprint
suited to diffuse lighting, removes residual error that the earlier current-light
prefilter left behind.

Finish tracing Unity's sample distribution, geometry weights, radius,
resolution handling, temporal-history writes, and optional second pair first.
Compare those mechanisms with the actual scale and boundaries of our artifacts.

Then isolate filter placement before replacing its footprint. Name whether the
result affects displayed light, the source of future rays, retained temporal
state, or more than one of these. A temporary output-only comparison can answer
a diagnostic question, but does not replace the user's integrated-lighting goal
without a new decision.

If placement helps but leaves broad patches, investigate footprint and guide
separately. Test real lighting edges, same-surface light changes, thin geometry,
glass frames, reflected detail, newly visible surfaces, and camera motion.
A filter that erases real lighting detail does not meet the goal.

Deliverable: an explanation of which spatial error scales disappear, which
remain, and how much detail, responsiveness, memory, and GPU work are traded.
Do not infer that Unity's filter is cheap merely because it has 16 taps. Its
geometry projection, extra passes, histories, and gathers also cost work.

### R4. Combine only supported changes

Keep a matched baseline and a one-change experiment for each tested hypothesis.
Once individual effects are understood, compare the supported changes together
to expose interactions. For three binary experimental changes, a full matrix
has eight combinations; agree a bounded subset before spending GPU time.

Test more than the deck. Include a broad diffuse receiver, a small bright
emissive surface, low light, rough and smooth reflections, changed exposure,
moving camera, moving/disabled lights, occlusion changes, and resolution changes.
Sampling-sequence quality and shared-ray correlation remain secondary hypotheses
if these experiments leave a residual pattern. Do not silently add them to the
first three changes.

## 7. Measurement and acceptance

| Quantity | Required interpretation |
| --- | --- |
| Spatial error | Compare with justified scene expectations or an independent reference, not only a blurred candidate. Report fine speckles and broad mottling separately. |
| Temporal fluctuation | Measure matched frame windows in linear light and displayed code values separately. Record resets. |
| Brightness/color change | Report linear-light means and bright-region loss. Do not retune exposure to conceal energy loss. |
| Temporal response | Track moved/disabled lights and newly visible surfaces. Preserve or explicitly renegotiate existing response requirements. |
| Reflected detail | Show identifiable reflected geometry or state the screen-space/material limitation. Low variance is not evidence of a reflection. |
| GPU cost | Count complete pipeline work and storage. Use valid GPU timing with controlled clocks when available. Do not claim an isolated MoltenVK pass cost from late-latched timestamps. |
| Portability | Write to Vulkan rules. Mac correctness does not establish Linux-driver acceptance. Keep deferred Linux work explicit. |

Choose numerical acceptance thresholds with the user after explaining what the
measurements mean. For each meaningful visual result, inspect two to four
real-renderer screenshots before showing them, with matched settings and short
captions. Use motion clips for trails. Do not deliver only a percentage table.

## 8. FSR and dynamic-resolution boundary

The separate `ter-s7c6` work integrates FSR 3.1 and dynamic resolution. This
research does not block it or rewrite its controller.

The inspected FSR inputs are jittered pre-exposed HDR scene color, opaque color,
reactive mask, depth, unjittered motion vectors in render-size units, exposure,
render/output extents, jitter, camera data, reset state, and output storage.
Tracing and indirect accumulation occur before composition and upscaling.

Ray selection, hit-light reconstruction, and indirect denoising can generally
change behind that composed-color boundary. Preserve these integration rules:

1. Keep color linear and keep exposure conversions consistent.
2. Preserve jitter, motion-vector, depth, and coordinate conventions.
3. Honor the active render extent for every neighborhood and history read.
4. Keep history reset/resize behavior explicit, with correct image lifetimes
   and Vulkan barriers between dependent passes.
5. Do not assume the FSR output is a free scene-color history. Feeding it back
   introduces another resolution and temporal dependency that needs a design.
6. Include changed tracer/denoiser work in the controller's measured frame cost.

FSR is not a substitute for a diffuse/reflection denoiser. Final combined
validation must check composition, motion, exposure, resizing, and visual
quality after both branches are ready. Do not repeatedly import a moving FSR
branch into an unaccepted research experiment.

## 9. How the discussion proceeds

Start by summarizing the user's three hypotheses and the correction about scene
color still containing feedback. Explain Unity's confirmed order and its
post-temporal spatial filter. Use a self-contained diagram, then propose the
first small source-reading or measurement question. Do not start a long silent
implementation push.

At a real design fork, present exactly two coherent designs, the different
results/costs, usage examples, and what both share. Use the ask-user-question
tool when it is available. If the host rejects that tool, ask one concise
plain-text question rather than pretending a decision was made.

When behavior changes are authorized, load the software-design and TDD skills,
write behavior tests, observe the intended failure, implement minimally, and
show the user the result before post-acceptance hardening. Keep native numerical
experiments, tests, builds, and renders in the Nix dev shell and use project
locks. Coordinate capacity with the parent before starting them.

Keep a research log in this document with date, source identity, hypothesis,
single changed factor, matched inputs, evidence paths, result, uncertainty, and
the user's decision. Notify the orchestrator of a decision, a capacity request,
a ready checkpoint, or a real blocker. Do not close `ter-icn3` or call research
results integrated. Existing dirty checkouts and diagnostic evidence must stay
intact.

## 10. Sources and artifact index

Primary source base, pinned to the commit above:

`https://github.com/Unity-Technologies/Graphics/blob/0efc28ea742b1966b9dd9cc4489e58b986a13faf/Packages/com.unity.render-pipelines.high-definition/`

| Reference | Relative path within that source base | Relevant source anchors |
| --- | --- | --- |
| U1: SSGI shader | `Runtime/Lighting/ScreenSpaceLighting/ScreenSpaceGlobalIllumination.compute` | Lines 37/39, 104-108, 264-284, 320-325: mip, brightness cap, direction generation, reprojected color read, exposed-value limit. |
| U2: SSGI schedule | `Runtime/Lighting/ScreenSpaceLighting/HDRenderPipeline.ScreenSpaceGlobalIllumination.cs` | `DenoiseSSGI`, approximately lines 400-465: temporal, diffuse spatial, optional second pair. |
| U3: Diffuse filter shader | `Runtime/RenderPipeline/Raytracing/Shaders/Denoising/DiffuseDenoiser.compute` | Approximately lines 57-194: point distribution, radius, tangent-plane projection, 16/four taps, normalized accumulation. |
| U4: Diffuse dispatch | `Runtime/RenderPipeline/Raytracing/HDDiffuseDenoiser.cs` | `Denoise`: filter parameters, dispatches, and reduced-work gather. |
| U5: Temporal history | `Runtime/RenderPipeline/Raytracing/HDTemporalFilter.cs` | `Denoise`, approximately lines 225-314: temporal output and copy to history before returning. |
| U6: Temporal shader | `Runtime/RenderPipeline/Raytracing/Shaders/Denoising/TemporalFilter.compute` | Trace temporal weighting, validity, clipping, and copy behavior before asserting equivalence. |
| U7: Scene-color producer | `Runtime/RenderPipeline/HDRenderPipeline.RenderGraph.cs` | Approximately lines 1882-1891 and `RenderPostProcess`: scene-color pyramid before later refraction/post-processing. |
| U8: Geometry weights | `Runtime/RenderPipeline/Raytracing/Shaders/Denoising/BilateralFilter.hlsl` | Lines 49-97 and 147-192: geometry data and weights. Inspected November 5; no light-color guide. |
| U9: Reduced-work gather weights | `Runtime/Lighting/ScreenSpaceLighting/BilateralUpsample.hlsl` | Lines 16-27, `BilUpColor3_Uniform`: depth weights and additive constants in the light and weight sums. |
| U10: Radius lower bound | `Runtime/RenderPipeline/Raytracing/Shaders/Denoising/DenoisingUtils.hlsl` | Lines 6-12, `ComputeMaxReprojectionWorldRadius`: projected pixel size and surface-view angle contribute to the radius. |

The retrieved primary files are cached at
`/private/tmp/tertius-unity-noise-research-20261105/unity/`.
The original checksum manifest remains unchanged. Added U8-U10 checksums are in
the adjacent `unity-additional-sources.sha256` file.

Supporting references:

- Unity HDRP 17 SSGI overview:
  `https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Override-Screen-Space-GI.html`
- Unity HDRP 17 SSGI controls:
  `https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/reference-screen-space-global-illumination.html`
- Unreal SSGI, a different ray budget and supplemental-lighting situation:
  `https://dev.epicgames.com/documentation/en-us/unreal-engine/screen-space-global-illumination-in-unreal-engine`
- AMD portable reflection-denoiser context:
  `https://gpuopen.com/manuals/fidelityfx_sdk/techniques/denoiser/`
- Original SVGF research, a broader temporal and multiscale reconstruction
  reference, not a claim that its costs transfer to this game:
  `https://research.nvidia.com/publication/2017-07_spatiotemporal-variance-guided-filtering-real-time-reconstruction-path-traced`

Use primary code and papers for algorithm claims. Record further source
inspection against immutable revisions, and keep observations separate from
hypotheses. No source comparison in this document establishes the root cause of
our noise or selects a production solution.

## 11. Research log

### November 5, 2026: standalone discussion begins

- Checkout: the path, branch, and HEAD match the handoff. HEAD is
  `34c28466e641a864fd6a915c029e08f07b3b9ccc`. The three prepared documentation
  and tracking edits remain present. No checkout or branch operation ran.
- Source identity: reread cached U1, U2, U3, U5, and U7 and retrieved their
  pinned primary sources at `0efc28ea742b1966b9dd9cc4489e58b986a13faf`.
  Read the baseline hit-light and spatial-accumulation shaders too.
- Confirmed distinction: the baseline feeds previous fast indirect history
  into hit lighting. Its slow history supplies displayed indirect light.
  Unity reads previous scene color for hit lighting. Unity copies each
  temporal output into its own history before the following spatial filter.
  These temporal histories are not the scene-color pyramid.
- Hypotheses remain separate: rare bright ray estimates, noise in the source
  of hit light, and residual spatial error after temporal accumulation.
  No experiment changed any factor. There are no new matched captures,
  numerical results, or quality-acceptance claims.
- First proposed source question: does Unity's diffuse spatial filter
  distinguish a real lighting boundary on one flat surface, or only changes
  in surface geometry? U8 remains uninspected. The full producer-to-consumer
  component audit also remains open.
- User decision: none added. No production policy, implementation, heavy
  command, subagent, workflow, commit, or integration is authorized or started.

### November 5, 2026: diffuse spatial-filter source inspected

The user authorized source inspection. Source identity remains
`0efc28ea742b1966b9dd9cc4489e58b986a13faf`. Read U8-U10, the complete U3
shader, and U4's dispatch. The three added source files are cached and hashed.

U8 scales contributions by normalized depth difference, normal agreement, and
separation from the local surface planes. A normal is a surface direction,
decoded here from Unity's normal buffer. The diffuse shader enables the unlit
test, not the helper's optional roughness mode.

U3 adds distance falloff and rejects off-screen, background, unlit, or
depth-incompatible reads. It averages 16 neighboring reads, or four reads
followed by a gather. These are not additional traced rays. U10 supplies a
radius lower bound from projected pixel size and surface-view angle.

U9 confirms that the reduced-work gather is depth-guided, not light-guided.
Its light sum and weight sum both include additive constants. Do not describe
that path as an exactly constant-preserving average. No visible effect or
floating-point behavior was measured.

Inference: with geometry and read locations fixed, changing only indirect-light
values does not change neighbor weights. A real indirect-light boundary on one
flat wall has no explicit light-difference protection. This statement concerns
the filtered indirect-light input, not direct-light boundaries or later material
color. It does not establish the cause of Tertius's mottling.

Single changed factor: none. No matched inputs, numerical results, new captures,
performance measurements, implementation, or user policy decision. The complete
scene-color component audit and the quality-acceptance criteria remain open.

### November 5, 2026: bounded visual prototype authorized

The user authorized direct implementation of a throwaway diffuse spatial filter
after temporal accumulation and a few warmed real-renderer stills. This is a
human-authorized exploratory prototype, not production code or a TDD waiver for
future implementation. No extensive tests, coverage or mutations are requested.

The user assigned ONE build/render allocation, released by ter-6gf8. The other
three allocations remain ter-s7c6, ter-rgg9 and ter-7tnv. Run one foreground
heavy command at a time through Nix. Queue GPU work through tools/gpu-lock.sh
without interrupting FSR. Disk budget is about 12 GB; keep captures bounded.
Release this allocation explicitly when builds/captures finish.

The prototype adds 16 tangent-plane reads after the existing temporal blend and
repair/clamp. It writes scratch, then copies back into slow diffuse history.
Composition and the next temporal blend therefore read filtered slow history.
Fast history, which supplies ray-hit lighting, stays unchanged. No ray count,
sample cap, brightness limit, exposure or hit-light source changes.

Reference situation: HDRP's GPU diffuse reconstruction uses HDR light, depth,
normals and temporal filtering. Our inputs provide those quantities at native
1280-by-720 resolution. The prototype uses full-resolution reads, not the
four-read/gather mode. Sixteen projected reads and a full-image copy add fixed
GPU work per pixel; this is not a speedup or an accepted performance budget.

Adaptations: shading normals, a deterministic 16-point golden-angle disk with
linear radial spacing, no frame jitter, radius setting 0.5 (not a claimed Unity
default), relative eye-depth weighting instead of finite-far-plane normalized
depth, and preservation of the center's history length. The distance radius,
two-pixel projected-footprint lower bound, normal/plane weights, Gaussian
distance falloff and 0.1 m expected-eye-depth gate follow the source's model.
Sampling.hlsl confirms SampleDiskCubic uses a linear radial coordinate; it is
cached with this source revision. The low-discrepancy texture sequence is not
ported. Unlike Unity's first temporal history, this prototype's slow history
retains the spatial result. This difference must remain explicit.

The runtime research switch is TERTIUS_UNITY_DIFFUSE_PROTOTYPE=1. Without it,
the added dispatches and pipelines are absent. This is not a public renderer
option or a settled production policy. Keep controls and candidate captures
matched from the same build with advancing snapshot time.

### November 5, 2026: prototype stills inspected, allocation released

The Release build succeeded with zero warnings/errors. Four matched runs used
the real renderer: deck control/candidate at +1.57 exposure stops and reflection
test control/candidate at -6.8 stops. All used native 1280-by-720 resolution,
ACES, SMAA, bloom, local shadows, the default tracer/environment, one
synchronous frame slot, no impostors and no upscaling. The only runtime
difference within each pair was the prototype switch.

Each script film captured 33 frames, with a simulation tick and fresh snapshot
before each. The renderer added 32 hidden warm-up draws before the first
capture. All runs ended at 65 renderer draws and zero-based history draw index
64. Deck final snapshot time was 7.2 s, simulation tick 432. Reflection final
snapshot time was 0.7166666666666667 s, tick 43. Each selected still is the
film's frame 32, after 64 earlier draws. Both control PNGs are byte-identical
to the preserved merge-gayt baseline at the same settings.

Evidence root: /private/tmp/tertius-unity-noise-research-20261105/prototype/.
manifest.md records settings, commands, source/build hashes, adaptations,
final history counters, inspected findings and limitations. Four selected
PNGs and each run's exposure records remain in captures/. Intermediate PNGs
were removed only from these new folders. The artifact root is about 45 MB.
The obsolete --reflections CLI flag caused one rejected command before any
rendering. Its log is preserved. The corrected command uses --screen-trace on.

All four selected PNGs were opened and inspected. Many fine speckles disappear
from the deck wall, cabin structure and floor. Broad floor patches and some
floor grain remain. The reflection test loses much of its background/floor
speckling, but grain persists around the bright reflected strip and blue
sphere. Local lighting gradients change and become smoother. This is not proof
that every remaining speckle comes from the unchanged specular signal.

The stills support further investigation, not user quality acceptance. Filter
order, footprint and retained slow history changed together. No independent
reference, brightness-loss measurement, motion/light-response sequence, Linux
runtime check or controlled-clock GPU cost result exists for this prototype.
No automated tests, coverage or mutations ran under this visual authorization.
Future production implementation still requires design discussion and TDD.

The one build/render allocation is RELEASED. All four runs finished by
12:40:41 EST, and the parent was notified immediately afterward. GPU work
queued normally behind other holders. No further heavy command is assigned
here. No commit, merge, prerequisite import, child, workflow, ticket closure
or production-policy acceptance occurred. The prototype remains uncommitted
and disabled by default. All prior local edits and the old Design B checkout
remain preserved.

### November 5, 2026: positive still review and short-history follow-up

The user described the prototype as "staggeringly better" and asked to keep
developing this prototype before trying unrelated fixes. The user proposed
feeding spatially filtered light into short history as well, and asked about
performance. This is a positive review of the shown stills and authorization
to continue exploratory discussion. It is not permission to merge, harden,
change brightness limits, or treat motion/performance as accepted.

The short diffuse history retains a fast-changing estimate and supplies
indirect diffuse light at ray hits. Both diffuse rays and reflection rays
read it. It also contributes to the local range that constrains long history.
At a mature surface its blend gives the current sample at least one-quarter
of the weight. Long diffuse history supplies displayed light, with a current
sample weight as low as one-thirty-second. These weights do not count
independent observations or bound the response of multi-bounce feedback.

Two distinct experiments are proposed, not selected:

| Design | Stored short-history result | Expected response trade | Additional prototype work |
| --- | --- | --- | --- |
| Separate filtering, recommended | Apply the same spatial filter to the short estimate itself | Keeps the short blend rule separate from long history, but spatial reuse and multi-bounce response still need observation | Another 16-read filter and copy in a simple sequential version; scratch can be reused without adding a full-size image |
| Shared filtered result | Copy the existing filtered long result into short history | Removes the independent short estimate; ray-hit lighting inherits the long result's response and can retain old illumination longer | An extra output write in the existing copy stage, without another 16-read filter |

The next control must be the current long-only spatial prototype, not just the
unfiltered baseline. Keep ray-count settings, brightness limits, material
factors, exposure and the hit-light composition unchanged. Filter only diffuse
history. Do not call the shared result a preserved four-frame response.
No choice or implementation follows from this note alone.

The existing gpu-timings.json files permit a provisional whole-GPU-frame
comparison on Apple M1 Max at native 1280 by 720. Each row compares one
33-capture control run with one 33-capture candidate run:

| Scene | Control mean, ms | Long-only prototype mean, ms | Observed difference, ms | Relative difference |
| --- | ---: | ---: | ---: | ---: |
| Deck | 16.414688 | 16.917841 | +0.503153 | +3.065% |
| Reflection test | 10.328314 | 11.334621 | +1.006307 | +9.743% |

These are differences between two short sequential runs, not isolated filter
cost measurements. GPU clocks were not controlled and the runs were not
repeated or interleaved. MoltenVK reports late-latched stamps in both files,
so individual pass numbers must not be interpreted as the filter's own time.
The whole-frame span remains the relevant reported quantity. Do not convert
it into end-to-end game frame rate. The current prototype adds a 16-read
spatial dispatch and a copy, but no new full-size images. The short-history
extension has no timing result yet.

No engine edit, build, test or capture was made for this follow-up. The earlier
allocation remains released. Request a new bounded allocation before further
builds or captures. Keep the current prototype and evidence as the control.

### November 5, 2026, 12:51 EST: new allocation reserved, design pending

The user explicitly assigned a NEW single build/render allocation to this
existing standalone thread. The earlier allocation remains released. This
reservation completes a four-allocation ledger: FSR, corrected environment,
parent portable-walk hardening/integration, and this follow-up.

Do not start a heavy command until the user chooses the short-history behavior.
The capacity assignment does not choose separate short filtering or a shared
filtered long result. After that choice, no additional parent release is
required. Run one foreground heavy command at a time through Nix and
tools/gpu-lock.sh. The corrected-environment run holds the GPU lock, with
FSR and parent hardening queued. Queue normally without interruption or bypass.

Reported capacity: load 5.39, memory 52% free, disk 7.4 GB free. Check disk
before EVERY heavy command. Below 5 GB free, hold further heavy work and
report. Do not delete another work's artifacts. Keep only the few matched
warmed stills/logs, without a long video or a large new capture corpus.

Preserve the current long-only prototype and controls. No unrelated noise
fixes, prerequisites/imports, ray-count/brightness/hit-source changes, full
suites, hardening, commits, merges, children or workflows are assigned.
Short-history content changes are the proposed experimental variable, not
permission to replace the ray-hit composition or image-source mechanism.
The positive still feedback is not production integration acceptance.
Label existing unclocked frame differences as observations, not isolated
filter costs or controlled-clock budget evidence. Release this NEW allocation
explicitly after the chosen bounded experiment and captures finish.

Status: reservation held, short-history design awaiting the user's choice.
No heavy command or engine edit has started for this follow-up.

### November 5, 2026: clarify the short history's clamp role

The user correctly recalled that short history is not only a ray-hit source.
The preceding explanation understated its role in constraining long history.
Source inspection confirms two distinct times and uses:

| Use | Short-history version | Effect |
| --- | --- | --- |
| Ray-hit light | Previous frame's retained short diffuse history | Supplies the indirect diffuse term at hits for diffuse and reflection rays |
| Long-history control | Current frame's temporally updated short history | Supplies the compatible 3-by-3 neighborhood's channel means and standard deviations for the long-history clamp |

For diffuse light, once the long history length exceeds four, each channel is
limited to the neighborhood mean plus or minus two standard deviations.
Standard deviation here measures variation between neighboring short-history
light values. It is not an independently measured noise-only uncertainty.

The relative brightness change caused by the clamp also reduces long history's
effective length. A sufficiently large correction reduces it to four, so new
samples receive more weight. This clamps the value and weakens old evidence;
it does not simply discard the whole history. Surface/motion validity tests
separately decide which previous-surface history taps can be reused.

Therefore short-history filtering changes a feedback light source and the
reference that constrains long history. Its mean and spread can both change.
Copying the filtered long result into short history removes the independent
fast-changing reference. Separate filtering preserves separate estimates, but
does not guarantee unchanged clamp behavior or lighting response.

Ordering also matters: the existing long-only prototype filters after the
current clamp. If a short filter is also placed after that clamp, this frame's
clamp still reads the unfiltered current short estimate. The retained filtered
short estimate then affects later frames. Do not claim immediate same-frame
use of filtered clamp statistics without changing and documenting that order.

References: accumulate_spatial.comp, steps 4 and 5; accumulate_temporal.comp,
steps 1 and 2; SignalParameters.Diffuse; screen_trace.glsl, lastFastOf and hit
lighting. No engine edit, new design choice or heavy command follows from this
clarification. The new allocation remains reserved pending the user's choice.

### November 5, 2026: Unity's temporal acceptance and two-stage histories

The user asked whether Unity uses a short estimate to constrain a long estimate.
Inspected the pinned U2, U5 and U6 sources. This finding concerns HDRP's
screen-space global illumination path at the recorded revision, not every
Unity denoiser.

Unity's two diffuse histories are successive stages, not separate short and
long temporal estimates that constrain each other. The optional second stage
accumulates the first spatial filter's output. Both stages call the same color
temporal kernel with the same accumulation rule.

| Question | Pinned Unity behavior | Source |
| --- | --- | --- |
| Does the previous sample still describe this surface? | Reproject with motion vectors. Reject out-of-bounds history, previous background, excessive world-position difference or incompatible normals. Global validity can reject history too. | U6, lines 71-146 and 189-195 |
| Does motion alone force rejection here? | The validation pass records object motion, but the SSGI calls disable receiver-motion and occluder-motion rejection. Motion vectors still locate previous history and geometry checks remain active. | U2, lines 414-422 and 440-448; U6, lines 240-250 |
| How much accepted history remains? | Before the stored sample count reaches eight, use count / (count + 1) as the old-history weight. At eight, retain 93% old and 7% current each frame. Invalid history takes only the current input and restarts the count. Eight is a stored-count cap, not an eight-frame sliding window. | U6, lines 252-274 |
| What happens when exposure changes? | Rescale accepted color history into the current exposure. Reject it when exposure changes by a factor of two or more. | U6, lines 219-235 |
| Does short-history color constrain long-history color? | No short-versus-long comparison, neighborhood light-value clamp or correction-based history shortening appears in this SSGI temporal kernel. | U6, lines 169-275; U2, lines 400-465 |
| Which spatial result enters retained history? | Copy each temporal output to its history before the following spatial filter. First history therefore excludes spatial filter one. Optional second history accumulates spatial filter one's output, but excludes spatial filter two. | U5, lines 302-310; U2, lines 412-457 |

The optional second stage is evidence that Unity temporally accumulates a
spatially cleaned input. It is not evidence that copying our long estimate
over our independent short estimate preserves the short estimate's purpose.
The current prototype also differs from Unity's first stage: it retains the
spatial result in long history.

Inference: with unchanged surface geometry and valid exposure, an illumination
change does not itself trigger this kernel's geometry rejection. New light
instead enters through its temporal blend. Do not extend that observation to
Unity's entire scene-color feedback and final-image processing.

No implementation choice, engine edit, build or capture follows from this
source comparison. The new allocation remains reserved pending the user's
short-history choice and ordering decision.

### November 5, 2026: authorized single-history prototype

The user chose a simplified Unity-like alternative: one retained diffuse
history containing spatially filtered output. Accept previous history by
surface correspondence and exposure validity, without the short-versus-long
lighting check. This settles the previous fork for a throwaway experiment,
not for production policy. The existing prototype exception applies. No
automated suites, coverage or mutations are assigned.

The NEW allocation assigned at 12:51 EST is now active. Disk measured 9.2 GB
free before implementation. One foreground heavy command at a time, Nix,
the normal GPU-lock queue and the 5 GB disk guard still apply. Notify the
parent when captures finish and explicitly release this allocation.

The new switch value is TERTIUS_UNITY_DIFFUSE_PROTOTYPE=single-history.
Values 0 and 1 preserve the default and long-only prototype controls.
The new mode allocates one retained diffuse image, not mirrored fast images.
Its temporal dispatch reads the prior filtered estimate into scratch. The
spatial dispatch reads scratch and writes that one retained estimate.
Composition and existing ray-hit reads both use it. Specular stays unchanged.

Use Unity's count-based weighting, then 93% previous and 7% new once the
stored count reaches eight. Reject one-stop-or-larger exposure changes.
Reproject with the existing motion vectors. Test previous image bounds,
background, world-position distance and normal agreement. Geometry limits
follow U6/U10: at least 0.1 m or four projected pixel footprints, and normal
cosine at least 0.65. Explicit adaptations are per-bilinear-tap validation,
renormalized accepted weights, and geometric normals because Tertius retains
geometric normals in its surface history. Keep the existing invalid-sample
fallback and radiance safety bounds.

No short-history clamp, correction-based history shortening or initial repair
runs in this mode. The established tangent-plane spatial footprint stays the
same. Its shared shader body serves both prototypes without changing the
long-only filter's arithmetic. Original source evidence and harness binaries
remain in the prior prototype folder. The new harness has its own folder.

No ray-plan code or ray-budget setting changes. However, the existing ray plan
reads diffuse history count. The new validity rule and removal of clamp-driven
shortening can therefore change adaptive sample reuse. Do not claim identical
actual ray counts across modes without recording them.

Capture plan: new-binary long-only control and single-history candidate for
deck and reflection scenes, using the same advancing-clock scripts, settings,
fixed exposures and final draw index as before. Keep only each run's final
PNG, exposure/timing records and bounded logs. Compare new controls with the
preserved long-only stills. Inspect all four images before presentation.

### November 5, 2026: single-history stills ready for discussion

The Release build passed with zero warnings and errors. The four planned
captures finished by 13:28:27 EST. All report 65 renderer draws, last draw
index 64 and the matched advancing snapshot clocks. Both new long-only
controls are byte-identical to the preserved long-only stills. All four
images were inspected at original resolution before presentation.

The new mode looks close to the prior spatial prototype in these views.
Smooth wall/cabin light remains, with similar overall scene brightness.
Broad deck floor patches and fine grain remain. The reflection beneath the
blue sphere changes, and grain around the bright reflected strip remains.
This is not a reference-based brightness/detail result or a motion result.

Observed whole-GPU-frame means, long-only versus single-history: deck
17.955028 versus 16.791803 ms; reflection test 11.044968 versus 10.455257 ms.
These single sequential pairs lack controlled clocks and repeated/interleaved
sampling. Actual adaptive ray reuse is not fixed between the algorithms.
Do not report isolated filter cost, accepted budget or game frame rate.
Structurally, the new mode uses two diffuse dispatches instead of four and
omits two RGBA16F diffuse history images, about 14.1 MiB at native 1280 by 720.

Evidence and exact commands:
/private/tmp/tertius-unity-noise-research-20261105/single-history/manifest.md.
Only four final PNGs remain in the new captures, with bounded logs and records.
The new folder is about 44 MB. Disk remains about 9.2 GB free.

The NEW allocation is RELEASED. The parent was notified immediately after
captures finished. No further heavy work is assigned. No automated suites,
coverage, mutations, hardening, integration or acceptance occurred. The
single-history and long-only modes remain uncommitted, optional prototypes.

### November 5, 2026: requested FSR pipeline import

The user requested bringing the completed FSR and dynamic-resolution pipeline
into this research branch, with the existing prototypes reapplied on top.
This explicitly changes the earlier no-prerequisite-import boundary. It does
not authorize prototype integration into main or noise-policy acceptance.

Initial inspection: research HEAD is still 34c28466e, with no branch-only
commits. Main is 7d9f6e673. FSR branch tip 9bbbea67c is not an ancestor of main,
and its workflow is in the fix stage. The parent was asked for the final
integrated SHA, or an explicitly safe completed import point. Do not import
the unfinished fixer checkout or write to the parent's main checkout.

Preserve all tracked changes, all untracked prototype files, and the original
base identity in a bounded external backup before moving this branch. With
no local commits to rebase, the safe operation is to fast-forward this branch
to the completed upstream point and reapply the saved uncommitted edits.
Do not create a prototype commit or run its hooks merely to move the work.

Source inspection identifies an FSR compatibility change: temporal lookups
must use the new historyPlace helper, which accounts for current and previous
pixel jitter. The single-history shader currently uses the pre-FSR formula.
Adapt it after importing the completed helper. The existing spatial footprint
already uses the shared viewRayThrough and pixelOf helpers. The new CLI
replaces --anti-aliasing with --upscaling off|fsr3 and --render-scale. Preserve
old harnesses and evidence rather than rewriting their recorded commands.

No heavy allocation is held. The prior NEW allocation remains released.
Source preservation and migration inspection are light work. A new capacity
assignment is required before any new build, test or render. Rebuilt captures
must compare modes on the same imported base, not attribute all differences
from the old native/SMAA stills to FSR or the diffuse filter alone.

### November 5, 2026: safe-point wait and new capacity reservation

The parent confirmed at 13:40 EST that the existing FSR workflow remains in
its fix stage. Main and origin/main are both
7d9f6e6739b8fcd1b96fcd307c6434e3d4a8ebaf. No completed safe import point exists
yet. Do not import 9bbbea67c or the dirty fixer checkout. The parent will send
an immutable completed fix or integrated-main SHA.

The parent reserved ONE of four heavy allocations for a later bounded
prototype build/render after that handoff. This is a NEW reservation, not
reuse of either released allocation. It does not permit a build of unfinished
FSR. Check capacity and disk before heavy work, stop below 5 GB free, use Nix
and the normal GPU-lock queue. Disk currently remains about 9.2 GB free.
Current status: reserved, waiting for the safe import SHA. No heavy work runs.

The tracked patch, staged patch, untracked archive, original branch/HEAD,
file list and working-file checksums are saved under
/private/tmp/tertius-unity-noise-research-20261105/fsr-rebase-backup/.
All ten changed working files matched the saved checksums before this new
coordination note. The seven untracked files are present in the archive.
The initial backup is about 64 KB. The branch remains at 34c28466e and all
prototype modes and prior evidence remain unchanged.

Compatibility preparation remains provisional until the final import SHA:
use the new jitter-aware historyPlace helper, preserve render-size versus
output-size meanings, and update only a new capture harness to the new CLI.
The existing headless FSR options use a fixed render scale. Such stills can
compare reconstruction but do not prove automatic scale-change behavior.
No capture setting or production noise design is selected by these notes.

The parent's semantic-frame box-projection replacement proceeds independently
on ter-7tnv. Do not import these noise experiments into that work.

### November 5, 2026: bounded pre-FSR motion-video assignment

The user requested a moving-camera video of the current single-history
prototype while the safe FSR import remains pending. At 14:05 EST the parent
explicitly repurposed this thread's existing one-of-four reservation for one
bounded render and encode. This is not another allocation. A later FSR
comparison requires a fresh assignment after the safe import point arrives.

Reuse the existing Release harness at the unchanged pre-FSR base 34c28466e.
Do not change source behavior or build anything. The planned script follows
the walking person through demo-exit. It advances 64 captured warm-up frames,
then 600 captured motion frames, each after one simulation tick. The playback
copy removes those first 64 frames and runs at 60 frames per second for ten
seconds. Keep the lossless source and inspect start, middle and end against
the playback copy. State dumps and final renderer counters record the camera
and advancing simulation/snapshot time.

Keep native 1280 by 720, fixed exposure +1.57 stops, ACES, SMAA, bloom, local
shadows, screen tracing and the environment map. This is pre-FSR and uses no
dynamic resolution. Playback frames per second do not measure live renderer
speed. This is an exploratory motion view, not a matched motion comparison
or noise-quality acceptance.

Use Nix and the normal GPU queue. Run one foreground heavy command at a
time and hold further heavy work below 5 GB free. Check disk again after
the queue. Keep output bounded to one source video, one playback video,
three start/middle/end image pairs and small logs/state dumps. Preserve
existing stills, binaries and migration backups. Record completion and
explicitly release the allocation after all render/encode processes finish.
Evidence folder:
/private/tmp/tertius-unity-noise-research-20261105/single-history-motion/.

### November 5, 2026: pre-FSR motion-video result

The bounded render, encode and frame extraction finished by 14:09:50 EST.
No source behavior or binary changed and no build ran. The GPU queue wait
was 23 seconds. The original branch remains at 34c28466e. Source and binary
checksums matched after completion. No FSR state was imported.

The lossless source has 664 decoded frames at 60 playback frames per second.
The trimmed MP4 has 600 decoded frames and lasts exactly ten seconds. Its
first frame follows tick 464 at 7.7333333333 seconds. Its last follows tick
1063 at 17.7166666667 seconds. Camera poses change between the two state
dumps. The renderer reports 696 draws, final history draw index 695 and
final snapshot clock 17.716666666666665 seconds. Thus the clip uses advancing
snapshots rather than a frozen warm-up. All 664 exposure records are +1.57
stops and both state dumps report no simulation errors.

Inspected the lossless and encoded start, middle and end at full resolution.
The start looks through the cabin structure. The middle looks across the
deck. The final image is severely washed out with a blue-white cast in both
copies. Constant exposure and lossless-source inspection rule out automatic
exposure adaptation and MP4 encoding as explanations. They do not identify
the renderer cause. No matched original-engine or long-only motion control
was rendered. Do not assign the cause to history feedback or accept motion
quality from this clip. The playback copy softens some fine grain because
it uses lossy video compression. Keep the RGB lossless source for review.

Evidence totals about 369 MiB. The ten-second MP4 is about 5.3 MiB and disk
remains about 7.3 GiB free. Exact commands, clocks, camera poses, decoded
frame counts and inspection notes are in single-history-motion/manifest.md.
Playback frames per second are not a live-performance measurement.

The REPURPOSED allocation is RELEASED and the parent was notified after all
render/encode/extraction processes ended. There is no simultaneous later-FSR
reservation. A new assignment is required for a later imported-base render.
No further fix, heavy work, import, integration or quality acceptance follows.

### November 5, 2026: approved immutable FSR import and fresh allocation

At 14:16 EST the parent approved the exact completed FSR checkpoint
96db37df965c7b35dae499a2f34410d2c3b35011 for this standalone comparison.
The parent reports terminal workflow success, settled members, cleanup and
matching Checked/Release records. This is not hardened or merged-main FSR
acceptance. Main remains separate. Import the exact SHA, not a moving ref
or later parent hardening work. No box work is part of this import.

A FRESH one-of-four allocation is assigned for this migration's bounded build
and matched warmed views. The pre-FSR video allocation remains released.
Refresh the patch, untracked archive and checksums with all latest notes.
Verify ancestry, fast-forward only this existing research branch, then reapply
the preserved edits. No prototype commit, new checkout, child or workflow.

Reconcile the existing prototype only with changed FSR jitter, active-size and
image interfaces. Preserve its selected history policy, exposure, ray settings
and hit-light source. Do not fix or retune the provisional pre-FSR washout.
Ask at any real unresolved behavior fork rather than changing the policy.
Preserve the old binaries and evidence as controls. Compare modes on the same
new base and distinguish output dimensions from rendered dimensions.
Headless fixed render scale does not test live dynamic resolution.

Use Nix and the normal GPU queue. Check current capacity and disk. Run one
foreground heavy command at a time and hold below 5 GB free. No full suites,
coverage, mutation or extra experiment. Inspect and show the bounded results,
record clocks/reset/configuration provenance, then release the allocation.

### November 5, 2026: exact FSR checkpoint imported, prototype preserved

Verified that 96db37df965c7b35dae499a2f34410d2c3b35011 descends from the
original 34c28466e base. Refreshed the complete tracked patch, empty staged
patch, seven-file untracked archive and all ten working-file checksums under
fsr-import-96db37df9-backup. The archive includes the latest video notes and
fresh allocation. All checksums passed before the move. A preservation stash
also remains recorded in that backup, not dropped after reapplication.

Fast-forwarded this existing branch to the exact approved SHA and reapplied
all dirty work successfully. No checkout switch, prototype commit or main
write occurred. ARCHITECTURE merged its upstream changes. TemporalSignal
and all untracked files initially remained unchanged after reapplication.

The only prototype code adaptation replaces its previous-image coordinate
formula with historyPlace. This shared helper applies current/previous pixel
jitter using the corresponding active render sizes. The existing spatial
projection already calls the shared jitter-aware helpers. Its bounds use
frame.viewport, the active render rectangle. Previous tap bounds use
frame.previousViewport. Dispatch uses FrameContext.Extent, while image
allocation uses RenderTargets.Extent. Thus no separate prototype size model
or image interface is needed. History acceptance, blend weights, filter
radius, hit-light source and ray-budget settings stay unchanged.

The bounded comparison uses the selected single-history mode on this same
new base, native versus fixed 2/3 FSR, in the two existing scenes. Both output
1280 by 720. The CLI now uses --upscaling off|fsr3 instead of the removed
SMAA option. Native has no anti-aliasing. FSR includes its upstream jitter,
material mip bias and reduced main-view shadow footprint. These are whole
pipeline settings, not an isolated upscale pass comparison. Headless scale
is fixed, so these captures do not test live dynamic-resolution transitions.
Old binaries, stills and the motion clip remain separate pre-FSR controls.

### November 5, 2026: bounded same-base native/FSR still comparison

The migrated Release harness built successfully on a sequential retry with
shared compilation and node reuse disabled. No source fix separated the
attempts. The first ordinary build ended with code 1 and no diagnostic.
The successful retry reports zero warnings/errors. No automated suite or
hardening ran for this explicitly authorized throwaway comparison.

Four matched captures completed by 14:38:20 EST. All use single-history mode
on exact 96db37df965c7b35dae499a2f34410d2c3b35011. Native renders and outputs
1280 by 720. Fixed 2/3 FSR renders 848 by 480, after eight-pixel alignment,
and outputs 1280 by 720. Sharpening stays off. Each run reports 65 renderer
draws and final history draw index 64. These are 32 hidden initial warm-up
draws plus 33 advancing film captures. Final clocks match within each pair:
deck 7.2 seconds and reflections 0.7166666666666667 seconds. Each exposure
file has 33 constant records at +1.57 or -6.8 stops. No frozen-snapshot loop
or extra renderer-history reset substitutes for the advancing film.

Opened all four final PNGs at their original output resolution. FSR looks
softer and shows fewer fine speckles in both views. Broad deck-floor patches
remain, with some fine repetitive texture visible. The reflection test still
has grain around the bright reflected strip and streaks beneath the sphere.
FSR changes the full pipeline and sample locations, not only one filter.
No reference-based brightness/detail or noise-quality acceptance follows.
No motion or live dynamic-resolution transition ran in this comparison.
The pre-FSR motion washout remains unresolved and was not fixed or retuned.
These earlier-time stills do not establish that it disappeared.

The captures waited normally for 620, 0, 0 and 4 seconds through the GPU
queue. No holder was interrupted or bypassed. All commands finished before
the FRESH allocation was RELEASED and the parent notified. No further heavy
reservation remains. New evidence totals about 78 MiB and disk remains about
7.5 GiB free. The old pre-FSR binary checksums still match. Only the four
selected new PNGs remain, plus bounded logs, records, binaries and archives.

Exact commands, dimensions, history/reset provenance, source identity and
visual limitations are in fsr-comparison/manifest.md. Whole-frame timing
logs remain unclocked, sequential observations, not isolated pass cost or
live performance. The branch remains at the approved immutable checkpoint
with uncommitted optional prototypes. No main integration, commit, merge
commit, production acceptance, full suite, child or workflow occurred.

### November 5, 2026: user correction to the intended comparison dimensions

The user specified native 1920 by 1080 versus FSR from 1280 by 720 to
1920 by 1080, the intended default configuration. The previous experiment
instead used 1280 by 720 output with FSR starting at 848 by 480. That is a
lower-input configuration and does not answer the intended default comparison.
Do not generalize its observed softness into an inherent FSR quality penalty.

The corrected experiment will keep the same pinned base, built harness,
selected single-history mode, two scenes, fixed exposures, film clocks and
other settings. Change only the requested output size to 1920 by 1080 and
keep FSR fixed at 2/3. Confirm actual active/output dimensions from the
renderer record rather than assuming them. Preserve the previous stills and
their provenance in their own folder. No source edit, rebuild, history retune,
ray/source change, new import or washout fix is needed for this correction.

The earlier allocation remains RELEASED. A FRESH allocation was requested
from the parent for four corrected warmed stills. Until it arrives, only
light script/provenance preparation is authorized. Use Nix, the normal GPU
queue, one foreground heavy command and the 5 GB stop after assignment.
Fixed headless scale remains distinct from live dynamic-resolution behavior.

At 14:46 EST the parent assigned a FRESH one-of-four allocation for these
four warmed stills. This assignment uses only the existing built harness.
At 14:48 EST disk is 7.45 GiB free, memory 54% free and load 3.32/6.05/5.92.
The branch remains at exact 96db37df965c7b35dae499a2f34410d2c3b35011 with
the selected dirty prototype. The corrected script checks disk before each
run and after the normal GPU queue. It holds below 5 GiB and keeps only
each new run's final PNG plus bounded records. All prior evidence remains.
The allocation will be released after all render processes finish, including
on failure. This does not authorize a build or any source behavior change.

The first corrected batch ended at 14:52 EST after three completed captures.
Before the fourth run, free disk fell to 4,701,872 KiB. The script exited 75
under the 5 GiB guard. No FSR reflections render started. All render
processes finished and this allocation is RELEASED. The parent received the
release and a request for only the missing still. Disk recovered without
cleanup here to 7,569,868 KiB at 14:52:32. No further heavy work starts
without a fresh assignment.

The renderer records confirm native deck 1920 by 1080 and FSR deck internal
1280 by 720, both output 1920 by 1080. Native reflections are also 1920 by
1080. All three PNG headers confirm that output size. All three runs have
65 draws, final history index 64 and 33 advancing captured frames. Exposure
records stay exactly +1.57 stops for deck and -6.8 for reflections. Deck
clocks match at 7.2 seconds. Native reflections end at 0.7166666666666667
seconds. The normal GPU waits were 24, 70 and 3 seconds. No holder was
interrupted or bypassed. Verification is in fsr-comparison-1080p/verification.json.

Opened all three originals. The corrected deck FSR still shows less fine
grain than native. Broad floor patches remain in both. Faint repeating
structure is visible on the FSR floor. Thin beams and doorway edges differ,
but this pair does not isolate lower input size from temporal reconstruction,
jitter, texture detail selection or shadow sampling. No inherent FSR quality
penalty, motion stability, live controller behavior or acceptance follows.
Reflections have no corrected pair yet. The existing washout remains unchanged.

At 14:53:30 EST the parent assigned ONE FRESH allocation for only the
missing FSR reflections still. Parent measured 7,584,348 KiB free, load
2.62/4.61/5.36 and memory 53% free. No completed capture will be rerun.
The partial batch and exit 75 remain preserved. The same 5 GiB guard runs
before and after the normal GPU queue. All settings and the built harness
remain unchanged. This new allocation will be released as soon as this
single capture and all owned heavy processes finish, including on failure.

The single FSR reflections capture finished successfully at 15:06:57 EST.
It waited 648 seconds in the normal GPU queue without interruption or
bypass. The post-queue guard passed with 7,617,384 KiB free. All owned
heavy processes finished. This FRESH allocation is RELEASED and the parent
was notified at 15:07 EST. No further heavy allocation remains here.
Free disk at 15:07:49 is 7,612,288 KiB. The earlier partial batch and exit
75 remain preserved beside the successful single-capture follow-up.

All four corrected originals now exist and were opened for inspection.
Renderer records confirm native internal 1920 by 1080 and FSR internal
1280 by 720, all output 1920 by 1080. All PNG headers confirm output size.
All four have 65 draws, final index 64 and 33 constant exposure records.
FSR sharpening stays zero. Both deck clocks are 7.2 seconds. Both
reflections clocks are 0.7166666666666667 seconds. The prototype source
checksums match before and after every capture. No source behavior changed.

The corrected FSR reflections still shows fewer isolated bright speckles
around the vertical light reflection and the sphere. Reflection grain and
streaks under the sphere remain. The deck pair retains broad floor patches
and faint repeating structure on parts of the FSR floor. These are visual
observations of the complete configurations, not an isolated FSR effect,
detail/brightness acceptance, motion stability or live performance evidence.
The unresolved motion washout remains unchanged. Fixed-scale stills do not
validate the dynamic-resolution controller. The original lower-output
comparison stays separate and is not generalized to the intended default.

Exact commands, binary/source identity, clocks, reset/warm-up provenance,
metadata checks, final dirty patch, seven-file untracked archive, image
checksums and four-original comparison HTML are in fsr-comparison-1080p/.
No build, test suite, coverage/mutation run, import, commit, main write,
child, workflow, completed-capture rerun or production acceptance occurred.

### November 5, 2026: next grain-control discussion after corrected FSR stills

The user says image quality under FSR looks great and asks what reduces
grain next, especially bright spots. The user sees the prototype as close
to later integration after FSR. Record this as positive still-image feedback,
not a merge instruction, production acceptance or a completed motion check.
No heavy allocation is held. This discussion changes no source behavior.

The optional single-history filter applies only to diffuse light. Reflections
still use the existing accumulation path. The new spatial filter weights
surface geometry and distance, not brightness differences. Temporal acceptance
tests geometry, bounds, reset and exposure, not whether a new finite light
estimate is unusually bright. Existing maximum radiance and ray-weight caps
protect numerical range. They do not prove that visible bright noise is
controlled. Nor do the stills prove which component carries a bright dot.

The proposed next target is the influence of unusually bright incoming ray
estimates before temporal accumulation. First distinguish diffuse and reflected
contributions at the remaining dots. Then isolate one reversible input-limiting
experiment in the implicated component. Keep the current history policy,
spatial filter, ray configuration, hit-light source and output comparison fixed.
The pinned Unity SSGI shader limits its exposed final sample before storing
it. That is a feasibility reference, not a transferable numerical threshold.

| Offered design | Mechanism | Cost and risk |
| --- | --- | --- |
| Fixed brightness ceiling, recommended only for the first diagnostic | Rescale a new indirect-light estimate when its brightness exceeds an explicit ceiling, before temporal blending. Preserve the overflow guards and leave direct light untouched. | Small arithmetic change and no second lighting history. Genuine bright bounced light or reflections can lose energy. The threshold and exposure convention remain undecided. |
| Same-surface neighbor-relative soft limiter | Compare new estimates on compatible nearby surfaces and reduce the influence of isolated bright estimates. Do not restore a separate short lighting history. | Additional neighbor reads and policy complexity. Thin genuine highlights or a newly visible small source can resemble an outlier. |

No design or threshold is selected. A lower-noise but incorrectly darker
image is not success. A later authorized comparison must retain real bright
features and record scene brightness, as R1 requires. The earlier motion
washout remains unresolved and separate from this still-image improvement.
These discussion notes postdate the corrected-capture preservation archive.

### November 5, 2026: Design B selected; component scope pending

The user selected the same-surface neighbor-relative soft limiter, not the
fixed brightness ceiling. The limiter acts on new indirect-light estimates
before temporal accumulation. Keep the selected history policy, spatial
filter, rays, exposure and hit source unchanged. Preserve an off switch.

Parent assigned ONE FRESH allocation at 15:47:30 EST for focused red-green
checks, one bounded build path, four warmed deck/reflections off/on FSR
stills (actual internal 1280 by 720 to output 1920 by 1080), and three
paired timing rounds per scene. Alternate which mode runs first. Each
timing run has at most 60 advancing warm-up draws and 120 measured draws,
with image saving off. Record GPU clocks, whole-frame milliseconds, paired
differences and spread. Unmatched clocks or noisy timing are inconclusive,
not grounds for extra runs. No isolated limiter cost is implied. Target
new evidence below 300 MiB; hold above 500 MiB or below 5 GiB free disk.
Use Nix and the normal GPU queue, one foreground heavy command at a time.
Release after all owned commands end, including failure or guarded stop.

Source inspection confirms a component-scope fork. TemporalSignal selects
the single-history path only for diffuse light. Reflected light retains its
existing temporal path. DemoScenes.Reflections defines the sixth floor
strip with Metallic equal to one. lighting.glsl and temporal.glsl give it
a zero diffuse material factor. A diffuse-only limiter therefore cannot
directly suppress that strip's reflected estimates, although feedback may
still couple components over later frames. Final scene color alone does
not identify which component carries every dot. Offer diffuse-only versus
both-component limiting before implementation; do not silently extend the
chosen history policy to reflections. At 15:52 EST no heavy command or
implementation has started. The allocation remains reserved pending scope.

### November 5, 2026: reflection toggle resolves prototype scope

The user requested a toggle for applying Design B to reflected light. Use
one research switch with off, diffuse-only and both-component values. Keep
reflection-history rules unchanged. The authorized four stills and twelve
timing runs compare off with both enabled. This exposes the larger cost and
the risk to real reflection highlights without expanding the capture matrix.
Diffuse-only remains selectable and is covered by focused behavior checks.
The current allocation now covers this settled removable prototype.

Reuse the existing shader specialization mechanism so the off control has
no neighbor reads. The same shader helper limits diffuse and reflected new
estimates before either temporal blend. It adds no lighting history or pass.
The initial removable settings will be recorded explicitly with the evidence;
none is a production threshold. No ray/source/exposure/history-policy change,
import, suite, hardening, commit, integration or acceptance is authorized.

### November 5, 2026: removable Design B evidence

Implemented `TERTIUS_NEIGHBOR_LIMIT_PROTOTYPE=off|diffuse|both`. Unset is
off. The user requested selectable reflection limiting, not a reflection
history redesign. Diffuse keeps the selected single-history spatial path;
reflections keep their original temporal path. Both call one shared input
limiter. The existing shader-specialization mechanism disables its neighbor
reads in the off control. No new pass, image or history is allocated.

The initial research settings are eight neighbors at a two-pixel stride,
outside the center's shared 2 by 2 ray block; the existing same-surface
depth/normal/model test; and, for reflected estimates, a roughness difference
of at most 0.1. At least four finite, nonnegative neighbors must pass. Sample
brightness means the largest RGB channel of the pre-exposed, material-
demodulated new estimate. The reference is the largest neighbor brightness.
The limiter leaves estimates up to four times the reference unchanged. Above
it, soft compression approaches eight times the reference and scales RGB
together. Supported all-black neighbors give zero. Too little support leaves
the sample unchanged. These are removable experiment settings, not accepted
production thresholds. Multiple bright neighbors can protect each other;
real narrow highlights can be mistaken for isolated noise.

Focused real-GPU behavior checks first produced four expected failures and
four passing controls. The four failures disappeared after implementation:
eight cases pass. They cover a shared-ray bright block in diffuse and
reflections, preserved uniform bright regions, RGB ratios, the reflection
off switch, diffuse-only activation, sparse finite support and relative
scaling. Existing same-surface predicates are reused; these eight cases do
not constitute geometry-edge hardening. The first build had two test-style
errors, corrected before the behavioral red run. The Release capture harness
then built with zero warnings/errors. No full suites or hardening ran.

Four warmed FSR stills completed at 16:11:09 EST. Each has actual internal
1280 by 720 and output 1920 by 1080, 65 total draws, final index 64, 32 hidden
initial draws and 33 advancing captured frames. Deck ends at 7.2 seconds;
reflections ends at 43/60 seconds. Exposure remains +1.57 and -6.8 stops,
respectively. Sharpening is zero; rays, hit source, bloom, shadows, environment
and spatial/history settings are unchanged. Both off PNGs are byte-identical
to the preserved corrected FSR controls. All four originals were opened.

The deck changes little. The reflections image has fewer isolated bright
dots around the light reflection and smoother streaks beneath the sphere.
However, the sphere's shadowed side is visibly darker. A fixed rectangle on
that side changes from mean display gray 55.63 to 35.32 on a 0 to 255 scale
(-36.51%). The light-reflection surround changes from 89.74 to 84.15 (-6.23%).
The deck-floor rectangle changes by -0.047%. These are final display-image
measurements, not linear HDR energy or a proof that removed light was noise.
The images do not isolate diffuse from reflection limiting. Diffuse-only was
not rendered as an extra matrix; its toggle is covered by focused checks.

The twelve bounded timing runs finished at 16:13:16 EST. Every run has 60
advancing warm-up captures followed by 120 measured captures, with image
saving off. The renderer also makes 32 hidden initial draws at the first
snapshot. Each run ends at 212 total draws, final index 211. Measured draw
indices are 92 through 211 with no repeats. Measured clocks advance by 1/60
second; matched modes use exactly the same clocks and exposures. Deck ends
at 579/60 seconds, reflections at 190/60. Each scene has three pairs, ordered
off/both, both/off, off/both. The existing gpu-clock wrapper sampled the actual
measured span between timed-start and timed-end markers.

| Scene and pair | Off whole-frame mean, ms | Both whole-frame mean, ms | Difference, ms (%) | Mean GPU clock match |
| --- | ---: | ---: | ---: | --- |
| Deck 1 | 15.848 | 15.794 | -0.053 (-0.34%) | No; 7.03% apart |
| Deck 2 | 15.634 | 15.927 | +0.293 (+1.87%) | No; 4.15% apart |
| Deck 3 | 15.888 | 15.951 | +0.063 (+0.40%) | No; 7.19% apart |
| Reflections 1 | 10.577 | 10.308 | -0.269 (-2.54%) | No; 14.84% apart |
| Reflections 2 | 10.887 | 11.771 | +0.884 (+8.12%) | Yes; 0.04% apart |
| Reflections 3 | 12.267 | 11.083 | -1.184 (-9.65%) | No; 20.95% apart |

Overall performance is INCONCLUSIVE. Five pairs fail the existing 3% mean
clock-match rule. The one matched reflection pair is about 1034 MHz, not the
1296 MHz reference clock; its per-frame p10/p90 times are 9.425/12.349 ms off
and 10.086/13.421 ms on. It is one noisy pair, not a robust cost estimate or
evidence that overhead is negligible. Raw paired differences span -0.053
to +0.293 ms on deck and -1.184 to +0.884 ms on reflections. Whole-frame
differences can include altered adaptive ray work. No isolated limiter cost,
accepted production budget or live-window rate is established. No extra runs
were added to rescue the timing result.

All owned processes ended and the ONE allocation was explicitly RELEASED to
the parent at 16:14 EST. At 16:14:26 disk was 7,069,208 KiB free. Evidence is
about 46 MiB, including the new harness and copied 35 KiB clock executable.
Only owned intermediate PNGs were removed; the four final originals, old
binaries, controls and earlier archives remain. Full commands, source/binary
hashes, before/after patches and untracked archives, per-frame measurements,
GPU-clock samples and checks are under
`/private/tmp/tertius-unity-noise-research-20261105/neighbor-limit/`.

Exact HEAD remains 96db37df965c7b35dae499a2f34410d2c3b35011. No imports,
commits, suites, coverage/mutations, children, workflows, main writes or
integration occurred. The motion washout is unchanged and untested here.
Neither still quality nor production performance is accepted by this result.

Subsequent parent coordination reports FSR/dynamic resolution merged and
pushed at 23d603077ac306722cd1c4e55f8459480fc8b1e8, with combined tested
code-input ba5127acef21489cc12c01efc090351788f7c0ef. This is parent-supplied
coordination evidence, not an import or a new verification by this thread.
The user explicitly says to preserve this worktree and do lightweight
analysis only. A later user-requested migration would need full dirty
preservation and separate capacity. The independent production-window video
does not import this noise prototype. The released allocation stays released.

### November 5, 2026: reflective-plate mottling review

The user reports excellent overall improvement but questions broad mottling
or swirls on the reflections scene's plate. They ask whether the plate is
intended as a mirror or whether reconstruction creates the pattern. This is
an open visual-quality concern, not acceptance or authorization to retune.

Lightweight source inspection confirms the following scene expectations:

| Scene part | Authored material and geometry | Expected interpretation |
| --- | --- | --- |
| First gray strip, at negative X | Requested roughness 0, floored to 0.045 | The closest supported mirror, not a mathematical perfect mirror. |
| Remaining gray strips, in increasing X order | Roughness 0.1, 0.2, 0.3 and 0.45 | Increasing spread of reflected light, not an authored swirl texture. |
| Last strip, at positive X | Gold-colored metal, roughness 0.1 | A colored, smooth reflection. |
| All six strips | Flat box meshes, constant material factors, no color, normal or metallic-roughness maps | No deliberately wavy surface or painted mottling. Real reflected lighting can still vary across each strip. |

The primary scene evidence is `DemoScenes.Reflections.cs`, the
`MaterialDesc` defaults in `RenderSeam.cs`, and the roughness floor in
`GgxLobe.cs` / `brdf.glsl`. Each strip is 2 m wide and 12 m long. The
front camera looks along negative Z, so negative X is image-left.

Component inspection also corrects a possible causal shortcut. The new
single-history spatial filter is selected only for diffuse light in
`TemporalSignal.cs`. Reflections retain `accumulate_temporal.comp` and
`accumulate_spatial.comp`, including short-history neighborhood repair and
the long/fast-history clamp. The new input limiter can also affect
reflections when its switch is `both`; that is a different operation.

The diffuse filter uses the same 16 projected disk positions at each
application. Its output is retained as the next diffuse history. Repeated
spatial averaging of retained estimates can spread localized sampling errors
into broader patches. This is a plausible mechanism to investigate, not
proof that the fixed sample pattern causes these particular swirls.
Furthermore, `PartImages.LastFast` aliases the retained diffuse image in
single-history mode, and `screen_trace.glsl::hitLightOf` includes it when
lighting later ray hits. Thus a diffuse-filter change can influence later
reflections indirectly without directly filtering the reflection history.

Opened the existing limiter-off and limiter-both reflection originals
again. Broad uneven shading remains in both, while isolated bright dots
decrease with the limiter. Both images already contain the single-history
diffuse spatial prototype, so this pair cannot isolate that filter's effect.
Also opened the earlier native-720p original control and long-only spatial
candidate. Those provide context, not a matched comparison against the
current FSR/single-history state.

No independent reference currently distinguishes correct lighting gradients
from residual sampling, history, spatial-filter or reconstruction errors.
The swirls are not authored material detail; their exact cause remains
unproven. A useful next diagnostic is to inspect diffuse and reflected-light
components separately under matched conditions before changing roughness,
filter settings or brightness. No such capture is started or authorized by
this source-reading discussion. No implementation, test, build or render
ran. HEAD and prototype behavior are unchanged; the allocation remains
RELEASED.

### November 5, 2026, 16:54 EST: diffuse-only limiter still requested

The user requests the existing neighbor limiter enabled for diffuse light
but disabled for reflections before continuing the swirl investigation.
This selects `TERTIUS_NEIGHBOR_LIMIT_PROTOTYPE=diffuse`, not a change to the
single-history spatial filter, material roughness or reflection history.
One matching warmed reflections still is sufficient; existing off/both
originals remain the controls. No new build or timing run is needed.

Requested one fresh bounded render allocation from the parent. No allocation
is inferred from the earlier release. Before any heavy command, wait for
explicit assignment and use Nix plus the normal GPU queue with the 5 GiB
disk guard before and after waiting. The planned capture retains exact
HEAD96db37df9, the existing built harness, 1280x720 internal -> 1920x1080
FSR, exposure-6.8, sharpening0, all other settings and the same advancing
snapshot clocks/warm-up. At16:54:42EST free disk is6,793,832KiB, memory53%
free, load1.71/2.41/3.02. All twelve retained source/binary/script hashes
still match. Preparation is lightweight only; no render has started.

The parent explicitly assigns ONE fresh bounded capture allocation using
released ter-570o capacity. Its16:55:22EST check reports6,799,220KiB free,
memory53% free, load2.01/2.41/2.99, with no engine render/GPU holder seen.
Only the single existing-harness still is authorized. Release the allocation
directly to the parent immediately after the render ends, including failure
or a disk-guard stop. Inspection and documentation do not retain the slot.

The one requested diffuse-only limiter still finishes successfully at
16:56:32EST. The new original is
`neighbor-limit/stills/reflections-diffuse/front-000032.png` under the
existing evidence root. Its log confirms actual1280x720 ->1920x1080 FSR,
sharpening0, exposure-6.8, single-history and limiter=diffuse. There are65
total draws, final index64 and snapshot clock43/60 seconds, matching the
off/both controls. The32 hidden initial draws remain distinct from the33
advancing captures. Exposure records match the both control byte-for-byte.

The new original and existing both original were opened. More bright
speckles appear around the reflected bar when reflection capping is off.
The sphere's shadowed side remains dark, and broad uneven plate shading
remains. These observations do not identify the source of the swirls.
Both images retain the diffuse spatial filter and single-history behavior.

No existing original or binary is replaced. Only the new run's32 intermediate
images are removed after success. Exact hashes, settings, command and limits
are in `neighbor-limit/diffuse-only.md`; the complete evidence root is
47,904KiB. Free disk stays above5GiB before/after queue and before capture,
ending at6,790,768KiB. All owned render processes finish. The ONE allocation
is explicitly RELEASED to the parent immediately at completion. No build,
timing run, import, behavior edit, swirl fix or quality acceptance occurs.

### November 5, 2026: selected all-on prototype cleanup

The user reports tiny clustered smudges that improve but remain with
reflection capping off. They select all current prototype components on
for continued research and request simplification before the next smudge
experiment. This selects single-history diffuse spatial filtering plus
neighbor capping for both signals. It is not production or merge acceptance.

Parent assigns ONE fresh bounded cleanup allocation at17:04:48EST:
focused behavior checks, one existing-harness Release rebuild and at most
one matched warmed reflections still. No limiter/reference/history/ray/
source/exposure/roughness policy change is authorized. Nix, the normal
GPU queue and the5GiB disk guard remain required. Release immediately
after all owned heavy work finishes.

Before changes, the complete dirty tracked patch and untracked archive
are preserved in `all-on-cleanup/before/` under the evidence root. Their
SHA256 values are48b06437a3e17cbf4e0e7178810b7ba4c087dc36702bedf69c1c708fa54f573b
and73faa701e2bcc5b842b87ddf7e0a950f2fb42da606a87816b386bb4450ff7801.
Old harness/rendering binaries and all controls stay untouched.

The original eight limiter checks pass before implementation. New control
checks then produce three behavioral failures because the new master
setting does not activate the old two-setting implementation. Five controls
pass; no compilation/source-shape failure is counted as red. A first green
attempt finds an overly strict test bound: it expected less than10 for a
sample whose neighboring red channel is4. The unchanged limiter can retain
nearly32 there. Correct the assertion to the original relative-reduction
criterion rather than retuning rendering. Both original missing-control
cases returned2000, so they still distinguish missing activation.

The cleanup uses only `TERTIUS_INDIRECT_LIGHT_PROTOTYPE=1`. Unset or0 is
the original whole-prototype control. Remove the superseded independent
mode reads and the long-only filter/copy pipeline pair and shader wrappers.
Leave the chosen temporal/spatial shaders and limiter algorithm unchanged.
Copies in archived evidence retain all old modes. The next reference
statistic or temporal outlier policy remains a separate user decision.

Final focused result is8/8 pass. The one Release harness rebuild succeeds
with0 warnings/errors in a separate evidence directory, preserving old
DLLs. The only new warmed still is byte-identical to the prior both-enabled
original, SHA25657182fe5ff69666c07c3a728ce6c3bea92c2fe6c8a115dbe9ddb9ede29e26b5c.
Exposure JSON also matches byte-for-byte. Metadata confirms1280x720 internal
->1920x1080 output FSR, sharpening0, exposure-6.8,65 draws/index64 and
snapshot clock43/60 seconds. The original is opened. No smudge or highlight
change is intended or observed from cleanup. `git diff --check` passes.

Every phase stays above5GiB free. All owned tests/build/render processes
end at17:13:24EST, with6,456,456KiB free. The ONE allocation is immediately
RELEASED directly to the parent. Full commands/hashes/results/limits are
in `all-on-cleanup/manifest.md`. No further heavy work is assigned here.

### November 5, 2026: clustered-smudge designs for discussion

The current limiter references the largest compatible neighboring estimate,
not a typical neighboring estimate. One bright neighbor can therefore raise
the allowed brightness for another. This is a confirmed mechanism by which
a bright cluster can avoid limiting, not proof that every visible smudge
comes from that mechanism. Reflection capping off did not remove all of
the user's observed pattern. Selected filtered-history feedback and the
original reflection reconstruction remain possible contributors.

Two next designs are offered, with no algorithm selected or implemented:

| Design | Brightness evidence | Expected structural cost | Main risk |
| --- | --- | --- | --- |
| Typical-neighbor reference, recommended for the next narrow experiment | Replace the largest neighbor with the median, the middle brightness among compatible current estimates. A minority of bright samples cannot alone raise the reference. | Keep the current eight neighbor positions and add a small selection operation. No extra image or history. Cost is not measured. | A real thin reflection can also be a bright minority; a large cluster can still dominate the neighborhood. |
| History-supported reference | Use the typical-neighbor reference but allow more light when valid retained history already predicts a bright feature at the same surface point. | Reuse existing retained light, with additional validation/response logic. No second lighting-history image. Cost is not measured. | An old erroneous patch can support itself. New thin highlights can still be limited before history supports them. Retained filtered light is not independent ground truth. |

For example, the first design reduces a small bright island in mostly dark
neighbors even when its members protect each other under the largest-value
rule. The second design can preserve a thin reflection that valid history
already contains, despite darker neighbors.

Both keep the selected all-on reconstruction, act on new ray estimates
before temporal blending, scale RGB together rather than averaging nearby
reflection colors, and require evidence that real highlights survive.
Neither is promised to remove all reflective-surface texture. The user must
settle the evidence/bright-feature trade-off before a new limiter prototype
or a fresh heavy allocation is requested.

### Selected next experiment: median neighbor reference

On November 5, 2026, the user chooses the narrow median-only experiment.
Replace the largest compatible-neighbor brightness with the median of those
same brightness values. For an even valid count, use the average of the two
middle values. Keep the eight sample positions, compatibility tests, minimum
four-neighbor support, four-times soft knee, RGB scaling curve, both-component
scope, single-history filtering, rays, source, exposure and FSR unchanged.
No retained-history support is added. Thin real highlights remain at risk.

Before editing, preserve the full tracked patch, untracked archive and
current all-on harness/rendering DLL and final image hashes under
`/private/tmp/tertius-unity-noise-research-20261105/median-reference/before/`.
The current maximum-reference originals and binaries remain untouched.
Fresh bounded capacity is requested for focused behavioral red/green checks,
one separate-copy harness Release build and one matched warmed reflections
still. No heavy command starts before assignment. No timing is requested.

Parent assigns ONE fresh bounded allocation at17:33EST. The focused GPU
tests run against the existing largest-neighbor implementation first:
eight existing cases pass and all seven new cases fail behaviorally.
Both adjacent-bright-block cases fail to reduce the center enough. All
five support-count cases retain1000 rather than the median-based soft
compression. The cases exercise four through eight finite neighbors,
including odd counts and the average of two middle values for even counts.
The source then changes only reference selection to a local insertion sort
of at most eight brightness values. The minimum support and compression
curve remain unchanged. See `median-reference/logs/red.log` in the evidence
root. Green/build/capture results remain pending at this point.

The focused green run passes15/15 cases. One separately copied existing
harness Release build succeeds with0 warnings/errors. One matched warmed
reflections still finishes at17:37:22EST, and the ONE allocation is
immediately RELEASED directly to parent before lightweight inspection.
All owned heavy processes end. All Nix/GPU queue/disk guards hold. Old
max-reference binaries and images retain their hashes.

New original: `median-reference/stills/reflections-median/front-000032.png`
under the evidence root. Actual internal1280x720 -> output1920x1080 FSR,
65 total draws/index64, snapshot clock43/60s, exposure-6.8, sharpening0.
The exposure record matches the preserved all-on control byte-for-byte.
The reflected lamp's bright speckled fringe shrinks, and small bright
patches on the red box and blue sphere diminish. The red box's right face
and sphere's shadowed side also darken. Lost brightness is not automatically
noise. A broad dark patch near the rear of the plate remains. Both
originals are inspected and shown with matched detail crops.

The median changes brightness evidence, not reflection-color averaging.
This still does not prove correct light energy, survival of real narrow
highlights or stable motion. Fixed-scale FSR does not test the live
dynamic-resolution controller. No timing experiment runs. Added sorting
has no extra image reads or passes, but its GPU cost remains unmeasured.
Automatic capture timestamps do not isolate that cost. The existing
performance finding remains INCONCLUSIVE. No production acceptance,
import, commit, full suite or further algorithm change occurs. The
evidence manifest, compact logs, hashes and full dirty archives are under
`median-reference/`, about42MiB after removing only its intermediate PNGs.

### November 5, 2026: reflection edges and an independent light-spread reference

The user reports much less swirling with the median, but objects still
have grain and patches around their reflected boundaries. This can make
a smooth surface look rougher than its material. They are concerned that
limiting removes real light and pulls a reflection's boundary inward.
They request exploration of both a small edge-detection kernel and an
ideal same-roughness reference while they are away. This is research,
not acceptance of the median or authorization for another algorithm.

No heavy allocation is held or requested for this reading. No engine code,
shader, test, build or capture changes. All ten entries in the median
experiment's capture-input checksum file still pass. HEAD stays at the
same pinned FSR research checkpoint. External text-source snapshots and
checksums are under `edge-reference-research/` in the evidence root.

#### What the current source establishes

| Fact | Consequence | Source |
| --- | --- | --- |
| The plate has six different strips. The red box spans the smoothest two. The glowing bar stands above roughness0.2. Authored roughness0 becomes0.045. | Do not judge the bar against a perfect mirror or sharpen every strip to one width. Grain is not intended roughness, but some smooth light spread is intended. | `src/Tertius.Demo/DemoScenes.Reflections.cs:28`, `src/Tertius.Demo/DemoScenes.Reflections.cs:158`, `src/Tertius.Rendering/Shading/GgxLobe.cs:6` |
| Median limiting uses the receiving surface's depth/normal/model and nearby raw brightness, not reflected-object boundaries. | On a flat strip, both sides of a reflected object can pass the surface test. A real narrow highlight can look like a bright minority. | `src/Tertius.Rendering/Shaders/neighbor_limit_prototype.glsl:6` |
| The same limiter runs before diffuse and reflected accumulation. | Darkening on the directly viewed box/sphere is not necessarily a reflection-filter defect. A reflection-only sharpening pass cannot restore diffuse light removed earlier. | `src/Tertius.Rendering/Shaders/accumulate_temporal.comp:136`, `src/Tertius.Rendering/Shaders/accumulate_diffuse_single_temporal.comp` |
| The smooth-reflection history cap is4, with a fast cap4. After reaching that cap, each fresh estimate still receives one quarter of the temporal blend. | More warm-up does not make the noise disappear as it does in an unlimited sample average. This is a confirmed response rule, not proof that it causes every visible patch. | `src/Tertius.Rendering/Frame/SignalParameters.cs:65`, `src/Tertius.Rendering/Shaders/accumulate_temporal.comp` |
| Reflections still use the original short-history repair and fast-history clamp. The new projected-disk spatial filter directly handles diffuse light only. | The remaining texture cannot be attributed solely to the new diffuse filter. Surface-only spatial repair and neighborhood clamping remain possible contributors. | `src/Tertius.Rendering/Shaders/accumulate_spatial.comp:29`, `src/Tertius.Rendering/Frame/TemporalSignal.cs` |
| Resolved ray weights already have a separate maximum of4, before the median. Misses use a filtered environment response. | A same-renderer many-sample image that leaves these mechanisms on is not an absolute ideal reference. Distinguish limiter bias from earlier estimator/source approximations. | `src/Tertius.Rendering/Shaders/screen_trace.glsl:76`, `src/Tertius.Rendering/Shaders/screen_trace_resolve.comp:54`, `src/Tertius.Rendering/Shaders/screen_trace.glsl:235` |

The current curve removes25% of an incoming estimate whose brightness is
eight times its neighbor median. This follows directly from the selected
curve, not from a new render measurement. The removal happens whether that
estimate is noise or a genuine thin highlight. An all-black supported
neighborhood removes a positive estimate completely. Sharpening after
temporal accumulation cannot, in general, infer that missing light.

Two different errors must remain separate. Edge erosion means the bright
feature becomes narrower or moves inward. Edge blur means the transition
between light and dark becomes wider. A sharper-looking image can have
more edge erosion. Restored local contrast is not proof of restored light.

#### Primary references and applicability

| Source and situation | Fit to this prototype | Constant cost and limits |
| --- | --- | --- |
| AMD FidelityFX CAS: a3x3 contrast-adaptive sharpening operation. Its source pipeline places it after temporal antialiasing/tone mapping and warns that sharpening can amplify grain. Repository pin `9fabcc9a2c45f958aff55ddfda337e74ef894b7f`. | Useful evidence for a cheap edge-contrast experiment, not a noise detector or physical reference. Our proposed target is a separate HDR reflected-light component before composition/FSR, unlike its display-image stage. Do not transplant its bounded-color assumptions or strength values. | A small fixed neighborhood, but an extra full-resolution pass has bandwidth/dispatch cost. No M1 cost is inferred. |
| NVIDIA NRD: complete ray-light denoisers with separate diffuse/specular inputs and geometric/motion/hit-distance data. Its published table uses native1440p on RTX4080. Repository pin `1b6c1865cb59cd14081f2e2eecd02e37b8ca6853`. | Supports using reflection-lobe and hit information rather than receiver geometry alone. It is not a one-pass drop-in for this MoltenVK prototype. Raw hit distance varies with sampled direction, so it is not a universal hard edge label. | More inputs and multiple reconstruction/history operations. Published desktop-RTX timing does not establish our cost. |
| Schied et al., SVGF,2017: temporally accumulated one-path-per-pixel light, with variance-guided hierarchical edge-avoiding wavelet filtering. | Explains why edge guidance and noise confidence belong together. Its complete hierarchy is outside this small-kernel experiment. | Multiple stages and variance state, not the same constant cost as one3x3 gather. |
| PBRT4, roughness and image reconstruction: the reflection distribution and pixel integration both contribute to the image. | A same-roughness reference must use our exact roughness-to-lobe conversion, view angle and pixel footprint. PBRT's parameter defaults are not automatically Tertius's. GGX has tails, so a hard cutoff is not the target. | Offline reference work can be restricted to a small image region. Resolution and sample convergence must be reported. |

Both small-kernel candidates and multi-stage denoisers scale with the
number of shaded pixels. At our fixed720p internal image, the meaningful
cost question is the number of reads, passes, guide images and arithmetic
per pixel. Better asymptotic language does not establish a performance win.

#### Path1: exactly two candidate edge designs, neither selected

Here, a kernel means one GPU operation over a small pixel neighborhood.
It can calculate an edge direction and an output value in one pass.
An edge detector measures a brightness change. It does not establish why
that change exists or what its correct width must be.

| Design | Treatment of the median's removed light | Needed evidence | Expected cost | Failure risk |
| --- | --- | --- | --- | --- |
| A: post-accumulation edge contrast | Keep the median unchanged. Use a small reflected-light-only neighborhood to increase contrast across coherent edges, with roughness limits. | Current accumulated reflection and receiver guides. No new retained lighting history is inherent to this design. | Cheapest diagnostic. One small gather, either an extra pass or carefully folded into existing reflection reconstruction using immutable inputs. | Can sharpen noise, create halos, exaggerate true roughness blur, or make a shrunken highlight look more convincing. It cannot certify or recover removed light. |
| B: reflected-feature-aware preservation | Protect supported reflection boundaries before limiting and stop the reconstruction from mixing across them. Do not merely add sharpness to the finished image. | Reliable reflected-feature evidence together with roughness/view-dependent spread. Existing current-ray records provide some information, but a raw stochastic hit distance alone is insufficient. Additional guide plumbing or confidence work is unresolved. | More source/interface work. Not promised to fit a color-only3x3 gather or to be cheap without measurement. | Mistaking a stable bright cluster for a real feature reintroduces speckles. Treating different random hits as hard boundaries destroys legitimate rough reflections. |

Example A: the existing lamp reflection gets a steeper-looking boundary,
but its missing outer light can remain missing. Example B: a narrow real
reflection receives protection from reliable reflected-feature evidence
before the median erases it, but unreliable evidence can protect noise.

Both designs operate on reflected light rather than the finished color
image. Both must preserve the intended roughness response and be judged
against the same independent reference. Neither is selected or implemented.
Do not add a generic display sharpen and call its apparent sharpness a
reflection-quality result. Do not use the new edge mask merely to clamp
the bright side harder.

#### Path2: a small independent reference, common to both designs

Define the target first: a noise-free estimate of reflected light under
the same intended material model, lighting, view and pixel footprint.
This is not an assertion that the current screen-space source is an exact
multi-bounce physical solution.

The manageable reference fixture is a flat strip and a uniform glowing
box/bar. The scene already contains a bar that emits6800 and reflects
nothing. Exact ray/box intersection can decide whether a direction sees
the emitter. Numerical integration of that known incident light through
the matching reflection model gives an independent reference. Integrating
over visible emitter faces or using suitably weighted emitter/BRDF samples
avoids relying on rare random hits to the small bright source. Report
convergence as work increases instead of assuming many samples are enough.

Reuse the existing double-precision material model and check it against
the shader. Match roughness floor0.045, lobe width equal to squared floored
roughness, Fresnel, geometry term and energy compensation. Preserve the
actual camera matrix, finite pixel footprint and view angle. A zero-width
geometric mirror is a useful position check, not the reference for the
roughness0.2 strip. The existing angular-width approximation in
`GgxLobe.AlphaPerWidth` assumes a particular orientation. It is not a
global pixel-width rule for this grazing plate.

Two diagnostic comparisons answer different questions, not competing
production designs:

- A frozen-source many-sample comparison isolates reconstruction and
  median bias under the same source field. It is not independent physical
  truth, especially if that source already contains filtered feedback.
- The controlled emitter/plane reference checks the intended reflected
  shape and light without screen-space hit/fallback/history approximations.
  It does not validate the whole existing scene's multi-bounce light.

Do not derive the physical light deficit from the existing PNGs. Their
tone mapping, bloom and clipped display highlights change brightness and
spread. Read scene-linear reflected light, multiply the material factor
back exactly once, and use one fixed exposure. Do not include direct or
diffuse light in that component's measurement. First compare before FSR
and bloom. Then measure output-resolution FSR effects separately against
a matching output-resolution reference. Fixed scale is not controller or
motion validation.

| Proposed measurement | Definition | What it distinguishes |
| --- | --- | --- |
| Edge displacement | Position of the half-reference-contrast crossing, in pixels at the stated measurement resolution. Use the reference's background and amplitude for both images. A missing crossing is a missing feature. | A shifted or eroded edge, not just lower contrast hidden by independent normalization. |
| Transition width | Distance between10% and90% reference-contrast crossings, in the same pixels. Handle each side of a narrow bar separately. | Edge blur. Report missing crossings rather than inventing a width. |
| Light deficit and surplus | Sum positive reference-minus-candidate values and positive candidate-minus-reference values separately over the same fixed region, divided by the reference light sum, in percent. | Removed light versus light moved or added elsewhere. A single equal total can hide both errors. This is an image-space light measure, not total scene-emitted energy. |
| Outer-light preservation | Compare the light in fixed reference-defined outer bands and a90%-light-containing width. Keep region and background subtraction fixed. | Legitimate roughness tails lost despite an attractive central edge. |
| Grain and persistent error | With future bounded repeated samples, compare variation over time and the mean signed error against the converged reference. | Random grain versus persistent bias. One displayed still cannot separate them. |

#### Recommended sequence and unresolved choice

Prepare the small same-roughness reference and stage-specific HDR probes
before judging a sharpening kernel. Include a broad bright area, a thin
real bright feature and a dark area with a rare large sample. A candidate
must distinguish these rather than winning only on the grainy picture.
Keep all results for each roughness separate.

Future behavior checks must cover constant regions, edge position,
thin-feature light, physical spread, isolated outliers and finite
nonnegative output. A real fixture and reference-convergence check are
required before any reference image is accepted. No behavior test or
reference approval is claimed from this source-reading work.

When the user returns, settle A versus B for the first edge prototype.
The common reference is recommended before either quality judgment.
Any implementation needs red-green checks, a fresh bounded allocation
and an explicit decision about its behavior. No new strength, roughness,
history, ray count, hit-source rule or performance budget is selected here.

### November 5, 2026: independent controlled reference implemented

The user selects the independent reference renderer before further edge
work: “Write the reference renderer. We will do everything based on that.”
The reference is now `tools/Tertius.ReflectionReference`. Its README owns
the fixture, equations, pixel-footprint definition, integration method,
file formats and run command. This is a CPU research tool, not a new live
lighting path or a new denoiser. It reuses the intended material response
and half-precision DFG table, but independently integrates exact box
visibility. It excludes the screen estimator, all limiting and histories,
the spatial filter, environment fallback, bloom, tone mapping and FSR.

The parent assigned ONE fresh bounded allocation after the previous
allocation was released. Before changes, the full dirty patch and
untracked archive were saved in `independent-reference/before`, with
checksums. Existing median/max binaries, originals and all10 median
capture-input checksums remain unchanged. HEAD is still the exact
`96db37df965c7b35dae499a2f34410d2c3b35011`. No migration or commit occurred.
Rendering gains only a friend-assembly entry; the existing rendering test
project references the tool. No live renderer behavior changed.

#### Behavior and build evidence

Core geometry, finite-pixel direction and absolute-light checks first
failed behaviorally:7 failed and2 passed. The core implementation then
passed9/9. Output/footprint/error-accounting checks next failed
behaviorally:5 failed and9 passed. The final focused Release run passed
14/14. Initial unused-stub/style compilation errors were corrected before
the behavioral reds and are not called red evidence. One standalone
build attempt found CA1869; the cached serialization-options correction
then built with zero warnings/errors. No suite, coverage, mutation,
production capture or live-performance run was performed.

Checks include exact nearest box face and inside exit; a parallel miss;
directions within a pixel and a top-down camera; analytic Lambert
irradiance of a square source; near-smooth broad-source reflection;
radiance scaling; an unseen emitter; agreement between area-only and
mixed integration at broad roughness; bottom-up linear HDR output;
positive bar reflection; and separate missing/extra pixel radiance.

#### Bounded outputs

Evidence root:
`/private/tmp/tertius-unity-noise-research-20261105/independent-reference/`.
The retained executable is in `bin`; outputs are in `run-001`.
`source-inputs.sha256`, `binaries.sha256`, full dirty archives and the
runtime manifest preserve the identities. The DFG half-table checksum is
`e8124ed0fc4fca228bcb75d8ca0b5cb83a3d2b3a424849f56152876d99f0007f`.

The fixture has one uniform glowing bar and an infinite flat plate.
The camera matches the authored front-camera model. The80x160 crop
retains individual pixels of a1280x720 frame, rather than downsampling
the full view. Two plate cases request roughness0 and float0.2. The first
uses the material floor0.045. All pixel values are one-bounce specular
RGB radiance with the material factor present once. No ambient or direct
camera image of the glowing bar is added. The controlled plate is not
the six-strip demo.

Each of two deterministic shifted replicates uses nested128,512,2048
sample-pair levels per pixel. Each pair contains two light directions,
with a shared position within the pixel. PFM files retain all levels and
the two-replicate means. PNGs are inspected display previews only.
Profiles retain row/column mean radiance at original-frame coordinates.

| Plate roughness after floor | Change128→512 pairs | Change512→2048 pairs | Final shifted-replicate difference | Fixed-point area-only versus mixed integration |
| --- | --- | --- | --- | --- |
| 0.045 | 0.4677% | 0.2102% | 0.1663% | +13.6930% |
| 0.20000000298 | 1.3176% | 0.5305% | 0.4106% | +0.005327% |

The first three columns are image-space absolute luminance differences
summed over the unchanged crop, divided by the finer/reference crop's
luminance sum. They are convergence evidence, not an error bound or
confidence interval. The final column is one surface point without pixel
averaging, using65536 samples per method. It must not be combined with
the image differences as if all four were the same metric.

At roughness0.2, the independent integration methods closely agree at
the checked point. At0.045, the area-only method disagrees materially.
Uniform area sampling can undersample this very narrow peak; that is a
plausible explanation, not proof that the mixed result is correct. The
0.045 case therefore remains provisional for absolute local accuracy.
Do not call the two shifted images' close agreement independent proof of
ground truth. A further targeted integration check is needed before
using that case to certify small local edge/light errors. No automatic
extra run was launched after the bounded experiment.

Both original PNGs were opened and inspected. The0.045 case has a narrow
bar reflection with a small tail. The0.2 case has a wider continuous tail
without the live prototype's clustered pattern. These are different
reference materials, not a denoiser off/on comparison. The finite crop
does not retain all GGX tail light, so its radiance sum is not total
reflected power. Existing full-scene display captures still cannot be
subtracted from these controlled raw-light references.

The numerical run ended normally at19:16:49 EST. Disk guards passed
before queue, after queue and at end; end free disk was5,655,596 KiB.
Evidence including the retained executable initially occupied about14 MiB,
below the128 MiB target. All commands used Nix and the unmodified normal
GPU queue, one foreground phase. The ONE allocation was explicitly
RELEASED to the parent immediately after command completion. Subsequent
inspection and notes hold no allocation.

The next comparison needs the same controlled fixture in the live
renderer, with reflected-light HDR readback before display and FSR.
That future work requires fresh capacity. No edge algorithm, clipping
tolerance, light-source/history change, production reference approval,
quality acceptance, performance acceptance or integration is selected.

### November 5, 2026: targeted reference-check follow-up held for disk capacity

The user authorizes resolving the smoothness-limit cross-check mismatch.
This does not authorize tuning the material or changing the live prototype.
Both original integration methods, binaries and results remain preserved.
The proposed follow-up independently integrates emitter faces, allocating
work around the narrow reflection peak, and reports calculation exhaustion
rather than treating a fixed sample count as convergence. A numerical
error estimate remains an estimate, not proof of an exact bound.

Focused checks and an adapter to the old area-only probe are written in
`ReflectionReferenceQuadratureTests` and
`ReferenceIntegral.Quadrature.cs`. They are not run or passed, and no
quadrature implementation or correction is claimed. The tests ask for
oblique narrow-reflection agreement, independent analytic diffuse
normalization, and refusal to claim convergence when work is exhausted.

At20:01 EST the parent explicitly assigns NO heavy allocation because
disk margin above5GiB is too small and falling. No build, test, numerical
run, queue or reservation starts. Light inspection finds the tracked Nix
source tree alone is67.48MiB; a new snapshot can exceed that margin.
The revised point-only evidence target is1MiB without a second dependency
directory; a conservative128MiB incremental work allowance is reported,
not a guaranteed peak or permission. All original hashes verify. The
before-state archive and current checkpoint are under
`reference-point-validation` in the evidence root. Continue only on a
fresh explicit allocation with disk guards satisfied.

### November 5, 2026, 20:48 EST: fresh point-only allocation

Parent assigns one new bounded build/test/numerical allocation after
user-approved unrelated cache cleanup. The local20:51 check finds
17,745,504KiB free, memory53%, load2.05/2.21/2.59. No prior reservation
is retained. Resume the already-written focused CPU checks and minimal
reference-tool-only correction, then point-only convergence and independent
emitter-face integration checks. New JSON/CSV/log evidence target<=1MiB;
retain only changed small assemblies and hashes, not duplicate dependencies.
No images, full image sweep, live prototype change or same-fixture live HDR
comparison. Nix, the normal GPU queue and >=5GiB guards apply to every
phase. Release immediately after all owned heavy commands finish, even
if the investigation remains inconclusive. Do not change the material or
tune estimates to agree.

### November 5, 2026, 21:01 EST: point disagreement resolved

The weak check was uniform-area sampling, not a demonstrated defect in the
original mixed estimator. At the smooth surface point, uniform-area shift811
puts only6 of65,536 samples inside the narrow reflection peak's core.
The core means a visible emitter sample above the plate whose half-vector
tilt has tan(angle)<=alpha, where alpha is roughness squared. At1,048,576
samples, that shift puts70 samples in the core. Counts use all emitter
samples as the denominator, including invisible faces.

The new independent check integrates across visible box faces directly.
It multiplies the unchanged material response by emission, emitting-face
cosine and inverse distance squared. It does not use either sampler's
sample positions, densities or weights. Face geometry is shared to avoid
two different definitions of the box. The material and DFG table remain
shared, so this is not an independent check of those equations.

The CPU integrator uses tensor3/5 Gauss-Legendre rules with adaptive
subdivision. It is for this tiny offline fixture, not a real-time GPU
algorithm. Quarter-face cuts and roughness-scaled cuts around the mirror
intersection prevent the initial rules from overlooking the narrow peak.
Each panel costs34 evaluations; splitting a panel into four costs136.
The summed panel differences estimate absolute error. This estimate is
not a rigorous bound. A budget-exhausted or incomplete domain cannot claim
convergence. No generic scene integrator or production policy is added.

All values below are one fixed surface point, not image averages. Radiance
is the light sent toward the camera, in W/(m²sr), before display mapping.
Requested roughness0 is floored to0.045; requested0.2 retains its float-input
value0.20000000298023224. Material, emitter and camera match the original.

| Floored roughness | Face integral, relative tolerance1e-3 | Face integral,1e-5 | Face integral,1e-7 | Strict evaluations | Last tolerance change |
|---|---:|---:|---:|---:|---:|
|0.045|626.3156158394573|626.3261371105867|626.3260669158196|159,290|0.00001121%|
|0.20000000298023224|519.6650036577311|519.6646723187670|519.6646780823439|37,842|0.000001110%|

Each integration meets its estimated stopping criterion within the300,000
evaluation budget. Absolute tolerance is1e-9. Strict estimated absolute
errors are6.260480699e-5 and5.157218741e-5W/(m²sr), respectively. These
are numerical estimates, not certified true errors. Independent analytic
Lambert irradiance and explicit budget exhaustion are also tested.

The unchanged estimators are checked at65,536,262,144 and1,048,576 samples
with shifts311,811 and1451. The mixed estimator takes two light directions
per sample pair; uniform area takes one. The original65,536 mixed shift311
value626.3256536190356 and uniform-area shift811 value712.088311659317
reproduce exactly. No value is tuned to agree.

| Roughness | Largest absolute mixed difference at1,048,576 pairs, across3shifts | Uniform-area difference range at1,048,576 samples | Interpretation |
|---|---:|---:|---|
|0.045|0.0003183%|-0.5597% to+4.8645%|Independent face integral supports mixed estimate; uniform area still unconverged|
|0.2|0.0020293%|-0.01444% to+0.0001281%|All methods much closer; limited repeat/count evidence, not an accuracy certificate|

For the smooth case, uniform-area differences across shifts shrink from
+13.69% to+24.61% at65,536 samples, to+6.00% to+9.56% at262,144,
then-0.56% to+4.86% at1,048,576. They do not reach a common stable value
within this bounded run. We stop rather than extend the budget or claim
uniform-area convergence. The original13.7% discrepancy was not a
denoiser error and is not a reference accuracy bound.

Focused checks: the first attempt stops on a scaffold style error, not
behavioral red. After correction,2fail/1pass reproduces the disagreement
and diffuse normalization shortfall; the face integrator passes3/3.
A point-only output check then fails for the missing mode,1fail/3pass.
After implementing it and fixing a named-argument style error, the final
focused Release run passes18/18. No full suites, coverage or mutation work.
The existing tool/test project graph compiles the Release executable;
the retained numerical command invokes that DLL directly with `--points`.

The final command ends21:01:37EST with17,708,116KiB free. All disk guards
pass. Every owned heavy process is finished, and the ONE fresh20:48
allocation is explicitly RELEASED directly to parent immediately after
exit. No reservation remains for later live work. Evidence is under
`reference-point-validation/points-001` in the existing research root:
JSON/CSV only, about13KiB, with compact logs, input hashes and changed
small tool assemblies. No new images, full image sweep or duplicate
dependency directory. All original reference binaries/outputs and all10
median capture inputs still match their retained hashes.

This resolves the local cross-check, not the user's original light-loss
question. The live prototype, median/max controls and old images are
unchanged. No same-fixture live HDR comparison has occurred. No claim of
full-image validity, certified accuracy, energy equivalence, denoiser
quality, performance or production acceptance follows from this result.

## Matched live comparison checkpoint, November 5, 21:51 EST

The requested comparison is the live renderer against the independent
reference, not another reference self-comparison. Native rendering uses
zero camera jitter and one pixel-center ray. The retained original reference
averages positions across each pixel's area. Those footprints differ. The
approved new reference therefore uses captured pixel-center receiver inputs;
the old area-average images and binaries remain separate and unchanged.

The read-only tool probe exports outgoing indirect specular red-channel
radiance. All fixture colors are achromatic. The probe uses the production
material response once, then CPU readback divides only by recorded
pre-exposure. Inputs include decoded material, normal, distance-derived
position and composite view direction. The reference uses these inputs,
not only authored material values. There is no fitted light multiplier.

The bounded fixture has no direct lights, ambient light, environment,
atmosphere, normal map, bloom, impostors or FSR. The emitting box has black
base color, metallic zero, reflectance zero, roughness one and emission6800.
Its decoded stored emission is recorded. Reflectance zero is not a proof
of zero oblique-angle reflection: live hit color retains existing feedback.
Emitter additional specular light is recorded as a slow-history observation,
not a certified bound on every fast-history hit. The reference integrates
one bounce; any residual source mismatch must remain explicit.

Four configurations remain: authored roughness0.045/0.2 times original
whole-prototype OFF/selected median single-history all-ON. Each gets64
advancing warm-up draws and32 measured draws, at native1280x720. The crop
is(530,240),80x160. New center references are128/512/2048pairs with shifts
41/97. Comparisons must separate live OFF versus center reference, ON
versus OFF, and original area-average versus decoded-input center results.
The last comparison changes both footprint and decoded inputs; it cannot
isolate either cause without another experiment, which is not assigned.

So far, there are NO measured configurations or comparison images.
Three core behavior checks reached red3/green3;18 existing reference checks
passed. Separate-output retention compilation failed on analyzer paths.
The normal-route successful tool was copied without a retention rebuild.
First live attempt exited134 before completing the first warm-up draw:
the existing fullscreen helper accepts only three images per draw, not
eight. The21:13 allocation was immediately released. Saved logs are evidence
of failure, not numerical results.

A new21:45 allocation authorized a tool-only3/3/2 draw correction. All three
draws use the same frame and descriptors and write only probe-owned images.
The corrected tool compiled. Its regression test compilation failed on an
unsupported expression-tree pattern. The21:45 allocation was immediately
released after the command exited. The test-only predicate is corrected;
tests and captures have not resumed. No allocation is retained. Evidence,
including both failures and old successful binary, is under
`live-reference-comparison` in the existing research root (~13MiB so far).

## Matched comparison stopped at decoded-input validation, November 5, 21:59 EST

The separately assigned21:54 allocation compiled only the test project
with existing references, BuildProjectReferences=false and no restore.
The four comparison checks and18 reference checks passed. The corrected
tool was neither changed nor compiled again. Its source and binary hashes,
and all recorded built dependency hashes, still matched after testing and
after the capture failure.

The corrected3/3/2 probe passed the previous attachment-count failure:
the smooth-OFF fixture completed64 advancing warm-up frames. At the first
measured frame(draw64, tick65, time65/60 seconds), the runtime check threw
"Probe output mapping does not match the actual achromatic plate inputs."
This compound check compares several decoded material, geometry, view and
light-history inputs, so the saved exception does not identify the failing
quantity. Do not describe the failure as a confirmed channel-mapping bug
or relax the check without identifying the actual mismatch.

The output contains only one unvalidated80x160 scalar crop(50KiB). ZERO
configurations completed; no mean previews, center-reference images, or
live-versus-reference numerical results were produced. The roughness0.045
ON case and both roughness0.2 cases were not started. No conclusion about
light loss, sharpness, or reference agreement follows from this attempt.

The process exited134 and the allocation was explicitly RELEASED directly
to parent immediately afterward. No retry or additional heavy command ran.
The normal GPU lock is released and no later slot is reserved. End free
disk was36,410,888KiB; cumulative comparison evidence was about25.4MiB.
The failed attempt remains under a separate live-corrected namespace and
logs/captures-corrected.log; prior failures and controls remain untouched.

Frozen corrected tool SHA256:
e3c2507a38a87008175cc5ceb16d144c0277c401266939423e92a0a18af73ec9.
Retained production Rendering DLL SHA256:
e212019b3f5578621ecdb0c4314efd675ac76504d14446ed8b7b84f795fac304.
Hashes-after-tests.txt and hashes-after-capture-failure.txt record all
unchanged source/binary checks. Attempt-2154.sha256 identifies both new logs
and the partial scalar output. The next correction needs the exact failing
decoded input, not another undiagnosed retry or a tuned reference value.

## Source-only diagnosis of receiver visibility after release

The fixed camera, original crop and emitter box imply that part of the
emitter's lower visible face falls inside the upper part of the crop. The
fixture draws that emitter as an ordinary visible box. Some camera rays
there meet the emitter before the plate. This is a source-geometry
inference, not a new projection run or recovered GPU pixel measurement.

The validation assumes every pixel is plate: zero emission, the case's
roughness, reflectance0.5, base color0.06, normal+Y and world positionY0.
The emitter instead has positive emission, roughness1, reflectance0 and
black base color, and its visible front face is not the plate. A correctly
mapped probe could therefore trip multiple plate-only predicates. The
exception does not establish that channel mapping itself failed.

The exact first failing runtime predicate remains unknown. CaptureLive
reads firstInputs, calls ValidateProbeInputs, and only then writes
decoded-inputs.f32. The exception discarded the inputs needed to locate
the failure. The retained50KiB partial file contains only outgoing specular
red radiance. It cannot reconstruct the emission, material, geometry,
independent distance or source-history values used by the compound check.

There is also a defined primary-visibility boundary in the old reference:
ReferenceImage intersects the emitter with the camera ray and omits the
plate's contribution when the emitter is nearer than the plate. Primary
visibility means the first surface that the camera sees. A zero plate
contribution there does not represent the emitter's own outgoing specular
light or emission. The new center-reference path requires plate receivers
everywhere. Do not treat those different quantities as comparable, and
do not silently move the crop, hide the emitter or select pixels by their
measured radiance. Original outputs and footprint remain unchanged.

The smallest future diagnostic would preserve the existing16 probe scalars
for every crop pixel before any validation can throw. It must also retain
the independent distance and source-history crop values used by the
checks, plus frame/camera/exposure/material metadata. Each failed predicate
must name the image pixel, actual value and expected value/tolerance.
A geometry/export preflight can use the first already-budgeted renderer
draw instead of waiting64 warm-up draws; history-dependent checks still
belong to their appropriate phase. No such correction or preflight is
authorized or implemented yet. This turn used only source/existing-file
inspection and notes; no heavy command or numerical program ran, no
allocation is held and no owned renderer process remains.

## Settled plate contribution and stopped baseline build, November 5,22:17 EST

The user settled the comparison quantity: outgoing specular contribution
of the VISIBLE PLATE. Keep the original crop/camera/fixture and original
reference outputs. Reuse the original primary-visibility rule. Where the
emitter hides the plate, the derived plate contribution is zero on both
sides. Raw live outgoing emitter light and decoded inputs must remain
unchanged and separately recorded, not relabeled as zero raw measurements.
Geometry classification/counts must agree OFF/ON. Unknown surfaces or
analytic/G-buffer disagreements are failures, not pixels to drop.

The22:08 allocation and a subsequent build-accounting exception allowed
one baseline Release tool compilation for meaningful assertion reds, then
one final corrected Release build, using existing production references
with BuildProjectReferences=false/no restore. New focused test source
describes plate/emitter distinction, raw-data preservation, unoccluded
plate signal, useful retained invalid-input diagnostics and matching
original primary visibility. A baseline extraction exposes the previous
all-plate check and late input retention. It is not the correction.

The baseline compile failed before tests ran. Errors introduced in that
scaffold were three TS0002 unnamed calls with more than three arguments
and IDE0060 for the unused optional camera parameter. This is a compiler/
analyzer failure, NOT a behavior red. No further compile, final corrected
tool, geometry preflight, live configuration or center reference ran.
ZERO completed/validated comparison configurations remain.

The process exited1; all owned processes and normal GPU lock ended. The
allocation was explicitly RELEASED directly to parent at22:17. No retry,
compiler cleanup or further behavior edit followed the failure, and no
later slot is held. The failure and baseline source identity are retained
in logs/baseline-2208.log and baseline-2208/source.sha256. All recorded
production/dependency binary hashes, frozen earlier corrected tool files,
prior failed logs and partial scalar file still match. The original HEAD
remains96db37df965c7b35dae499a2f34410d2c3b35011.

## Meaningful baseline reds, final compilation stopped, November 5,22:33 EST

The22:22 allocation reused the parent's lightweight scaffold corrections
without changing the live fixture. The synthetic1x1pixel occluder test was
wide enough to cover its area-sampled pixel; this is not a live-fixture
retune. The corrected baseline and affected tests compiled with existing
Release dependencies, BuildProjectReferences=false/no restore. The nine
comparison tests produced four intended behavior failures and five passes.
The old measurement rejected a directly visible emitter as non-plate; two
checks lacked specific saved-invalid-input diagnostics; and the old center
reference returned0.640290377 where the original occluded plate reference
returned0. These are meaningful reds. No new implementation green exists.

The final source moves the original primary-visibility rule to one shared
ReferenceCamera.PrimaryHit method. Area reference, center reference and
live classification use it. Live raw outgoing values remain unchanged;
separate arrays/streams represent visible plate and emitter contributions.
All16 probe values and independent distance/history crops plus metadata
are saved before validation. Failure records name the predicate, pixel,
actual/expected values, units and tolerance. Geometry preflight runs on the
first already-budgeted renderer draw; source-history checks are measured-
frame checks. This describes unvalidated source, not successful captures.

The second and final allowed tool compile failed on TS0011 in
Program.Live.cs57: a nested conditional selects a diagnostic folder. This
new style error was introduced here. The final tool binary was not made,
final tests did not run, and no preflight/capture/center evaluation started.
ZERO completed/validated comparison configurations remain. The existing
normal tool bin contains the baseline, not this final implementation.

Baseline tool SHA256 is
8c3a0d7140d6ee6f7864b6a7bee6a08e4fe23086ebda5705118c7f515d67f6b5.
The baseline is retained separately under baseline-2222. Final source
identities are in final-2222/source.sha256. Failure log is
logs/final-2222-build-checks.log. All recorded production/dependency hashes,
old corrected retained tool files and baseline small assemblies still
match. Full dirty preservation includes both source stages and old failed
attempts. No production renderer or policy source changed in this turn.

The allocation was explicitly RELEASED directly to parent immediately
after all owned commands exited. No style fix, retry or further heavy
command followed the failure. End free disk36,404,532KiB; cumulative
comparison evidence was about26.2MiB before final checkpoint. No future
allocation/reservation remains and no comparison or acceptance is claimed.

## Corrected final-build allocation, November 5,22:35 EST

One fresh bounded allocation permits one corrected FINAL Release tool
compilation, separate affected-test compilation and focused green checks.
Use existing Release references, no restore and BuildProjectReferences=false.
Do not rebuild/rerun the preserved meaningful red baseline. The parent
changed only the diagnostic-folder nested conditional into independent
guards. Program.Live.cs SHA256 is now
58d9a45f661f2ae410cfeaf87a303f4331ebfdfd11f67d628f30b161d58bd73c;
preserve the earlier failed source manifest unchanged.

Only green checks permit the first already-budgeted renderer-draw preflight.
Only passing preflight permits the original four64+32frame configurations
and128/512/2048center-reference pairs with the two fixed shifts. Original
crop/fixture/visibility and raw-versus-plate-versus-emitter accounting stay
unchanged. Persist complete inputs and exact predicate diagnostics before
failure. Stop and release on an unexpected blocker; no automatic retry.
The5GiB free-disk guard,128MiB cumulative evidence cap, Nix/normal GPU queue
and one foreground phase remain binding. ZERO validated configurations at
assignment. This reserves no later work or production acceptance.

## Focused green, first-draw position blocker, November 5,22:43 EST

One corrected FINAL Release tool compilation and a separate affected-test
compilation succeeded using existing Release references, no restore and
BuildProjectReferences=false. Both builds had zero warnings/errors. All9
comparison and18 reference focused checks passed. The meaningful red
baseline was not rebuilt/rerun. Frozen final tool SHA256 is
18300a9807dde39cafbbd3670d38cb43f5501b574e4de3cd4bb46441468577bc.
All10 recorded production/dependency binary hashes are unchanged. The
normal production Rendering DLL remains
e212019b3f5578621ecdb0c4314efd675ac76504d14446ed8b7b84f795fac304.

The FIRST already-budgeted renderer draw in smooth-off failed geometry
preflight. First named condition: primary-position at pixel(572,240).
Decoded world position is
(-0.8999811410903931,0.22300291061401367,-1.9965004920959473)m;
analytic primary position is
(-0.8999999761581421,0.22294541864116946,-1.996666212554242)m.
The position-distance tolerance is0.0001m. The saved validation file has
26primary-position failures and no other failure predicate. This shows a
live/analytic position discrepancy under this strict check, not its cause
and not a renderer defect or a reflection-quality result. No thresholds,
inputs, classification or fixture values were changed to obtain agreement.

The new diagnostic path worked: all16probe fields, independent distance
and source-history crops, original fixture/exposure/frame metadata and
all failed conditions were saved BEFORE throwing. Geometry classification
records12,486plate and314emitter pixels with0unrecognized pixels; the
preflight as a whole still failed. Draw0/Tick1/snapshot1/60s advanced the
renderer exactly once, resettrue, at native1280x720internal/output. The
remaining warm-up and measured frames did not run. Three measured output
streams are empty. ZERO completed/validated configurations, no center
evaluation and no live/reference comparison exist.

The capture command exited134 and the normal GPU lock released. The ONE
22:35 allocation was explicitly RELEASED directly to parent immediately
after all owned heavy commands ended. No retry or later reservation is
held. Source/binary hashes still match after stopping, including the frozen
red baseline and earlier corrected tool. Logs/source/archive/binary hashes
are under live-reference-comparison/final-2235; complete diagnostic data is
under live-2235/smooth-off/preflight. All previous failed attempts, original
area references and prototype/control evidence remain intact. Any further
geometry diagnosis is distinct from a completed numerical comparison.

## Reference explanation and raster diagnostic scope, November 6,02:20 EST

The reference is a small CPU analytic ray caster for a box emitter above
a flat plate. Camera rays establish visible plate receivers. Each light
sample pair uses one sampled material-reflection direction and one sampled
emitter surface point, with the intended material response and combined
sampling densities. This is independent of the live screen-space light
estimator, histories, limiter and denoiser. It is NOT a general multibounce
full-scene path tracer. Its images contain plate reflected-light contribution,
not directly visible emitter light. Separate emitter-face integration
checks selected points; it shares the material law and does not independently
certify that law or the full crop.

Parent's position-diagnosis-2235 uses source triangle order and perspective
interpolation. Fixed nearest1/256pixel projected-vertex rounding reproduces
all28 emitter+X and286emitter+Z distances within1.244/0.858micrometers.
No pixel was excluded or light value fitted. Device limits and actual
prepared numeric camera/geometry inputs were not captured. These are the
remaining gaps, not a reason to enlarge the position tolerance.

ONE fresh02:20 diagnostic allocation permits metadata-retention red-green,
at most two Release tool compilations using existing references/no restore,
and at most ONE original-fixture OFF draw for numeric inputs. No warm-up,
measured frames, images or radiance integration. Save before validation;
preserve all old controls/failed evidence. New evidence<=8MiB, cumulative
<=128MiB; Nix/normalGPUqueue/oneforegroundphase/5GiBguards and explicit
release apply. Unexpected blockers mean stop, not automatic retries.
No validator or production change. ZERO lighting-comparison configurations.

## Numeric diagnostic reds and build stop, November 6,02:34 EST

The02:20 diagnostic allocation reviewed the retained parent model/source
and preserved the old2235 binary, failed first-draw data, all10 production
references and full dirty inputs before editing. Two new focused CPU tests
describe retaining numeric raster metadata with a valid plate signal and
retaining it before a real invalid-geometry exception. The scaffold adds
only a payload field to the existing frame; the existing saver still drops
it. Both tests fail on missing saved JSON fields after successful Release
tool/test compilation. Those are meaningful assertion reds. Earlier27
tests' baseline reds were not repeated, and no radiance integration ran.

Final source records actual device subpixel limits, prepared camera basis,
rotation/projection/shader constants/viewport/jitter, GPU instance model
rows, original box vertices/indices and CPU-derived transformed positions.
It reuses the same MeshData object that is sent to CreateMesh. Numeric
float inputs are widened exactly to double for JSON. The output explicitly
does NOT claim captured GPU vertex arithmetic. The read-only3/3/2 probe
draws remain unchanged; metadata is serialized before the unchanged
geometry validator. The new CLI limits execution to at most ONE OFF draw,
never warm-up, measured frames or image previews. This is ungreen source.

The final allowed tool compile stopped on TS0002 at Program.cs19. The
existing --live call supplies three positional arguments to a method that
now declares four parameters, including optional diagnosticOnly. The
analyzer requires names based on declared parameter count, not supplied
argument count. This mistake was introduced here. There is no final
diagnostic tool binary or green check, no actual-device draw and no new
numeric metadata to compare. No validator, pixel mask, tolerance or live
lighting behavior was changed to bypass the original position mismatch.

Both owned commands ended and the normal GPU lock released. The allocation
was explicitly RELEASED directly to parent before further light notes.
No automatic fix/retry or later reservation is held. Baseline tool SHA256
47949eb0edd217a7960676cacf7086c806d07601f3e0bff42b98a2882182e115;
normal tool bin still contains that baseline. Failed source and logs are
preserved under raster-metadata-20261106-0220/final and logs. The frozen
2235 tool, failed preflight, all10 production refs and baseline hashes
still match. New evidence was452KiB before final notes. ZERO completed
lighting comparisons remain. Parent's subsequently announced source-only
named-call correction is expected and will have a separate hash; it does
not rewrite or approve the failed build record or authorize heavy work.

### November 6,02:35: bounded diagnostic recovery allocation

Parent assigned ONE fresh allocation after releasing02:20. Only the known
--live named-call correction changed tool source. Program.cs SHA256 is
2ea97c368784444fbb464ef45185c48d02c3f846518d1f7f41402de0bbec501b;
the failed source/log/archive and meaningful metadata assertion reds are
preserved under the earlier namespace, not replaced or repeated.

This recovery permits one final Release tool compile and separately built
focused tests against existing Release references, without restore or
dependency builds. Geometry and metadata checks must not launch lighting
integration. Green checks and preserved source/control/dependency hashes
gate one original-fixture OFF diagnostic renderer draw, without warm-up,
measured frames, images, validator changes or a second draw. Numeric device,
prepared-camera and model-row data then inform the existing offline raster
model. Evidence remains<=8MiB cumulative diagnostics and<=128MiB comparison.
All commands use Nix/normalGPUqueue/oneforegroundphase/5GiBguards. New
unexpected failure stops the allocation; no subsequent slot is reserved.

### November 6,02:48: actual device metadata does not yet certify the model

The02:35 allocation was explicitly RELEASED directly to parent after all
owned heavy commands and normal GPUlock ended. No later slot is held.
One corrected Release tool compilation and separate tests-only compilation
succeeded with zero warnings/errors, existing refs, no restore/dependency
builds. Sixteen selected CPU checks passed:2metadata-retention,6comparison
accounting/probe,8pure geometry/storage/profile. All light-integration tests
were excluded. The preliminary native xUnit help request returned usage
code2, not a test result; it did not cause a source fix or repeat compile.

The exact frozen tool DLL hashes
97446f6a461321515ade821467155122c1b694ae084bf3d499542147ba5801e7.
Only small new tool artifacts were copied. Frozen2235 dependencies are
linked, not duplicated. Parent Program.cs SHA2ea97c... is the only changed
tool input relative to failed0220final. Old sources/logs/control and all10
production-reference hashes remain verified and retained separately.

One OFF1280x720native/zerojitter draw retained all16fields, independent
distance/history and actual raster metadata before the same26position
failures(exit134). Draw0,Tick1,Time1/60s,Resettrue,DrawnFrames1. There was no
warm-up, measured frame, second draw, preview or light integration. All
12800probe distances equal independent distances; bytes match saved2235.

The actual AppleM1Max/MoltenVK payload reports eight SubPixelPrecisionBits,
zero ViewportSubPixelBits and four SubPixelInterpolationOffsetBits. It
contains actual prepared camera basis/projection/viewport/zerojitter and
source mesh/model rows. CPU-derived transformed positions are explicitly
not captured GPU vertex outputs. The active original geometry range is Near.

The new analysis reuses the old source's triangle interpolation functions,
with recorded numeric matrix/CPU-transformed vertices and fixed nearest
1/256pixel rounding selected from the reported eight bits, not fitted.
It accounts for all314emitter pixels without exclusions or light gains.

| Recorded emitter face | Pixels | Old ideal-camera rounded model maximum | Recorded-matrix unrounded maximum | Recorded-matrix rounded maximum |
| --- | --- | --- | --- | --- |
| +X | 28 | 1.244 micrometers | 244.226 micrometers | 471.529 micrometers |
| +Z | 286 | 0.858 micrometers | 11.344 micrometers | 5.743 micrometers |

Each maximum is an absolute modeled-versus-recorded distance difference,
not an error in reflected light or a certified distance allowance. The
recorded-matrix rounded model is worse on the narrow+Xface. This negative
result remains alongside the old ideal-camera result rather than replacing
it. Actual shader projection arithmetic, rounded triangle placement and
interpolation have not been reproduced exactly; a small projection change
can also move a vertex across a fixed-point rounding boundary. Reported
device bits alone do not establish which arithmetic/rounding path ran.

No validator/tolerance correction is selected. The old geometry preflight
remains failed; ZERO lighting configurations/comparisons are validated.
No quality, reference-error-bound, energy, performance or production verdict
follows. Any further numeric or live diagnostic needs a fresh bounded scope.

Evidence is raster-metadata-20261106-0235:actual-metadata-residuals.json,
emitter-distance-residuals.csv,analyze_actual_metadata.py, frozen final
source/bin manifests,16green check records and the retained failed draw.
The prior0220diagnostics and new recovery totaled2292KiB; whole comparison
evidence31036KiB before final notes, below8/128MiB limits. Final disk check
was39,200,052KiB free. All old controls/reference outputs remain unchanged.

### Parent-only precision follow-up after the02:35 release

Parent supplied a separate saved-input arithmetic analysis under
position-diagnosis-0235-parent. This thread read the existing README,
script and report and verified their manifest; it did not execute the
analysis, build, test, queue or render. All allocations remain RELEASED.
The original0235negative result and its sealed artifacts remain unchanged.

The recorded matrix contains32-bit floating-point values. The first
actual-input model used64-bit arithmetic to evaluate those values. Parent
identified a projected emitter corner at572.7246068380872pixels, only
0.000002536913pixel below the rounding midpoint572.724609375pixels.
A replay rounding each clip-projection/viewport operation to32bits lands
exactly on that midpoint. Python's nearest-even tie rule then selects
572.7265625pixels instead of572.72265625pixels. The tiny arithmetic change
therefore changes the selected position by one complete1/256pixel step.

This32-bit arithmetic hypothesis reproduces all28side-face distances
within1.242997micrometers and all286front-face distances within0.857659
micrometers. No pixel is excluded and no light gain or per-pixel correction
is fitted. These are parent-produced saved-input results, not new live
measurements or numerical results rerun here. The original double replay
still has its471.529micrometer side discrepancy and remains preserved.

The follow-up explains how a precision-sensitive rounding boundary can
produce the two offline results. It does not certify actual GPU arithmetic.
The32-bit replay assumes separately rounded multiply/add operations and
nearest-even subpixel ties; interpolation in the replay remains64-bit.
Actual GPU clip outputs, contraction of multiply/add operations, viewport
arithmetic, tie handling and interpolation remain unverified. Neither the
small residual nor the reported eight bits establishes a geometric error
bound. No global allowance, tolerance change or validator correction is
selected. The geometry preflight remains failed and ZERO of FOUR lighting
configurations is validated. Future work must establish the right geometry
measurement rather than tune a threshold until a comparison passes.

Parent script SHA256:
f349334b0b03adae64ded08182d09e0a74c02631ea81270889e9bc1e9bc10a96.
Parent report SHA256:
7ce692069edf759bfacd48bc9a2786a7c7823d77667010c01618a3e1041a3bd6.
Read-only preservation receipt:position-diagnosis-0235-receipt. No later
heavy work or reservation follows this documentation update.

### Ongoing user goal: make the reference satisfactory

On November 6 the user explicitly asked to work until the reference is
satisfactory. For this research goal, the reference must support an honest
measurement of retained or missing reflected light in the controlled
plate/emitter fixture. This is not a request to accept production noise
quality, certify the shared material equations or build a full-scene tracer.
No fixed percentage threshold is selected merely to declare success.

The compared light is outgoing specular radiance, in W/(m² sr), before
display mapping and FSR, with recorded pre-exposure undone exactly once.
The reference independently integrates one emitter-to-plate reflection.
The original analytic primary-visibility rule still defines the visible
plate contribution; raw outgoing emitter values remain separate and intact.

| Trust question | Evidence needed | Current state |
| --- | --- | --- |
| Is the same surface/point measured? | Original camera/crop/visibility; independently checked live geometry inputs; explicit reconstruction differences | Actual inputs saved; ideal-position preflight still fails; arithmetic replay is only a hypothesis |
| Is the same light quantity measured? | Outgoing specular, material factor once, recorded exposure removed, raw emitter data separate | Probe/CPU accounting checks exist; no validated live configuration |
| Is light integration independent and checked? | Mixed emitter/direction sampling versus direct emitter-face integration; no fitted gain | Agreement supported at one selected point/two roughness cases, not the whole crop |
| Is the pixel footprint matched? | Native zero-jitter pixel centers use decoded receiver inputs; original area averages retain their own labels | Center-reference path exists; aligned crop comparison has not run |
| Is numerical uncertainty visible? | Nested levels and fixed shifts; per-profile changes and honest unconverged/budget limits | Point-only evidence retained; no validated center-reference crop evidence |
| Does the prototype lose or add light? | Matched OFF/ON profiles and variability for both roughness cases; reference uncertainty shown separately | Zero of four validated configurations |

Two shifted deterministic sample sequences provide an empirical spread,
not a statistical confidence interval or a guaranteed error bound. The
adaptive face rule's coarse/fine difference is also an error estimate,
not a certified bound. Satisfactory evidence must retain those limits,
not replace them with a single averaged percentage or a pleasing preview.

#### Smallest proposed next measurement stage

The source currently tests the decoded position reconstructed from a
camera ray and the rasterized distance against an ideal ray intersection.
Those are not identical geometric quantities. Source inspection confirms
that the main geometry shader writes interpolated mesh position, while
the lighting probe reconstructs position from ray times distance. The
reported precision-sensitive midpoint can therefore affect this check
without establishing an error in reflected light.

A fresh bounded scope has been requested from parent for a tool-owned
geometry audit. The proposed pass would use the existing mesh vertex
shader and GeometryPass binding/drawing helpers, actual prepared scene,
range matrices, range order, culling and original viewport. It would write
only separately owned position/distance and normal/material images plus
owned depth. Existing G-buffer, depth, source/history and composition
images remain read-only. All extra GPU draws belong to one renderer frame;
they must not advance camera, simulation or temporal history.

The audit is a geometry measurement, not another lighting estimator. Its
distance output must be checked against the actual live distance readback
before it can justify a measurement correction. Identical source shaders
alone do not guarantee identical compiled interpolation/arithmetic; a
disagreement is evidence to retain, not permission to loosen a threshold.
The decoded position actually used by live lighting remains the receiver
input to the independent center reference. Original visibility accounting
and raw plate/emitter data remain unchanged.

Planned behavior checks include retaining complete geometry evidence on
failure; rejecting a wrong measured distance or wrong visible surface;
keeping real plate signal and raw emitter signal separate; and proving
the diagnostic pass does not modify live inputs. A legitimate difference
between ideal and rasterized position must be recorded as such, not hidden
by a fitted offset, excluded emitter pixels or a new global allowance.

The capacity request allows at most two tool compilations including any
meaningful-red API scaffold and one OFF diagnostic draw without warm-up
or light integration. It requests no simultaneous later comparison slot.
All old allocations remain RELEASED. Until a fresh assignment arrives,
work is limited to source/evidence inspection and documentation, with no
tool/test behavior edits, builds, tests, numerical evaluations or draws.

## November 6: direct geometry audit, 05:16 allocation completed

Parent approved one new bounded geometry-measurement allocation. It is now
RELEASED, with no later reservation. The exact pinned96db37df9 checkout,
dirty prototypes, old controls, negative results and rounding hypotheses
are preserved. Only reference tool/test behavior changed in this stage.

Two permitted Release tool compilations ran against existing Release
dependencies with BuildProjectReferences=false/no restore. The executable
baseline gave FOUR meaningful assertion reds and THREEpasses; the final
focused run passed17/17 with no errors/failures/skips. No old lighting
integration was rerun. Final tool SHA256:
0974e57225ce7d4d18bbe7dc1a9eefb8ce797ca7b90608624eccf142424215e2.

ONE original smooth-OFF diagnostic frame ran at native1280x720, zero jitter,
original80x160crop, draw0/tick1/time1/60/resettrue. No warm-up or measured
lighting frame ran. Four owned RG32color targets and owned D32depth replayed
the completed scene with the byte-identical production mesh.vert, original
range/culling/material/indirect draw rules and geometry helpers. Three range
rendering instances added one indexed-indirect API call, not another frame.

All12,800audited scalar distances equal both independent R32G-buffer and
probe distances EXACTLY. Audited mesh position/normal/material agrees with
the original analytic visible surface at all12,800pixels:12,486plate and
314emitter, no unknown/excluded pixels. Geometry/material classification
does not use emission or brightness. Raw emitter/plate light accounting
and original reconstructed shading receiver remain unchanged.

Real whole-image raw GPU-to-buffer copies surrounded the audit in the SAME
command sequence. Distance/history/composition before/after bytes match,
and exact original tracked layouts/stages/access are restored. These
are not host readbacks taken only after the pass. Attachment handles and
readonly shader resources are audited; no original image is attached.

Original ideal-camera position failures STILLnumber26 at0.0001meter.
They remain in original validation.json and audit-validation.json. The
generic stdout's "first-draw preflight passed" means only direct geometry
alignment here, NOT an original analytic-position pass or lighting result.
The old validator's throwing entry point retains its original behavior.
Max interpolated-mesh versus reconstructed-shading difference is34.1605um
at572,258; max interpolated-mesh versus ideal-camera hit is246.4036um at
572,254. The measured geometry and reconstructed shading receiver are
different quantities. Their difference must not be hidden or substituted
into the later reference. No global tolerance was widened or fitted.

New evidence is about4.2MiB, below8MiB. Full field definitions, exact
shader/tool/source identities, per-pixel residual CSV, old failures,
surrounding-copy hashes and original numeric metadata are retained in:
live-reference-comparison/geometry-audit-20261106-0516/README.md.
All10production-reference and10median capture/control-input hashes verify
before/after. All owned build/test/render processes ended and release was
sent directly to parent. ZERO OF FOUR lighting cases remains. This is
measurement alignment on one fixture/device, not independent certification
of engine geometry, full-crop reference radiance or production acceptance.

## 2026-11-06: first completed audited live/reference comparison

Fresh audited-live allocation completed and was explicitly RELEASED06:08:18EST.
Pinned96db37df9 and all dirty prototypes/old tools/control archives unchanged.
Only tool measurement acceptance and compatibility wiring changed. Real baseline
11checks gave10assertion reds/1pass; final29/29green. TWO Release tool builds,
separate affected tests, existing production references/no restore. Final tool
b394beba494b5d63a15b4fc7809045bcd93c47d06af983cf46b8d94f9e535e2c.

FOUR OF FOUR smooth/rough xOFF/all-ON configurations completed first attempt,
1280x720nativeZEROjitter/original80x160crop/exposure-6.8/unchangedfixture.
Exactly64warm+32measured draws each, one reset. All1152 genuine surrounding
distance/history/composition hash pairs agree;12,800exactdistance/visibility
matches per assessed frame, no exclusions. Old26analytic failures remain
negative diagnostics, not a changed tolerance. Actual reconstructed receivers,
decoded material and recorded source storage were used, not ideal substitutes.

Only approved center levels128/512/2048and shifts41/97, both materials, ran.
Outgoing visible-plate specular radiance is W/(m^2sr), pre-display/noFSR.
Crop signed sums below are sums of pixel radiances, not physical power.

| Material | OFF vs reference signed sum | OFF relative L1 | ON vs reference signed sum | ON relative L1 | ON vs OFF signed sum |
| --- | ---: | ---: | ---: | ---: | ---: |
| Smooth | +4.472413% | 7.103088% | +2.277541% | 6.170770% | -2.100911% |
| Rough | +5.410265% | 16.846191% | -24.428007% | 28.236199% | -28.306799% |

Reference512vs2048L1 .172573%smooth/.474224%rough; finalshiftL1 .133329%/.359877%.
Not certified error bounds. Local smooth edge differences exceed1000W/(m^2sr),
and rough live last8/first8 measured-frame L1 drift25.9%OFF/18.6%ON shows that
32frames do not establish a converged live pixel solution. Reference display
shows rough shoulders missing from ON. This is evidence for the user's light-
loss concern, not authority to retune or an isolated limiter-error measurement.
Live source has fast-history feedback; reference is one bounce. Original ideal
pixel-area references remain separate; center/area L1 differences2.2311%smooth/
.6905%rough include footprint, decoded material/receiver and storage differences.

All original sources/binaries/failures remain preserved. Full actual fields,
units, geometry distinctions, clocks, XML and byte identities are documented in
live-reference-comparison/audited-live-20261106-0555/README.md. New retained
evidence~61.38MiB before sealing, cumulative<128MiB; own CSVresiduals losslessly
compressed with original hashes. All10production/10control/35runtime identities
and previous geometry89-file seal verified after. No owned process or allocation
remains. No production quality/performance/reference-ground-truth acceptance.

The full goal remains open. Proposed next bounded trust check, not yet run:
independent emitter-face integration at predetermined actual decoded edge/
shoulder receivers, rather than a higher-resolution full-image sweep or fitted
gain. It needs its own fresh capacity assignment. No live policy change follows
automatically from these measurements.

## 2026-11-06: point-only independent verification not yet run

Parent assigned a separate fresh06:15slot for17preselected captured points x2
materials,at most68unchanged emitter-face quadratures. The current comparison's
four valid cases remain sealed322files,manifest8f1f737183fcb738ce1ad6c121acef61f6dc78deb1dd723bf2a3aa813b4a8628.
Baseline Release tool/test compiles succeeded and ten focused point-entry
checks produced5meaningfulassertionreds/5passes. Final permitted tool build
stopped on twoCA1861constant-array findings in Program.CapturedPoints.cs20/22.
No final tests or point command ran, ZERO quadratures. No retry/source correction.
The allocation was explicitly RELEASED06:26:37EST after every owned command
ended. Existing production/control/runtime/prior-stage seals verify unchanged.
Full failed source/logs/receipt:live-reference-comparison/captured-points-20261106-0615/README.md.
No new reference-validity conclusion follows. Active goal remains open.

## 2026-11-06: bounded captured-point verification stopped honestly

A fresh 06:33 recovery allocation used the parent-only constant-array correction,
one final Release tool build and a separately built affected test project.
Thirty focused pure checks passed, including all ten new point checks.
Existing reds were not repeated. Frozen tool SHA256:
8ebabf9bf3dca47e4c45529f72b2cf6a1d01cc1db391ab592aa981be4cda14e2.

The single fixed command retained all 34 actual warmed decoded plate inputs
before integration, then stopped at receiver two under its unchanged criterion.
Four existing emitter-face quadratures ran. The other 32 receivers, including
all rough receivers, remain unevaluated. At smooth (550,260), both converged;
the 2048-pair mean differs by -0.0478623705% locally. At smooth (570,260), the
fine quadrature did NOT converge within 299914 of 300000 evaluations, despite
mean 1001.0501708984375 and fine 1001.4481474232464 W/(m^2 sr) agreeing to
-0.0397401030% locally (0.0385131005% of image peak). Small coarse/fine drift
does not override nonconvergence. No budget, points or sampler rounds were added.

All 34 retained receiver/material/source inputs use the decoded shading receiver,
not ideal Y=0 or audit positions. Existing quadrature error estimates are not
bounds. Its relative convergence scale is the maximum diffuse/specular RGB
component, not a specular-only relative-error promise. Full-crop accuracy remains
unestablished. The four valid live cases, center levels/shifts and 322-file seal
stay unchanged. The 89-file geometry and 42-file failed-point seals, ten production
references, ten controls and 35 runtime files verify before/after. Historical
26 analytic-position failures and one-bounce/source-feedback limits remain
unchanged. All owned commands ended at 06:38:34 EST. The allocation was explicitly
RELEASED immediately, with no future capacity held. Full JSON/CSV/request/inputs
and receipt:
live-reference-comparison/captured-points-recovery-20261106-0633/README.md.
The active goal remains open. This verification did NOT satisfy all requested
criteria.

## 2026-11-06: extended work budget satisfies all selected-point criteria

With a fresh 06:46 allocation, the parent raised only the maximum quadrature
work to 3000000 evaluations. Relative/absolute tolerances, criteria, intended
material/DFG equations, integrator and fixed 17 points for both decoded materials
were unchanged. One Release tool build, one separate affected-test build and
all 30 focused pure checks passed. The one authorized numerical command then
completed all 34 receivers and 68 quadratures in 5011532 actual evaluations.
All 68 converged and all 34 combined diagnostic criteria passed.

The largest mean error relative to the retained image peak is 0.193020568%
smooth and 0.273930637% rough, below the agreed 1%. Largest coarse/fine drift
is 0.0000162951% and 0.0000163559% of peak, below 0.01%. These support using
the reference for broad several-percent diagnostic differences at the tested
receivers. They do NOT bound all crop pixels or establish physical energy.
Worst local relative differences remain -6.004208913% smooth at dim (574,255)
and +1.672791331% rough at (550,320). The receipt/JSON/CSV retain actual
radiances and local errors alongside peak normalization.

All 34 decoded input records are byte-identical to the prior 300000-budget
stage. Its false convergence and frozen binary remain untouched. The previously
exhausted smooth (570,260) fine quadrature now converged at 1388458 evaluations,
without a tolerance or model change. Across both captured-point commands:
72 calls and 5670656 actual evaluations. Error estimates remain estimates,
not bounds, and relative stopping scales diffuse/specular terms together.

All four live cases, all levels/shifts and 322/89/42/58-file prior seals verify
unchanged, as do ten production references, ten controls and 35 runtime files.
The historical 26 analytic-position failures and live fast-history-feedback
versus one-bounce-reference distinction remain. No render/capture, sampler
rerun, full-image integration, production edit, dependency build or new point
was added. All owned commands ended 06:49:42 EST; allocation explicitly RELEASED
immediately, no future capacity held. Frozen tool SHA256:
3ffafdde12c8132e1369249e4b3b8644b4768a3e08ec8b2a2cca2d6df7307b0b.
Full scoped receipt:
live-reference-comparison/captured-points-budget-20261106-0646/README.md.

### Offline light, edge and temporal observables, 2026-11-06

The reference tool now has a saved-data entry point:
`--metrics <retained-stage> --out <new-directory>`.
It reads the existing four audited native cases and 128 retained frames.
It makes no new renderer draw or light integration. Tool documentation gives
the full definitions and output schema.

One 2048-pair center-reference mean owns all thresholds, regions and visibility
for each material. Horizontal scanlines are primary. Column-mean and row-mean
profiles are aggregate diagnostics. Background is physical zero. The anchor
profile maximum defines fixed10/50/90percent contrast levels for every
candidate. All roots and exact-level intervals remain in the report.
Only unique unobscured crossings yield numeric edges. Candidate shape width
means central5–95percent cumulative light in the measured crop. It never
renormalizes radiance or moves the fixed loss bands.

The shared summation mechanism preserves ReferenceDifference. It separates
missing and extra light, so they cannot cancel. OFF/reference and ON/reference
use reference light as denominator. ON/OFF uses OFF light in the same anchor
regions. These are image-space pixel-radiance sums, not physical watts.
All 12,800 crop pixels remain in full-crop totals.

| Case | Full missing / reference | Full extra / reference | Total / reference | Fixed outer-band missing / band reference |
| --- | ---: | ---: | ---: | ---: |
| Smooth OFF | 1.3153% | 5.7878% | 104.4724% | 3.7972% |
| Smooth ON | 1.9466% | 4.2242% | 102.2775% | 8.2244% |
| Rough OFF | 5.7180% | 11.1282% | 105.4103% | 8.4887% |
| Rough ON | 26.3321% | 1.9041% | 75.5720% | 73.0911% |

The outer bands are anchor left10–left50 and right50–right10, with fractional
unit-pixel-cell overlaps. Their combined observable is valid on141/160rows
for both materials. Invalid rows retain occlusion, crop and ambiguity reasons.
One row can have several reasons. Eligible rows contain99.7647% of full
smooth reference light and99.6906% of full rough reference light. The bands
themselves contain1.3415%/13.9526% of full reference light. The report states
both quantities. An invalid band does not exclude its row from crop totals.

The ON/OFF full-crop change is−2.1009%smooth and−28.3068%rough, normalized
by OFF light. Rough ON also narrows the crop-limited column-mean5–95width
from25.4678pixels OFF to16.7806pixels ON. The reference width is24.4711pixels.
This is a shape-only aggregate, not proof that every individual edge agrees.

For rough ON/reference, valid left50displacements average+1.6981pixels on
110rows. Right50displacements average−1.9241pixels on114rows. Both signs
are inward for a bright bar. Their eligible rows contain89.6810%/89.9772%
of full reference light. The report retains every other row's reason rather
than selecting a convenient crossing.

Temporal variation and mean error remain separate. Root-mean-square means
the square root of the mean squared quantity. Temporal SD is the sample
standard deviation across 32 correlated advancing frames. Persistent mean
error is the recorded per-pixel mean minus the reference.

| Quantity, W/(m²sr) | Smooth OFF | Smooth ON | Rough OFF | Rough ON |
| --- | ---: | ---: | ---: | ---: |
| Root-mean-square per-pixel temporal sample SD | 13.3464 | 11.8201 | 33.7155 | 26.5231 |
| Root-mean-square per-pixel persistent mean error | 52.0801 | 48.1213 | 36.1504 | 48.6839 |

The report also retains first-eight/last-eight drift and valid/missing edge counts.
It supplies no independent-sample confidence interval or convergence claim.

Saved128/512means and2048shifts41/97 use the same frozen basis. Their full-crop
signed changes range−0.00152%..+0.00485%smooth and−0.00154%..+0.00491%rough.
Local metrics remain less stable. The largest retained reference-only row50
displacement is about1.31pixels smooth and1.06pixels rough. The largest
central5–95row-width change is21.93pixels smooth and3.40pixels rough.
Validity sets also change across reference candidates. These are observed
sensitivities, not error bounds. Small total variation does not certify local
edges or dim tails.

Validation used 3 Release tool compilations and 3 separate affected-test
compilations with existing Release references, no restore and no dependency
build. The baseline had4meaningful assertion failures and12passes. One tool
style failure and one tests-only access-name failure remain in evidence.
The final focused checks passed 52/52. One offline report command exited 0.
Its post-command wrapper exited1 because retention exceeded the8MiB target:
11224KiB at the guard. Cumulative evidence was107364KiB, below128MiB. No
automatic rerun or further compression phase followed. This is not a green
retention verdict.

All original seals, controls and production references remain unchanged.
All 33 saved report inputs/tool bytes matched before/after. The historical 26
ideal-position failures remain failed diagnostics. Live source includes
fast-history feedback while the reference is one bounce. These results
measure whole-pipeline deviation, not isolated limiter loss or production
acceptance. Parent owns independent inspection and integration.

All owned heavy processes ended07:54:33EST. The allocation was explicitly
RELEASED immediately. No later allocation is held. Frozen tool SHA256:
4f7661dc2628fd0fa2cb25cc6213761778a2c70906ec6b882b8d40eeea4b59a7.
Evidence: live-reference-comparison/offline-metrics-20261106-0733/README.md.

### Narrow neighbor-cap isolation, 2026-11-06

The user selected the next experiment: keep the selected single diffuse
history and disable only the incoming neighbor cap. The user also required
temporal noise and actual renderer output alongside reference accuracy.
The research-only environment override leaves original OFF and selected
all-ON defaults unchanged. All 60 embedded engine shader modules were
byte-identical to the original Rendering assembly.

Six focused selection checks first produced two intended assertion failures
and four passes. Final focused checks passed 58/58, including the existing
52 pure comparison/geometry/metric/point-metadata checks. There were three
Rendering compilations, two tool compilation attempts and two affected-test
compilations. One tool attempt failed TS0003 null-check style rules. A later
successful tool compilation was followed by a retention-manifest setup
failure because a directory was passed to shasum. The exact successful
binaries were reused for the tests-only completion, not rebuilt again.
All failures remain preserved. Original build outputs were restored and all
old seals and controls verified after each phase.

Two new commands completed: smooth-uncapped and rough-uncapped, each with
64 warm-up draws and 32 measured draws, original native 1280x720 zero jitter
and the 80x160 crop. All 96 frames per case used the existing direct geometry
audit and surrounding-copy checks. No reference integration, original OFF/ON
recapture, new camera or fitted gain occurred. Raw outgoing, emitter and
plate data remain separate, with original-byte hash receipts for losslessly
compressed new diagnostics.

The post-capture wrapper failed the cumulative retained-evidence guard:
138228 KiB versus 131072 KiB. Disk remained 34662376 KiB, above 5242880 KiB.
All owned heavy commands ended at 09:00:47 EST and were released. No offline
cap-isolation metric report or single-frame preview export ran. Accuracy,
noise changes and exact cross-case source compatibility remain unreported;
successful individual captures do not establish those results. No later
reservation is held and no production or reference acceptance is claimed.

Frozen tool SHA256:
c2b50c4a8fd546a2b133110677d3f3a298ac880e4ddb9bad3298d8da74e9a308.
Frozen experiment Rendering SHA256:
7cce5a269847c7537ec47f54f57ed7ea52569b627e4c3ae4e38671493148b261.
Evidence: live-reference-comparison/cap-isolation-20261106-0850/README.md.

### Cap-isolation offline completion, 2026-11-06

The user explicitly approved increasing cumulative retained evidence to
160 MiB and finishing offline analysis/previews only. The old 128 MiB
post-capture guard failure and its exact seal remain unchanged. One frozen
tool report completed, followed by one bounded retained-data verification
and presentation command. No build, test, renderer frame, random sample or
light integration occurred in this continuation.

The same frozen 2048-center basis defines all thresholds, fractional bands
and visibility. Every crop pixel remains in full totals. Both new uncapped
cases match the original OFF/ON receiver fields 1..15 byte-for-byte at all
12800 pixels, with identical classification bytes. Field0 is measured outgoing
light and deliberately differs. Recorded source/exposure/camera/material
metadata passed compatibility checks. All old OFF/ON comparison and noise
objects reproduce exactly. All 43 actual report inputs/tool hashes match
before and after. Seven gzip tables match their plaintext byte counts/hashes.

| Roughness 0.2 observable | Cap ON, selected history | Cap OFF, same selected history | Original whole-prototype OFF |
| --- | ---: | ---: | ---: |
| Total / reference crop light | 75.5720% | 105.4089% | 105.4103% |
| Pixelwise missing / reference crop light | 26.3321% | 5.7190% | 5.7180% |
| Pixelwise extra / reference crop light | 1.9041% | 11.1280% | 11.1282% |
| Missing / fixed outer-band reference light | 73.0911% | 8.4888% | 8.4887% |
| Crop-limited column-mean central 90%-light width | 16.7806 px | 25.4679 px | 25.4678 px |
| RMS per-pixel temporal sample SD | 26.5231 | 33.7151 | 33.7155 |
| RMS persistent per-pixel mean error | 48.6839 | 36.1510 | 36.1504 |

Noise/error units are W/(m² sr). RMS means square root of the mean squared
quantity across crop pixels. Temporal sample standard deviation measures
frame-to-frame variation over 32 correlated advancing frames. Persistent
mean error compares each recorded temporal mean with the reference. These
are different observables, not a score or confidence interval. Reference
central 90%-light width is 24.4711 pixels. Full totals are image-space sums
of radiance, not physical watts. Outer bands remain valid on 141/160 rows;
eligible rows contain 99.6906% of rough reference crop light, while the bands
themselves contain 13.9526%. Invalid band rows are never removed from crop
totals. Reference-level/shift sensitivities remain in the same frozen basis.

Relative to cap ON, disabling the cap raises rough temporal noise RMS by
27.1160% and lowers mean error RMS by 25.7434%. The rough halo and width
return, but the result is still noisy and locally inaccurate. Visible
speckling also remains in the 32-frame mean, so temporal sample SD alone
does not describe every persistent spatial artifact.

For roughness 0.045, cap OFF raises noise from 11.8201 to 13.3464 W/(m² sr),
or 12.9121%, and mean error from 48.1213 to 52.0798 W/(m² sr), or 8.2260%.
Full total rises from 102.2775% to 104.4722% of reference light. Removing
the cap is not a uniform improvement across the two materials.

Uncapped versus original OFF full-crop L1 is only 0.001333% of OFF light for
rough and 0.000252% for smooth. Thus the selected diffuse-history change
alone barely changes this measured specular fixture. The intervention
isolates the cap setting while allowing its downstream history feedback to
respond; it does not separate direct cap loss from every feedback effect.
No conclusion about diffuse output, other scenes, production defaults or
physical energy equivalence follows. The independent reference remains one
bounce while the live source contains fast-history feedback.

Actual single-frame previews use draw95, chosen before inspection. Separate
mean previews use draws64..95. Every preview uses exposure-6.8, Reinhard and
sRGB, without brightness fitting. They show the 80x160 outgoing visible-plate
specular crop, not the full composed game. `comparison.html` contains rough
and smooth views plus a noise/error chart; JSON/CSV retains all raw values,
profiles, crossings, coverage and temporal variation.

Both offline commands exited0. The presentation command ended at09:35:11EST;
all owned heavy work was explicitly RELEASED immediately, with no later
reservation. Cumulative evidence remains below the approved160MiB limit
and disk above5GiB. All original seals, controls, frozen runtime and old
failed analytic-position diagnostics remain unchanged. No new builds or
tests were needed; the preceding58/58 focused greens remain the build check.
Evidence: live-reference-comparison/cap-isolation-report-20261106-0928/README.md.

### Design A: output-only reflection sharing, 2026-11-06

The user selected a bounded reflection-sharing prototype after inspecting the
cap-isolation results. The user also required actual renderer output and noise
measurements alongside reference error. This experiment changes final reflection
reconstruction, not the intended material or the independent reference.

`TERTIUS_REFLECTION_SHARING=1` enables one3x3 neighborhood in final composition.
It exchanges outgoing specular radiance using symmetric nonnegative pair weights,
at most1/16per neighbor. Exact receiver-material matching, normal/plane checks and
roughness-scaled dominant-reflection-direction agreement restrict the exchange.
It reads the accumulated specular history without writing source/history images.
It adds no retained production image. The ordinary switch-off behavior remains
unchanged. The tool's `sharing` mode selects the existing uncapped prototype,
which disables incoming neighbor capping for both signals and retains the chosen
single diffuse history. Only reflected-light reconstruction gets the new filter.

Two new configurations completed: smooth-sharing and rough-sharing. Each used
64warm-up draws0..63 and32measured draws64..95, one initial reset, native1280x720,
zero jitter, fixed exposure-6.8 and the original80x160crop. Existing OFF, all-ON,
uncapped and reference data were reused without recapture or integration. The
geometry audit, independent distances, decoded receiver data, source/exposure
checks and surrounding-copy checks remained active. All32unfiltered measured
frames were byte-identical to the uncapped control for each material. All16
decoded fields and classifications also matched. Retained distance/history
snapshots matched, and288surrounding-copy pairs per configuration were unchanged.
Every saved raw scalar equaled plate plus emitter contribution. No pixel mask,
fitted gain, light-sampling change or material adjustment was used.

All light quantities below describe outgoing visible-plate specular radiance
before display, in W/(m²sr). Crop totals are image-space radiance sums, not watts.
Missing and extra light sum positive pixelwise deficits and surpluses separately.
Their denominator is the unchanged reference crop total. Outer missing light
instead uses the reference light inside the fixed outer bands as its denominator.

| Observable | Rough uncapped | Rough sharing | Smooth uncapped | Smooth sharing |
| --- | ---: | ---: | ---: | ---: |
| Total / reference crop light | 105.4089% | 105.4066% | 104.4722% | 104.4719% |
| Missing / reference crop light | 5.7190% | 3.9937% | 1.3155% | 1.4948% |
| Extra / reference crop light | 11.1280% | 9.4003% | 5.7877% | 5.9668% |
| Missing / fixed outer-band reference light | 8.4888% | 4.8452% | 3.7972% | 3.1063% |
| Column-mean central90%-light width, pixels | 25.4679 | 25.5634 | 17.2906 | 17.3449 |
| RMS per-pixel temporal sample SD | 33.7151 | 23.8261 | 13.3464 | 9.8926 |
| RMS persistent per-pixel mean error | 36.1510 | 31.0913 | 52.0798 | 48.5192 |

RMS means the square root of the mean squared quantity over crop pixels.
Temporal sample standard deviation describes variation across32correlated
advancing frames. Persistent mean error compares each pixel's32-frame mean with
the reference. Neither is a confidence interval. The fixed outer bands are valid
on141/160rows. All12800pixels remain in crop totals. Band coverage and every
invalid-row reason remain in the report. Central90%-light width is the crop-limited
central5–95% cumulative-light width of the column-mean profile. Reference widths
are24.4711pixels rough and16.6027pixels smooth.

Relative to uncapped, sharing lowers temporal noise RMS by29.3312%rough and
25.8781%smooth. Persistent mean-error RMS falls13.9959%rough and6.8368%smooth.
Crop totals change by-0.0021921%rough and-0.0002730%smooth, normalized by uncapped
light. Rough missing and extra light both decrease. Smooth missing and extra
light both increase slightly, despite its lower RMS error. Both reflections widen
slightly from an already wider-than-reference profile. This is not uniform
improvement of all accuracy observables.

Inspected draw95previews show fewer isolated bright pixels and more locally
shared light, but visible speckling remains. The32-frame means retain spatial
speckling too. All previews use the same exposure-6.8, Reinhard and sRGB mapping,
without brightness fitting. They show specular crops, not full composed game
images. The retained comparison includes separate single-frame and mean views,
common-axis row320profiles, reference sensitivity and temporal measurements.

Pair symmetry supports preservation of the full-image radiance sum in exact
arithmetic. It does not establish physical power conservation, exact crop-sum
equality or correct reflection shape. This filter has no reflected-object identity
or new hit-distance guide. Receiver geometry cannot protect every reflected
silhouette. Exact material matching can disable sharing across texture variation.
The existing history clamp remains. Performance, motion, disocclusion and other
scenes were not evaluated. The live source still includes history feedback while
the independent reference is one bounce. Original26analytic-position failures
remain failed diagnostics; the measured geometry audit does not relabel them.

Meaningful red checks produced4intended assertion failures and2passes, followed
by78/78focused greens including real GPU sharing and accounting. Earlier setup
failures remain preserved: missing shader include, illegal test upload usage and
stale test metadata references. They are not behavioral reds. Actual build totals
were3Rendering compilations,3tool compilation attempts and4affected-test
compilations. One tool and one test compilation failed. Three test executions
were recorded: setup-invalid failures, meaningful reds, then greens. No full
suite, coverage, mutation or timing run was used. Freezing after the green build
also failed on a directory hash; the exact successful binaries were retained and
reused without another build. The corrected file-only runtime inventory is
`frozen/runtime-final.sha256`.

Both captures validated, but their wrapper exited93at the retained-evidence
guard. The offline report completed, but its wrapper also exited93before final
packing. These wrapper failures remain failures. Neither capture nor report was
rerun. Lossless compression and base/delta storage affected only new evidence;
old controls and evidence were untouched. All7complete table byte streams were
reconstructed and verified against the original C#byte-count/SHA256receipts.
Old OFF/ON/uncapped metric and noise objects reproduced exactly.58/60engine
shader modules were byte-identical to uncapped; only the two composite variants
changed. Mesh, source, estimator and history shader bytes remained unchanged.

The final verification/presentation command exited0. All owned heavy processes
ended and were explicitly RELEASED at10:36:38EST. No later reservation remains.
Cumulative evidence was163512KiB at that release, below the approved163840KiB
limit but with little remaining space. No production/default-policy, physical
energy, reference-wide accuracy or performance acceptance follows.

Frozen Rendering SHA256:
258b0357cc3af459422f57f56c8c0e9ea1a482067e6599d1ddf1dfed0dcf1aae.
Frozen tool SHA256:
3c8582028c15054dffdbd9f7d01c0a846175a76c06acf813098cf0c4d5ff3c93.
Evidence: live-reference-comparison/reflection-sharing-20261106-1006/README.md.

### Wider Design A with receiver continuity, 2026-11-06

The user selected a wider single pass, not repeated local passes. The user also
approved additional retained storage as needed. This stage set a conservative
256MiB cumulative comparison cap, kept the5GiB free-disk guard and retained all
old controls/evidence. No new allocation request, child or workflow was used.

The current prototype extends the neighborhood from3x3 to5x5 pixels. Maximum
pair weight changes from1/16 to1/48, so the24neighbors still exchange at most
one half of the center value in total. Roughness/reflection-direction weighting
is unchanged. A larger neighborhood therefore does not promise a larger actual
exchange: rejected or low-weight neighbors still reduce the exchange.

The stronger receiver check requires exact material parameters, both geometric
and shading normals within10degrees, and symmetric plane agreement. Plane
tolerance is the larger of0.02times the smaller distance times native pixel
angle, and8float32 epsilons times the larger distance. Distances are in meters;
the pixel angle is in radians. The first term is a world-scale pixel-angle proxy,
not an exact footprint on a grazing surface. The second term handles arithmetic
precision. These are filter decisions, not changes to geometry validation.
The original0.0001m analytic-position failures remain failed diagnostics.

An integer segment traversal checks every pixel cell touched between the two
centers. At corner contact it checks both side cells. Each intermediate receiver
must agree with both endpoints, keeping acceptance symmetric. This prevents
endpoint-only jumps over gaps or depth/material/normal discontinuities. It does
not prove object identity for indistinguishable coplanar surfaces. It also does
not identify the object visible in a reflection. Source/history remain read-only,
and the switch remains disabled by default.

Focused GPU tests first ran against the existing frozen3x3 tool, without a
baseline engine/tool rebuild.24checks produced11meaningful assertion failures
and13passes: missing wider reach plus leakage across a small depth step and a
shading-normal boundary. Existing gap controls passed and remain regression
checks for the wider implementation. One Release Rendering build and one Release
tool build then succeeded, followed by the second separate tests-only build.
All96focused GPU/CPU checks passed, with no warnings or Vulkan errors. No setup
or compilation retry was needed. Production dependency builds and restore were
disabled. No full suite, coverage, mutation or timing run occurred.

Exactly two new configurations completed, smooth/rough sharing. Each used one
initial reset,64warm-up draws0..63 and32measured draws64..95, native1280x720,
zero jitter, original80x160crop/fixture and exposure-6.8. All32unfiltered frames
per material match uncapped bytes. All16decoded fields/classifications and saved
distance/history crops match both uncapped and3x3controls. All576surrounding-copy
pairs are unchanged, and every saved raw scalar equals plate plus emitter.
58/60engine shader modules match uncapped; only the two composite modules differ.
The original audit, receiver checks and primary visibility accounting remain.

One offline report reused the exact frozen reference basis and existing
128/512means and2048shifts. All old OFF/ON/uncapped metric/noise objects and
reference-sensitivity objects reproduced exactly. All7new gzip tables match their
original plaintext byte-count/SHA256receipts. No new camera, reference integration,
sample, light fit, mask or extra capture round was used.

| Observable | Rough3x3 | Rough5x5 | Smooth3x3 | Smooth5x5 |
| --- | ---: | ---: | ---: | ---: |
| Total / reference crop light | 105.4066% | 105.4025% | 104.4719% | 104.4718% |
| Missing / reference crop light | 3.9937% | 3.6346% | 1.4948% | 1.6305% |
| Extra / reference crop light | 9.4003% | 9.0371% | 5.9668% | 6.1023% |
| Missing / fixed outer-band reference light | 4.8452% | 3.7350% | 3.1063% | 3.0941% |
| Column-mean central5–95%width, pixels | 25.5634 | 25.7654 | 17.3449 | 17.3685 |
| RMS per-pixel temporal sample SD | 23.8261 | 20.0502 | 9.8926 | 11.1236 |
| RMS persistent per-pixel mean error | 31.0913 | 30.7705 | 48.5192 | 49.6281 |

Noise/error units are W/(m²sr) of outgoing visible-plate specular radiance before
display. Temporal sample standard deviation describes32correlated frames; mean
error compares their per-pixel mean with reference. No independent-sample
confidence interval or convergence claim follows. Missing/extra denominators are
the unchanged reference crop sum. Outer missing uses fixed-band reference light.
The bands remain valid on141/160rows, with all invalid reasons/coverage retained.
All12800pixels remain in crop totals. Those totals are image-space radiance sums,
not physical watts. Width is crop-limited and shape-only. Reference widths remain
24.4711pixels rough and16.6027pixels smooth.

Against3x3, wider sharing changes rough noise RMS by-15.8478%and mean-error RMS
by-1.0318%. The rough halo is less grainy in the inspected frame, but clumps and
persistent spatial speckling remain. Rough width moves farther from reference.
Smooth noise RMS increases12.4438%and mean-error RMS increases2.2856%; missing
and extra light both worsen. Totals change only-0.0039433%rough and-0.0000946%
smooth, normalized by3x3light. The rough improvement is not a universal5x5win.

This experiment changes range, receiver acceptance and per-neighbor budget
together. It cannot separately attribute the smooth regression to those changes.
The fixed total budget plus the existing narrow-lobe direction weights can leave
less effective exchange when distant taps carry little weight; that is a source
explanation to investigate, not an isolated measured cause. No radius, strength,
roughness policy or threshold was retuned after the results. Both prototypes stay
preserved for the next user decision.

Actual draw95and separate32-frame mean crops use the same exposure-6.8/Reinhard/
sRGB mapping, without brightness fitting. They are specular crops, not composed
game images. The live source includes history feedback; reference is one bounce.
No performance, motion, general-scene or production/default-policy acceptance is
established. All builds, checks, two captures, report and presentation wrappers
exited0. All owned heavy commands ended and were explicitly RELEASED at
11:41:42EST. No later reservation remains. Cumulative evidence was191944KiB at
that release, below262144KiB; remaining lightweight notes/sealing are additional.

Frozen Rendering SHA256:
a4c9dbfc9865416cdf1907f6758d7fe4cbeedfa749b58779bd3fc4eb932f3b44.
Frozen tool SHA256:
a431e3d2107b0fe895fd46a0437eedb5a0fff5bd02df96d7a71e4d48c6ad4b3f.
Evidence: live-reference-comparison/reflection-wide-sharing-20261106-1135/README.md.

## H1 DesignB: roughness and hit-distance footprint, November6 14:05EST

User selected DesignB after the fixed5x5 experiment. Sharing range now depends on
roughness and ray-hit distance, with a separate near-mirror reconstruction cap.
The fixed5x5 search and all prior material/normal/plane/segment checks remain.
There is no change to the intended material, emitted light, ray plan or history
update. The switch remains off by default. Frozen3x3/fixed5x5 controls remain.

The distance already exists. `screen_trace.glsl` measures original ray origin to
reconstructed hit point in meters. Ray storage retains float16 distance, capped
at65504m. Resolve writes it to `specularSample.a`. Fast-history alpha copies the
latest distance, but slow-history alpha is history count. Composition now reads
existing `Specular.Sample(0)` through read-only binding4. Its descriptor count
changes4to5, with the required fragment-read barrier when sharing is enabled.
No distance image or trace is added. The distance is not temporally smoothed.
Shared2x2 samples retain donor-ray distance rather than a per-receiver trace.
The existing diffuse-ray handover can supply distance at high roughness. Both
measured materials remain below that handover.

The shader uses this exact footprint calculation after rejecting unknown distance:

```glsl
    float roughness = flooredRoughness(surface.roughness);
    float alpha = roughness * roughness;
    float coneSlope = 2.0 * alpha / max(1.0 - alpha * alpha, 1.0e-6);
    float projectedRadius = coneSlope * (hitDistance / (hitDistance + viewDepth)) / frame.params.z;
    float mirrorLimit = 3.0 * min(alpha / (0.1 * 0.1), 1.0);
    return min(projectedRadius, mirrorLimit) * viewDepth * frame.params.z;
```

where

- `surface.roughness` is decoded receiver material roughness, a dimensionless value in0..1.
- `flooredRoughness` applies the existing material floor, without a new material policy.
- `roughness` is that floored roughness, and `alpha` is its square.
- `coneSlope` is the dimensionless tangent of twice atan(alpha), with a finite fully rough endpoint.
- `hitDistance` is the latest traced ray-hit distance in meters.
- `viewDepth` is the camera-forward depth of the decoded camera-relative receiver point, in meters.
- `frame.params.z` is native center-screen angular pixel size, in radians per pixel.
- `projectedRadius` is the planar finite-distance footprint radius in native pixels.
- `mirrorLimit` is the near-mirror reconstruction cap in native pixel pitches.
- `max` and `min` select the larger and smaller operands. The return value is receiver-world radius in meters.

Both endpoint radii must cover their world-space separation. The cap scale0.1
and upper radius3pixel pitches were selected before captures, not fitted to
reference. The existing search remains offsets-2..2, not a larger search.
Zero, negative, nonfinite or saturated65504m distance permits no new sharing.
The symmetric nonnegative exchange remains at most1/48per neighbor and one half
in total. This is a planar normal-incidence GGX-cone proxy, not exact oblique
BRDF support, material-tail truncation or reflected-object identity.

The executable baseline retained fixed5x5 behavior with the new descriptor and
synthetic distance transport.35focused cases produced10meaningful assertion
failures and25passes, with no test errors. They cover range, near-mirror
protection, both endpoint radii, unknown distances and unchanged guide input.
The test-only pulse fixture uses0.25radFOV and300m hit distance so its32x24
resolution can exercise a real footprint. It does not change the live fixture.

The first final Rendering compile failed: the no-radius return guard was placed
in the bool cell-compatibility function instead of the outgoing-light function.
The failed source/log archive remains. One narrow correction moved only that
guard. Actual attempts were3Rendering Release builds,1final tool Release build
and2separate test build/run cycles. All used existing references with
BuildProjectReferences=false/no-restore. Final107/107focused checks passed,
with zero errors/skips. No suite, coverage, mutation or timing run occurred.

Exactly two new sharing cases completed. Each used one initial reset,64warm-up
draws0..63 and32measured draws64..95, native1280x720, zero jitter, original
80x160crop/fixture and exposure-6.8. No old control was recaptured. All32new
unfiltered frames per material match uncapped bytes. All decoded inputs,
classification and retained distance/history crops match the saved controls.
All576genuine surrounding-copy pairs are unchanged. Every raw scalar equals
plate plus emitter. The original26analytic position failures remain historical
failed diagnostics, not relabeled passes. The audited geometry path remains.

One offline report reused the frozen reference basis and retained levels/shifts.
Old OFF/ON/uncapped metric and temporal objects reproduce exactly, as do all
reference-sensitivity objects. All7new lossless gzip tables match their original
plaintext byte counts and SHA256receipts. No reference integration, random sample,
brightness fit, pixel mask or extra capture round was added.

| Observable | Rough uncapped | Rough fixed5x5 | Rough DesignB | Smooth uncapped | Smooth fixed5x5 | Smooth DesignB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Total / reference crop light | 105.4089% | 105.4025% | 105.4090% | 104.4722% | 104.4718% | 104.4722% |
| Missing / reference crop light | 5.7190% | 3.6346% | 5.5314% | 1.3155% | 1.6305% | 1.3155% |
| Extra / reference crop light | 11.1280% | 9.0371% | 10.9403% | 5.7877% | 6.1023% | 5.7877% |
| Missing / fixed outer-band reference light | 8.4888% | 3.7350% | 8.3166% | 3.7972% | 3.0941% | 3.7972% |
| Column-mean central5–95%width, pixels | 25.4679 | 25.7654 | 25.4778 | 17.2906 | 17.3685 | 17.2906 |
| RMS per-pixel temporal sample SD | 33.7151 | 20.0502 | 32.6706 | 13.3464 | 11.1236 | 13.3464 |
| RMS persistent per-pixel mean error | 36.1510 | 30.7705 | 35.5770 | 52.0798 | 49.6281 | 52.0798 |

Noise/error units are W/(m²sr) of outgoing visible-plate specular radiance before
display. Temporal sample standard deviation describes32correlated frames.
Persistent mean error compares their per-pixel mean with reference. No confidence
interval or convergence claim follows. Missing/extra use the entire reference
crop sum. Outer missing uses fixed-band reference light. Bands remain valid on
141/160rows. Invalid reasons and coverage remain explicit. Every pixel remains
in crop totals, which are image-space radiance sums, not watts. Width is
crop-limited and shape-only. Reference widths remain24.4711rough/16.6027smooth.

DesignB changes rough temporal noise by-3.0981%against uncapped but+62.9441%
against fixed5x5. Rough mean-error RMS changes-1.5877%/+15.6204%, respectively.
The rough crop again shows much of the uncapped speckling. Its width stays
near uncapped rather than the more spread fixed5x5 profile.

All smooth plate-contribution frames match uncapped bytes exactly. The near-mirror cap is
below one native pixel pitch for this material, so this fixture permits no
neighbor exchange. That prevents extra sharing blur, but also removes its noise
reduction. Smooth noise is19.9826%higher than fixed5x5, and mean-error RMS is
4.9400%higher. It is not evidence that this version denoises smooth reflections.

The separately retained raw emitter contribution does change. The plate-byte
identity does not claim identity of emitter data or raw whole-crop outgoing light.

Across399552plate/frame samples per material, known distance occurs68166times
rough (17.0606%) and67107times smooth (16.7956%). Remaining331386/332445values
are zero, with no other unknown values. Known/miss state changes between adjacent
measured frames total56186rough/4298smooth. These counts describe the latest
stochastic guide, not new exclusions from any metric. The conservative unknown
rule prevents exchange whenever either endpoint lacks distance, even when its
accumulated reflected light is nonzero. This is a concrete limitation of using
one latest ray to gate an accumulated signal. It does not isolate every source
of temporal noise or reject roughness/distance guidance in general.

Inspected actual draw95and separate32-frame means use identical exposure-6.8,
Reinhard and sRGB mapping. No brightness fit is used. These are specular crops,
not composed game images. The live source includes prior history feedback while
reference is one bounce. No general-scene, motion, physical-energy, default-policy
or performance acceptance follows. No coefficient was retuned after results.

All owned heavy commands ended and were explicitly RELEASED at14:18:09EST.
The final recorded heavy-phase guard was33675392KiBfree and228436KiBcumulative
evidence, below the user-authorized393216KiBcap. Lightweight sealing is additional.
No process or later reservation remains. Tickettiunr-mln1 stays open for integration.

Frozen Rendering SHA256:
8a53c2723380c9dfb50fb4699d9652cc643cff57c412c0c2eabe2cf2fd1d9d27.
Frozen tool SHA256:
9d91d076065aa42cc2ddf735bed1f0a001ca35953eb84ca32e376dea79cc53aa.
Evidence: live-reference-comparison/reflection-footprint-b-20261106-1405/README.md.

## Temporal distance history: implementation green, capture admission blocked

The user selected temporal distance history after the latest-ray footprint
experiment. Ticket `tiunr-o1ei` owns this follow-up. It does not change reference
integration, rays, source, material response or existing light RGB histories.

`TemporalSignal` owns a guide and scratch image only for specular sharing. The
RGBA16F guide stores distance moment in meters in R and dimensionless valid-hit
support mass in G; B/A are zero. The guide follows the same accepted light-history
reprojection, temporal sample weights and existing short-history repair. A miss
adds zero support, not a zero-meter hit. Representative distance is R/G when G
is positive. Symmetric sharing uses the smaller endpoint value of
clamp(G*current slow-history length,0,1). This is supporting-hit weight, not a
probability or error bound. Bright samples receive no extra distance weight.
The footprint, near-mirror cap, material/geometry checks and maximum exchange
remain unchanged. Anti-lag shortening of light history still limits guide trust.

Actual build accounting is two Rendering Release compilations, one tool Release
compilation and five affected-test compilations. Two test setup failures precede
the behavior reds. The same frozen baseline produced12intended history/repair
assertion failures, three passes and two tests blocked by a shared inactive-image
descriptor synchronization defect. Final wiring fixes that aliasing defect.
The final124focused cases passed with no errors/skips; all three final projects
compiled with zero warnings/errors. No expensive light-integration tests ran.

Capture admission then failed at shader identity, exit134, before any renderer
frame. Exactly54/60embedded shaders match the original uncapped assembly. The
expected four differences are generic temporal/spatial accumulation and the two
composite variants. Two additional diffuse-single-history variants also differ.
The new common include reaches those modules; their semantic equivalence is not
established here. The four-module guard was not weakened, and no capture/report
retry ran. Zero of the two new material configurations completed. There is no
temporal-history image, guide-coverage result or noise/accuracy comparison yet.
All old controls, source archives, earlier failures and reference results verify.

Frozen Rendering SHA256:
38af284901416556af1837c5555da7d5a6ef8cc806a0c15a54e871452f994d23.
Frozen tool SHA256:
275fd58cc7ca2cf36c0e32585fe51cc2df426c82c3587d71d2c64fd5256eaf20.
All owned commands ended and were RELEASED2026-11-06at15:11:12EST. No future
reservation remains. Preserve the failed shader manifest and frozen binaries.
The next correction must isolate distance-only declarations from the unused
diffuse variants or establish their exact equivalence before changing provenance.
Evidence: live-reference-comparison/reflection-distance-history-20261106-1445/README.md.

## Temporal distance history: completed narrow comparison

The user's Continue resumes ticket `tiunr-o1ei` without changing its filter model.
Guide-only declarations move from the common accumulator header to
`accumulate_reflection_guide.glsl`, used by generic temporal/spatial accumulation.
No light RGB, source, ray, material, reference, footprint or strength value changes.
The saved shader admission failure supplies the unchanged-diffuse regression red.

One Rendering Release build and one affected-test compilation pass with zero
warnings/errors;124focused checks pass. No tool or dependency rebuild runs.
The first recovery admission nevertheless loads the old engine through a linked
tool entry DLL. Its recorded CurrentAssembly identifies the old1445directory.
The rebuilt diffuse SPIR-V files already match the control hashes. Preserve this
setup failure and release; it is not a failed source correction or a new behavior
red. A separate local-entry namespace materializes the same frozen tool DLL.
Without another compilation, admission loads the correct engine and confirms
56/60shader modules identical, with exactly the original four allowed changes.
The whitelist remains unchanged. The tests used the corrected engine bytes.

Both smooth/rough cases complete once: native1280x720, zero jitter, original
80x160crop, exposure-6.8, one initial reset,64warm-up draws0..63and32measured
draws64..95. No old-control recapture or reference integration runs. All32unfiltered
frames, decoded receiver inputs and classifications match uncapped control bytes.
All576surrounding whole-image distance/history/composition hash pairs match.
Original analytic-position failures remain diagnostics, not reapproved passes.
Raw, plate and emitter outgoing arrays remain separate and sum exactly.

| Observable | Rough uncapped | Rough latest ray | Rough distance history | Smooth uncapped/latest/history |
| --- | ---: | ---: | ---: | ---: |
| Crop radiance sum / reference, % |105.4089|105.4090|105.4090|104.4722|
| Missing / whole-crop reference, % |5.7190|5.5314|5.0760|1.3155|
| Extra / whole-crop reference, % |11.1280|10.9403|10.4850|5.7877|
| Missing / fixed outer-band reference, % |8.4888|8.3166|6.9188|3.7972|
| Temporal noise RMS |33.7151|32.6706|29.2528|13.3464|
| Persistent mean-error RMS |36.1510|35.5770|34.0402|52.0798|
| Column-mean central5–95%width, pixels |25.4679|25.4778|25.5474|17.2906|

Noise/error units are W/(m²sr), outgoing visible-plate specular radiance before
display. Noise is the root mean square of per-pixel temporal sample standard
deviation. Mean error compares the32-frame mean with retained reference. These
32advancing frames are correlated; no confidence interval or convergence claim
follows. Crop sums are image-space radiance sums, not watts. First8/last8rough
crop-sum drift remains14024.3550against14002.6531uncapped; variability is not
assumed stationary noise. Existing128/512means and2048shifts use the same frozen
reference basis; their previous sensitivity results are exactly unchanged.

Rough noise falls13.2353%against uncapped and10.4613%against latest-ray guidance.
Rough mean-error RMS falls5.8388%and4.3197%, respectively. Outer missing light
also falls, without a meaningful crop-total reduction. However, rough width grows
0.0795pixels against uncapped; reference width is24.4711pixels. Width therefore
moves slightly farther from reference. This is not improvement on every metric.
Fixed outer bands remain valid on141/160rows. Those rows contain99.6906%of rough
and99.7647%of smooth reference light. The bands cover13.9526%and1.3415%of whole-crop
reference light, respectively. All invalid reasons/counts and every crop pixel
remain in the retained report; no brightness/error mask is added.

Rough usable guide samples rise from68166/399552(17.0606%)to339201/399552(84.8953%).
Mean valid-hit support mass is0.168545. Mean effective supporting-hit weight is
0.407366at the first measured frame and0.415146at the last. This is evidence weight,
not probability. Smooth guide coverage rises from16.7956%to21.9631%, but its
near-mirror cap still allows no plate-neighbor exchange. All32smooth plate arrays
are byte-identical to uncapped and prior latest-ray results, SHA256
b27aef6ca1508c32e6f45b7e24d9110c9644802f57fa173a44f98b6faf874484.
Do not extend that plate-only identity claim to raw/emitter arrays.

Actual draw95images and separate32-frame means were inspected with one identical
fixed display mapping. Speckles remain obvious. Temporal distance history makes
the guide usable more often and gives a modest measured improvement; it does not
solve the denoising problem. Reference remains one bounce; live source includes
prior fast-history feedback. No new quality/performance/production acceptance.

Frozen Rendering SHA256:
347de2fc91aa276dc48a137324e0af911fa6fec7df34dd989ae4bf20cca29fef.
Unchanged tool SHA256:
275fd58cc7ca2cf36c0e32585fe51cc2df426c82c3587d71d2c64fd5256eaf20.
All owned build/test/render/report/presentation commands ended and were explicitly
RELEASED2026-11-06at15:29:59EST. No process or later reservation remains.
Ticket stays open for integration; the broader reference/noise goal is not complete.
Evidence: live-reference-comparison/reflection-distance-history-recovery-20261106-1520/README.md.

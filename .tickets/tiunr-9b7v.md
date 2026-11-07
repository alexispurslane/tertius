---
id: tiunr-9b7v
status: in_progress
deps: [ter-gayt]
links: []
created: 2026-11-05T16:19:55Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-icn3
---
# Research Unity-guided diffuse and reflected-light noise reconstruction

User-requested direct discussion in an isolated worktree. Investigate bright-ray suppression, the source of hit radiance and indirect feedback, and temporal-then-spatial filtering. Record confirmed Unity behavior, failed Design B evidence, hypotheses, discriminating experiments, FSR/dynamic-resolution boundaries, and user decisions. No implementation algorithm is selected by this ticket.

## Design

docs/plans/unity-guided-indirect-light-research.md

## Acceptance Criteria

Deliver a source-pinned research program and a readable comparison with Unity. Separate speckles, spatial mottling, temporal fluctuation, brightness loss, and motion lag. Present evidence and genuine design forks to the user. No quality acceptance, FSR gate, production change, heavy experiment, merge or closure is implied by this handoff.

## Research notes

November 5, 2026: verified the bound path, branch, starting HEAD, and all three
prepared edits. Initial source reading confirms that Unity's temporal-history
writes precede their following spatial filters and are distinct from scene-color
feedback. Baseline fast indirect history remains the ray-hit source. Proposed
next source question: whether Unity's diffuse filter distinguishes a lighting
boundary on one flat surface from noise. See research log section 11 in the
design document. No new user decision, implementation, heavy work, or quality
acceptance. U8 and the complete scene-color component audit remain open.

November 5, 2026: completed the user-authorized source inspection of U8 and its
diffuse caller, reduced-work gather, and radius helper. Added pinned U8-U10
sources and checksums to the external source cache. Findings and their limits
are recorded in the new research-log entry. The component audit remains open.
No numerical experiment, production change, capacity request, or quality
acceptance follows from this checkpoint.

November 5, 2026: the user authorized a throwaway Unity-modeled diffuse spatial
filter after temporal accumulation and a few warmed real-renderer stills.
The user explicitly assigned ONE build/render allocation, freed by ter-6gf8.
Other allocations remain ter-s7c6, ter-rgg9 and ter-7tnv. Use one foreground
heavy command at a time, Nix and tools/gpu-lock.sh; queue behind FSR normally.
No ray-count, brightness-limit or hit-source changes, full suites, coverage,
mutations, commits, integration, children, workflows or quality acceptance.
The prototype retains filtered slow diffuse history; fast history and ray-hit
inputs stay unchanged. Allocation must be released after builds/captures.

November 5, 2026: prototype and four warmed stills are ready for user review.
The Release build passed with zero warnings/errors. Deck and reflection
control/candidate pairs used matched settings and advancing snapshot clocks.
All ended at 65 renderer draws, final history draw index 64. Both selected
controls are byte-identical to the preserved baseline. All four PNGs were
inspected. Many fine speckles disappear; broad floor patches and reflection
grain remain. Lighting gradients also change. No motion, brightness-loss,
Linux or controlled-cost acceptance is claimed. No automated tests ran.
Evidence: /private/tmp/tertius-unity-noise-research-20261105/prototype/manifest.md.
The ONE build/render allocation is RELEASED. Captures ended by 12:40:41 EST,
and the parent was notified immediately afterward. No further heavy work is
assigned. No commits, imports, integration, children, workflows, closure or
quality acceptance. The prototype stays uncommitted and disabled by default.

November 5, 2026: the user strongly preferred the prototype stills and requested
continued prototype work before unrelated fixes. Proposed next experiment:
spatially filtered short diffuse history. Two meanings remain a real design
choice: separately filter the short estimate (recommended), or replace it
with the already-filtered long estimate (less GPU work, different response).
No choice or engine change is implied yet. Existing whole-frame GPU means
show +0.503153 ms (+3.065%) on deck and +1.006307 ms (+9.743%) in the reflection
test, from one 33-capture pair per scene on M1 Max at native 1280 by 720.
These are provisional, non-clock-controlled observations, not isolated pass
costs or accepted performance. The prior allocation remains released. A new
bounded build/render allocation is needed for the next experiment.

November 5, 2026, 12:51 EST: NEW one-allocation reservation received for the
bounded history-filter follow-up. This is not reuse of the released allocation.
The four-allocation ledger is FSR, corrected environment, parent portable-walk
hardening/integration and this research reservation. Start no heavy command
until the user chooses the short-history behavior. After the choice, no further
parent capacity release is needed. Use one foreground heavy command, Nix and
the normal GPU-lock queue. Check disk before each heavy command and hold/report
below 5 GB free. Reported free disk is 7.4 GB. Keep artifacts bounded and preserve
the long-only prototype/control. No broader changes, suites, hardening or
integration. Explicitly release the NEW allocation after the bounded captures.
Current status: reserved, awaiting the design choice; no heavy work started.

November 5, 2026: the user correctly pointed out the short history's additional
role in constraining long history. Confirmed current-frame 3-by-3 short-history
means/spreads guide the diffuse mean-plus/minus-two-standard-deviation clamp
after long history exceeds four. A large clamp correction also reduces long
history's effective weight. Previous-frame short history separately supplies
ray-hit light. Surface validity rejects incompatible previous taps, rather
than short-history color alone deciding to discard history. Filtering short
history can therefore change both feedback and long-history control. Recorded
the same-frame versus later-frame ordering distinction in the research log.
No engine change or heavy work; allocation remains reserved, design pending.

November 5, 2026: inspected pinned Unity U2/U5/U6 to answer the user's temporal
acceptance question. HDRP SSGI uses geometry/global/exposure validity and a
sample-count blend, not a short-light guide clamping a long-light estimate.
Accepted history reaches 93% old / 7% current once its stored count reaches
eight. SSGI disables the optional motion-only rejection flags, but motion
vectors still locate prior history and geometry rejection stays active.
Optional second temporal/spatial processing uses a separate history of the
first spatial output. Each history is copied before its following spatial
filter, and both temporal stages use the same weighting rule. Recorded exact
source ranges and scope limits in the research log. This does not choose our
short-history behavior or ordering. No engine change or heavy work; the new
allocation stays reserved pending that choice.

November 5, 2026: the user chose a throwaway single-history diffuse experiment.
Retain the spatial output as the only diffuse history for temporal reuse,
composition and existing ray-hit reads. Use Unity-style surface/exposure
validity and temporal weighting, without the fast-guide clamp/shortening or
initial repair. Specular, brightness bounds, ray-budget settings and hit-light
composition stay unchanged. Per-tap geometry checks and geometric normals are
documented adaptations. Existing adaptive ray planning still reads history
count, so actual sample reuse is not guaranteed identical. The NEW allocation
is ACTIVE; disk measured 9.2 GB free. Preserve 0/1 controls, old artifacts and
old binaries. Build a separate bounded harness, then four warmed matched
stills. No suites, hardening, imports, commits, integration or acceptance.

November 5, 2026: single-history prototype and four warmed comparison stills
are ready for discussion. Release build passed, zero warnings/errors. All
four runs ended at 65 draws and index 64 with matched advancing clocks. Both
new long-only controls are byte-identical to prior selected stills. Inspected
all four PNGs. The new mode looks close to the previous smooth result; broad
deck patches and bright-strip reflection grain remain. No motion, response,
reference-quality or integration acceptance is claimed. Whole-frame timing
observations are recorded with the unclocked/sequential and adaptive-ray
limitations. Two diffuse dispatches replace four. One retained diffuse image
replaces three, while the sample and shared scratch remain. Evidence:
/private/tmp/tertius-unity-noise-research-20261105/single-history/manifest.md.
The NEW build/render allocation is RELEASED after captures ended by 13:28:27
EST; parent notified immediately. New artifacts total about 44 MB, free disk
about 9.2 GB. No further heavy work assigned. No suites, hardening, imports,
commits, integration, children, workflows, closure or production acceptance.

November 5, 2026: user requested the completed FSR/dynamic-resolution pipeline
in this research branch with our prototype edits reapplied. Import permission
supersedes the earlier no-prerequisite-import boundary, not prototype acceptance
or main writes. Verified research 34c28466e, main 7d9f6e673, FSR 9bbbea67c not
merged and fix stage running. Asked parent for the completed safe import SHA.
Preparing bounded tracked/untracked backups. No branch-only commits exist,
so prefer fast-forward plus reapplying the saved uncommitted work, not an
extra prototype commit. FSR requires the shared jitter-aware historyPlace
helper for the single-history temporal lookup and new capture CLI flags.
No heavy allocation held; source work only until a new assignment.

November 5, 2026, parent confirmation at 13:40 EST: FSR workflow is still in
its existing fix stage; main/origin-main remain
7d9f6e6739b8fcd1b96fcd307c6434e3d4a8ebaf. No safe completed import SHA exists.
Do not import 9bbbea67c or dirty fixer state. Parent will provide the immutable
handoff. A NEW one-of-four build/render allocation is RESERVED for the later
bounded prototype comparison after that handoff, not active for unfinished
FSR. Keep Nix, normal GPU lock, capacity/disk checks and the 5 GB stop rule.
Verified all ten working-file checksums and seven archived untracked files
in the bounded fsr-rebase-backup. Branch remains 34c28466e. Only lightweight
compatibility notes and archives change. The separate ter-7tnv semantic-frame
box replacement does not receive noise work. No import, build or render yet.

November 5, 2026, 14:05 EST: user requested a short moving-camera video of
the single-history prototype before FSR. Parent explicitly REPURPOSED the
existing one-of-four reservation for this render and encode. No second
reservation remains for a later FSR comparison. Reuse the already-built
pre-FSR harness, without source behavior edits or builds. Queue normally
through tools/gpu-lock.sh in Nix and check disk before each heavy phase and
after the GPU wait. Hold below 5 GB free. Planned bounded evidence: 64
advancing warm-up frames, then 600 advancing frames at 60 playback frames
per second from follow-camera. Keep native 1280 by 720, fixed exposure
+1.57 stops and existing deck image settings. Preserve the lossless source,
one playback copy, three start/middle/end frame pairs and small logs. Verify
encoded duration/frame count, inspect frames, then release this allocation.
Playback speed is not live performance evidence. No noise acceptance or
unfinished FSR import is authorized. Evidence folder:
/private/tmp/tertius-unity-noise-research-20261105/single-history-motion/.

November 5, 2026: bounded pre-FSR video completed by 14:09:50 EST. Reused
the existing binaries without source behavior edits or builds. Normal GPU
queue wait was 23 seconds. ffprobe verifies 664 lossless source frames and
600 playback frames, exactly 10 seconds at 60 playback frames per second.
Removed 64 advancing warm-up frames from the playback copy. Camera positions
and rotations change between dumps. Final snapshot clock 17.7166666667 s,
696 renderer draws and final history index 695 agree with 664 captures plus
32 hidden initial warm-up draws. All exposure records remain +1.57 stops.
Opened source and playback PNGs at start, middle and end. The end is severely
washed out in both copies. This is not MP4 damage or automatic exposure,
but its renderer cause remains unproven without matched motion controls.
Lossy playback also softens fine grain, so the lossless source is retained.
Evidence totals about 369 MiB, playback about 5.3 MiB, disk about 7.3 GiB free.
The REPURPOSED allocation is RELEASED and parent notified. No concurrent
later-FSR reservation remains. No imports, fixes, commits or acceptance.
Exact provenance and inspection limits are in single-history-motion/manifest.md.

November 5, 2026, 14:16 EST: parent explicitly approved immutable FSR import
96db37df965c7b35dae499a2f34410d2c3b35011 after workflow completion and cleanup.
This is a completed comparison checkpoint, not hardened or merged main.
Parent assigned a FRESH one-of-four allocation for a bounded migration build
and matched warmed views. Earlier video allocation remains released. Refresh
the full tracked patch/untracked archive/checksums, including video notes,
then fast-forward this existing branch if ancestry permits and reapply edits.
Do not read dirty fixer state, import box work, move another branch, commit
the prototype or write main. Reconcile only FSR jitter/active-size interfaces.
Keep history policy, exposure, ray settings and hit-light source unchanged.
Do not fix or retune the pre-FSR washout. Use Nix, normal GPU queue, one
foreground heavy command and the 5 GB disk stop. No full suites or hardening.
Release this FRESH allocation after bounded captures finish.

November 5, 2026: refreshed all ten working-file checksums and seven untracked
files in fsr-import-96db37df9-backup, including latest video/allocation notes.
Saved patch, archive and preservation stash. Verified descendant ancestry,
fast-forwarded only this existing research branch to exact 96db37df9, then
reapplied every dirty edit successfully. No prototype commit or branch switch.
TemporalSignal and six of seven untracked files initially remained byte-identical.
ARCHITECTURE merged upstream text. The single-history temporal shader then
replaced its previous-place formula with the shared jitter-aware historyPlace
helper. Existing spatial projection, previous-size bounds and active dispatch
already use the new shared interfaces. No filter/history threshold changed.
Planned bounded captures: two existing scenes, selected single-history mode,
native versus fixed 2/3 FSR, all output 1280 by 720 with matched clocks and
exposure. Old binaries/stills/video stay untouched as pre-FSR controls.

November 5, 2026: the bounded migration comparison is complete. A Release
build retry with separate compiler/no node reuse passed with zero warnings
and errors. The first build ended 1 without a diagnostic, with no source
fix between attempts. Four captures ended by 14:38:20 EST. All have 65
renderer draws, final history index 64 and 33 advancing captured frames.
Native renders/output 1280 by 720. Fixed FSR renders 848 by 480 and outputs
1280 by 720, sharpening off. Final clocks match: deck 7.2 s, reflections
0.7166666667 s. Exposure records stay exactly +1.57/-6.8 stops. Opened all
four full-resolution PNGs. FSR looks softer and shows fewer fine speckles.
Broad floor patches, residual reflection grain and some fine floor pattern
remain. No motion, live-controller, brightness/detail or noise acceptance.
The pre-FSR washout is unresolved and was not modified. The normal GPU waits
were 620/0/0/4 seconds, without interruption or bypass. Old binary hashes
still match. New evidence is about 78 MiB and disk about 7.5 GiB free.
The FRESH allocation is RELEASED and parent notified after all commands
finished. No further heavy reservation, suite, hardening, commit or import.
Exact settings, clocks, limitations and migrated dirty archives are under
/private/tmp/tertius-unity-noise-research-20261105/fsr-comparison/manifest.md.
Branch remains exact 96db37df9 with all optional prototypes uncommitted.

November 5, 2026: user corrected the comparison target to the intended
default setup: native 1920 by 1080 versus fixed FSR from 1280 by 720 to
1920 by 1080. The prior comparison instead output 1280 by 720, with FSR
starting at 848 by 480. Its softer appearance describes that lower-input
configuration, not an inherent FSR quality penalty or the intended default.
Preserve the prior evidence and label its scope, rather than replacing it.
Requested a FRESH parent allocation for four corrected warmed stills using
the existing built harness, same two scenes and pinned 96db37df9. No build
or source behavior change is needed. Prior allocation remains RELEASED.
Prepare the corrected capture script only until the new allocation arrives.
Keep exposure, prototype/history policy, ray settings and hit source fixed.
No washout fix, new import, hardening or acceptance follows from this request.

November 5, 2026, 14:46 EST: parent assigned a FRESH one-of-four allocation
for the four corrected warmed stills. This replaces no released allocation
and adds no concurrent second reservation here. Existing harness only, no
build. At 14:48 EST disk is 7.45 GiB free, memory 54% free and load
3.32/6.05/5.92. Keep exact 96db37df9, all prototype settings and prior
evidence. Capture native 1920 by 1080 versus actual internal 1280 by 720
FSR with 1920 by 1080 output. Use Nix and the normal GPU queue. Check disk
before each run and after the queue. Hold below 5 GiB. Release this fresh
allocation after all render processes finish, including on failure.

November 5, 2026, 14:52 EST: three corrected captures finished. The script
then stopped with exit 75 before FSR reflections because disk fell below
5 GiB (4,701,872 KiB free). No render process remains. This allocation is
RELEASED and parent notified. Disk recovered to 7,569,868 KiB without any
cleanup here. Requested a fresh allocation for only the missing FSR
reflections still. No further heavy command is authorized until assignment.
Verified deck dimensions are native 1920 by 1080 and FSR internal 1280 by
720, both output 1920 by 1080. Native reflections also output 1920 by 1080.
All three have 65 draws, final index 64 and 33 constant exposure records.
Deck clocks match at 7.2 s. Native reflections clock is 0.7166666666666667 s.
Opened each completed original PNG. The deck FSR still shows less fine
grain but broad floor patches and a faint repeating floor pattern remain.
These observations do not isolate FSR or establish motion/detail acceptance.

November 5, 2026, 14:53:30 EST: parent assigned ONE FRESH allocation for
only the missing FSR reflections still. Parent reports 7,584,348 KiB free,
load 2.62/4.61/5.36 and memory 53% free. No build or completed-capture rerun.
Keep the original partial exit 75. Use the same harness/settings, Nix,
normal GPU queue and 5 GiB disk stop before and after queue. Release as
soon as this single capture and all owned heavy processes end, even on failure.

November 5, 2026: the missing FSR reflections capture finished successfully
at 15:06:57 EST after 648 seconds in the normal GPU queue. The post-queue
disk guard passed with 7,617,384 KiB free. All owned heavy processes ended.
The FRESH single-still allocation is RELEASED and parent notified at 15:07
EST. No heavy allocation remains here. Free disk is 7,612,288 KiB at 15:07:49.
All four corrected originals now exist. Metadata confirms native internal
1920 by 1080 versus FSR internal 1280 by 720, all output 1920 by 1080.
Every run has 65 draws, last index 64 and 33 constant exposure records.
Both scene clocks match their control exactly. Opened all four PNGs.
FSR reflections show fewer isolated bright speckles around the light strip
and sphere. Some reflection grain and streaks remain. No motion, detail,
brightness or noise acceptance follows. Source checksums match before/after.
The original partial exit 75 is retained alongside the successful follow-up.
Complete provenance, four-original comparison HTML, final dirty patch,
seven-file untracked archive and checksums are in
/private/tmp/tertius-unity-noise-research-20261105/fsr-comparison-1080p/.
No build, behavior change, completed-capture rerun, suite, hardening, import,
commit, main write, child or workflow occurred for this correction.

November 5, 2026: user says image quality under FSR looks great and asks
for the next step against grain, especially bright spots. The user sees
the prototype as close to later integration after FSR. This is positive
still-image feedback, not merge authorization or motion/hardening acceptance.
Discussion only. No heavy allocation is held and no behavior changed.
Source inspection confirms the new single-history spatial path is diffuse
only. Reflections still use the existing path. The diffuse filter weights
geometry and distance, not brightness outliers. Finite bright estimates can
enter the temporal blend. Existing radiance/weight caps protect numerical
range and do not establish visual outlier control. The visible dots are
not yet attributed to a specific light component or proven to be outliers.
Proposed next experiment: first identify diffuse versus reflected contributions,
then test input brightness suppression before temporal accumulation without
changing ray counts, hit source or the selected one-history policy.
Two designs offered: a simple fixed brightness ceiling as a reversible
diagnostic, or a same-surface neighbor-relative soft limiter. No threshold,
exposure convention or design is selected yet. Both can suppress genuine
light, so noise reduction alone is not success. Keep real bright reflected
features and scene brightness visible in a matched comparison. The earlier
motion washout remains a separate unresolved pre-merge issue. These later
discussion notes postdate the preserved corrected-capture archive.

November 5, 2026, 15:47:30 EST: user selected removable Design B, the
same-surface neighbor-relative soft limiter before temporal accumulation.
Parent assigned ONE FRESH one-of-four allocation for focused red-green
checks, one bounded build path, four warmed FSR off/on stills, and three
paired timing rounds per scene. Earlier allocations remain released.
Timing has at most 60 advancing warm-up draws and 120 measured draws per
run; no per-frame images. Target new evidence below 300 MiB; stop expansion
above 500 MiB and heavy work below 5 GiB free disk. Nix and the normal GPU
queue remain required. No heavy command has started. At 15:52 EST HEAD is
still exact 96db37df965c7b35dae499a2f34410d2c3b35011, disk 7,510,172 KiB
free, memory 53% free, load 4.66/4.88/4.84. All prior edits are preserved.

Source/component inspection identifies a real scope choice, not a settled
threshold. TemporalSignal selects single-history only for diffuse light.
Reflections use the original temporal path. The reflections scene includes
a fully metallic strip, whose diffuse material factor is zero; a diffuse
limiter cannot directly limit its reflected estimates. The completed image
alone does not attribute each bright dot to a light component. Ask whether
this prototype limits diffuse only or both diffuse and reflected estimates.
Do not change reflection-history policy. Allocation remains reserved while
this choice is pending; no implementation, build, test or render has begun.

November 5, 2026: user resolved scope by requesting a prototype with a toggle
for applying Design B to reflections. Implement one off/diffuse/both research
switch; both modes use the same input limiter, while reflection-history rules
remain unchanged. The bounded four stills and twelve timing runs compare off
with both enabled, exposing the largest cost and highlight-loss risk. Diffuse
only remains selectable and gets focused behavior checks, not an additional
capture/timing matrix. Use the existing assigned allocation; no extra slot.
Parent supplied the existing gpu-clock executable and verified its SHA256
c6cf6be39df144197ababaaccc87a9851f7e8d2b6c797438b03dbe7c3dc28734.
Copy only that executable into owned evidence; use this checkout's wrapper.
At 15:58 EST exact HEAD and dirty state remain preserved; free disk is
7,493,984 KiB. No implementation or heavy command has started yet.

November 5, 2026, 16:13:16 EST: all four warmed still captures and all twelve
capped timing runs finished successfully. All owned build/test/render/sampler
processes have ended. The ONE FRESH Design B allocation is RELEASED. No
further heavy work is assigned here. Eight focused GPU behavior checks pass
after four expected red failures; the first compile-only style failure is
preserved separately and is not counted as red. The bounded Release harness
build passed with zero warnings/errors. Both newly rendered off stills are
byte-identical to the preserved corrected FSR controls. Four originals have
been opened. Reflection speckles are reduced, but the sphere's shadowed side
also darkens. Detailed clock/timing analysis and preservation remain light
follow-up only. No acceptance, commit, import or integration is implied.

November 5, 2026: Design B analysis is complete. The removable switch is
TERTIUS_NEIGHBOR_LIMIT_PROTOTYPE=off|diffuse|both; unset is off. The four
stills compare off with both enabled at actual 1280x720 -> 1920x1080 FSR.
Both off controls are byte-identical to preserved originals. Bright dots
and sphere-reflection streaks reduce, but a fixed rectangle on the sphere's
shadowed side loses 36.51% of mean final display gray. This is not linear
HDR energy or proof that all removed light was noise. Deck changes little.
Timing is INCONCLUSIVE: five of six pairs fail the existing 3% mean-clock
match. The one matched reflection pair is 10.887 -> 11.771 ms, +0.884 ms
(+8.12%), near 1034 MHz and with broad per-frame spread. This does not
establish negligible overhead or isolated limiter cost. All twelve capped
runs have exactly 60 advancing warm-up +120 measured captures, no saved
images, unique measured indices92..211 and matched advancing clocks. The
32 hidden first-snapshot draws are separate. No extra run was added.
Source/component scope, initial limiter settings, all six pairs and limits
are recorded in the research document. Evidence/provenance is in
/private/tmp/tertius-unity-noise-research-20261105/neighbor-limit/ (about46MiB).
The allocation remains RELEASED. No new quality, motion or production-cost
acceptance; no further heavy work, import, commit or integration authorized.

November 5, 2026: parent coordination reports FSR/dynamic resolution merged
and pushed at immutable23d603077ac306722cd1c4e55f8459480fc8b1e8. This is a
future safe migration point only if the user requests it with separate
capacity. No migration, build, render or retest follows from this notice.
This worktree stays at96db37df9 with its limiter evidence preserved. The
parent's independent merged-production video does not include noise work.

November 5, 2026: user raises broad mottling/swirl concern on the reflective
plate despite positive overall feedback. Source inspection confirms six
flat, untextured strips, not one uniform mirror: gray roughness0 (floored
to0.045),0.1,0.2,0.3,0.45, then gold metal roughness0.1. No authored swirl
texture or wavy normal map exists. New single-history spatial filtering
acts directly on diffuse light only; reflections retain their older
temporal/spatial repair and clamp. The both-mode input limiter and filtered
diffuse feedback into later ray-hit lighting are separate dependencies.
Opened existing originals again. Uneven shading is present in both latest
limiter modes, but both include the diffuse spatial prototype. This does
not isolate the source of the swirls or prove that every gradient is wrong.
Record this as an unresolved visual-quality concern. Component-separated,
matched evidence is a proposed next diagnostic, not a running experiment.
Only source/image reading and tracking updates occurred. No heavy work,
source behavior change, migration or acceptance. Allocation stays RELEASED.

November 5, 2026,16:54EST: user requests a matching reflections still with
the existing neighbor limiter on for diffuse and off for reflections
(TERTIUS_NEIGHBOR_LIMIT_PROTOTYPE=diffuse). Single-history filtering and
all capture settings stay unchanged. Requested ONE fresh bounded capture
allocation from the parent; earlier allocations remain released. Existing
Release harness and all12 retained source/binary/script hashes match.
No build, timing, behavior change or render started during preparation.

November 5, 2026: parent explicitly ASSIGNS ONE fresh bounded allocation
from released ter-570o capacity for only the diffuse-only limiter still.
Use existing harness, Nix and normal GPU queue, disk>=5GiB before/after
queue and before capture. Release directly to parent as soon as render
processes finish, even on guard/failure. No build or swirl fix authorized.

November 5, 2026,16:56:32EST: the single diffuse-only limiter still finishes
exit0, and its ONE allocation is immediately RELEASED directly to parent.
All owned render processes end. Actual1280x720 ->1920x1080 FSR, exposure-6.8,
sharpening0,65 draws/index64, clock43/60s, single-history, limiter=diffuse.
Exposure JSON matches prior both control exactly. Both originals opened.
Reflection speckles around the bar return; the sphere's shadowed side and
broad plate mottling remain. This does not isolate the swirl cause. New
original and full provenance: neighbor-limit/stills/reflections-diffuse/
and neighbor-limit/diffuse-only.md in the existing evidence root. Prior
controls/binaries remain unchanged. Disk after capture6,790,768KiB. No
build, timing experiment, behavior edit, import or acceptance occurred.

November 5, 2026: user selects all current prototype components on and
requests simplification before another smudge-removal experiment. Parent
assigns ONE fresh bounded cleanup allocation: focused checks, one Release
harness rebuild, at most one matched warmed reflections still. Full dirty
patch/untracked archive preserved in all-on-cleanup/before with hashes.
Old8 checks pass. New one-control tests are behaviorally red3/8; the first
green attempt exposes an overly tight absolute test bound, corrected to
the old relative-reduction criterion without changing the limiter policy.
Active prototype now uses TERTIUS_INDIRECT_LIGHT_PROTOTYPE=1; unset/0 is
the original control. Superseded long-only pipelines and component-mode
choices are removed, with old binaries/evidence preserved. No new capping
algorithm, smudge fix, history/source/ray/exposure policy or acceptance.

November 5, 2026,17:13:24EST: all-on cleanup heavy work ends and its ONE
allocation is immediately RELEASED directly to parent. Final focused8/8
pass, one Release harness build0warnings/errors, one warmed still is
byte-identical to preserved both-enabled original. Actual1280x720 ->
1920x1080 FSR,65 draws/index64, clock43/60s, exposure-6.8, sharpening0.
Old binaries retain hashes; exposure records also match. No owned heavy
process remains; disk6,456,456KiB free. Provenance/all archives and new
original are in all-on-cleanup under the research evidence root.

Next design discussion identifies a confirmed weakness: the largest-neighbor
reference lets bright neighbors protect one another. It does not prove the
cause of all remaining smudges. Offer exactly two designs: median current-
neighbor evidence, or that evidence with support from valid retained bright
history. The first risks thin real highlights; the second can preserve old
bad patches and delays new unsupported highlights. Both retain the selected
single-history/both-caps model and avoid extra reflection-color averaging.
No next algorithm, reference statistic or policy is selected or implemented.

November 5, 2026,17:33EST: user selects a median-only neighbor-reference
experiment, with conventional averaging of the two middle values for even
support. No history support or other selected-prototype policy changes.
Full pre-edit tracked/untracked archive and max-reference DLL/image hashes
are preserved under median-reference/before in the research evidence root.
Fresh ONE allocation requested for focused red/green, one separate harness
Release build and at most one matched warmed reflections still. No current
allocation held and no heavy command started. Performance remains unproven.

November 5, 2026,17:33EST: parent explicitly assigns ONE FRESH bounded
allocation for this median-only experiment. Focused GPU red/green, ONE
separate-copy existing-harness Release rebuild, AT MOST ONE matched warmed
reflections still. No timing/suites/imports/commits/children/workflows.
Use Nix/normal GPU queue and5GiB guards before/after waits/between phases.
Release directly to parent immediately after final owned command exits,
including blocked/failure. All-on policy and max-reference controls stay.

November 5, 2026,17:37:22EST: median experiment heavy work ends and ONE
fresh allocation is immediately RELEASED directly to parent. Proper
behavioral red7fail/8pass, focused green15/15, ONE separate harness Release
build0warnings/errors, ONE warmed matched reflections still exit0.
1280x720 ->1920x1080 FSR,65 draws/index64, clock43/60s, exposure-6.8,
sharpening0. Exposure records match prior control exactly. Old max-reference
binary/image hashes stay unchanged. All5GiB guards pass. No owned heavy
process remains. Evidence and full dirty archives: median-reference/
under the existing research root, about42MiB. No timing/suite/import/commit.

Both originals inspected: the reflected lamp's speckled fringe and small
bright patches diminish, but the box's right face and sphere's shadowed
side also darken. A broad dark rear-plate patch remains. No claim that all
lost brightness is noise, no motion/controller/energy validation and no
production acceptance. Median-only reference change retains all other
selected policies. Performance stays unmeasured/INCONCLUSIVE.

November 5, 2026: user requests research while shopping into a small
edge-detection kernel and an ideal same-roughness light-spread comparison.
Concern: median reduces swirls but can erode real thin light, while grain
and patches make reflections look falsely rough. No new allocation held
or requested, no implementation/build/test/render. All10 median capture
input checksums still pass. Research source snapshots/checksums are under
edge-reference-research in the evidence root.

Source confirms bar over roughness0.2, mirror floor0.045, smooth reflection
cap4 with continued25% new-sample weight, original reflection repair/clamp
still active, and median guidance cannot identify reflected-object edges
on one flat receiver. Its curve removes25% at incoming brightness8times
neighbor median, regardless of whether that light is genuine. This is a
curve calculation, not a rendered reference-error measurement.

Two unselected edge designs recorded: A small reflected-light contrast
pass after limiting, cheap but unable to certify/restore removed light;
B reflected-feature-aware preservation before limiting/reconstruction,
more guidance work and false-support risk. Common recommendation is an
independent emitter/plane reference using the exact material model and
pixel footprint before judging either. Metrics separate edge position,
transition width, light deficit/surplus, outer light and temporal grain.
Existing display PNGs cannot provide the physical light-error numbers.
Primary CAS/NRD/SVGF/PBRT references, source-context/cost comparisons and
future bounded-check plan are in the research document. No new production
policy or algorithm selected. Prior allocation remains released.

November 5, 2026, 19:01 EST: user selects the independent controlled
emitter/flat-plate reference renderer as the basis for further work.
Parent assigns ONE fresh bounded allocation: focused CPU red-green checks,
one standalone tool build, small linear-HDR outputs and convergence
profiles, target <=128 MiB. No live captures/timing/suites/production
changes/imports/commits/children. All phases use Nix and normal GPU queue,
one foreground phase, >=5 GiB guards. Before-edit full dirty archive and
control hashes saved under independent-reference/before in the evidence
root. Existing single-history/median prototype stays unchanged.

Independent reference tool implemented; final focused Release checks14/14
and standalone Release build0warnings/0errors. Genuine behavioral reds:
7/9 before core integration,5/14 before image/error output. No full suites.
Rendering friend metadata and test reference only; prototype inputs and
old binaries/images remain checksum-identical. Separate executable,
PFM levels/means, PNG previews and profiles retained in
independent-reference/run-001; source/binary/DFG hashes recorded.

Bounded80x160 crop at original1280x720 pixel footprint, roughness0.045
and0.2, two shifted replicates,128/512/2048 pairs. Fine-level image
changes0.2102%/0.5305%; final replicate differences0.1663%/0.4106%.
Fixed-point area-only cross-check agrees to0.005327% at0.2 but differs
13.693% at0.045. Narrow-peak area undersampling is plausible, not proven;
smooth absolute local accuracy stays provisional. No ground-truth/energy
equivalence approval from appearance or replicate agreement. Both PNGs
inspected; all metrics/limits in the research document.

Numerical run ended19:16:49 EST with>=5GiB guard passing; evidence~14MiB.
All owned commands ended, ONE allocation explicitly RELEASED directly to
parent before inspection/notes. No allocation retained. No production
captures/timing, suites, imports, commits, child/workflow or acceptance.

November 5, 2026: user authorizes resolving the narrow-reference
cross-check disagreement. Fresh bounded point-validation allocation
requested from parent; no old allocation retained. Full before-edit
patch/archive saved under reference-point-validation/before; all original
independent-reference binaries still match hashes. Investigation will
separate uniform-area undersampling from a mixed-estimator defect,
without tuning the material or changing live prototype behavior. Only
light source reading and test/scaffold edits until explicit assignment.

20:01 EST parent explicitly assigns NO fresh allocation because free disk
is only49,784KiB above the mandatory5GiB guard and falling. No queue,
build, test or numerical run started; no slot held. Focused checks are
written but unexecuted, with a compilable adapter to the original
uniform-area probe, not a completed correction. The old independent
reference binaries and every original output still match their hashes.

Read-only footprint inspection: tracked baseline source67.48MiB may need
a new Nix source snapshot, already larger than available guard margin.
No new dependencies planned. Point-only evidence budget reduced to1MiB;
no image sweep or full duplicate dependency directory. Conservative
incremental request128MiB for source snapshot/build temporaries/compact
outputs was reported to parent, not treated as a hard bound or allocation.
Await explicit fresh assignment; do not relax guard, retry/queue, delete
evidence, or call either numerical estimate ground truth.

November 5, 2026, 20:48 EST: parent assigns ONE fresh bounded allocation
after user-approved cache cleanup. Local 20:51 check:17,745,504KiB free,
memory53%, load2.05/2.21/2.59. The previous disk hold remains historical;
no earlier reservation is retained. Resume only the written focused CPU
checks, minimal independent-reference-tool correction and point-only
convergence checks. New JSON/CSV/log evidence target<=1MiB, no images,
live changes/captures/timing, suites, imports, commits or child work.
Nix and unmodified GPU queue, one foreground phase, >=5GiB guards before
queue, after acquisition and between phases. Release immediately when all
owned commands finish. Original reference/results and live controls stay
preserved; neither estimator is assumed to be ground truth.

November5,2026,21:01:37EST: point-only numerical command exits0;
all owned heavy processes have ended. ONE fresh20:48 allocation is
explicitly RELEASED directly to parent immediately after process exit.
End free17,708,116KiB. All5GiB guards pass; no future slot retained.

Core behavioral red2failed/1passed reproduced the13.7% mismatch and
analytic diffuse integration error; face integration green3/3. Point-mode
behavioral red1failed/3passed, final focused Release18/18. Two separate
style-compilation failures were fixed, not counted as behavioral reds.
No suites/coverage/mutations. Tool and focused test compilation only;
the compiled Release tool runs directly with --points, not image mode.

Independent adaptive3/5 Gauss-Legendre integration over visible emitter
faces does not use either sampler's samples/densities/weights. Material,
DFG and face geometry stay shared and unchanged in meaning. Loose/medium/
strict relative tolerances1e-3/1e-5/1e-7 all meet their estimated criteria
within300000 evaluations. Smooth final626.3260669158196W/(m²sr),159290
evaluations;0.2 final519.6646780823439W/(m²sr),37842 evaluations. Last
tolerance changes0.00001121%/0.000001110%; estimates are not rigorous bounds.

At the original smooth point/shift811,65536 uniform-area samples enter
the narrow peak core only6times;1048576 samples enter70times. Across
3shifts, highest-count uniform-area results range-0.5597% to+4.8645%,
still not converged. Mixed results agree with face integration within
0.000319% smooth and0.002030% rough at1048576 pairs across3shifts. This
resolves the original discrepancy as insufficient uniform-area sampling,
not a demonstrated defect in the original mixed estimator. Original
seed311 mixed and seed811 uniform-area65536 values reproduce exactly.

Evidence reference-point-validation/points-001 has only JSON/CSV(~13KiB);
logs and changed small tool DLL/PDB retained, no duplicate dependencies.
All10 median capture-input hashes, original reference binaries and all
original reference outputs verify. Old images/control binaries untouched;
no material/live behavior changes, captures/timing/imports/commits/children.
Point agreement does not measure denoiser error or establish full-image,
pixel-footprint, ground-truth, energy-equivalence or production acceptance.

November 5, 2026, 21:13 EST: ONE fresh allocation assigned for the matched
live/reference comparison. Pixel-center compatibility refinement is approved
within this SAME allocation. Four native 1280x720 configurations: roughness
0.045/0.2 times original OFF/selected median single-history ON. At most 64
advancing warm-up and 32 measured draws each. Read-only tool probe applies
the existing specular material factor once; recorded pre-exposure is undone.
New decoded-input center references use 128/512/2048 pairs, two fixed shifts.
Original area-average results stay unchanged. Focused red-green checks and
one retained Release harness/tool build; new evidence <=128MiB. Nix, normal
GPU queue, one foreground phase, >=5GiB guards. Release after owned commands.

November 5, 2026, 21:43 EST: explicitly RELEASED the 21:13 allocation
directly to parent. All owned commands exited; no reservation remains.
Three measurement behavior checks fail for intended reasons then pass;
18 existing reference checks pass. Earlier compilation errors are not reds.
Separate-output retention build failed before compilation on analyzer paths;
the successful focused-build tool and dependencies were copied unchanged.
Capture stopped before completing first warm-up draw: FrameContext's helper
accepts at most THREE targets, whereas the read-only tool probe requested
eight. No measured draws, live images or reference-comparison results exist.
Failure log and empty partial output preserved; ~13MiB new evidence. Need
fresh explicit permission for corrected tool compilation and bounded run.
Lightweight correction/test preparation only while no allocation is held.

November 5, 2026, 21:45 EST: NEW separate ONE allocation assigned for the
tool-only 3/3/2 correction and the four still-uncompleted configurations.
Existing helper and live policies unchanged. Three probe draws read ONE
completed renderer frame, without advancing histories between probe draws.
Saved failed capture is integration red. One corrected normal-route Release
tool compilation; copy successful outputs, no retention rebuild. Same
64+32 renderer-frame/configuration cap, center levels and <=128MiB cumulative
evidence, Nix/normal queue/5GiB guards. Release when owned commands end.

November 5, 2026, 21:51 EST: explicitly RELEASED the 21:45 allocation
after corrected normal-route tool compilation succeeded but regression TEST
compilation failed (CS8122: pattern matching inside an assertion expression
tree). No captures started; no measured configurations exist. Saved log
corrected-build-checks.log is not green. Test-only predicate is corrected
lightly; no further command or tool rebuild started. Fresh assignment is
required for tests-only compilation and the unchanged bounded comparison.

November 5, 2026, 21:54 EST: NEW separate ONE allocation assigned for
tests-only compilation with BuildProjectReferences=false and no restore,
then the four still-uncompleted configurations and fixed center-reference
budget. Corrected tool must NOT be compiled again. Its successful outputs
are separately copied/hashes in corrected-bin (not the failed retained-bin).
Hash tool/dependency inputs before and after. Same bounds and guards;
stop and explicitly release on ANY further blocker, no automatic retry.

November 5, 2026, 21:59 EST: explicitly RELEASED the 21:54 allocation
directly to parent after the owned capture process exited134. Tests-only
compilation used BuildProjectReferences=false and no restore; comparison
checks passed4/4 and reference checks passed18/18. Corrected tool sources,
retained binaries and built dependency hashes matched before/after tests
and again after the failed capture. No tool compilation or change occurred.

The smooth-OFF case completed64 advancing warm-up draws and reached its
first measured draw(index64, tick65, snapshot time65/60 seconds). The
decoded-input validation then threw "Probe output mapping does not match
the actual achromatic plate inputs." Only one unvalidated50KiB scalar
crop is retained; ZERO configurations completed, no mean images or center
references exist, and the other three configurations never started. The
compound check does not identify which decoded input failed. Do not infer
light loss, comparison agreement, or a production defect from this failure.

Failure log: live-reference-comparison/logs/captures-corrected.log.
Focused green log: live-reference-comparison/logs/tests-only.log.
Exact frozen tool SHA256:
e3c2507a38a87008175cc5ceb16d144c0277c401266939423e92a0a18af73ec9.
Retained production Rendering DLL SHA256:
e212019b3f5578621ecdb0c4314efd675ac76504d14446ed8b7b84f795fac304.
Hash checks are in hashes-after-tests.txt and
hashes-after-capture-failure.txt; failed artifacts are in attempt-2154.sha256.
End disk36,410,888KiB free; cumulative evidence about25.4MiB. No owned
heavy process or future reservation remains. No retry, additional numerical
run, image, production edit, or tool correction is authorized by that slot.

November 5, 2026, lightweight source-only diagnosis after release:
the fixed crop includes camera rays that meet the visible emitter before
the plate. The emitter's lower part projects into the upper part of the
crop. This is an inference from fixed camera/box geometry, not a recovered
GPU failure pixel. The live fixture explicitly draws the emitter and has
no camera-only visibility exclusion. A correct emitter export would have
positive emission, authored roughness1, reflectance0, black base color and
a non-plate front-face normal/position. Several plate-only predicates
would reject it without any channel-mapping error.

Exact runtime failing predicate/pixel is NOT recoverable: firstInputs is
read, then validated, then written. The exception prevents the write of
decoded-inputs.f32. The only partial retained file holds specular red
radiance, not emission/material/geometry/source-history channels. Do not
name emitter overlap as the proven first or only runtime cause.

The original area-average reference already tests primary visibility:
ReferenceImage checks whether the emitter intersects the camera ray before
the plate and skips the plate contribution when blocked. Its zero there
means no visible plate contribution, not the emitter's outgoing specular
or emission. The new center-reference path instead requires every crop
receiver to be plate. Receiver comparability therefore needs an explicit
decision before another comparison; no crop/visibility/mask was changed.

Smallest future tool-only diagnostic: retain all16 probe scalars per pixel,
the independent crop distance and source-history values, frame/exposure/
camera/material metadata BEFORE validation; report each failing predicate
with pixel coordinates, actual value and expected value/tolerance BEFORE
throwing. Use the first existing renderer draw for geometry/export preflight,
without extra camera/history advances. Keep history-dependent checks
separate from geometry preflight. No preflight, numerical program, queue,
build/test/render, tool behavior edit or allocation occurred in this turn.

November 5, 2026,22:08 EST: NEW separate ONE bounded allocation assigned
for the settled visible-plate contribution correction. Keep original crop,
camera and fixture. Reuse reference primary visibility: emitter-occluded
plate contribution is zero on both sides, while raw emitter light and
decoded inputs remain separately retained. Require geometry classification
and OFF/ON consistency; do not use brightness to classify or hide errors.
Focused behavioral red-green, one final corrected Release tool build,
conditional four64+32frame configurations and original center budget only.
Persist complete diagnostic crops BEFORE throwing; geometry preflight on
first existing renderer draw. Same Nix/normal queue/5GiB guards and128MiB
cumulative evidence cap. Stop/release on any unexpected blocker. All
earlier allocations remain released; no later reservation is assigned.

November 5,2026,22:17 EST: explicitly RELEASED the22:08 allocation
directly to parent after the baseline tool compilation exited1. Parent
had approved an explicit exception for TWO Release tool compilations:
baseline scaffold for assertion reds, then final corrected tool, both
BuildProjectReferences=false/no restore with existing Release dependencies.
Only the first compilation was attempted. It failed on errors introduced
in the scaffold: TS0002 unnamed calls with more than three arguments at
ComparisonMeasurement.cs47 and Program.Live.cs73/182, plus IDE0060 unused
camera parameter at ReferenceComparison.cs24. No assertion red occurred;
tests were not compiled/run, final tool was not built, and no preflight,
capture or center integration started. ZERO validated configurations.

The new test source describes visible plate versus direct emitter,
retained raw emitter light versus zero plate contribution, invalid-input
diagnostics and shared original primary visibility. These are NOT verified
tests yet. The baseline extraction still has all-plate behavior and late
input retention; it is NOT the completed measurement correction. No
automatic compiler fix/retry or implementation followed the failed build.

Baseline source manifest and failure log are retained under
live-reference-comparison/baseline-2208 and logs/baseline-2208.log. All10
recorded production/dependency binaries, old corrected retained binaries,
and prior failed logs/partial scalar hashes still match after failure.
End disk36,402,924KiB free, cumulative evidence about25.7MiB before final
checkpoint. All owned processes ended and normal GPU lock released. No
future heavy allocation/reservation is held.

November 5,2026,22:22 EST: NEW separate ONE allocation assigned. Parent
made lightweight named-argument/unused-parameter corrections in four
scaffold/test files and widened only the synthetic1x1pixel occluder test
to cover its full pixel area. Original live fixture and assertions are
unchanged. Record these corrected input hashes separately; preserve the
failed22:08 baseline archives. Allow at most two Release tool compiles
in this allocation: corrected baseline assertion-red stage, then final
visibility/diagnostic tool and focused checks. Existing Release references,
BuildProjectReferences=false/no restore; same capture/reference limits,
5GiB guard and128MiB cumulative evidence. Stop/release on unexpected error.

November 5,2026,22:33 EST: explicitly RELEASED the22:22 allocation
directly to parent. Both allowed tool compilations were attempted. The
baseline tool and affected tests compiled using existing Release refs,
BuildProjectReferences=false/no restore. Focused comparison tests produced
FOUR intended behavior failures and FIVE passes: direct emitter rejected
by old all-plate validation, invalid-input diagnostics lacking named pixel
checks, unknown-material diagnostics lacking named checks, and center
reference0.640290377 versus original occluded plate contribution0. This
is real assertion/behavior red, not the earlier compiler failures.

Baseline tool SHA256:
8c3a0d7140d6ee6f7864b6a7bee6a08e4fe23086ebda5705118c7f515d67f6b5.
Baseline small assemblies/source/archive/log are in baseline-2222 and
logs/baseline-2222.log. Source now shares ReferenceCamera.PrimaryHit
between original area reference, center reference and live accounting;
keeps raw/plate/emitter contributions separate; retains all16 probe fields,
independent distance/history crops and metadata before named validation;
and uses the first existing renderer draw for geometry preflight. These
changes are NOT green or validated yet.

The final allowed tool compile failed on ONE new style error introduced
in Program.Live.cs57: TS0011 nested conditional choosing the diagnostic
folder. No corrected final binary exists. The normal bin still contains
the RED BASELINE tool, not the final source implementation; do not treat
it as a capture-ready artifact. Final test compilation/checks, preflight,
captures and center integration never started. ZERO validated comparison
configurations. No automatic compiler fix/retry followed the failed build.

Final source identities and failed log are in final-2222/source.sha256 and
logs/final-2222-build-checks.log. All10 recorded production/dependency
binaries, old corrected retained binaries and retained baseline assemblies
still match after failure. End disk36,404,532KiB free; cumulative evidence
about26.2MiB before final checkpoint. All owned processes ended; GPU lock
and allocation are released. No later allocation/reservation is held.

November 5,2026,22:35 EST: ONE fresh bounded allocation assigned for the
single corrected FINAL Release tool build and separately compiled focused
green checks, using existing Release references only, no restore and
BuildProjectReferences=false. The successful red baseline is frozen and
must not be rebuilt/rerun. Parent's source-only diagnostic-folder style
correction has Program.Live.cs SHA256
58d9a45f661f2ae410cfeaf87a303f4331ebfdfd11f67d628f30b161d58bd73c.
The failed final-2222 source/log and baseline binary remain separate.

Conditional scope remains first-draw geometry/visibility preflight, then
the original FOUR64+32frame configurations and original bounded center
references. Keep exact original crop/fixture and separate raw/plate/emitter
values. Save all probe/distance/history inputs before validation. Any
unexpected blocker means stop and release without retry. Nix/normal GPU
queue, one foreground phase,5GiB guard and128MiB cumulative evidence cap
remain. ZERO validated configurations at assignment; no later slot held.

November 5,2026,22:43 EST: explicitly RELEASED the22:35 allocation
directly to parent after every owned build/test/render command ended.
The ONE corrected FINAL Release tool build and separate affected-test
build succeeded, zero warnings/errors. All9 comparison and18 reference
focused checks passed. No baseline rebuild/rerun or dependency build ran.
Frozen final tool SHA256 is
18300a9807dde39cafbbd3670d38cb43f5501b574e4de3cd4bb46441468577bc;
Rendering DLL remains
e212019b3f5578621ecdb0c4314efd675ac76504d14446ed8b7b84f795fac304.

The first already-budgeted smooth-off renderer draw FAILED geometry
preflight on primary-position at pixel(572,240). Actual decoded world
position(-0.8999811410903931,0.22300291061401367,-1.9965004920959473)m
versus analytic(-0.8999999761581421,0.22294541864116946,-1.996666212554242)m,
tolerance0.0001m. The saved validation file lists26 primary-position
failures and no other failure predicate. Analytic classification recorded
12,486plate pixels,314emitter pixels,0unrecognized pixels; this does not
make the failed geometry validation green. No exact cause is established.

Preflight retained ALL16fields(819,200bytes), independent distance
(51,200bytes), source history(204,800bytes), classification(12,800bytes),
fixture/exposure/frame metadata and all named failures BEFORE throwing.
Draw0,Tick1,Time1/60s,DrawnFrames1,DrawIndex0,Resettrue; native internal
and output1280x720. No remaining warm-up draws or measured frames ran.
All three measured output streams are empty. ZERO completed/validated
configurations; no center reference or live/reference comparison exists.

Evidence is in final-2235 and live-2235/smooth-off/preflight; logs are
logs/final-2235-build-checks.log and logs/captures-2235.log(exit134).
Old failed logs/binaries/area references remain separate. Frozen final
source/binary, all10 production/dependency, earlier corrected tool and
red-baseline hashes still match after stopping. Probe source remains
ab188876aab9ac7c9890afed0e6a43c75adbc7e38ca1fe1e81c152c8e3c256c3.
End free36,123,800KiB; cumulative evidence28,540KiB before final notes.
No threshold relaxation, production edit, numerical retry or later slot.

November 6,2026,02:20 EST: ONE NEW bounded DIAGNOSTIC allocation assigned.
All previous allocations remain released. Reuse position-diagnosis-2235:
fixed nearest1/256pixel projected-vertex rounding reproduces all28+X
and286+Z emitter distances within1.244/0.858micrometers respectively.
Actual device/camera/geometry numeric inputs were not recorded; this
support is not a certified error bound. The0.0001m check remains unchanged.

At most TWO Release tool compilations including any red API scaffold;
separate focused CPU-test compilation, existing refs, no restore and
BuildProjectReferences=false. Then at most ONE OFF original-fixture draw
to retain actual device limits, prepared camera/projection/viewport/jitter,
model rows and the same box geometry supplied to renderer. No warm-up,
measured frames, images or radiance integration. Save before validation.
New evidence<=8MiB, cumulative<=128MiB. Nix/normalGPUqueue/oneforeground
phase/5GiBguards. Unexpected build/test/runtime blocker means stop/release.
No validator correction, tolerance change or later reservation authorized.

November 6,2026,02:34 EST: explicitly RELEASED the02:20 diagnostic
allocation directly to parent after all owned commands and GPU lock ended.
Reviewed the parent's saved1/256pixel model rather than restarting it.
The first tool/API-scaffold and separate affected-test compiles succeeded.
TWO new metadata-retention tests failed on actual missing saved JSON data,
both after valid/invalid geometry paths reached the real retention method.
These are meaningful assertion reds, not placeholder/compiler failures.
Baseline tool SHA256:
47949eb0edd217a7960676cacf7086c806d07601f3e0bff42b98a2882182e115.

Final diagnostic source adds an optional retained raster-metadata payload,
read-only actual device/prepared camera/shader matrices/viewport/instance
rows, and the same box MeshData passed into renderer creation and probe.
Float components widen exactly to double for JSON. Derived transformed
vertices are explicitly CPU calculations, not GPU vertex-output readback.
The new --raster-metadata CLI is capped at ONE OFF draw and returns before
the measured-frame/image path if validation passes. The unchanged geometry
validator may still fail; raw data/metadata are saved before it runs.
No production, fixture, threshold or lighting policy changed.

The second/final allowed tool compile FAILED on my introduced TS0002 at
Program.cs19. The old three-positional-argument --live call now targets a
four-parameter method because diagnosticOnly is optional. This analyzer
uses METHOD parameter count, not supplied argument count. No final binary,
test green, actual-device draw, metadata confirmation or numerical analysis
ran. Normal tool bin remains the red baseline, NOT diagnostic-ready.
ZERO validated lighting configurations; no radiance integration performed.

Failed source/log/baseline identities are retained separately in
live-reference-comparison/raster-metadata-20261106-0220. Failed Program.cs
SHA25653138a0421bd41c141439662bab2489b4758b6f96ed9885f66ad85679bf5aa21.
All10 production refs, frozen2235 tool, original failure and baseline hashes
match after stopping. New evidence452KiB before these notes, below8MiB.
No source cleanup or automatic retry followed the failure. Parent then
announced an explicit source-only named-argument correction to that call;
its future hash must be recorded separately, not substituted into failed
evidence. This notice authorizes no new heavy work or reservation.

November 6,2026,02:35 EST: ONE FRESH bounded recovery DIAGNOSTIC allocation
assigned. All earlier allocations remain released. Parent corrected only
the --live CaptureLive named arguments; Program.cs now hashes
2ea97c368784444fbb464ef45185c48d02c3f846518d1f7f41402de0bbec501b.
The failed source/logs and two meaningful metadata assertion reds stay
unchanged. No baseline rebuild or repeated red is permitted.

Allow ONE corrected final Release tool compile, separate affected tests
with existing references/BuildProjectReferences=false/no restore, then
focused metadata/comparison/geometry checks without light integration.
Conditional on green and preserved hashes, freeze the exact tool and
record at most ONE OFF original1280x720 diagnostic draw. No warm-up,
measured lighting configurations, tolerance changes or second draw.
Use actual numeric raster inputs for bounded offline residual analysis.
Normal Nix/GPUqueue/oneforegroundphase/5GiBguards; diagnostic evidence
remains cumulatively<=8MiB and comparison evidence<=128MiB. Any new
unexpected blocker means stop/release. No later allocation is held.

November 6,2026,02:48 EST: explicitly RELEASED the02:35 recovery allocation
directly to parent after all owned commands/normalGPUlock ended. Only ONE
corrected Release tool compilation and one separate tests-only compile ran,
both zero warnings/errors, existing refs/no restore/no dependency builds.
The native xUnit help request printed usage and returned2 after those
compiles; this was not a test execution. Subsequent focused checks ran
without rebuilding:16passed,0failed,0skipped(2metadata,6accounting/probe,
8pure reference geometry/storage/profile checks). No lighting integration.

Frozen tool SHA256:
97446f6a461321515ade821467155122c1b694ae084bf3d499542147ba5801e7.
New small tool outputs were copied; dependency links point to preserved2235
outputs. The parent-only Program.cs correction is the sole tool-source
difference from the failed0220final. Existing source/control/10production
reference/frozen2235 hashes verify. Failed sources/logs/baselines unchanged.

Exactly ONE OFF/native1280x720/zerojitter diagnostic renderer draw saved
actual numeric metadata and all16probe fields+distance/history BEFORE the
same26primary-position failures. Draw0,Tick1,Time1/60s,DrawnFrames1,Resettrue;
exit134 preserved, no warm-up/measured frames/second draw. All12800distance
values match the independent readback and new distance bytes equal2235.
Actual AppleM1Max/MoltenVK reports SubPixelPrecisionBits8,ViewportSubPixelBits0,
SubPixelInterpolationOffsetBits4. Prepared matrices/camera/modelrows/source
geometry were retained; derived CPU vertices are not GPU vertex outputs.

The new actual-input offline model DOES NOT confirm the earlier ideal-camera
agreement. Using recorded Near-range matrix, camera-relative CPU vertices,
source triangle order and fixed nearest1/256pixel rounding covers all314
emitter pixels but gives max distance residual471.529micrometers for all28
+Xface pixels, versus unrounded244.226micrometers. All286+Zface pixels give
5.743micrometers rounded, versus11.344unrounded. The original ideal-camera
model's1.244/0.858micrometer maxima stay preserved separately. Eight reported
bits support the precision scale, not our exact rounding/projection model.
Actual shader clip outputs/float arithmetic/interpolation remain unproven.
No tolerance relaxation, masking, validator correction or renderer-defect
claim. ZERO validated lighting comparisons. No ray/material/history/source
change, integration sweep or future reservation. End free39,200,052KiB;
diagnostics cumulatively2292KiB and comparison31036KiB before final notes.
Evidence:live-reference-comparison/raster-metadata-20261106-0235.

November 6,2026, parent-only precision follow-up received after RELEASE.
No new allocation/reservation/build/test/draw/numerical run is authorized
or performed by this thread. Read and verified existing parent artifacts
in position-diagnosis-0235-parent; the0235negative artifacts still verify
unchanged against their original manifest. No old evidence is rewritten.

Parent's saved-input replay locates one projected emitter corner only
0.000002536913pixel below a nearest1/256pixel rounding midpoint when
recorded float matrix values are evaluated with double arithmetic. A
replay rounding each projection/viewport operation to float32 lands on
the midpoint. Python nearest-even ties then selects the other1/256step.
This explicit arithmetic hypothesis reproduces all28+Xdistance values
within1.242997micrometers and all286+Zwithin0.857659micrometers, without
pixel exclusions, per-pixel corrections or light fits. These are received
parent results, not a rerun or new renderer measurement by this thread.

The original double-model471.529micrometer side residual remains factual.
The two results expose sensitivity to a rounding boundary, not proof of
GPU multiply/add contraction, viewport arithmetic, tie handling or
interpolation. No certified error bound, revised tolerance, accepted
validator correction or renderer-defect verdict follows. Resolve geometry
measurement rather than tune a global allowance. Preflight remains failed;
ZERO of FOUR validated lighting configurations. All allocations RELEASED.
Parent script SHA256f349334b0b03adae64ded08182d09e0a74c02631ea81270889e9bc1e9bc10a96;
parent report SHA2567ce692069edf759bfacd48bc9a2786a7c7823d77667010c01618a3e1041a3bd6.

November 6,2026: user explicitly set the active goal, "work until the
reference is satisfactory". This is continuing research, not a completion
or production-quality verdict. All old heavy allocations remain RELEASED.
This turn has only read source/existing evidence and written tracking.
The existing goal is active; no duplicate goal or new reservation exists.

Working trust criteria are recorded in the research document: correct
receiver/visibility/component/units, independently checked light integration,
declared pixel footprint and convergence limits, then genuinely matched
control/prototype comparisons. No numerical acceptance threshold or
production policy is silently selected. The shared material equations
and DFG approximation remain outside independent validation.

Requested a fresh bounded geometry-measurement stage from parent, not a
later comparison reservation. Proposed tool-owned position/distance and
normal/material outputs plus owned depth, using the same mesh vertex
source, prepared scene, geometry helpers and original range/culling rules.
It would read the same completed renderer frame without writing any live
image or advancing snapshot/history. Its purpose is to compare measured
geometry against the live distance buffer rather than certify a guessed
rounding replay. Equality is an experimental requirement, not an assumed
property of a separately compiled diagnostic pass. Any mismatch is retained
and stops the bounded attempt; no fitted allowance follows.

Requested scope is meaningful focused CPU reds/greens, at most two Release
tool compilations including red scaffold, existing refs/no restore, then
at most one original OFF diagnostic draw with no warm-up/light integration.
Evidence<=8MiB new/128MiB total, Nix/normalGPUqueue/oneforeground/5GiBguards.
The request is NOT an assignment. No tool/test behavior implementation,
build/test/numerical work or renderer draw will start without fresh parent
authorization. The four lighting configurations remain uncompleted.

November 6,2026,05:16EST: parent explicitly ASSIGNED ONE fresh bounded
geometry-measurement allocation. All preceding allocations remain released.
This assignment permits two Release tool compilations (behavior-red scaffold,
then final), separate focused tests against existing Release references,
and conditionally ONE smooth-OFF native1280x720 zero-jitter diagnostic frame.
No warm-up, measured lighting cases, integration, policy change or retry.
The05:24local check found36,795,892KiB free,58%free memory. Mandatory disk
guard5,242,880KiB applies before/after queue and between phases/end.

Existing BarrierBatch.ImageLevels and mapped GpuBuffer readback support
same-command-sequence snapshots before/after the tool-owned audit, with
original exact ResourceState restoration. Original distance/specular slow
history/composition have TransferSrc usage. No host readback is needed
during command recording. Four owned RG32float color attachments can retain
positionXYZ/distance/normalXYZ/materialID; owned reversed-Z depth retains
the original range ordering. Existing ReadImage supports RG32float.
Unchanged mesh.vert will be embedded locally and required byte-identical
to the frozen Rendering assembly before any diagnostic draw.

Before-state full tracked patch/untracked source archive/checksums and
all10verified production inputs are retained in
live-reference-comparison/geometry-audit-20261106-0516/before.
Frozen0235tool97446f6a461321515ade821467155122c1b694ae084bf3d499542147ba5801e7
and2235tool18300a9807dde39cafbbd3670d38cb43f5501b574e4de3cd4bb46441468577bc
remain unchanged. Allocation ACTIVE; zero builds/tests/draws started.

05:31EST geometry-audit baseline: first permitted Release tool compilation
and separate Release tests-only compilation succeeded, no dependency build.
Seven new focused checks produced FOUR intended behavior assertion reds
(audit evidence missing, wrong audited distance accepted, unknown audited
material accepted, changed surrounding-copy bytes accepted) and THREEpasses
(plate/emitter accounting and exact embedded production mesh.vert bytes).
Baseline DLLefe553a2d37b77d9dabb46c12fd8a10e8915378e7bed14c0d33b5d512853f5e2
and source archive/logs are frozen in geometry-audit-20261106-0516/baseline.
All10production reference hashes still verify. One final tool compilation
remains; no renderer draw has occurred under this allocation.

05:39EST: explicitly RELEASED the ONE05:16geometry allocation directly to
parent after all owned commands ended. Both Release tool compilations and
separate test compiles completed. Baseline4behavior reds/3passes; final17/17
focused greens, zero errors/failures/skips. No dependency build or retry.
Final tool0974e57225ce7d4d18bbe7dc1a9eefb8ce797ca7b90608624eccf142424215e2.

ExactlyONEsmooth-OFF original renderer frame, native1280x720/zerojitter,
original80x160crop, draw0/tick1/time1/60/resettrue. Extra audit geometry:
3owned range-rendering instances and1indexed-indirect API call. Probe's
3draws remain inside the same frame. No warm-up or measured lighting.
All12,800audit scalar distances == independent R32 == probe EXACTLY.
Analytic/audited geometry+normal+material visibility agrees12,800/12,800:
12,486plate/314emitter, no unknown/excluded pixels. Byte-identical embedded
mesh.vert SPIR-V1bc05f4e23f4387f19bed9715fa962864e4f8c1c0646ba390f8a72f0bb1b18ba
is verified against frozen Rendering assembly before the audit.

Genuine surrounding raw GPU copies in the SAME command buffer match for
whole distance3,686,400bytes/history7,372,800/composition7,372,800. No host
read occurs during recording. Exact original tracked image states are
restored; four owned RG32color attachments plus own depth never alias live
targets; shader resources are readonly. All10production-reference hashes,
all10median capture/control-input hashes and tool runtime dependency hashes
verify before/after. New evidence4,240KiB before lightweight sealing.
End disk36,591,388KiB, guard5,242,880KiB. No owned heavy process remains.

Original ideal-position failures remain26at original0.0001m and are saved
as negative diagnostics. Generic stdout "first-draw preflight passed"
means only the new direct geometry measurement, NOT original analytic
validation or light acceptance. Worst interpolated-vs-shading34.1605um
at572,258; interpolated-vs-ideal246.4036um at572,254. Original reconstructed
shading receiver remains unchanged; no widened global allowance or fitted
offset. See geometry-audit-20261106-0516/README.md for every field/unit,
identities, per-pixel residuals, exact outcomes and remaining limits.
ZERO OF FOUR lighting cases. User goal remains active, not satisfactory
reference/production acceptance. All allocations now RELEASED; none held
for later work. Next measurement correction/comparison needs fresh scope
and capacity; no automatic continuation into lighting is authorized here.

## 2026-11-06 05:55 fresh audited live/reference allocation

ONE new bounded allocation explicitly assigned after geometry allocation release.
Pinned HEAD96db37df965c7b35dae499a2f34410d2c3b35011, all prior evidence preserved.
Namespace live-reference-comparison/audited-live-20261106-0555 contains full
dirty patch, untracked archive, source identities and verified geometry seal,
10 production references and 10 median/control inputs. Initial disk36,381,776KiB.
At most TWO Release tool builds, existing references only/no restore;
meaningful measured-light/source-compatibility reds then focused greens.
Conditional FOUR native zero-jitter configurations, exactly64warm+32measured
draws each, same80x160crop/fixture/exposure. Center-reference budget only after
all four validate. Evidence target64MiB, cumulative128MiB. Mandatory5GiB guard,
Nix/normal GPU queue/one foreground phase. No later reservation. Allocation
ACTIVE at this receipt; no heavy phase has started and ZERO OF FOUR complete.

## 2026-11-06 06:08 audited comparison completed; allocation RELEASED

TWO successful Release tool compilations total, existingrefs/no restore and
separate affected-test compilations only. Baseline11checks:10intended assertion
reds/1pass. Final29/29green,0errors/skips. Frozen final tool
b394beba494b5d63a15b4fc7809045bcd93c47d06af983cf46b8d94f9e535e2c.
All FOUR configurations validated on one attempt each: smoothOFF/ON,roughOFF/ON,
native1280x720zerojitter/original80x160crop. Exactly64warm+32measured draws each,
initialresetonly. All1152 genuine whole-image surrounding-copy hash pairs equal.
12,800exact distance/surface matches per assessed frame,12,486plate/314emitter,
zero unknown/excluded. Historical26analytic-position failures remain diagnostics
at unchanged0.0001m, not analytic passes. Measured material-factor-once/history/
previous-exposure checks now run through accepted audit measurement, including
emitter pixels with old position residuals. OFF/ONsource+receiver fields match.

Approved center128/512/2048pairs x2fixedshifts x2materials completed, no extra
rounds. Reference512vs2048relativeL1 .172573%smooth/.474224%rough; final shifts
.133329%/.359877%. These are observed differences, not error bounds. Smooth
OFF/reference signedcrop+4.472413%,L1=7.103088%; ON/reference+2.277541%,L1=6.170770%.
RoughOFF/reference+5.410265%,L1=16.846191%; ON/reference-24.428007%,L1=28.236199%.
ON/OFFsignedcrop -2.100911%smooth/-28.306799%rough. Sums are pixel radiances,
not physical energy. Local edge disagreements and temporal drift remain large;
whole-pipeline/source-feedback mismatch is not isolated limiter error.

All10productionrefs/10mediancontrols,35runtimefiles,finalsources and previous
89-file geometry seal verify after. Every level/replicate/linearPFM/profile,
32-frame raw/plate/emitter series, metadata and failures are retained. New own
CSVresiduals preserved losslessly asgzip with original-byte hashes; no earlier
evidence touched. New62852KiB before sealing, cumulative<128MiB. Enddisk
36,113,448KiB. All owned commands ended; explicit RELEASE sent directly to
parent06:08:18EST. No later reservation. Six originals inspected and shown with
one common display mapping; profiles shown. Full receipt:
live-reference-comparison/audited-live-20261106-0555/README.md.

Goal remains ACTIVE. Proposed next trust step, NOT RUN/allocated: independent
emitter-face integration at a few fixed captured edge/shoulder points before
accepting local reference accuracy or selecting a live algorithm correction.

## 2026-11-06 06:15 fresh POINT-ONLY allocation

Parent assigned ONE fresh bounded point verification slot after acknowledged
06:08release. Prior four-case stage sealed322files, manifestSHA
8f1f737183fcb738ce1ad6c121acef61f6dc78deb1dd723bf2a3aa813b4a8628.
New namespace captured-points-20261106-0615 preserves incoming archives/hashes.
Exactly17preselected pixel locations xBOTHmaterials =34actualWARMEDdecoded
plate receivers. Only existing IntegrateEmitter:1e-5/1e-7relative,1e-9absolute,
300000evaluations each,max68quadratures. No captures/sampler reruns/image sweep.
Practical diagnostic criteria: convergedboth,drift<.01%retainedpeak,retainedmean
within1%peak; local relative errors also retained. No claim for untestedpixels.
At most2ReleaseTOOLbuilds with separatefocusedtests,existingrefs/no restore.
Nix/normalGPUqueue/oneforegroundphase,5GiBguards,2MiBnew/128MiBcumulative.
Stop/preserve/release on unexpectedblocker. No later reservation. Allocation
ACTIVE, no heavy work yet under this assignment.

## 2026-11-06 06:26 POINT-ONLY attempt stopped; allocation RELEASED

Baseline tool and affected-test Release compiles succeeded. Newfocusedchecks
10total:5meaningfulassertionreds/5passes,0errors/skips. BaselineDLL
d5e5f09ffcac6411ee9894d84ff634a7df70996f64f8885bf0d97fc0f0be497a retained.
Final permitted tool compilation FAILED with2CA1861findings only, at
Program.CapturedPoints.cs20:72and22:34:constant Materials/RelativeTolerances
array arguments in request serialization need staticreadonly storage.
No correction/retry. No final test compile/run, no finalgreen/frozenpointtool,
ZERO point commands/receivers/quadratures. BothTOOLcompileattempts spent.
Failed source/hash archive and all logs preserved. This is not a behavior red.

All four VALID prior configurations and center references stay EXACT, their
322file seal8f1f737183fcb738ce1ad6c121acef61f6dc78deb1dd723bf2a3aa813b4a8628
verifies before/after. Old89geometryseal/35priorruntime/10production/10controls
also verify. Pinned96db...unchanged. All owned commands ended; explicit RELEASE
sent directly to parent06:26:37EST. Disk35,972,764KiB,guardpassed;new912KiB
prereceipt,below2MiB. No later reservation. Only lightweight sealing follows.
Receipt:live-reference-comparison/captured-points-20261106-0615/README.md.
Goal remains ACTIVE; next correction/recovery requires separately bounded scope.

## 2026-11-06 06:33 recovery: point command stopped, allocation RELEASED

Parent's narrow source-only CA1861 correction arrived with exact
Program.CapturedPoints.cs SHA256
9b70f0ea03ab3959084419042ba2e9bed3c9032678b788188e2cc9bef713cb1b.
One fresh allocation allowed one final Release tool build, separate affected
tests and the unchanged fixed point command. Both compiles succeeded using
existing Release references, BuildProjectReferences=false and no restore.
All 30 focused pure checks passed, including ten new point checks. No baseline
was rerun. Frozen tool SHA256:
8ebabf9bf3dca47e4c45529f72b2cf6a1d01cc1db391ab592aa981be4cda14e2.

All 34 preselected inputs were retained before integration. Plate, +Y-normal
and source checks passed. One command evaluated two smooth receivers and four
quadratures, then STOPPED on its criterion. At (550,260), both converged in
12240/55080 evaluations; fine specular radiance is 0.002401013089848248 W/(m^2 sr),
and the saved mean differs by -0.0478623705% locally. At (570,260), the coarse
call converged in 291890 evaluations, but the fine call did NOT converge in
299914 of 300000 evaluations. Fine specular radiance is 1001.4481474232464;
the saved mean is 1001.0501708984375 W/(m^2 sr), a local difference of
-0.0397401030%. Absolute difference is 0.0385131005% of the image peak;
coarse/fine drift is 0.000003670305295% of the image peak. Those two numerical
criteria pass, but fine nonconvergence means the combined criterion FAILS.
The other 32 receivers, including all rough receivers, were NOT evaluated.
No extra quadrature, sampler run or capture occurred. Estimated errors are
NOT bounds. The quadrature's relative stopping scale uses the maximum
diffuse/specular component, not a specular-only guarantee. No whole-crop
accuracy claim follows.

All owned heavy commands ended at 06:38:34 EST. Explicit RELEASE was sent
immediately to the parent. Before/after verification passed for the 322-file
four-case stage, 89-file geometry stage, 42-file failed stage, ten production
references, ten controls, 35 prior runtime files and new frozen sources/runtime.
The old 26 analytic failures and four valid cases remain unchanged. New evidence
was 924 KiB, cumulative comparison 93872 KiB, before sealing; end disk was
35765268 KiB. Nix, normal GPU queue, one foreground phase and 5 GiB guards
were used throughout. No later allocation is held. Receipt:
live-reference-comparison/captured-points-recovery-20261106-0633/README.md.
The full goal remains open. Further evaluation needs a new bounded decision
and allocation.

## 2026-11-06 06:46 larger work budget: 34 point checks pass; RELEASED

Parent assigned a NEW bounded allocation and changed ONLY the experiment work
limit to 3000000 evaluations per existing quadrature. Incoming source SHA256
e9e3f75df9f668d0423804d79afa62e459d75be19b091fd50130a94d410f673a verified.
All tolerances, criteria, points, inputs, intended material and integration
methods remained unchanged. One Release tool and one separate test-project
compile succeeded using existing references, BPR=false/no restore. All 30 pure
focused checks passed. No baseline or expensive numerical tests were repeated.
Frozen tool SHA256:
3ffafdde12c8132e1369249e4b3b8644b4768a3e08ec8b2a2cca2d6df7307b0b.

ONE numerical command completed 34/34 receivers and 68/68 quadratures. ALL
converged and ALL 34 combined criteria passed. Actual evaluations: 5011532.
The 34 preselected input records are byte-identical to the prior stage. The
smooth material used 3670504 evaluations, rough 1341028. Largest mean error
relative to image peak: smooth 0.193020568% at (574,260), rough 0.273930637%
at (575,275), both below 1%. Largest drift relative to peak: 0.0000162951% and
0.0000163559%, below 0.01%. Worst LOCAL relative errors: smooth -6.004208913%
at dim (574,255), rough +1.672791331% at (550,320). These remain visible in
JSON/CSV/receipt; peak normalization does not hide them. No all-pixel bound.

The old false convergence at smooth (570,260), 299914/300000 evaluations, is
preserved. The new fine call converged at 1388458 evaluations, estimated error
0.00010011238205936286, radiance 1001.4480169645341 W/(m^2 sr). No flag was
overridden. Across the two captured-point commands: 72 calls, 5670656 actual
evaluations; old four calls and 659124 evaluations remain separate evidence.
Quadrature estimates are not bounds; relative stopping covers diffuse/specular
maximum together. One-bounce/live-source-feedback limits remain unchanged.

All required 322/89/42/58-file seals, ten production references, ten controls,
35 prior runtime files and new frozen identities verified before/after. Four
valid live cases remain exact; old 26 analytic failures stay failed diagnostics.
All owned heavy commands ended 06:49:42 EST; explicit RELEASE sent immediately.
No later allocation held. New evidence 1212 KiB, total comparison 96044 KiB,
disk 35668292 KiB before lightweight sealing. Nix/normal queue/one foreground
phase/5 GiB guards used throughout. Receipt:
live-reference-comparison/captured-points-budget-20261106-0646/README.md.
The agreed selected-point criteria pass; this is not universal reference,
physical energy, quality, performance or production acceptance.

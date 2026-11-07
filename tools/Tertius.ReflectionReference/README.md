# Controlled reflection reference

## Reflection-sharing prototype

The research-only `--live smooth sharing --out <new-directory>` and
`--live rough sharing --out <new-directory>` modes enable the selected prototype,
disable its incoming neighbor cap and enable `TERTIUS_REFLECTION_SHARING=1`.
This uses the existing uncapped control, including its single diffuse history.
The new sharing filter affects only final reflected-light reconstruction. It is
off by default and is not an accepted production policy.

The same outgoing-radiance helper runs in real composition and the read-only
comparison probe. The original3x3 and fixed5x5 versions remain frozen. The current
DesignB version uses the same5x5 search, at most1/48exchange per neighbor and maximum
total exchange of one half. Receiver materials must match exactly. Both shading
and geometric normals must agree within10degrees. Plane residuals use distance
times native pixel angle, rather than1%of camera distance. Every pixel cell
touched by the straight connection, including corner contacts, must agree with
both endpoints. A roughness-scaled reflection-direction guide also restricts
sharing. It writes no existing history or source image. Receiver guides do not
identify reflected objects, so this is not reflected-silhouette protection.
The existing history clamp remains unchanged. DesignB also requires separation
to fit both endpoints' projected footprints. Roughness sets a planar GGX-cone
proxy. Existing ray-hit distance and camera-forward receiver depth set its
projection. A separate near-mirror cap protects narrow reflections. This cap is
reconstruction policy, not a change to material roughness or reference light.

Composite binding4 retains the existing `Specular.Sample(0)` RGBA16F image.
Its alpha remains the latest traced ray-hit distance in meters. Shared2x2 samples
carry the donor-ray distance. At high roughness, the existing diffuse-ray handover
can supply distance. The two measured materials remain below that handover.

The follow-up temporal distance history adds binding5 and two signal-owned
RGBA16F images for specular sharing only. R is the distance first moment in
meters; G is dimensionless valid-hit support mass; B/A are zero. The guide follows
accepted light reprojection, temporal weights and existing short-history repair.
Misses add no support, rather than zero-meter hits. Representative distance is
R/G for positive support. Sharing strength uses the smaller endpoint value of
clamp(G*existing slow-history length,0,1). This is evidence weight, not probability
or a certified confidence. Negative, nonfinite and saturated65504m distances add
no support. Existing light RGB, raw hit alpha, rays, material and source stay
unchanged. Light anti-lag can shorten history, so the guide is still an estimate.
The research plan records the unchanged footprint formula/caps.

Sharing adds one probe-owned RG32image after the original3/3/2probe draws.
Its Rchannel stores shared outgoing specular radiance and its Gchannel stores
unfiltered outgoing specular radiance, both pre-exposed. The tool undoes recorded
pre-exposure once. The material factor is already present. Raw16-field inputs
remain intact. Separate raw, plate and emitter outputs use the unchanged audited
classification. Unfiltered frame arrays are retained for exact control checks.
Probe draws do not advance renderer simulation, camera or history.

`--sharing-metrics <audited-stage> <uncapped-stage> <sharing-stage> --out <new-directory>`
reuses the frozen reference basis and the existing metric/noise mechanism.
It reads retained32-frame arrays, including lossless gzip arrays. It creates
single-frame draw95and separate32-frame-mean previews with the same fixed
mapping. It performs no renderer capture or reference integration.
`--sharing-shaders <uncapped-rendering.dll> --out <new-directory>` records shader
byte identities and requires exactly the two composition and two generic
accumulation modules to differ. The first temporal-history capture admission
failed this guard: both diffuse-single-history variants also changed through the
shared accumulator include. All124focused checks passed, but no capture occurred.
That failure remains preserved. The recovery moves guide-only declarations to
`accumulate_reflection_guide.glsl`; both diffuse modules again match control bytes.
The unchanged four-module guard then passes. Both temporal-history captures are
complete. Rough noise improves modestly; the smooth plate output stays unchanged.
Do not treat the failed provenance guard as an accepted six-module change.

When reusing a frozen tool with a different Rendering assembly, retain its entry
DLL as a local byte-identical copy, not a symbolic link. A linked entry DLL can
select the previous directory as the .NET application base. The first recovery
admission loaded the old engine this way; its failure and loaded path are retained.
The corrected local entry uses the same tool bytes, with no tool rebuild. Check
`CurrentAssembly` and `CurrentSha256` in shader provenance before rendering.
Unchanged dependencies can remain read-only links.

Sharing captures retain raw sample RGBA and guide RGBA crops before validation.
`measured-reflection-hit-distance.f32` retains all32latest-hit alpha crops.
`measured-reflection-distance-guide.f32` retains all32guide RGBA crops, frame-major
80x160x4little-endian float32 values promoted from float16. No exposure conversion
applies to distance or support. Old captures and shader manifests stay immutable.

The first November6experiment completed both original-fixture materials. Temporal noise
RMS fell29.33%rough and25.88%smooth against uncapped. Persistent mean-error RMS
fell14.00%rough and6.84%smooth, while both profiles widened slightly. Smooth
missing and extra light increased slightly. Actual crops still show speckling.
All32unfiltered frames per material matched uncapped bytes exactly. The report
retains accuracy and noise separately, without a weighted score or quality gate.

Evidence: `live-reference-comparison/reflection-sharing-20261106-1006` under the
preserved research output root. `summary.json` is the compact result and
`comparison.html` shows actual crops/profiles. `report/metrics.json.gz` retains
the complete metric tree. The7CSVtables use immutable previous-table bases,
new-row XZdeltas and gzip row-order indices. `report/table-reconstruction.json`
names every base, byte hash and reconstruction rule. Positive index n selects
base line n-1; negative n selects delta line -n-1, retaining exact newlines.
All full reconstructed byte streams matched their original C#receipts. Old
controls/evidence were not removed. Capture/report wrappers retain their exit93
space-guard failures; only the later verification/presentation wrapper exited0.

The32frames are correlated, the live source includes history feedback, and the
reference is one bounce. Image-space radiance sums are not physical watts. This
experiment establishes neither production quality nor performance acceptance.

The wider November6experiment is retained separately at
`live-reference-comparison/reflection-wide-sharing-20261106-1135`. It reuses the
same CLI mode with a separately frozen binary and explicit5x5manifest metadata.
Both smooth/rough cases completed64warm-up plus32measured draws. Against the
frozen3x3 output, temporal noise RMS changed-15.85%rough and+12.44%smooth;
persistent mean-error RMS changed-1.03%/+2.29%. The wider rough profile moved
farther from the reference width. Smooth missing and extra light increased.
No radius, strength or threshold was retuned after inspecting these results.
`summary.json` and `comparison.html` retain uncapped/3x3/5x5 comparisons and actual
draw95crops. All7new tables remain lossless gzip with original plaintext hash
receipts. Existing reference levels/shifts and old metric objects reproduce
exactly. The experiment does not establish a universal preference for5x5.

The DesignB November6experiment is retained separately at
`live-reference-comparison/reflection-footprint-b-20261106-1405`. Both original
smooth/rough cases completed64warm-up plus32measured draws. All unfiltered frames
match uncapped controls. Smooth plate-contribution frames match uncapped bytes exactly:
the near-mirror footprint permits no exchange in this fixture. Rough temporal
noise RMS falls3.10%against uncapped but rises62.94%against fixed5x5. Rough
persistent mean-error RMS falls1.59%against uncapped but rises15.62%against fixed5x5.
This is not a solution to the remaining speckles.

Every sharing diagnostic phase now retains `reflection-hit-sample.f32`, all four
raw sample channels. `measured-reflection-hit-distance.f32` retains all32alpha
frames in frame-major crop order. Lossless gzip can store these arrays. Alpha
values are float16 meters promoted to float32. Known hit distance appears in
17.06%of rough plate samples and16.80%of smooth plate samples. These are guide
diagnostics, not new pixel exclusions. `summary.json` names the reused
`narrow`/`wide` report slots as fixed5x5/DesignB. `comparison.html` shows actual
draw95crops and separate32-frame means with one fixed display mapping. No new
reference integration or old-control recapture occurred.

The research-only `--raster-metadata --out <new-directory>` mode permits
exactly one native original-fixture renderer draw, with the whole prototype
OFF. It retains numeric device limits, prepared camera and shader matrices,
viewport/jitter, instance model rows, and the same source mesh supplied to
the renderer. Float inputs widen exactly to double for JSON storage. The
CPU-derived transformed vertices are not captured GPU vertex outputs.
All raw probe crops and metadata are saved before the unchanged geometry
validator runs. A failed validation remains failed; this mode neither
warms history nor produces a lighting comparison or preview image.

This CPU tool integrates one reflection from a known glowing box into a
flat plate. It is an offline measurement tool, not a denoiser. It does not
change the live renderer's lighting. Its original image and point modes run
only the independent CPU integration. A separate research capture mode reads
the live renderer for a matched comparison, as described below.

## Offline saved-data metrics

`--metrics <retained-stage> --out <new-directory>` reads the four audited
native captures and their saved center references. It does not draw a new
renderer frame, integrate light, change the material, or select new samples.
The retained stage must contain `live/{smooth,rough}-{off,on}`, `centers`,
and their original manifests. The command saves input hashes before it
validates the data and checks those bytes again before it returns.

The measured quantity is outgoing visible-plate specular radiance before
display mapping or FSR, in W/(m² sr). Capture already undid its recorded
pre-exposure; the report applies no additional gain. Every crop pixel stays
in full-crop totals. Directly visible emitter pixels contribute zero plate
reflection, as in the original reference, without changing the retained raw
emitter captures. OFF/ON source, receiver and visibility compatibility remains
mandatory.

One saved 2048-pair center-reference mean owns the measurement basis for
each material. Each horizontal scanline is primary. Column-mean and row-mean
profiles are explicitly aggregate diagnostics. Physical background is zero.
Each anchor profile's maximum defines fixed 10%, 50% and 90% levels for OFF,
ON and every saved reference candidate. The report never fits endpoints,
renormalizes candidate brightness or chooses the nearest crossing.

| Observable | Definition and limits |
| --- | --- |
| Missing / extra light | Positive per-pixel deficits and surpluses are summed separately. The old `ReferenceDifference` uses the same summation mechanism. These are image-space pixel-radiance sums, not physical watts. |
| Edge displacement | Candidate minus comparison-profile 50% crossing, in original pixels, using fixed anchor thresholds. Left-positive / right-negative displacement is inward for a single bright bar. |
| Transition width | Per-side distance between fixed 10% and 90% crossings, in pixels. |
| Outer-band light | Fractional pixel-cell overlap within anchor left10–left50 and right50–right10. Tails lie outside anchor10; the complementary core lies between anchor50. Bands never move with the candidate. |
| Shape-only width | Central 5–95% cumulative-light width with constant radiance inside each unit pixel cell. Each candidate uses its own cumulative distribution. Always crop-limited; it does not change radiance or loss bands. |
| Temporal variation | Sample standard deviation over the 32 retained correlated frames, first8/last8 drift, and persistent mean error reported separately. No independent-sample confidence interval or convergence claim. |
| Reference sensitivity | Saved128/512 means and final2048 shifts41/97 pass through the same fixed basis. Their metric ranges and crossing-status changes are descriptive sensitivity, not error bounds. |

All directed roots and exact-level plateau intervals remain in the tables.
An isolated exact-level sample counts once. Multiple roots, plateaus,
occlusion, no contrast, missing levels and an actually open crop boundary
have distinct reasons. No numeric edge is selected from ambiguous roots.
An undefined band affects only that band. Every band and edge aggregate
states valid/invalid row counts and reference-radiance coverage, so losing
a bright unscorable row cannot silently improve the result. Zero reference
light produces null normalized values with reasons, never an epsilon or a
fabricated zero error.

`metrics.json` is the readable summary. Lossless compressed CSV tables retain
per-row roots/status, fixed fractional band weights, temporal edge counts,
per-pixel temporal variation, frame-region totals and common-basis profiles.
Every compressed table has an uncompressed byte-count/SHA256 receipt.
`charts.html` uses shared radiance axes for reference/OFF/ON curves, with no
brightness fit. OFF/reference and ON/reference normalize by anchor light;
ON/OFF explicitly normalizes by OFF light in the same anchor bands.

This is whole-pipeline deviation: live screen-hit light includes fast-history
feedback, while the reference integrates one bounce. The report supplies no
weighted score, production acceptance threshold, isolated limiter attribution
or physical-energy equivalence claim. Original area-average references and
the historical failed analytic-position diagnostics remain separate.

## Isolated neighbor-cap experiment

`--live smooth uncapped --out <new-directory>` and the corresponding `rough`
case retain the selected single diffuse history while disabling only the
incoming neighbor cap for both light signals. The original `off` and `on`
modes keep their meanings. Every case records 64 warm-up draws and 32 measured
draws at the original native resolution and crop. No extra renderer frames
are needed to display retained single-frame values.

`--cap-shaders <original-rendering.dll> --out <new-directory>` records and
requires byte equality for every embedded engine SPIR-V shader. The host
specialization control is the only intended rendering change.

`--cap-metrics <original-audited-stage> <uncapped-stage> --out <new-directory>`
reuses the original 2048-center metric basis, reference candidates and the
four old OFF/ON cases. It compares each of the two new cases against the
reference, selected all-ON and original OFF. Noise is the sample standard
deviation across the same 32 correlated measured frames. Persistent mean
error and first-eight/last-eight drift remain separate. These are not
independent samples or a convergence claim.

Preview filenames distinguish `draw95`, an actual captured single frame
chosen before image inspection, from `mean`, the 32-frame average. All
previews use exposure -6.8, Reinhard mapping and sRGB, without image-specific
brightness fitting. They show the cropped outgoing visible-plate specular
contribution, not the full composed game image. Raw outgoing, emitter and
plate values remain separately retained. New diagnostic files may use
lossless gzip storage with original-byte hashes and verified round trips.

The first cap-isolation capture stage completed both cases, but its cumulative
retention guard failed before this offline report ran. Do not infer a noise
or reference-accuracy result from the capture completion alone. Evidence:
`live-reference-comparison/cap-isolation-20261106-0850/README.md`.

The user then approved a 160 MiB cumulative limit for offline completion.
The frozen tool completed one saved-data report with no new build, test,
renderer draw or light integration. Its separate presentation check verified
the exact decoded receiver/visibility bytes, old metric identity and all
seven compressed table receipts. The comparison includes actual draw95
previews and 32-frame means under the same display mapping.

For roughness 0.2, disabling the cap increased temporal noise RMS by 27.12%
and reduced persistent mean error RMS by 25.74%, relative to cap ON. Outer
missing light fell from 73.09% to 8.49% of fixed-band reference light. The
crop-limited central 90%-light width became 25.47 pixels, versus 16.78 with
the cap and 24.47 in the reference. It still has missing and extra light.
For roughness 0.045, disabling the cap increased noise by 12.91% and mean
error by 8.23%. This is not a blanket accuracy improvement.

The uncapped result nearly reproduces original whole-prototype OFF for this
specular fixture. Full-crop absolute differences, normalized by original OFF
light, are 0.001333% rough and 0.000252% smooth. This does not establish that
the selected diffuse-history change has no benefit for diffuse light or
other scenes. Live source-history feedback remains part of the intervention.
Evidence: `live-reference-comparison/cap-isolation-report-20261106-0928/`.
`comparison.html` has the images and noise/error chart; `summary.json` and
`report/metrics.json` retain the numerical observables and raw table paths.

## Boundary

The tool reuses `MaterialSample.ReflectedTerms`, `GgxLobe`, and the shared
half-precision DFG table from Rendering. The DFG table supplies the material's
energy compensation. The reference therefore measures the intended engine
material, including that approximation. It is not an independent validation
of the material equations or the DFG table.

Light integration is independent of the live noisy estimator. Visibility
comes from double-precision ray/box intersection, not screen tracing. The
tool reads no live hit color, environment map, ray-sharing record, limiter,
history, spatial filter, bloom, display image, or FSR output. There is no
ray-weight or radiance cap. Emission and the material response enter once.

The center-sampled comparison also reads the live renderer's decoded receiver
inputs. It still integrates the light independently. It does not take the
live renderer's light samples or history as reference light.

This first fixture measures specular reflection only. It does not reproduce
the full demo, its ambient light, its sun, its other objects, or multiple
reflections. Diffuse reflection is used in an analytic behavior test, but
is not included in the reference images.

## Fixture and pixels

All positions use meters relative to the plate top. The plate is the
infinite plane Y=0, with normal +Y. The glowing box spans X=-1.1 to -0.9,
Y=0 to 2, and Z=-2.1 to -1.9. Each outer face emits uniform RGB radiance
6800 W/(m² sr). It blocks camera rays but contributes no direct camera
color to these reflected-light-only images.

The camera eye is (0,3,6). Its target is (0,0,6-3 sqrt(3)). Its vertical
field of view is the engine default, `MathF.PI / 3f`. The virtual frame is
1280 by 720 pixels. The crop starts at (530,240) and covers 80 by 160 of
those pixels. It is not a low-resolution resize of the full frame. Each
pixel averages a uniform square footprint over one original pixel.

The plate has the strip's float-input RGB base color 0.06, metallic 0,
and reflectance 0.5. The cases request roughness 0 and float 0.2.
`GgxLobe` floors roughness 0 to 0.045 and sets alpha to roughness squared.
The whole plate uses one material per case. In the real demo, the bar
stands only over the strip of roughness 0.2. Moving the same material across
the entire reference plate removes strip boundaries as a confounding factor.

## Integration

Each sample pair has one uniform emitter-area sample and one sample of
the GGX microfacet-normal distribution. The area sampler includes every
box face in proportion to its area. Invisible faces contribute zero.
The GGX sample reflects the view direction about the sampled normal.
Directions below the plate or missing the emitter contribute zero.

The GGX direction density is D(h) (n dot h) / (4 (v dot h)), per steradian.
The visible emitter density is distance squared / (total box area times
the emitting face cosine), per steradian. The two contributions divide by
the sum of these densities. There is one sample from each method per pair,
so the sum of the two weighted contributions is averaged over pairs, not
over twice the number of pairs. This is the equal-count balance form of
multiple importance sampling, which combines samples from two densities.

Here D is the material's GGX normal distribution, h is the unit half vector,
n is the plate normal, and v is the direction from the plate to the eye.
The material response already includes the incoming-direction cosine.
No second receiver cosine or extra emitter cosine multiplies that response.
The area-to-direction density already includes the emitting face cosine.

The method suits a small offline fixture with known emissive geometry.
Emitter samples cover a small source even when a rough reflection rarely
hits it. GGX samples cover a narrow lobe even when area sampling rarely
lands near its peak. Both have fixed per-sample overhead; this is not a
proposed real-time replacement. Primary explanation:
https://pbr-book.org/4ed/Monte_Carlo_Integration/Improving_Efficiency

A five-dimensional Halton sequence uses bases 2,3,5,7,11, starting at 1.
The first two dimensions select the camera position within the pixel.
The last three select the light samples. Both light methods share that
camera sample. Each pixel and replicate uses a recorded deterministic
unit-torus shift. The two replicate seeds are 41 and 97.

## Outputs and convergence

Run through the project Nix shell and normal GPU queue. Use a fresh output
directory. The tool refuses an existing path to protect previous evidence.

```sh
nix develop --command bash tools/gpu-lock.sh <log> \
  dotnet run --project tools/Tertius.ReflectionReference -c Release -- \
  --out <new-output-directory>
```

The caller must apply the current capacity and free-disk guards. A queue
reservation does not exempt this CPU tool from the research allocation.
The tool performs one fixed bounded pass, without an automatic retry loop.

Nested levels use 128, 512, and 2048 light-sample pairs per pixel per
replicate. Both camera-footprint integration and light integration change
together. The tool retains each replicate and the two-replicate mean in
PFM format: little-endian float32 RGB, bottom row first. PFM values are
scene-linear radiance, without exposure or display mapping. The material
factor is already present; do not multiply it back a second time.

The PNG previews use exposure -6.8, Reinhard mapping, then sRGB encoding.
They are for inspection only, not energy measurement or a display match
to the live demo. They contain no bloom, FSR or sharpen.

`manifest.json` records the fixture, float-input material, roughness floor,
alpha, camera footprint, seeds, sample levels, DFG checksum, and observed
differences. A fixed surface point also compares the mixed method with
emitter-only sampling. Emitter-only sampling can be noisy for a very narrow
lobe. It is a diagnostic, not a second absolute truth.

The CSV profiles retain the mean radiance for each crop column and row.
They use original-frame pixel coordinates. The image-difference helper
reports missing and extra pixel radiance separately, normalized by the
unchanged reference region's sum. Equal missing and extra light do not
cancel. This is an image-space measure, not emitted or reflected watts.

Changes between nested levels and shifted replicates are convergence
evidence, not rigorous error bounds or confidence intervals. Two agreeing
images can share a missed feature. Analytic diffuse normalization, a
near-smooth broad-source limit, independent sampling agreement, exact
geometry tests, and pixel-footprint tests provide additional checks.
This first bounded run cannot certify arbitrary scenes or every roughness.
No production error tolerance, quality acceptance, or performance target
is selected by the tool.

Before judging the prototype, render this same controlled fixture through
the live renderer and read its reflected light before bloom/display/FSR.
Use the same material, camera and pixel footprint. The existing full-scene
display PNGs cannot quantify the limiter's light loss against this fixture.

## Point-only validation

Use `--points --out <new-output-directory>` to investigate the original
fixed-point cross-check without rendering images. This mode writes only
`points.json` and `points.csv`. It uses the same fixture and camera as the
image mode. The point is the plate intersection of camera pixel center
(570.5,320.5), not an average over that pixel. Material values stay unchanged.
Both roughness cases remain in the bounded check.

The original mixed sampler combines emitter-area and GGX-normal samples.
The original uniform-area sampler samples only emitter faces. Both remain
unchanged and available. The point check accumulates their nested sample
counts 65,536, 262,144 and 1,048,576 for shifts 311,811 and1451.

The independent face integral uses quadrature, a weighted sum of values
at fixed positions across each emitter face. It integrates the material
response times emitted radiance, emitting-face cosine and inverse distance
squared. The material response already includes the receiver cosine.
It uses neither sampler's direction density, sample positions or weights.
For the convex box, only outward faces that face the receiver are visible.
Face geometry is shared with the area sampler; the material response and
DFG table remain shared by every integration method.

Each rectangular panel compares a 3-by-3 with a 5-by-5 Gauss-Legendre
rule, costing34 material evaluations. The initial partition includes
quarter-face cuts and cuts around the perfect-mirror intersection. The
extra cuts use the existing roughness width to make the narrow peak
visible to the rules. They change only where the integral is evaluated,
not the material or emitted light. The panel with the largest estimated
error is split into four, costing136 more evaluations.

The estimated absolute error is the sum of panel differences. Each panel
uses the largest absolute channel difference across diffuse and specular
radiance. `Converged` means that this sum is no larger than absolute
tolerance plus relative tolerance times the largest integrated channel,
before the evaluation budget runs out. This is an estimated error, not a
rigorous bound. An incomplete face domain has a null error and cannot
claim convergence. A completed domain that exhausts subdivision work
retains its estimate but reports `Converged=false`.

The point mode uses relative tolerances1e-3,1e-5 and1e-7, absolute
tolerance1e-9 W/(m² sr), and at most300,000 evaluations per integral.
The CSV reports specular radiance in W/(m² sr); all RGB channels are equal
for this fixture. Differences use the finest face integral as a diagnostic
comparison, not an accepted ground truth. No threshold for the live
renderer or denoiser follows from these tolerances.

`AreaCoreHits` counts visible emitter samples whose light direction is
above the plate and whose half vector is within tan(angle)<=alpha of
the plate normal. The half vector bisects the light and view directions.
Alpha is roughness squared. The count's denominator is every area sample,
including samples on invisible faces. It diagnoses how often uniform
sampling visits the narrow reflection peak; it is not an error bound.

The November5 point investigation identifies insufficient uniform-area
sampling at the smooth point. At65,536 samples, shift811 visits that
peak core only6 times. At1,048,576 samples it visits70 times. The three
highest-count uniform-area estimates still span-0.560% to+4.864% from
the face integral, so they are not called converged. The highest-count
mixed estimates agree with the independent face integral within0.000319%
at roughness0.045 and0.002030% at roughness0.2 across these three shifts.
These are observed point agreements, not certified accuracy bounds.

The prior images and13.7% diagnostic stay preserved. No image was rerun
or changed for this point-only check. It does not measure the live
prototype's light loss, edge width, motion stability or performance.

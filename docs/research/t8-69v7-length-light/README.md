# Why a still's diffuse light depends on the diffuse history's length (t8-69v7), 2026-11-15

This folder holds the diagnosis of bug ticket `t8-69v7`. All takes ran on this Mac from 03:49 to 04:24 local time, under the shared GPU lock. No take used the cloud.
The ticket's note "Diagnosis design (line orchestrator, 2026-11-15)" holds the model and the takes. `docs/research/t0-y46f-window-18-excess/README.md` defines the terms that this README does not define.

The short result: the effect has two causes, one for each setting of the diffuse filter.

- With the filter on, which is the default: the GPU stores the diffuse history's half floats with a rounding that loses light in every draw. A long history sums more of these losses than a short one. The rays read the history at their hits, and that loop multiplies the loss. With a store that rounds to the nearest half float, the cap of 4 draws lies within 0.1 points of the cap of 32 draws.
- With the filter off: FSR 3.1 darkens the noisier image of a short history. Without FSR, the cap of 4 draws lies within 0.9 points of the cap of 32 draws. With the nearest-half store as well, it lies within 0.2 points.

## Terms

- The frame: the lamp hall still `rect-lamps` at 4.08 stops, no jitter, both clamps off (`TERTIUS_A_NO_CLAMP=1`). Every take uses it.
- Cap N: `TERTIUS_X_DIFFUSE_CAP=N` of the t0-y46f patch. It holds the diffuse history's length at most N draws. No cap means the diffuse history's own cap of 32 draws.
- Light: the scene light of the survey still's long mean (`scene.py`, `scenelight.py` of t0-y46f), as % of the same variant with no cap. Settled ceiling / walls, then the whole image. No tone curve lies between the renderer and these means.
- Sample: the diffuse signal's sample of a draw, one per pixel, demodulated and pre-exposed (`TemporalSignal.Sample(0)`).
- Slow history: the diffuse signal's slow history at the end of a draw (`TemporalSignal.Slow(0)`). With the filter on, the filter's first dispatch wrote it last.
- Shown diffuse light: what the composite shows, `ScreenTraceImages.ShownDiffuse`. It is the filter's output, or the slow history with the filter off.
- Stage mean: the mean luminance of one of three images over the counted draws, at the render size of 1280 x 720. The images are the sample, the slow history and the shown diffuse light.
- The loop: a ray reads, at its hit, the last draw's diffuse and specular light. With the filter on it reads the filter's output. With the filter off it reads the fast history.
- Half float: a 16-bit float. The diffuse histories are RGBA16F images. Between 1 and 2, the half floats lie 2^-10 apart.
- Nearest-half store: the new switch `TERTIUS_X_HALF_NEAREST=1`, below.
- Native: the still rendered at 1280 x 720 without FSR 3.1 (`--upscaling off`), as the survey's split does with `--native`.

## The scratch switches

`scratch-probe-on-73f4a6548.patch` holds the t0-y46f patch and the changes below. It applies to commit `73f4a6548`.

| Switch | Effect | Default | Taken |
|---|---|---|---|
| `TERTIUS_X_SINGLE_BOUNCE=1` | A hit reads none of the last draw's diffuse or specular light. This opens the loop. Specialization constant 18. | off | yes |
| `TERTIUS_X_HALF_NEAREST=1` | The diffuse slow and fast histories are rounded to the nearest half float before each store. Specialization constant 22. | off | yes |
| `TERTIUS_X_STAGES=<folder>` | The stage means, below. | off | yes |
| `TERTIUS_X_NO_FEEDBACK=1` | The filter does not write its light back into the slow history. Specialization constant 19. | off | no |
| `TERTIUS_X_NO_FIX=1` | No fix (step 3 of `accumulate_spatial.comp`) of the diffuse history. Specialization constant 20. | off | no |
| `TERTIUS_X_YOUNG_FRAMES=<n>` | `YoungFrames` of the diffuse filter. Specialization constant 21. | 4 | no |
| `SCENE_NATIVE=1` | An option of this folder's `scene.py`, not of the probe: render native. | off | yes |

The last three probe switches are built but not taken. The two causes below explain the effect to within 0.5 points, so the design's stop rule ended the takes before them. The filter's pipelines now take the specialization constants, as the accumulation's did since t0-y46f.

### The nearest-half store

```glsl
float nearestHalf(float x)
{
    float h = unpackHalf2x16(packHalf2x16(vec2(x, 0.0))).x;
    float size = abs(h);
    if (isinf(size) || isnan(size))
        return h;
    // The spacing of the half floats at h: 2^(e - 10) for a normal half, 2^-24 below the least normal one.
    float spacing = size >= 6.103515625e-05 ? exp2(floor(log2(size)) - 10.0) : exp2(-24.0);
    float other = h + (abs(x) > size ? sign(x) : -sign(x)) * spacing;
    return abs(x - other) < abs(x - h) ? other : h;
}
```

where

- `x` = the 32-bit value that a shader is about to store.
- `packHalf2x16`, `unpackHalf2x16` = GLSL's conversion to a half float and back. The conversion's rounding is the implementation's.
- `h` = `x` as a half float, by the implementation's rounding.
- `size` = the magnitude of `h`.
- `spacing` = the distance between `h` and the next half float of the same exponent.
- `other` = the half float on the other side of `x` from `h`.
- The result = whichever of `h` and `other` lies nearer to `x`. A store of it changes nothing, because it is a half float already.

One case gives a wrong `other`: an `h` that is exactly a power of two, with `x` below it. The half floats below that power lie half as far apart. That case needs a conversion that rounds away from zero, so it cannot occur under a rounding toward zero or to the nearest.

The switch applies the function to three stores of the diffuse signal: the temporal dispatch's slow and fast histories, the spatial dispatch's slow history, and the filter's write of its light into the slow history. The spatial store reads:

```glsl
if (probeHalfNearest && parameters.light == DiffuseLight)
    slow = nearestHalf(slow);
```

where `probeHalfNearest` = specialization constant 22, `parameters.light` = the signal of the dispatch, and `slow` = the history's light that step 5 stores.

### The open loop

```glsl
vec3 diffuseHistory = holdsHistory(r.diffuse) ? lastLightOf(r.diffuse, KindDiffuse) : closedLight.diffuse;
// SCRATCH (t8-69v7, TERTIUS_X_SINGLE_BOUNCE): a hit reads none of the last draw's indirect light, so no sample
// depends on a history.
if (probeSingleBounce)
    diffuseHistory = vec3(0.0);
```

where

- `diffuseHistory` = the last draw's diffuse light at the hit pixel `q`, which `leavingLightOf` adds to `q`'s direct light.
- `holdsHistory(r.diffuse)` = whether `q`'s reprojection holds a history. Without one, `closedLight.diffuse` is the environment's closed form.
- `lastLightOf` = the last draw's diffuse light at the reprojection: the filter's output, or the fast history with the filter off.
- `probeSingleBounce` = specialization constant 18.

The same switch sets `specularHistory`, the last draw's specular light at the hit, to 0.

### The stage means

The counted draws start at `TERTIUS_X_LENGTHS_FROM`, default 297, and come every `TERTIUS_X_LENGTHS_EVERY` draws, 7 in every take. After each counted draw, the renderer reads back three images and adds each pixel's luminance to a sum:

```csharp
GpuImage[] images = [tracer.Diffuse.Sample(0), tracer.Diffuse.Slow(0), tracer.ShownDiffuse];
```

where

- `tracer` = the screen tracer's images (`ScreenTraceImages`).
- `Diffuse.Sample(0)` = the sample of this draw.
- `Diffuse.Slow(0)` = the slow history at the end of the draw.
- `ShownDiffuse` = the shown diffuse light.

A pixel whose luminance is not finite adds 0. It writes the means to `stages.f32`. `tools/stages.py` gives each stage's region mean as % of another variant's. `tools/own.py` gives the slow history and the shown light as % of the same variant's sample. In a still whose steps are all linear, both shares of `own.py` are 100 %, whatever the length.

## The control runs

| Run | Compared with | Result |
|---|---|---|
| no cap, without the stage readback | no cap, with the stage readback | equal long means |
| no cap, second build | no cap, first build | equal long means |
| cap 4, second build | cap 4, first build | equal long means |
| cap 4 and no cap, first build | t0-y46f's scene-light takes | cap 4 102.3 / 105.0, whole image 101.9 %, as in t0-y46f |
| cap 4 and no cap, filter off, first build | t0-y46f's scene-light takes | cap 4 94.8 / 89.8, whole image 84.9 %, as in t0-y46f |

"Equal" means that the two `rect-lamps-long.npy` files are equal byte for byte.

## The takes

`tables.txt` holds the printed output of `tools/measure.sh` for every take. Every line is light as % of the same variant with no cap.

### Filter on

| Variant | Cap 4 | Cap 8 | Cap 16 |
|---|---|---|---|
| plain | 102.3 / 105.0, 101.9 % | 102.1 / 104.4, 101.8 % | 101.4 / 102.9, 101.2 % |
| loop open | 100.7 / 101.1, 100.1 % | not taken | not taken |
| nearest-half store | 100.0 / 100.1, 99.7 % | 100.1 / 100.2, 100.0 % | 100.1 / 100.2, 100.1 % |
| native | 102.2 / 104.9, 101.9 % | not taken | not taken |

### Filter off

| Variant | Cap 4 | Cap 8 | Cap 16 |
|---|---|---|---|
| plain | 94.8 / 89.8, 84.9 % | 98.1 / 96.2, 94.6 % | 99.3 / 98.6, 97.7 % |
| loop open | 98.5 / 94.6, 83.3 % | not taken | not taken |
| nearest-half store | 94.5 / 89.1, 84.5 % | not taken | not taken |
| native | 100.4 / 100.9, 100.1 % | 100.1 / 100.5, 100.3 % | 100.1 / 100.4, 100.2 % |
| native and nearest-half store | 100.2 / 100.2, 99.7 % | not taken | not taken |
| bounce alone | 80.7 / 88.0, 66.7 % | not taken | not taken |
| reflections alone | 100.0 / 100.1, 99.9 % | not taken | not taken |

### What the switches change in the variant with no cap

| Variant | Against | Light |
|---|---|---|
| nearest-half store, filter on | plain, filter on | 102.7 / 105.8, 102.6 % |
| nearest-half store, filter off | plain, filter off | 100.4 / 101.1, 100.6 % |
| native, nearest-half store, filter off | native, filter off | 100.3 / 101.1, 100.6 % |
| native, filter on | plain, filter on | 100.2 / 100.2, 102.9 % |

### Stage means

As % of the same stage with no cap, ceiling and walls:

| Variant | Sample | Slow history | Shown |
|---|---|---|---|
| cap 4 | 102.3 / 103.7 | 104.0 / 105.8 | 103.8 / 105.8 |
| cap 8 | 101.8 / 103.3 | 103.4 / 105.0 | 103.3 / 105.0 |
| cap 16 | 101.2 / 102.1 | 102.2 / 103.3 | 102.2 / 103.3 |
| cap 4, loop open | 100.6 / 100.3 | 102.1 / 102.0 | 102.0 / 102.0 |
| cap 4, nearest-half store | 100.1 / 99.8 | 100.0 / 99.9 | 99.8 / 99.9 |
| cap 4, filter off | 101.5 / 100.1 | 102.2 / 101.2 | 102.2 / 101.2 |

Against each variant's own sample (`own.py`), slow history / shown, ceiling and walls:

| Variant | No cap | Cap 4 |
|---|---|---|
| plain | 98.1 / 98.7, 97.7 / 97.4 | 99.7 / 99.9, 99.6 / 99.3 |
| loop open | 98.7 / 98.5, 98.0 / 97.6 | 100.3 / 99.9, 99.6 / 99.2 |
| filter off | 99.0 / 99.0, 98.8 / 98.8 | 100.0 / 100.0, 99.9 / 99.9 |
| nearest-half store | 100.2 / 100.7, 99.9 / 99.6 | 100.1 / 100.2, 100.0 / 99.6 |
| nearest-half store, filter off | 100.1 / 100.1, 99.9 / 99.9 | 100.2 / 100.2, 100.1 / 100.1 |

For the slow history against the sample, cap 8 gives 99.6 / 99.4 and cap 16 gives 99.1 / 98.8. With the nearest-half store, both give 100.2 / 100.0.

## The first cause: the half-float store, with the filter on

Measured:

- With the nearest-half store, caps 4, 8 and 16 lie at 100.0 / 100.1, 100.1 / 100.2 and 100.1 / 100.2. Without it they lie at 102.3 / 105.0, 102.1 / 104.4 and 101.4 / 102.9.
- With no cap, the slow history's mean lies 1.9 / 2.3 % below the sample's mean. With cap 4 it lies 0.3 / 0.4 % below. With the nearest-half store it lies within 0.2 % of the sample's mean at every cap.
- The nearest-half store raises the variant with no cap by 2.7 / 5.8 points and the whole image by 2.6 %.
- With the loop open, cap 4 lies at 100.7 / 101.1. Its samples lie within 0.6 points of those with no cap, but its slow history lies 2.1 / 2.0 points above.

My reading. The temporal blend moves the history toward the sample by 1 / L of the difference, where L is the length. If every step is exact, its long mean in a still equals the samples' mean. Let b be the mean loss of each draw's stores. Then the long mean settles where the blend's gain toward the samples equals the loss. That gain is the gap between the samples' mean and the history's mean, divided by L. So the history lies L x b below its samples' mean. The cap acts after the blend, so under cap N the blend's length is N + 1. A loss that is the same in every draw then costs 32 times b with no cap. Under caps 4, 8 and 16 it costs 5, 9 and 17 times b. The measured ratio of no cap to cap 4, 1.9 / 0.3 and 2.3 / 0.4, fits 32 / 5. The nearest-half store removes b, and with it the effect, at every cap. So this GPU's conversion to a half float does not round to the nearest on these stores. Under a store that rounds to the nearest, the switch changes nothing.

The size of b also fits a rounding toward zero, which the Vulkan spec allows. Such a store loses half a step of the half floats on average, about 0.035 % of the value. With the filter on, two stores of a draw write a value that is not already a half float: the temporal blend and the filter's feedback. The spatial dispatch stores the blend's half float again, which is exact. So b is about 0.07 %, and 32 b is about 2.2 %, where `own.py` measures 1.9 / 2.3 %. With the filter off, only the blend's store remains: 32 b is about 1.1 %, where `own.py` measures 1.0 / 1.2 %. The engine already assumes this rounding. `BlendRounding` in `accumulate_temporal.comp` says that a half-float blend stops short of a constant sample "where the store rounds toward zero, as the spec allows". I did not measure the rounding of a single store directly.

The loop multiplies the loss. The rays read the history at their hits, so a lower history makes lower samples, and the samples feed the history. With the loop open, the samples no longer depend on the length, and only the history's own 2 points remain. With the loop closed, cap 4's samples also stand 2.3 / 3.7 points higher, and the image 2.3 / 5.0 points.

So the light of a long history is the wrong one, and a short history is nearer the truth. The truth here is the samples' own mean with exact stores. On this Mac the diffuse history's normal cap is 32 draws. With it, the lamp hall's still is 2.7 / 5.8 % too dark in scene light. That share is of all the still's light, the direct light included.

## The second cause: FSR 3.1, with the filter off

Measured:

- Native, cap 4 lies at 100.4 / 100.9 and the whole image at 100.1 %. With FSR it lies at 94.8 / 89.8 and 84.9 %.
- Native with the nearest-half store, cap 4 lies at 100.2 / 100.2.
- Caps 8 and 16 lie at 98.1 / 96.2 and 99.3 / 98.6 with FSR, and at 100.1 / 100.5 and 100.1 / 100.4 native.
- The loss lies in the bounce signal: 80.7 / 88.0 and 66.7 % of the whole image. The reflections alone lie at 100.0 / 100.1.
- The diffuse signal's own means do not fall: cap 4's shown diffuse light is 102.2 / 101.2 % of no cap's.
- With the loop open, the loss stays: 98.5 / 94.6, whole image 83.3 %.
- With the filter on, FSR does not change the cap's effect: native cap 4 lies at 102.2 / 104.9, as with FSR. Native changes the variant with no cap by 0.2 / 0.2 points and the whole image by 2.9 %.

My reading. With the filter off, the composite shows the slow history itself. Under cap 4, that history is a mean of about 4 samples and holds much of the samples' noise. FSR 3.1 takes this image as its input and darkens it by 5 to 15 %, more where the noise is larger. FSR's accumulation is not linear: it is known to accumulate in a tone-mapped space and to clamp its history to the input's neighborhood. I did not take FSR apart, so which of its steps acts is not measured. With the filter on, the filter removes most of the noise before FSR, and FSR does not change the cap's effect. The 2.9 % over the whole image between native and FSR with no cap is not measured further. It likely lies at the bright lamps, which the smooth regions leave out.

FSR alone does not meet the stop rule on the walls: native cap 4 lies at 100.4 / 100.9. Native with the nearest-half store meets it, at 100.2 / 100.2. So with the filter off the effect has two steps. FSR takes 5 to 15 points. The half-float store of the first cause adds 0.4 / 0.9 points. It is smaller here, because only one store per draw loses light.

## Departures from the design

- Take 2 asked for four stage means: the sample, and the slow history after the temporal blend, after the spatial dispatch and after the filter's feedback. The probe reads three: the sample, the slow history at the end of the draw, and the shown light. The temporal dispatch writes its blend into a scratch image that both signals share. The filter's feedback overwrites the spatial dispatch's slow history in the same draw. So a readback after the draw cannot see the two middle stages. The stage means still separated the samples from the history.
- Take 3 listed the half-float store last. I took it first. The first queue showed each variant's slow history below its own samples, by a share that grew with the length. This held also with the loop open and with the filter off. Of the candidates, only a loss in every store of every draw grows that way.
- Take 3 had no candidate after the composite. With the filter off, the stage means rose under cap 4 while the image fell. So I added the take without FSR.

## The design's other candidates

- The history fix is step 3 of `accumulate_spatial.comp`. It runs where the length after the blend is below `min(fixFrames, cap)`, and `fixFrames` is 4 for the diffuse signal. Under cap 4, the stored length is 4 and the blend makes it 5, so the fix does not run in a still.
- The filter's rule for a young history acts where the stored length is below `YoungFrames`, 4. Under cap 4 the stored length is 4, so it does not act either.
- The upper bounds on light, and the reprojection's handling of the length: the nearest-half store alone takes the effect to within 0.1 points, and native with the nearest-half store to within 0.2 points. So together they carry at most these 0.1 to 0.2 points.

The design stops at the step that its proof names. So I took none of these.

## The proposal for the fix

Make the diffuse histories' stores independent of the implementation's rounding. The cheapest way rounds to the nearest half float in the shader at the three stores, a few instructions per pixel. A fix in `src` must round with integer arithmetic on `floatBitsToUint`. The spec's precision lets the scratch function's `log2` be off at an exact power of two. A driver can also optimize away its `isinf` and `isnan` (ticket `ter-dbzx`). Another way is to store the diffuse slow history as RGBA32F, as the precise specular history does. It costs 7.4 MB more memory and twice the bandwidth of its reads and writes. Either way removes the loss that depends on the length. Other RGBA16F images still round toward zero: the sample, and the filter's output and its passes. Their loss does not depend on the length, and it is small. This fix changes what a still shows on this Mac. A still gets brighter where the diffuse history is long: the lamp hall's still in scene light by 2.7 % on the ceiling, 5.8 % on the walls and 2.6 % over the whole image. A driver that rounds to the nearest shows no change. Today the same still differs by that much between this Mac and such a driver. The choice between the two ways, and the acceptance of the brighter stills, is a decision for the line orchestrator. FSR's darkening of a noisy input needs no fix today, because the filter is on in every configuration that ships. If a configuration shows a noisy diffuse image through FSR, it inherits a loss of up to 15 %.

## What this means for Design X's cut to 4 draws

Design X cuts a diffuse history to the fast cap of 4 draws after light evidence. On this Mac, today, the history then carries 2 to 5 % more light than the settled history of 32 draws. As it grows back to 32 draws, its light sinks by that much. A viewer can take that slow sink for lag, and a settle test can count it as one. Every "% of the reference" in the lab's records is a share of a reference with the longest histories. In the lamp hall that reference is itself 2.7 / 5.8 % too dark. With the nearest-half store, a cut history carries no bias in its mean: cap 4 lies at 100.0 / 100.1. So the cut to 4 draws is sound, and the store must be fixed before a test judges it by the light of a still.

## Limits of the measures

- All takes ran on one Mac with MoltenVK. I did not run a driver that rounds to the nearest. The claim about such drivers rests on the nearest-half store, which does in the shader what such a driver does in the store.
- The regions are those of `light.py`, cut to the smooth pixels of each truth's scene-light long mean. The stage means read the masks at the render pixels' centers.
- The long means cover 20 captures, and the stage means 90 counted draws. A difference of a few tenths of a point can be noise of these means.

## Files

| File | Content |
|---|---|
| `scratch-probe-on-73f4a6548.patch` | The scratch probe with every switch of t0-mzhr, t0-y46f and this ticket, for commit `73f4a6548` |
| `tables.txt` | The printed output of `tools/measure.sh` for every take |
| `tools/scene.py` | t0-y46f's script with the option `SCENE_NATIVE=1` |
| `tools/scenetake.sh` | One still in scene light into `/private/tmp/t8-69v7/runs/<name>` |
| `tools/stages.py`, `tools/own.py` | The stage means against another variant, and against the variant's own sample |
| `tools/queue-q1.sh` to `queue-q3.sh` | The takes in the order that they ran |
| `tools/m.sh`, `tools/measure.sh` | Runs a measuring script in the dev shell. Prints every table |

`scenelight.py` and `light.py` are the unchanged scripts of `docs/research/t0-y46f-window-18-excess/tools/`, and `m.sh` finds them there. The scripts ran from this ticket's worktree, `/Users/alexispurslane/.t3/worktrees/tertius/t3-8868de59`, with the run folders in `/private/tmp/t8-69v7`. To run them again, change those paths, and copy `m.sh` to the run folder. To build the probe, apply the patch to a scratch checkout of `73f4a6548`. Then build `src/Tertius.Demo` in Release into `/private/tmp/t8-69v7/out-x/probe-bin`, as `tools/renderer-survey.py probe-build` does.

---
id: ter-fe63
status: in_progress
deps: []
links: [ter-12xp, ter-bdoh, t1-nyjl, t1-d9k3, t1-gjnl]
created: 2026-11-22T15:17:24Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering]
---
# A cloud of bright specks sparkles on the ceiling when the sun shines into the lamp hall: rare rough reflection rays that really reach a small sunlit surface

Found by the measurement of ter-ukw6 on 2026-11-22 (docs/research/back-face-depth-layer/sill-hits-on-main.md, part 2). The orchestrator gave this bug to the back-face line on 2026-11-22. It is a bug by the user's bar: bright specks that sparkle, in a view that the game shows whenever the sun shines into a room.

## The view

The lamp hall of the scene `orbit`, the person's camera, after a run of 277,243 ticks (77.0 min, 300 degrees of the orbit). The sun then stands 30 degrees above the ship's horizon, straight ahead of the hall's nose. Its light comes in through the nose window only. It lights the floor from 1.1 m to 4.6 m in front of the nose wall, and the top face of the window's sill, which is 0.5 m deep. Today the view is the scratch script docs/research/back-face-depth-layer/sill-hits-tools/stills-sunlit-nose.json.

Stills and a video of main 821481a5c, at a fixed exposure of -1.7507356 stops: docs/research/back-face-depth-layer/sill-hits-sunlit-ceiling.png (the right half) and sill-hits-sunlit-ceiling.mp4 (the right half).

## What is known

- The ceiling between the near lamp and the nose wall holds a cloud of single bright pixels. They change from frame to frame. The side walls hold single specks too, which no census counted.
- The census of the cloud's bright reflection samples (luminance 50 or more, pre-exposed, 400 draws, box 500:0:780:220 in render pixels) after the layer's merge: 246,488 samples with a light of 19,514,034. 83.4 % of that light is real hits on the sunlit floor patch, and 16.6 % real hits on the sunlit sill. At most 2.6 % is undecided. No false hit on the sill's slab stays.
- About 1 % of the cloud's reflection samples reach one of the two sunlit surfaces. The ceiling's reflection rays are rough. Neither the sun nor a lamp lights the ceiling directly.
- So the hit decision is right, and the layer cannot remove the cloud. The fault lies after the hit: a rare, very bright, real sample reaches the image as a speck.

## The model of the investigation

A reflection sample passes these stages on its way to the screen. Each one can cut a rare bright sample, or let it through:

| Stage | What it can do to one bright sample |
|---|---|
| The ray's light at the hit, with its cap | Limits how bright one sample can be |
| The sharing of a block's ray among its pixels | Spreads one bright sample over a block, or keeps it on one pixel |
| The specular history, with its clamp box and its change evidence | Averages the sample over frames, or drops the history and shows the sample |
| The reconstruction filter | Averages the sample over neighbors |
| FSR 3.1 under the dynamic render scale | Sharpens or keeps a single bright pixel |

The first step finds the stage that lets the specks through. It builds no remedy.

## Step 1: the cause, with measurements

1. Make the view a tracked demo script, `scripts/<name>.json`, so that every later take can show it. Record its fixed exposure.
2. How bright is one such sample against its pixel's mean? Give the distribution: the sample's luminance over the pixel's settled mean, for the samples that hit a sunlit surface, and how often a pixel gets one (per frame, and per 60 frames).
3. Which stage lets it through? Follow the specks stage by stage: the raw ray light, after the cap, after the history, after the reconstruction, after FSR. Count the specks after each stage with one measure (for example pixels more than 40 of 255 above the median of their 5 x 5 neighbors, at the fixed exposure, and its pre-exposed form for the stages before the tone curve). Name the stage where the count stops falling, and say why it stops: what the clamp box holds there, what the change evidence says, how many frames of history the pixel has.
4. Why do the same rays give no cloud on the ceiling of rect-lamps as scripted? Compare the two views with numbers: the brightness of the brightest surface that a ceiling ray can reach against the ceiling's own mean, its size on the screen, and the share of the rays that reach it.
5. How does the cloud look on the Design X line's merged head a7e373197 (branch t3/combined-renderer-check)? Design X changes the evidence that cuts the reflections' history. Take the same view at the same exposure, count the specks with the same measure, and make a side-by-side video: main on the left, a7e373197 on the right.
6. Also take the view under the dynamic render scale, at one scale below 1, because the game runs under it.

Before any take, list what lights each pixel of the cloud's box, as the record of ter-ukw6 does.

## Step 2: two remedies, as designs only

From step 1's facts, name the remedies that can work, with a rough cost of each in frame time and in lost light. The line orchestrator writes two designs from them and brings them to the orchestrator. The choice can be the user's. The user's rule: lost light is fine, bright specks are not. A scratch probe of a candidate is allowed when one line of a shader decides whether it can work at all, as a patch file and a scratch ref. No remedy enters the branch.

If a candidate comes from a paper or another engine, name the situation that it was made for (its input, its hardware, its denoiser) and compare it with ours, before any work on it.

## Evidence

The cloud sparkles, so the evidence for the user is a side-by-side video: real frames at 60 a second, one fixed exposure, H.264 at CRF 12 or lower. Stills only where a fault stands still.

## Deliverables

- The tracked script of the view.
- `docs/research/specks-sunlit-room/README.md`: the answers to the five questions with their numbers, the commands, the candidate remedies, and what was not measured. Readings apart from measurements.
- Two to four pictures or videos that the user sees first, with a one-line caption each.
- The probes as patch files beside the record, and their scratch refs.

## Acceptance

- Each question of step 1 has a measured answer, or a named reason why it has none.
- The record names one stage as the place where the specks pass, with the evidence, or says plainly that more than one stage shares it.
- The candidate remedies each have a rough cost and a statement of what the user loses.

## Out of scope

A remedy in the engine. The specks on the deck's walls (ter-12xp) and the sparkle of rect-lamps' glossy floor (ter-bdoh) are related bugs with their own tickets. If this record finds that one cause covers them, it says so, and their owners decide.


## Notes

**2026-11-22T18:17:08Z**

2026-11-22, the line orchestrator, from the orchestrator. The takes of this ticket run at two render scales: 2/3 and about 0.96. Reason: the Design X line found faults of main with the back-face layer at a render scale near 1 that no take at 2/3 shows: a thin bright vertical line along the inner edge of the lamp hall's near window openings, a row of bright dots along the lower sill, and a few bright specks on the ceiling. The layer's acceptance takes all ran at 2/3. The game runs under the dynamic render scale, so both scales count.

**2026-11-23T01:57:46Z**

2026-11-22, the line orchestrator. A possible second place of this mechanism: bright specks of 1 to 3 px around the lamp hall's lamps at some render sizes, under the dynamic render scale, on main before and after step 2 of ter-qt72. The evidence is in t7-3e22 until the ray census (ter-8pco) names the samples that make them.

**2026-11-23T02:52:02Z**

2026-11-22, the line orchestrator. The lamp hall's ceiling specks at some render sizes are this ticket's mechanism, and their evidence moves here from t7-3e22. The measured chain (docs/research/ray-census/README.md, section 7, main 0ed87f9e6): at 1072 x 603 the bursts come in one draw of every 25, and FSR's jitter has 25 phases at that width. In those draws the direct light (DirectLightPass, RenderTargets.DirectDiffuse) holds a row 1 px high and about 80 px long with the value 162 at the lower edge of the far wall's window, against 0.22 on the ceiling. The plan copies it into the hit light that the rays read, and the light pyramid's finest level holds it. Ceiling samples then reach 127 (diffuse) and 272 (rough reflection), all far hits at 5.7 to 32 m, and no cap binds them. At 1040 x 585, with 27 phases, no dumped draw has the row, no ceiling sample exceeds 2, and the picture is clean. The height decides too: 1072 x 600 was clean with the same 25 phases. Nothing is bright around the lamps themselves. The lead's reading: the row is the window sill's sunlit face, seen almost edge on and thinner than a pixel, so only some jitter phases at some sizes put pixel centers on it. Not measured: the hit pixel of each sample, so the link from the row to the samples rests on values and distances. The class, as the line orchestrator names it: a source thinner than a pixel and far brighter than its surroundings, which the rays read from one draw's jittered image at its full value. The jamb leak's sill strip (t1-gout) fed specks on the cabin wall in the same way. Pictures: docs/research/ray-census/lamp-pics/burst-line.png and lamp-border-levels.png. The films, the stills and the size table: docs/research/ray-census/lamp-pics/sizetable.md and /Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a-evidence/specks/pics/ (d16M-f62-1x.png, walk-m-10-f69-1x.png, specks-old-vs-new.mp4). No new rule against the specks is built without the orchestrator's word.

**2026-11-23T04:02:32Z**

2026-11-22, the line orchestrator. A result that the orchestrator accepted on 2026-11-22, from the first still of ter-pdw8 (the lamp hall at 1072 x 603): what a ray's end reads owns the specks, and the guide is part of the fault. Measured: with a resolved read at the hit (a stand-in, a running mean of the hit light), the largest diffuse sample on the ceiling falls from 127 to 10.1, the largest rough reflection sample from 272 to 12.9, the speck pixels in the mean from 67 and 648 to 4 and 52, and the shown captures are clean (specks per capture: median 0, most 7, against 83 and 180). With the resolved read and a footprint mip together, the largest samples are 0.447 and 1.13 and no speck remains. Guided rays are 0.25 of all diffuse samples and 0.906 of today's samples above 5: the guide reads the light pyramid, whose finest level holds the sill's row of 162 in the burst draws, so it aims rays at the row, and the hit then reads the row unfiltered. So a remedy must give the guide the same filtered light as the hit. Open in ter-pdw8, and no remedy is built before it is answered: every variant darkens the light by 7 to 23 percent and adds fine flecks on the sill's upper edge. The pictures: /Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a-evidence/rayend/pics/ (lamp1072-crop-ceiling.png, lamp1072-ceiling-lamp-2x.png, lamp1072-sill-row-2x.png).

**2026-11-23T06:28:25Z**

2026-11-23, the line orchestrator. The orchestrator accepted the surface share S2' of the ray end experiment (ter-pdw8) for an engine build. The build is t1-wl7c, and this ticket depends on it. It covers the part of this ticket's specks that comes from a lit surface thinner than a pixel (the lamp hall's sill row). It does not cover real hits on a wide sunlit patch, which this ticket's census counted at 83.4 % of the cloud's light in the orbit view. After t1-wl7c merges, one take of this ticket's scene shows how much of the cloud stays, and the orchestrator then closes or keeps this ticket.

**2026-11-23T13:37:41Z**

2026-11-23, the line orchestrator. One take of this ticket's scene on main 373da00eb against the surface share's head e11b3548b (t1-wl7c), before that build's merge: the sunlit nose at 1920 x 1080, render scale 2/3, -1.7507 stops, 20 captures. Capture specks, median (largest): main 3735.5 (11890), head 3736.5 (13102). At 1:1 the ceiling cloud is as dense on head as on main. In the ceiling box over 400 draws the largest reflection sample falls from 1072 to 161, and the brightest samples' share of the box's light from 9.0 % to 4.7 %. So the surface share cuts the tail of single bright draws and leaves the cloud: this ticket stays open after t1-wl7c. The lead's hypothesis: most of the cloud is not light of one draw. A census classification of the cloud's samples on that ceiling box follows after t1-wl7c's gates (kind, hit or miss, density and weight, the hit pixel's surface share, its fresh and history parts against its neighbors', the hit surface). Picture: the lead's evidence folder, surface-share/three/nose-ceiling-1x-main-head.png.

**2026-11-23T13:39:35Z**

2026-11-23, orchestrator: the surface share (t1-wl7c) leaves this cloud as dense as on main. I opened the 1:1 crop of the sunlit nose's ceiling, main against the share's head: both hold the cloud. The share cuts only the tail of single draws (largest reflection sample 1072 to 161). So this ticket no longer depends on a fix after the hit. My hypothesis, to test before any design: the cause lies before the hit, in how the ray's direction is drawn. screen_trace.glsl:746 reads 'bool guiding = kind == KindDiffuse && trace.guidedShare > 0.0 && guideTotal() > 0.0;'. So only a diffuse ray takes a guided draw with the one-sample mixture density (mixtureDensity, line 703). A reflection ray is drawn from its lobe alone. On the rough ceiling about 1 % of the lobe's draws reach the sunlit floor patch or the sill (this ticket's census), and each carries about 100 times the mean, so a history of 32 draws cannot average them. That is the case that a mixture of a lobe draw and a light draw exists for: a wide lobe and a small bright source. The diffuse rays of the same ceiling pixels aim at the same patch through the guide. The test, by one census of the nose's ceiling box and no build: (1) the diffuse signal's samples in that box, largest sample and speck count, against the reflection's. If the diffuse signal is clean there, the guide is the difference. (2) For each bright reflection sample: its lobe density, and the guided density that guidedDensityOf gives for its direction and hit pixel. From these, the weight that the sample would have under the mixture at today's guided share, and the tail of the box's samples under that weight. (3) The roughness of the box's pixels and the share of reflection rays in the frame at that roughness or above, for the cost. If the replayed tail falls to the diffuse signal's level, the remedy to design is the guided mixture for rough reflection rays, as one model for both ray kinds. If not, the stage model of this ticket stands.

**2026-11-23T16:52:24Z**

Orchestrator, 2026-11-23: a second box for the census. rect-bay's left wall while the sunbeam leaves shows a dense field of bright specks at 1:1, on main and on the region cap's build alike (the Design X line's sheet rect-bay-leftwall.png in docs/research/t0-qvm1-region-cap-takes/pics, draws 60, 90, 130, 179; worktree t3-c3911144 until the record is on main). The specks are faint at draw 60 while the wall is bright, dense at draws 90 and 130 while the wall darkens and the sun patch elsewhere is still bright, and fewer at draw 179. That fits rare bright samples that show against a darker wall, the same suspect as on the nose's ceiling. So the census replays the mixture's weight in this box too, at draw 130, and says by ray kind (diffuse, reflection) which samples carry the tail. If the two boxes have the same carrier, one fix serves both.

**2026-11-23T18:16:19Z**

2026-11-23, line orchestrator: the wall take of the census's second box (the surface share's lead; the survey's film rect-bay at the fixed scale 2/3, -0.359 stops, main 373da00eb against the surface share's head e11b3548b; box at output 0,80,360,480). The surface share does not change the left wall. Capture specks at draw 60, 90, 130 and 179: main 0, 4, 4,205 and 2,230; head 0, 4, 4,209 and 2,229. The samples in the box at draw 130 are the same on both to every digit: the largest diffuse sample is 102.6 against a mean of 0.465 (8,920 samples above 8 times the median), and the largest reflection sample is 96.2 against a mean of 1.04 (313 above 8 times the median). The wall holds a history, so its surface share is 1 on both builds. One early sign for the census: in this box the diffuse samples' tail is heavier than the reflection's (largest over mean 220 against 92), the other way round than on the nose's ceiling, where the reflection samples carry the cloud. So the two boxes may have different carriers. Crop at 1:1: t3-2b720d96-b40221aeff8f27f7-evidence/surface-share/wall/wall-d130-sbs.png. The surface share itself is merged (main adb34f376). The census's parts 1 and 2 and the guide census (t1-nyjl) follow from one set of takes.

**2026-11-23T18:23:51Z**

2026-11-23, line orchestrator: the census's parts 1 and 2 are in (the surface share's lead; probe over e11b3548b; tables in t3-2b720d96-b40221aeff8f27f7-evidence/surface-share/census/: nose-census2.json, wall-census2.json, nose-tailrel.json, wall-tailrel.json). Views: the nose's ceiling box (render 500,0,780,220 at 1280 x 720, -1.7507 stops, 400 counted draws after 240) and rect-bay's left wall box (render 0,53,240,320, -0.359 stops, draws 125 to 135 of the film). The two kinds' samples have different scales, so each tail is measured against its own kind's mean: the count per draw above 16 times that mean, and the share of the kind's light in its brightest 0.1 percent. The replay weighs each reflection sample by the mixture's density at a guided share of 0.25: light * min(w * pLobe / (0.75 pLobe + 0.25 pGuided), MostRayWeight), with guidedDensityOf evaluated at each reflection hit. Results as largest / mean, count per draw, top share. Nose, diffuse: 252, 46, 2.3 percent. Nose, reflection today: 75, 869, 4.7 percent. Nose, reflection replayed: 100, 28, 2.2 percent. Wall, diffuse: 308, 349, 6.9 percent. Wall, reflection today: 98, 273, 8.3 percent. Wall, reflection replayed: 115, 115, 3.6 percent. The bright reflection samples of the nose all hit a sunlit floor strip (render y 386 to 388, x 589 to 690; leaving luminance about 54, 96 percent of it fresh, surface share 1). Their lobe density is 0.66 / 2.6 / 5.9 per steradian (5th, 50th, 95th percentile) and the guided density for the same direction and hit is 35 / 162 / 254, so the replayed weight is 0.063 of today's. The bright reflection samples of the wall all hit the sunlit right jamb of the window (render x 586 to 595, y 99 to 243; leaving luminance about 121, 99.7 percent fresh, share 1): lobe density 0.88 / 2.9 / 4.5, guided density 5.4 / 27 / 46, replayed weight 0.32 of today's. Every pixel of both boxes has a roughness of 0.380 (the square root of alpha), and every pixel of the frame that starts a reflection ray is at that roughness or above, so the cost of a mixture would fall on every reflection ray of these views. The answer to the orchestrator's hypothesis: on the nose it holds. The replayed reflection tail falls to the diffuse signal's level (869 to 28 per draw against the diffuse 46; top share 4.7 to 2.2 percent against 2.3). On the wall the reflection part is the same class and the mixture cuts it by 2.4 times in count, but the wall has a diffuse tail that is at least as heavy, and 80 percent of its brightest diffuse samples are guided draws aimed at the jamb. Guided draws carry 71 percent of the diffuse sum of X squared there, against 12 percent on the nose. So the two boxes share one carrier, unguided rough reflection rays that land on a small sunlit sender, and the wall has a second one, the guide's own weight on a thin bright sender, which is t1-nyjl's question. Limits of the replay: it weighs only the lobe's draws, so the replayed mean falls (2.13 to 1.87 on the nose), and its largest single sample rises a little (161 to 187), because a few samples have a guided density near 0. Chart: t3-1b834d57-evidence/census/tail-chart.png.

**2026-11-23T18:39:38Z**

Orchestrator's word, 2026-11-23: design and build the guided mixture for rough reflection rays. The wait for the guide census is over: the census (t1-nyjl) found no better draw than today's guide (the geometry guide gives 1.05 to 1.27 times at the same share), so the reflection rays take the guide as it is. Evidence: the replay cuts the reflection tail from 869 to 28 samples per draw in the nose's box and from 273 to 115 on rect-bay's left wall.

The model: one draw rule for both kinds of ray. A ray is drawn from the one-sample mixture of its own lobe and the guide, and it is weighed by the mixture's density (mixtureDensity in screen_trace.glsl already does this for the diffuse kind). The kind supplies its lobe's draw and its lobe's density, and nothing else differs. No second mixture beside the diffuse one. The guided share of the reflection kind can be its own value, because the nose's box loses with a larger diffuse share (1.68 times at 0.5). The lead reports what share it chose and why.

Order of the work: a probe switch first, and the picture before any gate. The nose's ceiling and rect-bay's left wall at draw 130 at 1:1, main beside the mixture, and the largest sample. Then a glossy scene (rect-lamps' floor, the plate) to show that a sharp reflection does not lose its detail or gain noise: a guided draw that the lobe gives almost no density is a wasted ray, so the design says at which roughness the mixture starts and why. Then the frame's time, because every reflection ray of these views qualifies. Only then tests, hardening and review.

Not in this ticket: the diffuse tail on the wall (guided draws on a thin bright sender carry 71 % of the squared sum there). It stays with t1-nyjl.

**2026-11-23T18:45:14Z**

2026-11-23, line orchestrator of the back-face line: the build's detail, after the orchestrator's word above. The surface share's lead builds it after the stills of t1-nyjl, in its worktree t3-2b720d96-b40221aeff8f27f7 on its branch, with main merged first. The review base is main's head at that merge.

### Terms, one name each

- **Lobe draw** of a kind: the draw from the pixel's own lobe. For the diffuse kind it is the cosine draw, for the reflection kind the draw from GGX's visible normals.
- **Lobe density**: the lobe draw's density per steradian along a direction (`densityOf`).
- **Guided draw**: the draw that aims at a pixel of the screen in proportion to its guide light (`guidedTargetOf`, `guidedPointOf`). It is the same draw for both kinds.
- **Guided density** of a ray: the density per steradian with which a guided draw from the ray's start runs along the ray's direction and counts (`guidedDensityOf`). It depends on the start, the direction and the hit pixel, not on the kind.
- **Guided share** of a ray: the probability that the ray takes the guided draw. It depends on the kind and on the pixel's roughness. 0 means that the kind is not guided at that pixel.
- **Mixture density**: (1 - guided share) times the lobe density plus the guided share times the guided density.

### What causes what

- `traceFrom` (screen_trace.glsl) is the one choke point of a ray's draw and of its density. Today it guides only where `kind == KindDiffuse`. After the change it asks one function for the ray's guided share, and no other line of it tests the kind for the guiding. The kind supplies its lobe draw and its lobe density, and nothing else.
- The resolve (screen_trace_resolve.comp) is the reader of the density. This is the part that the model does not show at first. For a block's reflection ray, `weighedRayOf` does not take the record's density: it takes the pixel's own lobe density along the ray (ter-xo83). A guided ray lands where the lobe density is small. So with that rule the weight of a guided ray is the pixel's response over a small lobe density, which is the tail that the mixture is there to cut. The rule becomes: the pixel weighs a block's reflection ray by its own mixture density, which is its own lobe density mixed with the ray's guided density at its own guided share. So the record must give the resolve the ray's guided density.
- Nothing new decides which pixel is bright. The guide stays today's pyramid of the sent diffuse light. No second pyramid and no second mixture.

### Signatures

screen_trace.glsl:

```glsl
// The share of the rays of the kind from the pixel that take the guided draw (LightPyramid.GuidedShareOf): 0 where the
// kind is not guided at the pixel's roughness, where the guiding is off, and while no lit pixel is in view.
float guidedShareOf(TracingPixel p, uint kind);

// The direction of the kind's lobe draw from the pixel, for the two numbers u.
vec3 lobeDirection(TracingPixel p, uint kind, vec2 u);

// The density of the one-sample mixture of a lobe draw and the guided draw at the share (LightPyramid.MixtureDensity).
float mixtureDensity(float share, float lobeDensity, float guidedDensity);
```

In `traceFrom`:

```glsl
    float share = guidedShareOf(p, kind);
    bool guided = share > 0.0 && unitOf(draw.z) < share;
    ...
        drawn = lobeDirection(p, kind, u);
```

where
- `p` = the pixel that starts the ray, with its normal, its view direction and its alpha (the floored roughness squared).
- `kind` = `KindReflection` or `KindDiffuse`.
- `u` = the two random numbers of the lobe draw, each from 0 to below 1. The reflection kind keeps its numbers from the blue-noise mask.
- `draw.z` = the third word of the ray's direction draw, which already chooses the guided draw of a diffuse ray.
- `share` = the ray's guided share, from 0 to below 1.

screen_trace_resolve.comp, in `weighedRayOf`, the density of a block's reflection ray becomes:

```glsl
        float own = mixtureDensity(guidedShareOf(p, kind), reflectionDensity(p, ray.direction), guidedDensity);
```

where
- `guidedDensity` = the ray's guided density as its tracer computed it, from the record.

C#, LightPyramid.cs: `GuidedShare` becomes the diffuse kind's share under a name that says so, and the reflection kind gets its share and the two roughness values of its ramp. `MixtureDensity(share, lobeDensity, guidedDensity)` takes the share. `GuidedShareOf` mirrors the GLSL function, so that the tests reach it. The constants have one store, LightPyramid, and reach the shaders through `ScreenTracePushConstants`, as `GuidedShare` does today. No GLSL copy of a constant.

The lead may change these signatures where the code shows a better one, and records the change and its reason.

### My settlements, for the orchestrator to reverse

1. **The reflection kind's guided share is a ramp over the roughness.** It is 0 up to a start roughness and rises linearly to its full value at an end roughness, which is at most the hand-over's start (`ScreenTrace.HandOverStart`, 0.4 by default). Reasons: below the start a guided draw is a wasted ray, because the lobe gives its direction almost no density and the pixel's response there is almost 0, while the lobe's own rays pay 1 / (1 - share) in weight. A ramp shows no edge between two materials, which is the hand-over band's own argument. The lead finds the start, the end and the full share with the census's replay, by reweighting the logged reflection samples per roughness class, before any picture. It reports the three values and their reasons.
2. **The resolve weighs a block's reflection ray by the pixel's own mixture density.** See "What causes what". In the probe the guided density may travel in a scratch image. For the engine the lead proposes where it is stored (the ray record has no free component today) and tells me what it costs in images and bindings before stage 2.
3. **A ray with a guided share above 0 walks its drawn direction, as the diffuse kind does.** The guided density is a box function of the direction, and a direction rounded to the record's code can leave its target's box, which loses light. The record still holds the code. The lead states how far the code's rounding moves the resolve's weight at the ramp's start roughness. If it is above 1 %, the lead tells me.
4. **The reflection ray's guided draw takes random numbers of its own.** With the diffuse ray's numbers, both rays of a pixel aim at the same target in a draw.
5. **Nothing changes above the hand-over.** A pixel above `MaxRoughness` traces no reflection ray, and its specular sample comes from the diffuse rays, which are guided already.
6. **The diffuse kind does not change.** With the reflection kind's share at 0, every image is bit-identical to main's. This is a test.
7. **The known mismatch stays and is reported.** The guide light of a sender is its sent diffuse light, and a reflection ray reads the hit light's reflection layer. The lead reports how far the two differ on the senders of the nose's box.

### Stage 1: a probe switch, and the pictures before any gate

A scratch change, no tests. In this order, and the lead sends me each result the moment it exists:

1. The nose's ceiling box and rect-bay's left wall at draw 130, at 1:1, main beside the mixture, with the capture specks and the largest sample of each. The takes of the speck census, at its exposure.
2. The specks sparkle, which is a fault over time. So a side-by-side video of the nose's ceiling, main left: real frames at 60 a second, one fixed exposure, H.264 at CRF 12 or lower. The lead looks at frames of it at 1:1 and describes them.
3. A glossy scene: rect-lamps' floor and the reflection plate, main beside the mixture at 1:1. A sharp reflection keeps its detail and gains no noise. The error against each build's own late mean, per crop.
4. Each region's mean against main in all of these. The mixture is unbiased, so a mean that moves by more than the noise is a defect of the probe, or the cap `MostRayWeight` cut light before, which the lead then says.
5. The frame's GPU time, main against the mixture, on the nose's view and on rect-bay, on the Mac at a 1-minute load under 8, with the load in each row. Every reflection ray of these views pays the guided density's 3 x 3 reads.

Then the lead waits for my word. I pass the pictures to the orchestrator.

### Stage 2: only after my word

- Red-green TDD. The tests first: the mixture density at a share, the ramp of the share, the reflection kind's bit-identity at a share of 0, and an energy test of the reflection kind with the mixture on (a furnace or a scene whose mean is known).
- One fresh reviewer for the whole diff, every finding resolved.
- Main merged into the branch, every test project in Checked, then in Release. Coverage of the changed lines. The mutants of the changed C# and GLSL through tools/mutate.py, one result each.
- ARCHITECTURE.md, "Guided diffuse rays", says the one draw rule for both kinds, and docs/plans/back-face-depth-layer.md's decision on the guided rays is updated.
- The Vulkan rule: nothing that only MoltenVK gives. The six known lavapipe failures (t70-0pr5) are not this ticket's.
- The final JSON report in the format of the lead's brief.

### Rules

- No cloud money without my word. The Mac only.
- Disk: `/bin/df -g /` before each build and take. No new build under 22 GiB free, and the lead stops under 20 and tells me.
- The GPU lock for at most about 20 minutes per take.
- A fork comes to the line orchestrator first.

### Out of scope

- The diffuse tail on rect-bay's left wall, the guided share of the diffuse kind and the geometry guide (t1-nyjl).
- The resolve's weight of a diffuse ray.
- The caps of the temporal stack.

**2026-11-23T18:57:36Z**

2026-11-23, line orchestrator: stage 1 has its word, after the separated stills of t1-nyjl. The bay's grain is this ticket's carrier too: the bay's walls have a roughness of 0.38 and the specular signal carries the grain of the right wall and of the wall beside the sunlit stripe (grain 0.0193 of the full picture's mean on the right wall, from a signal that is 5.9 % of its light). Three additions to the build detail above: (1) the bay's right wall and sunlit patch join stage 1's pictures, as the full picture and as the specular signal alone, main beside the mixture at 1:1, with the grain / blob table against a reference of each mode; (2) the share's ramp must be at its full value at or below a roughness of 0.38; (3) the nose comes first, because 88 % of its guided draws arrive, while in the bay 1.4 to 32 % arrive (t1-cyhy). The bay's first mixture pictures are labeled as taken with today's arrival, and the lead builds no scratch fix of the target point on its own. The lead takes t1-cyhy's four log tables before stage 1.

**2026-11-23T20:30:03Z**

2026-11-23, line orchestrator: a fork in my settlement 1 (the share as a ramp over the roughness), from the lead's replay before any picture. The probe is built as scratch (TERTIUS_PROBE_REFLECTION_SHARE and TERTIUS_PROBE_RAMP; the guided reflection ray has its own random numbers and walks its drawn direction; the resolve weighs a block's reflection ray by the pixel's own mixture density with the guided density from a probe layer; with the share at 0 every image is as main). The replay, from short takes at a share of 0 (draws 240 to 280): the relative variance of one reflection sample (variance over squared mean) under a share s of 0 (today), 0.1, 0.25, 0.5, 0.75: nose ceiling box (roughness 0.38) 7.15, 2.40, 1.41, 1.14, 1.91; bay right wall (0.38) 5.76, 4.49, 3.66, 2.92, 2.53; bay sunlit patch (0.38) 3.67, 3.96, 4.52, 6.05, 9.55; rect-lamps' glossy floor (0.38) 0.39, 0.54, 0.83, 1.71, 4.27; mirror plate (0.05) 0.084, 0.20, 0.45, 1.17, 3.34. All four rough surfaces have the same roughness of 0.38, so no ramp over the roughness can give the nose its cut of 5 times without doubling the floor's variance at 0.25. The reasons sit in the guide: on rect-lamps' floor 54 % of the reflection's second moment comes from hits on pixels to which the guide gives no light (the lamps, whose glow sends nothing in the sent diffuse light); the plate's reflection light comes only from misses; on the bay's sunlit patch the guide aims mostly at the patch itself, below the receiver's plane. The lead's two ways: (A) keep the ramp and accept the floor at 0.83 and the patch at 4.52 for the nose at 1.41; (B) let the share follow the part of the pixel's reflection light that the guide can see, a new model. The lead builds nothing of B and takes the nose's pictures at a full share of 0.25 with the ramp from 0.1 to 0.38, which do not depend on the fork.

My reading, passed to the orchestrator as a recommendation: where the guide is useless, a guided draw brings about 0 and a lobe draw is weighed by 1 / (1 - s), so the part of the variance between the two kinds of draw is s / (1 - s) of the squared mean: 0.33 at s = 0.25. That is most of the floor's tax (0.83 - 0.33 = 0.50, and the lobe draws alone at three quarters of the count give 0.52). For the reflection kind on a clean surface the squared mean is larger than the variance, so the coin flip of the guided choice is what it was not for the diffuse walls of t1-nyjl. An exact pattern of guided draws in time, one draw in 1 / s per ray cell, removes that part from a pixel's mean, and the lead already built that switch for t1-nyjl. It is one rule for both kinds. The tax then is 1 / (1 - s) on the variance alone (the floor 0.39 to about 0.52 at 0.25, the plate 0.084 to about 0.11), and it is bounded on every surface. Ordered from the lead, no take: the between and within parts per surface and share from the replay's logs (mC = the mean of X q_l / q_s over the lobe samples, mG = (mean - (1 - s) mC) / s, between = s (1 - s) (mG - mC)^2). If the within part is small where the total is large, the pictures of the bay and of rect-lamps' floor get a column with the exact pattern, and the lead reports what the specular fast history of 4 draws and its clamp do with a zero in every fourth sample, with a short film of the floor. I recommend against B: a share that follows a learned quantity needs a new stored state per pixel with its own history, its own behavior after a cut and its own way to disagree with the picture. The root on rect-lamps' floor is the guide's coverage for the reflection kind (the mismatch of my settlement 7), which is a follow-up, not part of the first build.

**2026-11-23T20:31:16Z**

2026-11-23, line orchestrator: the within table confirms the reading above. Relative variance of one reflection sample as total = between + within, with the lobe draw's mean mC and the guided draw's mean mG over the pixel's mean in brackets. Nose ceiling box: s = 0.1: 2.40 = 1.15 + 1.26 (0.81, 2.67); s = 0.25: 1.41 = 0.50 + 0.92 (0.88, 1.37); s = 0.5: 1.14 = 0.22 + 0.93 (1.22, 0.78). Bay right wall: 4.49 = 0.20 + 4.30 (0.86, 2.24); 3.66 = 0.15 + 3.50 (0.80, 1.61); 2.92 = 0.07 + 2.85 (0.82, 1.18). Bay sunlit patch: 3.96 = 0.05 + 3.90 (1.09, 0.21); 4.52 = 0.16 + 4.37 (1.26, 0.21); 6.05 = 0.43 + 5.62 (1.77, 0.23). rect-lamps' floor: 0.539 = 0.102 + 0.436 (1.11, 0.04); 0.834 = 0.308 + 0.525 (1.32, 0.04); 1.711 = 0.919 + 0.792 (1.96, 0.04). Mirror plate (0.05): 0.204 = 0.111 + 0.093 (1.11, 0.00); 0.445 = 0.333 + 0.112 (1.33, 0.00); 1.168 = 1.000 + 0.168 (2.00, 0.00). Today (s = 0): 7.15, 5.76, 3.67, 0.391, 0.084. So with an exact count of guided draws at s = 0.25 the floor is at 0.525 against today's 0.391 (1.34 times, not 2.13) and the plate at 0.112 against 0.084 (1.33 times, not 5.3); the nose is at 0.92 (7.8 times below today, not 5.1); the bay's right wall is at 3.50 (1.64 times below today) and its patch at 4.37 (1.19 times above). Where the guide brings nothing, the tax with an exact count is 1 / (1 - s) on the variance, which bounds it on every surface. The within column is what a pixel's mean over a full period keeps; the fast history of 4 draws decides how much of it shows, and the pictures report it. Chart: t3-1b834d57-evidence/fe63/within-chart.png. Tool and tables: /private/tmp/t1-wl7c/fe63/within.py and keeps-*/within-result.json in the lead's scratch, to go into the record.

**2026-11-23T20:34:00Z**

2026-11-23, the orchestrator's decision on the fork, recorded by the line orchestrator: (A) with the exact count is the model; (B), a share that follows a learned quantity per pixel, is out. The model in one sentence: a ray cell takes the guided draw on a fixed pattern in time, one draw in 1 / s, and every draw is weighed by the same mixture density, so the count is fixed where the coin was random and nothing else changes for either kind.

Conditions and my settlements for each. (1) The pattern's phase is spread over the ray cells, so that every draw holds the same share of guided cells and the frame has no beat of the pattern's period; the phase is a pure function of the cell and the draw's number. My starting form for the lead: a cell takes the guided draw at draw n when fract(n * s + phase(cell)) < s, which is exactly one draw in four at s = 1/4 and exact within one for any other s, so a ramp over the roughness still works; phase(cell) = (the cell's place in its 2 x 2 group of cells in the order 0, 2, 3, 1, plus one hashed number per group) / 4. The lead checks one lock: the plan rotates which pixel of a block traces the block's ray (t2-8foa, t0-8wag), and if that rotation's period divides the pattern's, a block's guided draw always starts from the same pixel; then the group's hashed number changes with the window, which keeps one guided draw per window. (2) One draw alone is not an unbiased estimate under a fixed count; only the period's mean is. The readers of single draws: the fast history of 4 draws and the clamp are checked with the pictures, the short film of rect-lamps' floor and a count of the clamp's moves; the anti-lag acts only on the clamp's move, so the same count answers it; the resolve shares a block's ray among the block's four pixels, which are one ray cell with one phase, so each pixel gets one guided draw per period, subject to the lock of point 1. My argument for the surface where the guide is useless at s = 1/4: a pixel's samples are three of 4/3 of its mean and one of 0, so their deviation is 0.58 of the mean and the clamp's box of 1.5 deviations around the fast history is wider than the fast history's ripple; the slow history then stays inside the box and the clamp does not move it. The count has to confirm it. (3) The diffuse kind keeps its coin for now. One function gives the choice for both kinds; the diffuse kind moves to the pattern in the same build only if that costs nothing and changes no diffuse picture beyond noise, else it stays with a line here as the known difference (the census gives it at most 1.17 times). (4) The pictures choose the share. The bar for rect-lamps' floor and the plate is no visible loss at 1:1 and in the short film; if 0.25 shows there, 0.125 is tried before any change of the ramp. (5) The two root causes that stay keep their tickets: t1-cyhy (the guided draws that do not arrive) and t1-d9k3 (the guide light for the reflection kind). This replaces my settlement 1's reading that the roughness alone separates the cases; the ramp stays only to keep the guide off the smooth lobes.

**2026-11-23T20:49:01Z**

2026-11-23, line orchestrator: stage 1's first picture exists, and it shows what the ticket asks. The sunlit nose (1920 x 1080 at 2/3, a still continued, -1.7507 stops, capture 19), main beside the mixture with the coin at a full share of 0.25 (ramp 0.1 to 0.38; every surface of the box is at 0.380). Picture: t3-2b720d96-b40221aeff8f27f7-evidence/fe63/nose-box-1x-main-mix.png, the ceiling box at 1:1. I looked at it: main's ceiling holds a dense cloud of single white specks; the mixture shows the same glossy glow as a smooth cone with a fine grain and no single specks. Numbers, main against the mixture: capture specks, median over 20 captures (largest) 3,719.5 (13,095) against 128 (2,142); largest specular sample over draws 240 to 640: 161 against 175; specular samples above 3.89 per draw 4,358 against 7,777 (the mixture spreads the rare large values over many moderate ones); mean specular sample 2.1345 against 2.1295 (-0.2 %), so the probe's mixture is unbiased before the temporal stack and the cap MostRayWeight cuts nothing that matters here; the box's shown mean over 20 captures 0.3012 against 0.3231 (+7.3 %). The lead's reading of the shown mean, not yet proved: main's clamp cuts the speck samples' tail, and the mixture leaves little tail to cut. I ordered the clamp-off pair to prove it. The exact count is in the probe's source for the reflection kind (flag 8): in each window of P = round(1 / s) draws a ray cell takes the guided draw in exactly one draw, its slot = (the cell's place in its 2 x 2 group in the order 0, 2, 3, 1, times P / 4, plus a hash of the group and the window) mod P. The lead found the lock that I asked about: chosenPixelOf rotates a block's chosen pixel as (drawIndex + h(block) + k) & 3, a period of 4, so a fixed phase would start a block's guided ray from the same pixel in every period. Hence the window-hashed slot. The four pixels of a block share the block's ray and its slot; a pixel's own ray has its own cell and slot. Next: the nose's film (coin and exact count), rect-bay's left wall at draw 130, the history lengths (t1-nyjl), then the bay's and rect-lamps' floor's columns.

**2026-11-23T21:32:18Z**

2026-11-23, line orchestrator: the nose's film, the clamp-off pair and rect-bay's left wall (full share 0.25, ramp 0.1 to 0.38; coin = the mixture with the coin, exact = the mixture with the window-hashed exact count). I looked at wall-d130-main-coin-exact.png and nose-film-frames-240-242.png in t3-2b720d96-b40221aeff8f27f7-evidence/fe63/: main shows single bright specks, new in every frame; the coin and the exact count show an even fine grain and no specks, and I see no difference between the two. (1) The clamp-off pair proves the shown mean. The box's shown mean over 20 captures at -1.7507 stops, clamp on / clamp off (with the clamp off, the anti-lag and every evidence cut are off too): main 0.3012 / 0.3156; coin 0.3231 / 0.3161; exact 0.3234 / not taken. With the clamp off, main and the mixture agree within 0.16 %: the mixture is unbiased, and main loses 4.6 % of the box's light in the clamp. With the clamp on, the mixture's picture is 2.2 % above the clamp-off value, so the clamp moves the mixture's light up a little; the build's energy test measures with the clamp off. Capture specks with the clamp off: main 997 (largest 18,164), coin 37.5 (3,426). (2) The nose's film: nose-film-main-coin-exact.mp4, the ceiling box at 1:1, main / coin / exact, 240 real frames at 60 a second (frames 120 to 359 of the orbit from tick 277003), -1.7507 stops, H.264 CRF 12. The film's box in gray 0 to 255, main / coin / exact: per-pixel temporal deviation 4.01 / 2.26 / 2.16; frame-to-frame RMS change 1.06 / 0.52 / 0.51; the box mean's period-4 amplitude 0.0012 / 0.0002 / 0.0003 against a largest other component of 0.23 / 0.19 / 0.18; box mean 146.2 / 151.2 / 151.3. The exact count makes no beat of period 4. Its gain over the coin in the shown film is small: 4.5 % less temporal deviation, where the within table predicted about 19 % in deviation; the temporal stack passes only part of it. Capture specks of the exact count's still: median 101 (largest 1,617) against the coin's 128 (2,142). (3) rect-bay's left wall at draw 130 (-0.359 stops), main / coin / exact: capture specks 4,208 / 688 / 726; reflection samples, largest / 99.9th percentile / mean: 96.2 / 76.0 / 1.041, 111.1 / 33.3 / 1.016, 91.1 / 31.5 / 1.045; the brightest 1 % of the samples carry 0.280 / 0.195 / 0.192 of the light; the diffuse samples are identical to every digit in all three (largest 102.6, mean 0.4647), so the reflection mixture leaves the diffuse kind as it was. The wall keeps 688 capture specks: its second carrier, guided diffuse draws at a thin sunlit jamb (ter-fe63's census), is not this ticket's. Still to come in stage 1: the bay's and rect-lamps' floor's columns (the bar for the floor and the plate is no visible loss), the frame's time.

**2026-11-23T21:58:50Z**

2026-11-23, line orchestrator of the back-face line: stage 1, part 1, the frozen bay's full picture under the reflection mixture. The surface share's lead took it.

Take: the frozen bay at a render scale of 2/3 and -4.089 stops, capture 19. Guided share of the reflection kind 0.25, with the ramp from a roughness of 0.1 to 0.38. Each build against its own late mean of captures 30 to 99. Pictures at 1:1, in the lead's evidence folder `t3-2b720d96-b40221aeff8f27f7-evidence/fe63/`: `bay-full-right-wall.png` and `bay-full-sunlit-patch.png`. The columns are main, the coin, the exact count, and the coin with the scratch thin box of t1-cyhy.

Each cell is the deviation of all scales / the grain / the blob, in units of main's full mean:

| Region | Main | Coin | Exact count | Coin, thin box |
|---|---|---|---|---|
| Wall beside the sun stripe | 0.0149 / 0.0096 / 0.0072 | 0.0203 / 0.0133 / 0.0090 | 0.0191 / 0.0125 / 0.0084 | 0.0193 / 0.0127 / 0.0084 |
| Back wall below the slot | 0.0150 / 0.0055 / 0.0095 | 0.0152 / 0.0059 / 0.0094 | 0.0149 / 0.0055 / 0.0095 | 0.0136 / 0.0058 / 0.0101 |
| Floor | 0.0195 / 0.0099 / 0.0114 | 0.0198 / 0.0102 / 0.0114 | 0.0194 / 0.0099 / 0.0112 | 0.0181 / 0.0091 / 0.0099 |
| Right wall | 0.0502 / 0.0331 / 0.0212 | 0.0457 / 0.0308 / 0.0175 | 0.0455 / 0.0303 / 0.0175 | 0.0322 / 0.0220 / 0.0121 |

Each region's mean over the 20 captures, against main: coin -0.14 %, +0.08 %, +0.27 %, +1.47 %. Exact count -0.17 %, +0.06 %, +0.26 %, +1.52 %. Thin box -0.20 %, -0.48 %, +0.49 %, +2.61 %.

What I see in the pictures:
- Right wall: main's top half has fine bright specks. The coin and the exact count remove most of them and leave a soft mottle of a few pixels. The thin box's column is the smoothest of the four.
- Wall beside the sun stripe: a visible loss. The dark wall left of the stripe is coarser in all three mixture columns than in main. The grain rises 39 % with the coin and 30 % with the exact count.
- Back wall and floor: no difference.

What it means:
- The loss beside the stripe is the guided draw's tax where the guide is useless. From that wall, 78.5 % of the guided draws aim below the wall's own plane and 1.4 % arrive (t1-cyhy). A wasted share of 0.25 costs 1 / (1 - 0.25) in variance under the exact count, which is 15 % in deviation. The measured 30 % is above that, so the draws that arrive add variance of their own there.
- The right wall is the reverse case: the thin box turns an arrival of 32 % into 98 % and cuts the grain by 34 % where today's box cuts 8 %. On the bay, the arrival (t1-cyhy) is a larger lever than the mixture alone.
- The right wall's mean rises by 1.5 to 2.6 %. On the nose the same sign was light that main loses in the clamp. No clamp-off pair exists here yet, so this is open.

Ordered from the lead after this part (my settlement, for the orchestrator to reverse):
1. The exact count at a share of 0.125 on the wall beside the stripe, the right wall and the nose's box, with the replay's row at 0.125 for all five surfaces. This is the orchestrator's condition 4: 0.125 before any change of the ramp.
2. A clamp-off pair of the bay's right wall, main against the exact count, to say whether the 1.5 % is the clamp's loss or a bias.
3. Part 4 gets a column with the exact count at 0.125 beside the one at 0.25.

**2026-11-23T22:01:41Z**

Orchestrator, 2026-11-23 17:15, after opening bay-full-right-wall.png and bay-full-sunlit-patch.png. I see what the line reports: the mixture removes most of the right wall's bright specks, the thin box's column is the smoothest, and the dark wall left of the sun stripe is coarser in all three mixture columns, the thin box's too. The line's three settlements stand (the exact count at 0.125 next, a clamp-off pair of the right wall, a 0.125 column in part 4).

One correction of the model before the decision: the guided draws that do not arrive have two causes with two cures, not one root. (a) The depth box misses a target that the ray meets from the side (right wall, 32 % arrive, 98 % with the thin box). The target model of t1-cyhy cures it. (b) The target lies on the shading point's own plane (the wall beside the stripe: 78.5 % of the guided draws aim below the geometric plane, t1-cyhy table d). No target model cures it, and the thin box's column shows that: 0.0127 against main's 0.0096. Such a draw is known to bring nothing before any ray is traced. So if 0.125 still shows the loss, the question is what the mixture does at a place whose guide offers it mostly its own plane, and that gets two designs before a build. The share must stay a number that is known when the draw is weighed, so that the mixture stays exact. My decision waits for part 4.

**2026-11-23T22:10:04Z**

2026-11-23, line orchestrator of the back-face line: stage 1, part 2, the frozen bay's specular signal alone. The surface share's lead took it.

Take: as part 1, with the composite showing the specular signal alone. Each build against its own late mean of this mode. Pictures at 1:1, in the lead's evidence folder `t3-2b720d96-b40221aeff8f27f7-evidence/fe63/`: `bay-spec-right-wall.png` and `bay-spec-sunlit-patch.png`. The columns are main, the coin, the exact count, and the coin with the scratch thin box, at a guided share of 0.25.

Each cell is the deviation of all scales / the grain / the blob, in units of main's full mean:

| Region | Main | Coin | Exact count | Coin, thin box |
|---|---|---|---|---|
| Wall beside the sun stripe | 0.0150 / 0.0099 / 0.0064 | 0.0229 / 0.0150 / 0.0093 | 0.0198 / 0.0132 / 0.0080 | 0.0220 / 0.0143 / 0.0091 |
| Back wall | 0.0007 / 0.0005 / 0.0002 | 0.0009 / 0.0006 / 0.0003 | 0.0006 / 0.0005 / 0.0002 | 0.0009 / 0.0006 / 0.0003 |
| Floor | 0.0034 / 0.0026 / 0.0011 | 0.0035 / 0.0027 / 0.0012 | 0.0035 / 0.0026 / 0.0012 | 0.0031 / 0.0023 / 0.0010 |
| Right wall | 0.0293 / 0.0193 / 0.0125 | 0.0290 / 0.0196 / 0.0110 | 0.0289 / 0.0194 / 0.0112 | 0.0212 / 0.0144 / 0.0080 |

The specular signal's mean per region over 20 captures, against main:

| Region | Coin | Exact count | Thin box |
|---|---|---|---|
| Wall beside the sun stripe | -0.7 % | -1.3 % | +0.1 % |
| Back wall | +4.1 % | +1.4 % | +3.6 % |
| Floor | +7.2 % | +6.1 % | +13.6 % |
| Right wall | +8.9 % | +9.2 % | +17.9 % |

What I see in the pictures:
- Right wall: main has sparse bright specks on a dark ground. The coin and the exact count show a denser, even grain and a brighter glow. The thin box's column is the smoothest and the brightest.
- Wall beside the sun stripe: coarser in all three mixture columns, a mottle of several pixels. Part 1's loss is in the specular signal.

What it means:
- On the right wall the mixture at today's arrival changes the grain's kind, not its strength: 0.0193 against 0.0196 and 0.0194. The picture has no specks, but the number does not fall. The thin box cuts the grain by 25 % and the blob by 36 %.
- The exact count pays on the wall beside the stripe: +33 % grain against the coin's +52 %.
- The means are the open point. The right wall's specular signal is 9 % brighter under the mixture and 18 % brighter under the thin box. An unbiased mixture cannot move a mean. So either main's clamp cuts that light (on the nose it cut 4.6 %), or the probe has a bias. A clamp that cuts 9 to 18 % of a wall's specular light is itself a finding: main is then that much too dark there.

Ordered: the lead's hold 3 takes the clamp-off pair in the specular signal alone for main, the exact count and the thin box. If the three means differ by more than the noise, the lead stops before part 4 and tells me.

The probe was rebuilt before part 3 with two dumps: the specular trace's layers 3 and 6 per draw (the fast history's luminance and the clamp's move), and the reflection ray's cause record (guided or not, the walk's end, counts, the hit's depth over the target's).

For part 3: the mirror plate has a roughness of 0.05, below the ramp's start of 0.1. Its guided share is 0, so its pictures must equal main's to the bit.

**2026-11-23T22:28:26Z**

2026-11-23, line orchestrator of the back-face line: stage 1, part 3, rect-lamps' glossy floor and the mirror plate. The surface share's lead took it.

Take: rect-lamps' floor (f62) at a render scale of 2/3 and 3.83 stops, and the mirror plate at 0.73 stops, capture 19, guided share 0.25. Columns: main, the coin, the exact count. Each build against its own late mean. Pictures at 1:1, in the lead's evidence folder `t3-2b720d96-b40221aeff8f27f7-evidence/fe63/`: `lamps-floor-main-coin-exact.png` and `plate-main-coin-exact.png`.

rect-lamps' floor, the output's box (600, 620) to (1320, 1000), in units of main's mean:

| Build | All / grain / blob | Mean against main |
|---|---|---|
| Main | 0.0073 / 0.0055 / 0.0026 | |
| Coin | 0.0130 / 0.0087 / 0.0052 | +0.05 % |
| Exact count | 0.0096 / 0.0068 / 0.0037 | -0.09 % |

What I see: main's floor is smooth around the lamps' long reflections. The coin has a faint mottle over the floor, clearest in the upper half. In the exact count I can hardly tell the floor from main's at 1:1 and at two times. The lead sees a fainter mottle there too. The numbers say that it exists: the grain rises 24 % and the blob 1.4 times (coin: 58 % and 2.0 times). The means do not move, so the floor has no clamp loss.

So 0.25 does not pass on the floor with the coin, and with the exact count it is at the edge. The replay predicted both rows (within 0.525 against main's 0.391 for the exact count, 0.834 in total for the coin). The cause is t1-d9k3: 54 % of this floor's reflection light comes from the lamps, which the guide does not hold, so here the guided draw is mostly a tax. The lead takes the floor's exact count at 0.125 in its hold 5. The floor's film is its own hold.

Mirror plate: the three stills are identical to the byte. The plate's floored roughness is 0.05, below the ramp's start of 0.1, so its guided share is 0. The plate's bar is met by the ramp, and a share of 0 leaves main's picture unchanged.

**2026-11-23T22:39:22Z**

2026-11-23, line orchestrator of the back-face line: stage 1, hold 3: the clamp-off takes, the fast history, and the replay at six guided shares. The surface share's lead took it.

**1. The probe's mixture has no bias. The clamp cuts main's specular light.** Frozen bay, specular signal alone, -4.089 stops, with the clamp off (and with it the anti-lag and every evidence cut). Each region's mean over 20 captures, against main:

| Region | Exact count, 0.25 | Thin box | Main's own halves (captures 10 to 14 over 15 to 19) |
|---|---|---|---|
| Wall beside the sun stripe | 0.995 | 0.994 | 0.996 |
| Back wall | 0.998 | 0.985 | 0.986 |
| Floor | 0.993 | 0.988 | 0.997 |
| Right wall | 0.995 | 0.996 | 1.000 |

All agree within main's own noise of 0.5 to 1.5 %. So the +9 % and +18 % of part 2 are the clamp. Capture 19, the specular signal with the clamp on over the same with the clamp off:

| Region | Main | Exact count | Thin box |
|---|---|---|---|
| Right wall | 0.81 | 0.88 | 0.94 |
| Floor | 0.80 | 0.86 | 0.92 |
| Back wall | 1.03 | 1.05 | 1.08 |
| Wall beside the sun stripe | 0.99 | 0.99 | 1.00 |

Main's clamp cuts 19 to 20 % of the specular light on the bay's right wall and floor. The mixture loses 12 to 14 %, and the thin box 6 to 8 %. This is the specular signal's case of ter-18hc (the slow history's clamp cuts rare bright samples). In the full picture it is 1.5 to 2.6 % of the right wall's light. A draw rule with fewer rare bright samples loses less in the clamp, so the mixture makes the picture more correct there. The build's energy test measures with the clamp off, as settled.

**2. The fast history of 4 draws and the clamp's moves**, from the trace over draws 600 to 615. The fast deviation is the per-pixel deviation over the draws over its mean. A clamp move is a pixel and draw whose clamp moved the slow history.

| Region | Main | Coin, 0.25 | Exact, 0.25 (window 4) | Exact, 0.125 (window 8) |
|---|---|---|---|---|
| Wall beside the stripe: fast deviation | 0.335 | 0.440 | 0.422 | 0.378 |
| Wall beside the stripe: clamp moves | 7.8 % | 7.4 % | 7.3 % | 7.4 % |
| Right wall: fast deviation | 0.594 | 0.559 | 0.559 | 0.604 |
| Right wall: clamp moves | 9.4 % | 7.4 % | 7.4 % | 8.2 % |

The exact count's pattern does not make the clamp move more often at a window of 4. At a window of 8 the right wall's clamp moves a little more often (8.2 % against 7.4 %) and the right wall's gain is gone (0.604 against main's 0.594). On the wall beside the stripe, 0.125 halves the loss. The anti-lag acts only on a clamp move, so the same counts answer for it. This is the answer to the orchestrator's condition 2 for the fast history, the clamp and the anti-lag.

**3. The replay at six guided shares.** One draw's relative variance (variance over the squared mean), from the logs at share 0, draws 240 to 280, which repeat the earlier numbers to every digit:

| Surface (floored roughness) | 0 | 1/32 | 1/16 | 1/8 | 1/4 | 1/2 | Least at |
|---|---|---|---|---|---|---|---|
| Nose's box (0.38) | 7.15 | 3.97 | 3.03 | 2.13 | 1.41 | 1.14 | 1/2 (0.75 gives 1.91) |
| Bay's right wall (0.38) | 5.76 | 5.19 | 4.83 | 4.31 | 3.66 | 2.92 | 0.75 or above (2.53) |
| Wall beside the stripe (0.38) | 3.67 | 3.75 | 3.84 | 4.04 | 4.52 | 6.05 | 0 |
| rect-lamps' floor (0.38) | 0.391 | 0.434 | 0.480 | 0.581 | 0.834 | 1.711 | 0 |
| Mirror plate (0.05) | 0.084 | 0.119 | 0.156 | 0.239 | 0.445 | 1.168 | 0 |

The replay cannot see a guided reflection draw that misses its target, so the real best share is at or below the replay's.

What it means: four surfaces of one roughness, 0.38, have their best share at the two ends, 0 for two of them and 1/2 or above for the other two. So no fixed share and no ramp over the roughness serves them all. A share of 0.125 keeps under half of the nose's gain (2.13 against 1.41, from 7.15) and still costs the floor (0.581 against 0.391). The share must depend on the place.

**4. The wall beside the stripe: the real draws cost about three times the replay.** The replay at 0.25 gives +19 % in variance, which is +9 % in deviation (between 0.157, within 4.365), and the guided draws carry 10.0 % of its second moment (5.1 % at 0.125). Measured: the fast deviation rises 26 % with the exact count and 31 % with the coin, and the still's grain 30 % and 39 %. The real takes of hold 5b give the two means of w squared and say where the replay falls short. Open until then.

The lead deleted 12.0 GB of its own scratch (captures and census sums whose tables are committed or sent) and kept every named picture, every build's reference mean and the four raw logs (2.8 GB). Disk 40 GiB free after the hold.

**2026-11-23T22:51:33Z**

2026-11-23, line orchestrator of the back-face line: stage 1, hold 4, the film of rect-lamps' glossy floor. The surface share's lead took it.

Take: frames 120 to 359 at 3.83 stops, one fixed exposure, guided share 0.25 with the ramp from 0.1 to 0.38. Files in the lead's evidence folder `t3-2b720d96-b40221aeff8f27f7-evidence/fe63/`: `lamps-floor-film-main-coin-exact.mp4` (main, coin, exact count from left to right; H.264 at CRF 12, 240 real frames at 60 a second, checked with ffprobe) and `lamps-floor-frames-240-242.png` (three frames of each at 1:1).

Measures in gray levels of the 8-bit output:

| Measure | Main | Coin | Exact count |
|---|---|---|---|
| Deviation over time per pixel | 0.384 | 0.643 | 0.499 |
| Change from frame to frame, RMS | 0.179 | 0.237 | 0.207 |
| Amplitude at a period of 4 frames | 0.0026 | 0.0026 | 0.0023 |
| Largest other component | 0.11 | 0.11 | 0.12 |

What I see in the frame sheet: the coin's column has the coarsest grain and it changes from frame to frame. The exact count's grain is between the coin's and main's.

What it means:
- The exact count has no beat at its window of 4 frames. Its amplitude there is at main's level. With the nose's film this answers the orchestrator's condition 1 for the pattern in time.
- At 0.25 both mixtures flicker more than main on this floor: the coin 1.67 times and the exact count 1.30 times in the deviation over time. The still's grain said the same (0.0055 / 0.0087 / 0.0068). The excess of the exact count is 0.115 of a gray level, so I do not expect to see it, but it is a loss in the number and no gain stands against it on this floor.
- The floor's best share in the replay is 0. So the floor does not decide between 0.25 and 0.125. It asks for a share that depends on the place.

**2026-11-23T23:04:57Z**

2026-11-23, line orchestrator of the back-face line: stage 1, holds 5 and 5b: the exact count at a guided share of 0.125, the guided draws by cause, and the slope of the second moment in the share. The surface share's lead took them.

**1. Stills at 0.125.** Capture 19 against each build's own late mean, in units of main's full mean, grain / blob. Pictures at 1:1 in the lead's evidence folder `t3-2b720d96-b40221aeff8f27f7-evidence/fe63/`, columns main, coin 0.25, exact 0.25, exact 0.125: `bay-125-sunlit-patch.png`, `bay-125-right-wall.png`, `nose-box-1x-125.png`, `lamps-floor-125.png`.

| Region | Main | Coin 0.25 | Exact 0.25 | Exact 0.125 | Coin 0.25, thin box |
|---|---|---|---|---|---|
| Bay, wall beside the stripe | 0.0096 / 0.0072 | 0.0133 / 0.0090 | 0.0125 / 0.0084 | 0.0108 / 0.0077 | 0.0127 / 0.0084 |
| Bay, right wall | 0.0331 / 0.0212 | 0.0308 / 0.0175 | 0.0303 / 0.0175 | 0.0316 / 0.0187 | 0.0220 / 0.0121 |
| Bay, back wall | 0.0055 / 0.0095 | 0.0059 / 0.0094 | 0.0055 / 0.0095 | 0.0054 / 0.0095 | 0.0058 / 0.0101 |
| Bay, floor | 0.0099 / 0.0114 | 0.0102 / 0.0114 | 0.0099 / 0.0112 | 0.0097 / 0.0113 | 0.0091 / 0.0099 |
| rect-lamps' floor | 0.0055 / 0.0026 | 0.0087 / 0.0052 | 0.0068 / 0.0037 | 0.0061 / 0.0031 | not taken |

Nose's box, capture specks (median): main 3,719.5, coin 0.25 128, exact 0.25 101, exact 0.125 257.5 (largest 2,666).

Means over 20 captures against main at 0.125: bay 0.9995, 1.0007, 1.002, 1.0098 (wall beside the stripe, back wall, floor, right wall) and rect-lamps' floor 0.9999. They are taken against main's recorded means, because main's early captures were deleted with the census sums.

What I see: on the wall beside the stripe the 0.125 column lies between main's and 0.25's. The right wall's four columns are close. On the nose 0.125 shows scattered white specks again, far fewer than main's. On rect-lamps' floor I cannot tell 0.125 from main.

So 0.125 halves the loss (wall beside the stripe +12 % grain against +31 %, rect-lamps' floor +13 % against +25 %) and halves the gain (nose 257.5 specks against 101, right wall's blob 0.0187 against 0.0175). It is a compromise and no answer.

**2. The reflection kind's guided draws by cause**, in percent of the guided draws. The classes are the same at 0.25 within 0.5 points.

| Region | Own plane | Box miss | Blocked | Arrives and counts | Arriving draws' share of the second moment, 0.125 / 0.25 |
|---|---|---|---|---|---|
| Bay, wall beside the stripe | 78.6 % | 19.7 % | 0.2 % | 1.5 % | 5.6 % / 10.1 % |
| Bay, right wall | 0.8 % | 67.0 % | 0.002 % | 32.2 % | 26 % / 42 % |
| Bay, floor | 1.3 % | 85.9 % | 0.04 % | 12.8 % | 19 % / 25 % |
| Nose's box | 1.0 % | 10.8 % | 0.7 % | 87.5 % | 45 % / 50 % |
| rect-lamps' floor | 82.9 % | 12.6 % | 0.2 % | 4.3 % | 0.5 % / 0.9 % |

New: rect-lamps' floor is an own-plane case too. 82.9 % of its guided draws aim below the floor's own plane, because the guide's brightest targets are the lit spots on the same floor. So both surfaces that lose under the mixture lose for cause (b), and t1-d9k3 (the guide does not hold the lamps) is the smaller part there: of the draws above the plane, a quarter arrive, and they carry under 1 % of the moment. Blocked draws are nowhere above 0.7 %.

**3. The slope of the second moment in the share**, at 0.25, from the real draws (20 draws). w is the reflection sample as the mixture weighs it, uncapped. Two estimates of the same slope: the density estimate is the mean over all draws of w squared times (lobe density minus guided density) over the mixture density. The split estimate is the lobe draws' mean of w squared minus the guided draws' mean. Both over the region's squared mean.

| Region | Lobe draws' mean of w squared, 0.25 / 0.125 | Guided draws' mean, 0.25 / 0.125 | Slope: density estimate / split estimate |
|---|---|---|---|
| Nose's box | 2.58 / 3.11 | 7.74 / 18.16 | -1.77 / -5.16 |
| Bay, wall beside the stripe | 11.80 / 9.71 | 3.94 / 4.04 | +7.32 / +7.86 |
| Bay, right wall | 6.36 / 7.89 | 13.56 / 19.46 | -4.21 / -7.20 |
| Bay, floor | 9.03 / 8.55 | 8.82 / 14.34 | +2.40 / +0.21 |
| rect-lamps' floor | 2.44 / 1.81 | 0.069 / 0.065 | +2.37 / +2.37 |

A negative slope says that a larger share lowers the variance. The sign is the same in both estimates in every region, and it is the sign of the replay's best share (nose and right wall up, wall beside the stripe and rect-lamps' floor down, bay floor nearly flat). The density estimate uses every draw and has less noise in the heavy-tailed regions: it lies between the replay's secants on both sides of 0.25 everywhere, and the split estimate does not. A share that follows the draws takes the density estimate as its signal: one number per draw, from densities that the tracer already computes.

**4. The replay's limit.** Away from a take's own share, the replays of different takes disagree in the heavy-tailed regions. At share 0 the nose's box gives 7.15 from the lobe take, 2.32 from the 0.25 take and 3.46 from the 0.125 take, and the right wall 5.76, 4.12 and 4.64. The wall beside the stripe and rect-lamps' floor agree within 12 % and 3 %. The least share is the same from every take. The bay's floor is flat from 0 to 1/4 (4.66 to 5.29). The lead suspects that a guided draw that misses its box, recorded with a guided density of 0, breaks the replay's weights. Not checked. The real stills decide, not the replay.

Disk: 27 GiB free after hold 5. The lead then deleted the four raw logs (2.76 GB, their tables are kept) and its finished films' frames (4.49 GB), 19.2 GB in all today. 41 GiB free after that.

**2026-11-23T23:14:28Z**

2026-11-23, line orchestrator of the back-face line: stage 1, part 4 (the bay with region evidence off, the diffuse cap at 128 and the exact count on top) is recorded in t1-nyjl, with its table and pictures. For this ticket: the mixture's effect is the same with that setting as without it. The right wall's blob falls from 0.0203 to 0.0168 at a guided share of 0.25, and the wall beside the stripe's grain rises 34 % at 0.25 and 13 % at 0.125. This was stage 1's last hold. Not taken: the frame's GPU time. I moved it to the probe of the share that follows the draws, if the orchestrator picks that design, because that probe has the same reads per ray and one pass more.

**2026-11-23T23:16:37Z**

Orchestrator's pick for the reflection kind's guided share, 2026-11-23 18:25 (reversible by the user). Stage 1 showed that no fixed share serves surfaces of one roughness: the nose's box and the bay's right wall get better with every step of the share, and the wall beside the stripe and rect-lamps' floor get worse. Four of five guided draws on both losing surfaces aim at the pixel's own plane.
- The mechanism is the line's design 1: the guided share of a tile of ray cells follows the slope of the second moment (the mean over all draws of w^2 (l - g) / q), stored in one small image that the screen tracer owns and one share pass writes. The share comes from earlier draws, so the mixture's density stays exact. It covers every cause of a guided draw that brings little.
- Design 2 (no guide mass below the pixel's plane) waits in a ticket: by the replay it adds at most 5.5 % in deviation on one surface and costs about 120 reads per traced reflection ray. The target model of t1-cyhy and t1-d9k3 become improvements of the guide that the share rewards by itself.
- Start value: the ramp's share. Floor 0, where the picture equals main's to the byte. Ceiling 0.25 first, 0.5 as a probe column.
- Next is one more probe of stage 1, a scratch switch, no build: the five surfaces at 1:1, the share image as a picture and over time (it must settle and not breathe), a pan and a walk across surfaces with opposite shares, the draws to settle after a cut, the films of the nose and rect-lamps' floor, the frame's GPU time, and two runs that agree to the bit. Bar: no loss that a viewer sees on rect-lamps' floor and the wall beside the stripe, the nose without specks from the first draw after a cut, the mirror plate to the byte.

**2026-11-23T23:46:30Z**

2026-11-23, replacement back-face line orchestrator (Gemini 4 Neon XHigh). Merged stage 1 record 5c01a17b8 into ter-kh4g-back-face-depth-layer (commit 626aac83e; docs/research/guide-census/, docs/research/reflection-mixture/, and .tickets/t1-nyjl.md; zero src/ changes). Created deferred Design 2 ticket t1-gjnl as ordered in the 18:25 design pick. Launched replacement ticket lead for Design 1 Stage 1 probe on Gemini 4 Neon High (exact target providerInstanceId=antigravity, model=gemini-4-neon, options={thought_level:high}, isolation=shared) in existing worktree /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7 (branch t3/fade-lit-front-trace, HEAD 5c01a17b8): clientRequestId neon-nov23-ter-fe63-lead-1, taskId node:delegated-task:command%3Amcp%3A3c0a322b-8440-4956-87c2-563575318fca%3Adelegate-task%3Aneon-nov23-ter-fe63-lead-1, childThreadId thread:delegated-task:command%3Amcp%3A3c0a322b-8440-4956-87c2-563575318fca%3Adelegate-task%3Aneon-nov23-ter-fe63-lead-1. Scope stays Stage 1 scratch probe (not production); disk 26 GiB free, zsh cloud, shared GPU lock, announce exclusive lock first.

**2026-11-24T00:11:00Z**

2026-11-23 19:11, line orchestrator: Design 1 Stage 1 probe built in /private/tmp/t1-wl7c/probe-head (RG16F 80x45 tile share image, 8x8 ray cells, linearClamp read, ShareStep=1/128, ShareGuard=1/16, MaxRatio=2.0, windowed Bayer + Van der Corput guidedTurn, census gated behind probeCensus). Hold 1 ceiling 0.25 results: (1) mirror plate byte-identical to main (sha256 d290f1a8...); (2) frozen bay (capture 19 vs own 100-cap ref, all/grain/blob): sunlit patch 0.0149/0.0095/0.0072 vs main 0.0149/0.0096/0.0072 (mean 0.99995, settled share 0.0034, std_med 0.0000, 32 draws), back wall 0.0149/0.0054/0.0095 (share 0.0343), floor 0.0194/0.0096/0.0112 (share 0.1319), right wall 0.0453/0.0303/0.0173 vs main 0.0502/0.0331/0.0212 (mean 1.01522, share 0.2499, std_med 0.0001, 0 draws); (3) sunlit nose ceiling box: capture specks median 166.5 (max 1,719 at c0, 129 at c19) vs main 3,719.5 (13,095), mean specular 2.1302 vs main 2.1345 (-0.20 %, unbiased), box mean 0.3233, settled share 0.1881 (cone 0.25, outer corners 0.02..0.15, 38 draws); (4) rect-lamps floor (provisional vs main ref): 0.00729/0.00546/0.00263 matching main to 5 decimals (98.5 % pixels bit-identical, max diff 2/255, mean 0.999997, 100 % of 480 tiles at 0 in 26 draws, std_med 0.0). Resumed after parent freed 4 invalid kqii folders (23.11 GiB free) for lamps own ref, ceiling 0.5 + window-2 clamp check, crops, share strips, motion pan/walk films, tile-border check, determinism, and exclusive GPU timing.

**2026-11-24T11:46:00Z**

2026-11-24 06:46, replacement back-face line orchestrator (Gemini 4 Neon XHigh). Complete Hold 1 results (ceilings 0.25 and 0.50, own 100-capture rect-lamps references, and window-2 fast-history clamp traces) verified from read-only output and visual check of all six c-s19.png renders under /private/tmp/t1-wl7c/fe63-d1/:
- Mirror plate (0.73 stops, roughness 0.05): byte-identical to main at both 0.25 and 0.50 (247,406 B, sha256 d290f1a857f8e0494dcf2ca4b34cf3b80753f6cbd5210c6b4da10131e90fcaca).
- Sunlit nose ceiling box (-1.7507 stops, 750,0,1170,330, 400 draws): Design 1 0.50 capture specks median 127.0 (c0 1,466, c1 444, c19 118), largest specular 198.33, mean specular 2.1272 (-0.34 % vs main), box mean 0.3244 (1.0769x main), settled share mean 0.2797 (6.7 % at ceiling, 0 % at zero, tile std median 0.0177 / p95 0.0248, settles in 55 draws).
- Frozen bay (-4.089 stops, capture 19 vs own 100-capture ref, all / grain / blob): Design 1 0.50 sunlit patch 0.0149 / 0.0096 / 0.0072 (mean 0.99982, share 0.0073, 89.0 % at 0, 58 draws), back wall 0.0150 / 0.0055 / 0.0095 (share 0.0354, 63 draws), floor 0.0193 / 0.0096 / 0.0111 (share 0.1759, 76 draws), right wall 0.0437 / 0.0291 / 0.0166 (mean 1.02040, share 0.4938, 87.7 % at ceiling, std median 0.00009, 0 draws). Independent repeat bay captures matched to the byte on both ceilings (0.25 sha256 7773fdc6..., 0.50 sha256 88909888...).
- rect-lamps glossy floor (3.83 stops, own 100-capture refs, all / grain / blob): main 0.007294 / 0.005457 / 0.002634; Design 1 0.25 0.007292 / 0.005456 / 0.002633 (mean 0.999997, 98.5 % identical, 100 % at 0 in 26 draws); Design 1 0.50 0.007290 / 0.005453 / 0.002634 (mean 0.999969, 98.2 % identical, 100 % at 0 in 45 draws).
- Window-2 clamp trace (draws 600 to 615, fast deviation / clamp move share / mean lag): sunlit patch main 0.335 / 7.76 % / 0.0075, D1 0.25 0.334 / 7.74 % / 0.0076, D1 0.50 0.335 / 7.75 % / 0.0075; right wall main 0.594 / 9.40 % / 0.0138, D1 0.25 0.561 / 7.44 % / 0.0114, D1 0.50 0.496 / 6.91 % / 0.0099 (zero clamp fighting).
Parent completed user-approved Tier 1A cleanup (51.61 GiB free, no further deletions authorized). Resuming original ter-fe63 lead thread (Neon High) in t3-2b720d96-b40221aeff8f27f7 to finish remaining Stage 1 deliverables (1:1 crop sheets, share visuals and stretched tile-border check, slow pan and walk films across opposite-share surfaces, nose and rect-lamps floor 60 fps films, bit-exact film share-image determinism pair, serialized exclusive GPU timing after t1-kqii, and Stage 1 record commit under docs/research/reflection-mixture-adaptive/ with src/ clean; no production merge).

**2026-11-24T12:41:00Z**

2026-11-24 07:41, replacement back-face line orchestrator (Gemini 4 Neon XHigh). Design 1 Stage 1 probe complete, verified, and merged into ter-kh4g-back-face-depth-layer (commit b21fd6f1f from t3/fade-lit-front-trace; ONLY docs/research/reflection-mixture-adaptive/ added, 46 files, zero src/ changes; git apply --check on probe.patch passed cleanly against 5c01a17b8; all 15 still/plot sheets visually verified).
- Step 1 (1:1 crops, share maps, settling strips, stretched tile-border check): zero visible 24-pixel tile boundaries across 0 <-> 0.25 or 0 <-> 0.50 transitions (bay border dx/dy gradient ratios: main 1.069/1.212, D1 0.25 1.070/1.219, D1 0.50 1.079/1.203).
- Step 2 (60 fps H.264 CRF 12 films, motion, cuts, determinism): sunlit nose RMS drops 53.9 % (0.25) / 56.9 % (0.50) and temporal std drops 47.4 % / 50.5 % with zero period-4/2 beats; rect-lamps floor matches main (RMS 0.13687 main vs 0.13691 / 0.13690; std 0.34577 vs 0.34579 / 0.34577); slow pan specks median drops 3,763 -> 149 (0.25) / 130 (0.50); walk specks median drops 1,063 -> 7 / 1; two independent walk-hall runs produced bit-identical w-000179.png (sha256 ab9a9225...) and d.shares.f32 across all 240 draws (sha256 68c61000..., matching --video run). Camera cut finding: 180-deg cut from settled share-0 aft view without tile reset climbs back to 90 % in 16 draws (0.25) / 30 draws (0.50), causing a brief ~12-frame speck burst (16,535 at post-cut draw 5); Stage 2 must reset the tile share image to ceiling on camera cuts so cuts match draw-0 startup behavior.
- Step 3 (exclusive GPU lock, 4m52s hold, locked 1296.0 MHz on all 27 runs, 1-min load 2.41..5.39, --pass-timestamps off counterbalanced mean vs probe-off main): sunlit nose 15.493 -> 15.958 ms (+0.465 ms, +3.00 % at 0.25) / 15.992 ms (+0.499 ms, +3.22 % at 0.50); rect-bay 15.552 -> 16.074 ms (+0.522 ms, +3.36 %) / 16.123 ms (+0.572 ms, +3.68 %); rect-lamps floor (share-0 read cost) 15.492 -> 15.827 ms (+0.336 ms, +2.17 %) / 15.855 ms (+0.363 ms, +2.34 %). Stage 2 will re-time clean engine build against unpatched main binary.
- Recommendation passed to parent for Stage 2 authorization: ceiling 0.25 default (or 0.50 optional) with camera-cut reset to ceiling. Ticket remains open (in_progress) awaiting parent/user Stage 2 go-ahead; no production edits started.

**2026-11-24T12:45:00Z**

2026-11-24 07:45, replacement back-face line orchestrator (Gemini 4 Neon XHigh). Parent Stage 1 ruling: promising motion result, not an all-bars pass; Stage 2 production and merge remain held. Parent authorized one targeted scratch follow-up at ceiling 0.25 only (no new parameter sweep): on a real camera-cut temporal invalidation (FrameHistory PendingFrame.Reset == true), initialize the tile share image to the roughness-limited starting ceiling (0.25, capped per pixel by the roughness ramp) before the new view's first trace. Compare the same settled-aft-to-nose 180-deg real camera cut across unpatched main, Design 1 0.25 reset-off, and Design 1 0.25 reset-on (first draw and first 30 draws, normal-exposure 60 fps film, counts separated from what visibly flashes at 1:1, not cold startup), plus a reverse real camera cut onto the rect-lamps glossy floor to measure any new transient grain from reset-on. Also fix README wording so counted specks (e.g. 1,776) are never called zero/speck-free, +0.336 ms is recorded as the whole-scene delta on a view whose floor has zero share (not an isolated per-floor cost), and fixed-cost acceptance stays explicitly open until a clean engine build is timed against unpatched main. Continued original ter-fe63 lead thread (Neon High) in t3-2b720d96-b40221aeff8f27f7 under --shared GPU lock, >= 20 GiB floor, zero deletions, and zero src/ commits.

**2026-11-24T13:02:00Z**

2026-11-24 08:02, replacement back-face line orchestrator (Gemini 4 Neon XHigh). Merged ceiling-0.25 real camera-cut follow-up commit b20f012f6 from t3/fade-lit-front-trace into ter-kh4g-back-face-depth-layer (ONLY docs/research/reflection-mixture-adaptive/ changed, zero src/ edits, probe.patch verified via git apply --check, all 4 new visual sheets inspected directly, zero deletions, 45.8 GiB free). Real camera-cut event verification (CameraCuts++ -> context.History.Reset == true, DrawsOf == 1, zero warm-up draws, pre-cut shares 0.000) compared a clean rebuilt unpatched main binary (5c01a17b8) against D1 0.25 reset-off and reset-on:
- Forward cut (settled aft -> sunlit nose, -1.7507 stops, detector g - median7(g) > 12): d0 / d1 / d2 / d5 / d10 / d15 / d29 / d0..d29 median / d60 counted specks: unpatched main 26,342 / 24,190 / 14,008 / 11,705 / 10,821 / 10,692 / 13,610 / 11,755 / 8,681; reset-off 26,337 / 24,446 / 13,687 / 11,323 / 11,605 / 9,338 / 3,517 / 9,816 / 787 (share 0.0059 -> 0.1956); reset-on 19,910 / 10,716 / 3,438 / 5,742 / 6,133 / 4,694 / 2,123 / 4,351 (-55.7 % vs off, -63.0 % vs main) / 731 (share 0.2500 -> 0.2083). Visually on real-cut-nose-strip-1x.png: d0 shows a coarse 2x2 block-ray pattern on all builds; unpatched main and reset-off flash stark white square firefly blotches across the dark outer ceiling over d0..d10; reset-on suppresses those outer blotches from d0 and fills the cone evenly by d1..d2 while leaving fine short-history grain inside the cone over d5..d29.
- Reverse cut (settled aft -> rect-lamps glossy floor, 3.83 stops, floor box residual grain RMS): reset-off matches unpatched main to 4-5 decimals on every draw (d0 0.01226, d5 0.00866, d10 0.00937, d29 0.00669, d60 0.00580). Reset-on (share 0.2398 -> 0.0000 by d29) adds transient grain: d0 0.01951 (+59.1 %, visibly stronger 2x2 dither grid at 1:1), d5 0.01132 (+30.7 %), d10 0.01178 (+25.7 %), d15 0.01045 (+21.5 %), d29 0.00743 (+11.1 %), d60 0.00580; d0..d14 frame-to-frame RMS 0.5084 vs 0.4345 (+17.0 %), d15..d29 0.2522 vs 0.2392 (+5.4 %); subtle at 1:1 after d0, clearly visible at 16x, no mottle or brightness shift.
- README wording fixed (exact speck detector vs visible flashes, +0.336 ms whole-scene rect-lamps delta clarification, fixed-cost acceptance explicitly OPEN requiring clean-vs-unpatched-main timing). Stage 2 production and merge remain held awaiting parent decision.

**2026-11-24T14:40:25Z**

2026-11-24, stage 2 lead (branch t3/adaptive-reflection-ray-guiding, base main 56feef7d5): the production build of Design 1 at Config B is commit e07bf0655. It has a ceiling of 0.25, a step of 1/128, a guard of 1/16, a most ratio of 2 and a ramp from 0.1 to 0.38. Every tile restarts at the ceiling when the frame resets its histories (a camera cut, the first draw, a still) and when the render size changes. Three changes from the probe, each for Vulkan on Linux:
- The share image is R32F, not RG16F, because Vulkan leaves the rounding of a half-float store to the driver.
- The bilinear read is done by hand, because the linear filtering of R32F is optional.
- The sums have no isnan check, because their bounds keep them finite.

The record is docs/research/reflection-mixture-adaptive/production/README.md, against unpatched main, in Release, at 1920 x 1080 and a render scale of 0.6667, with a fixed exposure:
- Mirror plate still: byte for byte main's.
- Settled nose specks (median, frames 120 to 359): 3,496 on main against 144.
- Camera cut onto the nose, specks d0 to d29 (median): 11,674 against 4,322. At d0 they are 26,342 against 19,910, the same as the probe's reset-on.
- Pan: 3,822 against 164. Walk: 1,073 against 7.
- Two runs of the walk give the same 240 frames.
- Camera cut onto rect-lamps' glossy floor: the floor's grain is 1.59 times main's at d0, 1.26 times at d10, 1.11 times at d29 and 1.00 times at d64. This is the accepted transient grain, the same as the probe's reset-on.
- Settled rect-lamps floor: the same as main.

GPU time, under the exclusive lock, with two counterbalanced rounds per view and a load under 8:
- Nose: 14.798 against 15.592 ms (+0.794 ms, +5.4 %).
- Bay: 14.802 against 15.725 ms (+0.923 ms, +6.2 %).
- rect-lamps: 14.846 against 15.501 ms (+0.655 ms, +4.4 %).

This is about 0.3 ms more than stage 1's table, but it is no new regression. A clocked run on the nose, at 1296 MHz in every run, gave:

| Build | Frame |
|---|---|
| Unpatched main | 14.82 ms |
| The probe build with the probe off | 15.47 ms |
| The probe at Config B | 15.94 ms |
| Production | 15.61 ms |

Stage 1 measured against its probe-off build, whose scaffolding costs 0.65 ms. Production is 0.33 ms cheaper than the probe.

**2026-11-24T17:16:30Z**

2026-11-24, stage 2 lead: the stage 2 review and its fixes (commits 8e7bcc272 and 945f36790), and the hardening.

The review ran on the Anthropic fallback (claude-opus-5-5, effort high), because Antigravity had no quota left. Its findings and their answers:

| Finding | Answer |
|---|---|
| B1: the resolve weighed a block's guided ray at the pixel's own share, not at the share that drew it | Fixed. The block guide's w holds the drawing share, and the resolve mixes the pixel's own lobe density at that share. Test: ResolveSharedReflectionTests.APixelWeighsAGuidedBlockRayByItsOwnMixtureAtTheShareThatDrewTheRay. |
| B2: a restart at a new render size threw away the shares while the histories went on | Fixed. The shares restart only when the frame resets its histories. A restart writes the ceiling into every tile of the image, which covers the largest render size. |
| B3: a comment called the step an odd multiple of 1/16 wrongly | Fixed. |
| F1: the GPU cost of 0.67 to 0.94 ms has no acceptance | Recorded in the README and ARCHITECTURE.md. The parent accepted it under the user's delegation of 2026-11-24. Ticket tf3-g3rs looks for a cheaper path at a share of 0. |
| F2: the known limit of a block that straddles two tiles | Gone with B1. |
| F3: R32G32B32A32_SFLOAT missing from the device format table | Added. |
| T1: no test of the first draw after a cut | ReflectionShareRenderTests.TheFirstDrawAfterACameraCutDrawsItsReflectionRaysAtTheCeiling. |
| T2: no test of the evidence or of a tile at 0 | TheEvidenceOfAReflectionSampleIsThatOfTheCopyAtEveryShareAndWeight, ATileAtAShareOfZeroStillFindsTheGuidedDensityThatRaisesItWhereTheGuideFindsTheLight. |
| Q1: the tile size 8 written in three places | One constant, TileBlocks. |
| Q2: the guiding condition written in three places | One function, guidesReflectionsOf. |

The retakes on 945f36790 match the takes before the fixes within 2 specks and 0.03 ms (production README). The pan's median is 162 specks, not 164.

Mutation testing, with tools/mutate.py over three lists (Rendering has no Stryker run):

| id | place | change | result | killed by or answer |
|---|---|---|---|---|
| S01 | screen_trace_share.comp:18 | a tile without moment drops to 0 | killed | ReflectionShareRenderTests.AfterTheFirstDrawEachTilesShareIsOneStepFromTheCeilingByItsBlocksEvidence, ReflectionShareRenderTests.EachLaterDrawMovesEveryTilesShareOneStepFromItsLastShare |
| S02 | screen_trace_share.comp:19 | upper ratio cap 1 | killed | ReflectionShareRenderTests.ACameraCutRestartsEveryTilesShareAtTheCeiling |
| S03 | screen_trace_share.comp:20 | step up the slope | killed | ReflectionShareRenderTests.OnASunlitFloorWhoseGuideHoldsOnlyItsOwnPlaneTheFloorsSharesFallToZero, ReflectionShareRenderTests.AfterTheFirstDrawEachTilesShareIsOneStepFromTheCeilingByItsBlocksEvidence |
| S04 | screen_trace_share.comp:29 | share pass ignores the restart | killed | ReflectionShareRenderTests.AfterTheFirstDrawEachTilesShareIsOneStepFromTheCeilingByItsBlocksEvidence, ReflectionShareRenderTests.ACameraCutRestartsEveryTilesShareAtTheCeiling |
| S05 | screen_trace_share.comp:31 | tile sums skip its last row and column | killed | ReflectionShareRenderTests.AfterTheFirstDrawEachTilesShareIsOneStepFromTheCeilingByItsBlocksEvidence, ReflectionShareRenderTests.ACameraCutRestartsEveryTilesShareAtTheCeiling |
| S06 | screen_trace_share.comp:34 | slope from the guided density | killed | ReflectionShareRenderTests.OnASunlitFloorWhoseGuideHoldsOnlyItsOwnPlaneTheFloorsSharesFallToZero, ReflectionShareRenderTests.AfterTheFirstDrawEachTilesShareIsOneStepFromTheCeilingByItsBlocksEvidence |
| S07 | screen_trace.glsl:743 | rays ignore the restart | killed | ReflectionShareRenderTests.AfterTheFirstDrawEachTilesShareIsOneStepFromTheCeilingByItsBlocksEvidence, ReflectionShareRenderTests.EachLaterDrawMovesEveryTilesShareOneStepFromItsLastShare |
| S08 | screen_trace.glsl:751 | bilinear place at the wrong tile scale | killed | ReflectionShareRenderTests.EachBlockReflectionRayIsDrawnAtTheBilinearShareOfTheTilesAroundItsBlock |
| S09 | screen_trace.glsl:735 | ramp starts at roughness 0 | killed | ReflectionShareRenderTests.ASmoothFloorsReflectionsAreThoseOfItsLobeAlone |
| S10 | screen_trace.glsl:774 | pixel share ignores its tile | killed | ReflectionShareRenderTests.EachBlockReflectionRayIsDrawnAtTheBilinearShareOfTheTilesAroundItsBlock, ReflectionShareRenderTests.ATileAtAShareOfZeroStillFindsTheGuidedDensityThatRaisesItWhereTheGuideFindsTheLight |
| S11 | screen_trace.glsl:760 | no roughness ramp | killed | ReflectionShareRenderTests.ASmoothFloorsReflectionsAreThoseOfItsLobeAlone |
| S12 | screen_trace.glsl:781 | Van der Corput scaled by 1/2 | killed | ReflectionShareRenderTests.AReflectionCellTakesTheGuidedDrawOnAnExactCount(channel: 1, reason: "a share of 1/8 gives each cell exactly one guided "···) |
| S13 | screen_trace.glsl:779 | no hashed turn of the Bayer slot | killed | ReflectionShareRenderTests.AfterTheFirstDrawEachTilesShareIsOneStepFromTheCeilingByItsBlocksEvidence |
| S14 | screen_trace.glsl:851 | no guided density at share 0 | killed | ReflectionShareRenderTests.ATileAtAShareOfZeroStillFindsTheGuidedDensityThatRaisesItWhereTheGuideFindsTheLight |
| S15 | screen_trace.glsl:868 | guided reflection ray walks the code direction | survived | judgment call: a guided ray that walked its 16-bit coded direction loses light only for draws within about 10^-4 rad of a target box edge, below every test tolerance; the code keeps the drawn direction because the guided density is a box function |
| S16 | screen_trace.glsl:897 | miss counts with the lobe density | killed | ReflectionShareRenderTests.ASunlitFloorsReflectionOfTheSkyKeepsItsMeanAtTheCeilingsShare |
| S17 | screen_trace.glsl:894 | hit counts with the lobe density | killed | GuidedRaysRenderTests.AGuidedBlocksDiffuseDensityHoldsTheCosineShareAndTheGuidedDensityOfItsHit, GuidedRaysRenderTests.OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity |
| S18 | screen_trace.glsl:943 | evidence above the hand-over band | removed | the check was dead: no pixel above the hand-over band traces a reflection ray, and at its top the weight (1 - handOver) is 0, so 8e7bcc272 deleted it |
| S19 | screen_trace.glsl:962 | no cap of the evidence weight | killed | ReflectionShareRenderTests.TheEvidenceOfAReflectionSampleIsThatOfTheCopyAtEveryShareAndWeight |
| S20 | screen_trace.glsl:964 | no guard on the evidence mixture | killed | ReflectionShareRenderTests.TheEvidenceOfAReflectionSampleIsThatOfTheCopyAtEveryShareAndWeight |
| S21 | screen_trace.glsl:953 | slope sign flipped | killed | ReflectionShareRenderTests.OnASunlitFloorWhoseGuideHoldsOnlyItsOwnPlaneTheFloorsSharesFallToZero, ReflectionShareRenderTests.ACameraCutRestartsEveryTilesShareAtTheCeiling |
| S22 | screen_trace_block_rays.comp:31 | block guide drops the guided density | killed | ReflectionShareRenderTests+PanelsReflectionOverManyFrames.AGlossyFloorsReflectionOfAGlowingPanelKeepsItsMeanAndLosesNoiseWithTheGuidedReflectionRays |
| S23 | screen_trace_resolve.comp:48 | block ray weighed with the lobe density | killed | ReflectionShareRenderTests+PanelsReflectionOverManyFrames.AGlossyFloorsReflectionOfAGlowingPanelKeepsItsMeanAndLosesNoiseWithTheGuidedReflectionRays |
| C01 | GuidedShares.cs:105 | C# step up the slope | killed | GuidedSharesTests.ATilesShareStopsAtTheCeiling, GuidedSharesTests.ATilesStepIsAtMostTheStepTimesTheMostRatio |
| C02 | GuidedShares.cs:102 | C# no-moment guard at 0 | killed | GuidedSharesTests.ATileWithoutEvidenceKeepsItsShare |
| C03 | GuidedShares.cs:91 | C# no guard | killed | GuidedSharesTests.BelowTheGuardTheEvidenceWeighsBySquaredWeightOverTheMixtureAtTheGuard |
| C04 | GuidedShares.cs:89 | C# no weight cap | killed | GuidedSharesTests.AWeightAbove10To12CountsAs10To12 |
| C05 | GuidedShares.cs:72 | C# ramp from 0 | killed | GuidedSharesTests.TheReflectionShareRisesLinearlyOverTheRamp, GuidedSharesTests.TheRampCapsATilesShareOnASmootherSurface |
| C06 | ScreenTraceImages.cs:164 | no restart on a reset | killed | ReflectionShareRestartTests.ACameraCutRestartsTheShares, ReflectionShareRestartTests.TheFirstDrawRestartsTheShares |
| C07 | ScreenTraceImages.cs:164 | no restart on a new render size | killed | ReflectionShareRestartTests.ANewRenderSizeRestartsTheSharesThoughTheHistoriesContinue; killed in the first run; 8e7bcc272 then changed the rule on purpose (no restart at a new render size), and C12 pins the new rule |
| C08 | GpuTypes.cs:923 | reflection guiding stays on with the guiding off | survived | judgment call: reachable only through the debug switch TERTIUS_GUIDED_RAYS=off, where the light pyramid is never written, so the mutant reads an undefined image and no test can pin a deterministic difference |
| S24 | screen_trace.glsl:765 | guiding without a lit pixel in view | killed | ReflectionShareRenderTests.WithoutALitPixelInViewAGlossyFloorsReflectionsAreThoseOfItsLobeAlone |
| S25 | screen_trace.glsl:750 | one tile too many in the render size | killed | ReflectionShareRenderTests.EachBlockReflectionRayIsDrawnAtTheBilinearShareOfTheTilesAroundItsBlock |
| S26 | screen_trace_block_rays.comp:32 | block guide drops the drawing share | killed | ReflectionShareRenderTests.EachBlockReflectionRayIsDrawnAtTheBilinearShareOfTheTilesAroundItsBlock, ReflectionShareRenderTests.TheFirstDrawAfterACameraCutDrawsItsReflectionRaysAtTheCeiling |
| S27 | screen_trace_resolve.comp:48 | resolve mixes at the ceiling | killed | ResolveSharedReflectionTests.APixelWeighsAGuidedBlockRayByItsOwnMixtureAtTheShareThatDrewTheRay |
| S28 | screen_trace_resolve.comp:137 | resolve ignores the block guide | killed | ResolveSharedReflectionTests.APixelWeighsAGuidedBlockRayByItsOwnMixtureAtTheShareThatDrewTheRay, ReflectionShareRenderTests.ASunlitFloorsReflectionOfTheSkyKeepsItsMeanAtTheCeilingsShare |
| S29 | screen_trace_share.comp:34 | restart leaves the tiles outside the render size | survived | judgment call: no headless renderer draws below its largest render size, so no tile lies outside the render size in a test; the write keeps a later, larger size from reading shares from before the restart |
| S30 | screen_trace_share.comp:38 | share pass ignores the restart | killed | ReflectionShareRenderTests.AfterTheFirstDrawEachTilesShareIsOneStepFromTheCeilingByItsBlocksEvidence, ReflectionShareRenderTests.ACameraCutRestartsEveryTilesShareAtTheCeiling |
| C09 | GuidedShares.cs:91 | C# bilinear scale | killed | GuidedSharesTests.ABlockTakesTheSharesOfTheTwoTilesAroundItByItsDistanceToTheirCenters |
| C10 | GuidedShares.cs:95 | C# no clamp of the high tile | killed | GuidedSharesTests.ABlockInTheLastTileOfTheImageReadsNoTileBeyondIt |
| C11 | GuidedShares.cs:91 | C# clamp past the covered tiles | survived | equivalent: with the high tile clamped to the covered tiles, a place past the last center blends the last tile with itself, so the wider clamp gives the same share |
| C12 | ScreenTraceImages.cs:167 | restart at a new render size | killed | ReflectionShareRestartTests.ANewRenderSizeKeepsTheSharesAsTheHistoriesDo |
| C13 | ScreenTracePass.cs:276 | share pass covers only the render size's tiles | survived | judgment call: as S29, the share pass covers the same tiles as the render size in every headless renderer |

43 mutants: killed 37, removed 1, survived 5. Runs: tools/mutate.py over three lists (31, 20 and 2 rows); a row shows its last run.

Coverage, from a full Debug run of tests/Tertius.Rendering.Tests with --coverage, by tools/coverage/changed-lines.py against 56feef7d5: changed C# lines 104/104 (100 %), changed branches 13/16. The three partial branches are ScreenTraceImages.cs:364, 369 and 374, the UnreachableException guards of WriteLightAges, which the refactor moved out of WriteSet unchanged. That run had one failure, EnvironmentLightingTests.ACurvedMirrorCutByTheScreensEdgeShowsTheStarsMeanInItsFirstAndLastColumnsAndRows, which passed three times alone and in both full suites. Its mirrors sit below the ramp, so it is ticket tf3-t9wo, not this change. The GLSL is covered by the mutation table.

Full suites on the branch, through tools/test-all.sh in the dev shell, before the merge of main:
- Checked (before 945f36790, whose hook then ran its 2 classes, 41 passed): Rendering 3593, Core 1207, Simulation 2007, Physics 1500, Host 2089, Unions 59, Style 276, Slop 597; 0 failed.
- Release at 945f36790: Core 1175, Simulation 2026, Physics 1493 (8 skipped), Rendering 3583 (11 skipped), Host 2081 (8 skipped), Unions 59, Style 276, Slop 597; 0 failed.

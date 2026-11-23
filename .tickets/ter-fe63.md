---
id: ter-fe63
status: open
deps: []
links: [ter-12xp, ter-bdoh, t1-nyjl]
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

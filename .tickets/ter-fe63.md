---
id: ter-fe63
status: open
deps: []
links: [ter-12xp, ter-bdoh]
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

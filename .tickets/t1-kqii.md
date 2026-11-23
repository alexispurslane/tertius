---
id: t1-kqii
status: open
deps: []
links: []
created: 2026-11-23T17:22:43Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-6fka
tags: [rendering, probe]
---
# A picture probe of the far-field store: does a grid of lean cubes, read by the diffuse rays, make the frozen bay cleaner at 1:1

A probe build with pictures, not for merge. The orchestrator ordered it on 2026-11-23 (ter-6fka's note of 17:19Z, main 66643b5ae) and gave its model. This ticket adds the build's detail. The cost question of the store is closed (t1-6tn2: 216 lean faces of 16 in 60 draws cost 0.35 ms per draw on the Mac with the range skip). The open question is whether the store changes the picture. Four changes of the history rules in a row gained in measures and not in pictures, so pictures at 1:1 decide here, before any engine design.

## What the probe is

Terms, one name each:
- **Field**: the far-field store of one interior frame: a regular grid of points, 2 m apart, in that frame's own axes. A point's place never depends on the eye.
- **Point**: one grid place. It holds a radiance cube of 16 x 16 texels per face and, per texel, the distance to the surface that the texel sees.
- **Lean face**: the face of t1-6tn2's record (section 10.2): the frame's own objects only, lit by the face's light pass with the shadow views, no prefilter. A texel that sees no own object is an **empty texel**.
- **Field read**: `fieldRadiance(frame, position, direction)`, the light that arrives at `position` from `direction` according to the field.
- **Near field**: what a diffuse ray's screen walk finds within 2 m of its start.
- **Far end**: the place 2 m along a diffuse ray from its start, where the near field ends.
- **Frozen take**: a still of a frozen world with a settled exposure, at one fixed exposure value for every build compared.

What causes what:
- The field's owner refreshes the points. In the probe it may refresh every face of every point in every draw, because cost is not the question. Nothing in the field reads the eye's place.
- The screen tracer's diffuse ray is the only reader in the variants V1 and V2. The reflection rays, the closed form and the main view's shading stay as they are.
- The frame's map of today stays, and an empty texel falls back to it: an empty texel reads the parent frame's map along its direction (`environmentLobe` of the map one nearer the root), at write time or at read time, as the lead finds simpler.

## Signatures

One reader, in a header of its own that the screen tracer includes:

```glsl
// The light that arrives at `position` from `direction` according to the field of `frame`: the 8 points around the
// position, each read along `direction`, weighted by its trilinear weight and by whether the point sees the position.
// a is the sum of the weights before the division: 0 where no point sees the position, and the caller then keeps today's
// miss light.
vec4 fieldRadiance(uint frame, vec3 position, vec3 direction);
```

where
- `frame` = the index of the interior frame whose field is read, as the pixel's map index gives it today (`p.s.map`).
- `position` = a place in the shader's world space, in m. The reader moves it into the frame's own axes.
- `direction` = a unit direction in the same space, pointing away from `position` toward the light's source.
- A point **sees** the position when the distance from the point to the position is at most the distance that the point stores along that line, plus a bias that the lead chooses and states (start with 0.2 m plus one texel's width at that distance).

The lead may change the signature where the code shows a better one, and records the change and its reason.

The three variants of who reads it, each one switch of the probe:
- **V1, misses only**: `missLightOf` reads `fieldRadiance(frame, p.origin, direction)` for a diffuse ray, in place of `environmentLobe(p.s.map, direction, 0.5)`. The position is the ray's start.
- **V2, misses and far ends**: as V1, and a diffuse ray whose hit lies more than 2 m from its start takes `fieldRadiance(frame, p.origin + 2.0 * direction, direction)` in place of the hit's light. A miss reads at the same far end. This is a choice after the walk, by the hit's distance, so the walk itself does not change, and I expect no extra build cost over V1. A ray's density stays as the walk gave it. If a guided ray makes this ambiguous, the lead also takes V2 with the guided share at 0 and says which it shows.
- **V3, the young history**: V2, and `leavingLightOf` starts a pixel without a history from the field in place of the closed form. It needs the field's light over a hemisphere, for which the lead may read a coarse mip of the point's cube along the normal, and states which. V3 is built only in stage 2.

The bounce variant: the lean face's light pass adds the field's own light at each texel (the field read at the texel's surface, over its hemisphere, as in V3), so each refresh adds one bounce. Stage 2 only.

## Storage, for the probe only

The probe is not for merge and runs on the Mac. So it may use what is fastest to build, for example more layers in the maps' cube array than the 42 maps that the engine's rule allows (the Mac allows 2,048 layers), or a plain 2D atlas with a tile per face. The engine's storage is a later design. The record states what the probe used and what would not hold on a desktop Linux driver.

## Stage 1: two pictures, then stop

1. Build the field for the bay (the scene of scripts/rect-bay.json) with the lean face, 16 per face, all points refreshed. Report the grid's size, the count of points, how many points lie inside solid geometry, and what such a point does to a read.
2. One check before any picture: which lights the lean face's light pass keeps at a point, and whether the sun's cascades cover a point outside the main view. A lamp that the main view culls must still light the field, or the field cannot show what t2b-xeo7 needs. State what the pass does today.
3. A sanity picture of the field itself: the cubes of three points (one in the open bay, one near a wall, one inside geometry if there is one), as face strips at a fixed exposure.
4. Frozen takes of the bay, main, V1 and V2, at one fixed exposure, 1920 x 1080 at the render scale that the lab's frozen bay takes use. Crops at 1:1 of the regions where main's mottle shows (the lead takes the crops of the last bay sheet of the lab, docs/research/t0-qvm1-region-cap-takes, and of the ray end record), main beside V1 beside V2, and the daub measure of each against the bar of about 0.010 (the lab's tool, docs/research/frame-time-sweep-2026-11-11/tools/daub.py).
5. Send me the two pictures (V1 and V2 beside main at 1:1) the moment they exist, with the daub numbers and one sentence each on what you see. Then wait for my word. Stage 2 runs only if one of the two shows a gain.

## Stage 2: only after my word

- The frozen rect-lamps (scripts/rect-lamps.json) at 1:1 against main.
- The lit-block film of t2b-xeo7: the floor point P = (1.95, 0, -1) behind the block reads 0.078 at rest and 0.507 on main while the eye sees the lit face. With the field it must hold still within about 10 %. A side-by-side video, main left, real frames at 60 a second, one fixed exposure, H.264 at CRF 12 or lower.
- A leak take: a lit room behind a wall thinner than the grid's 2 m. Does its light show on the dark side?
- The two speck boxes of ter-fe63 (the nose's ceiling box, and rect-bay's left wall at draw 130): capture specks and the largest sample, main against V2. The orchestrator's prediction: V2 removes the specks that far hits carry and keeps those of hits within 2 m.
- The bounce variant and V3, each as a frozen bay crop beside V2.
- The reader's GPU cost per draw (the diffuse rays' pass, main against V2, at a load under 8), and the settle time in draws after a lamp switch.

## How the result is read (the orchestrator's rule)

The store goes to an engine design only if the frozen bay is visibly cleaner at 1:1 and the film's floor point holds within about 10 %, with no leak that shows. If V1 shows nothing and V2 does, the structural step's definition changes from "what a miss reads" to "where the near field ends". If neither shows, the store is not the missing layer.

## Rules of the build

- A scratch change in the lead's worktree, kept as `probe.patch` in the record docs/research/far-field-probe/. Nothing of the probe enters src on the branch. The record, its pictures and its tools are committed.
- No test suite, no review and no mutants: nothing merges but the record. The code says in a comment that it is probe code.
- No cloud money. The Mac only.
- Disk: check `/bin/df -g /` before each build. A rebuild in the existing worktree needs 22 GiB free, and the lead stops under 20.
- The GPU lock for at most about 20 minutes per take. A timing row needs a 1-minute load under 8 at the lock. The pictures need no quiet machine.
- A fork comes to the line orchestrator first.

## Out of scope

- Any engine design of the store: its storage, its refresh schedule, its owner in C#.
- The range skip in the engine (its own ticket) and the prefilter per round.
- The reflection rays and the main view's ambient light.


## Notes

**2026-11-23T17:22:57Z**

2026-11-23, line orchestrator: the order. This probe goes to the lead of the face cost split, because it holds the lean face. That lead first builds the fix of t58-rcd6 and closes t1-6tn2's record. So this ticket is ready by its dependencies and waits for that lead. My settlements in the text above, for the orchestrator to reverse: (1) V1 reads the field at the ray's start, the same substitution as today's map read; (2) V2 is a choice after the walk by the hit's distance, so the walk does not change, and it costs no more to build than V1; (3) an empty texel reads the parent frame's map; (4) the probe may use more cube layers than the engine's 42 maps or a 2D atlas, because it runs on the Mac only and does not merge; (5) V3 and the bounce variant are stage 2.

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
# A picture probe of the lights' views as the far-field store: a ray walk in the lights' depth images, read by the diffuse rays

A probe build with pictures, not for merge. The orchestrator ordered it on 2026-11-23 and changed its model on the same day by the user's word (ter-6fka's notes of 17:19Z and 17:40Z, main 7de78028f): the store is the lights' own shadow views, not a grid of capture points. This text replaces the ticket's first text, which described the grid. The cost question of a grid is closed (t1-6tn2). The open question is whether the store changes the picture, and pictures at 1:1 decide before any engine design.

## The model

Terms, one name each:
- **View**: one depth image that the renderer draws from a place: a tile of a near light, a cascade of the sun, or the eye's depth image. Each view sees only the first surface along each of its lines of sight.
- **Seen side** of a view: the space between the view's place and its stored depth. It is known to be empty, because the view saw through it.
- **Hidden side** of a view: the space behind its stored depth. The view knows nothing about it: it can be solid, or empty and in shadow.
- **Surface texel**: a texel of a light's view. In the probe it holds, beside its depth, the surface's diffuse color and its normal.
- **Event**: a place where a ray meets a view's stored surface. A **front event** is a ray that passes from the seen side to the hidden side through a surface texel's plane, from the side of its normal. A **back event** is a ray that passes from the hidden side to the seen side through that plane, from behind.
- **Jump**: a place where a ray changes sides between two surface texels of different depth without meeting either texel's plane. It is the side of a shadow, not a surface.
- **Surface point**: what a front event gives: a position, a normal and a diffuse color.
- **First bounce** of a surface point: its diffuse color times the direct light of every light at that point, with no history.
- **Unlit surface**: a surface that no light's view holds. No light lights it directly, so it has no first bounce, but it still blocks rays.
- **Frozen take**: a still of a frozen world with a settled exposure, at one fixed exposure value for every build compared.

What causes what:
- A light's shadow pass writes the light's views, as today. In the probe it also writes each surface texel's diffuse color and normal. Who causes the light decides where the store exists: a view covers exactly the surfaces that its light reaches. No grid, no points and no placement.
- A diffuse ray of the screen tracer is the only reader. The reflection rays, the closed form and the main view's shading stay as they are.
- One function turns a surface point into light, for a hit from any view. One rule picks the hit: the nearest event over all views.

## The reader

Along a ray, in each light's view:
1. A front event is a hit on a surface that this light lights. The ray takes the surface point there.
2. A back event means that the ray would come out of a lit surface from behind, so an opaque body lies on its way. The ray ends dark.
3. A jump is the side of a shadow. The walk goes on.

The ray takes the nearest event over all views.

```glsl
// A ray's nearest event in the lights' views between the distances `from` and `to` along it: a front event with its
// surface point, a back event, or none.
ViewEvent nearestViewEvent(vec3 origin, vec3 direction, float from, float to);

// The first bounce of a surface point: its diffuse color times the direct light of every light there, through the
// existing shadow lookups, pre-exposed.
vec3 firstBounceOf(SurfacePoint point);
```

where
- `origin`, `direction` = the ray's start and unit direction in the shader's world space, in m.
- `from`, `to` = the part of the ray that the walk covers, in m from the start.
- `ViewEvent` = the event's kind (front, back, none), its distance along the ray and, for a front event, its `SurfacePoint`.
- `SurfacePoint` = a position, a normal and a diffuse color.

The lead may change these signatures where the code shows a better one, and records the change and its reason.

## My settlements of the open points, for the orchestrator to reverse

(a) **What a hit reads: the surface point, shaded by every light.** The writer stores the diffuse color and the normal, not a color already multiplied by one light's irradiance. Then a hit is a surface point like any other, and `firstBounceOf` shades it with every light through the existing shadow lookups (one tap per light is enough in the probe). This is the orchestrator's lean. If the loop over the lights is too hard to reach from the tracer in the probe, the lead takes the finding view's light alone, and says so.

(b) **What a ray with no event reads: 0 inside the frame's own surfaces, and the outside light where the ray leaves them.** The user decided on 2026-11-23 that the model is one bounce (ter-6fka's note of 17:51Z): a diffuse ray reads the direct light that a surface sends, and an unlit surface sends none. So the unlit surface's light is no longer a question. Its blocking is: the walk cannot tell an unlit wall from an opening, and the outside light must not enter through a wall. The candidate answer is the frame's map: where the map's stored distance along the ray's direction says that the direction ends on one of the frame's own surfaces, the ray reads 0, and where it says that the direction leaves them, the ray reads the map's light as today. The map is captured at the eye, so this test has the eye's parallax, and the probe measures what that does (the leak takes, and a window seen from a place far from the eye). A switch gives today's miss light for every ray with no event, as the comparison.

(c) **A crossing at the stored depth against a jump: the texel's plane decides.** A ray meets a surface texel when it crosses the plane through the texel's stored position with the texel's normal, inside the texel's footprint. The side of the normal gives front or back. A change of sides that meets no texel's plane is a jump. Where the test is in doubt (a plane nearly edge-on to the ray, or a footprint on a texel's border), the walk takes the answer that loses light and cannot leak: a doubtful crossing from the hidden side to the seen side is a back event, so the ray ends dark. The lead reports the share of rays that end by a doubtful crossing. If the normal cannot be written in the probe's shadow pass, the lead takes the plane from the depth's differences toward the nearer neighbor and says so.

(d) **The eye's depth image is one more view.** A screen hit is an event like any other: it has a place, and it blocks the ray. So V2 does not throw a far screen hit away. See the variants below. Without this, a ray would pass through an unlit wall that is on screen and that no light sees.

## The variants

- **V1, misses only**: a diffuse ray that the screen walk loses goes on through the lights' views, `nearestViewEvent` from the place where the screen walk lost it, or from a small bias past the ray's start if the walk does not give that place. A front event reads `firstBounceOf`. A back event reads 0. No event reads today's miss light (settlement b).
- **V2, the near field ends at 2 m**: as V1, and a screen hit farther than 2 m from the ray's start keeps its place but reads only its first bounce, the fresh light that the hit pixel sends (`sentDiffuseLight`, without the histories). If a guided ray makes this ambiguous, the lead also takes V2 with the guided share at 0.
- **V3, one bounce everywhere**: as V2, and every screen hit reads only its fresh light, the near hits too. This is the user's model in full: no ray reads a history, so the loop between the diffuse history and the hit light ends, and with it the long drain after a cut or a light change (3.4 % per draw, t58-rcd6). It also makes a pale closed room darker, and the probe shows by how much.

## What the store cannot know (by argument, to be measured)

1. **Unlit surfaces.** The store holds only the surfaces that some light lights. A ray toward an unlit surface finds no event, and nothing in the lights' views says whether it left the frame or hit that surface. Unlit surfaces are common: the lamp hall's ceiling is lit by bounce alone, because its lamps shine down. By the user's decision such a surface sends no light, which is the one-bounce model. What it must still do is block: if a ray with no event read the far light of the parent frame, the exterior would enter through every unlit surface, at about 4,800 times the cabin's level (t58-rcd6). Settlement b is the answer to measure.
2. **A body that no view sees** (off the screen and in every light's shadow) does not stop a ray. A ray can pass through it and then meet a lit surface. The leak takes measure it.
3. **One bounce.** By the user's decision this is the model, not a limit. A pale closed room gets darker by the later bounces that are lost. V3 shows it as a picture with each region's mean, and the path trace gives the level of a full solution for comparison.
4. **No average.** A hit reads one point of a surface. A small bright patch stays a rare bright sample. So this reader does not remove the specks that rare far hits carry. By the user's decision the store is not asked to: the temporal and spatial denoising after the rays is for that, and the store is not judged by the bay's mottle. See "A second reader" below for the one candidate that gives the denoiser better samples.
5. **The sun's cascades follow the eye.** They cover the eye's view, so a sunlit surface behind the eye is in no view, and the sun's part of the store depends on the eye's place. The lead reports which sunlit surfaces of each take the cascades hold. A sun view fit to the frame's own objects would not follow the eye. It is not part of this probe.
6. **A lamp that the main view culls** needs its views all the same, or the store has a hole (`viewKeeps` in lighting.glsl keeps a light for the main view by `inMainView`). The lead states which lights have views in each take, before any picture.

## A second reader, for stage 2 only

The store also lists the bright senders: each light's view holds exactly the surfaces that send its first bounce. A reader can pick a sender by its light, as the guide picks a bright pixel on the screen, and test with the same walk whether the sender is visible from the shaded point: any event before the sender blocks it. That is E2's reader (ter-9t54) with the visibility test that it lacked, and it is the form that removes rare bright samples. It needs a pyramid of each view's light. It is not built in stage 1.

## Stage 1: the results below, then stop

1. Before any picture: the list of lights with views in the bay and in the lit-block room, the size of each view, and which sunlit surfaces the cascades hold (points 5 and 6).
2. A sanity picture: for two lights, the view's depth, diffuse color and normal side by side.
3. Frozen takes of the bay (scripts/rect-bay.json) for main, V1, V2 and V3, at one fixed exposure, at the render scale that the lab's frozen bay takes use. Crops at 1:1 of the regions where main's mottle shows (the crops of docs/research/t0-qvm1-region-cap-takes and of the ray end record), main beside V1, V2 and V3, with the daub measure of each (docs/research/frame-time-sweep-2026-11-11/tools/daub.py) against the bar of about 0.010, and each region's mean against main.
4. The floor point of the lit-block film (t2b-xeo7): P = (1.95, 0, -1) behind the block reads 0.078 at rest and 0.507 on main while the eye sees the lit face. Two frozen takes with V1, one at rest and one at the walk's hold. This is the claim that only this store makes: the block's lit face is in its lamp's view wherever the eye is, so the point must read the same in both, within about 10 %.
5. Send me the bay's crops and the floor point's two numbers the moment they exist, with one sentence each on what you see. For the bay the question is whether a variant shows a new fault at 1:1. A lower level by lost bounces is not a fault. Then wait for my word.

Also reported from stage 1: the depth reads per ray, the count of views that a ray walks, the share of rays by their end (front event, back event, doubtful crossing, no event), and the walk's GPU cost per draw at a load under 8.

## Stage 2: only after my word

- The frozen rect-lamps (scripts/rect-lamps.json) at 1:1 against main.
- The lit-block film as a side-by-side video, main left, real frames at 60 a second, one fixed exposure, H.264 at CRF 12 or lower.
- Two leak takes, with the path trace tool for the level: a lit room behind a wall, and a dark room around a corner whose walls no light sees.
- The two speck boxes of ter-fe63 (the nose's ceiling box, and rect-bay's left wall at draw 130): capture specks and the largest sample, main against V2.
- The switch of settlement b (today's miss light for every ray with no event) as a frozen bay crop beside V1.
- The second reader, if the orchestrator orders it.
- The settle time in draws after a lamp switch.

## How the result is read (the orchestrator's rule of 2026-11-23, 17:51Z)

The store goes to an engine design if the lit-block floor point holds still within about 10 %, no leak take shows a leak, the frozen bay shows no new fault at 1:1, and the walk's cost fits. A lower level by lost bounces is not a fault. The blotches stay their own problem of the samples and the denoiser.

## Rules of the build

- A scratch change in the lead's worktree, kept as `probe.patch` in the record docs/research/light-view-probe/. Nothing of the probe enters src on the branch. The record, its pictures and its tools are committed.
- No test suite, no review and no mutants: nothing merges but the record. The code says in a comment that it is probe code.
- The probe may use what only the Mac allows. The record states what would not hold on a desktop Linux driver.
- No cloud money. The Mac only.
- Disk, the orchestrator's rule of 2026-11-23: no full build under 22 GiB free; an incremental build in the existing worktree that adds under 0.5 GiB may run down to 18 GiB; under 18 GiB the lead stops and says so. Check `/bin/df -g /` before each build and take.
- The GPU lock for at most about 20 minutes per take. A timing row needs a 1-minute load under 8 at the lock. The pictures need no quiet machine.
- A fork comes to the line orchestrator first.

## Out of scope

- Any engine design of the store: the views' new attachments, a sun view fit to a frame, a cache of later bounces.
- The reflection rays and the main view's ambient light.
- A grid of capture points and the lean cubes (dropped by the user's word; t1-6tn2's record keeps the lean face's cost).

## Notes

**2026-11-23T17:22:57Z**

2026-11-23, line orchestrator: the order. This probe goes to the lead of the face cost split, because it holds the lean face. That lead first builds the fix of t58-rcd6 and closes t1-6tn2's record. So this ticket is ready by its dependencies and waits for that lead. My settlements in the text above, for the orchestrator to reverse: (1) V1 reads the field at the ray's start, the same substitution as today's map read; (2) V2 is a choice after the walk by the hit's distance, so the walk does not change, and it costs no more to build than V1; (3) an empty texel reads the parent frame's map; (4) the probe may use more cube layers than the engine's 42 maps or a 2D atlas, because it runs on the Mac only and does not merge; (5) V3 and the bounce variant are stage 2.

**2026-11-23T17:46:20Z**

2026-11-23, line orchestrator: the ticket's text above is rewritten to the lights' views as the store, by the orchestrator's note of 17:40Z after the user's objection to a grid of capture points. The first text described a grid of lean cubes with the variants V1 to V3 and a bounce variant, and my note above lists the settlements that belonged to it. They no longer apply. The settlements of the new text are in its section 'My settlements of the open points': (a) a hit is a surface point shaded by every light; (b) a ray with no event reads today's miss light, with a switch that tests the map's distance; (c) the texel's plane tells a crossing from a jump, and a doubtful crossing ends the ray dark; (d) the eye's depth image is one more view, so V2 keeps a far screen hit's place. Stage 1 gained the lit-block floor point, because that is the claim that only this store makes. The order is as before: the face cost lead builds the fix of t58-rcd6, closes t1-6tn2's record, and then takes this probe.

**2026-11-23T17:53:10Z**

2026-11-23, line orchestrator: the text above is changed to the user's two decisions of this day (ter-6fka's note of 17:51Z). 1. The model is one bounce: an unlit surface sends no light. So settlement b is turned around: a ray with no event reads 0 where the frame's map distance says that its direction ends on the frame's own surfaces, and today's miss light is only the comparison switch. A third variant, V3, reads the fresh light at every screen hit, so that no ray reads a history. 2. The store is not asked to average: the denoiser does that. So the store is not judged by the bay's mottle, and the reading rule is the orchestrator's new one: the lit-block floor point holds within about 10 %, no leak, no new fault in the frozen bay at 1:1, and a cost that fits. The prefiltered sender read is dropped. The second reader stays the one candidate for stage 2.

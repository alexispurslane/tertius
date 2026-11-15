---
id: t1-yy2f
status: in_progress
deps: []
links: []
created: 2026-11-15T01:24:22Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-kh4g
tags: [rendering, design]
---
# The back layer gives a surface that the back-face pass does not draw the back face of the solid behind it (the borrowed back face)

Found in stage 1b (docs/research/back-face-depth-layer/stage-1b/pictures.md, part 3). The probe's back-face pass draws the one-sided solid runs only. A hair card in front of the floor then takes the floor's back face as its own. Its slab spans the air between the two: a median of 4.12 m on 131,399 card pixels of lamp-hall-16-hair. Under the layer as the slab, 24 % of the reflection rays there hit, against 2 % under the 10 % rule. 95.0 % of those hits have their crossing of the hit pixel's plane behind the ray's start. The stills hide it in that view, because the hit light and the miss light behind the cards are alike. The design of stage 2 must settle it. The line orchestrator's answer, not measured, follows Design 1 in the record. The pass draws the two-sided and cutout runs with no face culled and with the cutout test. A card's own fragment then ends its slab at the front's distance. An open one-sided mesh still has no back face of its own. For it, a flag that the mesh is not closed, computed at mesh load, can put it in the same draw. The design decides the owner of that flag.

## Design of the experiment (stage 2, 2026-11-15)

This ticket is an experiment of stage 2, with no code for the branch. It also does the census of ticket `t1-i54g`, because both change the same probe files. It uses the probe of `docs/research/back-face-depth-layer/stage-1b/probe.md` and the census of `stage-1b/pictures.md` with `stage-1b/census.patch`. Read both first, and the record's section "Stage 1b".

### Terms

- A sheet is a surface pixel whose slab has no thickness, or only the depth of its own plane.
- A closed mesh is a mesh where every edge belongs to exactly two triangles with opposite winding. An open mesh is any other mesh.
- A real hit is a hit whose ray crosses the hit pixel's plane ahead of its start, where the eye sees that surface.
- The plane span of a pixel is the depth that the pixel's own surface plane covers over a square footprint. The footprint lies around the pixel's center, and its half-width a is in pixels.

### Part 1: the pass draws what Design 1 says

Change the probe's back-face pass to the rule that Design 1 of the record has:

| Run | Faces drawn | Alpha test | What a pixel of that surface gets |
|---|---|---|---|
| One-sided solid, closed mesh | Back faces | None | Its own back face, as in stage 1b |
| One-sided cutout, closed mesh | Back faces | The cutout test | Its own back face where the texture is opaque |
| Two-sided, solid or cutout | Both faces | The cutout test for cutout | Its own fragment at the front's distance: thickness 0 |
| One-sided, open mesh | Both faces | As its material | Its own fragment at the front's distance: thickness 0 |

The pass keeps the nearest drawn fragment at or behind the front surface. So the peel rule changes for the runs that draw both faces: a fragment at the front's own distance counts there. For a closed one-sided mesh the rule stays: only a fragment behind the front counts.

One owner holds whether a mesh is closed: the mesh's record at upload, computed once from its index buffer. Its signature:

```csharp
/// <summary>Whether every edge of the mesh belongs to exactly two triangles with opposite winding.</summary>
public static bool IsClosed(ReadOnlySpan<uint> indices, ReadOnlySpan<Vector3> positions);
```

Two vertices at the same position count as one for this test, because a hard edge splits its vertices. The batcher reads the flag to sort an instance into a run of the pass. The pass itself holds no list of meshes. How the pass separates the runs is your choice: more pipelines, or one pipeline without culling whose fragment shader drops the front faces of closed one-sided instances. The cost ticket `t1-kiaw` measures that choice later, so say which one you built.

Report, for every demo scene that a script renders: how many meshes are open and one-sided, and which they are.

### Part 2: a bright object behind hair cards

Add a throwaway view: a curtain of hair cards with a bright glowing object behind it, over a glossy floor. Its camera sees the cards, the object through the gaps, and the floor's reflection of both. Reuse the cards of `lamp-hall-16-hair` or of `skin-and-hair`. Make the object bright enough that a false hit and a lost hit both show.

Render it under three rules at one fixed exposure: the 10 % rule, the probe of stage 1b (`layer`), and the pass of part 1 with `layer`. Run the sheet census of stage 1b on all three. Report the hits, the hits whose crossing lies behind the ray's start, and what the eye sees in crops at full size. Also run the lamp hall again under the pass of part 1, and report its numbers beside those of stage 1b.

### Part 3: the real hits that a sheet rule loses, by reason

Under the pass of part 1, every card pixel is a sheet. For each real hit on a sheet that the 10 % rule finds and a sheet rule loses, the census records:

- The distance on screen, in pixels, between the hit pixel and the place where the ray crosses the hit pixel's plane.
- How deep the ray enters the 10 % slab behind the pixel's center w, in units of the pixel's plane span with a = 0.5.
- The angle between the ray and the card's plane.
- Whether the ray starts on a hair card.

From these, sort the lost hits into reasons. Candidates:

1. The stair case: the ray crosses the plane inside the hit pixel, in the half where the plane is nearer than the center.
2. The crossing lies one pixel before the hit pixel, so a footprint of a = 1.5 keeps it.
3. The ray runs behind the card nearly along its plane and crosses it far from the hit pixel. The 10 % slab catches it, and the plane does not.
4. The crossing falls in a cutout gap between strands, and the slab of the next strand catches the ray.
5. Another reason, which you name.

For each reason, say whether such a hit is a hit of the visible face. Give its share of the lost hits and of their light. Then measure the sheet rules: no thickness, the plane span with a = 0.5, and the plane span with a = 1.5. The rule for a = 1.5 is new. At the finest level, the sheet's back w is the center w minus 1.5 times a sum. The sum adds the absolute parts of the plane's gradient per pixel. Give for each rule the real hits that it keeps and the hits of reasons 3 and 4 that it also keeps.

### Settled decisions

- The probe's changes, the view and the census stay throwaway: patches, scratch commits, and the folder `docs/research/back-face-depth-layer/stage-2/`. The branch gets documents, images, scripts and small tools only.
- The closed-mesh test is part of the probe here. The real one comes with the build, with tests.
- The census of `t1-i54g` is part 3 of this ticket. The merge of this ticket closes both.
- The cloud budget of this ticket is $1.20 by the job log's estimates.

### Deliverables

- `docs/research/back-face-depth-layer/stage-2/cards.md`: the three parts with their tables, the commands, and what was not checked. Keep measured facts apart from your reading.
- Comparison images under `docs/research/back-face-depth-layer/stage-2/`, at most 10 files and about 5 MB.
- The patch against the probe of stage 1b, and the scratch refs.

## Acceptance Criteria

The design document of the back-face depth layer states the rule for a surface that is not closed. A test view with a bright object behind hair cards shows no hit on a card from a ray that starts behind the card's plane.


## Notes

**2026-11-15T12:10:27Z**

Merged 2026-11-15 as ab7bdcd34 (the lead's head 42b656643). The lead reported the status done and not ready_for_integration, because the two acceptance criteria name the design document, which is the line orchestrator's work. The experiment itself is complete: clean checkout, no fork, every review finding resolved with evidence. Record: docs/research/back-face-depth-layer/stage-2/cards.md. Main numbers: under the pass of Design 1 all 139,646 hair pixels of the lamp hall hold their own fragment, the hits with the crossing behind the start fall from 817,466 to 1,525, and no scripted demo scene holds an open one-sided opaque mesh. The ticket stays open until the design document states the rule for a surface that is not closed.

---
id: t1-r9vj
status: in_progress
deps: []
links: []
created: 2026-11-15T13:23:44Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, research]
---
# A view with a lit front and a dark back, with its own path trace: is any rule for a blocked ray's light nearer than the fade over the real slab

Deferred on 2026-11-15 by the line orchestrator, under the user's bar of that day. The bar is a good look with no visible bug, and a path trace is only a tool to diagnose a bug. This ticket asks for a new path trace to rank rules by accuracy, and no still shows a bug at a thick slab. So no lead starts it now. One event starts it: a still or a film shows a fault of the fade, such as light of a lit front on a surface behind a dark back.

This ticket is an experiment, with no code for the branch. It follows the reference experiment of stage 2. Read first: `docs/research/back-face-depth-layer/stage-2/reference.md`, parts 1 and 2, and `docs/plans/back-face-depth-layer.md`, decision 6.

## The question

A ray that the screen walk finds blocked takes the light of the hit pixel's visible face, faded by the entry share:

```glsl
float fade = edgeFade(hit) * (1.0 - entryShare);
result.light = mix(missLight, hitLightOf(hitPixel, kind), fade);
```

where

- `entryShare` = how deep in the slab the ray enters it: 0 at the front, 1 at the back.
- `hitLightOf(hitPixel, kind)` = the light of the face that the camera sees at the hit pixel.
- `missLight` = the light of a ray that nothing on screen blocks.

The reference experiment judged the rules in two views. The fade over the real slab was nearer to a path trace than the fade over at most 10 %. A rule with no fade by the entry share was nearer still in six of eight pillar cells. It overshot one cell by 67 %. The design keeps the fade over the real slab.

Both views are closed rooms of light-gray surfaces. There a hidden face is lit about as a visible one, so the visible face's light is a good stand-in for the hidden face's. The views do not test the opposite case. This ticket tests it.

## The model of the experiment

The fade says: the deeper a ray enters a slab, the less the visible face's light tells about the face that the ray meets. So the test needs a slab whose visible face and hidden face are lit very differently, and receivers whose rays meet the hidden face.

Build two views of one scene, as a throwaway scene of the probe:

| View | The slab's face toward the camera | The slab's hidden faces | What a rule with no fade predicts on the receivers behind the slab |
|---|---|---|---|
| Lit front | Lit directly by a lamp | In shadow, lit by bounces only | Too bright: the receivers take the lit front's light |
| Lit back | In shadow | Lit directly by a lamp behind the slab | Too dark: the receivers take the dark front's light |

Conditions on the scene:

- The slab is a closed block, deeper along the view ray than 10 % of its distance from the eye. So the 10 % rule and the real slab differ.
- The camera sees receiver surfaces whose rays meet the slab's hidden faces: the floor behind the block seen over it, and a wall beside it. Mark them as regions by their faces, as `reference.md`, "The regions", does.
- One floor patch far from the block is the neutral region.
- The room is closed, with no sun and no Earth. So the missing Earth light (`t1-tbz9`) is not in the comparison.
- The lamps are rectangle lamps or spot lamps with a shadow. The surfaces are light gray and diffuse, and one receiver is glossy, so both ray kinds show.

## The columns

The same as the reference experiment's, from the same probe (`refs/scratch/ter-kh4g-2-ref-controls`):

| Column | Hit light |
|---|---|
| `ten` | The 10 % rule |
| `layer` | The fade over the real slab: the design |
| `layerfade` | The fade over at most 10 % |
| `dfront` | No fade by the entry share |
| `dmiss05`, `dmiss15` | The known-light rule with the miss light |
| `dblack05`, `dblack15` | The known-light rule with black |

The path trace comes from the probe's export and `stage-2/tools/cycles_scene.py`: two seeds of 2,048 samples per crop, no denoiser.

## What to report

- The ratio of each region to the neutral region, per column and view, beside the path trace's, as `tools/ratios.py` gives it. Add the frame noise of each ratio.
- Which column is nearest in each cell, in these two views and in the two views of the reference experiment together.
- A plain statement: is any rule nearer than `layer` in every cell of all four views? If one is, say so first, with its crops beside the path trace.
- The engine's floor against the path trace's in the neutral region. If they differ by a factor again, say how much of each column's gap that factor explains.
- One image per view: the columns side by side, then the path trace, as `tools/sheet.py` makes it.

## Settled decisions

- The scene is throwaway: a patch and a scratch ref. The branch gets documents, images, scripts and small tools only.
- A still is frame 239 of a film at 1920 × 1080 with `--bloom off` and one fixed exposure per view.
- Renders on the Mac take `tools/gpu-lock.sh` in shared mode. Plan the stills on the Mac, because cloud stills jobs of these binaries timed out (`t1-lw1f`). The cloud budget of this ticket is $0.20.
- A paragraph that starts with "Reading:" holds interpretation. Every other sentence states a measurement.
- Do not change the design or the build tickets. The line orchestrator passes a better rule up with its crops.

## Deliverables

- `docs/research/back-face-depth-layer/stage-2/fade-lit-front.md`, with the tables, the images, the commands, and what was not measured.
- The patch of the throwaway scene and the scratch ref.

## Acceptance

- Both views have their ratios beside a path trace, with the regions named by their faces.
- The record states whether any rule is nearer than the fade over the real slab everywhere.

## Out of scope

A change of the fade in the engine.


## Notes

**2026-11-22T15:07:20Z**

2026-11-22, the line orchestrator. A fact from ter-ukw6's measurement (docs/research/back-face-depth-layer/sill-hits-on-main.md, part 2): with the layer, the real hits on a thin sunlit sill read about half of their light of before (6,356,278 to 3,247,583 over the same draws), and those on the floor patch 15 % less. The record's reading, not measured: the fade is edgeFade * (1 - entryShare), and the same depth is a larger entry share of a slab of 0.5 m than of a slab of 3.95 m. This is the rule that this ticket examines.

**2026-11-22T15:10:50Z**

2026-11-22, the line orchestrator. The deferral ends by the orchestrator's order of today: the user wants the slab right wherever the walk uses it. The facts that changed since this ticket was written:

- The back-face depth layer is on main since 2026-11-22 (main 84802ed66). The column `layer`, the fade over the real slab, is what main ships. The probe of the columns is still the scratch ref refs/scratch/ter-kh4g-2-ref-controls, on its old base. Add one more column, `main`: main 84802ed66 itself, with no probe. It must agree with `layer` within the frame noise. If it does not, say so first, because then the probe's `layer` is not what ships.
- The judge is the eye first. The user's bar is a good look with no visible bug, and a path trace is a tool to explain a difference. So each view's sheet of the columns comes first in the record, with a plain sentence per column on what an eye sees: light of the lit front on the receivers behind the block, a dark stain, a hard step, or nothing. The ratios beside the path trace come second.
- A fact from ter-ukw6 (docs/research/back-face-depth-layer/sill-hits-on-main.md, part 2): on a thin sunlit sill, the real hits read about half of their earlier light with the layer. The record's reading is that the same depth is a larger entry share of a thinner slab. That is lost light, which the bar accepts. It is context for the reading of the columns, not a third view.
- A fault that shows over time (a flicker, a crawl, a fade) goes into one side-by-side video: `layer` on the left and the other column on the right, real frames at 60 a second, one fixed exposure, H.264 at CRF 12 or lower. A fault that stands still stays a sheet of stills.
- If another rule looks better than `layer`, that is a fork for the user. The record shows it with its pictures. Nobody builds it in this ticket.
- The path trace: two views, as the ticket says. The trace machinery is stage 2's. If it does not run within about two hours of work, report the views with the engine's columns alone and name what stopped the trace.
- Cloud money: $0.20, as the ticket says. The deliverable's place and the rest stay as written.

**2026-11-22T15:14:31Z**

2026-11-22, the line orchestrator. The ticket lead is launched. taskId and child thread: node/thread:delegated-task:command%3Amcp%3A1606afbd-dec8-4bf8-a33c-bc8fce97f948%3Adelegate-task%3Aslabs-3-t1-r9vj-lead-1. clientRequestId: slabs-3-t1-r9vj-lead-1. Target: claudeAgent, claude-opus-5-5, effort high, role implementation, isolation worktree. Base and review base: main 906d1c260. Cloud money: $0.20. No reviewer stage. The lead waits with builds and renders while the Mac's load is above 40 (it was 173 at the launch, from other work).

**2026-11-22T16:25:02Z**

2026-11-22, the line orchestrator. By the orchestrator's order, this take gives way to the wobble fix ter-qt72: its lead starts no build, no render and no path trace until that fix is in its review. The lead is not cancelled.

---
id: t1-6ucc
status: open
deps: []
links: []
created: 2026-11-15T13:06:42Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-kh4g
tags: [rendering]
---
# A mesh's record says whether the mesh is closed, and the batcher splits the opaque runs for the back-face pass

Step 2 of the design `docs/plans/back-face-depth-layer.md`. Read that document first: its terms, decisions 4 and 5, and "The interfaces". This ticket gives the back-face pass what it needs to know of the scene, and changes no frame.

## System design

The back-face pass draws the back faces of a closed mesh and both faces of everything else. So two facts must reach it: whether a mesh is closed, and whether an instance's material is two-sided.

| State | Writer | Content |
|---|---|---|
| Whether a mesh is closed | The mesh's record at upload (`StagedMesh`, then the mesh record of `GeometryStore`) | One required flag, computed once |
| The pass's runs of a depth range | `SceneBatcher` | The same indirect commands as the G-buffer's runs, split by what the pass draws |

What causes what: an upload computes the flag once. The batcher reads the flag and the material's side for each instance, and its sort puts the instance into a run. The pass later reads the runs. No other part holds a list of closed meshes.

A deformed copy of a mesh keeps the flag of its mesh: a deformation moves vertices and keeps the triangles.

## Signatures

```csharp
/// <summary>Whether a mesh bounds a volume: after a weld of its positions, every directed edge has as many
/// triangles as its reverse edge, and the mesh has at least one triangle that is not degenerate.</summary>
internal static class MeshClosure
{
    public static bool IsClosed(ReadOnlySpan<uint> indices, ReadOnlySpan<Vector3> positions);
}
```

```csharp
/// <summary>The opaque runs of one depth range, as the back-face pass draws them.</summary>
/// <param name="ClosedOneSided">The instances of a closed mesh with a one-sided material: the pass draws their back faces.</param>
/// <param name="BothFaces">The instances of a two-sided material or of an open mesh: the pass draws both faces.</param>
internal readonly record struct BackFaceRuns(OpaqueRuns ClosedOneSided, OpaqueRuns BothFaces);
```

`PreparedScene` gives the `BackFaceRuns` of each depth range beside `Opaque[range]`. Each `OpaqueRuns` keeps its `Solid` and `Cutout` command ranges. The mesh record gains `bool Closed` as a required member with no default.

How the sort key carries the split is yours. One way is a third key bit for an open one-sided mesh, below the two bits of today. The G-buffer pass must find its runs by the material's side as today, with every instance drawn once.

## Settled decisions

- The weld has a tolerance: two positions within it are one vertex. A grid alone leaves two near positions apart across a grid line (stage-2/cards.md, "Notes for the design"). Choose the tolerance from the mesh's extent and float precision, and give its reason in the code.
- The pairing rule decides, not "exactly two triangles per edge". The station's spokes, chute, chute brake and body and the ship have edges that four triangles share in opposite pairs. They are closed.
- A triangle with two equal welded corners does not count.
- The probe's `MeshClosure` is in `docs/research/back-face-depth-layer/stage-2/probe-cards.patch`. It is a starting point, not the code.
- The probe's tool `--probe-meshes` is not part of this ticket. A test replaces it: no mesh of a scripted demo scene that draws one-sided and opaque is open.

## Tests first

- `MeshClosure`: a box of 24 vertices is closed. A single quad and a box less one face are open. A UV sphere with split pole vertices is closed. A mesh with an edge of four triangles in opposite pairs is closed. A mesh with only degenerate triangles is open. Two positions within the tolerance weld, also across a grid line.
- The batcher: an instance of a closed mesh with a one-sided material lands in `ClosedOneSided`. A two-sided material and an open mesh land in `BothFaces`. Cutout and solid stay apart in both. The union of the pass's runs is the union of the G-buffer's opaque runs, in each depth range.
- The frame: the G-buffer's draws do not change. Every snapshot test stays as it is.

## Acceptance

- Every test of `tests/Tertius.Rendering.Tests` passes in Checked and in Release with no reference image approved again.
- Coverage and the mutants of the changed lines follow CLAUDE.md, "Testing". The table is in the ticket lead's report.
- ARCHITECTURE.md names the flag, its owner and the pass's runs.

## Out of scope

The pass itself, its shaders and its target.


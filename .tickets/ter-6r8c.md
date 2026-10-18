---
id: ter-6r8c
status: closed
deps: [ter-k8cg]
links: []
created: 2026-10-17T21:44:05Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-ozm8
tags: [frames, physics]
---
# Kepler, Spin and Static motion become components, and every frame follows its owner

Part of the epic on frames that follow their owners. Read the epic and its design doc first (docs/plans/rails-components.md, 'Order of the work', item 2). The driver state moves onto components of the frame's entity, which call the rails library and hand the engine a motion sample each tick. A frame follows its owner for every kind of motion, as it follows a Physics hull today.

Scope:
- The sample table, indexed by the frame's index, with three buffers (written, applied and followed). Self.MoveOnRails, Self.TimeAt and Self.OwnedFrame.
- KeplerMotion and SpinMotion in src/Tertius.Simulation/Components/, with their checks (a KeplerMotion whose parent has no G·M holds its frame still and logs once, and a rails component on a body that is not kinematic logs once and writes no sample), the ProperAcceleration field of the sample, and the start of a new frame (it holds still at its spawn pose for its first step, and the first run of the component puts it on its rails).
- FollowOwners in step 2 of the sync point, and the end of step 6b (AdvanceDrivenFrames).
- Physics reads the samples for targets and predictions, and snaps bodies on rails to their targets. DriveKinematicHulls, PredictMotion and OrbitMatchesTree go.
- The orbit frame follows its anchor's samples, and NumericBody.Orbits reads 'the hull is kinematic'. The tree's orbit and spin, PlaceOnOrbit, MoveOnOrbit, KeplerMotion, SetFrameOrbit and SetFrameSpin go.
- Static hulls take teleports, and owners without a body write their transforms.
- One motion record per frame (user decision 2026-10-18, design section "One motion record per frame"): every frame takes a MotionSample step record in the one loop FollowOwners, whoever fills it: physics releases it for every hull (dynamic, kinematic from the sample it applied, static teleport), and an owner without a body writes it (a rails sample, or a transform write at the sync point). FrameState goes, physics predicts every frame from its step record, the tree keeps the instant velocity for every frame, and the followed buffer holds the record of every frame that moved.
- The scenes, the benchmark (1,000 Kepler frames without bodies in WorldTickBenchmarks, with the time of the tick and of the sync point before and after), and the sections of ARCHITECTURE.md that this changes.
- In this ticket the enum stays only to choose a hull's motion, so Kepler and Spin both mean a kinematic hull. Until ter-ru5u, a change of motion is SetFrameDriver plus a swap of components with World.AddComponent and World.RemoveComponent.
- Tests: the changes in the doc's section 'Tests' that concern samples, components and checks, and the new tests 1 to 7 and 9. The tests about drivers and state dumps belong to ter-ru5u, and new test 8 to ter-zg77.

## Notes

**2026-10-18T13:13:11Z**

Before the merge (user decision 2026-10-18): change the words in ARCHITECTURE.md and docs/plans/rails-components.md. A frame and its owner are one entity with two tables: the frame tree row is the exact table (doubles: pose in the tree parent, velocity, angular velocity, proper acceleration) and the transform row is the float view of the same entity. Replace 'the owner mirrors its frame' and similar wording with 'the entity's float view is filled from its exact table'. The user rejected widening TransformRow to make it the official store (about 36 bytes per row, reverses the one-float-vector decision, the tree keeps its own copy anyway).

**2026-10-18T17:37:35Z**

Benchmark of 1,000 Kepler frames without bodies (KeplerFrameTickBenchmarks in benchmarks/Tertius.Benchmarks/WorldTickBenchmarks.cs), before and after the rails components. Command: `dotnet run -c Release --project benchmarks/Tertius.Benchmarks -- --filter Tertius.Benchmarks.KeplerFrameTickBenchmarks.Tick` (the short in-process job of Program.cs). Apple M1 Max, 10 cores, .NET SDK 11.0.100-rc.1, with other agents' builds on the machine. Each side ran twice, in the order after, before, before, after. The two runs of each side agree within 2 percent for the tick and within 7 percent for the sync point.

- Before: commit 0b4dde6, the commit that added the benchmark on the old code. The sync point placed each frame on its orbit. Main has no copy of the benchmark, so this commit stands for main.
- After: branch ter-6r8c-rails-components at 3b1952d. Each KeplerMotion solves its orbit in its tick, and the sync point puts each frame on the sample that its component wrote a tick before.
- Tick = the BenchmarkDotNet mean of one World.Tick. Sync point = the mean time of the sync point per tick that the cleanup prints. This mean includes the pilot and warm-up ticks, so the "before" value is a little above the tick mean.

| Workers (MaxDegreeOfParallelism) | Tick before | Tick after | Sync point before | Sync point after | Allocated per tick, before / after |
|---|---|---|---|---|---|
| all (-1) | 365 to 367 µs | 349 µs | 358 to 382 µs | 253 to 258 µs | 2.6 KB / 3.3 KB |
| one (1) | 353 to 354 µs | 460 to 461 µs | 334 to 339 µs | 203 to 204 µs | 0 / 0 |

What the numbers say:

- The sync point is 30 to 40 percent shorter.
- With all workers the tick is about 5 percent faster, because the solve now runs in the parallel chunks.
- With one worker the tick is about 30 percent slower. The part of the tick outside the sync point grew from about 20 µs to about 257 µs. The sync point shrank by about 131 µs. So the solve and the sample in the components cost about 1.8 times what the old placement in the sync point cost.
- The sync point still takes about 200 µs with one worker, about 200 ns per frame, although it only copies samples for these frames.

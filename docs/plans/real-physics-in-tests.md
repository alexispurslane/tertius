# Real physics in every test

Ticket: `ter-se84`. It comes before [test hardening](test-hardening.md), because test hardening measures the tests that this plan rewrites.

## Goal

Every test runs the real code. A test with physics runs the real `World` on the real `JoltPhysicsBackend`. A test without physics runs a `World` with no backend.

The stand-ins go away: `PhysicsHarness`, `FakePhysics`, the NSubstitute physics mock and `ContractBreakingBackend`. A stand-in is a test copy of real code. Each stand-in drifts from the code that it copies, and no test catches the drift.

## Why

A survey (workflow `wf_dba924f7-4b3`, at commit `cb4ecda`) counted where the physics tests run. A skeptic then read the code for each gap that the survey found, and made sure that it holds.

| Project | Tests | What stands in |
|---|---|---|
| Physics | 107 | `PhysicsHarness`, a copy of the World's sync point, drives real Jolt. |
| Physics | 2 | Nothing. The real World runs on real Jolt. |
| Simulation | 136 in Debug, 155 in Release | An NSubstitute mock of `IPhysicsBackend`. About 80 of these tests script the physics output by hand. |
| Simulation | 9 | `FakePhysics`, a hand-written backend |
| Simulation | 2 | `ContractBreakingBackend`, a backend that breaks a contract |

These gaps hold between `PhysicsHarness` and the World:

| Area | `PhysicsHarness` | The World |
|---|---|---|
| A frame owner's state | Keeps the body state, with the velocity of the center of mass | Rewrites the owner's state from the frame state, with the velocity of the frame origin |
| Contacts | Keeps the raw contact | Makes two Collision events. Flips the normal for the second body, and converts the point into each body's frame. Delivers the events on the next tick. |
| Frame destroy | Tests move a child hull through a backend path that the World never takes. | Destroys and recreates the hull of each surviving child frame |
| Kepler frames | Advances them, then flushes the changes | Flushes the changes first |
| A Kepler frame with no orbit | Stays where it is | Jumps to its parent's origin |
| A failed change batch | Throws at the first failure | Records the error, clears the batch and goes on |
| Intents | Accepts an intent for any entity | Throws for an entity without a body |
| Entity handles | Starts at slot 1, generation 1, and never reuses a slot | Starts at slot 0 and reuses slots with a higher generation |
| Threads | Steps on the test thread | Steps on its own thread while gameplay runs |

The order in which a step's output lands matches, but it exists as two copies.

These gaps hold between the fakes and Jolt:

| Area | The fakes | Jolt |
|---|---|---|
| Hand-offs | A script sends a hand-off alone. | Jolt always sends a body state in the new frame with it. |
| A body state in another frame | The World moves the entity with no hand-off. | Jolt never sends this, so a backend bug that sends it passes. |
| Which bodies report | Every body, every step | Awake bodies. A body that comes to rest sends one last report. |
| Frame states | One every tick | No test makes sure that a frame keeps its state on a tick with no report. |
| Rotation | Never reported | Reported after the first spin or collision |
| Velocity | Nothing states which point of the body the velocity belongs to. | The velocity of the origin, which the backend converts from and to Jolt's velocity of the center of mass. See "The velocity point". |
| Change batches | Tests read the lists, never the frame tree that comes with them. | Jolt needs the frame in the tree. |
| A batch that partly fails | Never happens | Can leave some entities without a Jolt body |
| Intents | `FakePhysics` adds impulses first, then sets the velocity. | Jolt sets the velocity first, then adds impulses. |
| Determinism | Proven only on `FakePhysics` | No test runs the World on Jolt twice and compares the results. |
| Allocations | The zero-allocation tests run with no backend. | Never measured with a backend |

Real Jolt costs little. In a Debug build at `07c74f0`, the Physics suite ran 148 tests in 1.4 s. The Simulation suite ran 1,258 tests in 3.8 s.

## Decisions

- Tests always use the real World and the real Jolt backend. No test uses a model of Jolt. (User)
- The only test double for physics is `FaultInjectingBackend`, a wrapper around the real Jolt backend. (User)
- This plan runs first, before test hardening. (User)
- Assertions stay exact. A test compares the World's state with the output that Jolt really sent, which the wrapper records. It does not compare with numbers that the test picked.
- The engine code keeps its module boundary. `Tertius.Simulation` does not reference `Tertius.Physics`. Only the test projects reference both.
- Sonnet agents port the tests in parallel, one test file each, as the user's rule for test-writing agents says. They do not run coverage or mutation testing.

## Design

### FaultInjectingBackend

Fault injection means that a test feeds the code a fault on purpose. The World's rule for bug-only output needs it, because Jolt never produces such output on purpose. The wrapper forwards every call to Jolt. A test can edit the output, or make a call throw. The wrapper is real Jolt plus one edit, not a second model of Jolt, so it cannot drift. A sketch:

```csharp
public sealed class FaultInjectingBackend(JoltPhysicsBackend jolt) : IPhysicsBackend
{
    public JoltPhysicsBackend Jolt { get; } = jolt;
    public List<PhysicsChanges> AppliedChanges { get; } = [];
    public List<PhysicsStepOutput> Outputs { get; } = [];
    public Action<PhysicsStepOutput>? EditOutput { get; set; }
    public Exception? ThrowFromStep { get; set; }
    public Exception? ThrowFromApplyChanges { get; set; }

    public void ApplyChanges(PhysicsChanges changes, FrameTree frames) { ... }
    public void Step(in PhysicsStepContext context, PhysicsStepOutput output) { ... }
    public float BoundingRadius(ShapeDesc shape) => Jolt.BoundingRadius(shape);
    public void Dispose() => Jolt.Dispose();
}
```

where

- `Jolt` = the real backend. Tests read its internals through it, for example `HasGhostIn` and `AssertInvariants`.
- `AppliedChanges` = a copy of every change batch that the World sent, in order.
- `Outputs` = a copy of every step output that the World received, after `EditOutput` ran.
- `EditOutput` = runs after Jolt's step and before the World sees the output. Null means no edit.
- `ThrowFromStep`, `ThrowFromApplyChanges` = an exception that the next call throws instead of calling Jolt. Null means no throw.

### A shared test project

A new class library, `tests/Tertius.Testing`, holds the code that both test projects use:

- `FaultInjectingBackend`.
- Scene builders for common setups. Examples are a floor with a ball, a ship with an interior and a bubble, and a moon on a Kepler orbit. They replace the scenes inside `PhysicsHarness`.
- The test components from `tests/Tertius.Simulation.Tests/Components`, such as `Recorder`.

### What replaces each stand-in

| Stand-in | Tests today | Replacement |
|---|---|---|
| `PhysicsHarness` | 107 Physics tests | The World on Jolt, built by a scene builder |
| The `PhysicsHarness` hooks `BeforeApply`, `ReplaceHandOffs` and `RefusesHandOff` | The tests of bug-only output | `EditOutput` |
| `PhysicsHarness.Reconcile()` | The tests that read the backend between the reconcile and the step | Decided in step 4: an internal backend method, or the same assertion after a tick |
| The tests of `PhysicsHarness` itself | 21 tests in `PhysicsHarnessTests` | Tests of `PhysicsOutputCheck` and of the wrapper. A test that has no reason to stay is deleted, with the reason in the report. |
| The NSubstitute mock as a holder or a call recorder | About 54 Simulation tests | A World with no backend, or the wrapper's `AppliedChanges` |
| The NSubstitute mock as a script of the output | About 80 Simulation tests | A scene that makes Jolt produce the output, or `EditOutput` for bug-only output |
| `FakePhysics` | 9 Simulation tests, 8 of them in `DeterminismTests` | The World on Jolt |
| `ContractBreakingBackend` | 2 Simulation tests | `ThrowFromStep` and `ThrowFromApplyChanges` |

A test that scripted a hand-off now builds a scene that causes one, for example a ball that crosses the edge of a bubble. It then asserts that the World applied the hand-off that `Outputs` holds.

### The velocity point

Jolt keeps the velocity of a body's center of mass, but the pose that it reports names the body's origin. The seam carries the velocity of the origin, the point that the pose names, in every direction (`IPhysicsBackend` in `PhysicsSeam.cs`, ticket ter-jkqr). So the Jolt backend converts at each place where a body velocity crosses the seam:

```csharp
private static Double3 PointVelocity(Double3 velocity, Double3 angular, Double3 offset) =>
    velocity + Double3.Cross(angular, offset);
...
public Double3 OriginVelocity => PointVelocity(Linear, Angular, Pose.Position - CenterOfMass);
```

where

- `PointVelocity` = the velocity of the point of a rigid body at `offset` from a point whose velocity is `velocity`.
- `angular` = the body's angular velocity in frame axes, in radians per second.
- `offset` = the place of the second point relative to the first, in frame axes, in meters.
- `OriginVelocity` = the velocity of the body's origin, from one `BodySnapshot`: what Jolt holds for one body.
- `Linear` and `Angular` = Jolt's velocity of the center of mass and Jolt's angular velocity.
- `Pose.Position` and `CenterOfMass` = the places of the body's origin and of its center of mass in its frame.

The places:

| Place | Direction | What it does |
|---|---|---|
| `Report`, the `BodyState` of each body | Out | `OriginVelocity`. A plain kinematic body that a motion intent aimed reports the record's velocity, which is the origin's already. |
| `ExecuteHandOff`, the `HandOff` | Out | `OriginVelocity` of the body in its new frame |
| `HullRecordOf`, a dynamic hull's step record | Out | `OriginVelocity`, as before: the frame's origin is the hull's origin |
| The creation of a body from a `BodyCreate` | In | Adds the spawn's angular velocity times the offset of the center of mass from the origin |
| `CreateHullFromTree` | In | The same, from the frame tree's velocities, as before |
| `ApplyBodyIntent`, a `SetVelocity` | In | The same, with the angular velocity that the same intent sets, or else the body's angular velocity in Jolt |

A `SetAngularVelocity` alone keeps Jolt's velocity of the center of mass, as an angular impulse does, so the body turns about its center of mass. Every other path inside the backend keeps Jolt's velocity of the center of mass, which never leaves the backend: the moves between systems, the shifts of numeric frames, the growth of a system, the ghosts, the chords and the contacts. A character never turns in Jolt and has its center of mass at its origin, so its velocity takes no conversion.

Before the fix, the World read Jolt's velocity of the center of mass as the velocity of the origin. For a spinning body, it was wrong by the angular velocity times the offset: 1.5 m/s on a body that spun at 0.5 rad/s, with its center of mass 3 m from its origin. The tests are in `tests/Tertius.Physics.Tests/OriginVelocityTests.cs`. A body whose center of mass is its origin, such as a box, reports Jolt's velocity bit for bit.

### One command for both builds

The Release halves of the tests run only in a Release build, and the documented test command builds Debug. A new dev-shell command, `test-all`, runs every test project in Debug and in Release. CLAUDE.md names it as the test command.

## Steps

1. Fix the velocity point test-first. Write a test with an off-center body that spins, and watch it fail. Then convert the velocity in the backend, and state the rule in `PhysicsSeam.cs`.
2. Add the `test-all` command to the dev shell of the flake.
3. Create `tests/Tertius.Testing` with `FaultInjectingBackend`, the scene builders and the shared test components. Test the wrapper first. With no edit, a World on the wrapper must give the same determinism hash as a World on bare Jolt.
4. Decide how the tests of `PhysicsHarness.Reconcile()` read the backend.
5. Port the Physics tests to the World. Give one test file to each Sonnet agent, in parallel worktrees. Each port keeps the test's name and the behavior that it tests.
6. Port the Simulation tests the same way. Move `DeterminismTests` onto Jolt, with more than one gameplay worker.
7. Delete `PhysicsHarness`, `FakePhysics`, the NSubstitute physics mock and `ContractBreakingBackend`. If no other test uses NSubstitute, remove the package.
8. Add an end-to-end test for each behavior that no test runs on the real pair today:
   - A ship hand-off with ghosts.
   - A frame destroy with rehome.
   - A contact that becomes Collision events, with the point and the normal converted into each body's frame.
   - A Kepler hull, a dynamic hull, and a change of motion with `SetMotion`.
   - The growth of a full system.
   - A change batch that partly fails.
   - A destroyed slot that a new entity reuses in the same batch.
   - A game reparent between ticks.
   - Zero allocations per tick in a steady scene on Jolt.
9. Run `test-all`. Every build must have 0 warnings, and every test must pass.
10. Update the testing section of CLAUDE.md.

## Risks

- A ported test can find a real bug, because it now sees real output. Each bug gets a failing test and a fix. A design question goes to the [design review](design-review.md).
- Jolt's global lock lets only one backend step at a time, so parallel test classes take turns. Measure the suite times before and after.
- A scene that must produce one exact event, such as a hand-off at a given tick, takes more setup than a script. The scene builders keep this setup short.

## Report

The report gives:

- The test counts per project and build, before and after.
- Each gap from the tables above, with the test that now covers it.
- Each deleted test, with its reason.
- The suite times before and after.
- Each bug that the port found, with its regression test.

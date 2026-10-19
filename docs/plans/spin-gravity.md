# Spin gravity

Epic: `ter-s9m0`. Rewritten on 2026-10-18 after a design check, with the user's answers to its eight forks and to the three forks of a second check (see "Decisions").

A station can be a semantic frame that turns. Characters and objects inside it then stand, walk, fall, throw and jump correctly, because physics applies the forces of a turning frame. The same forces act in every frame that turns: a ship that faces its planet on its orbit, the ground of a planet that turns with its day, or a ship that physics spins.

This design builds on two pieces of work:

- The motion intent of `ter-xdt8` ([Rails components](rails-components.md), "Rails motion for every entity"). That section came with `ter-xdt8`, and "Terms" below defines the names that this doc takes from it. An owner moves its frame by a motion intent, `PhysicsIntent.Move`, and `FrameTree.StepRecordInStep` gives the step record of the step in flight from the front intents. A spawn can carry the new entity's first motion intent, so that a frame moves on its rails from its first step. The frame tree derives a frame's proper acceleration from its step record (the accelerometer rule).
- The instant velocities of `ter-ediu`, merged on 2026-10-16 (`ARCHITECTURE.md`, "Instant velocities and chords"). A velocity that gameplay sets is an instant velocity, and Jolt gets the matching chord.

The spike on the branch `spike-spin-gravity` showed that Jolt handles a turning frame. Its numbers come from a station of radius 100 m that spins at 0.31321 rad/s, which gives 9.81 m/s² at the floor. A Sonnet reader recomputed every expected value from straight-line motion in inertial space. The spike branched before the rails components, and it found turning frames by a driver enum that is gone. So its math carries over, and its plumbing does not. Its results:

- A dropped crate landed at 0.5607786 s and 0.176458 m antispinward, as the inertial line says, to the last digit.
- While the station spun up, sleeping crates woke and stayed at rest in inertial space. The Euler force matched to 0.01 %.
- A velocity shadow in doubles kept a free body within 1 mm of its inertial line over 10 minutes.
- A person stood for 600 of 600 ticks, walked both ways, and jumped and landed 7 µm from the predicted place.

## The model from scratch

Over one step, a frame moves relative to inertial space in two ways: its origin moves, and its axes turn. The engine already models the first. A body in a frame feels the frame's field minus the frame's proper acceleration, and in a numeric home also minus the home's acceleration. This epic adds the second.

A free body moves on a straight line in inertial space. In axes that turn, that line bends. The rotation terms (Coriolis, centrifugal and Euler) are the shape of the bent line, and nothing more. So one rule gives all three: keep a free body on its inertial line, as the frame's axes see it.

Jolt steps a body with semi-implicit Euler, so it keeps chords. The map from one chord to the next that keeps a free body exactly on its inertial line needs two turns: the turn of the frame's axes over the step in flight, and over the step that ended. It needs no angular velocity and no angular acceleration. That map is the two-turn update.

Cause, storage and effect are three separate things:

- Cause. What turns a frame is what moves it. Its owner moves it by a motion intent, physics moves a dynamic hull, and a teleport jumps it. A frame also turns with every frame above it.
- Storage. Before each step, the frame tree fixes two turns for every semantic frame, in one pass in its dense order: the turn of the step in flight (V1) and the turn of the step that ended (V2). It composes them from double poses and step records. The records of the rails components are exact, so their turns hold no float rounding. The level of a dynamic hull takes Jolt's float angular velocity, which is the hull's real motion. A numeric frame never turns, so it shares its semantic frame's turns.
- Effect. Physics applies the two-turn update to every awake dynamic body and to every character that the ground does not hold. The chord rule turns an instant velocity into a chord with the same turns. The frame tree moves the origin of each falling numeric frame by the same update, so that its members feel only the difference. The effective gravity gains the centrifugal and Euler terms for the readers that need one continuous value: up, the form and the weight of a character, the sleep threshold and the accelerometer rule. The wake test reads the angular velocity and the angular acceleration from V1 and V2 directly (V8).

Rates appear only where a reader needs a continuous value. The angular velocity at the end of a step comes from the step records. The angular acceleration is the change of that angular velocity over the step.

## How this design compares with the first one

The first version of this doc, written on 2026-10-16 from the spike, differed from the model above in these points:

| Question | First design | This design |
|---|---|---|
| The update of a free body | `R² v − (R − I)² r / dt − α × r dt`, which is exact only for a steady spin and adds the Euler term to first order. It claimed the spike's last-digit result, but the spike's code used another formula. | The spike's two-turn update, `x_(n+1) = 2 R_n x_n − R_n R_(n−1) x_(n−1)`. It is exact for any rate and holds the Euler term. It takes no angular acceleration. |
| Where the angular velocity lives | "The frame tree sums it in doubles". No such store exists: the tree keeps a float angular velocity, and `ter-xdt8` removes the last double copy. A float rate gives about 9e-7 m/s² of false centrifugal acceleration at 100 m, about 0.17 m over 10 minutes. | V1 and V2 hold each frame's inertial angular velocity in doubles, beside its turn. `BeginStep` composes both in one pass, from double poses and step records, so no second store sums rates. The rate is exact for a rails record. A dynamic hull's level takes Jolt's float rate, the hull's real motion. |
| The source of the angular acceleration | Three sources: "the change between steps", "only its step record holds it", and the motion sample | One rule for a frame's inertial angular acceleration: the change of the angular velocity in V1 and V2 over a step. Conversions between frames read each level's own record and compose them. |
| Which frames turn | A list of kinds with the old driver names. It left out the Kepler attitude `Spin`, so `earth-surface` and the facing orbit were missing. | Every turning frame: V1 or V2 is not the identity (`FrameTurns.Turns`), with no list of kinds |
| Who fixes the turns | Physics, per system, as in the spike | The frame tree, per frame, in `BeginStep`. Physics and the numeric update read the same numbers. |
| A dynamic hull that turns | Silent | Its turn in the step in flight is predicted from its last record (V3) |
| A ship inside a turning parent | Silent: a rotation force outside `GravityApplied` read as thrust | V4 holds the whole acceleration that the frame gave the body, and the accelerometer rule covers every frame |
| The chord rule in a turning frame | "The engine corrects it everywhere", with no rule | The chord rule and its exact inverse, with the two turns |
| The angular velocity of a free body | Not covered | The angular form of the two-turn update |
| The velocity shadow and the contact turn | Loose fields, no scope, no events that end them | V6, a custom `[Union]`, only in turning frames (fork F8), with Jolt's damping in its expected velocity (fork O3) |
| A turning frame that spawns with its contents | Silent | The spawn can carry the frame's first motion intent, so it turns from its first step, and its contents turn with it (fork O1) |
| The wake on a change of turn | No unit and no threshold. A facing frame on an oval orbit would wake its sleepers in every step. | V8, in m/s², against `WakeAccelerationThreshold` |
| Ghosts | Silent | A chord converts by the frame's poses at both ends of the step |
| The velocity convention | Step 1 of this epic | Done by `ter-ediu`. Its text is in `ARCHITECTURE.md`, "Instant velocities and chords". |
| Conversions between frames | Through the old table of samples | `MotionSample.Converted` through `ConvertIntent`, with `FrameMotion`'s new acceleration terms and `FrameTree.MotionRelativeInStep` |
| `ARCHITECTURE.md` | At the end of the epic, in `ter-n44b` | Each ticket updates its own lines before its merge |

## Terms

| Term | Meaning |
|---|---|
| Step in flight | The physics step that runs beside the current tick: step k + 1 beside tick k + 1. A motion intent written in tick k acts in it. |
| Last finished step | The step whose results the current tick reads: step k during tick k + 1 |
| Turn of a step | How the coordinates of a direction that is fixed in inertial space change in a frame's axes over one step. It is a rotation from the frame's axes at the step's start to its axes at the step's end. A frame that turns by ω for a step of `dt` seconds has a turn by the angle −`dt` times the length of ω, about ω. |
| Turning frame | A frame whose turn of the step in flight, V1, or whose turn of the step that ended, V2, is not the identity: `FrameTurns.Turns`. This one definition decides where physics runs the two-turn update, where the velocity shadow lives, and when it ends. So in the step after a station stops, V1 is the identity, V2 is a turn, and the station is still a turning frame. There is no list of kinds: a spin, a Kepler attitude, a dynamic hull that turns, a parent that turns, or any later writer of motion makes one. |
| Inertial angular velocity | A frame's angular velocity relative to inertial space, in the frame's own axes. The null frame above the roots is inertial space. |
| Rotation terms | The accelerations that a turning frame adds to a body in it: Coriolis −2 ω × v, centrifugal −ω × (ω × r) and Euler −α × r, with ω the frame's inertial angular velocity, α its rate of change, r the body's place and v its velocity in the frame |
| Linear gravity | The effective gravity without the rotation terms: the semantic frame's field at the body's place, minus the semantic frame's proper acceleration, minus the home's acceleration. It is today's `EffectiveGravity`, under a new name, `FrameTree.LinearGravity`. |
| Effective gravity | Linear gravity plus the centrifugal and the Euler term at the body's place (V5). It has no Coriolis term, because it takes no velocity. |
| Two-turn update | The change of a free body's chord over one step that keeps the body on its inertial line (see "The two-turn update") |
| Chord velocity | Jolt's meaning of a stored velocity: the change of a body's coordinates over the last step, divided by the step |
| Instant velocity | The velocity at one moment. A velocity that gameplay sets has this meaning. |
| Semantic place, semantic chord | The place and the chord of a body relative to its semantic frame. In a numeric home they add the home's offset and the home's frame velocity. The rotation terms act on these. |
| Velocity shadow | The double chord and place that the two-turn update gave a body, which the next step continues from while Jolt's float velocity agrees with them to rounding (V6) |
| Contact turn | The turn by the previous step's turn of a velocity change that a contact made, because Jolt applies a contact in the frame's axes at the step's start, while the step's chord is in its axes at the step's end |
| Accelerometer rule | The proper acceleration of a frame is its acceleration minus the gravity at its place, in its own axes (`ter-xdt8`). This epic extends "the gravity at its place" by the parent's rotation terms. |
| Motion intent | What an entity's components say about its motion in the next step, in the field `PhysicsIntent.Move` of its intent: a custom `[Union]` of an end pose, a full sample and a steer target. Tick k writes it, and step k + 1 applies it. ([Rails components](rails-components.md), "Rails motion for every entity", which came with `ter-xdt8`) |
| First motion intent | The motion intent that a spawn description can carry for the new entity: a property of `SpawnDesc` beside `LinearVelocity` and `AngularVelocity`, of the type `MotionIntent`, which `ter-xdt8` adds. It is the intent that the entity would have written in the spawn's tick. A spawn in tick k becomes live at step 6 of that tick's sync point, and step 6 stores the intent in the new entity's front intent, so step k + 1 applies it. A spawn without it gives the entity no motion intent for its first step. |
| Step record | An entity's motion over one step, a `MotionSample`: its pose at the end of the step, its chord and instant velocity, its acceleration, its angular velocity and its angular acceleration. An end pose or a full sample gives the record of the step that applies it. Physics measures the record of a dynamic hull. |
| Step plan | The step record of a following frame's anchor, which the frame tree derives before the step and keeps for that step |
| Chord behind | The chord that matches an instant velocity: the chord of the step that ends at the instant of the velocity. Physics gives Jolt the chord behind, and Jolt's next step then carries it on. Under a steady acceleration `a` and no turn, it is the instant velocity minus `a dt / 2`. |
| Frames without a last turn | The frames whose V2 is not the turn that the chords of their bodies came from: a new frame, a frame whose axes jumped, and every frame after a failed step (see "Frames without a last turn") |

## The two-turn update

A free body moves on a straight line in inertial space, so its places at the ends of three steps satisfy `X_(n+1) = 2 X_n − X_(n−1)`. Seen in the frame's axes, the same line is:

```
x_(n+1) = 2 R_n x_n − R_n R_(n−1) x_(n−1)
```

where

- `x_n` = the body's place at the start of the step in flight, relative to the frame's origin, in the frame's axes at that moment, in metres.
- `x_(n−1)` = the body's place at the start of the step that ended, in the frame's axes at that moment.
- `x_(n+1)` = the body's place at the end of the step in flight, in the frame's axes at that moment.
- `R_n` = the turn of the step in flight, V1. It carries a direction's coordinates in the frame's axes at the start of the step to its coordinates at the end of the step.
- `R_(n−1)` = the turn of the step that ended, V2.

`R_n` carries `x_n` into the axes at the end of the step, and `R_n R_(n−1)` carries `x_(n−1)` there. Jolt keeps the chord `c_n = (x_n − x_(n−1)) / dt`, a difference of coordinates. The spike's code turns the update into the change of the chord over the step in flight (`SpinKinematics.cs` on the branch `spike-spin-gravity`):

```csharp
internal static Double3 FreeChange(DQuaternion step, DQuaternion twoSteps, Double3 position, Double3 chord, double dt)
{
    Double3 previous = position - chord * dt;
    Double3 next = step.Rotate(position) * 2 - twoSteps.Rotate(previous);
    return (next - position) / dt - chord;
}
```

where

- `step` = `R_n`, the turn of the step in flight, V1.
- `twoSteps` = `R_n · R_(n−1)`, the turn over the step that ended and the step in flight together.
- `position` = `x_n`, the body's place at the start of the step in flight, in metres.
- `chord` = `c_n`, Jolt's stored velocity, in m/s.
- `dt` = the length of the step, in seconds.
- `previous` = `x_(n−1)`.
- `next` = `x_(n+1)`, the place at the end of the step on the body's inertial line.
- `Rotate` = turns a vector by a rotation.
- The result = the change of the chord over the step in flight, in m/s, that keeps the body on its inertial line.

For a steady spin at ω, the result is `(−2 ω × c − ω × (ω × x)) dt` to first order in `dt`. A rate that changes between the two steps adds `−α × x dt`. The update holds all three rotation terms exactly, at any step length, and the free part of the motion drifts by nothing but rounding. Physics adds the linear gravity times `dt`, in doubles, and narrows the sum once. The cross term between the linear gravity and the turn is of order ω g `dt`²: 2e-7 m/s per step on the ground of Earth, and zero in a station without a field.

The spike's `SpinKinematics` was a static class. The user's rule puts every function in the struct that it belongs to, so the update becomes an instance method of the struct that holds the two turns:

```csharp
internal readonly record struct FrameTurn(DQuaternion Turn, Double3 AngularVelocity)
{
    public static FrameTurn None => new(DQuaternion.Identity, Double3.Zero);
    public bool IsIdentity => Turn == DQuaternion.Identity;
    public Double3 ChordBehind(Double3 position, Double3 instant, double dt) { ... }
    public Double3 InstantAtEnd(Double3 position, Double3 chord, double dt) { ... }
    public Double3 ChangeBehind(Double3 change) => Turn.Conjugate().Rotate(change);
    public Double3 AngularChordBehind(Double3 instant, double dt) { ... }
    public Double3 InstantAngularAtEnd(Double3 chord, double dt) { ... }
    internal Double3 AngularChordBehindInertial(Double3 inertial, double dt) { ... }
    internal Double3 InertialAngularAtEnd(Double3 chord, double dt) { ... }
}

internal readonly record struct FrameTurns(FrameTurn Step, FrameTurn Last)
{
    public bool Turns => !Step.IsIdentity || !Last.IsIdentity;
    public Double3 FreeChange(Double3 position, Double3 chord, double dt) { ... }
    public Double3 AngularChordAhead(Double3 chord, Double3 change, double dt) { ... }
    public Double3 AngularAcceleration(double dt) => (Step.AngularVelocity - Last.AngularVelocity) / dt;
    public Double3 Centrifugal(Double3 position) => -Double3.Cross(Step.AngularVelocity, Double3.Cross(Step.AngularVelocity, position));
    public Double3 Euler(Double3 position, double dt) => -Double3.Cross(AngularAcceleration(dt), position);
    public Double3 Coriolis(Double3 velocity) => -2 * Double3.Cross(Step.AngularVelocity, velocity);
}
```

where

- `FrameTurn` = the turn of one step and the inertial angular velocity at that step's end, in the frame's axes. It has no case tag: the identity turn means no turn. Its default is not `None`: the default `DQuaternion` is (0, 0, 0, 0), not the identity (`src/Tertius.Core/Math/DQuaternion.cs`, lines 7 to 9). So `IsIdentity` is false for it, and `Rotate` gives zero vectors. Every store of a `FrameTurn` fills its slots with `None` (see V1).
- `FrameTurns` = the two turns that a reader in the step in flight needs: `Step` is V1 and `Last` is V2.
- `ChordBehind` and `InstantAtEnd` = the chord rule and its exact inverse (see "The chord rule and the two conversion rules").
- `ChangeBehind` = the linear part of `ChordBehind`. It takes a velocity change that something makes at the start of the step in flight, such as an impulse, in the frame's axes at that moment. It gives the change of the chord of the step that ended that has the same effect, so that the two-turn update carries the change by `R_n` alone (see "Impulses"). Physics calls it on V2.
- `AngularChordBehind`, `InstantAngularAtEnd` and `AngularChordAhead` = the angular form below. `AngularChordBehindInertial` and `InertialAngularAtEnd` = the same pair in the body's inertial angular velocity, which the angular pair and `AngularChordAhead` share: Jolt's angular chord over the step is the rotation vector of `exp(Ω dt) · R`, over `dt`, with Ω the body's inertial angular velocity at the step's end, in the frame's axes then. `change` = the change of the inertial angular velocity that an angular impulse makes at the start of the step, in the axes at that moment, or zero.
- `Turns` = whether physics must run the update. With both turns the identity, the result is zero to rounding, so physics skips it, and frames that do not turn keep today's results bit for bit.
- `AngularAcceleration` = the frame's inertial angular acceleration, the mean over the step in flight, in rad/s². The time derivative of a frame's own angular velocity is the same in its axes and in inertial axes, because ω × ω = 0, so the change of the components in the frame's axes is the right value. It includes the cross terms of a chain. A station that spins at 0.31321 rad/s about an axis at right angles to a facing orbit's turn of 1.1e-3 rad/s gets 3.4e-4 rad/s², which is 0.034 m/s² of Euler acceleration at 100 m.
- `Centrifugal`, `Euler` and `Coriolis` = the continuous rotation terms at the end of the step in flight, for the readers of one value (V5, the weight of a character and the accelerometer rule).

### The angular form

Jolt's angular velocity is an angular chord too: the rotation vector of a body's turn over the last step, in the frame's axes, divided by the step. Jolt turns a body by the exact rotation of that vector. A free body keeps its angular velocity in inertial space, because Jolt applies no gyroscopic torque, so in the frame's axes that vector turns each step. The rule, with `exp` the rotation of a rotation vector and Ω the body's inertial angular velocity:

```
exp(Ω_J,n dt) = R_n · exp(Ω_n dt)
```

where

- `Ω_J,n` = the angular velocity that Jolt keeps for the step in flight, in the frame's axes.
- `Ω_n` = the body's inertial angular velocity in the frame's axes at the start of the step in flight.
- `R_n` = V1's turn.

`AngularChordAhead` takes Jolt's chord of the step that ended, recovers `Ω_(n−1)` by the inverse with `R_(n−1)` (V2), turns it into the axes at the start of the step in flight by `R_(n−1)`, adds `change`, and applies the rule with `R_n`. The spike's `SpinAngularChord` computed the same rule to second order in `dt`, from a rate at the start of the step and the rate one step earlier. That earlier rate is not in V1 or V2. The form with the two turns needs no rate and is exact, so it replaces the spike's expansion. The tumbling test checks it against the spike's numbers. The rigid form of a character has locked rotation, so it gets no angular part.

## Values

Each value names its analogue, its store, how it changes, and a table of readers and writers with the step that each needs. "Step k + 1" is the step in flight beside tick k + 1. "Step 2" and so on are the steps of the sync point after it.

### V1, the turn of the step in flight

`FrameTree.StepTurn(FrameHandle frame)` returns a `FrameTurn`: the turn of the step that `BeginStep` last prepared, and the inertial angular velocity at that step's end, in the frame's axes.

- Analogue: `FrameTree.stepAcceleration` and the step plan of a following frame (`ter-xdt8`). `BeginStep` fixes both for the step in flight before it runs.
- Store: the frame tree, one `FrameTurn` per frame. A numeric frame reads its semantic frame's value. `FrameTree.Grow` fills the new slots of V1 and V2 with `FrameTurn.None`, because `Array.Resize` fills them with the default, which holds a zero quaternion. `FrameTree.Create` resets V1 and V2 of its slot to `None`, so a reused slot keeps nothing of a destroyed frame.
- The named reason to store it for the step in flight: it needs each level's pose at the start of the step, which step 2 moves, and an end pose needs its start pose, as the step plan does. Physics and the numeric update read it many times in each step.
- Change: `BeginStep` writes it in `PrepareTick` of tick k + 1. From then until the end of step k + 1 it describes the step in flight. From step 2 of the sync point after step k + 1 until the next `PrepareTick`, it describes the last finished step.
- V1 and V2 describe a frame's axes against inertial space, so a change of the tree parent at step 5 or 6c leaves them valid.

`BeginStep` is today's `FrameTree.BeginNumericStep`, renamed because it now prepares every frame. It takes the front intents, the length of the step, and whether the last step failed: `BeginStep(ReadOnlySpan<PhysicsIntent> stepIntents, double step, bool lastStepFailed)`. The World knows the last flag from the sync point, which gets the step's exception (`World.Sync`, `physicsError`). `BeginStep` visits the frames in the tree's dense order, parents first. For each frame it copies V1 into V2, unless the frame has no last turn (see "Frames without a last turn"), and then composes V1 from its parent's V1 and its own level:

```csharp
FrameTurn parentTurn = StepTurn(TreeParent(frame));   // FrameTurn.None above a root
DQuaternion turn = parentTurn.IsIdentity && start == end
    ? DQuaternion.Identity
    : end.Conjugate() * parentTurn.Turn * start;
Double3 angularVelocity = end.Conjugate().Rotate(parentTurn.AngularVelocity + levelAngularVelocity);
```

where

- `parentTurn` = the tree parent's V1. A numeric tree parent gives its semantic frame's V1, and the null frame gives no turn.
- `start` = the frame's rotation relative to its tree parent at the start of the step in flight.
- `end` = the same rotation at the end of the step in flight.
- `levelAngularVelocity` = the frame's angular velocity relative to its tree parent at the end of the step, in the parent's axes.
- `turn` = V1's turn. The product acts right to left on coordinates: `start` carries them from the frame's axes at the start into the parent's, `parentTurn.Turn` carries them to the parent's axes at the end, and `end.Conjugate()` into the frame's axes at the end. When no level of the chain turns, the turn is exactly the identity, so a frame that does not turn is not a turning frame, to the bit.
- `angularVelocity` = V1's inertial angular velocity, in the frame's axes at the end of the step.

Each kind of level gives its three inputs (fork F1, design B). `BeginStep` chooses a level's row by two reads, as `FollowOwners` chooses the follow kind: the hull's motion in the step, `FrameTree.HullMotionInStep(frame, stepIntents)`, and the case of the owner's front motion intent. It never reads the stored `FrameTree.HullMotion`, which describes the last finished step: a hull that a `SetMotion` makes kinematic for the step in flight takes the row of its motion intent, not the prediction of a dynamic hull.

| Level | Chosen when | `start` | `end` | `levelAngularVelocity` |
|---|---|---|---|---|
| A frame whose owner has an end pose or a full sample | The owner's front motion intent holds one of those cases, also a first motion intent that step 6 stored for a new frame. Its hull is kinematic in the step, or it has no hull. | The tree pose, or the teleport pose of the same intent | The rotation of the step record from `FrameTree.StepRecordInStep` | The record's angular velocity |
| A frame with a dynamic hull | `HullMotionInStep` gives `Dynamic`, with or without a steer target | The tree pose, or the teleport pose | `start` turned about the parent's axes by the predicted angular velocity times the step, as Jolt turns a body by its angular velocity after the velocity update | The predicted angular velocity: the row's angular velocity plus V3 times the step (fork F3, design A). It holds Jolt's float rounding. |
| A frame that holds still or teleports | Any other frame: a static hull, a kinematic hull without a motion intent, a frame without a body and without a motion intent. A new frame whose spawn gave no first motion intent holds still in its first step, because its components first write a motion intent in the tick beside that step (see "Frames without a last turn"). | The tree pose, or the teleport pose | `start` | Zero |
| A numeric frame | Always | The identity | The identity | Zero |

A transform write of a tick is in the tree pose by `PrepareTick`, so it needs no row of its own. Every pose here is a double. The records of the rails components are exact, so the turn of a rails level holds no float rounding of a rate. A dynamic hull's level takes Jolt's float angular velocity.

A dynamic hull's steer target and a `SetAngularVelocity` give the hull velocities at the start of the step, which the prediction does not read. So under fork F3 they reach the hull's contents one step late, as the hull's proper acceleration does.

The predicted rate of a dynamic hull gets no dead band, for a named reason: the frame's axes turn by exactly what the hull's record says, because Jolt turned the hull by that rate, and its contents must feel that turn. A dead band at the float resolution of the rate, as `AboveResolution` gives the proper acceleration, would not catch the noise of Jolt's solver anyway, which lies far above that resolution. So an awake dynamic hull with solver noise in its angular velocity is a turning frame: its contents run the update, and their results differ from today's by rounding. A hull that sleeps has a zero angular velocity in Jolt, so its contents keep today's results to the bit.

#### Frames without a last turn

V2 must be the turn that the chords of a frame's bodies came from. Some frames have no such turn. The frame tree keeps a list of them, the frames without a last turn. `BeginStep` gives each listed frame its new V1 as its V2, so that the step assumes a steady turn: no Euler term, and no change of turn for the wake test. A frame enters the list by four causes:

| Cause | Who lists the frame | Why it has no last turn |
|---|---|---|
| A new frame | `FrameTree.Create` | It had no step |
| A teleport that turns a level: the owner's front intent teleports it to a rotation that is not its tree rotation | `BeginStep`, when it reads the level's `start` | The frame's axes jumped against inertial space at the start of the step, so V2 is in axes that no longer exist |
| A transform write that turns a frame without a hull | `FollowOwners` at step 2, which tells the write apart already (`GameplayWroteTransform`) | The same jump, between the end of the step and the next `BeginStep` |
| A failed step | `BeginStep`, for every frame, when `lastStepFailed` is true | Its results were dropped. Frames without a hull turned by V1 at step 2, while the chords of the bodies may or may not come from it. |

A frame below a listed frame is listed too, in the same pass, because its axes jumped with its parent's. A move that does not turn the frame leaves its axes as they were, so it lists nothing. A listed frame leaves the list at the `BeginStep` that gives it its new V1 as its V2. Destroying a frame drops it from the list. The analogue is physics' list of the rigid bodies created since the last step, whose spawn velocities the next step converts.

A jump moves a frame and its contents together, as a spawn places them, so the step after a jump has the history of a new frame. Two other rules were weighed and dropped. Turning V2 by the jump keeps the parent's angular acceleration, but it gives a false one when the jump tilts the frame's own spin axis: about 27 rad/s² for a tilt of 90° of the spike's station at 60 Hz. Keeping V2 after a failed step reads the change of two steps as the angular acceleration of one. A jump can still wake the frame's sleepers through the wake test, because ω at the last wake (V8) is in the old axes. A jump moves the sleepers in inertial space, so that wake does no harm.

A new frame leaves the list at its first `BeginStep` (fork O1, design C). So V2 of its first step is its first V1, and that step assumes a steady turn. What its first V1 holds depends on its spawn:

- A spawn that carries a first motion intent (see "Terms") puts the frame on its rails, turning, from its first step. Step 6 stores the intent in the owner's front intent, so the first `BeginStep` reads it, and V1 of the first step is the real turn. The rule of a new frame makes V2 equal to it. So contents spawned at rest on the floor of a turning frame start out turning with it, with no spin-up: crates spawned at rest on the floor of the spike's station stay on the floor from step 1 and press on it with m ω² r, with no Euler term and no wake. The station scene `spin-station` and the acceptance tests give the station its first motion intent.
- A spawn without it holds still for its first step, because its components first write a motion intent in the tick beside that step. Its first V1 is the identity, and so is its first V2. In its second step V1 holds the full turn against an identity V2, so the frame spins up in one step. That is the physical result of a frame that held still. For the spike's station at 60 Hz, the angular acceleration of that step is ω / `dt`, about 18.8 rad/s², which is about 1,900 m/s² of Euler acceleration at 100 m. Bodies at rest on its floor keep their place in inertial space, as on a real floor that starts to turn at full rate all at once: in the station's axes, the floor slides under them at ω r, 31.3 m/s at the spike's floor. The wake test wakes any of them that sleep.

A dynamic hull needs no first motion intent: physics moves it from its first step, by its spawn velocities. A frame that holds still on purpose, such as a static room, needs none either.

Some readers read V1 and V2 of a frame before its first `BeginStep`. Physics creates bodies in `FlushPhysicsChanges` and at step 6c, before the next `BeginStep`. So the up of a character created in a new frame (`SemanticGravity`, read in `JoltPhysicsBackend.Bodies.cs` at line 368) reads them. So does `EffectiveGravity` read right after a scene build, as `OrbitSliceTests` reads it on the turning ship. They read `FrameTurn.None`, so they get the linear gravity. A test pins this.

| Writer | When | What |
|---|---|---|
| `FrameTree.BeginStep` | `PrepareTick` of tick k + 1 | V1 of step k + 1, for every semantic frame in dense order. Lists a frame whose level a teleport turns, and every frame after a failed step. Takes each listed frame off the list once it has given it its V2. |
| `FrameTree.Grow` | When the tree grows | `None` in every new slot |
| `FrameTree.Create` | At a spawn, and outside a tick | `None` in its slot, and lists the frame among the frames without a last turn until its first `BeginStep` |
| `World.FollowOwners` | Step 2 | Lists a frame without a hull whose transform write turned it |
| `FrameTree.Destroy` | Step 5, and outside a tick | Drops the frame from the list |

| Reader | When | What it reads |
|---|---|---|
| `JoltPhysicsBackend.ApplyGravity`: the two-turn update, the angular form, the contact turn and the scope of the shadow | Step k + 1 | `R_n`, and whether the frame turns (`FrameTurns.Turns`) |
| `UpdateCharacter`: the free and the instant case, the weight | Step k + 1 | `R_n`, ω |
| `InstantVelocity`, `InstantAngularVelocity` and `HandedOffVelocities` | End of step k + 1, after the Jolt update | The turn of the step that ended at that instant (see "The chord rule") |
| `FrameTree.EffectiveGravity` and `SemanticGravity` (V5) | Step k + 1, and at a body's creation | ω, and with V2 the angular acceleration |
| The wake test (V8) | Start of step k + 1 | ω and the angular acceleration |
| `FrameTree.BeginStep`, for the falling-frame origins | `PrepareTick` of tick k + 1, after the semantic frame's V1 | `R_n` |
| `FrameTree.ProperAccelerationFrom`, the accelerometer rule | Step 2 of the sync point after step k + 1 | The parent's V1, then the last finished step |

No gameplay code reads V1 or V2. A tick reads the tree row's angular velocity, which describes the last finished step.

### V2, the turn of the step that ended

`FrameTree.LastTurn(FrameHandle frame)` returns a `FrameTurn`: the turn of the step before the one that V1 holds.

- Analogue: integrator memory, like Jolt's chord. The chord of every body in the frame and the frame velocity of every falling frame in it came from this turn.
- Store: the frame tree, one `FrameTurn` per frame, read through the semantic frame for a numeric frame. `Grow` and `Create` fill it with `None`, as they fill V1.
- Change: `BeginStep` copies V1 into it before it writes V1. A frame without a last turn takes its new V1 instead (fork F2, design B, and "Frames without a last turn").

| Writer | When | What |
|---|---|---|
| `FrameTree.BeginStep` | `PrepareTick` of tick k + 1 | The V1 of step k, or the new V1 for a frame without a last turn |
| `FrameTree.Grow`, `FrameTree.Create` | As for V1 | `None` |

| Reader | When | What it reads |
|---|---|---|
| `ApplyGravity` and `UpdateCharacter`: the two-turn update | Step k + 1 | `R_(n−1)` |
| `ApplyGravity`: the contact turn, and the inverse of the angular form | Step k + 1 | `R_(n−1)` |
| `ApplyBodyIntent` and `ConvertSpawnVelocities`: the chord rule | Start of step k + 1 | The turn of the step that ended at the instant of the velocity, and ω at that instant |
| `ApplyGravity` and `UpdateCharacter`: the change of an impulse (see "Impulses") | Step k + 1 | `R_(n−1)`, through `ChangeBehind` |
| `UpdateCharacter`: the instant case | Step k + 1 | The same, for a jump or for a velocity that `ApplyCharacterIntent` stored |
| `FrameTurns.AngularAcceleration`, for V5, the wake and the character | Step k + 1 | ω at the start of the step in flight |
| `FrameTree.ProperAccelerationFrom`, the Euler term of the accelerometer rule, through `FrameTurns.AngularAcceleration` | Step 2 of the sync point after step k + 1 | ω at the start of the last finished step |
| `FrameTree.BeginStep`, for the falling-frame origins | `PrepareTick` of tick k + 1 | `R_(n−1)` |

### V3, the angular acceleration of a frame's last record

`FrameTree.AngularAcceleration(FrameHandle frame)` returns a `Double3`: the angular acceleration of the frame's step record relative to its tree parent, in the parent's axes, in rad/s². It is the mean over the step: the change of the angular velocity over the step, divided by the step.

- Analogue: the row's angular velocity, `FrameTree.AngularVelocity`, a value of the last finished step that the tree writes at step 2.
- Store: the frame tree, one `Double3` per frame. `ter-xdt8` keeps no angular acceleration in the row and leaves this store to spin gravity.
- Change: at step 2, by `Follow` and `HoldStill`. A move to another parent at step 5 or 6c converts it.
- Every step record means the same by its angular acceleration: the mean over its step. A dynamic hull's record already gives Jolt's change over the step. `KeplerOrbit.Sample` changes: it solves the angular velocity at both ends of the step, where today it gives the angular acceleration at the step's start. `FrameSpin.Sample` gives zero.

| Writer | When | What |
|---|---|---|
| `FrameTree.Follow` | Step 2, from `FollowOwners` | The record's angular acceleration |
| `FrameTree.HoldStill` | Step 2 | Zero |
| `FrameTree.Reparent`, the numeric update | Steps 5 and 6c | Converted by `FrameMotion`'s rule (V7) |

| Reader | When | What for |
|---|---|---|
| `FrameTree.BeginStep` | `PrepareTick` of tick k + 1 | The predicted turn of a dynamic hull's level (fork F3) |
| `FrameTree.MotionRelativeInStep` | Steps 5 and 7b | The predicted angular acceleration of a dynamic hull's level |

The prediction has a lag of one step, the same as the hull's proper acceleration: the contents of a hull that spins up feel the Euler force one step late. A `SetAngularVelocity`, an angular impulse or a steer target on the hull reaches its contents one step late too, as a linear impulse reaches them through the hull's proper acceleration. A test locks this in (see "Acceptance tests").

### V4, the acceleration that the frame gave a body

`BodyRecord.FrameAcceleration`, of the type `AppliedFrameAcceleration(Vector3 Field, Vector3 Turning)`, with the read `Total => Field + Turning`. It replaces `BodyRecord.GravityApplied`. It holds the acceleration that the body's frame gave it in the last step in which physics moved it, in the home's axes:

- `Field` = the linear gravity at the body's place.
- `Turning` = the change that the turning frame's update made to the chord beyond the field, divided by the step: the rotation part of the two-turn update, plus the velocity shadow's take-back of Jolt's rounding of the last step. Zero in a frame that does not turn.

`ComputeProperAccelerations` computes the change of velocity over the step divided by the step, minus `Total`. So every velocity change that is not in `Total` reads as proper acceleration. Each correction has its own reason:

- The contact turn stays out of `Total`. It turns a contact's change into the axes at the end of the step, and the contact cancels it in the same step, so the change of velocity over the step does not hold it. For example, a hull at rest on a station floor gets a contact turn of about ω³ r `dt`, 0.051 m/s² at the spike's floor. Left in `Total`, it would read as a sideways thrust of that size.
- The shadow's take-back joins `Total`, through `Turning`. It is a velocity change that physics makes on purpose, and it can pass the dead band of `AboveResolution`, which is one ulp of the velocity divided by the step, or two ulps of the gravity, per component. Jolt's float update rounds several times in each step, so the error that the take-back undoes can pass one ulp. Left out, the accelerometer would read it as noise above the band.

The cases:

| Body | `Field` | `Turning` |
|---|---|---|
| An awake dynamic body, a rigid character | The linear gravity at its place | The two-turn update's change over the step, and the shadow's take-back |
| An awake dynamic body with a steer target | The linear gravity at its place | Zero: the steering velocity takes the place of the update (see "The flow per kind of entity") |
| A walking character that the ground holds | Zero | Zero |
| A walking character in the air, or one that jumps or gets a velocity | The linear gravity at its place | The rotation part of its case's rule |
| A sleeping dynamic hull | The linear gravity at its centre of mass | The centrifugal and Euler terms there, so that `Total` is V5: the hull rests against them |
| A sleeping body that is not a hull | Not written | Not written |

- Analogue: `BodyRecord.GravityApplied`, which it replaces.
- Store: `BodyRecord`, in physics.
- Change: each step, by physics.

| Writer | When | What |
|---|---|---|
| `ApplyGravity`, per body | Step k + 1 | Both parts for an awake dynamic body |
| `ApplyGravity`, the sleeping hulls | Step k + 1 | V5 at the hull's centre of mass |
| `UpdateCharacter` | Step k + 1 | By the character's case |

| Reader | When | What it reads |
|---|---|---|
| `InstantVelocity` | End of step k + 1 | `Field`, for half a step of the linear gravity. Whether the frame accelerated the body, as today: an awake dynamic body, or a character whose value is not zero. |
| `ComputeProperAccelerations` | End of step k + 1 | `Total`: the proper acceleration of a dynamic hull is its change of velocity over the step divided by the step, minus `Total`, turned into the hull's axes |

With `Total`, a ship on a station floor reads ω² R toward the axis. Its cargo feels that, plus the ship frame's own rotation terms, and the sum is ω² (R + r), which is correct. A ship that holds its radius by thrust reads its thrust, as the demo plan assumes (`docs/plans/blue-danube-demo.md`, line 40).

### V5, the effective gravity

`FrameTree.EffectiveGravity(FrameHandle frame, Double3 position)` returns a `Double3`: the linear gravity plus the centrifugal and the Euler term at the semantic place, in the home's axes, which are the semantic frame's axes. Today it returns a `Vector3`. It changes to `Double3`, as `LinearGravity` returns, so that the sum of its three double terms narrows once, in the reader that needs a float. It takes no velocity, so it has no Coriolis term. `FrameTree.SemanticGravity` gains the same two terms and returns a `Double3` too. The new read `FrameTree.LinearGravity(FrameHandle frame, Double3 position)` returns today's value as a `Double3`. Before a frame's first `BeginStep`, V1 and V2 are `FrameTurn.None`, so `EffectiveGravity` equals `LinearGravity` there.

- Analogue: today's `EffectiveGravity`.
- Store: none. It is derived from the field, the proper accelerations, the home's acceleration and the turns.
- Change: it reads V1 and V2, so it describes the step in flight from `PrepareTick` on, and the last finished step at step 2.
- `EffectiveGravity` becomes internal. Only physics and the frame tree read it, in the step whose turn V1 holds, and no gameplay code reads it today. A public read for gameplay, when one is needed, describes the last finished step under a name of its own.

| Reader | When | What for |
|---|---|---|
| `SwitchCharacterForms` | Start of step k + 1 | The form of a character: near the hub of a station, the centrifugal term is weak, so a person there takes the rigid form |
| `UpdateCharacter` | Step k + 1 | Whether the character is under gravity, and with the Coriolis term its weight |
| `UpdateCharacter` and `Bodies.cs` at a character's creation, through `SemanticGravity` | Step k + 1, and at a spawn | Up |
| `ApplyGravity` and `SleepThresholdFor` | Step k + 1 | The weakest effective gravity on the awake dynamic bodies of a system |
| `ApplyGravity`, the sleeping hulls | Step k + 1 | V4 of a sleeping hull |
| `FrameTree.ProperAccelerationFrom` | Step 2 | The parent's gravity at the frame's place, with the Coriolis term (see "The proper acceleration of a frame") |

| Reader of `LinearGravity` | When | What for |
|---|---|---|
| `ApplyGravity` and `UpdateCharacter` | Step k + 1 | The field part of the update |
| `LinearChord`, `ChordAcceleration`, `HandedOffVelocities` and `InstantVelocity` | Start and end of step k + 1 | Half a step of the field part in the chord rule |
| `FrameTree.BeginStep` | `PrepareTick` | The field part of a falling frame's origin |

`FrameWorld.ForceAcceleration`, which the minimum rebound reads, counts the force of the two-turn update without a change, because it reads the accumulated force.

### V6, the velocity shadow

`BodyRecord.Shadow`, a custom `[Union]`, as `Target` is:

```csharp
internal readonly record struct ShadowTracking(Double3 Chord, Double3 Place, Vector3 JoltTarget, long Step);

[Union]
internal readonly struct VelocityShadow
{
    public VelocityShadow(ShadowTracking tracking) { ... }
    public bool HasValue { get; }
    public bool TryGetValue(out ShadowTracking tracking) { ... }
}
```

where

- The default holds no case: the body has no shadow, and the next update starts from Jolt's velocity.
- `Chord` = the double chord that the last update gave the body, relative to its home, in m/s.
- `Place` = the double place at which that update expects the body at the end of its step, in the home.
- `JoltTarget` = the float velocity that physics expects Jolt to hold at the end of that step, after the force and Jolt's damping. A Jolt velocity that differs from it by more than rounding means a contact.
- `Step` = the index of the step that wrote it. A shadow from any other step than the last one is stale, for a cause that cleared nothing, such as a failed step.

- Analogue: the double frame velocity of a falling frame, which the numeric update carries from step to step beside Jolt's floats.
- Store: `BodyRecord`, in physics.
- Scope: only the dynamic bodies of a system whose semantic frame is a turning frame, `FrameTurns.Turns` (fork F8, design A). Everywhere else it holds no case. It serves bodies with Jolt's damping as well as bodies without, because its expected velocity holds the damping (fork O3, design A, and "Jolt's damping in the shadow" below). This is an exception keyed on turning frames, which the user accepted for its cost: float rounding grows only where the turn of a step is large, and free bodies keep their gaps up to 1,000 m/s without a shadow ([Numeric frames](numeric-frames.md), "Decisions").

| Writer | When | What |
|---|---|---|
| `ApplyGravity` | Step k + 1, after the update of the body | A `ShadowTracking` for the step |
| `ApplyBodyIntent`: a teleport, a `SetVelocity`, an impulse, a steer target | Start of step k + 1 | No case |
| A spawn, a hand-off, a switch of a character's form | When it happens | No case |
| `ApplyGravity`, for a body that sleeps, or for the bodies of a system whose semantic frame is no longer a turning frame (`FrameTurns.Turns` is false) | Step k + 1 | No case |
| `ApplyShifts` and the numeric moves | When a body moves between homes | Carry the shadow into the new home, as they carry the body: `Place` by the offset, `Chord` and `JoltTarget` by the difference of the homes' frame velocities |

| Reader | When | What it does |
|---|---|---|
| `ApplyGravity` | Step k + 1 | If the shadow is from the last step and Jolt's velocity differs from `JoltTarget` by at most 1e-6 times the sum of `JoltTarget`'s length and 1 m/s (the spike's bound for rounding), the update starts from `Chord` and `Place`, and Jolt gets the velocity that also takes back its rounding of the last step. If Jolt's velocity differs by more, a contact changed it: the update starts from Jolt's velocity, with the contact's change turned by `R_(n−1)` (the contact turn). Otherwise the update starts from Jolt's velocity. `JoltTarget` and `Chord` hold Jolt's damping, so a damped body in free motion passes the test too. |
| `ApplyGravity`, for `JoltTarget`, `Chord` and the angular form | Step k + 1 | The body's linear damping and angular damping, from its description: `BodyRecord.Material`, the `BodyDesc` that created the body (`LinearDamping` and `AngularDamping`) |

#### Jolt's damping in the shadow

After it adds the force, Jolt multiplies a body's velocity by `max(0, 1 − c dt)` in single precision, with c the body's linear damping, 0.05/s by default (`BodyDesc.LinearDamping`, `src/Tertius.Core/Seams/PhysicsSeam.cs`, line 254), and its angular velocity by the same factor with the angular damping (Jolt's `MotionProperties::ApplyForceTorqueAndDragInternal`). At 60 Hz the default changes the velocity by 8.3e-4 of its value in each step, far above the bound of the test above. So the expected velocity holds the damping (fork O3, design A):

- `JoltTarget` = the float velocity after the force, times the body's linear damping factor for the step, computed with the same float operations as Jolt: the factor `max(0, 1 − c dt)` in floats, applied after the force.
- `Chord` = the chord of the update, times the same factor in doubles. `Place` = the body's place at the start of the step plus `Chord` times the step.
- The angular form expects, at the end of the step, the angular velocity that it gave Jolt times the angular damping factor, in the same form. Jolt damps the angular velocity before it turns the body, so the angular chord that the next step's `AngularChordAhead` reads holds the damping as the turn that Jolt made.

Physics reads both damping values from the body's description, `BodyRecord.Material`, as the chord sites read them today (`JoltPhysicsBackend.Chords.cs`, lines 72 and 89). The rigid form of a character has zero damping, so its factor is 1. A damped body in free motion then keeps its shadow, and a contact still shows as a difference beyond rounding. Jolt's damping acts relative to the frame's axes, like air that turns with the station, so a damped body leaves its inertial line: its velocity relative to the frame's axes shrinks by the factor in each step. A test pins the damped shadow over many steps (see "Acceptance tests").

### V7, the relative motion of two frames

`FrameMotion` gains the two accelerations. They have default values, so that every `new FrameMotion(...)` of today still compiles during stage 1 of `ter-1bnq` (see "Order of the work"):

```csharp
internal readonly record struct FrameMotion(DPose Pose, Double3 Velocity, Double3 AngularVelocity,
    Double3 Acceleration = default, Double3 AngularAcceleration = default)
```

The rules, from the frame into its parent, for a point at `x` with velocity `v`, acceleration `a`, angular velocity `ω_b` and angular acceleration `α_b` in the frame:

```
a'  = R a + A + α × r + ω × (ω × r) + 2 ω × (R v)
α'  = R α_b + α + ω × (R ω_b)
```

where

- `R` = the frame's rotation into the parent's axes, `Pose.Rotation`.
- `r` = `R x`, the point's place relative to the frame's origin, in the parent's axes.
- `A` = the acceleration of the frame's origin in the parent, `Acceleration`.
- `ω` = the frame's angular velocity relative to the parent, in the parent's axes, `AngularVelocity`.
- `α` = the frame's angular acceleration relative to the parent, in the parent's axes, `AngularAcceleration`.
- `a'`, `α'` = the point's acceleration and angular acceleration in the parent.

The rules back, from the parent into the frame, follow by solving these: with `u = v' − Velocity − ω × r` the point's velocity relative to the frame in the parent's axes, `a = R⁻¹ (a' − A − α × r − ω × (ω × r) − 2 ω × u)` and `α_b = R⁻¹ (α' − α − ω × Ω')`, with `Ω'` the point's angular velocity in the parent. Today's `AngularAccelerationToParent` and `FromParent` only turn the vector, and nothing converts the acceleration of a point.

The new rules need the point's velocity and angular velocity, so they are new methods: `AccelerationToParent(Double3 pointInFrame, Double3 velocity, Double3 acceleration)`, `AccelerationFromParent(Double3 pointInParent, Double3 velocity, Double3 acceleration)`, with the names of the places that `VelocityToParent` and `VelocityFromParent` take, and overloads `AngularAccelerationToParent(Double3 alpha, Double3 angularVelocity)` and `AngularAccelerationFromParent(Double3 alpha, Double3 angularVelocity)`. Stage 1 adds them beside the old one-parameter methods. Stage 2 moves the callers, which `ter-xdt8` edits, and removes the old methods.

The builders:

| Builder | When | From what |
|---|---|---|
| `InstantMotionOf`, `MotionOf` | A hand-off, in step k + 1 | The frame's step record, a single level, with its own acceleration and angular acceleration |
| `FrameTree.MotionRelativeInStep(FrameHandle of, FrameHandle to, ReadOnlySpan<PhysicsIntent> stepIntents, double step)` | Steps 5 and 7b, for `ConvertIntent` | Derived: it composes the motion of every level on the path over the step that the front intents describe, by the rules above, and returns `(FrameMotion Start, FrameMotion End)`. The table below gives each level's motion. |

where

- `step` = the length of the step in flight, in seconds, as `StepRecordInStep` takes it.
- `Start` = the relation at the start of the step in flight: each level at its `start` pose of `BeginStep`'s table (its tree pose, or the teleport pose of the step's intents), with the instant velocity and the angular velocity that its tree row holds, which describe that moment. It has no accelerations.
- `End` = the relation at the end of the step in flight, with both accelerations.

| Level on the path | Its motion at `End` |
|---|---|
| A level whose owner has an end pose or a full sample | Its record from `StepRecordInStep`: the record's pose, instant velocity, angular velocity, acceleration and angular acceleration. An end pose has zero accelerations. |
| A level with a dynamic hull | Its prediction, by Jolt's scheme from the tree row. The acceleration `a` is the hull's proper acceleration turned into the parent's axes, plus the parent's effective gravity and Coriolis term at its place: the accelerometer rule read backward. The angular velocity is the rate of fork F3, the row's angular velocity plus V3 times the step, and the angular acceleration is V3. The pose is the start pose moved by the chord `v + a step / 2` and turned by the predicted rate times the step, with `v` the row's instant velocity. The instant velocity at the end is `v + a step`. |
| A level that holds still | At rest in its tree parent: the `start` pose, with zero velocities and accelerations |
| A numeric level | Its place and velocity after the numeric update of the step: a falling frame moves by its frame velocity plus its step acceleration times the step, as `AdvanceNumericFrames` moves it, and a following frame lands on its step plan. Its instant velocity at the end adds `HomeInstantVelocityOverChord`. Its acceleration is its step acceleration. It turns nothing. |
| A level that dies at step 5 | Its relation at the moment of its death at both `Start` and `End`: its tree pose, with its row's velocities and no accelerations. That is the relation by which `Rehome` converts the rows of its contents at the same moment. The converted intent misses by the frame's motion over one step, the miss that `ter-xdt8` accepts for every conversion. |

`ConvertIntent` converts each part of an intent by the moment that the part describes:

| Part of the intent | Converts by |
|---|---|
| An end pose, a steer target | `End`'s pose: each is a pose at the end of the step |
| A full sample: its pose, instant velocity, angular velocity, acceleration and angular acceleration | `End`, by the instant rule and the acceleration rules above |
| A full sample: its chord | `Start`'s and `End`'s poses, by the chord rule of a conversion (see "The two conversion rules") |
| `SetVelocity`, `SetAngularVelocity` | `Start`, by the instant rule: they act at the start of the step |
| A teleport pose, forces, torques, impulses, a character's walk | `Start`'s pose, as today they convert by the relation that the tree holds |

`MotionIntent.Converted` and `MotionSample.Converted` take the pair: `Converted(in FrameMotion start, in FrameMotion end, double step)`. `ter-xdt8` gives `MotionIntent.Converted` the parameters `(in DPose relative, in FrameMotion motion, Double3 relativeAcceleration)`, and `MotionSample.Converted`, which takes over the body of today's `MotionSampleTable.Convert`, the same. The acceleration of the relation moves into `FrameMotion`, so `relativeAcceleration` goes.

`MotionRelativeInStep` replaces `FrameTree.AccelerationRelative` in every conversion. It gives the linear acceleration of a semantic frame on the path too, which the first design sent to "a ticket of its own", so no separate ticket is needed. `AccelerationRelative` goes when no caller remains. The tree stands at the start of the step in flight at steps 5 and 7b, as `StepRecordInStep` requires.

| Reader | When | What for |
|---|---|---|
| The hand-off, `HandOff.cs` | Step k + 1 | The instant velocity, the angular velocity, and the accelerations of a body that changes frame |
| `MotionSample.Converted`, through `ConvertIntent` | Steps 5 and 7b | A full sample that changes frame with its intent: its pose, its instant velocity and its accelerations by `End`, its chord by `Start` and `End` (see "The two conversion rules") |
| `FrameTree.InSemanticParent`, through `JumpFollowingFrame` and the accelerometer rule | Step 2 | The record that physics released relative to a numeric tree parent, in the semantic parent, through the numeric frames between the two only: its place gains their places (`HomeOffset`), its chord their velocities (`HomeVelocity`), which are their chords of the step, and its acceleration their step accelerations (`HomeAcceleration`), after `AdvanceNumericFrames`. It takes no `FrameMotion` and does not go through `MotionSample.Converted`. Numeric levels turn nothing, so the rotation terms are zero there, and it does not change. |

### V8, the turn at the last wake

`FrameWorld.WakeAngularVelocity` and `FrameWorld.WakeAngularAcceleration`, two `Double3` values beside `FrameWorld.WakeProperAcceleration`: the semantic frame's inertial angular velocity and angular acceleration when the system's dynamic bodies last woke.

- Analogue: `FrameWorld.WakeProperAcceleration`, the proper acceleration at the last wake.
- Store: `FrameWorld`, in physics, per system.
- Change: at the start of each step, by the wake test (see "The wake").

| Writer | When | What |
|---|---|---|
| `ApplyGravity`, the wake test | Start of step k + 1 | ω and α from V1 and V2 of the system's semantic frame, when the bodies wake or the system is new |

| Reader | When | What for |
|---|---|---|
| `ApplyGravity`, the wake test | Start of step k + 1 | The change of the turn since the last wake |

### Which step each value describes

| Moment | V1 | V2 | V3 and the tree row | `stepAcceleration` |
|---|---|---|---|---|
| `PrepareTick` of tick k + 1, `BeginStep` | Written: step k + 1 | Written: step k | Step k | Written: step k + 1 |
| Tick k + 1 and step k + 1 | Step k + 1, in flight | Step k | Step k | Step k + 1 |
| Step 2 of the sync point after step k + 1 | Step k + 1, finished | Step k | Written: step k + 1 | Step k + 1 |
| Steps 5 and 6c | Kept | Kept | Converted for a frame that moves | Kept |
| `PrepareTick` of tick k + 2 | Written: step k + 2 | Written: step k + 1 | Step k + 1 | Written: step k + 2 |

## The flow per kind of entity

One rule per kind. "Terms" means the rotation terms of the system's semantic frame at the body's semantic place.

| Kind of entity | What it gets in a turning frame | Values it reads |
|---|---|---|
| Dynamic body, awake | The two-turn update on its semantic chord at its semantic place, plus the linear gravity, as one force on the locked body. Its angular velocity follows the angular form. The shadow and the contact turn apply. | V1, V2, V6, `LinearGravity`. Writes V4. |
| Dynamic body with a steer target | Physics gives it the steering velocities, which carry it to the target over the step. The steering chord is already the chord of the step in flight in the frame's coordinates, and the steering angular velocity already turns it to the target's rotation in the frame's axes. So the two-turn update and the angular form do not act on it in that step: only the linear gravity is added, as in a frame that does not turn. Without this rule the update would turn the chord ahead a second time. Its shadow ends, as for a `SetVelocity`. In the next step its chord came from the steering of the step that ended, with the turn that is V2 by then, so the update applies again. | V1 and V2 for its scope, `LinearGravity`. Writes V4 with `Turning` zero. |
| Dynamic body, asleep | No force, as today. It wakes by the wake test. | V8 |
| Kinematic body | Nothing. Its motion intent moves it in the frame's coordinates. | None |
| Static body | Nothing | None |
| Walking character | By its case (see "Characters") | V1, V2, V5 |
| Rigid character | The rule of a dynamic body, without the angular part | As a dynamic body |
| Dynamic hull, as a body in its parent | The rule of a dynamic body in its parent's system. Its proper acceleration is its change of velocity minus V4's `Total`. | As a dynamic body |
| Every frame, as a turning frame | Its turn is the composed V1 of its chain | V1, V2 |
| Frame with a dynamic hull, for its contents | A turn predicted from its last record (fork F3) | V3, the row |
| Frame whose hull physics does not move: kinematic, static or without a body | Nothing on the owner. Its proper acceleration comes from the accelerometer rule with the parent's rotation terms (fork F4). | The parent's V1, V2 and V5 at step 2 |
| Falling numeric frame | `BeginStep` gives its origin the two-turn update at the origin's semantic place and chord. Its members feel the terms at their own semantic place minus the home's acceleration, so only the difference. | The semantic frame's V1 and V2 |
| Following numeric frame (an orbit frame) whose anchor has a motion intent in the step | Nothing on the origin: it lands on its anchor's step plan, which is already motion in the semantic frame's coordinates. Its members feel the terms as in a falling frame. | None for the origin |
| Following numeric frame whose anchor has no motion intent in the step | It has no step plan, so it falls as a falling frame. `BeginStep` gives its origin the falling-frame term, as for a falling frame. | The semantic frame's V1 and V2 |
| Resting numeric frame | Nothing on the origin. Its members feel the terms at their semantic place, the frame's offset included. | None for the origin |
| Attached entity, entity without a body or a frame | Nothing. Step 7 derives an attached entity's row, and a motion intent moves an entity without body or frame. | None |
| Ghost | Follows its real body's chord, converted by the frame's poses at both ends of the step. Its angular chord converts the same way: `exp(Ω' dt) = P_end.Rotation · exp(Ω_J dt) · P_start.Rotation⁻¹`, with `Ω_J` the real body's angular chord in its frame and `Ω'` the ghost's (see "The two conversion rules"). | The frame's step record and its tree pose |

## Characters

A character's update has three cases, by what its velocity at the start of the update means. They form a closed set, so they are a custom `[Union]`, `CharacterStartVelocity`, which `UpdateCharacter` builds once after the standing test and reads with a `switch`:

| Case | When | Velocity rule | Jolt's weight input | V4 |
|---|---|---|---|---|
| `HeldStart(Vector3 Velocity)` | The character stands and does not jump | The ground's velocity plus the walk along the ground plane. No term: the ground holds the character against the whole acceleration of its frame, as it holds it against gravity today. | The part of V5 plus the Coriolis term along up, pressed along the ground normal | Zero |
| `InstantStart(Double3 Velocity)` | The character stands and jumps (ground plus walk plus jump along up), or the game gave it a velocity since its last update, less the home's instant velocity past its frame velocity | The chord rule at the start of the step, with V2, and then the two-turn update, with V1 and V2, in one update | V5 plus the Coriolis term; along the ground normal for a jump, which stands | `Field`, and the rotation part of the rule |
| `ChordStart(Double3 Velocity)` | The character does not stand, and nothing gave it a velocity | Jolt's chord plus the two-turn update plus the linear gravity times the step | V5 plus the Coriolis term | Both parts |

The declaration, in the shape of `ter-xdt8`'s `MotionIntent`:

```csharp
internal readonly record struct HeldStart(Vector3 Velocity);
internal readonly record struct InstantStart(Double3 Velocity);
internal readonly record struct ChordStart(Double3 Velocity);

[Union]
internal readonly struct CharacterStartVelocity
{
    private const byte KindNone = 0, KindHeld = 1, KindInstant = 2, KindChord = 3;
    private readonly Double3 velocity;   // the three cases share it
    private readonly byte kind;

    public CharacterStartVelocity(HeldStart held) { ... }
    public CharacterStartVelocity(InstantStart instant) { ... }
    public CharacterStartVelocity(ChordStart chord) { ... }
    public bool HasValue => kind != KindNone;
    public bool TryGetValue(out HeldStart held) { ... }
    public bool TryGetValue(out InstantStart instant) { ... }
    public bool TryGetValue(out ChordStart chord) { ... }
}
```

where

- `HeldStart.Velocity` = the ground's velocity plus the walk, relative to the home, in m/s. It is a float, as Jolt gives the ground's velocity.
- `InstantStart.Velocity` = the character's instant velocity relative to the home's instant velocity, in m/s, in doubles.
- `ChordStart.Velocity` = Jolt's chord of the step that ended, relative to the home, widened to doubles.
- `velocity`, `kind` = the private fields that the cases share. `HeldStart` widens its float to the shared double and narrows it back, which is exact.
- The default holds no case. Only a bug produces it, because `UpdateCharacter` builds a case for every character that it updates, so the `switch` breaks a contract on it.

In a frame that does not turn, each rule reduces to today's: a held character gets no gravity, an instant velocity loses half a step of gravity before it gains a whole step, and a free character gains a whole step.

An impulse on a character is in `PendingVelocityChange`, the change of velocity `Δv` that it makes. In the cases `InstantStart` and `ChordStart`, `UpdateCharacter` adds `ChangeBehind(Δv)`, with V2, to the start velocity before the two-turn update, so that the update carries the change by `R_n` (see "Impulses"). Today it adds the change after the gravity. In the case `HeldStart`, which takes no update, it adds `Δv` as today.

The other rules of a character in every frame:

- Up. Under gravity, up turns against the semantic gravity with the centrifugal and the Euler term, whether the character touches anything or not (fork F5, design A). So a person who jumps across the middle of a station lands upright on the far floor and stands. In a point field the same rule acts. A person who drifts out of a ship in orbit now turns its feet toward the planet, which changes the rule in `ARCHITECTURE.md` that a character in free fall keeps its last up.
- Form. `SwitchCharacterForms` reads V5. Near the hub of a station, a person whose effective gravity is below 0.05 m/s² takes the rigid form. In the spike's station that is within 0.5 m of the axis.
- Standing. The character stands when V5 is at least `FrameTree.ZeroGravityThreshold`, Jolt found walkable ground, and the character moves away from the ground along the ground normal slower than 0.1 m/s. Today the test measures along up.
- Walking. A standing character walks with the part of its desired velocity across up, turned into the ground plane at the same speed (fork F6, design A). On flat ground the normal is up, so nothing changes. On a slope, and on a faceted station floor, the walk follows the face, so the character does not creep after a landing.
- Weight. Jolt reads the system gravity to press a standing character's weight onto its ground. That input is V5 plus the Coriolis term on the character's velocity in the frame. For a standing character, its part along up acts along the ground normal, and the rest is dropped, because the ground holds it. On flat ground under a uniform field, up is against the gravity, so nothing changes.

The spike's weights fit this rule. Walking at 6 m/s, its person weighed 977.6 N spinward and 450.1 N antispinward, against 969.0 N and 442.9 N from `m (ω r ± v)² / r`. Those weights hold the Coriolis term, though the spike passed only the field and the centrifugal term as the weight input: the rest came in through the velocity, which the spike pressed into the floor. In this design the held character's velocity has no term, so the weight input holds the Coriolis term.

## The chord rule and the two conversion rules

### The chord rule

The chord rule turns an instant velocity into Jolt's chord, the chord behind it. Jolt's stored velocity is the chord of the step that ended. So the rule gives the chord, over the step that ends at the instant of the velocity, of the inertial line through the body's state:

```
ChordBehind(x, v)  = (x − R⁻¹ (x − (v + ω × x) dt)) / dt
InstantAtEnd(x, c) = (x − R (x − c dt)) / dt − ω × x
```

where

- `x` = the body's semantic place at the instant, in metres.
- `v` = its instant velocity relative to the semantic frame at that instant.
- `c` = its chord over the step that ended at that instant.
- `R`, `ω` = the turn of the step that ended at the instant, and the inertial angular velocity at the instant, from one `FrameTurn`.
- `v + ω × x` = the body's inertial velocity, in the frame's axes at the instant.
- `R⁻¹ (x − (v + ω × x) dt)` = the place on the inertial line one step earlier, in the frame's axes then.

The two are exact inverses of each other. Physics then subtracts half a step of the linear gravity and of the damping, as today:

| Site | When | The `FrameTurn` | Then |
|---|---|---|---|
| `ApplyBodyIntent`: `SetVelocity`, the rest after a teleport | Start of step k + 1 | V2 | Jolt's velocity is `ChordBehind(x, v)` minus half a step of the linear gravity and the damping. The step's two-turn update, with V1 and V2, then carries it onto the inertial line ahead. |
| The instant case of `UpdateCharacter`: a jump, or a velocity that `ApplyCharacterIntent` stored | Step k + 1, in the character's update | V2 | The same, then the two-turn update, in one update |
| `ConvertSpawnVelocities` | Start of the first step after a spawn | V2 | The same |
| `HandedOffVelocities` | End of step k + 1, after the update | The new frame's V1, the step that just ended | The chord behind the instant velocity in the new frame: the chord of the step that ends at the hand-off, which Jolt's next step carries on. A character that stood keeps its instant velocity as its chord, as today. The next `BeginStep` copies V1 into V2, so the next update reads the same turn. |
| `ApplyBodyIntent` and `ApplyGravity`: an impulse | Start of step k + 1 | V2 | See "Impulses" below |
| `UpdateCharacter`: `PendingVelocityChange` | Step k + 1, in the character's update | V2 | See "Characters" and "Impulses" |
| `InstantVelocity` | End of step k + 1 | V1 | The instant velocity at the end of the step: `InstantAtEnd(x, c)` plus half a step of V4's `Field` and of the damping |

The chord rule followed by the two-turn update gives the chord ahead on the inertial line through the instant state: `(R_n (x + (v + ω × x) dt) − x) / dt`, which is the spike's `FreeChordFromInstant`. The angular form has the same pair, `AngularChordBehind` and `InstantAngularAtEnd`, with the inertial angular velocity `ω_b + ω`. With both turns the identity, physics skips the turn and keeps today's formulas, so frames that do not turn keep their results bit for bit.

In a numeric home the rule acts on the semantic place and velocity, with the same home terms that `LinearChord` and `InstantVelocity` use today (`FrameTree.HomeInstantVelocityOverChord`). The result loses the home's frame velocity of the same step to give Jolt's local chord.

### Impulses

An impulse adds its change of velocity `Δv` to Jolt's velocity at the start of the step, before `ApplyGravity` runs. `Δv` is in the frame's axes at the start of the step. The two-turn update reads Jolt's velocity as a chord of the step that ended, so it would carry `Δv` by `R_n R_(n−1)` instead of `R_n`: a direction error of ω `dt` |`Δv`|, 0.5 % of the impulse at the spike's station. So the change enters as a change of the chord behind, the linear part of the chord rule:

| Impulse | Rule |
|---|---|
| `AddImpulse` on a dynamic body | `ApplyGravity` takes the change that the impulse gave Jolt's velocity, the impulse divided by the mass, out of Jolt's velocity, and puts `ChangeBehind(Δv)` with V2 in its place, before the update. The update then carries it by `R_n`. |
| An angular impulse | The change of angular velocity that it gave, Jolt's inverse inertia times the impulse, leaves Jolt's angular velocity and goes to `AngularChordAhead` as its `change`, which adds it to the inertial angular velocity at the start of the step before the rule with `R_n` |
| A character's `PendingVelocityChange` | `ChangeBehind(Δv)` before the update, in the cases `InstantStart` and `ChordStart` (see "Characters") |

In a frame that does not turn, `ChangeBehind` is the identity, and every impulse acts as today. An impulse also ends the body's shadow (V6), and the accelerometer reads its change as a jolt, as `IntentTests` expects.

### The two conversion rules

A velocity converts between two frames by the rule of its meaning:

| Meaning | Rule | Where |
|---|---|---|
| Instant velocity | By `FrameMotion` at one instant: `v' = R v + V + ω × (R x)` | A hand-off (`InstantMotionOf`), the conversion of a `SetVelocity` by `ConvertIntent` (by `Start`), and a full sample's instant velocity in `MotionSample.Converted` (by `End`) |
| Chord velocity | By the frame's poses at both ends of the step: `c' = (P_end x_end − P_start (x_end − c dt)) / dt` | A ghost (`UpdateGhosts`, `HandOff.cs` at lines 422 and 423), and a full sample's chord in `MotionSample.Converted` |
| Angular chord | By the rotations at both ends of the step: `exp(Ω' dt) = P_end.Rotation · exp(Ω dt) · P_start.Rotation⁻¹` | A ghost's angular velocity, which follows its real body's angular chord |

where

- `P_start`, `P_end` = the frame's pose in the other frame at the start and the end of the step. For a ghost, `P_end` is the frame's step record and `P_start` its tree pose or its teleport pose. For a sample, they are the `Start` and `End` of `MotionRelativeInStep`.
- `x_end` = the body's place in the frame at the end of the step.
- `Ω`, `Ω'` = the angular chord in the frame and in the other frame. `exp` is the rotation of a rotation vector.

Today a ghost converts Jolt's chord with the instant rule at the end point. In a turning frame a chord spans a turn of ω `dt`, so at the spike's floor, at 31.3 m/s and 60 Hz, the ghost's velocity is off by about 8 cm/s in direction.

The reads in a frame, such as `VelocityIn` and `Snapshot.TryGetVelocityIn`, keep the instant rule, which they use today. For a body they read the velocity of the transform row, which is Jolt's chord. So in a turning frame a read in another frame is off by up to ω `dt` |v| / 2 in direction: 8 cm/s at the spike's floor at 31.3 m/s and 60 Hz. The read keeps its rule for a named reason: that error is of the order of the half step of acceleration by which every body velocity that a tick reads lags the instant velocity, 0.08 m/s under 9.81 m/s² at 60 Hz. The chord rule would need the poses at the start of the last finished step, which no read in a tick holds. A read in the body's own semantic frame converts nothing and has no such error.

## Numeric frames inside turning frames

A group with a body that thrusts, such as any character, gets a falling numeric frame from 50 m/s relative to its semantic frame. A group of free bodies gets one only from 1,000 m/s ([Numeric frames](numeric-frames.md), "Who gets a numeric frame"). In a turning frame those speeds are relative to the turning axes: a body at rest in inertial space at the spike's floor moves at ω r = 31.3 m/s.

### The origin of a falling frame

`BeginStep` gives a falling frame the step acceleration that keeps its origin on the inertial line of a free body at its place:

```
stepAcceleration = FieldOf(semantic, place) + turns.FreeChange(place, chord, dt) / dt − HomeAcceleration(parent, semantic)
```

where

- `place` = the origin's semantic place, `HomeOffset(frame, semantic)`.
- `chord` = the origin's semantic chord, `HomeVelocity(frame, semantic)`: the sum of the frame velocities, which are chords.
- `turns` = the semantic frame's V1 and V2, which `BeginStep` fixed earlier in the same pass.
- `FieldOf(semantic, place)` = the semantic frame's field at the place minus its proper acceleration.
- `HomeAcceleration(parent, semantic)` = the step accelerations of the numeric frames above, which hold their own origins' terms.

The members' acceleration in Jolt is the linear gravity at their place, which subtracts the home's whole acceleration, plus the two-turn update at their semantic place and chord. So they feel only the difference between their terms and the origin's. The origin's terms must match the members' update, for a named reason: a falling group then keeps small local velocities, which is what the frame is for. With the field alone, as today, a group in a turning frame drifts away from its origin at a growing rate. A following frame lands on its step plan and a resting frame does not move, so neither needs a term on its origin. A following frame whose anchor has no motion intent in the step has no step plan, so it falls as a falling frame and takes this term.

`ter-1bnq` builds this term, in the same `BeginStep` that fixes V1 and V2, for a named reason: `ter-1bnq` applies the two-turn update to the members of numeric homes, and members whose origin lacks the term drift away from it. If the term waited for `ter-vja8`, main would carry that drift between the two merges.

### The landing case

The spike found one failure. A person crossed the middle of the station at 40 m/s and got a falling frame five ticks before it landed. It landed 0.7 m deep in the floor and went through it. With falling frames off, it landed correctly. The speed fits the 50 m/s for a group that thrusts: a free path through the axis at 40 m/s meets the floor at `sqrt(40² + 31.3²) = 50.8` m/s relative to the station (arithmetic of the design check). `ter-dhkf` merged after the spike, and its interior anchor may keep this person in the station's own system. The user kept the block on 2026-10-18 without a test of that first (fork F7, design A): the station scene waits for this work, and its test of the landing runs either way. The work also covers bodies beyond an anchor's reach, such as a launch from a turning surface frame (`docs/plans/blue-danube-demo.md`, line 43).

## The proper acceleration of a frame

`ter-xdt8` derives every frame's proper acceleration from its step record by one rule, the accelerometer rule, which the user chose on 2026-10-18 (fork F4, design B): its acceleration minus the gravity at its place, in its own axes. `ter-xdt8` builds the linear rule. This epic extends "the gravity at its place" to the parent's effective gravity with the rotation terms:

```
proper = R_end⁻¹ (record.Acceleration − (FieldOf(parent, start) + turns.Centrifugal(start) + turns.Euler(start, dt) + turns.Coriolis(record.ChordVelocity)))
```

where

- `record` = the frame's step record in its semantic parent, as `ter-xdt8` defines it for each kind of record.
- `start` = the place of the frame's origin where the step started, `record.Pose.Position − record.ChordVelocity · dt`.
- `FieldOf(parent, start)` = the parent's field at `start` minus the parent's proper acceleration as the step found it: the value stored before step 2, which physics used for the parent's bodies during the step (fork O2, design A, which `ter-xdt8` builds). `FollowOwners` derives every frame's value from the stored values first, and writes the new values after its loop.
- `turns` = the parent's V1 and V2 at step 2: the turns of the step that ended, which `BeginStep` fixed before the step.
- `R_end⁻¹` = the turn from the parent's axes into the frame's axes at the end of the step.

So every child of a parent reads the same parent value in every step, a dynamic child and every other child alike, and a change of a proper acceleration moves down one level of the chain in each step. A static room inside a parent whose own proper acceleration holds ω² R, such as a ship that rests on the floor of a spinning station, gets that ω² R one sync point after its parent. The rotation terms do not wait: they come from the parent's V1 and V2, so a static room at rest directly in the spinning station reads ω² R at step 2 of the sync point after the station's first turning step, at the same sync point as the station's own record of that step.

A new frame gives zero during its first step, as `FrameTree.Create` sets it, and the first step 2 after its spawn derives its value from its first record, or its rest, as for every frame (fork O2, design A). So a static room spawned in a spinning station gives its contents only its own field and its own rotation terms for one step, and ω² R from its second step on. A crate spawned above the room's floor gets no ω² R in that step, so it starts to fall one step late.

Physics measures a dynamic hull's value by the same rule, from V4's `Total`. What the extension gives:

| Frame | Proper acceleration |
|---|---|
| A static room at rest in a spinning station | ω² R toward the axis, from the first step 2 after its spawn. Its contents feel that reversed, plus the room's own rotation terms, so ω² (R + r) in all. |
| A ship that physics keeps on a station floor | The same, measured by physics |
| A ship that holds its radius in a station by thrust | Its thrust |
| A frame at rest in a parent that does not turn | As `ter-xdt8` gives it |
| A frame on a Kepler orbit in a turning parent | Not zero. A Kepler orbit is in its parent's axes, so in a turning parent the orbit turns with it (`ARCHITECTURE.md`, "How a frame moves"), which is no free fall. Orbits belong in a frame that does not turn, as the demo's `earth` frame holds them. |

## The wake

A change of a frame's turn wakes its sleeping bodies, as a change of its proper acceleration does today. The wake test compares the change of the acceleration that the turn gives a place in the system with the existing threshold:

```
(|ω| + |ω_wake|) · |ω − ω_wake| · B + |α − α_wake| · B > WakeAccelerationThreshold
```

where

- `ω`, `α` = the semantic frame's inertial angular velocity and angular acceleration for the step in flight, from V1 and V2.
- `ω_wake`, `α_wake` = the same at the system's last wake (V8).
- `B` = the reach of the system: the largest distance from the semantic frame's origin to a corner of the bounds of the system's bodies, in metres. In a numeric home it includes the home's offset.
- `WakeAccelerationThreshold` = 0.02 m/s², the existing option (`JoltBackendOptions.cs`, line 47).
- The left side bounds the change of the centrifugal and the Euler acceleration at any place in the system, in m/s², since the last wake.

Physics reads the bounds once per system per step. Numeric homes read their semantic frame's turns. Two examples:

- The facing orbit of the demo turns at about 1.1e-3 rad/s. With a reach of 100 m, the turn term passes the threshold only after a change of rate of about 0.09 rad/s. So an eccentric orbit, whose rate changes a little in every step, lets its sleepers sleep. The spike woke them on any change of rate, so they never slept.
- A station of reach 100 m that spins up at 0.01 rad/s² passes the Euler term at once, 1 m/s², and the turn term after each change of rate of about 3.2e-4 rad/s near 0.31 rad/s. Its sleepers wake while it spins up, as they must.

## Content

- A floor that bends with the station is a mesh of at least 256 faces per ring, or smooth. On 64 faces, walking gave one-tick weight spikes of up to 6 times body weight. The mesh generators are ticket `ter-r1hi`, which the station scene and step 4 of the demo share.
- Two limits of Jolt also appear without spin. A slow rolling ball speeds up to 6 cm/s until it sleeps. A character on a seam between two faces shakes by 0.1 to 0.4 mm.
- Tidal gravity in a semantic frame stays out: a semantic frame still applies no tidal gravity to its contents.

## Cost to measure

The rotation terms are physics, not an optional feature, so they get no switch. Frames that do not turn skip the update and keep today's cost. Each ticket measures and reports its cost in its notes, and `ter-1bnq` puts the numbers in `ARCHITECTURE.md`'s physics section:

| What | How | Against what |
|---|---|---|
| The two-turn update, the angular form and the shadow, per awake dynamic body per step | The station scene with 1,000 crates, with its spin and with a spin rate of 0, in `benchmarks/Tertius.Benchmarks` | The same scene without the turn |
| `BeginStep` | 1,000 frames in chains of 3 levels, all turning, beside `NumericFrameUpdateBenchmarks` | Today's `BeginNumericStep`, and the 44 µs that `ter-sn28` measured for a pass over 1,000 frames |
| The character update in a turning frame | The station scene with 100 characters | The same characters in a frame that does not turn |
| Memory | Counted | V1 and V2: 112 bytes per frame. V3: 24 bytes per frame. V4: 24 bytes per body, 12 more than today. V6: about 80 bytes per body. |

The sleep threshold reads V5. An awake body near the hub, where V5 is weak, lowers the threshold of its whole system, as one awake body far out in a planet's frame does today. The crate benchmark shows what that costs.

## Scenes and tests

### Scenes

| Scene | What changes |
|---|---|
| A new scene, `spin-station`, with `scripts/spin-station.json` (`ter-n44b`) | A station on a floor of 256 faces per ring, a person who stands, walks both ways and jumps, and crates that drop and are thrown. The station's spawn carries its first motion intent, so the person and the crates spawned on its floor turn with it from its first step. The name `station` is taken by the orbiting station of `ter-dhkf`, which does not turn and does not change. |
| `orbit`, and its scripts `orbit`, `walk`, `spacewalk`, `airlock`, `throw` and `windows` | The ship is a dynamic hull on the facing orbit, so it turns at about 1.13e-3 rad/s, and its contents get the terms. The Coriolis term on a body at 5 m/s is about 0.011 m/s². |
| `orbit-cluster` | The same ship. `OrbitClusterTests` expects the step of the thrown crate to rise at the ship's bubble, because no Coriolis force acted inside the ship. Now it acts on both sides. |
| `surface-yard` | Nothing in behavior: its `earth-surface` is a root that does not turn. The comment at `DemoScenes.SurfaceYard.cs` line 42 gives "the engine applies no Coriolis or centrifugal force" as a reason, which becomes false. |
| `earth-surface` in the orbit scenes | Turns at the sidereal rate. It holds no bodies in the scenes today. |

### Acceptance tests

The spike's experiments become the acceptance tests, each against the expected path of its bodies, worked out by hand. All run the real World on the real Jolt backend, and every station in them spawns with its first motion intent, unless the test is about a spawn without one. The tests of free motion use bodies with the default damping, because the shadow holds Jolt's damping (fork O3, design A) and most bodies have the default. The expected path of a body is its inertial line with Jolt's damping: in each step, its velocity relative to the frame's axes shrinks by the damping factor of "Jolt's damping in the shadow". For a body without damping it is the inertial line. The spike's numbers below are those of bodies without damping, so a test that quotes one also runs a body without damping against it.

`ter-1bnq`, pure Core tests, worked out by hand:

- `FrameTurns.FreeChange` for a steady spin and for a spin-up, against the inertial line.
- `ChordBehind` and `InstantAtEnd` are exact inverses. So are the angular pair.
- `ChangeBehind` followed by the two-turn update carries a velocity change by `R_n` alone.
- `BeginStep` composes a chain: the station inside a facing orbit gets the cross term of 3.4e-4 rad/s².
- A frame that does not turn gets exactly the identity turn.
- Before its first `BeginStep`, a new frame's V1 and V2 are `FrameTurn.None`, also in a reused slot of a destroyed frame that turned, and `EffectiveGravity` equals `LinearGravity` there.
- A teleport that turns a frame inside a turning parent gives no false angular acceleration: the station turned by 90° about its axis inside the facing orbit reads zero, where the rule without the list read about 0.09 rad/s². The frames below it are listed too.
- After a failed step, every frame takes its new V1 as its V2.
- A new frame whose spawn carries a first motion intent of a spin gets the real turn as both V1 and V2 in its first step, and leaves the list. One whose spawn carries none gets the identity in its first step and the full turn against an identity V2 in its second.
- `BeginStep` chooses a hull's row by `HullMotionInStep`: a hull that a `SetMotion` makes kinematic for the step takes the row of its motion intent.
- `FrameMotion`'s accelerations to the parent and back, for a point that moves in a frame that turns and spins up.
- `MotionRelativeInStep` for each kind of level, and the relation by which each part of an intent converts.
- `KeplerOrbit.Sample` gives the mean angular acceleration over the step.

`ter-1bnq`, bodies in the station:

- Rest: a crate that starts at rest in inertial space stays within 1 mm of its expected path over 10 minutes. Without damping, that is its place.
- A drop lands where the expected path says. Without damping, it lands at 0.5607786 s and 0.176458 m antispinward.
- Throws at 60 and 120 Hz land where the expected path from the instant velocity says.
- An impulse in the station gives the crate the expected path of its instant velocity plus the impulse's change, at 60 and 120 Hz.
- A body with the default damping in free motion in the station keeps its shadow over many steps: over 10 minutes, every update after its first starts from the shadow, none takes the contact branch, and the body stays within 1 mm of its expected path.
- A body steered to a target in the station reaches it.
- A free orbit: a body that starts at rest in inertial space follows its expected path. Without damping, it circles in the station's axes.
- Crates spawned at rest on the floor with a station that carries its first motion intent stay on the floor from the first step and press on it with m ω² r. Spawned with a station that carries none, they keep their place in inertial space while the floor slides under them in the station's second step.
- A stack rests, presses with m ω² r, and sleeps.
- Sleepers wake when the station spins up and stay at rest in inertial space. The Euler force matches. A test component in `Tertius.Testing` writes full samples of a spin with a constant angular acceleration. The engine needs no new rails type for it, so it gets none.
- The contents of a dynamic hull that spins up feel the Euler force one step late (fork F3). A ship that physics spins up by a steady torque holds a free crate. Worked out by hand, the crate's path in the ship's axes is the path in which each step turns by the rate predicted from the hull's last record. It differs from the crate's expected path by the Euler force of one step, and the test checks both: it matches the first and misses the second by that much.
- A tumbling body follows its expected angular velocity: its inertial angular velocity, with Jolt's angular damping relative to the frame's axes. Without damping, it keeps its inertial angular velocity.
- A ball that leaves the station hands off with the velocity of its expected path. Its ghost at the bubble follows its chord and its angular chord.
- A ship on the floor reads ω² R toward the axis, and its cargo weighs ω² (R + r). A ship that holds its radius by thrust reads its thrust.
- A static room at rest in the station gives its contents ω² (R + r) from its second step on, and only its own field and rotation terms in its first step. A room at rest in a ship on the floor reads the ship's ω² R one sync point after the ship.
- A facing frame on an eccentric orbit lets its sleepers sleep.
- A body in a falling frame inside the station moves as it does in the station's own system.
- The same events and hashes with one and four threads.

`ter-1b21`, characters:

- A person stands for 600 of 600 ticks, walks both ways, and weighs `m (ω r ± v)² / r`, to the tolerance that the floor's faces allow.
- A person jumps and lands within 7 µm of the predicted place.
- A person who jumps across the middle lands upright on the far floor and stands.
- A person near the hub takes the rigid form.
- A person walks along a slope in the ground plane.
- An impulse on a person in the air gives the inertial line of its change.

`ter-vja8`, numeric frames:

- The landing case: a person who crosses the middle at 40 m/s lands on the floor and stays on it.
- The same events with one and four threads.

`ter-n44b`: a checked-in Host test runs the `spin-station` script against the expected paths of its bodies. Opus builds and runs it, and Sonnet reads the outputs. The work ends with screenshots of a person on the inside of the ring.

### Tests that change on purpose

Each test changes with its reason in the commit and in a comment:

| Test | Ticket | Why |
|---|---|---|
| `WorldSpinFrameTests.ABallThatLeavesASpinFrameKeepsTheVelocityOfTheGroundItLeft` | `ter-1bnq` | The ball now feels the rotation terms in the turning frame |
| `HandOffTests.BallThrownOutOfSpinningPhysicsShipKeepsItsParentVelocity` (lines 80 to 130) | `ter-1bnq` | Its comment at line 111 says no Coriolis force acts in the ship |
| `OrbitClusterTests`, lines 182, 504 and 548 | `ter-1bnq` | They state that no Coriolis or centrifugal force acts in the ship |
| `FrameMotionAccelerationTests`, a new file in `Tertius.Core.Tests` | `ter-1bnq`, stage 1 | The new acceleration terms. No test of the struct `FrameMotion` existed. `FrameMotionTests` in `Tertius.Simulation.Tests` tests frames that move with their owners, and it does not change. |
| `FrameTreeNumericMotionTests`, lines 64 to 98 | `ter-1bnq` | `AccelerationRelative` gives way to `MotionRelativeInStep` |
| `KeplerOrbitTests`, for the angular acceleration | `ter-1bnq` | The mean over the step in place of the value at its start |
| The tests of `MotionSample.Converted` that `ter-xdt8` writes | `ter-1bnq` | The rotation terms of a conversion, and the new parameters `(start, end, step)` |
| `OrbitSliceTests` (line 225 on) | `ter-1bnq` | It reads `EffectiveGravity` on the turning ship right after the scene's build, before the first `BeginStep`, so its values stay. The read returns a `Double3` now, so the test compares `Double3` values. Its runs of `scripts/orbit.json` change as the other tests on the scene do. |
| Tests on the `orbit` scene and its scripts: `FreePlayTests`, `ShipWindowTests`, `WindowingTests`, `CameraInShipCrateInEarthTests`, `AirlockSliceTests`, `SpacewalkSliceTests`, `ThrowSliceTests`, `WalkSliceTests`, `WindowsSliceTests` | `ter-1bnq` | The ship turns at about 1.1e-3 rad/s. Each one that fails changes with its reason. |
| `CharacterStandingTests.UnderGravityACharacterInTheAirKeepsItsUpAndTurnsItToTheGravityOnceItLands` | `ter-1b21` | Up follows gravity in the air too (fork F5) |
| `CharacterStandingTests.ACharacterOnASlopeTooSteepToStandOnDoesNotStandAndSlidesDown` (line 104) and `CharacterFormTests.ACharacterInTheAirUnderGravityIsNotHeldBackByTheSteepSlopeCancelFromAFreeWallThatItSlidesAlong` (line 542) | `ter-1b21` | They involve slopes, but neither should change: on ground too steep to stand on, the character does not stand, and fork F6 acts only on a standing character. The second character touches a wall, so its up turns today too (fork F5). Each runs, and one that fails changes with its reason. No test walks a standing character on a walkable slope today. The new test "A person walks along a slope in the ground plane" covers fork F6. |

`CharacterOrbitHandOffTests` and `NumericFrameOrbitTests` build ships of their own and do not load the `orbit` scene, so they are not in the list. `ScriptRecorderTests` names the scene `orbit` in its records but builds no world on it: its one run uses the `probes` scene. The other test files that use `SpinMotion`, `FacesParent` or `.Spinning(` test the motion of frames with no body inside a turning frame, so they do not change.

## Decisions

The user made the decisions marked (User).

| Question | Decision |
|---|---|
| Which frames get the rotation terms | Every turning frame. (User, 2026-10-16) |
| Jolt's velocity convention | The engine corrects it everywhere: a velocity that gameplay sets is an instant velocity, and Jolt gets the chord. (User, 2026-10-16) Built by `ter-ediu`. |
| The scope rule "Only the linear (proper) acceleration of a frame acts on its contents" | It changes with this epic: the rotation terms act too |
| Tidal gravity in semantic frames | Unchanged: a semantic frame still applies no tidal gravity to its contents |
| F1. Where the turn of the step in flight comes from | From the poses: compose each level's start rotation (its tree pose, or the teleport pose) and end rotation (its step record) up the chain. The angular velocity comes from the records, and the angular acceleration is the change between steps. (User, 2026-10-18, design B) |
| F2. Who fixes the turns | The frame tree, per frame: `BeginStep` fixes V1 and copies the old value to V2 in `PrepareTick`, beside the step accelerations. Numeric frames share their semantic frame's value. (User, 2026-10-18, design B) |
| F3. A dynamic hull's turn in the step in flight | Predicted from its last record: the rate is the row's angular velocity plus V3 times the step. Its contents feel the Euler force one step late, the same lag as the hull's proper acceleration. (User, 2026-10-18, design A) |
| F4. The proper acceleration of a hull that physics does not move | The accelerometer rule for every frame. The user chose it on 2026-10-18 in `ter-xdt8` too, which builds the linear rule. This epic extends the gravity to the parent's effective gravity with the rotation terms. (User, 2026-10-18, design B) |
| F5. A character's up in the air | It follows gravity in every frame, touching or not. (User, 2026-10-18, design A) |
| F6. A standing character's walk and press | It moves along the ground plane and is pressed along the ground normal, in every frame. (User, 2026-10-18, design A) |
| F7. Does the station scene wait for falling frames in turning frames | Yes: the block stays, with no new test first. (User, 2026-10-18, design A) |
| F8. Where the velocity shadow runs | Only for dynamic bodies whose system's semantic frame turns. It is an exception keyed on turning frames, which the user accepted for its cost. (User, 2026-10-18, design A) |
| O1. The start of a turning frame that spawns with its contents | `ter-xdt8` lets a spawn description carry the new entity's first motion intent, which step 6 stores in its front intent, so the spawn's first step applies it. A frame spawned with one turns from its first step. A new frame leaves the list of frames without a last turn at its first `BeginStep`, so V2 of its first step equals the real V1, and contents spawned at rest on its floor turn with it, with no spin-up. A spawn without one holds still for its first step and spins up in its second. Reason: it removes the cause, a first step without the frame's real motion, so the rule of a new frame needs no exception, and a frame that did hold still keeps the physical result. The station scene and the acceptance tests give the first motion intent. (User, 2026-10-18, design C) |
| O2. Which proper acceleration of the parent the accelerometer rule reads, and the proper acceleration of a new frame | Both as in design A of `ter-xdt8`. The rule reads each parent's value as the step found it: the value stored before step 2, which physics used for the parent's bodies during the step. A new frame gives zero during its first step, and the first step 2 after its spawn derives its value. Reason: the rule then reads the gravity that acted at the frame's place during the step, so every child of a parent agrees in every step, and a new frame's value comes from one rule on a step record, with no guess about a step that has not run. The cost: a change moves down one level per step, and a new frame gives its contents no proper acceleration for one step. (User, 2026-10-18, design A for both questions) |
| O3. The velocity shadow and Jolt's damping | The expected velocity holds Jolt's damping: `JoltTarget` takes the body's linear damping factor for the step, in the form Jolt applies it, the shadow's `Chord` takes the same factor in doubles, and the angular form expects the angular damping factor. The damping is read from the body's description. Reason: most bodies have the default damping, so a shadow without it would serve few bodies and would read the damping of every damped body as a contact. The cost: physics repeats a detail of Jolt's integrator, which a test of a damped body pins. The tests of free motion use the default damping. (User, 2026-10-18, design A) |
| The cases of a character's update | A custom `[Union]`, `CharacterStartVelocity`, by the user's rule that every closed set is one |
| The angular form | The exact form with the two turns, in place of the spike's second-order expansion, which needs a rate that V1 and V2 do not hold |
| A new rails type for a spin-up | None. A test component writes the samples. |
| A frame whose axes jumped (a teleport or a transform write that turns it, and every frame below it) and every frame after a failed step | It takes its new V1 as its V2, as a new frame does: one list, the frames without a last turn. Turning V2 by the jump gives a false angular acceleration when the jump tilts the frame's own spin axis, and keeping V2 after a failed step reads two steps of change as one. (Design check, 2026-10-18) |
| The default of V1 and V2 | `FrameTurn.None`, which `Grow` and `Create` write. The default `DQuaternion` is no rotation at all. (Design check, 2026-10-18) |
| The row of a level in `BeginStep` | Chosen by `HullMotionInStep` and the case of the owner's front motion intent, as `FollowOwners` chooses the follow kind (Design check, 2026-10-18) |
| A dead band on the predicted rate of a dynamic hull | None. The frame's axes turn by what the hull's record says, and a band at the rate's float resolution would not catch solver noise. (Design check, 2026-10-18) |
| The shadow's take-back of Jolt's rounding | It joins V4's `Total`, through `Turning`, because it can pass the dead band of `AboveResolution`. The contact turn stays out, because the contact cancels it. (Design check, 2026-10-18) |
| Impulses in a turning frame | Their change enters as a change of the chord behind (`ChangeBehind`), so the update carries it by `R_n` alone. The angular change goes into `AngularChordAhead`. (Design check, 2026-10-18) |
| A dynamic body with a steer target | Its steering velocities take the place of the update in that step (Design check, 2026-10-18) |
| The reads in a frame, such as `VelocityIn` | They keep the instant rule on the row's chord, with an error in direction of up to ω `dt` / 2 times the speed in a turning frame, of the order of a chord's own lag (Design check, 2026-10-18) |
| The return type of `EffectiveGravity` and `SemanticGravity` | `Double3`, as `LinearGravity` (Design check, 2026-10-18) |
| Which ticket builds the origin term of a falling frame | `ter-1bnq`, in the same `BeginStep`, so that main never carries the drift of members whose origin lacks the term. `ter-vja8` keeps the landing case. (Design check, 2026-10-18) |
| How `ConvertIntent` converts each part of an intent | By the moment that the part describes: an end pose, a steer target and a full sample by `End`, a sample's chord by `Start` and `End`, and the velocities, the teleport, the forces and the impulses by `Start` (Design check, 2026-10-18) |

## Order of the work

Every engine ticket updates `ARCHITECTURE.md` to match its code before its merge, and its review checks that. Each ticket runs on its own branch, named after it. The line numbers of `ARCHITECTURE.md` are those of main on 2026-10-18. `ter-xdt8` moves some of them, so each ticket finds its lines by the section named here.

| Order | Ticket | Needs | Work | `ARCHITECTURE.md` and other docs |
|---|---|---|---|---|
| Done | `ter-ediu` | | The velocity convention for every body | Its missing text, "Instant velocities and chords" in the physics section, came with this design check |
| Now | `ter-r1hi` | `ter-aiuy` | The mesh generators for cylinders, rings and tori, with collision shapes. Demo code only. | None |
| 1 | `ter-1bnq` | `ter-ediu`, `ter-xdt8` | Stage 1 can start before `ter-xdt8` merges. It changes `FrameMotion.cs` and adds new files: `FrameTurn`, `FrameTurns`, and `FrameMotion`'s acceleration terms (V7), with their tests worked out by hand. It adds the two fields of `FrameMotion` with default values, and the new conversion methods beside the old ones, so that the callers compile unchanged. Since `ter-xdt8` merged, the callers are `MotionSample.Converted` (`MotionSample.cs` line 37), `MotionIntent.Converted` (`MotionIntent.cs` line 106), `World.ConvertIntent` (`World.Sync.cs` lines 665 and 666), `MotionOf` (`JoltPhysicsBackend.cs` line 907), `InstantMotionOf` (`JoltPhysicsBackend.Chords.cs` line 207) and `HandOffBodies` (`JoltPhysicsBackend.HandOff.cs` lines 157 and 191). They move in stage 2, which waits for `ter-6azq`, because both change the code of the proper acceleration. Stage 2, after `ter-xdt8` merges: `BeginStep` with V1, V2, V3, the frames without a last turn and the origin term of a falling frame, `LinearGravity` and V5, the two-turn update and the angular form in `ApplyGravity`, impulses, steer targets, V4, V6, V8, the chord sites, `InstantVelocity`, the ghosts, `ComputeProperAccelerations`, the rotation terms in `ProperAccelerationFrom`, `MotionRelativeInStep`, `ConvertIntent` and the two `Converted` methods, the callers of `FrameMotion`, `KeplerOrbit.Sample`'s mean angular acceleration, the bodies' acceptance tests, the changed tests, and the cost. | In `ARCHITECTURE.md`: the scope (line 12), `SpinMotion` ("No centrifugal force acts", line 289), the conversions at step 5 (line 353, rewritten by `ter-xdt8`), effective gravity (line 364), hand-off rule 5 (line 388), the sleep threshold, which follows the weakest effective gravity, now V5 (lines 461 and 470), the wake (line 480), and in "Instant velocities and chords" the name `GravityApplied`, which V4 replaces (line 529), and the ghost, which converts by poses now (line 533). In the physics section the two-turn update, the shadow, the contact turn, impulses, the chord rule in turning frames and the measured cost. In "Numeric frames" the origin of a falling frame. The code comments at `FrameTree.cs` lines 1124 to 1127 and `DemoScenes.SurfaceYard.cs` line 42. In `docs/plans/rails-components.md`: the bullet on spin gravity's need in "Why", the row "Which epic comes first" in "Decisions", the bullet on `KeplerOrbit.Sample` in "The rails library", and the row `AngularAcceleration` in "The motion sample". The row "Rotation" of `docs/plans/numeric-frames.md` (line 48): numeric frames still never turn, and their members feel the semantic frame's terms. The pointers of "Pointers to removed sections" below. |
| 2 | `ter-1b21` | `ter-1bnq` | `CharacterStartVelocity`, up in the air, standing along the ground normal, the walk in the ground plane, the weight input, the form from V5, impulses on characters, and the characters' tests | The character paragraphs of the physics section, among them the rule that a character in free fall keeps its last up (line 501) and the forms (line 499) |
| 2 | `ter-vja8` | `ter-1bnq` | The landing case: its test, and the fix if the test fails once `ter-1bnq` has merged. The numeric tests that need it. | "Numeric frames", if the fix changes a rule there |
| 3 | `ter-n44b` | `ter-1b21`, `ter-vja8`, `ter-r1hi` | The `spin-station` scene, its script, its Host test and the screenshots | The scope rule in `CLAUDE.md` (line 34). A new row in `CLAUDE.md`, "Where the user changed my proposals", for fork F7: the design check proposed a test on main first, whether `ter-dhkf`'s interior anchor keeps a person who crosses the station at 40 m/s in the station's own system, and to order `ter-vja8` after this ticket if it does. The user kept the block with no test first. `GUIDE.md` section 7 item 1, which points at `ApplySpin`, a function that no longer exists, and `README.md` line 21. The questions in `docs/plans/blue-danube-demo.md` (lines 36, 43 and 523 to 528), which this design answers. |

`ter-1b21` and `ter-vja8` can run side by side: one changes the character update, the other the numeric update.

### Pointers to removed sections

The rewrite of 2026-10-18 removed the sections "The velocity convention", "Conversions between frames", "The rotation terms" and "Tests". These places pointed at them. Since stage 1 of `ter-1bnq`, the four code comments point at their new targets, and `ter-xdt8` fixed its rows of `rails-components.md`:

| Place | Old section | New target | Who fixes it |
|---|---|---|---|
| `src/Tertius.Physics/JoltPhysicsBackend.Chords.cs`, line 9 | "The velocity convention" | `ARCHITECTURE.md`, "Instant velocities and chords" | `ter-1bnq` |
| `src/Tertius.Physics/JoltPhysicsBackend.cs`, line 207 | "The velocity convention" | `ARCHITECTURE.md`, "Instant velocities and chords" | `ter-1bnq` |
| `tests/Tertius.Physics.Tests/InstantVelocityTests.cs`, line 11 | "The velocity convention" | `ARCHITECTURE.md`, "Instant velocities and chords" | `ter-1bnq` |
| `src/Tertius.Core/Frames/FrameMotion.cs`, line 33 | "Conversions between frames" | This doc, "V7, the relative motion of two frames" | `ter-1bnq`, stage 1 |
| `ARCHITECTURE.md`, line 353 | "Conversions between frames" | This doc, "V7, the relative motion of two frames" and "The two conversion rules" | `ter-xdt8` rewrites the line. `ter-1bnq` replaces its text. |
| `docs/plans/rails-components.md` on main, line 166, which `ter-xdt8` moves to its line 175 | "Conversions between frames" | This doc, "V7, the relative motion of two frames" | `ter-xdt8`, or else `ter-1bnq` |
| `ter-xdt8`'s `rails-components.md`, line 71, the row "What the frame tree row keeps of a step record" | The doc as a whole, as the place of spin gravity's own store | This doc, "V3, the angular acceleration of a frame's last record" | `ter-xdt8`, or else `ter-1bnq` |
| `ter-xdt8`'s `rails-components.md`, line 591, "The step record of the last finished step" | "Conversions between frames" | This doc, "V3, the angular acceleration of a frame's last record" | `ter-xdt8`, or else `ter-1bnq` |
| `ter-xdt8`'s `rails-components.md`, line 712, "Limits of the rule" | "The rotation terms" | This doc, "The proper acceleration of a frame" | `ter-xdt8`, or else `ter-1bnq` |
| `ter-xdt8`'s `rails-components.md`, line 800, "What goes, and the docs to change" | "Conversions between frames" and a paragraph at the end of "The rotation terms" | This doc, "V7, the relative motion of two frames" and "The proper acceleration of a frame", which hold both since the rewrite | `ter-xdt8` |
| `.tickets/ter-1bnq.md` | "Section 'The rotation terms'" | This doc, "The two-turn update", "Values" and "The flow per kind of entity" | A note on the ticket (see below) |
| `.tickets/ter-n44b.md` | "Sections 'Content' and 'Tests'" | This doc, "Content" and "Scenes and tests" | A note on the ticket (see below) |

## Notes for the tickets

This design check changes no file in `.tickets/`. The tickets' texts should take these notes:

- `ter-s9m0`: the design was rewritten on 2026-10-18 after a design check, with the user's answers to forks F1 to F8 (see "Decisions"). A second check on the same day fixed its gaps, and the user answered its three forks on the same day (O1 to O3 in "Decisions"). Replace the dependency on `ter-ozm8` by one on `ter-xdt8`: `ter-1bnq` needs only `ter-xdt8` of the rails epic, and no ticket of spin gravity needs `ter-zg77`.
- `ter-rb26` and `ter-ozm8`: their notes say that spin gravity starts when the rails epic closes. Spin gravity now starts when `ter-xdt8` merges, and stage 1 of `ter-1bnq` can start before. The demo steps keep their own dependencies.
- `ter-1bnq`: the ticket's pointer "Section 'The rotation terms'" goes to "The two-turn update", "Values" and "The flow per kind of entity". Replace "the inertial angular velocity summed along the chain" and "the angular acceleration from the change between steps" by V1 and V2 from `BeginStep`. Replace the list "Spin frames, Kepler frames that face their parent, frames that physics drives and that turn" by "every turning frame: V1 or V2 is not the identity (`FrameTurns.Turns`)". The note of 2026-10-18 on the proper acceleration of a ship inside a turning parent is answered by V4's `Total` and the accelerometer rule, with the two tests in "Acceptance tests". The ticket builds in two stages (see "Order of the work"), builds the origin term of a falling frame, fixes the pointers of "Pointers to removed sections", and updates its lines of `ARCHITECTURE.md`. Its re-baselines are the rows for `ter-1bnq` in "Tests that change on purpose". Its tests of free motion use the default damping, and the shadow holds Jolt's damping (fork O3). Its stations spawn with their first motion intent, which `ter-xdt8` adds (fork O1).
- `ter-1b21`: the note of 2026-10-18 on the form near the hub is answered: the form reads V5, which holds the centrifugal and the Euler term. Up in the air and the walk along the ground plane act in every frame (forks F5 and F6). The ticket updates the character paragraphs of `ARCHITECTURE.md`.
- `ter-vja8`: the block of `ter-n44b` stays (fork F7). The origin of a falling frame moved to `ter-1bnq`, so this ticket keeps the landing case, its fix, and the numeric tests that need it. The ticket updates "Numeric frames" in `ARCHITECTURE.md` if its fix changes a rule there.
- `ter-n44b`: the ticket's pointer "Sections 'Content' and 'Tests'" goes to "Content" and "Scenes and tests". The scene is `spin-station`, because `station` is taken. Its station spawns with its first motion intent, so the contents spawned on its floor turn with it from the first step (fork O1). Each engine ticket now updates its own lines of `ARCHITECTURE.md`, so this ticket keeps `CLAUDE.md`, with the new row for fork F7, `GUIDE.md`, `README.md` and the demo plan's questions.

# Rails components

Epic: `ter-ozm8`. The design ticket `ter-skca` wrote this plan. The work tickets are `ter-k8cg`, `ter-6r8c`, `ter-ru5u` for changes of motion, and `ter-zg77` (see "Order of the work").

## Goal

Every frame with an owner follows its owner. A frame whose owner has a dynamic hull follows the hull, as a frame with the `Physics` driver does today. A frame on rails, on a Kepler orbit or in a steady spin, follows a motion sample that a component of its owner hands over in each tick. A static frame follows its owner when gameplay moves the owner. The frame tree keeps no driver, orbit or spin, and physics never pushes a hull after its frame.

## Why

- Two mechanisms move frames today. Physics moves the hull of a `Physics` frame, and the frame follows the hull. The tree moves a `Kepler` or `Spin` frame in step 6b of the sync point, and physics pushes the hull after it in every step (`DriveKinematicHulls`). The second mechanism needs the `MotionDriver` enum, the tree's orbit and spin arrays, a driver version that physics polls (`FollowDriver`), a hull motion that overrides the body's description (`BodyDesc.FrameHullMotion`), and a prediction in physics that branches on the driver (`PredictMotion`, `OrbitMatchesTree`). One rule removes all of them.
- An orbit or a spin is gameplay state. A game chooses it, and components hold gameplay state. A mod can add its own motion on rails, such as a liner on a fixed route, with no change to the engine.
- The spin gravity epic needs the exact angular velocity and angular acceleration of every turning frame, in doubles. The float velocities that Jolt gives a kinematic body are not exact enough.
- Today the sync point solves each orbit in a serial loop, and physics solves it at least twice more in each step. A component solves it once, in the parallel part of the tick.

## Terms

| Term | Meaning |
|---|---|
| Rails motion | Motion that is an exact function of time: a Kepler orbit or a steady spin |
| Rails component | A component that moves its entity on rails: `KeplerMotion` or `SpinMotion` |
| Motion sample | One step of rails motion, the struct `MotionSample`: the pose at the end of a step, and the velocities and accelerations that go with it |
| Sample table | The table of motion samples, indexed by the frame's index, with three buffers (see "The motion sample") |
| Step N | The physics step that runs during tick N, from `t_N` to `t_{N+1}`. Gameplay of tick N reads `self.Time = t_N`. |
| Follow | At the sync point, a frame takes its pose and velocities from its owner |
| Hull | The body of a frame's owner, in the Jolt system of the frame's tree parent. A root frame has no hull. Its owner's body is only the frame's static interior. |
| Body on rails | A kinematic hull that follows motion samples |
| Following frame | A numeric frame that holds a fast or far body on rails at its origin and follows its samples. Today's orbit frame (`NumericMode.Orbiting`) is the only kind so far. |
| Chord velocity | The displacement over a step, divided by the step. Jolt reads a stored velocity this way. |
| Instant velocity | The velocity at one moment |

## Decisions

The user made the decisions marked (User).

| Question | Decision | Why |
|---|---|---|
| Where the state of rails motion lives | On components of the frame's entity, `KeplerMotion` and `SpinMotion`. The tree keeps no driver, orbit or spin. Every frame with an owner follows its owner. (User) | One rule for every frame: the rule that a `Physics` frame follows today. |
| What a rails component holds | An exact description as a function of time: orbit elements and an epoch, or a tilt, a rate, a phase and an epoch. No integration state. (User) | A sample at any time is exact, so no error builds up, and a save needs only the description. |
| How the engine gets the motion | In each tick, the component hands over one motion sample for the end of the next step, in one intent, `MoveOnRails`. The engine never calls into a component at the sync point. It reads the intent. (User) | The solve runs in the parallel part of the tick. The serial sync point only copies samples. |
| What a sample holds | The pose at the end of the step, the chord velocity over the step, the instant velocity at its end, the acceleration over the step, and the exact angular velocity and angular acceleration, all in doubles. (User) | Each reader needs another field (see "The motion sample"). |
| How exact a body on rails is | A body on rails ends each step exactly on its target, and its frame stands exactly on the sample at the sync point. (User) Physics does the snap right after the Jolt update, because the sync point cannot reach Jolt. | Jolt reaches a kinematic target only to the float rounding of the velocity, about 4 µm per step at 7.6 km/s. With the snap, a place on an orbit stays exact to the last bit. |
| A frame without a body | It takes the same intent, on its entity's transform. (User) | Planets and rooms follow the same rule as hulls. |
| A static frame | It needs no component. Gameplay moves its owner. (User) | A static frame moves only when gameplay moves it. |
| What replaces `MotionDriver` | Nothing. The enum and `BodyDesc.FrameHullMotion` go. The motion of the owner's body says how a frame follows. (User) | A second value beside the body's motion can disagree with it. |
| A change of motion | A swap of components plus a change of the body's motion. (User) The row "How a body changes its motion after spawn" below says how the body's motion changes. | Today only a frame driver changes a body's motion after spawn. |
| The rails math | Public pure functions in the namespace `Tertius.Frames.Rails`, in `src/Tertius.Core/Frames/Rails/`. Every function lives in its struct: an instance method for a function of an orbit or a spin, a static method of `KeplerOrbit` for two-body math that takes no orbit elements (see "The rails library"). (User, 2026-10-18) | Components and engine code call one copy. It is public, because the Demo and the Host cannot see the internals of Core. |
| How a frame takes its motion | One step record per frame, a `MotionSample`, for every kind of owner. Physics releases it for every hull (a dynamic hull, a kinematic hull from the sample it applied, a static teleport); an owner without a body writes it (a rails sample, or a transform write at the sync point). One loop, `FollowOwners`, applies it, and physics predicts every frame from it. `FrameState` goes. (User, 2026-10-18) | One rule for every frame, with no special path for physics. See "One motion record per frame". |
| The reverse path | `DriveKinematicHulls` and step 6b (`AdvanceDrivenFrames`) go. (User) | The tree no longer moves a frame on rails, so nothing pushes a hull after it. |
| Which epic comes first | This epic, then spin gravity (`ter-s9m0`). (User) | Spin gravity reads the exact angular velocity and angular acceleration from the sample. |
| A fast or far body on rails | It lives in a following frame. The frame takes its place and velocity at each sync point, and its acceleration in each step, from the sample. Every kinematic hull gets one by the speeds and distances of free bodies, not only a Kepler hull. `NumericBody.Fixed` and `Orbits` come from the hull's motion. | An anchor needs a home in which it is at rest (rule 3 of "Anchors" in [Numeric frames](numeric-frames.md)). With motion on components, the tree cannot tell a Kepler hull from another kinematic hull. |
| The velocity of a ghost of a frame on rails | The sample's chord. A hand-off uses the sample's instant velocity. | Ghosts use chords and hand-offs use instant velocities for every other body ("Bubbles and ghosts" in [Numeric frames](numeric-frames.md)). Today the ghost takes the orbit's instant velocity, 7 cm/s off in low orbit. The sample has both, so this limit goes. |
| The velocity that the tree keeps for a frame on rails | The instant velocity, as today. A frame with a dynamic hull keeps the hull's chord, as today. | Reads in a frame do not change. Physics reads the sample, so it needs no conversion. |
| A transform write to a bodyless owner on rails | Allowed. It moves the frame at the sync point of the tick, and the next sample puts the frame back on its rails. | No driver owns the pose, and the entity's own components decide. Today the World refuses the write and records an error. |
| How a component reads the time of its sample | A new `Self.TimeAt(long tick)` returns the World's time of a tick, the same double that `World.Time` holds then. | A sum such as `self.Time + 2 * self.DeltaTime` can differ from the World's time in the last bit, and a place on an orbit must match it exactly. |
| How rails motion starts | The new frame holds still at the pose of its spawn description for its first step, as a frame without a sample does. The first run of the component puts it on its rails with its first sample. Nothing writes a first sample. (User) | No new API, and it works for blueprints, content and a mod's own component. The engine still never calls into components, and a swap of components has no gap. |
| How a body changes its motion after spawn | An intent, `self.Body.SetMotion(BodyMotion)`, that the entity's own components write in a tick. The motion row changes at step 4 of the sync point, as a collision change does, and physics changes the Jolt motion type at the start of the next step (`ApplyHullMotion`). (User) | Gameplay can start a station's engines, and the hull keeps its body id, its system and its contacts. An entity changes only itself. |
| Where the checks of rails motion live | In the components. A property setter refuses an orbit or a spin that is not valid, with an `ArgumentException`. A `KeplerMotion` whose parent has no G·M holds its frame still and logs once. A rails component on a body that is not kinematic logs once and writes no sample. The World checks nothing. (User) | One place for each rule, and the same rules for a mod's own component. Without a driver, the World does not know which frames orbit. |
| The proper acceleration of a frame on rails | A field of the sample, `ProperAcceleration`, a `Vector3` in the frame's axes. `KeplerMotion` gives zero. `SpinMotion` gives its property `ProperAcceleration`, zero by default, and a component that starts a turntable copies the frame's value into it. (User) | Today's results: a Kepler frame has zero proper acceleration, and a landed ship that starts to turn keeps its cargo's weight (`SpinFrameTests`). Every rails component fills one more field. |
| Where the motion sample lives | In the sample table: a table of its own, indexed by the frame's index, with three buffers. Physics gets the buffer that its step applies in `PhysicsStepContext`. | Only frames pay, a frame without a body needs nothing special, and step 6c reads the third buffer. The table moves its buffers on at step 4 and converts at steps 5 and 7b. |
| What a state dump records about a frame | `FrameRecord(Id, Parent, Follows)`. `Follows` is `"rails"`, `"physics"` or `"static"`: how the frame followed its owner at the dump's sync point. | A dump still shows which frames move on rails. The tree reports one new value. |

## The rails library

The namespace `Tertius.Frames.Rails` holds the data types `KeplerOrbit`, `KeplerAttitude`, `FrameSpin` and `MotionSample`. Every rails function lives in its struct: a function of an orbit or a spin is an instance method, and a function of two-body motion that takes no orbit elements is a static method of `KeplerOrbit`. The records keep their `IsValid` checks. Everything is public and pure. The user chose this on 2026-10-18.

| Today | In the library |
|---|---|
| `KeplerOrbit.StateAt`, `RotationAt`, `AngularVelocityAt`, `MeanMotion`, `Period` | The same methods of `KeplerOrbit`, with the parameter `mu` |
| The private `EccentricAnomalyAt`, `FacingParentAt`, `PerifocalToParent` and `Spin` | Private members of `KeplerOrbit` |
| `NumericFrameRules.OrbitChord` | the static `KeplerOrbit.Chord(Double3 position, Double3 velocity, double mu, double step)` |
| `NumericFrameRules.OrbitAcceleration` | the static `KeplerOrbit.Acceleration(Double3 position, double mu)` |
| The body of `FrameTree.KeplerMu` | the static `KeplerOrbit.MuFrom(in GravityField parentGravity)`: the G·M of a point field that `CanBeOrbited`, else 0 |
| `FrameSpin.RotationAt`, `FrameSpin.AngularVelocity` | The same members of `FrameSpin` |
| `FrameTree.KeplerMotion` | `KeplerOrbit.Sample`. The tree's method calls it in `ter-k8cg` and goes in `ter-6r8c`. |

New functions:

- `KeplerOrbit.AngularAccelerationAt(double time, double mu)`. It is zero for the attitude `Spin`. For `FacesParent` it is the change of `Cross(position, velocity) / position.LengthSquared`.
- `KeplerOrbit.Sample(double mu, double stepEnd, double step)`. It solves Kepler's equation once, at `stepEnd`, for the pose, the instant velocity and the angular velocity. The chord comes from `Chord`. The acceleration is `Acceleration` at the place where the step starts, the end place minus the chord times the step. That is the step acceleration of an orbit frame today, to rounding. The angular acceleration comes from `AngularAccelerationAt`, at the start of the step. The proper acceleration is zero.
- `FrameSpin.Sample(Double3 place, Vector3 properAcceleration, double stepEnd, double step)`. The pose is the place with `RotationAt(stepEnd)`. The chord, the velocity, the acceleration and the angular acceleration are zero. The angular velocity is the spin's. The proper acceleration is the argument.

## The rails components

The two components live in `src/Tertius.Simulation/Components/`, beside `Sensor`. Each has a public constructor without parameters and `init` properties, so a blueprint can make it. The property setter refuses a description that is not valid, with an `ArgumentException`, as the World does today.

```csharp
public sealed class KeplerMotion : IComponent
{
    public KeplerOrbit Orbit { get; init; }  // the setter refuses an orbit that is not valid
    public int Priority => -1000;
    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        double mu = self.Frame.IsNull ? 0 : KeplerOrbit.MuFrom(self.Snapshot.Frames.Gravity(self.Frame));
        ...
        double stepEnd = self.TimeAt(self.Tick + 2);
        self.MoveOnRails(Orbit.Sample(mu, stepEnd, self.DeltaTime));
    }
}
```

where

- `Orbit` = the orbit elements and their epoch, relative to the frame's semantic parent, in its axes. They hold no G·M.
- `self.Frame` = the owner's row frame, which is the frame's semantic parent. It is null only for the owner of a root frame.
- `KeplerOrbit.MuFrom` = the G·M of the parent's point gravity, or 0.
- `self.TimeAt(tick)` = new: the World's simulation time at the start of a tick, in seconds.
- `stepEnd` = the end of the step in which this tick's intent acts, in seconds.
- `self.DeltaTime` = the step length in seconds, a float that widens to the same double that the World uses.
- `self.MoveOnRails` = new: writes the sample as the entity's rails intent.
- `...` = three rules that come before the last two lines:
  - When `mu` is 0, the parent has no point gravity with a G·M above 0. The component logs once, writes no sample and returns, so the frame holds still.
  - When the owner has a body that is not kinematic, the component logs once, writes no sample and returns.
  - On its first run, the component teleports the owner's hull onto its rails, so that the hull jumps to its orbit and does not sweep through the bodies in between. A frame without a body needs no teleport, because its first sample alone puts it on its rails.

`SpinMotion` holds a `FrameSpin` and a property `ProperAcceleration`, a `Vector3` that is zero by default. A component that starts a turntable copies the frame's proper acceleration into it. `SpinMotion` does the same with `Spin.Sample(place, ProperAcceleration, stepEnd, self.DeltaTime)`. It needs no G·M, so it has only the second and the third rule of the `...`. It reads its place from its own frame: `self.Snapshot.Frames.LocalPose(self.OwnedFrame).Position`, where the new `Self.OwnedFrame` is the frame that the entity owns. A spin frame does not move, so the place stays the same from tick to tick. A destroy of the parent moves the frame up, and the component reads the new place.

The timing:

| Moment | Time |
|---|---|
| Gameplay of tick N reads `self.Time` | `t_N` |
| Step N runs, beside gameplay of tick N | `t_N` to `t_{N+1}` |
| `World.Time` after the sync point of tick N | `t_{N+1}` |
| Step N+1 applies the intents of tick N | `t_{N+1}` to `t_{N+2}` |

So the sample that tick N writes describes step N+1 and ends at `t_{N+2} = self.TimeAt(self.Tick + 2)`. `Door.cs` states the same rule for a door's target.

Other rules:

- G·M stays in one place: the component reads the parent's gravity from the front snapshot in each tick.
- A rails component runs first in its entity (`Priority` -1000). So another component of the entity can stop it in the same tick: a later `MoveOnRails` wins, and a change of motion clears the sample.
- When the parent dies, the frame moves up as today, and the component reads the new parent's G·M in the next tick. It keeps its elements, so a Kepler frame jumps to the same orbit around the new parent, as today. When the new parent has no G·M, the component holds the frame still and logs once. `ter-e30t` and `ter-f620` become questions of what the components do then.
- A new frame on rails starts one tick late. A component that is new on an entity runs first in the tick after it arrives, and its first sample acts one step later. A new frame goes live at a sync point, and its first step runs before any of its components runs, so that step has no sample. The new frame holds still at the pose of its spawn description for that step, as a frame without a sample does. At the next sync point it still stands at that pose. At the one after that, it stands on its rails. Until then, the frame is one step of its motion behind its rails: 500 m for Earth around the sun. A fast hull is closer, about 1.2 mm in low orbit, because it holds still in the following frame that the numeric update gives it at spawn, and that frame falls along the orbit. A swap of components has no gap.
- The checks of rails motion live in the components, not in the World. The World keeps none of today's checks of Kepler frames: its refusals of a Kepler frame whose parent has no point gravity with a G·M above 0, at spawn and in `SetFrameDriver`, and the refusal in `SetFrameGravity` of a new G·M for the parent of a Kepler frame. So a spawn of a Kepler frame without G·M does not throw. A new G·M of a parent changes the orbits in it at once, so the frames on them jump. `World.AssertInvariants` loses its rule for Kepler frames. The setters of the properties of the two components refuse an orbit or a spin that is not valid, with an `ArgumentException`.

## The motion sample

```csharp
public readonly record struct MotionSample(
    DPose Pose, Double3 ChordVelocity, Double3 Velocity, Double3 Acceleration,
    Double3 AngularVelocity, Double3 AngularAcceleration, Vector3 ProperAcceleration);
```

Every field except `ProperAcceleration` is relative to the frame's semantic parent, `self.Frame`, in its axes. `ProperAcceleration` is in the frame's own axes, as the tree keeps it. A root frame has no parent, so its sample is in the root's own space, as its local pose is today.

| Field | Meaning | Physics | Frame tree at the sync point | Numeric update | Reads and render |
|---|---|---|---|---|---|
| `Pose` | The pose at the end of the step, in m | The hull's kinematic target, and the frame's predicted pose for ghosts and hand-offs | The frame's local pose, exactly | The place of the following frame | Through the tree pose, as today |
| `ChordVelocity` | The change of place over the step, divided by the step, in m/s | The velocity of the frame for its ghosts | None | The velocity of the following frame | None |
| `Velocity` | The instant velocity at the end of the step, in m/s | The velocity of the frame for hand-offs | The frame's linear velocity. In a following frame, the frame keeps the velocity minus the chord. | The hull's speed for the thresholds, through the tree | `VelocityIn` gives it, as today |
| `Acceleration` | The frame's acceleration during the step, in m/s² | The effective gravity in the following frame, through its step acceleration | None | The step acceleration of the following frame | None |
| `AngularVelocity` | The angular velocity at the end of the step, in rad/s | The angular velocity of the frame for ghosts and hand-offs | The frame's angular velocity, narrowed to the tree's float field | None | `AngularVelocityIn` gives it, as today |
| `AngularAcceleration` | The angular acceleration during the step, in rad/s² | Nothing yet. Spin gravity reads it. | None | None | None |
| `ProperAcceleration` | The frame's proper acceleration in its own axes, in m/s². The effective gravity of the frame's contents is the frame's field minus this value. `KeplerMotion` gives zero. `SpinMotion` gives its property `ProperAcceleration`, zero by default. | Through the tree, for the effective gravity of the contents and the wake test, as today | The frame's proper acceleration, in place of the value that physics measures for a frame with a dynamic hull | None | None |

`self.MoveOnRails(in MotionSample sample)` is a new member of `Self`. Its rules:

- The last write in a tick wins, as for `MoveKinematic`.
- It needs an entity that owns a frame. When the frame has a parent and the owner has a body, that body is a hull, and the hull must be kinematic. Else the call throws an `InvalidOperationException`. A plain kinematic body that owns no frame cannot write a sample. It does not need one yet.
- A frame without a body can write a sample. The sample acts on its transform.
- The samples live in the sample table: a table of its own, indexed by the frame's index, with three buffers. The written buffer holds the samples that gameplay writes in the tick. The applied buffer holds the samples that the physics step of the tick applies, which gameplay wrote one tick before. The followed buffer holds the samples of the step that ended, which the frame follows now. Physics gets the applied buffer in `PhysicsStepContext`. Only frames pay: a frame without a body needs no intent row.
- In step 4, the table moves its buffers on, as the intents swap: the applied buffer becomes the followed buffer, the written buffer becomes the applied buffer, and the written buffer starts empty. So a frame whose component writes no sample in a tick has no sample in the step that follows.
- Before a frame dies in step 5, each sample that names it converts into the frame's new parent. Step 7b converts the samples as it converts intents: the pose as a pose, the velocities with the motion of one frame relative to the other, and the accelerations as directions. A sample that converts misses for one step by the relative motion of the two frames, as a kinematic target does.

A kinematic hull takes `MoveOnRails` and `Teleport`, and the teleport applies first. `MoveKinematic` and `SetVelocity` throw for a kinematic hull, because only a sample tells its frame how to follow. In a step without a sample, a kinematic hull holds still in its tree parent: physics sets its velocity to zero, as it stops a kinematic body after its last target today.

## One motion record per frame

Every frame takes its motion the same way, whatever its owner is. The user chose this on 2026-10-18: one record type, one loop that applies it, and one prediction rule in physics. An entity with a body writes intents (forces, `MoveOnRails`, `Teleport`), physics moves the body, and for a hull physics releases the frame's record. An entity without a body writes the record itself: a rails sample, or a transform write.

The step record of a frame is a `MotionSample` that describes the frame's motion over the step that just ended. Its owner's kind decides who fills it:

| Owner | Who fills the step record |
|---|---|
| A dynamic hull | Physics, right after the Jolt update: the hull's pose, its chord (Jolt's velocity), its instant velocity (the chord plus half a step of its acceleration, `InstantMotionOf`), its acceleration and its angular acceleration measured from the change of Jolt's velocities over the step, Jolt's angular velocity, and the measured proper acceleration |
| A kinematic hull with a sample | Physics, after it snaps the hull onto the sample's pose: the sample that it applied, with every field exact, so the sample's doubles and not Jolt's float velocities |
| No body, with a sample | The component's sample in the applied buffer, which already is one |
| A static hull that teleported in the step | Physics: the teleport's pose, with zero velocities and accelerations |
| No body, and the owner wrote its transform in the tick | The sync point: the written pose, with zero velocities and accelerations. This one acts in the same tick, because no physics step stands between the write and the frame. |
| Anything else | No record. The frame holds still. |

Rules:

- Physics hands back a record for every hull whose frame moved in the step, in the step output, in place of `FrameState`, which goes. A record from physics is relative to the frame's tree parent, as `FrameState` is today, so a frame with a dynamic hull keeps its exact place in a numeric home. For a kinematic hull at the origin of its following frame, that is the origin with the sample's rotation, and the sample's instant velocity minus its chord. A sample from a component is relative to the frame's semantic parent. These agree for a frame on rails in its semantic parent.
- In step 2, `FollowOwners` is the one loop: each frame with a step record takes it, in the tree's dense order. The frame takes the pose, the instant velocity (minus the home's velocity under a numeric parent), the angular velocity narrowed to the tree's float field, and the proper acceleration. The tree keeps the instant velocity for every frame. Today it keeps the chord for a frame with a dynamic hull, so that frame's tree velocity changes by half a step of its acceleration. If a behavior, not only a pinned number, depends on the chord, that is the misbehavior that would justify an exception.
- Each record that a frame takes goes into the frame's slot of the applied buffer in step 2, if a sample is not already there. So after step 4 moves the buffers on, the followed buffer holds the record of every frame that moved in the step, and the numeric update and the following frames read one place.
- Physics predicts every frame at the end of the step from its step record: the pose from the record's pose, ghosts take the record's chord velocity, hand-offs take its instant velocity, and the angular velocity is the record's. A frame without a record predicts its tree pose with zero velocities. This replaces the three rows of the table "Predictions of a frame at the end of a step".
- The entity and its frame are one entity with two tables: the frame tree row is the exact table, and the transform row is the float view of the same entity. After `FollowOwners`, the float view of each frame entity is filled from its exact table. A body state of a hull never writes the float view.

## Frames without a body, and static frames

A frame without a body follows its sample, or its owner's transform write, or holds still. A frame with no sample and no body is a static frame. This replaces the `Physics` frame without a body, which nothing moves today.

A static frame follows its owner:

- Without a body, the owner writes `self.SetTransform`. The frame takes the pose at the sync point of the same tick, as today. The rule that only the owner of a `Static` frame can write its transform goes. Any frame owner without a body can write it, unless it is attached.
- With a static hull, the owner writes `self.Body.Teleport`. Physics moves the static hull at the start of the next step and wakes the bodies around its old and new places, with the code of the static branch of `DriveKinematicHulls`. Today physics ignores a teleport of a static body. The frame takes the teleport's pose, in doubles, at the next sync point.
- Outside a tick, an edit of the tree still moves the hull. A reparent changes the tree's `Version`, so the next reconcile moves the hull. `FrameTree.SetLocalPose` does not change `Version`, so the reconcile does not see it. So at the start of each step, before the intents, physics puts each static hull whose place in Jolt differs from its frame's tree pose at that pose, and wakes the bodies around its old and new places (`FollowStaticHulls`, the old static branch of `DriveKinematicHulls`). It runs before the intents, so that it never moves a hull back from a teleport, which the tree learns only at the next sync point. `WorldWakeTests` fails without it. Only the push of the hulls on rails in every step goes.

The cost of a frame without a body is one component call per tick, in a parallel chunk, and one copy at the sync point. Today it is a serial solve at the sync point. The orbit scene has two such frames, earth and earth-surface. `ter-6r8c` adds a benchmark to `WorldTickBenchmarks` with 1,000 Kepler frames without bodies, and it records the time of the tick and of the sync point before and after the change.

## The sync point

| Step | Today | After |
|---|---|---|
| 2 | `AdvanceNumericFrames` integrates the falling frames and the orbit frames. `ApplyPhysicsOutput` applies the hand-offs, the body states and the states of `Physics` frames. | `AdvanceNumericFrames` integrates the falling frames and puts each following frame on its anchor's sample. `ApplyPhysicsOutput` applies the hand-offs and the body states. Then `FollowOwners`, the one loop of "One motion record per frame": each frame with a step record takes it. Then the float view of each frame entity is filled from its exact table. A body state of a hull never writes the float view. |
| 4 | The collision intents, then the swap of the intents | The collision intents and the `SetMotion` intents change their rows, then the intents swap and the sample table moves its buffers on |
| 5 | The destroys. A frame that is not a `Physics` frame moves to the semantic parent. `StopKeplerFrameWithoutGM` stops a Kepler frame whose new parent has no G·M. | The destroys. A frame without a dynamic hull moves to the semantic parent. A sample that names a dying frame converts first. Nothing stops a Kepler frame. A `KeplerMotion` whose new parent has no G·M writes no sample from the next tick on, and logs once. |
| 6 | The spawns. `MakeLive` places each Kepler and Spin frame by its driver. | The spawns. A new frame stands at the pose of its spawn description. It holds still there for its first step, because it has no sample yet. |
| 6b | `AdvanceDrivenFrames` places the Kepler and Spin frames and the moved static frames at the end of the tick. | Gone. Step 2 does this work. |
| 6c | The numeric update. `NumericBody.Fixed` and `Orbits` come from the driver. A new orbit frame starts from the orbit. | The numeric update. `Fixed` and `Follows` come from the hull's motion. A new following frame starts from the frame's sample of the step that ended. |
| 7 | The owners of `Physics` frames mirror their frames. | Every frame owner mirrors its frame. |
| 7b | The intents convert. | The intents and the samples convert. |
| Next `PrepareTick` | `BeginNumericStep` gives an orbit frame the orbit's acceleration at its origin, from the orbiter's G·M. | `BeginNumericStep` gives a following frame its anchor's `Acceleration` from the sample that the next step applies. |

The order holds for these reasons:

- Step 2 reads the samples that step N applied before step 4 swaps them away.
- The numeric update in step 6c still sees every bubble at the end of the tick. Each sample's pose is at `t_{N+1}`, and the destroys and the spawns still come before step 6c. So the reason for step 6b stays met: a station that orbits at 7.5 km/s is not 126 m behind the bodies beside it (see "Bubbles and ghosts" in [Numeric frames](numeric-frames.md)).
- A following frame takes its place before `ApplyPhysicsOutput` writes the rows of its members, so their rows are relative to its exact place.
- A frame without a body takes its owner's write before the destroys. So a write in the tick in which its parent dies converts with the frame. Today step 5 mirrors the owner and loses the write.
- Step 11 builds the render snapshot from the tree, which holds the end of the tick. The sample for the next step never enters the tree before that step runs.

Determinism: the components run in parallel chunks, but each one writes only its own frame's sample. A sample is a pure function of the component's description, `self.TimeAt`, the step, the frame's place and the parent's gravity in the front snapshot. No sample depends on another sample or on the order of the chunks. The sync point applies the samples in the tree's dense order, and each frame reads only its own sample. `DeterminismTests` with one and eight workers checks this.

## Physics

Kinematic targets and the snap:

- `ApplyIntents` gives a body on rails `MoveKinematic` to the sample's pose in its physics home, minus `HomeOffsetAfterStep`, as it does for a gameplay target. A frame at the origin of its following frame gets that origin as its target.
- Right after the Jolt update, before the contacts, the hand-offs and the ghosts read the end of the step, physics puts each body on rails exactly on its target (`SetRPositionAndRotation`, without a change of activation).

Predictions of a frame at the end of a step replace `PredictMotion` and `OrbitMatchesTree`. Since 2026-10-18 they all read the frame's step record (see "One motion record per frame"). The table below says where each record comes from:

| The frame's owner | Pose | Velocity for ghosts | Velocity for hand-offs | Angular velocity |
|---|---|---|---|---|
| A dynamic hull | Jolt's state after the update (`TryHullMotion`), as today | Its chord, as today | Its chord plus half a step of its acceleration (`InstantMotionOf`), as today | Jolt's |
| A kinematic hull, or no body, with a sample | The sample's pose in the tree parent. At the origin of a following frame, that origin, at rest relative to it, as today. | The sample's chord | The sample's instant velocity | The sample's |
| A static hull, a kinematic hull without a sample, or no body and no sample | The tree pose, or the pose of a teleport in the step | Zero | Zero | Zero |

Hulls and layers:

- A hull's motion is its body's motion: `BodyDesc.Motion` at spawn, and the change of motion after that. `BodyDesc.Problem` checks a hull as it checks any body, so a mesh hull must be static and a dynamic hull needs a mass. `BodyDesc.FrameHullProblem` goes.
- A hull stays on `Layers.FrameHull` and meets static bodies and other hulls. A kinematic hull still carries the dynamic hulls that rest on it.
- Only a dynamic hull hands off, as today (`MovesBetweenFrames`). `PhysicsOutputCheck` treats a hand-off or a frame state of a frame without a dynamic hull as a bug.
- `BodyRecord.Driver`, `BodyRecord.DriverVersion` and `FollowDriver` go. `ApplyHullMotion` stays, as the code that changes a hull's motion.

## Numeric frames

The orbit frame becomes a following frame. It follows any kinematic hull that moves fast or far, by the speeds and distances of free bodies: from 1,000 m/s or 2^23 m, kept down to 400 m/s or 2^22 m.

- At each sync point, in step 2, it takes the sample's place and its chord as its velocity. Its anchor's frame lies at its origin with the sample's rotation, and keeps the sample's instant velocity minus the chord, so that a read in the semantic parent gives the instant velocity. This is `FrameTree.PlaceOnOrbit` today, with the sample in place of the orbit.
- In each step, it falls with the sample's acceleration, so it ends the step on the sample to rounding.
- The numeric update makes a following frame from the frame's sample of the step that ended: its place and its chord. Before the hull's first sample, it uses the frame's place and velocity.
- When its hull has no sample, because the component went or the motion changed, it falls on as a falling frame, as an orbit frame does today when its orbiter leaves (`LeaveOrbit`).
- When its hull teleports, it jumps with the hull, and the other bodies and frames in it keep their places and velocities in the semantic frame, with a `FrameShift`, as `FrameTree.MoveOnOrbit` does today for a new orbit.
- `NumericBody.Fixed` is true for an interior and for a hull whose body is not dynamic. `NumericBody.Orbits` becomes `Follows`, true for a kinematic hull. `NumericFrameRules.OrbitsInFrame`, `BodyMode` and `GroupMode` read `Follows`. A spin hull near its parent's origin does not move, so it stays in the semantic frame's own system, as today.
- `NumericMode.Orbiting` becomes `NumericMode.Following`, because the anchor need not orbit.
- The tree mirrors the motion of each owner's hull, `FrameTree.HullMotion(frame)`, a `BodyMotion?` that is null without a hull. The World sets it at spawn and at a change of motion. Since `ter-4psc`, `FrameTree.Heir` sends every frame of a dying frame to the dying frame's semantic parent, so it needs no hull motion.
- The invariants of the tree change: the frame at a following frame's origin has a kinematic hull and a zero local position, a semantic frame under any other numeric frame has a dynamic hull, and a numeric frame has no owner and no gravity. The rule that a Kepler frame has a parent with G·M goes from the tree.
- `FrameTree.StepLength`, `PlaceOnOrbit`, `MoveOnOrbit`, `KeplerMotion`, `CreateOrbitFrame` and `InOrbitFrame` go or become their following-frame forms.

## Changes of motion

A change of motion is a swap of components and a change of the body's motion. The entity's own components change the body's motion in one tick with the intent `self.Body.SetMotion(BodyMotion)`. The motion row changes at step 4 of the sync point, as a collision change does, and physics changes the Jolt motion type at the start of the next step (`ApplyHullMotion`). So the body's motion is a double-buffered row, as the collision groups are. Only a hull can change its motion, because only a hull asks Jolt for `AllowDynamicOrKinematic` today. The table shows the changes:

| Change | What gameplay does in one tick | What happens |
|---|---|---|
| Park | `SetMotion(Static)` | The hull holds still where the step leaves it, and its frame stops in its semantic parent. It keeps its proper acceleration, so the cargo keeps its weight. |
| Unpark | `SetMotion(Dynamic)` | The hull falls or coasts from rest. |
| Engines on, Kepler to dynamic | Remove `KeplerMotion`, then `SetMotion(Dynamic)` | The hull coasts on with the velocity of its last sample. Its following frame falls on. |
| Into orbit, dynamic to Kepler | Add `KeplerMotion`, then `SetMotion(Kinematic)` | The hull holds still in its physics home for one step, as a kinematic hull without a sample does. Then the component's first run teleports it onto its orbit. |
| Kepler to static | Remove `KeplerMotion`, then `SetMotion(Static)` | The frame stops. The numeric update moves the hull out of its following frame into the semantic frame's own system. |
| A new orbit or spin | Replace the component | The new component's first run teleports the hull. A following frame jumps with it, and its other contents keep their places in the semantic frame. |

A swap has no gap: the old component's last sample covers the step before the new component's first sample. A hull keeps its body id, its system and its contacts through every change. `SetMotion` refuses, with an always-on guard, a body that is no hull, a dynamic motion for a hull without mass, a motion other than static for a mesh hull, and a character.

`World.SetFrameDriver`, `SetFrameOrbit` and `SetFrameSpin` go. Outside a tick, a game adds and removes components with `World.AddComponent` and `World.RemoveComponent`.

## Content and scenes

- `FrameSpec` becomes `FrameSpec(FrameBounds Bounds, GravityField Gravity = default)`. `FrameTree.Create` loses its driver, orbit and spin. About 517 lines in about 120 test files and 6 benchmark files change by rote.
- The Demo scenes: `earth` gets a `KeplerMotion`, `earth-surface` a `SpinMotion`, and the `station` a `KeplerMotion` with a hull whose `BodyDesc.Motion` is `Kinematic`. The hulls of the `ship` and of the cargo pods say `Dynamic`. Today a hull's description did not count, so each hull description must now state its motion. A scene gives a new frame on rails its spawn pose and velocities from the library, so that it starts near its rails. The frame holds still there for its first step, and the first sample of its component puts it on its rails.
- The scripts in `scripts/` name no driver, so they do not change.
- `docs/plans/content-formats.md`: `asFrame` loses `driver`, `orbit` and `spin`. A Kepler frame lists `KeplerMotion` among its components, with the two orbit kinds `elements` and `circular` as values of its `orbit` field. The diagnostics CT7006 and CT7007 go with the driver, because the components check themselves (see "The rails components"). The text on them in this file (lines 419, 427, 431, 471, 472, 692 and 693) goes too. `docs/plans/editor.md` (lines 406, 612, 759 and 935) and `docs/plans/content-packages.md` (lines 331 and 806) follow too.

## State dumps and saved state

`StateDump.FrameRecord(string Id, string? Parent, MotionDriver Driver)` becomes `FrameRecord(string Id, string? Parent, string Follows)`. `Follows` is `"rails"` when the frame took a sample in the step, `"physics"` when it has a dynamic hull, and `"static"` for every other frame. So a dump still shows which frames move on rails. The entities in a dump do not change: the float view of a frame entity is still filled from its exact table, so its pose and velocities are those of its sample.

No save of a running world exists (`ter-72wp`). A rails component holds only its description, and the tree holds only the sample of the last step, which the next sample replaces. So a future save stores the components' properties, and the motion goes on exactly after a load.

## Render interpolation

Nothing changes. Step 11 reads the tree's local poses, which hold the end of the tick, because step 2 puts each frame on the sample of the step that just ended. `RenderSnapshotInterpolationTests` and `RenderSnapshotRootTests` stay green without a change.

## Tests

What changes:

- `DriverChangeTests` keeps its eight scenarios as changes of motion and swaps of components (see "Changes of motion"): park, park and unpark, static to dynamic, Kepler to dynamic and back, the body id across a change, and growth after a change. Its two tests of a hull that does not match its driver become tests of `SetMotion`'s refusals.
- `FrameDriverTests` becomes a test of `SetMotion`. `FrameTreeDriverTests`, `FrameHullMotionTests` and `KeplerParentMuTests` go with the driver. The G·M rule moves to the library's tests.
- `FrameTreeOrbitFrameTests` gives samples to the tree and tests following frames.
- About 30 refusal tests change from an `ArgumentException` of the World to the log of the component: `KeplerParentGravityTests`, `WorldSpawnRefusalTests` and the refusals in `SpinFrameTests`. A spawn of a Kepler frame without G·M no longer throws, so these tests check that the frame holds still and logs once. The tests of an orbit or a spin that is not valid check the setter of the component. `KeplerFrameTests.AKeplerFrameStaysOnItsOrbitWhenItsOwnerTriesToMoveIt` and `SpinFrameTests` line 113 become tests that a write moves the frame for one tick.
- `StateDumpTests`, `OrbitSliceTests` and `DumpFileTests` follow the new `FrameRecord`. `OrbitSliceTests` checks `Follows` of the orbit frames in place of their driver.
- A test that expects a frame on its orbit as soon as it exists waits for the second sync point after the spawn (see "Other rules" in "The rails components").
- The helpers `Scenes.MoonOnKeplerOrbit`, `Scenes.FastKeplerHull`, `SpawnKeplerFrame`, `SpawnSpinFrame`, the moon in `HostTesting`, `DeterminismTests` line 169 and `InvariantWorkload` lines 65 and 66 spawn components. The churn of `InvariantWorkload` (line 378) writes the transform of any frame owner without a body.
- The tests of poses over time stay as they are: `KeplerFrameTests`, `SpinFrameTests` lines 37 to 128, `WorldFacingFrameTests`, the scenarios of `NumericFrameOrbitTests`, `StationSliceTests` with `HullOffEarthOrbit` below 1e-6 m, and both determinism tests.

What the new tests prove:

1. The library's sample matches the orbit: its chord equals the change of place over the step to rounding, and its angular acceleration equals the change of the angular velocity.
2. A frame on rails stands at `orbit.StateAt(World.Time, mu)` to the last bit at every sync point from the second one after its spawn, for ten minutes, with a body and without, in its semantic parent and in a following frame.
3. After each step, the hull's place in Jolt equals its target exactly.
4. A body that sticks out of a frame on rails has a ghost with the sample's chord, and a body that leaves the bubble hands off with the sample's instant velocity.
5. A new frame on rails holds still at its spawn pose for its first step, and stands on its rails at the second sync point after its spawn. A swap of components has no gap.
6. A static hull follows a teleport and wakes the bodies around it. A frame without a body follows its owner's write, also when its parent dies at the same sync point.
7. A frame on rails whose samples stop holds still, and its following frame falls on.
8. A test component that drives a kinematic hull at 2 km/s along a straight line gives the hull a following frame, and the hull rests in it. A crate that drifts out of the hull's frame joins that following frame (`ter-zg77`).
9. The benchmark of 1,000 Kepler frames without bodies reports the tick and the sync point before and after.

Every implementation brief asks for hand mutations, as `CLAUDE.md` says.

## Order of the work

The epic listed three work tickets. This plan keeps their order and adds `ter-ru5u`, so that each ticket ends green and stays small enough for a few agents in a row.

1. `ter-k8cg`, the library. No change of behavior. The functions in "The rails library" move, with their tests (`KeplerOrbitTests`, `KeplerAttitudeTests`, `FrameSpinTests`, and the chord and acceleration cases of `NumericFrameRulesTests`). The new functions and `MotionSample` come with tests against today's `PlaceOnOrbit`, `FrameTree.KeplerMotion` and `ApplySpin`. The tree and the Demo call the library.
2. `ter-6r8c`, the samples and the components. The sample table, `Self.MoveOnRails`, `Self.TimeAt` and `Self.OwnedFrame`. `KeplerMotion` and `SpinMotion`, with their checks, their first run and the proper acceleration of the sample. `FollowOwners` in step 2, and the end of step 6b. Physics reads the samples for targets and predictions, and snaps bodies on rails. `DriveKinematicHulls`, `PredictMotion` and `OrbitMatchesTree` go. The orbit frame follows its anchor's samples, and `Orbits` reads "the hull is kinematic". The tree's orbit and spin, `PlaceOnOrbit`, `MoveOnOrbit`, `KeplerMotion`, `SetFrameOrbit` and `SetFrameSpin` go. Static hulls take teleports, and owners without a body write their transforms. The scenes, the benchmark, and the sections of `ARCHITECTURE.md` that this changes. In this ticket the enum stays only to choose a hull's motion, so `Kepler` and `Spin` both mean a kinematic hull. Until the next ticket, a change of motion is `SetFrameDriver` plus a swap of components with `World.AddComponent` and `World.RemoveComponent`.
3. `ter-ru5u`, "A body's motion changes after spawn, and the MotionDriver enum goes". It depends on `ter-6r8c`. The change of motion with the intent `SetMotion`, the hull's motion from its body, `FrameTree.HullMotion` and the tree's invariants, `NumericBody.Fixed` from the body's motion. `MotionDriver`, `FrameHullMotion`, `FrameHullProblem`, `SetFrameDriver`, `FollowDriver` and the driver in `FrameSpec` and `FrameTree.Create` go, with the rote test edits. The rewrite of `DriverChangeTests` and `FrameDriverTests`, the state dumps with `FrameRecord(Id, Parent, Follows)`, and a row in the table "Where the user changed my proposals" in `CLAUDE.md`.
4. `ter-zg77`, now "A following frame follows every fast kinematic hull". It depends on `ter-ru5u`. `ter-6r8c` already gives every kinematic hull a following frame. This ticket proves it for motion that is no orbit (test 8), renames `NumericMode.Orbiting` to `Following` and `Orbits` to `Follows`, and rewrites the sections "Orbit frames", "Anchors" (rule 3), "Frame owners in a numeric frame", "Bubbles and ghosts" and "Limits" of [Numeric frames](numeric-frames.md), and the numeric rows of `ARCHITECTURE.md`.

`ter-e30t` and `ter-f620` stay open outside this epic, as questions of what `KeplerMotion` and `SpinMotion` do when the parent dies.

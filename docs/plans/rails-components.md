# Rails components

Epic: `ter-ozm8`. The design ticket `ter-skca` wrote this plan. The work tickets are `ter-k8cg`, `ter-6r8c`, `ter-ru5u` for changes of motion, `ter-xdt8` and `ter-zg77` (see "Order of the work"). `ter-xdt8` brings rails motion to every entity.

## Goal

Every frame with an owner follows its owner. A frame whose owner has a dynamic hull follows the hull, as a frame with the `Physics` driver does today. A frame on rails, on a Kepler orbit or in a steady spin, follows its owner's motion intent. In each tick, a component of the owner writes a full sample as that intent. Since `ter-xdt8`, rails motion moves any entity, not only a frame owner (see "Rails motion for every entity"). A static frame follows its owner when gameplay moves the owner. The frame tree keeps no driver, orbit or spin, and physics never pushes a hull after its frame.

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
| Motion sample | The struct `MotionSample`: the pose at the end of a step, and the velocities and accelerations that go with it (see "The motion sample"). A full sample and a step record are motion samples. |
| Motion intent | The union `MotionIntent` in an entity's intent, the field `PhysicsIntent.Move`: how the entity moves in the step that applies the intent. It holds no case, an end pose, a full sample or a steer target (see "Rails motion for every entity"). |
| End pose | The case `EndPose(DPose Pose)` of a motion intent: the pose at the end of the step, relative to the intent's frame. `self.MoveKinematic` writes it. |
| Full sample | The case `MotionSample` of a motion intent: one step of motion, with the pose at the end of the step and every velocity and acceleration that goes with it. `self.MoveOnRails` writes it. A rails component writes one in each tick. |
| Steer target | The case `SteerTarget(DPose Pose)` of a motion intent: a pose that physics steers a dynamic body toward over the step, relative to the intent's frame. `self.Body.SteerTo` writes it. |
| First motion intent | The motion intent that a spawn description carries for its new entity, the property `SpawnDesc.FirstMove`: the intent that the entity would have written in the spawn's tick. The step after the spawn applies it (see "The first motion intent of a spawn"). |
| Step record | A `MotionSample` that describes an entity's motion over one step. An end pose or a full sample gives the record of the step that applies it. Physics measures the record of a dynamic hull. A steer target gives none. |
| Follow kind | How a frame took its record at the last sync point: `Rails`, `Kinematic`, `Physics` or `Static`. The frame tree keeps it, one byte per frame. |
| Step N | The physics step that runs during tick N, from `t_N` to `t_{N+1}`. Gameplay of tick N reads `self.Time = t_N`. |
| Follow | At the sync point, a frame takes its pose and velocities from its owner |
| Hull | The body of a frame's owner, in the Jolt system of the frame's tree parent. A root frame has no hull. Its owner's body is only the frame's static interior. |
| Body on rails | A kinematic body whose motion intents are the full samples of a rails component |
| Following frame | A numeric frame that holds a fast or far kinematic hull at its origin. At every sync point, it lands on the step record of the hull's motion intent. Today's orbit frame (`NumericMode.Orbiting`) is the only kind so far. |
| Chord velocity | The displacement over a step, divided by the step. Jolt reads a stored velocity this way. |
| Instant velocity | The velocity at one moment |
| Proper acceleration | What an accelerometer at a frame's origin reads, in the frame's own axes: the frame's acceleration minus the gravity that acts at its place. The bodies in the frame feel the frame's own field minus this value. Since `ter-xdt8` the frame tree derives it from the frame's step record (see "The proper acceleration of a frame"). |

## Decisions

The user made the decisions marked (User).

| Question | Decision | Why |
|---|---|---|
| Where the state of rails motion lives | On components of the frame's entity, `KeplerMotion` and `SpinMotion`. The tree keeps no driver, orbit or spin. Every frame with an owner follows its owner. (User) | One rule for every frame: the rule that a `Physics` frame follows today. |
| What a rails component holds | An exact description as a function of time: orbit elements and an epoch, or a tilt, a rate, a phase and an epoch. No integration state. (User) | A sample at any time is exact, so no error builds up, and a save needs only the description. |
| How the engine gets the motion | In each tick, the component writes one full sample for the end of the next step, as its entity's motion intent, with `MoveOnRails`. The engine never calls into a component at the sync point. It reads the intent. (User) | The solve runs in the parallel part of the tick. The serial sync point only copies samples. |
| What a sample holds | The pose at the end of the step, the chord velocity over the step, the instant velocity at its end, the acceleration over the step, and the exact angular velocity and angular acceleration, all in doubles. (User) | Each reader needs another field (see "The motion sample"). |
| How exact a body on rails is | A body on rails ends each step exactly on its target, and its frame stands exactly on the sample at the sync point. (User) Physics does the snap right after the Jolt update, because the sync point cannot reach Jolt. Since `ter-xdt8`, physics snaps every kinematic body that an end pose or a full sample moves. | Jolt reaches a kinematic target only to the float rounding of the velocity, about 4 µm per step at 7.6 km/s. With the snap, a place on an orbit stays exact to the last bit. |
| A frame without a body | It takes the same motion intent as a hull. The sync point applies it at step 2, to the frame tree row. (User) | Planets and rooms follow the same rule as hulls. |
| A static frame | It needs no component. Gameplay moves its owner. (User) | A static frame moves only when gameplay moves it. |
| What replaces `MotionDriver` | Nothing. The enum and `BodyDesc.FrameHullMotion` go. The motion of the owner's body says how a frame follows. (User) | A second value beside the body's motion can disagree with it. |
| A change of motion | A swap of components plus a change of the body's motion. (User) The row "How a body changes its motion after spawn" below says how the body's motion changes. | Today only a frame driver changes a body's motion after spawn. |
| The rails math | Public pure functions in the namespace `Tertius.Frames.Rails`, in `src/Tertius.Core/Frames/Rails/`. Every function lives in its struct: an instance method for a function of an orbit or a spin, a static method of `KeplerOrbit` for two-body math that takes no orbit elements (see "The rails library"). (User, 2026-10-18) | Components and engine code call one copy. It is public, because the Demo and the Host cannot see the internals of Core. |
| How a frame takes its motion | One step record per frame, a `MotionSample`, for every kind of owner. Physics releases it for every hull (a dynamic hull, a kinematic hull from the motion intent it applied, a static teleport). For an owner without a hull, the sync point derives it from the owner's motion intent, or takes the owner's transform write. One loop, `FollowOwners`, applies it, and physics predicts every frame from it. `FrameState` goes. (User, 2026-10-18) | One rule for every frame, with no special path for physics. See "One motion record per frame". |
| The reverse path | `DriveKinematicHulls` and step 6b (`AdvanceDrivenFrames`) go. (User) | The tree no longer moves a frame on rails, so nothing pushes a hull after it. |
| Which epic comes first | This epic, then spin gravity (`ter-s9m0`). (User) | Spin gravity reads the exact angular velocity and angular acceleration from the sample. |
| A fast or far body on rails | It lives in a following frame. At each sync point, the frame takes its place and velocity from its anchor's step plan. In each step, it takes the acceleration that lands it on that plan (see "The step plan of a following frame"). Every kinematic hull gets one by the speeds and distances of free bodies, not only a Kepler hull. `NumericBody.Fixed` and `Orbits` come from the hull's motion. | An anchor needs a home in which it is at rest (rule 3 of "Anchors" in [Numeric frames](numeric-frames.md)). With motion on components, the tree cannot tell a Kepler hull from another kinematic hull. |
| The velocity of a ghost of a frame on rails | The sample's chord. A hand-off uses the sample's instant velocity. | Ghosts use chords and hand-offs use instant velocities for every other body ("Bubbles and ghosts" in [Numeric frames](numeric-frames.md)). Today the ghost takes the orbit's instant velocity, 7 cm/s off in low orbit. The sample has both, so this limit goes. |
| The velocity that the tree keeps for a frame on rails | The instant velocity, as today. A frame with a dynamic hull keeps the hull's chord, as today. | Reads in a frame do not change. Physics reads the sample, so it needs no conversion. |
| A transform write to a bodyless owner on rails | Allowed. It moves the frame at the sync point of the tick, and the next sample puts the frame back on its rails. | No driver owns the pose, and the entity's own components decide. Today the World refuses the write and records an error. |
| How a component reads the time of its sample | A new `Self.TimeAt(long tick)` returns the World's time of a tick, the same double that `World.Time` holds then. | A sum such as `self.Time + 2 * self.DeltaTime` can differ from the World's time in the last bit, and a place on an orbit must match it exactly. |
| How rails motion starts | A spawn description can carry the new entity's first motion intent, `SpawnDesc.FirstMove`, beside `LinearVelocity` and `AngularVelocity`: the motion intent that the entity would have written in the spawn's tick. Step 6 of the sync point stores it in the new entity's front intent, so the step after the spawn applies it, and a station spawned in orbit is on its rails from its first step. The spawner computes it: for a Kepler station, from the same orbit that it gives the `KeplerMotion`. A spawn without one holds still at the pose of its description for its first step, as a frame without a motion intent does, and the first run of its component puts it on its rails. (User, 2026-10-18) Until then nothing wrote a first sample, and every new frame on rails held still for its first step. (User) | By the rule of "How a frame gets its proper acceleration", a frame that holds still feels the gravity at its place, reversed. So the contents of a new station that holds still for its first step fall for one step: they gain 0.145 m/s at 400 km above Earth. The first motion intent removes that step wherever the spawner can compute the motion. The engine still never calls into a component at the sync point, a blueprint, content and a mod's own component still work without one, and a swap of components has no gap. |
| How a body changes its motion after spawn | An intent, `self.Body.SetMotion(BodyMotion)`, that the entity's own components write in a tick. (User) Physics changes the Jolt motion type at the start of the next step, which applies the intent (`ApplyHullMotion`). The hull's motion has one store, the frame tree (`FrameTree.HullMotion`), and it lags like the collision rows: at step 4 of the sync point after that step, it takes the change from the intents that the step applied. The motion of a step that has not finished is derived from the pending intent, never stored: `FrameTree.HullMotionInStep` for the running step and `World.NextStepHullMotion` for the next one. (User, 2026-10-18) | Gameplay can start a station's engines, and the hull keeps its body id, its system and its contacts. An entity changes only itself. Data flows the same way for every row: the state that a tick reads describes the last finished step, and each reader that asks about a step in flight derives its motion from the intent. My proposal had the stored motion lead physics, as the motion of the step in flight that every reader reads directly. |
| Where the checks of rails motion live | In the components. A property setter refuses an orbit or a spin that is not valid, with an `ArgumentException`. A `KeplerMotion` whose parent has no G·M holds its frame still and logs once. A rails component whose entity refuses a full sample logs the guard's reason once (`self.MotionIntentProblem`) and writes no sample. The World checks nothing of an orbit or a spin. (User) | One place for each rule, and the same rules for a mod's own component. Without a driver, the World does not know which frames orbit. |
| How a frame gets its proper acceleration | One rule for every step record: the frame's acceleration minus the gravity that acts at its place, in the frame's own axes. The gravity at its place is its semantic parent's field there minus the parent's own proper acceleration. The numeric frames between the two count in the record's acceleration (see "The proper acceleration of a frame"). Gameplay and components never write it: `MotionSample` has no such field, the rails library's `Sample` functions take none, and `FrameTree.ProperAcceleration` is a value read. A frame that holds still in a gravity field feels that gravity reversed, so a parked cabin has weight. A frame on a Kepler orbit in a parent that falls freely feels exactly zero. Physics measures a dynamic hull's value by the same rule. (User, 2026-10-18) Until then the sample carried it: `KeplerMotion` gave zero, and `SpinMotion` gave a property that a component copied from the frame. The two rows below give the user's answers to the two choices of the rule. | One source for the value: it follows from the motion and the field, so no writer can make it disagree with them. A parked cabin, a turntable on the ground and a static hull get the weight of their place with no component that copies a value. A ship that parks in free space does not keep a thrust that no longer acts. Before, a static frame kept whatever value it last had, and zero if it never had one. |
| Which proper acceleration of the parent the rule reads | The parent's value as the step found it: the value stored before step 2, which physics used for the parent's bodies during the step. `FollowOwners` derives the new value of every frame from the stored values, and the tree writes the new values after the loop (see "Which proper acceleration of the parent"). So a change of a parent's value reaches its children one sync point later, and moves one level down the chain in each step. (User, 2026-10-18) | The rule reads the gravity that acted at the frame's place during the step, which is what physics applied there. So every child of a parent, a dynamic hull among them, agrees in every step, and the order of the visits does not matter. The other design read the value that the parent took earlier in the same pass. It needed no extra code, and stage 1 used it until this answer. |
| The proper acceleration of a new frame | Zero during its first step, as `FrameTree.Create` sets it. The first step 2 after the spawn derives it by the rule, from the frame's first record or from its rest, as for every frame (see "The proper acceleration of a new frame"). (User, 2026-10-18) | The pure model: one rule from a step record, with no guess about a step that has not run. Its cost is one step without weight: a crate spawned in the air in a parked cabin starts to fall one step late. The other design set the value of the frame's rest at the spawn. It needed one more rule per kind at the spawn, and the parent's field at the creation of a frame outside a tick. |
| Which entities move on rails | Every entity that the guard lets through, not only a frame owner: a kinematic body, hull or plain, an entity without a body, and the owner of a root frame. (User, 2026-10-18) | Frames follow their owner entity, and the rails components move their entity. Before `ter-xdt8`, `MoveOnRails` refused an entity that owns no frame, and physics read samples only for hulls. |
| Where the motion intent lives | In the entity's intent: the field `PhysicsIntent.Move`, which replaces `PhysicsIntent.Target` and the flag `IntentFlags.KinematicTarget`. It has the life of the rest of the intent. The swap at step 4 exchanges the two intent buffers. Without a physics backend, the swap keeps `Move`. (User, 2026-10-18) Until `ter-xdt8`, the sample table held the samples: a table of its own, indexed by the frame's index, with three buffers. | One store, and data that is used together stays in one struct. A sparse store beside the intents needs an index. The swap, the release of a slot, the rule without a backend and both conversions must keep it coherent. The intent grows from about 240 to about 380 bytes: about 27 MB more at 100,000 slots, on 48 MB. The buffer exchange moves about 380 bytes per writer, where today's copy and two clears move about 720. The benchmark of the swap at 100,000 entities tells whether a sparse store is needed. |
| What a motion intent can be | A custom union, `MotionIntent`, with three cases: an end pose (`self.MoveKinematic`), a full sample (`self.MoveOnRails`) and a steer target (`self.Body.SteerTo`). (User, 2026-10-18) | An end pose is not a full sample with zero velocities. Such a sample tells a frame that it stands still. The steer target keeps what `MoveKinematic` does for a dynamic body today: physics gives the body the velocities that carry it to the pose over the step. |
| Which entity takes which case | One guard, `World.MotionIntentProblem`. It allows an end pose and a full sample for a kinematic body, an entity without a body and the owner of a root frame. It allows a steer target only for a dynamic body. A static body, a character and an attached entity refuse every case. (User, 2026-10-18) A spawn's first motion intent passes the same rules, judged from the spawn description (see "The first motion intent of a spawn"). | An end pose and a full sample are the result itself, so physics or the sync point puts the entity exactly there. A force, an impulse and a steer target are inputs that physics integrates for a dynamic body. The writers, the step-4 drops and the rails components use one rule, so they never disagree. |
| How a kinematic body moves | Only by its motion intents and teleports, hull or not. It holds still in a step without a motion intent. `SetVelocity` and `SetAngularVelocity` throw for every kinematic body. A spawn refuses a velocity on a kinematic body that owns no frame. A kinematic hull keeps its spawn velocity as its frame's velocity. (User, 2026-10-18) | The pure model: one rule for every kinematic body, and one path per kind. A second way to move a plain kinematic body is an exception that no misbehavior calls for. Its cost is test code: about 10 tests that drift a kinematic body with a velocity use a test component that writes an end pose in every tick. |
| What the frame tree row keeps of a step record | What `FrameTree.Follow` writes today: the record's pose, instant velocity and angular velocity, and the frame's proper acceleration, which it now derives from the record (see "How a frame gets its proper acceleration"), plus the follow kind. The record's chord, acceleration and angular acceleration are gone after step 2, with the third buffer of the sample table. (User, 2026-10-18) | Once a following frame lands on its step plan, no reader in this epic needs the three fields. Kept, they are three values that six writers must keep coherent, for a reader that does not exist yet. When its design needs one, spin gravity adds its own store (see docs/plans/spin-gravity.md). |
| The first step of a new following frame | A limit of the landing rule. A new following frame starts with its anchor's instant velocity, so its first planned step takes about half the field as its acceleration, and its system shows the other half for that one step: a character at rest beside a new fast hull takes the walking form for that step. Decided on 2026-10-19, by the orchestrator of `ter-xdt8` (see "The first step of a new following frame"). | Two reasons. The contents' motion in the semantic frame stays as it is, because the frame's acceleration cancels out of it, so the only effect is the gravity that the contents' own system shows for one step. A builder later found one body for which that motion does not stay exact: a body that spawns beside a hull with a first motion intent in the same batch (see "Open: a velocity that a spawn gives in the first planned step of a new following frame"). The other answer started a new following frame with its anchor's chord. That needs a stored chord in the frame tree row, which the decision "What the frame tree row keeps of a step record" rules out. |
| What a state dump records about a frame | `FrameRecord(Id, Parent, Follows)`. `Follows` is `"rails"`, `"kinematic"`, `"physics"` or `"static"`: how the frame followed its owner at the dump's sync point. `"kinematic"` comes with `ter-xdt8`, for an end pose. | A dump still shows which frames move on rails. The tree stores the follow kind of each frame. |

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
- `KeplerOrbit.Sample(double mu, double stepEnd, double step)`. It solves Kepler's equation once, at `stepEnd`, for the pose, the instant velocity and the angular velocity. The chord comes from `Chord`. The acceleration is `Acceleration` at the place where the step starts, the end place minus the chord times the step. That is the step acceleration of an orbit frame today, to rounding. The angular acceleration comes from `AngularAccelerationAt`, at the start of the step. Since `ter-xdt8` the sample has no proper acceleration: the frame tree derives it from the sample's acceleration (see "The proper acceleration of a frame").
- `FrameSpin.Sample(Double3 place, double stepEnd, double step)`. The pose is the place with `RotationAt(stepEnd)`. The chord, the velocity, the acceleration and the angular acceleration are zero. The angular velocity is the spin's. Until `ter-xdt8` it also took a proper acceleration, which it put into the sample.

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

- `Orbit` = the orbit elements and their epoch, relative to the entity's row frame, in its axes. They hold no G·M.
- `self.Frame` = the entity's row frame. For a frame owner, it is the frame's semantic parent. It is null only for the owner of a root frame.
- `KeplerOrbit.MuFrom` = the G·M of the parent's point gravity, or 0.
- `self.TimeAt(tick)` = new: the World's simulation time at the start of a tick, in seconds.
- `stepEnd` = the end of the step in which this tick's intent acts, in seconds.
- `self.DeltaTime` = the step length in seconds, a float that widens to the same double that the World uses.
- `self.MoveOnRails` = new: writes the sample as the full-sample case of the entity's motion intent (see "Rails motion for every entity").
- `...` = three rules that the component applies before it writes the sample:
  - When `mu` is 0, the parent has no point gravity with a G·M above 0. The component logs once, writes no sample and returns, so the entity holds still.
  - When the entity refuses a full sample, the component logs the guard's reason once, writes no sample and returns. It reads the reason with `self.MotionIntentProblem`, the same read that `MoveOnRails` checks.
  - If the entity's body is kinematic in the next step (`self.Body.NextStepMotion`), the component teleports the body onto its rails on its first run. So the body jumps to its orbit and does not sweep through the bodies in between. An entity without a body needs no teleport, because its first sample alone puts it on its rails. After a first motion intent from the same orbit (see "The first motion intent of a spawn"), the teleport puts the body where it already stands, so it moves nothing.

`SpinMotion` holds a `FrameSpin`. It does the same with `Spin.Sample(place, stepEnd, self.DeltaTime)`. It has no property for a proper acceleration: a turntable that holds its place on the ground feels the ground's gravity reversed by the rule of "The proper acceleration of a frame", so its cargo keeps its weight with no value to copy. Until `ter-xdt8`, `SpinMotion` had a property `ProperAcceleration`, and a component that started a turntable copied the frame's value into it. It needs no G·M, so it has only the second and the third rule of the `...`. It reads its place with the public read `self.PoseAtNextStepStartIn(self.Frame).Position`: the place of its entity at the start of the step of its sample. That is the end pose of the motion intent that the step beside the tick applies. Without one, it is that step's teleport pose, or else the entity's place now. A read of the place now alone is one step behind: after a swap from a moving motion, a spin from it starts one step of the old motion back. The read is public, so a mod's rails component can do the same. It works for an entity without a frame too, so a spin turns such an entity in place. A spin does not move its entity, so the place stays the same from tick to tick. A destroy of the parent moves the entity up, and the component reads the new place.

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
- A rails component runs first in its entity (`Priority` -1000). So another component of the entity can stop it in the same tick: a later motion intent wins, and a change of motion drops a sample that the new motion refuses.
- When the parent dies, the frame moves up as today, and the component reads the new parent's G·M in the next tick. It keeps its elements, so a Kepler frame jumps to the same orbit around the new parent, as today. When the new parent has no G·M, the component holds the frame still and logs once. `ter-e30t` and `ter-f620` become questions of what the components do then.
- A new frame on rails whose spawn description carries a first motion intent is on its rails from its first step, and stands on them at the first sync point after its spawn (see "The first motion intent of a spawn"). Without one, it starts one tick late. A component that is new on an entity runs first in the tick after it arrives, and its first sample acts one step later. A new frame goes live at a sync point, and its first step runs before any of its components runs, so without a first motion intent that step has no motion intent. The new frame holds still at the pose of its spawn description for that step, as a frame without a motion intent does. At the next sync point it still stands at that pose. At the one after that, it stands on its rails. Until then, the frame is one step of its motion behind its rails: 500 m for Earth around the sun. A fast hull is closer, about 1.2 mm in low orbit, because it holds still in the following frame that the numeric update gives it at spawn, and that frame falls along the orbit. A swap of components has no gap.
- The checks of an orbit or a spin live in the components, not in the World. The World keeps none of today's checks of Kepler frames: its refusals of a Kepler frame whose parent has no point gravity with a G·M above 0, at spawn and in `SetFrameDriver`, and the refusal in `SetFrameGravity` of a new G·M for the parent of a Kepler frame. So a spawn of a Kepler frame without G·M does not throw. A new G·M of a parent changes the orbits in it at once, so the frames on them jump. `World.AssertInvariants` loses its rule for Kepler frames. The setters of the properties of the two components refuse an orbit or a spin that is not valid, with an `ArgumentException`.

## The motion sample

```csharp
public readonly record struct MotionSample(
    DPose Pose, Double3 ChordVelocity, Double3 Velocity, Double3 Acceleration,
    Double3 AngularVelocity, Double3 AngularAcceleration);
```

Every field is relative to the intent's frame, `self.Frame`, in its axes. For a frame owner, that is the frame's semantic parent. A root frame has no parent, so its sample is in the root's own space, as its local pose is today. Since `ter-xdt8` the struct has no `ProperAcceleration`: the frame tree derives a frame's proper acceleration from its step record (see "The proper acceleration of a frame").

| Field | Meaning | Physics | Frame tree at the sync point | Numeric update | Reads and render |
|---|---|---|---|---|---|
| `Pose` | The pose at the end of the step, in m | The body's kinematic target, and the frame's predicted pose for ghosts and hand-offs | The frame's local pose, exactly | The place of the following frame | Through the tree pose, as today |
| `ChordVelocity` | The change of place over the step, divided by the step, in m/s | The velocity of the frame for its ghosts | None | The velocity of the following frame | None |
| `Velocity` | The instant velocity at the end of the step, in m/s | The velocity of the frame for hand-offs | The frame's linear velocity. In a following frame, the frame keeps the velocity minus the chord. | The hull's speed for the thresholds, through the tree | `VelocityIn` gives it, as today |
| `Acceleration` | The frame's acceleration during the step, in m/s² | None since `ter-xdt8` | The frame's proper acceleration derives from it, with the gravity at the place where the step starts (see "The proper acceleration of a frame"). The row does not keep it. | None since `ter-xdt8`: the step acceleration of a following frame lands it on its step plan, from the plan's chord (see "The step plan of a following frame") | None |
| `AngularVelocity` | The angular velocity at the end of the step, in rad/s | The angular velocity of the frame for ghosts and hand-offs | The frame's angular velocity, narrowed to the tree's float field | None | `AngularVelocityIn` gives it, as today |
| `AngularAcceleration` | The angular acceleration during the step, in rad/s² | Nothing yet. Spin gravity reads it. | None | None | None |

`self.MoveOnRails(in MotionSample sample)` is a member of `Self`. It writes the sample as the full-sample case of the entity's motion intent (see "Rails motion for every entity"). Its rules:

- The last motion intent written in a tick wins, whatever its case.
- One guard decides for every entity, `World.MotionIntentProblem`. A full sample passes for a kinematic body, hull or plain, for an entity without a body, and for the owner of a root frame. Else the call throws an `InvalidOperationException` with the guard's reason.
- An entity without a body can write a sample. The sync point applies it at step 2: to the frame tree row for a frame owner, and to the transform row for an entity without a frame.
- The sample lives in the entity's intent, the field `PhysicsIntent.Move`, with the life of the rest of the intent. Tick k writes it, and step 4 of the sync point of tick k swaps it into the front intents. Step k + 1 applies it, and step 4 of the next sync point clears it. So an entity whose component writes no sample in a tick has no motion intent in the step that follows. After step 2, the frame tree row holds the record of the step that ended.
- A motion intent converts with the rest of the intent. At step 5, an intent that names a dying frame converts into its entity's new frame (`World.ConvertIntentsOfDyingFrames`). At step 7b, an intent whose entity changed frame in the sync point converts (`World.ConvertMovedIntents`). A full sample converts through `MotionSample.Converted`. The pose, the velocities, the angular velocity and the angular acceleration convert by the rule of a hand-off. They use the motion of one frame relative to the other (`FrameMotion`). The acceleration converts with the acceleration of one frame relative to the other (`FrameTree.AccelerationRelative`). The tree keeps the acceleration of a frame in its parent only for a numeric frame, its step acceleration. A semantic frame between the two frames adds no acceleration of its own yet. That is a known gap. Since `ter-xdt8` no reader meets it, because the step acceleration of a following frame comes from the chord of its step plan. A turning frame adds no rotation terms, and the angular acceleration only turns, until spin gravity (`ter-s9m0`, see docs/plans/spin-gravity.md, "Conversions between frames"). A sample that converts misses for one step by the relative motion of the two frames, as a kinematic target does.

Every kinematic body, hull or plain, moves only by its motion intents and teleports. It takes `MoveOnRails`, `MoveKinematic` and `Teleport`, and the teleport applies first. `SetVelocity` and `SetAngularVelocity` throw for every kinematic body. A spawn refuses a velocity on a kinematic body that owns no frame, and a kinematic hull keeps its spawn velocity as its frame's velocity. Since `ter-xdt8`, a kinematic hull takes `MoveKinematic`: the constant-velocity record of its end pose tells its frame how to follow. In a step without a motion intent, a kinematic body holds still: physics sets its velocity to zero, as it stops a kinematic body after its last target today. A kinematic hull holds still in its tree parent.

## One motion record per frame

Every frame takes its motion the same way, whatever its owner is. The user chose this on 2026-10-18: one record type, one loop that applies it, and one prediction rule in physics. An entity writes intents: forces, a motion intent (`MoveOnRails`, `MoveKinematic`, `SteerTo`) and `Teleport`. Physics moves a body, and for a hull it releases the frame's record. For an owner without a hull, the sync point makes the record at step 2: from the owner's motion intent, or from its transform write.

The step record of a frame is a `MotionSample` that describes the frame's motion over the step that just ended. Its owner's kind decides who fills it:

| Owner | Who fills the step record |
|---|---|
| A dynamic hull | Physics, right after the Jolt update: the hull's pose, its chord (Jolt's velocity), its instant velocity (the chord plus half a step of its acceleration, `InstantMotionOf`), its acceleration and its angular acceleration measured from the change of Jolt's velocities over the step, and Jolt's angular velocity. Beside the record, physics releases the proper acceleration that it measured by the rule of "The proper acceleration of a frame" (`FrameStepRecord.ProperAcceleration`). A hull that hands off in the step has the record relative to its new parent, in the new parent's axes: the acceleration is the one that made Jolt's chord there, and the angular acceleration turns into the new axes as a direction, as step 5 converts the angular acceleration of a sample (`FrameMotion.AngularAccelerationToParent`, `ter-nw4p`) |
| A kinematic hull with an end pose or a full sample | Physics, after it snaps the hull onto the end pose: the record of the motion intent that it applied (`PhysicsIntent.StepRecord`). A full sample gives itself, with every field exact, so the sample's doubles and not Jolt's float velocities. An end pose gives the constant-velocity motion from the hull's tree pose at the start of the step, or from its teleport pose. |
| No hull (no body, or the owner of a root frame), with an end pose or a full sample | The sync point, at step 2: the record of the owner's front intent, from the frame's tree pose (`FrameTree.StepRecordInStep`) |
| A static hull, or a kinematic hull without a motion intent, that teleported in the step | Physics: the teleport's pose, at rest in the hull's home, with zero velocities and accelerations relative to it |
| No body, and the owner wrote its transform in the tick | The sync point: the written pose, with zero velocities and accelerations. This one acts in the same tick, because no physics step stands between the write and the frame. |
| Anything else | No record. The frame holds still in its tree parent. |

Rules:

- Physics hands back a record for every hull whose frame moved in the step, in the step output, in place of `FrameState`, which goes. A record from physics is relative to the frame's tree parent, as `FrameState` is today, so a frame with a dynamic hull keeps its exact place in a numeric home. For a kinematic hull at the origin of its following frame, that is the origin with the record's rotation, and the record's instant velocity minus its chord. Under a numeric parent, a record from physics keeps its velocity relative to the parent's frame velocity, the chord of the step, as the tree does, so that a read in the semantic parent gives the instant velocity. Physics keeps a velocity relative to the parent's instant velocity, so each record that physics releases adds the parent's instant velocity past its chord (`FrameTree.HomeInstantVelocityOverChord`, `ter-mjt5`). For a falling frame that is half a step of its acceleration. For a following frame whose hull has a motion intent, it is the step plan's instant velocity minus its chord, exactly, which the frame keeps from its step plan: half a step of the orbit's pull would miss the change of the pull over the step, 3 cm/s on a fast test orbit. So every frame in a following frame reads its velocity relative to the hull's, and the record of the hull, at rest at the origin, is that term alone. A motion intent is relative to the frame's semantic parent. These agree for a frame on rails in its semantic parent.
- In step 2, `FollowOwners` is the one loop: each frame with a step record takes it, in the tree's dense order. The frame takes the pose, the instant velocity (minus the home's frame velocity, its chord, under a numeric parent) and the angular velocity narrowed to the tree's float field. Its proper acceleration derives from the record by the rule of "The proper acceleration of a frame", and a dynamic hull's comes from physics. A frame without a record holds still in its tree parent and moves with it, so under a numeric parent it keeps the parent's instant velocity past its chord (`FrameTree.HoldStill`, `ter-mjt5`). Its proper acceleration derives by the same rule from that rest. The tree keeps the instant velocity for every frame. Today it keeps the chord for a frame with a dynamic hull, so that frame's tree velocity changes by half a step of its acceleration. If a behavior, not only a pinned number, depends on the chord, that is the misbehavior that would justify an exception.
- After step 2, the frame tree row holds the record of the step that ended. `FrameTree.Follow` writes the record's pose, instant velocity and angular velocity, the proper acceleration that it derives from the record, and the follow kind, which says what made the record. The row does not keep the record's chord, acceleration and angular acceleration (see the decision "What the frame tree row keeps of a step record"). Until `ter-xdt8`, `World.KeepRecord` put each record into the applied buffer of the sample table, and the numeric update read it from the followed buffer. Now a following frame reads its step plan, and a new following frame starts from its anchor's place and instant velocity in the tree row.
- Physics predicts every frame at the end of the step from its step record: the pose from the record's pose, ghosts take the record's chord velocity, hand-offs take its instant velocity, and the angular velocity is the record's. A frame without a record predicts its tree pose with zero velocities. This replaces the three rows of the table "Predictions of a frame at the end of a step".
- The entity and its frame are one entity with two tables: the frame tree row is the exact table, and the transform row is the float view of the same entity. After `FollowOwners`, the float view of each frame entity is filled from its exact table. A body state of a hull never writes the float view.

## Frames without a body, and static frames

A frame without a body follows its owner's motion intent, or its owner's transform write, or holds still. A frame with no motion intent and no body is a static frame. This replaces the `Physics` frame without a body, which nothing moves today.

A static frame follows its owner:

- Without a body, the owner writes `self.SetTransform`. The frame takes the pose at the sync point of the same tick, as today. The rule that only the owner of a `Static` frame can write its transform goes. Any frame owner without a body can write it, unless it is attached.
- With a static hull, the owner writes `self.Body.Teleport`. Physics moves the static hull at the start of the next step and wakes the bodies around its old and new places, with the code of the static branch of `DriveKinematicHulls`. Today physics ignores a teleport of a static body. The frame takes the teleport's pose, in doubles, at the next sync point.
- Outside a tick, an edit of the tree still moves the hull. A reparent changes the tree's `Version`, so the next reconcile moves the hull. `FrameTree.SetLocalPose` does not change `Version`, so the reconcile does not see it. So at the start of each step, before the intents, physics puts each static hull whose place in Jolt differs from its frame's tree pose at that pose, and wakes the bodies around its old and new places (`FollowStaticHulls`, the old static branch of `DriveKinematicHulls`). It runs before the intents, so that it never moves a hull back from a teleport, which the tree learns only at the next sync point. `WorldWakeTests` fails without it. Only the push of the hulls on rails in every step goes.

The cost of a frame without a body is one component call per tick, in a parallel chunk, and one copy at the sync point. Today it is a serial solve at the sync point. The orbit scene has two such frames, earth and earth-surface. `ter-6r8c` adds a benchmark to `WorldTickBenchmarks` with 1,000 Kepler frames without bodies, and it records the time of the tick and of the sync point before and after the change.

## The sync point

| Step | Today | After |
|---|---|---|
| 2 | `AdvanceNumericFrames` integrates the falling frames and the orbit frames. `ApplyPhysicsOutput` applies the hand-offs, the body states and the states of `Physics` frames. | `AdvanceNumericFrames` integrates the falling frames and puts each following frame on its step plan. `ApplyPhysicsOutput` applies the hand-offs and the body states. Then `FollowOwners`, the one loop of "One motion record per frame": each frame with a step record takes it, with its follow kind. It derives the record of a frame without a hull from its owner's front intent. Each frame's proper acceleration derives from its record, or from its rest, by the rule of "The proper acceleration of a frame", with its parent's value as stored before step 2. A dynamic hull's comes from physics. The tree writes the new values after the loop. Then the float view of each frame entity is filled from its exact table. A body state of a hull never writes the float view. Then `FollowFramelessEntities` puts the record of each entity without body or frame on its transform row. |
| 4 | The collision intents, then the swap of the intents | The intents of the tick before, which the step that ended applied, change the collision rows, and their `SetMotion` changes the stored hull motion in the tree. The `SetMotion` intents of the tick that ends plan the next step: the frame and the intent drop what the new motion refuses, and `Move` drops by the guard's rule. Then the intents swap: the two intent buffers exchange, and without a backend each front intent keeps `Move` and the change of motion. |
| 5 | The destroys. A frame that is not a `Physics` frame moves to the semantic parent. `StopKeplerFrameWithoutGM` stops a Kepler frame whose new parent has no G·M. | The destroys. A frame without a dynamic hull moves to the semantic parent. A motion intent that names a dying frame converts first, with the rest of its intent. Nothing stops a Kepler frame. A `KeplerMotion` whose new parent has no G·M writes no sample from the next tick on, and logs once. |
| 6 | The spawns. `MakeLive` places each Kepler and Spin frame by its driver. | The spawns. A new frame stands at the pose of its spawn description. A spawn's first motion intent goes into the new entity's front intent, which step N+1 applies (see "The first motion intent of a spawn"). A new frame without one holds still for its first step. Its proper acceleration is zero during that step (see "The proper acceleration of a new frame"). |
| 6b | `AdvanceDrivenFrames` places the Kepler and Spin frames and the moved static frames at the end of the tick. | Gone. Step 2 does this work. |
| 6c | The numeric update. `NumericBody.Fixed` and `Orbits` come from the driver. A new orbit frame starts from the orbit. | The numeric update. `Fixed` and `Follows` come from the hull's motion. A new following frame starts at its anchor's place, with its anchor's instant velocity, from the frame tree row. Its first step plan lands it. |
| 7 | The float view of each `Physics` frame entity is filled from its exact table. | The float view of each frame entity is filled from its exact table. |
| 7b | The intents convert. | The intents convert, `Move` among them. |
| Next `PrepareTick` | `BeginNumericStep` gives an orbit frame the orbit's acceleration at its origin, from the orbiter's G·M. | `BeginNumericStep` derives each following frame's step plan from its anchor's front intent. It gives the frame the acceleration that lands it on the plan. |

The order holds for these reasons:

- Step 2 reads the motion intents that step N applied before step 4 swaps them away.
- The numeric update in step 6c still sees every bubble at the end of the tick. Each sample's pose is at `t_{N+1}`, and the destroys and the spawns still come before step 6c. So the reason for step 6b stays met: a station that orbits at 7.5 km/s is not 126 m behind the bodies beside it (see "Bubbles and ghosts" in [Numeric frames](numeric-frames.md)).
- A following frame takes its place before `ApplyPhysicsOutput` writes the rows of its members, so their rows are relative to its exact place.
- A frame without a body takes its owner's write before the destroys. So a write in the tick in which its parent dies converts with the frame. Today step 5 fills the float view of the frame entity from its exact table, and so loses the write.
- Step 11 builds the render snapshot from the tree, which holds the end of the tick. The motion intent for the next step never enters the tree before that step runs.

Determinism: the components run in parallel chunks, but each one writes only its own entity's motion intent. A sample is a pure function of the component's description, `self.TimeAt`, the step, the frame's place and the parent's gravity in the front snapshot. No sample depends on another sample or on the order of the chunks. The sync point applies the samples in the tree's dense order, and each frame reads only its owner's motion intent. `DeterminismTests` with one and eight workers checks this.

## Physics

Kinematic targets and the snap (since `ter-xdt8`, see "Rails motion for every entity"):

- One branch, the `Move` branch of `ApplyBodyIntent`, aims every kinematic body at the aim pose of its motion intent. The target is that pose in the body's physics home, minus `HomeOffsetAfterStep`. The branch runs after the body's teleport. A frame at the origin of its following frame gets that origin as its target. Until `ter-xdt8`, `AimHullsOnRails` aimed the hulls after `ApplyIntents`, and `ApplyIntents` aimed the plain bodies at their gameplay targets.
- The same branch steers a dynamic body to a steer target, as `MoveKinematic` did for a dynamic body before `ter-xdt8`: it gives the body the velocities that carry its centre of mass to the target over the step.
- Right after the Jolt update, physics puts each kinematic body that a motion intent moved exactly on its end pose (`SnapKinematicBodies`). It uses `SetRPositionAndRotation`, without a change of activation. This comes before the contacts, the hand-offs and the ghosts read the end of the step.

Predictions of a frame at the end of a step replace `PredictMotion` and `OrbitMatchesTree`. Since 2026-10-18 they all read the frame's step record (see "One motion record per frame"). The table below says where each record comes from:

| The frame's owner | Pose | Velocity for ghosts | Velocity for hand-offs | Angular velocity |
|---|---|---|---|---|
| A dynamic hull | Jolt's state after the update (`TryHullMotion`), as today | Its chord, as today | Its chord plus half a step of its acceleration (`InstantMotionOf`), as today | Jolt's |
| A kinematic hull, or no hull, with an end pose or a full sample | The record's pose in the tree parent. At the origin of a following frame, that origin, at rest relative to it, as today. | The record's chord | The record's instant velocity | The record's |
| A static hull, a kinematic hull without a motion intent, or no hull and no motion intent | The tree pose, or the pose of a teleport in the step | Zero | Zero | Zero |

Hulls and layers:

- A hull's motion is its body's motion: `BodyDesc.Motion` at spawn, and the change of motion after that. `BodyDesc.Problem` checks a hull as it checks any body, so a mesh hull must be static and a dynamic hull needs a mass. `BodyDesc.FrameHullProblem` goes.
- A hull stays on `Layers.FrameHull` and meets static bodies and other hulls. A kinematic hull still carries the dynamic hulls that rest on it.
- Only a dynamic hull hands off, as today (`MovesBetweenFrames`). `PhysicsOutputCheck` treats a hand-off or a frame state of a frame without a dynamic hull as a bug.
- `BodyRecord.Driver`, `BodyRecord.DriverVersion` and `FollowDriver` go. `ApplyHullMotion` stays, as the code that changes a hull's motion.

## Numeric frames

The orbit frame becomes a following frame. It follows any kinematic hull that moves fast or far, by the speeds and distances of free bodies: from 1,000 m/s or 2^23 m, kept down to 400 m/s or 2^22 m.

- At each sync point, in step 2, it takes its step plan's place, and the plan's chord as its velocity. The step plan is the step record of its anchor's motion intent, which `BeginNumericStep` derives before the step. Its anchor's frame lies at its origin with the plan's rotation. The anchor's frame keeps the plan's instant velocity minus the chord, so that a read in the semantic parent gives the instant velocity. This is `FrameTree.PlaceOnOrbit` today, with the plan in place of the orbit.
- In each step, it falls with the acceleration that lands it on its step plan: the plan's chord minus the frame's velocity, divided by the step. So it ends the step on the plan to rounding. For a full sample this acceleration equals the sample's acceleration to about 3e-10 m/s² in low orbit. An end pose has no acceleration of its own, so the landing rule serves both cases (see "The step plan of a following frame").
- The numeric update makes a following frame at its anchor's place, with its anchor's instant velocity, from the frame tree row. Its first step plan lands it.
- When its hull has no motion intent, because the component went or the motion changed, it falls on as a falling frame, as an orbit frame does today when its orbiter leaves (`LeaveOrbit`).
- When its hull teleports, it jumps with the hull, and the other bodies and frames in it keep their places and velocities in the semantic frame, with a `FrameShift`, as `FrameTree.MoveOnOrbit` does today for a new orbit.
- `NumericBody.Fixed` is true for an interior and for a hull whose body is not dynamic. `NumericBody.Orbits` becomes `Follows`, true for a kinematic hull. `NumericFrameRules.OrbitsInFrame`, `BodyMode` and `GroupMode` read `Follows`. A spin hull near its parent's origin does not move, so it stays in the semantic frame's own system, as today.
- `NumericMode.Orbiting` becomes `NumericMode.Following`, because the anchor need not orbit.
- The tree stores the motion of each owner's hull, `FrameTree.HullMotion(frame)`, a `BodyMotion?` that is null without a hull. It is the one store of the hull's motion: the body description keeps only the motion of the spawn. It is the motion of the last finished step: the World sets it at spawn, and at the sync point after the step that applies a change of motion. The numeric update plans the next step, so it reads the motion of that step, which counts a change written in the tick (`World.NextStepHullMotion`, see "Changes of motion"). Since `ter-4psc`, `FrameTree.Heir` sends every frame of a dying frame to the dying frame's semantic parent, so it needs no hull motion.
- The invariants of the tree change: the frame at a following frame's origin has a kinematic hull and a zero local position, a semantic frame under any other numeric frame has a dynamic hull, and a numeric frame has no owner and no gravity. The motion of a hull here is its motion in the step that the numeric frames are planned for, the next one (`FrameTree.HullMotionInStep` with the intents that the next step applies). The rule that a Kepler frame has a parent with G·M goes from the tree.
- `FrameTree.StepLength`, `PlaceOnOrbit`, `MoveOnOrbit`, `KeplerMotion`, `CreateOrbitFrame` and `InOrbitFrame` go or become their following-frame forms.

## Changes of motion

A change of motion is a swap of components and a change of the body's motion. The entity's own components change the body's motion in one tick with the intent `self.Body.SetMotion(BodyMotion)`. Only a hull can change its motion, because only a hull asks Jolt for `AllowDynamicOrKinematic` today.

The hull's motion has one store: the frame tree row of the frame entity, `FrameTree.HullMotion`. The body description keeps the motion of the spawn. The entity table has no motion row. (User)

Step k is the physics step that runs beside tick k. A `SetMotion` written in tick k is in the intents that step k + 1 applies. Physics changes the Jolt motion type at the start of step k + 1 (`ApplyHullMotion`), after the reconcile and before anything reads the body's motion. At step 4 of the sync point after step k + 1, the tree stores the change, from the intents that the step applied, exactly where and how the collision rows take their groups. So the stored motion lags physics by one step, like the collision rows: the state that a tick reads describes the last finished step. (User, 2026-10-18) This also works without a physics backend: the swap keeps the changes of motion in the front intents, and the tree stores each one at the same sync point.

The motion of a step that has not finished is derived, never stored. Each question has one read, named by the step that it answers, over the same data: the intents written in the tick, the front intents and the stored motion. No reader combines the intents and the store on its own. `HullMotionInStep` takes its step from the intents that its caller passes, so its name says only "in the step of these intents": physics asks it for the running step, and the World asks it for the next step after step 4 swaps the intents. A name such as "running step" would be wrong at those calls of the World.

| Read | The step that it answers | Readers |
|---|---|---|
| `FrameTree.HullMotion` | The last finished step | The body facts of the snapshot, the World's invariants |
| `FrameTree.HullMotionInStep(frame, stepIntents)` | The step that applies `stepIntents`, the step after the last finished step: with the front intents, the running step during a tick and at the sync point until step 4, and the next step after step 4 swaps the intents. Its value is the owner's change in those intents, or else the stored motion. | Physics at the start of each step, the check of the step's output, the kind of each frame's step record in `FollowOwners` (so the `follows` of a dump), the tree's invariants |
| `World.NextStepHullMotion(frame)`, and `self.Body.NextStepMotion` for a component | The next step, which applies this tick's writes. Its value is the owner's change written in this tick so far, or else the motion in the step that applies the front intents. | The in-tick guards, the checks of `KeplerMotion` and `SpinMotion`, the numeric update, the body that a rehome creates |

So the motion that Jolt uses in each step, and every rule about that step, takes the change at once. Physics follows the derived motion at every step, so a failed step loses no change: the tree stores it after that step all the same, and the next step that runs takes it from the store. The output check does not call the first hand-off of a hull that became dynamic a bug, and `KeplerMotion` finds its hull kinematic in the tick after "Into orbit". Before 2026-10-18 the store led physics: step 4 of the writing tick's sync point stored the change, so the store was the motion of the step in flight, and every reader read it directly. The user wanted the data to flow as it does for the collision rows.

The last `SetMotion` in a tick wins, and a change to the motion that the hull has changes nothing, in the tree or in Jolt. At step 4 of the writing tick's sync point, a change keeps the frame coherent with the motion of the next step (`FrameTree.PlanHullMotion`). These drops follow the motion of the next step, whatever the motion of the step that ended, because the guards judge each write by the motion at its moment, and a later change in the same tick can turn it:

- The guard's rule judges the motion intent written in the tick by the hull's motion in the next step (see "Rails motion for every entity"). A hull drops a motion intent that the rule refuses. So a hull that is not kinematic in the next step drops an end pose or a full sample. A hull that is not dynamic in the next step drops a steer target. The motion intent that the running step applies stays, because the frame follows it at the next sync point. A frame at the origin of its following frame leaves that frame falling.
- A hull that is kinematic in the next step drops the `SetVelocity` and `SetAngularVelocity` written in the tick. A kinematic body moves only by its motion intents and teleports. It keeps an end pose. Its forces and impulses stay, because physics applies them only to a dynamic body.
- The change sets no velocity of the frame. A parked frame stops at the sync point after the next step, because a static hull without a step record holds still there.

The table shows the changes:

| Change | What gameplay does in one tick | What happens |
|---|---|---|
| Park | `SetMotion(Static)` | The hull holds still where the step leaves it, and its frame stops in its semantic parent. Its proper acceleration becomes the gravity at its place, reversed. So a ship parked on the ground keeps its cargo's weight, and a ship parked in free space keeps no thrust. Until `ter-xdt8` the frame kept the proper acceleration of its last step. |
| Unpark | `SetMotion(Dynamic)` | The hull falls or coasts from rest. |
| Engines on, Kepler to dynamic | Remove `KeplerMotion`, then `SetMotion(Dynamic)` | The hull coasts on with the velocity of its last sample. Its following frame falls on. |
| Into orbit, dynamic to Kepler | Add `KeplerMotion`, then `SetMotion(Kinematic)` | The hull holds still in its physics home for one step, as a kinematic hull without a motion intent does. Then the component's first run teleports it onto its orbit. |
| Kepler to static | Remove `KeplerMotion`, then `SetMotion(Static)` | The frame stops. The numeric update moves the hull out of its following frame into the semantic frame's own system. |
| A new orbit or spin | Replace the component | The new component's first run teleports the hull. A following frame jumps with it, and its other contents keep their places in the semantic frame. |

A swap has no gap: the old component's last sample covers the step before the new component's first sample. A hull keeps its body id, its system and its contacts through every change. `SetMotion` refuses, with an always-on guard, a body that is no hull, a dynamic motion for a hull without mass, a motion other than static for a mesh hull, and a character.

`World.SetFrameDriver`, `SetFrameOrbit` and `SetFrameSpin` go. Outside a tick, a game adds and removes components with `World.AddComponent` and `World.RemoveComponent`.

## Content and scenes

- `FrameSpec` becomes `FrameSpec(FrameBounds Bounds, GravityField Gravity = default)`. `FrameTree.Create` loses its driver, orbit and spin. About 517 lines in about 120 test files and 6 benchmark files change by rote.
- The Demo scenes: `earth` gets a `KeplerMotion`, `earth-surface` a `SpinMotion`, and the `station` a `KeplerMotion` with a hull whose `BodyDesc.Motion` is `Kinematic`. The hulls of the `ship` and of the cargo pods say `Dynamic`. Today a hull's description did not count, so each hull description must now state its motion. A scene gives a new frame on rails its spawn pose and velocities from the library, so that it starts near its rails. The frame holds still there for its first step, and the first sample of its component puts it on its rails. Since `ter-xdt8`, the scene also gives the frame its first motion intent from the same orbit or spin, so that it is on its rails from its first step (see "The first motion intent of a spawn").
- The scripts in `scripts/` name no driver, so they do not change.
- `docs/plans/content-formats.md`: `asFrame` loses `driver`, `orbit` and `spin`. A Kepler frame lists `KeplerMotion` among its components, with the two orbit kinds `elements` and `circular` as values of its `orbit` field. The diagnostics CT7006 and CT7007 go with the driver, because the components check themselves (see "The rails components"). The text on them in this file (lines 419, 427, 431, 471, 472, 692 and 693) goes too. `docs/plans/editor.md` (lines 406, 612, 759 and 935) and `docs/plans/content-packages.md` (lines 331 and 806) follow too.

## State dumps and saved state

`StateDump.FrameRecord(string Id, string? Parent, MotionDriver Driver)` becomes `FrameRecord(string Id, string? Parent, string Follows)`. `Follows` is the frame's follow kind, which the tree stores at step 2 (see "The follow kind"):

- `"rails"`: the frame took its owner's full sample in the step.
- `"kinematic"`: the frame took its owner's end pose. This value comes with `ter-xdt8`.
- `"physics"`: the frame's hull was dynamic in the step, with or without a steer target.
- `"static"` for every other frame: a teleport, a transform write, or no record. A transform write that wins over a motion intent gives `"static"`.

The motion of the hull in the step comes from the intents that the step applied, or else from the stored motion (`FrameTree.HullMotionInStep`), because the tree stores a change of motion only at step 4, after the frames took their records. So a dump still shows which frames move on rails. The entities in a dump do not change: the float view of a frame entity is still filled from its exact table, so its pose and velocities are those of its step record.

No save of a running world exists (`ter-72wp`). A rails component holds only its description, and the frame tree row holds only the record of the last step, which the next record replaces. So a future save stores the components' properties, and the motion goes on exactly after a load.

## Render interpolation

Nothing changes. Step 11 reads the tree's local poses, which hold the end of the tick, because step 2 puts each frame on the step record of the step that just ended. `RenderSnapshotInterpolationTests` and `RenderSnapshotRootTests` stay green without a change.

## Tests

What changes:

- `DriverChangeTests` keeps its eight scenarios as changes of motion and swaps of components (see "Changes of motion"): park, park and unpark, static to dynamic, Kepler to dynamic and back, the body id across a change, and growth after a change. Its two tests of a hull that does not match its driver become tests of `SetMotion`'s refusals.
- `FrameDriverTests` becomes a test of `SetMotion`. `FrameTreeDriverTests`, `FrameHullMotionTests` and `KeplerParentMuTests` go with the driver. The G·M rule moves to the library's tests.
- `FrameTreeOrbitFrameTests` gives samples to the tree and tests following frames.
- About 30 refusal tests change from an `ArgumentException` of the World to the log of the component: `KeplerParentGravityTests`, `WorldSpawnRefusalTests` and the refusals in `SpinFrameTests`. A spawn of a Kepler frame without G·M no longer throws, so these tests check that the frame holds still and logs once. The tests of an orbit or a spin that is not valid check the setter of the component. `KeplerFrameTests.AKeplerFrameStaysOnItsOrbitWhenItsOwnerTriesToMoveIt` and `SpinFrameTests` line 113 become tests that a write moves the frame for one tick.
- `StateDumpTests`, `OrbitSliceTests` and `DumpFileTests` follow the new `FrameRecord`. `OrbitSliceTests` checks `Follows` of the orbit frames in place of their driver.
- A test that expects a frame on its orbit as soon as it exists waits for the second sync point after the spawn (see "Other rules" in "The rails components"). Since `ter-xdt8`, it can instead spawn the frame with its first motion intent.
- The helpers `Scenes.MoonOnKeplerOrbit`, `Scenes.FastKeplerHull`, `SpawnKeplerFrame`, `SpawnSpinFrame`, the moon in `HostTesting`, `DeterminismTests` line 169 and `InvariantWorkload` lines 65 and 66 spawn components. The churn of `InvariantWorkload` (line 378) writes the transform of any frame owner without a body.
- The tests of poses over time stay as they are: `KeplerFrameTests`, `SpinFrameTests` lines 37 to 128, `WorldFacingFrameTests`, the scenarios of `NumericFrameOrbitTests`, `StationSliceTests` with `HullOffEarthOrbit` below 1e-6 m, and both determinism tests.

What the new tests prove:

1. The library's sample matches the orbit: its chord equals the change of place over the step to rounding, and its angular acceleration equals the change of the angular velocity.
2. A frame on rails stands at `orbit.StateAt(World.Time, mu)` to the last bit at every sync point from the second one after its spawn, for ten minutes, with a body and without, in its semantic parent and in a following frame.
3. After each step, the hull's place in Jolt equals its target exactly.
4. A body that sticks out of a frame on rails has a ghost with the sample's chord, and a body that leaves the bubble hands off with the sample's instant velocity.
5. A new frame on rails without a first motion intent holds still at its spawn pose for its first step, and stands on its rails at the second sync point after its spawn. A swap of components has no gap.
6. A static hull follows a teleport and wakes the bodies around it. A frame without a body follows its owner's write, also when its parent dies at the same sync point.
7. A frame on rails whose samples stop holds still, and its following frame falls on.
8. A test component that drives a kinematic hull at 2 km/s along a straight line gives the hull a following frame, and the hull rests in it. A crate that drifts out of the hull's frame joins that following frame (`ter-zg77`).
9. The benchmark of 1,000 Kepler frames without bodies reports the tick and the sync point before and after.

Every implementation brief asks for hand mutations, as `CLAUDE.md` says.

## Order of the work

The epic listed three work tickets. This plan keeps their order and adds `ter-ru5u` and `ter-xdt8`. So each ticket ends green and stays small enough for a few agents in a row.

1. `ter-k8cg`, the library. No change of behavior. The functions in "The rails library" move, with their tests (`KeplerOrbitTests`, `KeplerAttitudeTests`, `FrameSpinTests`, and the chord and acceleration cases of `NumericFrameRulesTests`). The new functions and `MotionSample` come with tests against today's `PlaceOnOrbit`, `FrameTree.KeplerMotion` and `ApplySpin`. The tree and the Demo call the library.
2. `ter-6r8c`, the samples and the components. The sample table, `Self.MoveOnRails`, `Self.TimeAt` and `Self.OwnedFrame`. `KeplerMotion` and `SpinMotion`, with their checks, their first run and the proper acceleration of the sample. `FollowOwners` in step 2, and the end of step 6b. Physics reads the samples for targets and predictions, and snaps bodies on rails. `DriveKinematicHulls`, `PredictMotion` and `OrbitMatchesTree` go. The orbit frame follows its anchor's samples, and `Orbits` reads "the hull is kinematic". The tree's orbit and spin, `PlaceOnOrbit`, `MoveOnOrbit`, `KeplerMotion`, `SetFrameOrbit` and `SetFrameSpin` go. Static hulls take teleports, and owners without a body write their transforms. The scenes, the benchmark, and the sections of `ARCHITECTURE.md` that this changes. In this ticket the enum stays only to choose a hull's motion, so `Kepler` and `Spin` both mean a kinematic hull. Until the next ticket, a change of motion is `SetFrameDriver` plus a swap of components with `World.AddComponent` and `World.RemoveComponent`. `ter-xdt8` later replaces the sample table with motion intents, and derives the proper acceleration from the step record in place of the sample's field.
3. `ter-ru5u`, "A body's motion changes after spawn, and the MotionDriver enum goes". It depends on `ter-6r8c`. The change of motion with the intent `SetMotion`, the hull's motion from its body, `FrameTree.HullMotion` and the tree's invariants, `NumericBody.Fixed` from the body's motion. `MotionDriver`, `FrameHullMotion`, `FrameHullProblem`, `SetFrameDriver`, `FollowDriver` and the driver in `FrameSpec` and `FrameTree.Create` go, with the rote test edits. The rewrite of `DriverChangeTests` and `FrameDriverTests`, the state dumps with `FrameRecord(Id, Parent, Follows)`, and a row in the table "Where the user changed my proposals" in `CLAUDE.md`.
4. `ter-xdt8`, "Rails motion for every entity". It depends on `ter-ru5u`, and it comes after `ter-sn28`, because both change the frame tree and the sync point. The motion intent `PhysicsIntent.Move`, with its three cases, replaces the sample table and `PhysicsIntent.Target`. One guard decides which entity takes which case. Every kinematic body moves only by its motion intents and teleports, and an entity without body or frame follows its motion intents. A frame's proper acceleration derives from its step record by one rule. A spawn description can carry its new entity's first motion intent. The section "Rails motion for every entity" gives the design, its tests and the stages of its work.
5. `ter-zg77`, now "A following frame follows every fast kinematic hull". It depends on `ter-ru5u` and `ter-xdt8`. `ter-6r8c` already gives every kinematic hull a following frame. This ticket proves it for motion that is no orbit (test 8), renames `NumericMode.Orbiting` to `Following` and `Orbits` to `Follows`, and rewrites the sections "Orbit frames", "Anchors" (rule 3), "Frame owners in a numeric frame", "Bubbles and ghosts" and "Limits" of [Numeric frames](numeric-frames.md), and the numeric rows of `ARCHITECTURE.md`.

`ter-e30t` and `ter-f620` stay open outside this epic, as questions of what `KeplerMotion` and `SpinMotion` do when the parent dies.

## Rails motion for every entity

Ticket: `ter-xdt8`. It comes after `ter-ru5u` and `ter-sn28`, which both changed the frame tree and the sync point. `ter-zg77` depends on it. This section replaces the frame-keyed sample table of "The motion sample" and the parts of "One motion record per frame" that named it. The user gave the model on 2026-10-18, and settled its forks and the steer target the same day. The table "Decisions" at the top of this doc records each answer with its reason, in these rows:

- "Which entities move on rails"
- "Where the motion intent lives"
- "What a motion intent can be"
- "Which entity takes which case"
- "How a kinematic body moves"
- "What the frame tree row keeps of a step record"
- "How a frame gets its proper acceleration", which the user added to the design later the same day
- "Which proper acceleration of the parent the rule reads", "The proper acceleration of a new frame" and "How rails motion starts", the user's answers to the points that the builder of stage 1 raised, the same day
- "The first step of a new following frame", the orchestrator's answer on 2026-10-19 to the point that the builder of stage 5 raised

### The model

Cause, storage and effect are three separate things:

- Cause. In tick k, a component of an entity says how its own entity moves in the next step, step k + 1. It writes one motion intent into the entity's intent, beside the force, the impulse and the velocity. A motion intent is a union with three cases. An end pose gives only the pose at the end of the step (`self.MoveKinematic`). A full sample gives the pose and every velocity and acceleration that goes with it (`self.MoveOnRails`). A steer target gives a pose that physics steers a dynamic body toward over the step (`self.Body.SteerTo`).
- Storage. The motion intent has the life of the rest of the intent. Tick k writes it into the written intents. Step 4 of the sync point of tick k swaps it into the front intents, which step k + 1 applies. A spawn's first motion intent, which the spawner wrote into the spawn description in tick k, enters the new entity's front intent at step 6 of that sync point (see "The first motion intent of a spawn"). When its entity changes frame, steps 5 and 7b of that sync point convert it. The swap at step 4 of the next sync point clears it. No other table holds it.
- Effect. The step that applies an end pose or a full sample moves the entity onto it. That move is the entity's step record: its motion over the step that just ended, a `MotionSample`. Physics moves a kinematic body: it aims the body at the end pose, and puts it exactly there after the Jolt update. The sync point moves an entity without a body: at step 2 it puts the record into the entity's exact table. A frame takes its owner's record. After step 2, the frame tree row holds the record of the step that ended, and the frame's proper acceleration, which follows from the record and the gravity at the frame's place. A steer target gives no record. Physics gives the dynamic body the velocities that carry it to the target over the step. Contacts and gravity still act on it, and physics measures its motion as for every dynamic body.

An end pose and a full sample are the result itself, for a kinematic body or an entity without a body. A force, an impulse and a steer target are inputs that physics integrates for a dynamic body. The guard keeps the two apart. A dynamic body refuses an end pose and a full sample. A kinematic body and an entity without a body refuse a steer target. A static body, a character and an attached entity refuse every case.

One function turns a motion intent into a step record, for every reader: `PhysicsIntent.StepRecord`. A full sample is its own record. An end pose gives the motion at a constant velocity from the pose where the step starts to the end pose. If the same intent teleports, the step starts at the teleport's pose. Else it starts where the entity stands. A steer target gives no record, and neither does an intent without a motion intent.

### Why the sample table existed, and what replaces each reason

| Reason for a table of its own | What replaces it |
|---|---|
| `EntityTable.WritableIntent` requires a body, and a frame without a body writes samples | The motion intent has its own writer, `EntityTable.WriteMotionIntent`, which needs no body, as `WriteMotionChange` writes only a change of motion |
| Intents have two buffers, and step 6c and the state dumps read the record of the step that ended from the third buffer | After step 2 the frame tree row holds that record's pose, instant velocity and angular velocity, and the proper acceleration derived from it. A follow kind per frame says what made the record. Step 6c and the dumps read those. |
| Without a physics backend the swap drops the intents, and a frame without a body follows its samples | The swap keeps the motion intents without a backend, as it keeps the changes of motion. The sync point, not physics, applies them to an entity without a body. |
| Samples needed no conversion at step 7b, and intents do | A plain kinematic body hands off between frames, so its sample needs the conversion of step 7b too. The intent's conversion does it. |
| An intent had no chord, instant velocity, acceleration or angular acceleration | The full-sample case of the union carries them. It carries no proper acceleration, because the frame tree derives that. |
| "Only frames pay" | Every slot pays for the field. The swap exchanges the two intent buffers instead of copying each writer's intent, so it moves less memory per writer than today. The benchmark of the swap at 100,000 entities tells whether a sparse store is needed. |

### Terms

The table "Terms" at the top of this doc defines "Motion intent", its three cases, "Step record" and "Follow kind". This section also uses these terms:

| Term | Meaning |
|---|---|
| Plain body | The body of an entity that owns no frame |
| Frame owner without a hull | The owner of a frame that has no body, or the owner of a root frame, whose body is only the frame's static interior |
| Step plan | The step record of a following frame's anchor. The frame tree derives it before the step and keeps it for that step (see "The step plan of a following frame"). |

### The flow per kind of entity

Tick k writes the motion intent. Step k + 1 applies it. The sync point after step k + 1 lands its record. Every kind that takes an end pose or a full sample holds still in a step without a motion intent. A dynamic body moves under physics. In the step after its last steer target, physics stops it, unless that step sets its velocity, as it does today.

| Kind of entity | The cases that it takes | Who makes the step record, and when | Where the record lands | What follows the entity | Its frame's proper acceleration |
|---|---|---|---|---|---|
| Kinematic hull | An end pose and a full sample. Today it refuses `MoveKinematic`. | Physics, at the start of step k + 1, after the teleport: from the hull's tree pose. Physics aims the hull at the end pose, and snaps it there after the Jolt update. At the origin of its following frame, the hull aims at that origin. | Physics releases it (`FrameStepRecord`). Step 2: `FollowOwners` puts it in the frame tree row, and `MirrorFrameOwner` fills the transform row from it. | The frame and its contents | Derived at step 2 from its record in its semantic parent, which in a following frame is its step plan. Exactly zero on a Kepler orbit in a parent that falls freely. Without a motion intent, derived from its rest. |
| Plain kinematic body | An end pose and a full sample. Today it takes only `MoveKinematic`. | Physics, the same branch as for a hull: from the body's Jolt pose at the start of the step. Physics snaps it too. | The body state of the step carries the record's pose and instant velocity. Step 2 writes them into the transform row. A hand-off carries the record's instant velocity. | Its attached children, at step 7 | None: it owns no frame |
| Frame owner without a hull | An end pose and a full sample | The sync point, at step 2: `FollowOwners` derives it from the frame's tree pose, which still stands where the step started | The frame tree row, then the transform row. A transform write of the tick comes after it and wins for one tick. | The frame and its contents | Derived at step 2 from its record, from a transform write at rest, or from its rest |
| Entity without body or frame | An end pose and a full sample. Today it can take neither. | The sync point, at step 2: a new loop, `FollowFramelessEntities`, derives it from the transform row's pose | The transform row: the pose, and the velocities narrowed to floats. A transform write of the tick wins. | Its attached children, at step 7 | None: it owns no frame |
| Dynamic body, hull or plain | A steer target only. Today `MoveKinematic` gives it the velocities that carry it to the pose, and `self.Body.SteerTo` keeps that. | No record from the intent. Physics steers the body at the start of step k + 1, after the teleport, as today. It gives the body the velocities that carry its centre of mass to the target over the step. Physics measures a dynamic hull's record, as today. | As today | As today | A hull: physics measures it by the same rule and releases it beside the record. A plain body owns no frame. |
| Static body, hull or plain | None. Today physics ignores `MoveKinematic` on it. | A teleport of a static hull, as today. Physics still drops the teleport of a plain static body, a gap that this ticket leaves open. | As today | As today | A hull: derived at step 2 from its rest or its teleport, so the gravity at its place, reversed. A plain body owns no frame. |
| Attached entity | None, as it refuses `SetTransform` | None. Step 7 derives its row from its chain's root. | | | None: it cannot be a frame |
| Character | None. Today physics ignores `MoveKinematic` on it. | None. A walk, a jump, forces and impulses move it. | | | As for any frame without a record: the gravity at its place, reversed |

The guard is one read, `World.MotionIntentProblem(Entity entity, in MotionIntent intent)`. It returns the reason that the entity refuses the case of `intent` in the next step, or null. It checks these rules in this order:

1. An attached entity refuses every case.
2. A character refuses every case.
3. An end pose or a full sample needs a body that is kinematic in the next step (`World.NextStepMotion`), or no body. The owner of a root frame passes too, because its body is only the frame's interior, so physics does not move it. Its frame takes its records as a frame without a hull does.
4. A steer target needs a body that is dynamic in the next step. An entity without a body has nothing to steer. A kinematic body moves only by end poses and full samples. The owner of a root frame has only a static interior.

The rules live in one internal pure function, `MotionIntent.ProblemFor(BodyMotion? motion, bool attached, bool ownsRootFrame)`, an instance method of the union. `motion` is the motion of the entity's body in the step that applies the intent, null without a body. `attached` says whether the entity is attached. `ownsRootFrame` says whether it owns a root frame. `World.MotionIntentProblem` reads these three facts of the live entity in the next step (`World.NextStepMotion` for the motion), and the check of a spawn reads them from the spawn description (see "The first motion intent of a spawn"). So a writer and a spawn judge each case by the same rules.

`self.MoveOnRails`, `self.MoveKinematic` and `self.Body.SteerTo` all throw an `InvalidOperationException` with that reason. The rails components read the same reason through `self.MotionIntentProblem`, log it once, and write nothing. Step 4 drops a written motion intent by the same rule, with the hull's motion in the next step (see "Changes of motion"). So the components, the writers, the drops and the check of a spawn can never disagree. Today `RailsHull.IsNotKinematic` copies part of the guard, with its own test for a root frame. A guard built on `NextStepMotion` alone refuses the owner of a root frame, because `NextStepMotion` gives `Static` for it.

`World.WritableMotionIntent` stays the guard of `SetVelocity` and `SetAngularVelocity`. It refuses every body that is kinematic in the next step, hull or plain. A spawn refuses a linear or angular velocity on a kinematic body that owns no frame. It throws an `ArgumentException`, as the other refusals of a spawn do. A kinematic hull keeps its spawn velocity as its frame's velocity. From it, the numeric update gives a fast hull its following frame at spawn. A plain kinematic body gets no following frame until `ter-zg77`.

### Why a frame owner's row is filled from its frame

A frame entity has two tables. The frame tree row is exact: a double pose and velocity relative to the frame's tree parent, which can be a numeric frame. The transform row is the float view of the same entity, relative to its semantic frame: its `Pose` is in `Frame`, `PhysicsHome` is the tree parent, `HomePosition` is the position there, and `LinearVelocity` is a float relative to the home. `FrameOwnerRow` builds the row from the tree row. Its pose is the sum of the places of the numeric frames between the tree parent and the semantic frame.

So the row must be built again after each change of the tree row or of that sum. Step 2 changes the tree row of each frame that takes a record. Step 5 changes it for a frame that moves up. Step 6c changes it for a frame that the numeric update moves between numeric frames. A drain, a recenter or a jump of a nesting frame changes the sum and writes no row, so step 7 builds every row again. If the tree did not change, step 7 repeats step 2. `ter-sn28` found that pass costs about 44 µs for 1,000 frames and lists a skip for it.

This design adds no such pass. A motion intent lands in the entity's exact table: the frame tree row for a frame owner, and the transform row for an entity without a frame, which has no second table.

### The motion intent

```csharp
public readonly record struct EndPose(DPose Pose);
public readonly record struct SteerTarget(DPose Pose);

[Union]
public readonly struct MotionIntent
{
    private const byte KindNone = 0, KindEndPose = 1, KindSample = 2, KindSteer = 3;
    private readonly MotionSample sample;   // an end pose and a steer target fill only sample.Pose
    private readonly byte kind;

    public MotionIntent(EndPose end) { ... }
    public MotionIntent(MotionSample sample) { ... }
    public MotionIntent(SteerTarget steer) { ... }
    public bool HasValue => kind != KindNone;
    public bool TryGetValue(out EndPose end) { ... }
    public bool TryGetValue(out MotionSample sample) { ... }
    public bool TryGetValue(out SteerTarget steer) { ... }
    public DPose AimPose => sample.Pose;
    public bool TryRecordFrom(in DPose stepStart, double step, out MotionSample record) { ... }
    internal MotionIntent Converted(in FrameMotion motion, Double3 relativeAcceleration) { ... }
}
```

where

- `MotionIntent` = a custom C# 15 union, as `Target` is. The default holds no case: the entity has no motion intent in the step.
- `sample` = the private field that the three cases share. An end pose and a steer target read only its pose.
- `AimPose` = the pose that physics aims the body at, which every case gives. For an end pose and a full sample, it is the pose at the end of the step. Physics or the sync point puts the entity exactly there. For a steer target, it is the pose that physics steers a dynamic body toward, which contacts and gravity can keep the body from.
- `TryRecordFrom` = the step record of the intent, for a step that starts at `stepStart`, in the intent's frame, and lasts `step` seconds. A full sample gives itself. An end pose gives the constant-velocity motion described under "MoveKinematic as an end pose". A steer target and an intent with no case give false.
- `Converted` = the intent in another frame. `motion` is the motion of the intent's frame relative to the other frame now, whose pose is the relative pose of the two frames, and `relativeAcceleration` is the acceleration of one frame relative to the other (`FrameTree.AccelerationRelative`). An end pose and a steer target convert as a pose, `motion.PoseToParent(pose)`, which is `DPose.Compose(relative, pose)`, as `PhysicsIntent.Target` converts today, and keep their case. A full sample converts by the rule of a hand-off, as `MotionSampleTable.Convert` does today. That body moves to `MotionSample.Converted`. Today `ConvertInto` adds the relative velocity of the two frames to the zero velocities of an end pose. The result is no end pose any more.

The new field `PhysicsIntent.Move` holds it. The flag `IntentFlags.KinematicTarget` and the field `PhysicsIntent.Target` go, because `HasValue` tells whether the intent holds a motion intent. `PhysicsIntent.IsEmpty` also asks `!Move.HasValue`. The name `Move` matches the writers, and `PhysicsIntent.Motion` already names the change of motion.

`PhysicsIntent.StepRecord(in DPose standing, double step, out MotionSample record)` is the one read that turns the intent into a record. It is false without a motion intent and for a steer target. If the intent teleports, the step starts at `TeleportPose`. Else it starts at `standing`: the pose of the entity at the start of the step, in the intent's frame. `FrameTree.StepRecordInStep(FrameHandle frame, ReadOnlySpan<PhysicsIntent> stepIntents, double step, out MotionSample record)` wraps it for a frame: `standing` is the frame's pose in its semantic parent. It is valid only while the tree stands where the step starts. `FollowOwners` calls it at step 2 only for a frame without a hull. Nothing moves the tree pose of such a frame between the start of the step and step 2.

Writers:

- `self.MoveOnRails(in MotionSample sample)` and `self.MoveKinematic(in DPose endPose)` are members of `Self`, beside `SetTransform`. An end pose or a full sample is not a request to a body, because an entity without a body takes one too. `self.Body.MoveKinematic` goes.
- `self.Body.SteerTo(in DPose target)` is a member of `BodyHandle`, beside `SetVelocity`, because a steer target is a request to a dynamic body.
- All three write `PhysicsIntent.Move` through `World.WriteMotionIntent`, after the guard.
- All three check the same way, as `CheckedSample` does today: every number is finite, the rotation is not zero, and the World makes the rotation unit length.
- The last write in a tick wins, whatever its case.
- The intent's frame is `self.Frame` at the first write of the tick, as for every intent.
- A spawn description can carry the new entity's first motion intent, `SpawnDesc.FirstMove`, which acts in the step after the spawn (see "The first motion intent of a spawn").

### MoveKinematic as an end pose

An end pose is the case `EndPose` of the union, not a full sample with zero velocities. A sample with zero velocities tells the frame that it stands still. `FrameTree.Follow` copies `Velocity` into the frame. So the frame loses its velocity, and a ghost gets a zero chord.

Physics works out the motion of an end pose at the start of the step, as Jolt's `MoveKinematic` does today: the body moves at a constant velocity from where it stands to the end pose. `MotionIntent.TryRecordFrom` computes the same motion in doubles:

```csharp
Double3 chord = (end.Position - stepStart.Position) / step;
Double3 spin = stepStart.Rotation.AngularVelocityTo(end.Rotation, step);
return new MotionSample(end, chord, chord, Double3.Zero, spin, Double3.Zero);
```

where

- `end` = the end pose.
- `stepStart` = the pose of the entity at the start of the step, or the teleport's pose.
- `step` = the length of the step in seconds.
- `chord` = the chord velocity, and also the instant velocity at the end, because the velocity does not change during the step.
- `AngularVelocityTo` = today's `Rotations.AngularVelocity` in the physics project, which moves to `DQuaternion` in Core, so that Core and physics use one copy. The steer branch of physics uses it too.
- The acceleration and the angular acceleration are zero. So the proper acceleration of a frame that an end pose moves is the gravity at the place where the step starts, reversed (see "The proper acceleration of a frame").

The pose at the start of the step comes from the entity's exact table:

- For a frame owner, the frame tree row.
- For a plain body, Jolt's pose, which is the authority for a plain body.
- For an entity without body or frame, the transform row.

Physics and the sync point both derive the record of a frame without a hull from the same tree pose. So physics' prediction for ghosts and the frame's place at step 2 agree to the last bit.

A steer target keeps today's physics. At the start of the step, physics gives the dynamic body two velocities over the step. The linear velocity carries its centre of mass from where it stands to where the target puts it. The angular velocity, from `AngularVelocityTo`, turns it from its rotation to the target's. Contacts and gravity still act on it, so it can end the step away from the target.

What becomes of the exactness guarantees of each case:

| Kind of entity, and case | Pose at the sync point | Velocities | After a move to another frame |
|---|---|---|---|
| Kinematic hull, full sample | Exact. Physics snaps the hull, and the frame takes the sample's pose. | The sample's doubles in the tree. The angular velocity narrows to the tree's float field, as today. | A kinematic hull never hands off. A rehome at step 5 converts the intent. It misses for one step by the relative motion of the two frames, as a sample does today. |
| Kinematic hull, end pose | Exact | The chord, in doubles from the tree pose and the end pose. The accelerations are zero. | The same |
| Plain kinematic body, full sample | Exact in the transform row's double pose, rotation included | The row narrows them to floats relative to the home, as for every body | A hand-off at step 2 converts the next intent at step 7b. It misses for one step by the relative motion of the two frames, as a kinematic target does today. |
| Plain kinematic body, end pose | Exact | The chord. Jolt has the same velocity, to float rounding. | The same |
| Frame owner without a hull, either case | Exact in the tree | Doubles in the tree | A rehome at step 5: a one-step miss |
| Entity without body or frame, either case | Exact in the row's double pose | Floats in the row | A rehome at step 5: a one-step miss |
| Dynamic body, steer target | Not exact: contacts and gravity act on it, as today | Jolt's, as for every dynamic body | A hand-off at step 2 converts the next steer target at step 7b: a one-step miss, as a kinematic target has today |

### The first motion intent of a spawn

The user decided on 2026-10-18 that a spawn description can carry the new entity's first motion intent (the decision "How rails motion starts"). It is the motion intent that the entity would have written in the spawn's tick. Without it, a new frame on rails holds still for its first step, because its components run first in the tick after it goes live. By the rule of "The proper acceleration of a frame", the frame then feels the gravity at its place, reversed, and its contents feel that in its second step.

```csharp
public sealed class SpawnDesc
{
    ...
    public Double3 LinearVelocity { get; init; }
    public Vector3 AngularVelocity { get; init; }
    public MotionIntent FirstMove { get; init; }
    ...
}
```

where

- `LinearVelocity` and `AngularVelocity` = the entity's velocities at its spawn, relative to `Frame`, as today. They stay the entity's velocities until its first sync point, and the numeric update gives a fast hull its following frame from them, so a spawner with a first motion intent still gives them.
- `FirstMove` = the first motion intent: the motion intent of the new entity for the step after its spawn, relative to `Frame`, in its axes. The default holds no case, so the entity has no motion intent in that step. The name follows the field that it fills, `PhysicsIntent.Move`, because `BodyDesc.Motion` and `PhysicsIntent.Motion` name the motion of a body.
- `Frame` = the frame that the spawn names. For a frame owner, it is the frame's semantic parent, the frame in which the owner writes its motion intents. For the owner of a root frame, it is null, and the intent is in the root's own space, as a root's sample is.
- `...` = the other properties, as today.

Analogue: a body's spawn velocity, which the spawner gives for the entity's first moment. And a motion intent that the entity writes in tick k, which step k + 1 applies. The first motion intent is the intent that the new entity would have written in the spawn's tick, had it run then.

Who writes it, and when. The spawner writes it into the description, as it writes the spawn pose and the velocities. It computes the motion of the new entity's first step:

| Spawn | The new entity's first step | The end of that step |
|---|---|---|
| A component calls `self.Spawn` in tick k | Step k + 1, because the entity goes live at step 6 of the sync point of tick k | `self.TimeAt(self.Tick + 2)`, the same time as the end of a sample that the spawner writes for itself in tick k |
| A game calls `World.Spawn` outside a tick | The step of the next tick, tick `World.TickNumber`, because the entity goes live at the call | `World.TimeAt(World.TickNumber + 1)`. `World.TimeAt` becomes public, the same read as `Self.TimeAt`, so that the time matches the World's time to the last bit. |

For a Kepler station, the first motion intent is `new MotionIntent(orbit.Sample(mu, stepEnd, step))`, with the orbit, the G·M and the step that the station's `KeplerMotion` uses. A blueprint holds none, because the motion depends on the time of the spawn, which a blueprint does not know. So the content formats do not change.

Where step 6 stores it. `MakeLive` puts it into the new entity's front intent with a new `EntityTable.SetFirstMove(Entity entity, FrameHandle frame, in MotionIntent move)`. The entry becomes `new PhysicsIntent { Frame = frame, Move = move }`, with the spawn's frame, and the entity joins `EntityTable.IntentEntities`, the entities whose intent the next step applies. During a tick, this happens at step 6 of the sync point of tick k, after the swap at step 4. So the front intents are those of step k + 1, and nothing clears the entry before that step. Outside a tick, `MakeLive` runs at the call of `World.Spawn`, and the front intents are those of the next step. The front entry of the new slot is empty, as the entry of every entity outside `IntentEntities` is, so the store replaces nothing. The swap at step 4 of the next sync point clears it, as it clears every front intent.

After the store, the first motion intent lives as a motion intent that the entity wrote:

- Step 5 runs before step 6, so it never sees the intent. At step 7b the intent's frame is the entity's frame, because step 6 drops a spawn whose frame died, so the intent needs no conversion. The numeric update at step 6c can move a new body into a numeric frame, but that changes the body's physics home, not its frame.
- Physics creates a new body in the reconcile at the start of step k + 1, before it applies the intents. So the `Move` branch aims or steers the body from its spawn pose, and snaps it as it snaps every kinematic body that it aims. `PredictRecord`, `BeginNumericStep`, `FollowOwners`, `FollowFramelessEntities` and `Self.PoseAtNextStepStartIn` read it as they read every front motion intent. So the following frame that the numeric update gives a fast hull at spawn lands on the hull's first motion intent at the first sync point.
- The entity's own components run first in tick k + 1, and a sample that they write acts in step k + 2. The first run of a `KeplerMotion` teleports its hull to the orbit's pose at the start of step k + 2, where the first motion intent left the hull, so the teleport moves nothing.
- Without a physics backend, the front intent keeps it, and step 2 applies it to an entity without a body, as it applies every front motion intent.

The guard. The check of a spawn, `World.PrepareSpawn`, judges the case of `FirstMove` by the rules of `World.MotionIntentProblem`, through the same function, `MotionIntent.ProblemFor`, with three facts of the description:

- `attached`: the description sets `AttachTo`.
- `motion`: the motion of `Body`, or null without a body. The body has the description's motion in its first step, because a change of motion that the new entity writes in its first tick acts in its second step.
- `ownsRootFrame`: the description sets `AsFrame`, and its `Frame` is null.

So an attached entity and a character refuse every case. An end pose and a full sample need a kinematic body, no body, or the owner of a root frame. A steer target needs a dynamic body. The check also refuses a first motion intent with a number that is not finite or with a zero rotation, as the writers do, and the World makes the rotation unit length. Each refusal throws an `ArgumentException`, as the other refusals of a spawn do. `PrepareSpawn` runs at the call of `World.Spawn` outside a tick and at the call of `self.Spawn` in a tick, so a spawn never fails at the sync point for its first motion intent.

The Demo station scene gives its station a first motion intent. `SunAndEarth`, which the station scene and the orbit scene share, gives earth and earth-surface theirs:

```csharp
KeplerOrbit orbit = OrbitAt(world.Time);
(Double3 stationPlace, Double3 stationVelocity) = orbit.StateAt(world.Time, EarthMu);
Entity station = world.Spawn(new SpawnDesc
{
    Frame = earth,
    Pose = new DPose(stationPlace, orbit.RotationAt(world.Time, EarthMu)),
    LinearVelocity = stationVelocity,
    AngularVelocity = orbit.AngularVelocityAt(world.Time, EarthMu).ToVector3(),
    FirstMove = new MotionIntent(orbit.Sample(EarthMu, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
    Components = [new KeplerMotion { Orbit = orbit }],
    ...
});
```

where

- `orbit` = the station's orbit, as today.
- `EarthMu` = earth's G·M, the double that earth's point gravity holds. So it is the same value that `KeplerOrbit.MuFrom` gives the station's `KeplerMotion`.
- `world.TimeAt(world.TickNumber + 1)` = the end of the station's first step, the step of the next tick.
- `world.DeltaTime` = the step length, a float that widens to the same double that the `KeplerMotion` uses.
- `...` = the hull, the frame and the render row, as today.

Earth takes `earthOrbit.Sample(SunMu, ...)` and earth-surface takes `turning.Sample(Double3.Zero, ...)`, by the same pattern. Earth needs its first motion intent because of the rule of "Which proper acceleration of the parent": the station reads earth's value. An earth that holds still for its first step would give the station's contents the sun's gravity at earth's place, about 5.9e-3 m/s², for one step, and the crate of `StationSliceTests` must stay within 1e-6 m of its place until the script throws it. Earth-surface takes one so that it turns from its first step, as every frame on rails in these scenes moves from its first step. So the contents of the station feel exactly zero in every step, and `StationSliceTests` and `OrbitSliceTests` pass unchanged.

### Values: analogue, store, change, readers and writers

#### The motion intent, `PhysicsIntent.Move`

New. It takes the place of the written and applied buffers of the sample table, and of `PhysicsIntent.Target`.

- Analogue: an intent that gameplay writes and the next step applies, such as `TeleportPose` and `Velocity`. It matches that model in every point, with one named difference: the swap keeps it without a physics backend, because the sync point applies it to an entity without a body.
- Store: the entity's intent, the field `PhysicsIntent.Move`, in both intent buffers, for every slot.
- Change: tick k writes it. Step 4 of the sync point of tick k swaps it to the front, and steps 5 and 7b convert it. A spawn's first motion intent enters the front at step 6 instead (see "The first motion intent of a spawn"). Step k + 1 applies it. Step 4 of the next sync point clears it.

| Writer | When | What it does |
|---|---|---|
| `Self.MoveOnRails`, `Self.MoveKinematic` and `BodyHandle.SteerTo`, through `World.WriteMotionIntent` and `EntityTable.WriteMotionIntent` | Tick k, on the entity's worker | Writes the case, relative to `self.Frame`, after the guard. Lists the entity among the intent writers of its chunk. |
| `World.MakeLive`, through `EntityTable.SetFirstMove` | Step 6 of the sync point of tick k, after the swap, for a spawn of tick k. At the call of `World.Spawn`, for a spawn outside a tick. | Puts the spawn's first motion intent, `SpawnDesc.FirstMove`, into the new entity's front intent, relative to `SpawnDesc.Frame`. Lists the entity in `IntentEntities`. (See "The first motion intent of a spawn".) |
| `World.ApplyMotionChanges` | Step 4 of the sync point of tick k, on the written intents, before the swap | Acts on each hull that wrote a change of motion. If the guard's rule refuses the case of `Move` under the hull's motion in the next step, it drops `Move`. So a hull that is not kinematic drops an end pose or a full sample. A hull that is not dynamic drops a steer target. If the hull is kinematic in the next step, it also drops `SetVelocity` and `SetAngularVelocity`, as today. `FrameTree.PlanHullMotion` keeps only its `LeaveOrbit`. |
| `EntityTable.SwapIntents` | Step 4 | Exchanges the two intent buffers, so that the written intents become the front intents, and clears the old front entries, now in the back buffer. Without a backend, each front intent keeps only `Move` and the change of motion. |
| `World.ConvertIntentsOfDyingFrames`, through `ConvertIntent` | Step 5, on the front intents | Converts an intent that names a dying frame into its entity's new frame. This replaces `ConvertSamplesOfMovedFrames`. |
| `World.ConvertMovedIntents`, through `ConvertIntent` | Step 7b, on the front intents | Converts an intent whose frame is not its entity's frame now: after a hand-off of a plain body or a rehome. This replaces `AssertSamplesNeedNoConversion`. |

Every reader reads the front intents, the intents of the step that applies them.

| Reader | When | What it reads | The read it uses |
|---|---|---|---|
| `JoltPhysicsBackend.ApplyBodyIntent`, one `Move` branch for every body that takes a motion intent | Step k + 1, at the start, after the teleport | For a kinematic body, the aim pose as the kinematic target, and the record, for the snap and the step's output. For a dynamic body, the steer target. A case that the body's motion in the step refuses comes only from a bug (see "Output that only a bug produces" in `ARCHITECTURE.md`). | `AimPose`, and `PhysicsIntent.StepRecord` with the body's pose at the start of the step |
| `JoltPhysicsBackend.PredictRecord`, for the ghosts and hand-offs of the bodies around a frame | Step k + 1 | The record of a frame whose owner has an end pose or a full sample. A frame with a dynamic hull takes Jolt's state, as today, with or without a steer target. | `FrameTree.StepRecordInStep` |
| `JoltPhysicsBackend.WriteFrames` | End of step k + 1 | Whether a kinematic hull releases a record, and the record | `KinematicTargetStep`, which the `Move` branch sets, and the record that `StepRecordOf` caches for the hull's frame (see "The step record of a step in flight") |
| `JoltPhysicsBackend.Report` and `HandedOffVelocities`, for a plain body | End of step k + 1 | The record's pose and instant velocity | The record that the snap list keeps |
| `FrameTree.BeginNumericStep` | `PrepareTick` of tick k + 1, before the step | The anchor's record, as the step plan | `FrameTree.StepRecordInStep` |
| `World.FollowOwners`, for a frame without a hull | Step 2 of the sync point of tick k + 1, before the swap | The record | `FrameTree.StepRecordInStep` |
| `World.FollowOwners`, for the follow kind | Step 2, before the swap | The case: a full sample gives `Rails`, and an end pose gives `Kinematic`. A dynamic hull gives `Physics`, with or without a steer target. | `Move.TryGetValue` |
| `World.FollowFramelessEntities` | Step 2, before the swap | The record | `PhysicsIntent.StepRecord` with the transform row's pose |
| `Self.PoseAtNextStepStartIn(FrameHandle frame)` | Tick k + 1, on the entity's worker, its own intent only | The pose where the entity starts the next step: the aim pose of the running step's end pose or full sample, else that step's teleport pose, else its pose now, all in `frame`. A steer target gives no exact end, so the read skips it. For a dynamic body the read is the pose where the running step started, or its teleport pose. | `AimPose` of an end pose or a full sample. It replaces `FrameTree.TryGetAppliedSample` plus `LocalPose(self.OwnedFrame)`, so it works for an entity without a frame. The null frame is the root's own space, in which the motion intent of a root frame is written. |
| `FaultInjectingBackend` | Tests | Every motion intent of each step | `context.Intents` |

Physics skips an intent of an entity without a body, as `ApplyIntents` already skips an entity for which it has no body record.

#### The first motion intent of a spawn, `SpawnDesc.FirstMove`

New. "The first motion intent of a spawn" gives its design.

- Analogue: a body's spawn velocity, `SpawnDesc.LinearVelocity`, which the spawner gives for the entity's first moment. And the motion intent that the entity would have written in the spawn's tick, which the step after the spawn applies.
- Store: the spawn description until the entity goes live, then the new entity's front intent, the field `PhysicsIntent.Move`. No other store.
- Change: the spawner writes it into the description. Step 6 of the sync point of the spawn's tick, or the call of `World.Spawn` outside a tick, stores it in the front intent. The step after the spawn applies it, and step 4 of the next sync point clears it.

| Writer or reader | When | What |
|---|---|---|
| The spawner: a component through `self.Spawn`, or a game through `World.Spawn` | Tick k, or outside a tick | Writes the case for the new entity's first step, which ends at `self.TimeAt(self.Tick + 2)` in a tick and at `World.TimeAt(World.TickNumber + 1)` outside a tick |
| `World.PrepareSpawn` | At the call of `self.Spawn` or `World.Spawn` | Refuses a case that the entity would refuse, by `MotionIntent.ProblemFor` with the facts of the description, and a number that is not finite or a zero rotation, with an `ArgumentException` |
| `World.MakeLive`, through `EntityTable.SetFirstMove` | Step 6 of the sync point of tick k, or at the call of `World.Spawn` outside a tick | Stores the intent, with its rotation made unit length, in the new entity's front intent, and lists the entity in `IntentEntities` |
| Every reader of the front motion intents in the table above | Step k + 1 and tick k + 1, and the sync point after them until step 4 | Reads it as a motion intent that the entity wrote. No reader can tell the two apart. |

#### The step record of a step in flight

Derived, never stored, except for the step plan below. Physics derives it once in each step for each kinematic body that it moves. Its snap list (`kinematicTargets`, before `railsTargets`) keeps each aimed body's target pose for the snap. The record of a hull's frame is the record that `StepRecordOf` caches for the step, which `PredictRecord` derives with `FrameTree.StepRecordInStep`, the read that ghosts and hand-offs use too, and `WriteFrames` knows an aimed hull by `KinematicTargetStep`. Stage 6 adds a plain body's record to the snap list, for its body state and its hand-off. The sync point derives it at step 2 for an entity without a body. A steer target has none. `PhysicsStepContext.Samples` goes: physics reads `context.Intents`, which it already has.

#### The step record of the last finished step

Moved from the followed buffer into the frame tree row.

- Analogue: `FrameTree.HullMotion`, the stored value of the last finished step, which changes at the sync point. And the frame tree row as it is today: `FrameTree.Follow` writes the pose, the instant velocity, the angular velocity and the proper acceleration at step 2.
- Store: the frame tree row, plus a new follow kind per frame. The row keeps what `FrameTree.Follow` writes today: the record's pose, instant velocity and angular velocity, and the frame's proper acceleration, which `Follow` now derives from the record (see "The proper acceleration of a frame"). The record's chord, acceleration and angular acceleration are not kept after step 2. When its design needs one, spin gravity adds its own store (see docs/plans/spin-gravity.md, "Conversions between frames").
- Change: at step 2, by `FrameTree.Follow` or `FrameTree.HoldStill`. Step 5 and step 6c move a frame between parents, and the row's pose and velocity convert there, as today.

| Writer | When | What |
|---|---|---|
| `FrameTree.Follow(frame, relativeTo, record, kind, measured)` | Step 2, from `FollowOwners` | The record's pose, instant velocity and angular velocity, the follow kind, and the proper acceleration derived from the record or, for a dynamic hull, the value `measured` that physics released, which the tree writes after the loop of `FollowOwners` (see "Which proper acceleration of the parent") |
| `FrameTree.HoldStill` | Step 2 | At rest in the tree parent, with the proper acceleration derived from that rest, which the tree writes after the loop. The follow kind becomes `Static`. |
| `FrameTree.Reparent`, the numeric update | Steps 5 and 6c | The pose and the velocity, converted to the new parent, as today. The proper acceleration, in the frame's own axes, stays until the next step 2. |

| Reader | When | The read it uses |
|---|---|---|
| `MirrorFrameOwner` | Steps 2, 5, 6c and 7 | The tree row, as today |
| Every read in a frame, such as `VelocityIn` | Any time | The tree row, as today |
| `FrameTree.CreateOrbitFrame` | Step 6c | The frame's place and velocity in the tree row. Today it reads the chord from the followed buffer (see "The step plan of a following frame"). |
| `FrameTree.Followed`, read by `DumpRecorder.FollowsOf` | After the sync point | The stored follow kind |

#### The follow kind

Moved from the rows of the sample table into the frame tree, one byte per frame. Its values follow the names of the two writers, because "rails" names rails motion, which an end pose is not:

| Value | The frame's record came from | Dump string |
|---|---|---|
| `Rails` | Its owner's full sample | `rails` |
| `Kinematic` | Its owner's end pose. This value is new. | `kinematic` |
| `Physics` | Physics, for a hull that was dynamic in the step, with or without a steer target | `physics` |
| `Static` | A teleport, a transform write, or no record (the frame held still) | `static` |

`FollowOwners` writes the kind of the last record that the frame takes. So a transform write that wins over a motion intent records `Static`. Today the followed row keeps "rails" while the frame stands on the written pose. Two values decide a hull's kind. They are the motion of the hull in the step (`FrameTree.HullMotionInStep` with the front intents), and the case of its motion intent. Both are read before the swap.

#### The step plan of a following frame

New. It replaces `FrameTree.sampleVelocityPastChord`, and the reads of the applied buffer in `BeginNumericStep`, `PlaceFollowingFrame` and `JumpFollowingFrame`.

- Analogue: `FrameTree.stepAcceleration`, the acceleration of a numeric frame for the step, which `BeginNumericStep` fixes before the step.
- Store: one `MotionSample?` per numeric frame in the frame tree. Few numeric frames exist.
- Change: `BeginNumericStep` derives it in `PrepareTick` from the anchor's motion intent, with `FrameTree.StepRecordInStep`. The named reason to keep it for the step: an end pose needs the place where the step starts, and step 2 moves the following frame (`AdvanceNumericFrames`) before it reads the plan.

| Reader | When | What |
|---|---|---|
| `BeginNumericStep` | `PrepareTick` | The step acceleration that lands the frame on the plan: the plan's chord minus the frame's velocity, divided by the step. It also gives the anchor's instant velocity past the chord, the plan's velocity minus its chord. |
| `HomeInstantVelocityOverChord` | Physics and step 2 | The plan's velocity minus its chord, as `sampleVelocityPastChord` today |
| `PlaceFollowingFrame` | Step 2 | The plan's place and chord, exactly |
| `JumpFollowingFrame` | Step 2 | The plan's place and chord. After a teleport without a motion intent, the record that physics released, converted into the semantic parent, as `KeepRecord` converts it today. |

The landing rule changes one number of today's model, for a named reason: an end pose has no acceleration that lands the frame on it, because its velocity changes at the start of the step. For a full sample the landing acceleration equals the sample's acceleration to about 3e-10 m/s² in low orbit. Jolt and the numeric frames use one scheme: the velocity gains the acceleration, and then the place gains the velocity. The contents of a numeric frame feel the field minus the frame's acceleration. So in that scheme, the frame's acceleration cancels out of their motion in the semantic frame. Only where the frame lands changes, and it lands on the plan. So `CreateOrbitFrame` needs no chord: a new following frame starts at its anchor's place with its anchor's instant velocity, and its first plan lands it. Its first planned step has a limit (see "The first step of a new following frame" below). The case "before the hull's first sample" goes. A limit: a body that sleeps in a following frame does not feel its effective gravity, so it moves with the frame. An end pose that changes velocity can drag it. `ter-zg77`, which brings following frames for motion that is no orbit, decides the wake rule.

##### The first step of a new following frame

A limit of the landing rule. The builder of stage 5 found it, and the orchestrator decided it on 2026-10-19 (the decision "The first step of a new following frame").

The first planned step of a new following frame is the first step that applies a motion intent of its anchor. With a first motion intent of the spawn, that is the frame's first step. Without one, the anchor holds still in the frame for its first step, the frame falls as a falling frame does, and the first planned step is the second. A new following frame starts with its anchor's instant velocity (`CreateOrbitFrame`). In its first planned step, the landing rule gives it the acceleration that turns that velocity into the plan's chord. On an orbit the two velocities differ by about half a step of the field. So in that one step the frame's acceleration is about half the field: 4.3 m/s² on a low orbit of 6,800 km, where the field is 8.6 m/s². From the next step on, the frame's velocity is the chord of the step before, and the frame lands on each plan with the acceleration of the field.

What the contents' system shows in that step:

- The gravity in the frame's system is the field minus the frame's acceleration, so the other half of the field, about 4.3 m/s², in place of the tidal pull. Physics gives that gravity to every body in the frame (`FrameTree.EffectiveGravity`).
- A character takes its form from that gravity. So a character at rest beside a new fast hull, outside the hull's bubble, takes the walking form for that one step, and the rigid form again from the next step. It does not stand, so it falls freely in both forms.
- A body whose velocity physics carries over from an earlier step keeps its motion in the semantic frame, because the frame's acceleration cancels out of it (see the landing rule above). A character spawned beside a Kepler hull without a first motion intent stays beside the hull to a micrometer in a second.

`CharacterFormNumericFrameTests.ACharacterAtRestBesideAKeplerHullIsRigidInTheHullsOrbitFrameAndWalksInEarthsOwnSystemAndStaysBesideTheHull` reads the form from the step after the first plan. Test 66 pins the limit.

##### Open: a velocity that a spawn gives in the first planned step of a new following frame

The builder who settled the subsection above found this, and nobody has decided it. A body that spawns in the semantic parent beside a fast hull in the same batch as the hull, when the hull's spawn carries a first motion intent, joins the hull's new following frame before the frame's first planned step. That body does not keep its motion in the semantic frame. It moves away from the exact orbit, and from the hull, at about a quarter of a step of the field: 3.6 cm/s on the low orbit of 6,800 km, so 3.6 cm after one second, for a character and for a dynamic ball. Measured on the real World and Jolt, with the scene of the test above:

| Scene | The body's distance from the exact orbit after one second |
|---|---|
| The hull without a first motion intent, the body spawned with it | 7e-7 m |
| The hull with a first motion intent, the body spawned with it | 3.6e-2 m, growing by 0.6 mm in every step |
| The hull with a first motion intent, the body spawned one tick before it, so already moving | 7e-7 m |
| The body spawned beside a hull whose frame has made five steps | 9e-7 m |
| No numeric frames, the hull with a first motion intent | 4.8e-3 m, the float rounding that the test above allows |

The likely cause, by the arithmetic, which matches the measured rate: physics converts a velocity that the spawn or gameplay gives in a step from the frame's velocity into its system by subtracting the frame's instant velocity past its frame velocity (`HomeInstantVelocityOverChord`). For a following frame with a plan, that is the plan's instant velocity minus its chord, about half a step of the field. That is right when the frame's velocity is the chord of the step before. A new following frame's velocity is its anchor's instant velocity, and in that step the body's system shows only half the field. Together, the body ends the step with a velocity a quarter of a step of the field off. Without a first motion intent, the frame's first step falls, the same subtraction is exact for a falling frame, and the first plan comes one step later, when the frame's velocity is a chord.

So the first reason of the decision "The first step of a new following frame", that the contents' motion in the semantic frame stays exact, holds for every body except this one. Whether this is a second limit of the same start, or a reason to start a new following frame with a velocity that the numeric frames' convention expects, is open.

#### The proper acceleration of a frame

Changed: the frame tree derives it from the step record by one rule. Before, the sample carried it, and a frame without a record kept its last value. The user gave the rule on 2026-10-18 (the decision "How a frame gets its proper acceleration").

The rule: the proper acceleration of a frame is its acceleration minus the gravity that acts at its place, in the frame's own axes. `FrameTree.Follow` and `FrameTree.HoldStill` call one internal function for it, `FrameTree.ProperAccelerationFrom(frame, record, step)`:

```csharp
Double3 start = record.Pose.Position - record.ChordVelocity * step;
Double3 gravity = parent.IsNull ? Double3.Zero : FieldOf(parent, start);
Vector3 proper = record.Pose.Rotation.Conjugate().Rotate(record.Acceleration - gravity).ToVector3();
```

where

- `record` = the frame's step record in its semantic parent. The table below says what that is for each kind of record.
- `step` = the length of the step that ended, in seconds: the same double that the rails components widen `self.DeltaTime` to.
- `start` = the place of the frame's origin where the step started, in the semantic parent. The gravity there is the gravity that the step's acceleration acted against. Jolt and the numeric frames give a step's change of velocity from the gravity where the step starts, and `KeplerOrbit.Sample` takes its acceleration there too.
- `parent` = the frame's semantic parent. It is null for a root frame. No gravity acts at a root's place, so a root's value is its record's acceleration in its own space. That is zero for every root frame in this epic.
- `FieldOf(parent, start)` = the gravity that acts at the frame's place: the parent's field at `start` minus the parent's own proper acceleration, both in doubles and in the parent's axes. It is today's private `FrameTree.FieldOf`, which `EffectiveGravity` also uses for the bodies in a semantic frame. So a frame feels what a body at its origin feels. It reads the parent's proper acceleration as the step found it, the value stored before step 2 (see "Which proper acceleration of the parent").
- `record.Acceleration` = the frame's acceleration during the step, relative to the parent, in the parent's axes.
- `Conjugate().Rotate` = turns the difference from the parent's axes into the frame's own axes, by the frame's rotation at the end of the step, which the row keeps after step 2. Physics turns its measured value by the hull's rotation after the update, the same moment.
- `ToVector3` = narrows the value once, to the tree's float field.

The record in the semantic parent:

| Record | The record in the semantic parent |
|---|---|
| An end pose or a full sample | The record itself, because a motion intent is relative to its frame's semantic parent. For the anchor of a following frame, it is the step plan. Physics' record at the origin of the following frame is the same plan, expressed in the following frame. |
| A teleport that physics releases, or a transform write | The record: at rest in the tree parent, with zero acceleration. Only a dynamic hull or the anchor of a following frame hangs in a numeric frame. For an anchor whose hull teleports without a motion intent, `JumpFollowingFrame` converts the record into the semantic parent, as it does for its place. |
| No record: the frame holds still in its tree parent | At rest in the tree parent. Under a numeric tree parent, the frame moves with the numeric frames between it and its semantic parent. So in the semantic parent its place is its tree pose plus their places (`HomeOffset`), its chord is the sum of their velocities, which are their chords, and its acceleration is the sum of their step accelerations (`HomeAcceleration`). Its rotation is its tree rotation, because a numeric frame never turns. `HomeAcceleration` is the analogue of `AccelerationRelative` for numeric frames, as its summary says. |
| The record of a dynamic hull | None: physics measures the value by the same rule, and releases it beside the record |

Physics measures the value of a dynamic hull, as today (`ComputeProperAccelerations`): Jolt's whole change of velocity over the step, minus the gravity that physics applied at the hull's centre of mass, turned into the hull's axes, with a dead band for float noise. That is the same rule, evaluated by the one part of the engine that knows a dynamic hull's acceleration. `MotionSample` loses its field, so the value travels beside the record: `FrameStepRecord` gains `Vector3? ProperAcceleration`, which physics fills for a dynamic hull only, and `Follow` takes it in place of a derived value. Derived from the record of a dynamic hull, the value would misbehave in two named ways:

- The record's acceleration leaves out the velocity that the step's intents set, an impulse, a `SetVelocity` or a teleport, because the frame's motion over the step starts from it. An accelerometer feels it as a jolt. `IntentTests` expects Δv / dt in the step of an impulse.
- Jolt keeps velocities in floats, and applies gravity in floats. The record's acceleration minus the gravity in doubles keeps their rounding, about 5e-7 m/s² in low orbit. The cargo of a ship in orbit then drifts. `GravityTests.ShipInOrbitIsInFreeFallAndItsCargoFloats` expects exactly zero in every step, and the crate within 1e-9 m.

What the rule gives:

| Frame | Proper acceleration |
|---|---|
| A static hull, a frame without a body, or a kinematic hull without a motion intent, at rest on a planet | The planet's gravity at its place, reversed: its contents have weight |
| A ship that parks | The gravity at its place, reversed: weight on the ground, and no thrust in free space |
| A frame on a Kepler orbit, in a parent that falls freely | Exactly zero |
| A turntable (`SpinMotion`) that holds its place on the ground | The ground's gravity, reversed |
| A frame that an end pose moves at a constant velocity | The gravity where the step starts, reversed |
| A frame at rest at the origin of a falling frame | About zero, to rounding: the falling frame's step acceleration is the gravity at its origin where the step starts |
| A frame at rest in a ship that thrusts | The ship's proper acceleration, turned into the frame's axes |
| A frame at the centre of its parent's point field, such as the ground of a planet, which turns at the planet's centre | Zero, because `GravityField.AtPrecise` gives zero within 1 mm of the centre. The frame's own field alone acts on its contents. |
| A root frame | Zero |
| A dynamic hull | Physics' measurement, as today |

A frame on a Kepler orbit gets exactly zero, not zero to rounding. `KeplerOrbit.Sample` computes its acceleration as `KeplerOrbit.Acceleration` at `position - chord * step`. The rule computes `start` from the same two fields by the same expression. It evaluates the parent's point field with `GravityField.AtPrecise`, whose expression is the same as `KeplerOrbit.Acceleration`'s, `position * (-mu / (r2 * Math.Sqrt(r2)))`, with the G·M that `KeplerOrbit.MuFrom` read from that field. The parent's own proper acceleration is exactly zero when the parent falls freely: the root, or a frame on a Kepler orbit itself. A sample that converts at step 5 or 7b misses for one step, as its pose does.

The value:

- Analogue: `FrameTree.AngularVelocity`, a value of the last finished step that the tree writes at step 2, and that gameplay reads but cannot write.
- Store: the frame tree's float `Vector3` per frame, in the frame's own axes, as today. `MotionSample` and `SpinMotion` hold none.
- Change: at step 2. `Follow` and `HoldStill` derive the new value in the loop of `FollowOwners`, and `FrameTree.StoreNewProperAccelerations` writes it after the loop (see "Which proper acceleration of the parent"). A frame that a failed step or the output check skips keeps it, as it keeps its velocities. A frame that moves to another parent at step 5 or 6c keeps it until the next step 2, because the value describes the step that ended. A new frame has zero until the first step 2 after its spawn (see "The proper acceleration of a new frame").

| Writer | When | What |
|---|---|---|
| `FrameTree.Follow` | Step 2, in the loop of `FollowOwners` | Derives the value from the record by the rule, or takes the value that physics measured for a dynamic hull, and adds it to the tree's list of new values, `newProperAccelerations` |
| `FrameTree.HoldStill` | Step 2, in the loop of `FollowOwners` | Derives the value from the frame's rest in its tree parent by the rule, and adds it to `newProperAccelerations` |
| `FrameTree.StoreNewProperAccelerations` | Step 2, once, after the loop of `FollowOwners` | Writes each value of `newProperAccelerations` into its frame's row, and clears the list |
| `FrameTree.Create` | At a spawn, and outside a tick | Zero, as today: the value during the frame's first step (see "The proper acceleration of a new frame") |
| `JoltPhysicsBackend.ComputeProperAccelerations` | Each step | Measures a dynamic hull's value into `FrameStepRecord.ProperAcceleration`. It writes physics' own record of the hull, not the tree. |

| Reader | When | The read it uses |
|---|---|---|
| `FrameTree.EffectiveGravity`, for the bodies in the frame, and the wake test of `ApplyGravity` | Each step | The stored value, as today |
| `ProperAccelerationFrom`, for each child frame | Step 2, in the loop of `FollowOwners` | The parent's value stored before step 2, through `FieldOf` (see "Which proper acceleration of the parent") |
| Gameplay, through the snapshot's frame tree, and tests | Any time | `FrameTree.ProperAcceleration(frame)`, a value read that returns a `Vector3`, as `AngularVelocity` does. It no longer returns a `ref`, so gameplay cannot write it. |

Limits of the rule:

- The parent's rotation adds nothing. Until spin gravity, the bodies in a turning frame feel no rotation terms, so neither does a frame in it. Spin gravity adds the terms to both (see docs/plans/spin-gravity.md, "The rotation terms").
- An end pose has no acceleration of its own. So a change of its velocity at the start of a step gives no jolt, where an impulse on a dynamic hull gives one. The contents of the frame move with it, as they do whenever a frame's velocity changes without a proper acceleration.
- The rule evaluates the gravity at the frame's origin. Physics evaluates a dynamic hull's at its centre of mass. The two differ by the tidal gravity across that offset.
- A frame that holds still with a field of its own that copies its parent's gravity, to give its contents weight, now gives them twice that weight. Stage 1 finds such frames in the tests and removes their own fields. The Demo scenes have none: their light frames hold no bodies, the ground of the planet sits at the planet's centre, and the ship's hull is dynamic.
- A new frame on rails whose spawn carries no first motion intent holds still for its first step. So its first step 2 gives it the gravity at its place, reversed, and its contents feel that gravity in its second step: they gain 0.145 m/s at 400 km above Earth. Its first sample puts it on its rails, and from the second step 2 on, its value is exactly zero on a Kepler orbit in a parent that falls freely. A spawn that carries the first motion intent avoids this (see "The first motion intent of a spawn"). The Demo's frames on rails carry theirs.

#### Which proper acceleration of the parent

`FieldOf` subtracts the parent's own proper acceleration. At step 2 the parent has two values: the value that the step found, and the value that the parent takes in the same pass of `FollowOwners`. The rule reads the value that the step found: the value stored before step 2, which physics used for the parent's bodies during the step, and which a dynamic hull's measurement uses. The user chose this on 2026-10-18 (the decision "Which proper acceleration of the parent the rule reads").

So step 2 handles the proper accelerations in two passes:

1. The loop of `FollowOwners` visits every frame in the tree's dense order. `Follow` and `HoldStill` derive the frame's new value, and keep it in the tree's list of new values, `newProperAccelerations`, with the frame's index. They do not write it into the row yet. So each `FieldOf` in the loop reads its parent's value as stored before step 2, whether the loop visited the parent already or not.
2. After the loop, `FollowOwners` calls `FrameTree.StoreNewProperAccelerations` once. It writes each new value into its frame's row, and clears the list.

A frame that the loop skips, after a failed step or for output that the check refused, keeps its stored value, as it keeps its velocities.

An example: a ship's engine starts in step N, so the ship reads 1 m/s² at the sync point after step N. A turntable at rest in the ship reads 1 m/s² one sync point later, together with a fighter that rests in the ship's hangar. A frame at rest in the turntable reads it one sync point after the turntable. So a change moves one level down the chain in each step.

The reason: the rule reads the gravity that acted at the frame's place during the step, which is what physics applied there. So a dynamic child and every other child of the same parent agree in every step, and the order of the visits does not matter.

#### The proper acceleration of a new frame

The rule needs a step record, and a new frame has none before its first step. A new frame's value is zero during its first step, as `FrameTree.Create` sets it. The first step 2 after its spawn derives the value by the rule, from the frame's first record or from its rest, as for every frame. A dynamic hull takes physics' measurement from its first step 2 on. The user chose this on 2026-10-18 (the decision "The proper acceleration of a new frame").

An example: a parked cabin spawns on a planet with a crate 1 m above its floor. The crate hangs for one step, then falls.

A new frame on rails whose spawn carries a first motion intent is on its rails in its first step. So its first step 2 derives exactly zero on a Kepler orbit in a parent that falls freely, and its contents never feel the gravity at its place. Without one, the frame holds still for its first step, as the last limit of the rule above says.

The reason: this is the pure model, one rule from a step record, with no guess about a step that has not run. Its cost is one step without weight.

#### The transform row of an entity without body or frame

A new writer, `World.FollowFramelessEntities`, at step 2 after `FollowOwners`. It visits the front intent entities that have no body and no frame. For each, it writes the record's pose and velocities into the back row with `SetBackTransform`, unless the entity wrote its transform in the tick: that write wins for one tick, as for a frame. It must run before step 5, where `Rehome` converts the row, and before step 7, where `Derive` reads the root's row for the attached children.

`EntityTable.WritableTransform` also sets the row's velocities to zero. So a transform write leaves every entity without a body at rest, as a frame owner's write does today. Today a bodyless entity spawned with a velocity keeps it while `SetTransform` moves it, and `VelocityIn` reads it.

| Writer or reader | When | What |
|---|---|---|
| `EntityTable.WritableTransform`, from `Self.SetTransform` | Tick k, on the entity's worker | Writes the pose and zero velocities into the back row |
| `World.FollowFramelessEntities` | Step 2 | Writes the record's pose and velocities, unless the tick wrote the transform |
| `World.Rehome` | Step 5 | Converts the row out of a dying frame. The motion intent converts beside it. |
| `Attachments.Derive` | Step 7 | Reads the root's back row for the attached children |
| `EntityTable.CopyDirtyRows`, the spatial index, the render snapshot | Steps 8 and 11 | Read the front row, as for every entity |
| `Self.VelocityIn`, `Snapshot.TryGetVelocityIn` | Tick k + 1 | Read the front row, in the frame that the read names |

#### The entities without body or frame that moved in the last step

New: `World` keeps the entities without body or frame that took a record at the last step 2, in two lists that it swaps. At step 2, each one in the old list that takes no record now and wrote no transform gets zero velocities: it holds still, as `FrameTree.HoldStill` does for a frame. The analogue is physics' `kinematicMoversLast`, which stops a body after its last kinematic target.

### The sync point and the step, step by step

| Moment | Today | After |
|---|---|---|
| Tick k | `MoveOnRails` writes the table's written buffer. `MoveKinematic` writes `PhysicsIntent.Target`. | `MoveOnRails`, `MoveKinematic` and `SteerTo` write `PhysicsIntent.Move`. |
| `PrepareTick` | `BeginNumericStep` reads the applied buffer | `BeginNumericStep` derives each following frame's step plan from the front intents, and its landing acceleration |
| Step | `ApplyIntents` aims plain bodies at `Target`, and steers a dynamic body to it. Then `AimHullsOnRails` aims hulls at their samples. Only hulls snap. `PredictRecord` and `WriteFrames` read the applied buffer. | One `Move` branch in `ApplyBodyIntent` aims every kinematic body at its end pose, after its teleport, so the stop rule never zeroes it first. The same branch steers a dynamic body to its steer target, as today. Every kinematic body that it aims snaps. `PredictRecord` derives a frame's record from its owner's motion intent. A plain body reports its record. A dynamic hull's measured proper acceleration goes beside its record, in `FrameStepRecord.ProperAcceleration`, because the record has no such field. |
| 2 | `FollowOwners` reads physics' records and the applied buffer. `KeepRecord` puts each record into the applied buffer. | `FollowOwners` reads physics' records, and derives the record of a frame without a hull from its owner's front intent. It writes the follow kind. It derives each frame's proper acceleration from its record or its rest, with its parent's value as stored before step 2, and takes a dynamic hull's from physics. After the loop, `StoreNewProperAccelerations` writes the new values. `KeepRecord` goes. `PlaceFollowingFrame` and `JumpFollowingFrame` read the step plan. Then `FollowFramelessEntities`. |
| 4 | Drops of the plan, the swap, then `Samples.MoveOn` | The drops act on `Move` in the written intents, by the guard's rule. The swap exchanges the intent buffers, and keeps `Move` without a backend. No `MoveOn`. |
| 6 | A new frame holds still for its first step | A spawn's first motion intent goes into the new entity's front intent (`EntityTable.SetFirstMove`), which the next step applies. A spawn without one holds still for its first step, as today. |
| 5 | Intents and samples convert separately | Only intents convert, `Move` among them |
| 6c | `CreateOrbitFrame` reads the followed buffer | `CreateOrbitFrame` reads the frame tree row |
| 7b | Intents convert. A contract checks that no sample needs a conversion. | Intents convert, `Move` among them. The contract goes. |

Determinism: each entity writes only its own intent, and each reader reads only one entity's intent. So the order of the chunks and of the visits changes nothing. The proper acceleration of a frame reads only its own record and its parent's field and stored value, and the tree writes the new values after the loop, so the order of the visits does not matter either. A spawn's first motion intent enters `IntentEntities` in the order of the spawns, which is fixed. `DeterminismTests` with one and eight workers checks it.

### What goes, and the docs to change

From the code:

- `MotionSampleTable` with its three buffers and rounds, `MotionSamples`, `FrameTree.Samples`, `FrameTree.TryGetAppliedSample`, the call of `Samples.Grow` in `FrameTree.Grow`, and `PhysicsStepContext.Samples`.
- `World.KeepRecord`, `World.ConvertSamplesOfMovedFrames`, `IsRelativeToDyingFrame` and `AssertSamplesNeedNoConversion`.
- The refusal in `World.MoveOnRails` of an entity that owns no frame, and `World.WritableMotionIntent` as the guard of `MoveKinematic`. It stays the guard of `SetVelocity` and `SetAngularVelocity`.
- `PhysicsIntent.Target`, `IntentFlags.KinematicTarget`, the target line of `ConvertIntent` (it becomes the `Move` line), and the clear of `KinematicTarget` in `ApplyMotionChanges`.
- `Samples.DropWritten` in `FrameTree.PlanHullMotion`.
- The duplicated physics paths: `AimHullsOnRails` and the `KinematicTarget` branch of `ApplyBodyIntent` become one `Move` branch. The code that gives a dynamic body the velocities to a target stays, as the steer case of that branch. `SnapHullsOnRails` becomes `SnapKinematicBodies`, for every kinematic body that a motion intent moved. `stepSamples` goes.
- `FrameTree.sampleVelocityPastChord`, which the step plan replaces.
- `RailsHull` in the components, which the guard's read replaces. If its entity's body is kinematic in the next step (`self.Body.NextStepMotion`), a component teleports the body on its first run.
- `BodyHandle.MoveKinematic`, which moves to `Self`. `BodyHandle.SteerTo` takes its case for a dynamic body.
- `MotionSample.ProperAcceleration`, the parameter `properAcceleration` of `FrameSpin.Sample`, `SpinMotion.ProperAcceleration`, and the `ref` that `FrameTree.ProperAcceleration` returns.
- The proper acceleration in the records that physics makes for a teleport and for a frame without a motion intent, and in the record that `FollowOwners` makes for a transform write. Each of them copied the frame's value, so that the frame kept it.

These docs follow this design since it was written:

- The other sections of this doc.
- The section "Conversions between frames" of `docs/plans/spin-gravity.md`, and a paragraph at the end of its section "The rotation terms": spin gravity adds the rotation terms to the rule of "The proper acceleration of a frame".
- The acceleration of an orbit frame in the section "Orbit frames" of `docs/plans/numeric-frames.md`. `ter-zg77` rewrites the rest of that section.

In `ARCHITECTURE.md`:

- "Threads and the tick": steps 2, 4, 5 and 7b, and the preparation of the tick.
- "Entities": the physics row holds the motion intent. A spawn description can carry the new entity's first motion intent, which step 6, or the call of `World.Spawn` outside a tick, puts into its front intent, and `World.TimeAt` is public.
- "Ownership of transforms": every kinematic body moves only by its motion intents and teleports, and `SetVelocity` and `SetAngularVelocity` throw for it. A kinematic hull takes `MoveKinematic`. A dynamic body takes `SteerTo`. A motion intent converts as every intent does. An entity without a body takes end poses, full samples and `SetTransform`.
- "The frame tree": the follow kind, and no `Samples`. `ProperAcceleration` is a value read, which step 2 derives by the rule of "The proper acceleration of a frame".
- "How a frame moves" and "Changes of motion": the guard list of `NextStepHullMotion` and the drops. A frame takes the proper acceleration that the rule derives from its record, and a frame that holds still feels the gravity at its place reversed, where today it keeps its value. A parked ship keeps no thrust.
- "Rails components and the sample table" becomes "Rails components and motion intents". A sample has no proper acceleration, and `SpinMotion` no such property.
- "Gravity": the bodies in a frame that holds still in a gravity field feel that gravity through the frame's proper acceleration. A frame whose own field copies its parent's gravity doubles it.
- "Physics": the dead band of the proper acceleration stays. Physics releases a dynamic hull's value beside its record.
- "Destroying a frame": the motion intents convert.
- "Numeric frames": the "Orbit frames" row takes the step plan and the landing rule.
- "Output that only a bug produces": a motion intent whose case the body's motion in the step refuses comes only from a bug: an end pose or a full sample on a body that is not kinematic, or a steer target on a body that is not dynamic. A Debug build breaks a contract. A Release build ignores it and records it in `World.Errors`.

Elsewhere:

- `CLAUDE.md`, "Where the user changed my proposals": a new row. My proposal was a frame-keyed sample table with three buffers beside the intents. The user wanted a motion intent in the entity's intent, with the intent's life. It is a union of an end pose, a full sample and a steer target.
- The ticket `ter-zg77`: a following frame whose anchor is a plain body needs an anchor keyed by entity. `orbiter[]` holds frames today, and `BeginNumericStep` reads its anchor's intent by entity after this ticket.

### What changes for gameplay

- `self.MoveKinematic(endPose)` moves from `self.Body` to `self`. A kinematic hull, a frame owner without a hull and an entity without body or frame take it now. A dynamic body refuses it.
- `self.Body.SteerTo(target)` is new. It does for a dynamic body what `MoveKinematic` did: physics gives the body the velocities that carry it to the target over the step. A component that steered a dynamic body with `MoveKinematic` calls `SteerTo` instead.
- `self.MoveOnRails(sample)` takes every entity that the guard lets through, a plain kinematic body and an entity without a frame included.
- A static body, a character and an attached entity refuse all three writers, with an `InvalidOperationException`. A character and a static body ignored `MoveKinematic` without a word before.
- `SetVelocity` and `SetAngularVelocity` throw for every kinematic body, and a spawn refuses a velocity on a kinematic body that owns no frame. A component that moved a kinematic body with a velocity writes an end pose in every tick instead.
- `self.PoseAtNextStepStartIn(frame)` and `self.MotionIntentProblem(intent)` are new reads.
- No gameplay writes a proper acceleration. `FrameTree.ProperAcceleration` is a value read, `SpinMotion` loses its property `ProperAcceleration`, and `FrameSpin.Sample` loses its parameter. A frame that holds still in a gravity field gives its contents weight with no component.
- A state dump can report `kinematic`.
- A transform write leaves an entity without a body at rest.
- A spawn description can carry the new entity's first motion intent, `SpawnDesc.FirstMove`, which acts in the step after the spawn. A spawner computes it, with `self.TimeAt(self.Tick + 2)` in a tick and with the now public `World.TimeAt(World.TickNumber + 1)` outside a tick. So a station spawned in orbit is on its rails from its first step. A spawn refuses a first motion intent that its entity would refuse, with an `ArgumentException`.

### Tests

Stage 1, the proper acceleration of a frame. The rule's function has tests in `Tertius.Core.Tests/FrameProperAccelerationTests.cs`, and the scenes run the real World. Stage 2 writes tests 7 and 12, by the user's answers:

1. `AFrameOnAKeplerOrbitInAFreelyFallingParentHasExactlyZeroProperAcceleration`, with a body and without, in its semantic parent and in a following frame
2. `AFrameThatHoldsStillInAGravityFieldFeelsThatGravityReversed`, for a static hull, a frame without a body, and a kinematic hull without a motion intent
3. `AStaticHullThatTeleportsFeelsTheGravityAtItsNewPlaceReversed`, which replaces `AStaticHullThatTeleportsKeepsItsFramesProperAcceleration`
4. `ATurntableOnTheGroundGivesItsCargoWeightWithNoValueToCopy`, which replaces the copy into `SpinMotion.ProperAcceleration` in `SpinFrameTests`
5. `AShipThatParksFeelsTheGravityAtItsPlaceReversedAndKeepsNoThrust`
6. `ATransformWriteGivesTheFrameTheGravityAtItsPlaceReversed`
7. `AFrameInAnAcceleratingFrameFeelsItsParentsProperAcceleration` one sync point after its parent takes it, at the same sync point as a dynamic hull that rests in the parent, and a frame at rest in the child one sync point later again (the decision "Which proper acceleration of the parent the rule reads")
8. `AFrameThatHoldsStillInAFallingFrameIsInFreeFall`
9. `ADynamicHullTakesTheProperAccelerationThatPhysicsMeasured`, the jolt of an impulse and zero in orbit, through `FrameStepRecord.ProperAcceleration`
10. `TheProperAccelerationIsInTheFramesOwnAxes`
11. `ARootFrameAndAFrameAtTheCentreOfAPointFieldFeelNoGravity`
12. `ANewFrameTakesItsProperAccelerationByTheRuleAtItsFirstSyncPoint`: zero during its first step, and the rule's value from the first step 2 after its spawn on. A crate spawned 1 m above the floor of a parked cabin that spawns on a planet hangs for one step, then falls (the decision "The proper acceleration of a new frame").

Stage 2, the parent's value as the step found it, and the first motion intent of a spawn. Besides tests 7 and 12, its tests take the numbers after 58, so that the numbers of the other stages stay as the reports name them:

59. `ANewFrameOnRailsWithAFirstMotionIntentStandsOnItsRailsAtTheFirstSyncPointAfterItsSpawn`, with a kinematic hull and without a body, spawned outside a tick and by a component in a tick. From that sync point on it stands at `orbit.StateAt(World.Time, mu)` to the last bit, its follow kind there is `rails`, and the teleport of the first run of its `KeplerMotion` does not move it.
60. `TheContentsOfAStationSpawnedInOrbitWithItsFirstMotionIntentFeelNoGravity`: a crate that spawns at rest in the station's cabin together with the station, 400 km above a planet, keeps its place in the station to 1e-9 m, and the station's proper acceleration is exactly zero at every sync point.
61. `AFirstMotionIntentActsInTheFirstStepAsAnIntentOfTheSpawnsTickWould`: a plain kinematic body ends its first step exactly on the end pose of its first motion intent, and a dynamic body's first step carries it toward the pose of its steer target.
62. `AFirstMotionIntentActsInOneStepOnly`: an entity whose first motion intent no later intent follows holds still in its second step.
63. `AFastHullLandsItsFollowingFrameOnItsFirstMotionIntentAtTheFirstSyncPoint`
64. `ASpawnRefusesAFirstMotionIntentThatItsEntityWouldRefuse`, one case for each rule of the guard, with an `ArgumentException`, outside a tick and through `self.Spawn`: an attached entity, a character, an end pose and a full sample on a dynamic body and on a static body, and a steer target on a kinematic body, on an entity without a body and on the owner of a root frame
65. `ASpawnRefusesAFirstMotionIntentWithANumberThatIsNotFiniteOrAZeroRotation`, and the stored intent has a unit rotation

`StationSliceTests` and `OrbitSliceTests` pass unchanged with the first motion intents of the Demo scenes. `StateDumpTests.ANewFrameOnRailsFollowsRailsFromTheSecondSyncPointOnBecauseItHoldsStillAtTheFirst` keeps its spawn without one, and so proves that such a spawn still holds still for its first step.

Stage 3, the union, in `Tertius.Core.Tests/MotionIntentTests.cs`:

13. `AnEndPoseGivesAConstantVelocityRecordFromWhereTheStepStarts`
14. `AFullSampleIsItsOwnRecordWhereverTheStepStarts`
15. `ASteerTargetGivesNoStepRecord`
16. `TheStepStartsAtTheTeleportPoseWhenTheSameIntentTeleports`
17. `AnEndPoseConvertsAsAPoseAndStaysAnEndPose`
18. `ASteerTargetConvertsAsAPoseAndStaysASteerTarget`
19. `AFullSampleConvertsByTheRuleOfAHandOff`, moved from `MotionSampleTableTests`
20. `AnIntentWithAMotionIntentIsNotEmpty`

Stage 4, the end pose and the steer target in the intent:

21. `MoveKinematicWritesAnEndPoseThatTheNextStepApplies`
22. `AnEndPoseConvertsWhenItsBodyHandsOff`, in `WorldIntentFrameTests`, with today's bound of 1e-6 m
23. `ASteerTargetCarriesADynamicBodyToItsPoseOverTheStep`, which replaces `KinematicTargetDrivesDynamicBody`
24. `ASteerTargetConvertsWhenItsBodyHandsOff`

Stage 5, the full sample in the intent:

25. `AFullSampleWrittenInATickMovesItsFrameInTheNextStep`, with a hull and without a body
26. `TheLastMotionIntentOfATickWinsWhateverItsCase`
27. `WithoutAPhysicsBackendAFrameWithoutABodyFollowsItsMotionIntents`
28. `AMotionIntentRelativeToADyingFrameConvertsIntoItsEntitysNewFrame`
29. `AHullThatIsNotKinematicInTheNextStepDropsTheMotionIntentOfTheTick`
30. `EveryKinematicBodyEndsItsStepExactlyOnItsEndPose`, for a plain body and a hull
31. `AFollowingFrameLandsOnItsAnchorsPlanAtEverySyncPoint`
32. `ANewFollowingFrameStartsAtItsAnchorsPlaceAndVelocity`
33. `AStateDumpTellsHowEachFrameFollowedItsOwner`, with all four kinds, and `static` for a write that wins over a motion intent
34. `ASpinMotionStartsWhereTheLastSampleOfTheComponentBeforeItLeftTheFrame`, through `PoseAtNextStepStartIn`
66. `ACharacterAtRestBesideANewKeplerHullTakesTheWalkingFormInTheFirstPlannedStepOfTheHullsOrbitFrameOnly`, in `CharacterFormNumericFrameTests`, with a first motion intent of the hull and without: the walking form in the first planned step only, where the gravity in the frame's system is half the field (the decision "The first step of a new following frame"). It takes the number after 65, so that the numbers of the other stages stay.

Stage 6, one guard for every entity:

35. `ADynamicBodyRefusesAnEndPoseAndAFullSample`
36. `AStaticBodyRefusesEveryMotionIntent`
37. `ACharacterRefusesEveryMotionIntent`
38. `AnAttachedEntityRefusesEveryMotionIntent`
39. `ASteerTargetNeedsADynamicBody`, for a kinematic body, an entity without a body and the owner of a root frame
40. `AHullThatTurnsKinematicInTheTickDropsItsSteerTarget`
41. `AKinematicHullTakesAnEndPoseAndItsFrameMovesAtTheChord`, which replaces `MoveKinematicOnAKinematicHullThrows`, and the frame feels the gravity at its place reversed
42. `TheOwnerOfARootFrameTakesAnEndPoseAndAFullSample`
43. `ARailsComponentWritesNothingAndLogsTheGuardsReasonOnce`
44. `APlainKinematicBodyOnKeplerRailsStandsOnItsOrbitToTheLastBit`
45. `APlainBodyOnRailsReadsTheSamplesInstantVelocity`
46. `APlainBodyOnRailsThatHandsOffCarriesTheSamplesInstantVelocity`
47. `AKinematicBodyHoldsStillInTheStepAfterItsLastMotionIntent`
48. `AKinematicBodyRefusesSetVelocityAndSetAngularVelocity`
49. `SpawnRefusesAVelocityOnAKinematicBodyThatOwnsNoFrame`

Stage 7, entities without body or frame:

50. `AnEntityWithoutBodyOrFrameFollowsItsMotionIntent`
51. `AnEntityWithoutBodyOrFrameHoldsStillInTheStepAfterItsLastMotionIntent`
52. `ATransformWriteWinsForOneTickOverAMotionIntent`, with a frame and without
53. `ATransformWriteLeavesAnEntityWithoutABodyAtRest`
54. `AKeplerMotionMovesAnEntityWithoutBodyOrFrameOnItsOrbit`
55. `ASpinMotionTurnsAnEntityWithoutAFrameInPlace`
56. `AnAttachedChildFollowsARootThatMotionIntentsMove`
57. `MotionIntentsGiveTheSameStateWithOneAndEightWorkers`
58. A benchmark in `WorldTickBenchmarks`: 1,000 entities without body or frame on Kepler orbits, and `KeplerFrameTickBenchmarks` before and after, with the tick and the sync point. It also measures the swap at 100,000 entities, which tells whether `PhysicsIntent.Move` needs a sparse store (see the decision "Where the motion intent lives").

What changes: `MotionSampleTableTests` and `MotionSampleBufferTests` go or become `MotionIntentTests`. The proper acceleration changes these tests: `RailsComponentTests`, `FrameSpinTests` and `KeplerOrbitTests` lose it from the sample and from `Sample`. `RailsSpawn` and `SpinFrameTests` lose the property of `SpinMotion`, and the tests in `SpinFrameTests` that keep a frame's proper acceleration expect the rule's value. The park test of `MotionChangeTests` expects the gravity at the ship's place, reversed, in place of the thrust. `WorldWakeTests` no longer writes a proper acceleration. `IntentTests`, `GravityTests`, `PhysicsFrameStateTests` and `RailsFollowTests` read physics' value from `FrameStepRecord.ProperAcceleration`. A test whose still frame has a field that copies its parent's gravity drops that field. `FrameTreeFollowedTests` tests the stored follow kind. `FrameTreeOrbitFrameTests` gives the tree intents. `RailsHullTests`, `IntentTests`, `SetMotionTests` (the flags in its `InlineData`), `PhysicsIntentTests` line 212, `WorldIntentFrameTests`, `StationSliceTests` and `DeepYardSliceTests` follow the new names. `FaultInjectingBackend` records motion intents (`SampleRecord` becomes a record of the entity, its frame and its motion intent), and `StepRecords` follows. About 10 tests keep a system stepping or move a platform with a kinematic spawn velocity. They use `KinematicDrift`, a test component in `Tertius.Testing` that writes, in every tick, the end pose one step further along a constant velocity. `RailsSpawn.OnKeplerOrbit` and `RailsSpawn.Spinning` take a parameter `firstMove`, which gives a spawn its first motion intent (see stage 2 of the order of the work). Every implementation brief asks for hand mutations.

### Order of the work

Each stage ends green.

1. The proper acceleration of a frame. `MotionSample` loses `ProperAcceleration`, `FrameSpin.Sample` its parameter, and `SpinMotion` its property. `FrameTree.ProperAccelerationFrom` holds the rule, and `Follow` and `HoldStill` call it. `FrameStepRecord` carries physics' value for a dynamic hull. `FrameTree.ProperAcceleration` becomes a value read. The sample table still exists, so until stage 5 the record of the anchor of a following frame in its semantic parent is its applied sample, which the stage reads in place of the step plan. Tests 1 to 12, without 7 and 12. This stage was built before the user answered the two choices of the rule. It reads the value that the parent took earlier in the same pass, which stage 2 changes, and it gives a new frame zero until its first step 2, as the user then chose. Because a new station holds still for its first step, it spawns the bodies of seven Physics tests two ticks after their station, and `StationSliceTests.TheScriptThrowsTheCrateAfterThePodsHaveLeftTheStationsFrame` fails until stage 2.
2. The user's answers on proper acceleration, and the first motion intent of a spawn. It comes before the union of stage 3, because it makes the suite green again.
   - `FollowOwners` reads each parent's value as stored before step 2, in two passes: `Follow` and `HoldStill` add each new value to `newProperAccelerations`, and `FrameTree.StoreNewProperAccelerations` writes the values after the loop (see "Which proper acceleration of the parent"). Tests 7 and 12, by the decided rules.
   - `SpawnDesc.FirstMove`, its check in `PrepareSpawn`, its store at step 6 and at the call of `World.Spawn` (`EntityTable.SetFirstMove`), and the public `World.TimeAt` (see "The first motion intent of a spawn"). The property holds a `MotionIntent`, so this stage brings forward from stage 3 the union's type with its cases `EndPose` and `SteerTarget`, its three constructors, `HasValue`, `TryGetValue` and `ProblemFor`. Stage 3 adds the rest of the union.
   - Until stages 4 and 5 put the cases into `PhysicsIntent.Move`, `SetFirstMove` stores each case where its writer's case stands after the swap at that stage. A full sample goes into the applied buffer of the sample table, as the sample of `MoveOnRails` does. An end pose or a steer target goes into the front intent's `Target` with `IntentFlags.KinematicTarget`, as the target of `MoveKinematic` does, which physics aims a kinematic body at and steers a dynamic body to. Stages 4 and 5 move the store with the writers.
   - The check applies `ProblemFor` from this stage on. Until stage 6 gives every writer that rule, the check also refuses a case that its writer refuses at that stage, so that a first motion intent is never wider than an intent that the entity could write. So a full sample needs the owner of a frame, as `MoveOnRails` does, and an end pose needs a body that is no kinematic hull, as `MoveKinematic` does.
   - The Demo: `SunAndEarth` gives earth and earth-surface their first motion intents, and the station scene gives its station one. `StationSliceTests` passes unchanged.
   - `RailsSpawn.OnKeplerOrbit` and `RailsSpawn.Spinning` gain a parameter `firstMove`, false by default. True gives the description the sample of its first step as its first motion intent. Stage 1 spawned the bodies of seven Physics tests two ticks after their station only because the station held still for its first step: in `CharacterFormNumericFrameTests`, `HandOffTests`, `NumericFrameOrbitTests`, `NumericFrameStickOutGhostTests` and `RailsFollowTests`. This stage may restore those spawns, with the station's first motion intent, and the bounds that the later spawns changed.
   - Tests 7, 12 and 59 to 65.
3. The rest of the union `MotionIntent`, after the type, its cases, its constructors, `HasValue`, `TryGetValue` and `ProblemFor` of stage 2: `TryRecordFrom`, `Converted`, `MotionSample.Converted` and `DQuaternion.AngularVelocityTo`, in Core, with tests 13 to 20. The sample table calls `MotionSample.Converted`. No behavior changes.
4. `MoveKinematic` writes the end pose, and the new `self.Body.SteerTo` writes the steer target, into `PhysicsIntent.Move`. `Target` and `KinematicTarget` go. Physics' target branch and `ConvertIntent` read `Move`: physics aims a kinematic body at an end pose, and steers a dynamic body to a steer target. The tests that steer a dynamic body move to `SteerTo`. Until stage 6, the guards stay as they are, and physics steers a dynamic body that gets an end pose, as it does today. Tests 21 to 24.
5. `MoveOnRails` writes the full sample into `PhysicsIntent.Move`, and every reader of the sample table reads the intents. One physics branch aims and snaps every kinematic body. The stage also brings the step plan with its landing rule, and the proper acceleration of an anchor reads its step plan. The frame tree row, with the follow kind, becomes the record of the step that ended. It brings the swap without a backend, the exchange of the intent buffers, and `Self.PoseAtNextStepStartIn` for `SpinMotion`. The table goes. The guards keep today's scope. Tests 25 to 34. It is the largest stage. If one agent cannot finish it, two agents do it in a row. The first writes the full sample into both stores, and moves physics and the frame tree to the intent. The second agent moves the World's readers and removes the table. Both end green.
6. One guard for every entity: `World.MotionIntentProblem`, `Self.MoveKinematic`, `self.MotionIntentProblem`, the refusals of each case, and the step-4 drops by the guard's rule. A kinematic hull takes an end pose, and the owner of a root frame takes an end pose and a full sample. `SetVelocity` and `SetAngularVelocity` throw for every kinematic body. A spawn refuses a velocity on a kinematic body that owns no frame. The tests that drift a kinematic body with a velocity move to `KinematicDrift`. A plain body's record goes into its body state and its hand-off. The components use the guard's read. Tests 35 to 49.
7. Entities without body or frame: `FollowFramelessEntities`, the movers of the last step, the transform write at rest, and the components on an entity without a frame. Tests 50 to 58.
8. `ARCHITECTURE.md`, the row in `CLAUDE.md` and the ticket `ter-zg77`, as "What goes, and the docs to change" lists them. The orchestrator runs coverage on the changed lines, and a Stryker run on the changed lines of Core and Simulation.

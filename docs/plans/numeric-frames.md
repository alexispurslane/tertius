# Numeric frames

Ticket: `ter-s02r`. Its child tickets `ter-fa7j` and `ter-r90w` are the two bugs that this plan fixes.

## Goal

The engine makes frames by itself around groups of bodies that move together, and it removes them again. Each such frame has its own Jolt system, so Jolt sees only small positions and small velocities. Gameplay never sees these frames.

## Why

Near the ship in the frame earth, every body moves at about 7,672 m/s. Jolt keeps velocities and many of its internals in floats, even in double-precision mode.

- At 7,672 m/s, a float steps by 0.000488 m/s. Each tick of gravity adds 0.145 m/s to such a float, and the sum rounds. This rounding turns a person's thrust sideways (`ter-fa7j`). Five seconds of thrust brought the person back 1.7 m off the center of the hatch.
- The Jolt character sweeps its whole displacement of a tick, which is 128 m at that speed. It sweeps against other bodies at their positions at the start of the step. So a person stops behind any body that lies up to 128 m ahead along the orbit (`ter-r90w`).
- Jolt's own architecture doc names this problem for space games. Its fix is several physics systems, with the objects of each near its origin and at a low velocity.

Distance is not the problem inside a solar system. A double steps by 0.03 mm at 1 AU and by 1 mm at 30 AU.

## Terms

| Term | Meaning |
|---|---|
| Semantic frame | A frame that content or gameplay made, such as the sun, earth, the surface of a planet or the ship |
| Numeric frame | A frame that the engine makes and removes around a group of bodies |
| Member | A body whose Jolt system is the system of a numeric frame |
| Physics home | The frame whose Jolt system holds a body: a numeric frame, or the body's semantic frame |
| Local velocity | A body's velocity relative to its physics home, a float in Jolt |
| Frame velocity | A numeric frame's velocity relative to its semantic frame, a double in the frame tree |
| Read in a frame | A read of a position or velocity that names the frame in which the caller wants the answer |
| Drain | A move of the common part of the members' local velocities into the frame velocity, at a sync point |
| Effective gravity | The gravity that physics applies to a body: the field at the body's true position minus the acceleration of its physics home |

## Decisions

The user made the decisions marked (User).

| Question | Decision | Why |
|---|---|---|
| Where a numeric frame lives | It is a frame in the frame tree, of a numeric kind. (User) | Frames already have the pose tree, a Jolt system each, hand-offs and drivers. A second mechanism must copy all of them. |
| What gameplay sees | Nothing. No component, script, event, query, snapshot, state dump or saved state can tell that a numeric frame exists, apart from rounding. | The frames carry no meaning. They exist only for precision. |
| What makes a numeric frame | Speed or distance. (User) | Speed keeps Jolt's float velocities small. Distance keeps Jolt's float bounds tight, and through nesting it lets positions grow without bound. |
| Which speed gives a falling frame | A group with a body that thrusts gets one from 50 m/s, and a group of free bodies only from 1,000 m/s. (User, `ter-3xs6`) Every character thrusts. (User) Any other body thrusts after 0.5 s of ticks whose force and impulse intents ask for a minimum acceleration. The window for those ticks is 20 s. (User, for the rule that thrust means force intents of a given size within a given time. The measurements set the values.) | A body that thrusts drifts from its neighbors, by about 2 mm/s per minute of thrust at 50 m/s. Free bodies that move together keep their gaps and relative velocities nearly exact up to 1,000 m/s. The drift grows with the time that a thrust lasts, not with its size, so a single impulse never counts. See "Thresholds from measured precision". |
| How a numeric frame moves | Integrated free fall in doubles, in the semantic frame's field. (User) A frame made for distance alone rests in its parent. | It works in any field, and numeric frames can nest later. A drift from the true orbit is harmless, as the section on motion shows. A Kepler orbit needs new code that turns a state into orbit elements, and it blocks nesting. |
| Reads of positions and velocities | Every read names its frame. No read without a frame remains. (User) | A read can be about any entity, so it must say in which frame it wants the answer. No read can then be ambiguous, and what the row stores becomes an engine detail. A caller can name only semantic frames, so no read answers in a numeric frame. |
| Velocity storage | Jolt's float local velocity, plus the double frame velocity of the physics home. A read adds them in doubles. (User) | Jolt keeps its small float, and reads are exact. |
| Writes | Relative to `self.Frame`, as today. Physics converts a write at the moment that it applies it. (User) | An entity writes only itself, so its own frame is the natural frame for a write. |
| Directions | In `self.Frame`'s axes, as today: forces, impulses, torques, the walk velocity, contact normals, gravity and up. (User) | A direction depends only on axes, and numeric frames never rotate, so a direction cannot leak a numeric frame. |
| Rotation | None. A numeric frame keeps the axes of its semantic frame. | No Coriolis or centrifugal terms appear, and vectors need no rotation between the two frames. |
| Bubble | None. Membership is a list of bodies. | A numeric frame never sweeps up bodies as it moves, so it leaves no wake. |
| Owner | None. A numeric frame has no owner entity. | An owner entity shows in entity lists, broadcasts, dumps and handle numbers. |
| When a cluster drifts | The engine drains in place. It splits off only the members that leave the cluster. (User, for the split. The drain goes with it.) | A uniform shift of position and velocity keeps Jolt's contact cache and warm start. A move to another Jolt system loses both. |
| Which groups the merge test links to an anchor | Every group, also a group that falls. (User, `ter-dhkf`) | The ghost of an anchor in a falling frame is kinematic, and Jolt gives a kinematic body no linear cast, so a fast body passed through a thin wall (`ter-ykqs`). In the group, the anchor is a real body of the group's system. See "Anchors". |
| Which home a group with an anchor takes | The anchor's home. An anchor counts as infinitely heavy. (User, `ter-dhkf`) | A group of bodies keeps the frame that holds the most of its mass, and no force moves an anchor. |
| Where a fast or far kinematic body lives | In a numeric frame K of its own that follows its motion intents, a following frame, when they move it fast or far. The body is K's anchor and rests at K's origin. A hull's frame hangs there, and a plain body is a member of K's system there. (User, `ter-dhkf`, for a `Kepler` hull. Since `ter-6r8c` for every kinematic hull, and since `ter-zg77` for every kinematic body.) | An anchor's home must hold it at rest, as a falling frame holds a ship's hull. A kinematic body moves only by its motion intents and teleports, so no force moves it, and it is an anchor while it follows. |
| How a following frame moves | At each sync point its place in the semantic frame is the place of its anchor's step plan, and its velocity is the plan's chord over the step that ends then. During a step it falls as a falling frame falls, with the acceleration that lands it on the plan. Before `ter-xdt8`, its acceleration was the orbit's. (`ter-dhkf`) | The place follows the anchor's motion exactly, so the anchor's place does too, over any run. With the chord, the following frame moves as Jolt moves a body: its members move as they would in the semantic frame's own system. See "Following frames". |
| Which home a group that can touch a ghost takes | The home where the ghost lives. Everything that a group cannot move counts as infinitely heavy, ghosts included. (User, `ter-4psc`) | No body of the group can move a ghost. A ghost that comes at a group in the group's own falling frame is kinematic. Jolt gives it no linear cast, as with the ghost of an anchor (`ter-ykqs`). In the ghost's home, the group's fast body has its linear cast. See "Ghosts as infinitely heavy". |

## How a numeric frame moves

A numeric frame moves in one of two modes, and the speed of its members chooses the mode:

- A falling frame falls freely in the field of its semantic frame. Fast members get one. Without free fall, the 8.7 m/s² of Earth's gravity at the ship's orbit enters every member's float, and the drains never stop.
- A resting frame stays at one place in its tree parent. Slow members that are far from their semantic frame's origin get one. A slow body can rest on something, such as a crate on the ground, so a falling frame is wrong for it. The frame of a planet's surface can sit at the planet's center. A crate on the ground then lies one planet radius from its frame's origin. A falling frame there falls away from the ground at the surface gravity. Earth's radius, 6,371 km, is below the 8,389 km from which a slow group rests. So a crate on Earth's ground stays in its frame's own system.

A resting frame whose members become fast turns into a falling frame. Its first frame velocity is the common velocity of its members, as in a drain. A falling frame can also have slow members. If they are still far from the origin of the frame above it, the frame turns into a resting frame. Otherwise it goes away.

A falling frame moves as follows.

The engine steps the frame with the same scheme that Jolt uses for bodies, in doubles:

1. It takes the field of the semantic frame at the frame's origin, at the start of the step. This is the frame's acceleration for the step.
2. It adds the acceleration times the step to the frame velocity.
3. It adds the new frame velocity times the step to the frame's position.

Each member gets the effective gravity: the field at the member's true position minus the frame's acceleration from step 1. The engine computes both terms in doubles and subtracts them before it narrows the result to a float. Near the ship's orbit, this tidal gravity is about 0.00026 m/s² for 100 m of height difference.

A drift of the frame from the true orbit does not change the physics. A member feels the field at its true position, minus the acceleration that its frame really had. So the sum of the frame's motion and the member's local motion is the motion that the member has in the semantic frame. A drift only makes the local velocities grow slowly, until the next drain.

Without the tidal gravity, the clustering changes the physics. Two bodies 100 m apart near the ship's orbit move apart by tens of meters in ten minutes through tidal gravity.

### Following frames

Ticket: `ter-zg77`, which builds on the orbit frames of `ter-dhkf` and on `ter-6r8c` and `ter-xdt8` of [Rails components](rails-components.md). Before `ter-zg77` this section was "Orbit frames", and the mode was `NumericMode.Orbiting`, because only the hull of a `Kepler` frame had one.

A following frame (`NumericMode.Following`) is a numeric frame that holds one fast or far kinematic body at rest at its origin. That body is the frame's anchor. Only motion intents and teleports move a kinematic body, so no force moves the anchor, and the frame follows the anchor's motion intents. The rules of a free body give a kinematic body a following frame, because it never thrusts: from 1,000 m/s or 2^23 m, kept down to 400 m/s or 2^22 m (see "Anchors", rule 3). A slow kinematic body near its semantic frame's origin has no following frame.

Two kinds of entity can be the anchor. One rule serves both kinds:

| Kind of anchor | Where its body lives | What hangs at the origin |
|---|---|---|
| Hull: the kinematic body of a frame's owner | In the system of its frame's tree parent, the following frame | The owner's frame, with the anchor's rotation, as a ship's frame hangs under the numeric frame of its hull |
| Plain body: the kinematic body of an entity that owns no frame | In the following frame's system, as a member | The body itself, at the place zero of the system |

The frame tree stays the owner of numeric frames. It keeps one store for this rule: the anchor of each following frame, by entity (`FrameTree.anchor`, null for every other frame). The tree does not keep the anchor's own state. A hull's frame has its row in the tree, as every frame has. A plain body has its transform row in the World and its body in Jolt. The rule needs only the anchor's place, which is the frame's origin.

The frame never rotates. It lies in the anchor's semantic frame. A following frame moves as follows, for both kinds of anchor:

1. At the start of each step, `FrameTree.BeginNumericStep` derives the frame's step plan: the step record of the anchor's motion intent in the step, relative to the semantic frame. The record starts where the anchor stands at the start of the step, the frame's origin, with the rotation of a hull's frame, or for a plain body with the rotation of its motion intent's aim, as a paragraph below says (see `PhysicsIntent.StepRecord`). The frame takes as its step acceleration the acceleration that lands it on the plan: the plan's chord minus the frame's velocity, divided by the step (see "The step plan of a following frame" in [Rails components](rails-components.md)). For a Kepler orbit, it equals the orbit's acceleration at the origin to about 3e-10 m/s² in low orbit. An anchor without a motion intent gives no plan, and the frame falls as a falling frame.
2. In the step, the frame moves as a falling frame does: the velocity gains the acceleration times the step, then the place gains the velocity times the step. This ends the step on the plan to rounding. The bodies in the frame move with it. They feel the field minus the frame's acceleration, so in this scheme the frame's acceleration cancels out of their motion in the semantic frame. Only the place where the frame lands depends on it.
3. Physics aims the anchor at the frame's origin, and puts it exactly there after the Jolt update, because the frame lands on the plan, which ends at the same place (`JoltPhysicsBackend.KinematicTarget`, with `FrameTree.Anchors`). In the step of a teleport, the anchor does not start at the origin, so physics aims it at the plan's place, where the frame will be at the end of the step. It does not sweep back to the origin.
4. At step 2 of the sync point, before the rows of the members land, the frame takes the plan's place and the plan's chord as its velocity, to the last bit, when the anchor ended the step at the origin (`FrameTree.PlaceFollowingFrame`). The World finds that the anchor did so in the output of physics: in the record of a hull's frame, which lies at the origin, and for a plain body in its body state and its front intent, which says whether it teleported. Jolt's place of a plain body need not be zero to the last bit there, when its centre of mass lies off its origin. When the anchor teleported in the step, the frame jumps to the anchor's place after the rows have landed, with the plan's chord, or without a plan with the anchor's own record. The anchor goes back to the origin, and every other body and frame in the frame keeps its place and velocity in the semantic frame (`FrameTree.JumpFollowingFrame`, with a `FrameShift`).
5. The anchor's velocity relative to the frame is the plan's instant velocity minus its chord, so that a read of the anchor in the semantic frame gives the plan's instant velocity. For a hull the tree row of its frame keeps it. For a plain body its transform row keeps it, as physics reports it (see "Instant velocities and chords" in `JoltPhysicsBackend.Chords.cs`). For an end pose the two velocities are equal, so the anchor's velocity relative to the frame is zero.

The chord is the velocity that Jolt gives a body that moves along the plan. A crate in the frame feels the field minus the frame's acceleration, and its true motion is the frame's motion plus its own. With the chord, that sum is the motion that Jolt gives the crate in the semantic frame's own system, so the crate moves as it does without numeric frames. With the anchor's instant velocity instead, it would drift by half a step of gravity, 7 cm/s in low orbit.

The numeric update makes a following frame at the anchor's place, and its velocity is the chord of the anchor's motion over the step that ended: the anchor's instant velocity less half a step of its acceleration (`FrameTree.CreateFollowingFrame`, and "The first step of a new following frame" in [Rails components](rails-components.md)). The update reads both kinds from what the tree and the World keep:

- A hull: the place, the velocity and the rotation of its frame from the frame's tree row. Its acceleration is the gravity at its place plus its frame's proper acceleration.
- A plain body: its place and velocity in the semantic frame from its transform row, which the World gives the update (`NumericBody.Position` and `NumericBody.Velocity`). A plain body owns no frame, so the tree keeps no proper acceleration for it. Its frame starts with the chord of a free fall, as the frame of a hull that spawned at this sync point does. The local velocity of the row is a float, so the start is exact to that float's step, about 0.12 mm/s at 2 km/s.

The frame's first plan lands it. A plain kinematic body takes no spawn velocity (see "Which entity takes a spawn velocity" in [Rails components](rails-components.md)). So at the sync point of its spawn it is at rest, and it gets its following frame at the sync point after its first step. A kinematic hull keeps its spawn velocity as its frame's velocity, so it gets its following frame at the sync point of its spawn.

A following frame never drains, never moves its origin to its members, and never nests: its anchor gives it its place and velocity. Only the group of its anchor keeps it. A body in it adds no mass to it, so a group in it without the anchor finds a frame of its own, as a group does when another group keeps its frame.

The frame loses its anchor in these cases, and then it turns into a falling frame that falls on from its place, at its velocity. The bodies in it keep their motion, and the next numeric update takes it as any falling frame (`FrameTree.LeaveFollowingFrame`):

| The anchor | How the tree learns it |
|---|---|
| Moves out, because the update moves it to another home, for example when it slows down | The update moves the anchor, so it lets the frame go first (`FrameTree.MoveBody`), for both kinds |
| Hands off into a bubble in the step, which only a plain body does | The update of the same sync point finds, before its rules run, that no body of its input is the anchor in the frame |
| Dies, or loses its body | For a hull its frame dies, and the tree lets the following frame go when it removes the frame. For a plain body the update of the sync point finds it as above. |
| Is no longer kinematic in the next step, which only a hull can be | `FrameTree.PlanHullMotion`, at step 4 |

The anchor of a following frame counts as an anchor in the rules of the update while the frame holds it (`NumericFrameRules.IsAnchor`): it is infinitely heavy, a group that links it takes its home, the split clock of the frame does not hold it, and it gets no leave clock. A hull is always an anchor. A plain kinematic body is an anchor only while it lives in a following frame of its own. A slow one is infinitely heavy but no anchor, as before (see the decision "How a kinematic body weighs in its group" in "Anchors"). The update marks each body whose home is the following frame that it anchors (`NumericBody.AnchorsHome`). A kinematic body that follows and lives in another home, for example a plain body that sped up in the frame of another anchor, gets a following frame of its own.

A group that links the anchor and an anchor at rest, or the anchors of two following frames, takes one home, and that home keeps a ghost of each anchor that lives apart from it, in a following frame of its own (`FrameTree.AnchorGhosts`, and the decision "A group that links two anchors that move differently" in "Anchors"). The ghost of a hull has the hull's layer, and the ghost of a plain body has the body's own layer, so each ghost meets what its body meets.

The step plan of a plain anchor starts from the rotation of its motion intent's end pose, not from the body's own rotation, because the tree keeps no rotation of a plain body. So the plan of an end pose of a plain anchor turns with no angular velocity. No reader of a plan reads its angular velocity: the frame never turns, and only a hull's frame takes a proper acceleration from the plan, which reads the plan's place, chord, acceleration and end rotation.

A still kinematic body far from its semantic frame's origin, 2^23 m or more, has a following frame too, by the distance rule. Without a motion intent the frame has no plan, so it falls, and the body falls with it. A body in a gravity field without numeric frames would hold still. A hull has the same rule since `ter-6r8c` (test 7 of [Rails components](rails-components.md)). See "Limits".

## Velocity

| Velocity | Where it lives | Type |
|---|---|---|
| Local velocity | Jolt, and the entity's row | Float `Vector3`, small |
| Frame velocity | The frame tree | `Double3` |
| A velocity read in a frame | Computed at each read | `Double3` |

A sketch of the API (the names are not final):

```csharp
DPose pose = self.PoseIn(self.Frame);
Double3 velocity = self.VelocityIn(self.Frame);
bool found = self.Snapshot.TryGetVelocityIn(crate, self.Frame, out Double3 crateVelocity);
self.Body.SetVelocity(velocity + push);  // a Double3 relative to self.Frame
// A velocity read in the body's semantic frame:
//   FrameVelocity(row.PhysicsHome) + row.LocalVelocity
// Physics, when it applies the set velocity:
//   local = (intent.Velocity - FrameVelocity(home)).ToVector3();
```

A read in another frame converts through the frame tree in doubles, as `TryGetPoseIn` does today for poses. `FrameTree.ConvertVelocity` already does this for velocities, with the turn of each spinning frame on the path. A numeric frame on the path adds only its double position or velocity, because it has no rotation.

Physics converts a write at the moment that it applies the request. The setter does not convert it. A sync point lies between those two moments, and it can drain the frame or move the body to another numeric frame. So the request keeps the velocity relative to `self.Frame`, and physics subtracts the frame velocity of the body's home at that moment. Spawn velocities work the same way, because a spawn also lands at a sync point.

The API changes in these ways:

- The reads without a frame go away: `Self.Pose`, `Self.Velocity`, `Self.AngularVelocity`, `World.GetPose`, `World.GetVelocity`, `World.GetAngularVelocity`, `Snapshot.PoseOf`, `Snapshot.VelocityOf`, `Snapshot.AngularVelocityOf` and `Snapshot.TryGetPose`.
- Reads in a frame replace them: `PoseIn`, `VelocityIn` and `AngularVelocityIn` on `Self` and `World`, and `TryGetPoseIn`, `TryGetVelocityIn` and `TryGetAngularVelocityIn` on `Snapshot`. A velocity read returns a `Double3`. An angular velocity read returns a `Vector3`, because numeric frames do not rotate.
- `Self.Transform` is a writable reference, so it also reads without a frame. It becomes a setter relative to `self.Frame`.
- `Self.SetVelocity` and `SpawnDesc.LinearVelocity` take a `Double3`.

Every place in physics that narrows a double velocity to a float subtracts the frame velocity of the body's home first. Today these places are:

- the hand-off and the ghosts,
- the re-home on frame destroy,
- the hull velocity from the tree,
- the kinematic target,
- the gravity force.

## Positions and the entity row

Today, physics writes into each entity's row the frame whose Jolt system holds the body. That row frame drives `Self.Frame`, `FrameChanged`, the frame topics, `QueryRadius`, the render snapshot and the state dumps. So the row splits in two:

- The row frame stays the semantic frame: the nearest ancestor of the physics home that is not numeric.
- A new field holds the physics home.

The row's pose stays relative to the semantic frame. Because every read names a frame, this is an engine detail: it makes the most common read, in `self.Frame`, free. The sync point computes it in doubles from the home's position and the body's local pose. Because numeric frames do not rotate, this is one addition of positions.

The poses of intents, such as a kinematic target or a teleport, stay in the semantic frame's coordinates. Physics converts them at the moment that it applies them, as it does for velocities.

## Membership

### Who gets a numeric frame

A body gets a numeric frame with its group, the bodies that the merge test links to it (see "Merge before contact"). A body alone is a group of one. The group's common speed is the speed of its center of mass relative to its semantic frame. "Anchors" gives the common speed of a group with a static or kinematic body. A group gets a numeric frame for either of two reasons. No content flag is needed.

- Speed: the group's common speed passes a threshold. It gets a falling frame. A group with a body that thrusts gets one from 50 m/s. A group of free bodies only gets one from 1,000 m/s.
- Distance: the group's center lies 2^23 m (8,389 km) or more from its semantic frame's origin. It gets a resting frame. If it is also fast, it gets a falling frame.

A falling frame of free bodies stops falling below 400 m/s. A falling frame with a body that thrusts stops falling below 20 m/s. A resting frame goes away when the center of its slow members comes nearer than 2^22 m (4,194 km) to the origin. "Thresholds from measured precision" gives the reasons for these values.

A free body is a body that does not thrust: it coasts, collides, pushes other bodies or falls with them. A body thrusts in either of two cases:

- It is a character. Every character thrusts, always and at once, because the game can walk it on any tick.
- Its thrust clock says so. A thrust tick is a tick whose force and impulse intents ask the body for at least 1e-3/60 m/s², 1 mm/s per minute. The acceleration that they ask for is (|force| + |impulse| / step) / mass. The thrust clock adds up the time of the thrust ticks. From 0.5 s on, the body thrusts. After 20 s without a thrust tick, the clock starts again at zero. So a body keeps thrusting for 20 s after its last thrust tick.

So a single impulse, or a single tick of force, never counts. Gravity is no intent, and a torque does not move a body, so neither counts. A static or kinematic body never thrusts, because physics gives a force only to a dynamic body or a character. An anchor never makes its group thrust. A kinematic body, which its motion intents move, never thrusts, so it gets its following frame by the speeds of free bodies. The numeric update reads the intents that the next step applies, so a thrust counts at the sync point before it acts. `NumericFrameRules.ThrustClock` runs one clock over one tick, and `FrameTree` keeps a clock for each body that is not static.

Distance matters to Jolt even where doubles are fine, but it costs work, not precision. Jolt keeps each body's bounds as floats in its system's coordinates. At 6,800 km from the origin, those bounds step by 0.5 m, and at 1 AU by 16 km. Loose bounds make the broadphase test pairs that cannot touch. Jolt's docs put floats at their best within about 5 km of the origin. But Jolt's double-precision mode keeps positions in doubles, and the measurements found no error from distance up to 9,000 km. So loose bounds cost only broadphase work, and resting frames start at 2^23 m.

Inside the ship and in other small frames, nothing changes.

### Merge before contact

Bodies in two different Jolt systems cannot touch. So two numeric frames merge before any of their members can collide. The test looks ahead in time: it grows each member's bounds by its velocity relative to the other frame, times a lookahead time. Space Engineers grows its bounds by a distance in meters, but at orbital speed a fixed distance is crossed in a fraction of a second.

A numeric frame also takes in a body of its semantic frame by the same test. Examples are the ship's hull and a crate that a person throws out of the ship.

### Anchors

Ticket: `ter-dhkf`, which fixes `ter-ykqs`. This section is the binding design for anchors in the merge test.

An anchor is a static body, a hull that is not dynamic, the interior of a frame owner, or a kinematic body that lives in a following frame of its own (`NumericFrameRules.IsAnchor`). Before `ter-ru5u`, a hull that is not dynamic was the hull of a frame with the `Static`, `Kepler` or `Spin` driver. Before `ter-zg77`, only a hull could live in a following frame. Physics keeps the interior (`BodyDesc.InteriorShape`, or the hull's shape when there is none) as a static body in the frame's own system, at its origin, so the cabin walls of every ship and station are an anchor of that system. No force moves an anchor. Before `ter-dhkf`, the merge test linked an anchor only to a group that does not fall. A falling group stayed apart from the anchor, and its frame kept a kinematic ghost of the anchor. That ghost comes at the members at the frame's speed. Jolt gives a kinematic body no linear cast: a cast follows only the motion of the dynamic body that it belongs to. So a crate at 60 m/s in a falling frame passed through a wall thinner than its step of 1 m at 60 Hz. Without numeric frames, the crate's linear cast stops it at the wall (`ter-ykqs`). At that time, every group fell from 50 m/s. Since `ter-3xs6`, a free crate falls only from 1,000 m/s (see "Who gets a numeric frame").

The user decided three rules. They extend the merge rules above literally.

1. The merge test includes anchors for every group, also for a group that falls. (User)
2. An anchor counts as infinitely heavy when the group chooses its home. A group that links an anchor takes the anchor's home, as a group of bodies keeps the frame that holds the most of its mass. (User)
3. A moving anchor needs a home in which it is at rest. A kinematic body that its motion intents move fast or far in its semantic frame becomes the anchor of a numeric frame K of its own that follows those motion intents, a following frame, and rests at K's origin. A hull is a member of K, and its frame hangs under K, as the frame of a ship with a dynamic hull hangs under the numeric frame of its hull (see "Frame owners in a numeric frame"). A plain kinematic body is a member of K. The frame tree's invariant that only a frame with a dynamic hull can hang under a numeric frame changes for this. (User, for the hull of a `Kepler` frame in `ter-dhkf`. Every kinematic hull since `ter-6r8c`, and every kinematic body since `ter-zg77`.) The body gets K by the speed and distance rules of a group of free bodies, because its motion intents are no thrust. So it gets K from 1,000 m/s or 2^23 m. It keeps K while it moves at 400 m/s or more, or lies 2^22 m or more from the origin. A slow kinematic hull near its semantic frame's origin stays in that frame's own system, as a static hull does, and a slow plain kinematic body near the origin is a body as any other, infinitely heavy in its group but no anchor. See "Following frames".

With these rules the anchor is a real body in the group's Jolt system, so Jolt sweeps the fast body against it. The user's precision case still holds: crates that leave a `Kepler` station through its airlock join the hull's frame K, where both are slow, so float velocities stay small. So do crates near a fast plain kinematic body.

The open details have these decisions:

| Question | Decision | Why |
|---|---|---|
| The home that a static body gives its group | Its semantic frame's own system, or its resting frame when it lies far out. The group gets the mode that its static bodies get alone: velocity zero, the center of mass of the static bodies, and the numeric frame that holds one of them as the frame that the group keeps, or none. | A static body takes no velocity, so its group never falls. The static bodies alone decide where the group lives, so a group that passes cannot pull a wall out of its own system. A wall 7 km from the origin, in the semantic frame's own system, stays there. With the group's own center and the mode of the group's falling frame, the group would rest there and take the wall into its frame. |
| The home that a `Static` or `Spin` hull gives its group | Its semantic frame's own system, where the hull's center is at rest. The group gets the mode None. | The hull's frame hangs in its semantic parent, so the hull lives in the semantic parent's own system. A `Spin` hull turns in place, and its center does not move. |
| The home that the interior of a frame owner gives its group | The frame's own system. The numeric update has a body for the interior beside the one for the hull: a static body at the frame's origin, with the bounding radius of the interior's shape, that no update moves (`NumericBody.Fixed`). The group gets the mode None, as with the hull of a `Static` frame. | Physics creates the interior in the frame's own system and has no move for it, so the interior can only be a real body there. A crate thrown at 1,000 m/s at a cabin wall stays in the frame's own system while the merge test reaches the interior's bounding sphere. There, Jolt sweeps it against the wall. Before, the update had no body for the interior, so the crate fell in a numeric frame of its own, whose system holds no copy of the interior, and passed through every cabin wall. A fast body inside a frame therefore falls in a numeric frame only when it is beyond the merge test's reach of the interior, for example in a frame whose bubble is far larger than its interior. |
| How a kinematic body weighs in its group | A kinematic body that does not live in a following frame of its own counts as infinitely heavy too, but it is no anchor: the velocity and the center of a group with a kinematic body and no static body are those of its kinematic bodies alone. So a group can fall with its kinematic bodies, and a group with a still kinematic body does not fall. A static body still outweighs a kinematic one. The frame that the group keeps is chosen by the masses as before, and the drain gives it the group's velocity. | No contact changes the velocity of a kinematic body, and Jolt treats it as infinitely heavy in a contact, so a shut door stops a crate as a static wall does. When the group weighed a door of 1 kg by its mass, a crate of 25 kg at 60 m/s and the still door moved at 57.7 m/s and fell in a numeric frame, where the door came at the crate at 57.7 m/s. Jolt gives a kinematic body no linear cast, so the door passed through the crate. A kinematic body can move between frames, unlike a static one, so it does not fix where the group lives. |
| The home that a kinematic body that follows gives its group | The following frame K of rule 3, when the body's own motion gives it one. A group without an anchor at rest takes K and the mode `Following`, and the update makes K when the body anchors none (`NumericBody.AnchorsHome` says whether it anchors its home). A kinematic hull without K is fixed in the semantic frame's own system. A plain kinematic body without K weighs in its group as the row above says. | The body moves fast in its semantic frame's own system, so a group that joined it there would be fast too. In K, the body rests. |
| How a group lets go of an anchor | By the split rules, when the merge test no longer links them. Two bodies of one system stay linked up to the split margin farther. The split cooldown holds only the members of a numeric frame that are not anchors, as before. So a group that falls leaves the semantic frame's own system at once. It leaves a resting frame at once too, unless that frame also holds other bodies that are not anchors, such as a crate on the wall: the cooldown then holds it with them for 1 s. | The semantic frame's own system lets a group that falls leave at once, because the speed thresholds give falling its own hysteresis. The cooldown of a numeric frame stays as it is, because it stops the members of one frame from splitting and merging at each sync point. The split margin stops a group at the edge of the reach from going in and out at each sync point. |
| A group that links two anchors whose homes differ, and both homes are at rest in the semantic frame | The group takes one home for all its anchors, and a static body moves into it, as before `ter-dhkf` for an anchor that a slow group links. A `Static` or `Spin` hull wins: the group goes to the semantic frame's own system. Else the resting frame that holds a static body of the group wins, the first in entity order on a tie, and the mode of the static bodies decides whether the group stays in it or goes to the semantic frame's own system. | Each home at rest in the semantic frame can hold a static body, so no anchor needs a ghost, and each one is a real body for the group. The choice reads only the anchors and their homes, which the moves of the group do not change. After the move the anchors share one home, so the next sync point makes the same choice. |
| A group that links two anchors that move differently: the anchor of a following frame K and an anchor at rest, or the anchors of two frames K | The group takes one home, and each other anchor keeps a ghost in it, as before `ter-dhkf`. The home of the anchors at rest wins over K. Among frames K, the K of the first anchor in entity order wins. An anchor that follows in a frame K of another anchor gets a K of its own. | No home can hold both anchors at rest. When the anchors at rest win, the group's fast bodies are real bodies with linear casts against them, and the ghost of the anchor moves with the bodies that move with it. The choice reads only which anchors the group links, so it does not flip at each sync point. A kinematic body has no linear cast of its own, so its ghost loses none. |
| How the infinite mass is computed | No sum adds or divides an infinite mass. A numeric frame that holds a static body of the group gets the mass +∞, which only comparisons read: the choice of the frame that the group keeps, and of the group that keeps a frame. A group with a static body outweighs every group without one in that frame, and between two such groups the first in entity order keeps it. The velocity and the center of a group with a static body are those of its static bodies alone, and those of a group with a kinematic body and no static one are those of its kinematic bodies alone. | Infinity times a velocity, divided by infinity, is NaN. Those velocity and center are the limits of the mass-weighted means as the mass of the static or kinematic bodies grows without bound. The sums ∞ + m and ∞ + ∞ are exact, and so are comparisons with ∞. |

The work had two steps, both built in `ter-dhkf`, for the hull of a `Kepler` frame. `ter-zg77` extended step 2 to every kinematic body (see "Following frames"):

1. Rules 1 and 2 for static bodies and for the hulls of `Static` and `Spin` frames, and the rule for a group that links anchors whose homes are at rest in the semantic frame. `NumericFrameRules.Summarize` gives a group with a static body the mode, center and frame of its static bodies.
2. Rule 3. A kinematic body that follows gets its following frame K (see "Following frames"), the tree's invariant changes, and a group that links anchors that move differently keeps a ghost of the other anchor. `FrameTree.UpdateSemanticFrame` links every pair of bodies in one pass. `NumericFrameRules.FollowsInFrame` says which kinematic bodies live in a following frame, `NumericFrameRules.Summarize` gives a group its anchor (`NumericGroup.Anchor`), and the update marks each such body that its group does not follow: that body stays in its own K, which the update makes when the body anchors none, and the group's home keeps a ghost of it (`FrameTree.AnchorGhosts`).

An encounter of a fast group with an anchor runs at the encounter speed in the anchor's home. The doc already accepts this for two bodies that pass each other (see "Limits").

What the two steps changed outside the tree: the World tells the update which bodies are kinematic in the next step (`NumericBody.Follows`, which was `NumericBody.Orbits` and named the hulls with the `Kepler` driver before `ter-ru5u`), and the tree fills in the mode of each body's home (`NumericBody.HomeMode`) and whether the body anchors it (`NumericBody.AnchorsHome`). Physics needed no new path for an anchor that a group shares, because a fast body that joins the anchor's home is a body there like any other, with its linear cast. A move keeps the moved body's ghosts in the systems of other frames (see "Moving bodies between Jolt systems"). A hull meets only static bodies and other hulls in its own system (`Layers`), so a fast body that joins the system of a hull passes it, as it does without numeric frames. Only a hull bumps a hull outside a bubble: a cargo pod, a frame with the Physics driver, that drifts out of a `Kepler` station bumps the station's hull in K, at 1 m/s. The old ghost of a `Static` or `Spin` hull in the body's falling frame met moving bodies, so it stopped the body there. So the ghost that rule 3 keeps for a hull in the home of a group has the hull's own layer, and it meets there only what the hull meets: static bodies and other hulls, and a kinematic ghost meets no static body (`GhostBody.Role`, the `FrameHull` role). The ghost of a plain anchor has the body's own layer and collision groups, so it meets what the body meets. Physics predicts the record of a hull's frame from the anchor's motion intent, at the origin of its following frame, and it reports the sleeping members of a following frame, which move with the frame, as it reports those of a falling frame. Physics aims every anchor at the origin of its following frame (`FrameTree.Anchors`).

### Split with hysteresis

Two conditions split a member, or a group of members that touch or are joined, off into a new numeric frame. First, it is farther from the other members than the merge test reaches, plus a margin. Second, this stays true for a cooldown time. The margin and the cooldown stop merge and split thrashing, a problem that Kitten Space Agency reported with a plain sphere test.

The semantic frame's own system gets the same cooldown, with a clock for each body instead of each frame. A body that the system keeps although it needs a frame by itself, for example a slow body far out next to a hull that stays in the system, leaves for a new resting frame only after it has been apart for the cooldown. A merge into a numeric frame never waits, and a group that starts to fall leaves at once, because the speed thresholds already give falling its own hysteresis. A new body that nothing keeps in the system gets its frame at once.

### Drain

At each sync point, the engine looks at the members' local velocities. If their common part, weighted by mass, passes a limit, the engine adds it to the frame velocity and subtracts it from each member. Jolt has no call that shifts a whole system, so the drain sets each body's velocity, one body at a time.

A sleeping body has a local velocity of exactly zero, and a nonzero velocity wakes it. So sleeping members count with a velocity of zero in the common part, and the drain runs rarely. If the members drift far from the frame's origin, a drain also moves the origin to their center of mass.

### Collect

A numeric frame with no members goes away. A numeric frame can also become slow relative to its semantic frame. If its speed falls below a second threshold, its members go back to the semantic frame's own system. The frame then goes away.

### Nesting for vast distances

The distance rule also applies to numeric frames. A numeric frame that is farther than a threshold from its tree parent's origin gets a new resting frame above it, near it. So no frame's position in its parent grows large, and every double stays fine-grained.

A resting frame that nests sits on a coarse grid of a power of two meters, for example 2^20 m, which is about 1,049 km. Every multiple of 2^20 m up to about a million light-years is exact in a double. The frame below can drift far from it. The resting frame then jumps by whole grid steps, and the frame below shifts back by the same exact amount.

A read between two nearby entities composes through their lowest common ancestor, as `PoseTree.Relative` does today. So no large number enters the read. Only a read in a distant frame, such as a star's, sums large numbers, and a double then rounds that read at that distance. The row's pose in the semantic frame rounds far from that frame's origin, so the row also keeps the exact position in the physics home, and a read in another frame starts from it. The render snapshot places everything relative to the camera's physics home. `ComputeRelativeTo` composes from that reference down the tree, so frames near it stay exact. Only other trees go through the shared root.

Frames near each other share one nesting frame: a far frame joins the nearest nesting frame within half the nesting distance. A nesting frame jumps only when all of its frames are then within half that distance of it. Otherwise the far frame moves to another nesting frame, or to a new one.

At 1 light-year a double steps by 2 m. A frame there that moves tick by tick in its parent rounds by up to 1 m per tick. With nesting, it moves within a frame whose origin is near it.

## Thresholds from measured precision

Ticket `ter-kugq` measured how the precision of physics degrades with speed and with distance from the origin. Ticket `ter-3xs6` set the thresholds from those measurements. `NumericFrameOptions` holds the values, and its doc comments repeat the reasons.

| Value | Setting | From |
|---|---|---|
| Speed to get a falling frame, for a group with a body that thrusts | 50 m/s | The user |
| Speed to give it up, for a group with a body that thrusts | 20 m/s | The start value |
| Speed to get a falling frame, for a group of free bodies only | 1,000 m/s | The measurements |
| Speed to give it up, for a group of free bodies only | 400 m/s | 0.4 of the speed to get one |
| Acceleration that makes a tick a thrust tick | 1e-3/60 m/s² (1 mm/s per minute) | The measurements |
| Time of thrust ticks after which a body thrusts | 0.5 s | The measurements |
| Thrust window: the time without a thrust tick after which the thrust time starts again at zero | 20 s | The measurements |
| Distance from the semantic frame's origin to get a resting frame | 2^23 m (8,389 km) | The measurements |
| Distance from the semantic frame's origin below which a slow group gives it up | 2^22 m (4,194 km) | Half the distance to get one |
| Distance from the tree parent's origin to nest a numeric frame | 10^9 m | The start value |
| Grid of a resting frame that nests | 2^20 m | The start value |
| Lookahead time of the merge test | 2 s | The start value |
| Split margin beyond the merge test | 10 m | The start value |
| Split cooldown | 1 s | The start value |
| Common local speed that starts a drain | 1 m/s | The start value |
| Distance of the members' center of mass from the frame's origin that moves the origin | 1 km | The start value |

The measurements did not test the start values. They are the first values of this design.

### The measurements

The measurements are explicit tests in `tests/Tertius.Physics.Tests/Precision`. They run the real World on the real Jolt backend, on one thread, with numeric frames off and Jolt's speed limit raised to 10^6 m/s. This command runs them:

```
nix develop --command bash -c 'dotnet test --project tests/Tertius.Physics.Tests -c Release -- --filter-namespace Tertius.Physics.Tests.Precision --explicit only'
```

The drift measurements write CSV files to the folder that `TERTIUS_PRECISION_OUT` names. The contact measurements use `TERTIUS_PRECISION_DIR`. Three drift scenes set the thresholds:

| Scene | What it measures |
|---|---|
| Thrust drift | A character of 70 kg in zero gravity, pushed along +X by 105 N on every tick, so 1.5 m/s². The error is against its exact path. |
| Relative drift | Two free boxes of 10 kg, 10 m apart, that move apart at 0.1 m/s in zero gravity. The error is in their gap and their relative velocity. |
| Ballistic drift | A box of 10 kg that falls at 9.81 m/s². The error is against its exact path. |

The common speed is the speed that every body of a scene shares on top of its own motion. Each scene runs at 16 common speeds from 0 to 300,000 m/s, in two directions. In the easy direction, the common velocity is at right angles to the axis where the velocity changes. In the hard direction, it points along the diagonal (1, 1, 1)/√3, so each axis carries the common speed divided by √3. The runs sample at 5, 20 and 60 s, at 60 Hz.

A distance control runs the same scene at a common speed of 0, as far from the origin as the speed run went. That is up to 9×10^6 m. The speed-added error is the error of a speed run minus the error of its distance control, as vectors.

In the easy direction, the common speed added no velocity error, in any scene and at any speed. The two tables give the hard direction.

Thrust drift, the speed-added velocity error of the character in mm/s:

| Common speed (m/s) | After 5 s | After 20 s | After 60 s |
|---|---|---|---|
| 10 | 0.091 | 0.19 | 0.35 |
| 50 | 0.20 | 1.9 | 2.1 |
| 100 | 0.43 | 2.1 | 3.7 |
| 200 | 0.44 | 3.2 | 21 |
| 500 | 1.8 | 7.0 | 25 |
| 1,000 | 7.3 | 30 | 85 |
| 2,000 | 7.3 | 30 | 85 |
| 7,672 | 29 | 117 | 355 |

The thrust takes the character's own speed up to 90 m/s. At a common speed of 0, that own speed gives an error of 0.019, 0.32 and 3.2 mm/s, which the table leaves out. At 1,000 m/s the drift grows linearly in time, by about 1.4 mm/s per second of thrust. At 50 m/s it does not, because the roundings partly cancel there.

Relative drift of two free boxes, speed-added:

| Common speed (m/s) | Relative velocity error (mm/s) | Gap error after 60 s (mm) |
|---|---|---|
| 50 | 0.00038 | 0.0063 |
| 200 | 0.0015 | 0.44 |
| 500 | 0.0061 | 0.44 |
| 1,000 | 0.024 | 1.3 |
| 2,000 | 0.024 | 5.6 |
| 7,672 | 0.098 | 12.5 |

The relative velocity error does not grow with time: 5, 20 and 60 s give the same value. The gap error grows linearly. At 1,000 m/s it is 0.11, 0.43 and 1.3 mm.

The distance controls found no error from distance. Thrust drift and ballistic drift did not change at any distance up to 9×10^6 m. Relative drift changed once: from 1.5×10^6 m, its gap error grew by 0.42 µm, in the hard direction only. The contact measurements reached up to 6×10^5 m and changed by at most 18 nm.

### The float model

The measurement report explains the drift with a float model of the engine's update. The model has three rules. It gives the thrust errors of the CSV files to four digits. In the rules, V is the common speed, a is a body's acceleration, and dt is the tick length, 1/60 s.

- Rule A, the position step. In each tick, a body moves by the float `float(V·dt)`, and Jolt adds that step to a double position. After n ticks along an axis that carries V, the position error is `n·(float(V·dt) − V·dt)`. Bodies with the same velocity get the same rounding. So rule A moves a whole group and leaves its gaps alone.
- Rule B, the velocity sum. The velocity is a float. In each tick, Jolt adds `float(a·dt)` to it, and the sum rounds to the float spacing of the velocity along that axis. So the spacing of the velocity component that carries V sets the error of each change of velocity, from thrust, gravity or a contact. This error grows with the number of ticks. When every member of a group gets the same change, as in a falling group, all get the same rounding. Then the group moves as one piece.
- Rule C, the start rounding. The start velocity narrows to a float once. This rounding is the whole relative velocity error of the two free boxes, and it does not grow.

Rule B gives the drift of a body whose velocity changes on every tick:

```
velocity error after k ticks ≈ k · s(V∥) / c
s(x) = 2^(floor(log2 x) − 23)
```

where

- `V∥` = the part of the common velocity along the axis where the velocity changes, in m/s.
- `s(x)` = the float spacing at x, the gap between x and the next float, in m/s. For example, s is 6.1e-5 m/s at 577 m/s.
- `k` = the number of ticks with that change of velocity, 60 per second.
- `c` = how much the roundings cancel. At c = 2, every tick rounds by half a spacing in the same direction. The thrusting character measured about 2.5, and boxes that push each other about 6.

Three facts follow from the model:

- The error depends on the part of V along the axis where the velocity changes, not on the length of V. So the easy direction gets no speed-added velocity error.
- The error moves in steps, because the spacing doubles at each power of two. The thrust drift is 85 mm/s at both 1,000 and 2,000 m/s.
- A tick rounds by at most half a spacing, whatever the size of the change. So the drift grows with the time that a change of velocity lasts, and not with its size. The ceiling is `k·s(V∥)/2`. The measured drift was about 60 % of the ceiling at 50 m/s and 80 % at 1,000 m/s.

The thrust scene used one acceleration, 1.5 m/s². So the claim that the size of a thrust does not set the drift comes from the model, not from a measurement.

### Why each threshold has its value

Speeds of a group of free bodies, 1,000 and 400 m/s:

- A group of free bodies coasts, collides, pushes its members or falls together. Its members share each change of velocity, so rules A and B move the whole group and leave its gaps alone.
- Only rule C, and rule A between members of slightly different velocity, open its gaps. At 1,000 m/s, two free boxes keep their relative velocity to 0.024 mm/s, and their gap drifts by 1.3 mm per minute.
- 1,000 m/s lies just below 1,024 m/s, a power of two. Below it, every axis component of a velocity has a spacing of at most 6.1e-5 m/s, whatever its direction. So one bound of the float model, a spacing of 6.1e-5 m/s, covers the whole range below the threshold. Above 1,024 m/s, the spacing doubles.
- The common path of the group still drifts from its exact path, as one piece. In the ballistic scene at 500 m/s, the box ends 0.33 m and 23 mm/s off its path after a minute of fall.
- The speed to give up a falling frame is 400 m/s. This is 0.4 of the speed to get one, the same ratio as 20 to 50 m/s.
- One measurement does not fit this picture. Three boxes stacked on a kinematic platform stood at 200 m/s and fell apart from 500 m/s. No speed between was tested, and the cause is unknown. The user decided that a moving platform that carries things is always a semantic frame (`ter-jwzd`). Such a stack then rests in the platform's own system.

Speeds of a group with a body that thrusts, 50 and 20 m/s:

- A body that thrusts changes its velocity on every tick, and its neighbors do not. So rule B moves it relative to them.
- At 50 m/s, the character drifted by 2.1 mm/s in a minute of thrust. At 1,000 m/s, it drifted by 85 mm/s. A neighbor that does not thrust has only the error of rule C, so the drift relative to that neighbor is about the same.
- The design's target was 1 mm/s over a minute of thrust (see "Tests", `ter-fa7j`). The user accepted about 2 mm/s per minute at 50 m/s and kept 50 m/s. (User) The speed to give up the frame stays 20 m/s.

Characters:

- Every character thrusts, always and at once. The game can walk a character on any tick, and a walk is a thrust. (User)
- The sweep measurement also favors the lower speed for characters. The sweep of a character hits a crate that moves with it whenever their gap is at most V·dt. That is 0.83 m at 50 m/s and 17 m at 1,000 m/s.

The thrust clock of any other body:

- The minimum acceleration is 1e-3/60 m/s², 1 mm/s per minute. Rule B can lose a change of less than half a spacing entirely. A thrust below the minimum changes the velocity by less than 1 mm/s in a minute. So even a total loss stays under the target of 1 mm/s per minute.
- The body thrusts after 0.5 s of thrust ticks. Just below 1,000 m/s, the model's ceiling is 1.8 mm/s per second of thrust: 60 ticks times half a spacing of 6.1e-5 m/s. The measurement gave 1.4 mm/s per second. So the drift before the clock counts the body stays under 1 mm/s, at most 0.92 mm/s.
- The thrust window is 20 s. Thrust ticks less than 20 s apart add up, and a body keeps thrusting for 20 s after its last thrust tick. So a body that thrusts in short pulses gets at most 0.5 s of uncounted thrust in each 20.5 s.
- At the measured 1.4 mm/s per second, that uncounted thrust drifts by about 2 mm/s per minute. This is the rate that the user accepted at 50 m/s. At the model's ceiling, it drifts by 2.7 mm/s per minute.
- A single impulse never counts, because one tick is 1/60 s, far below 0.5 s. It needs no count. Rule B rounds one change once, by at most half a spacing: 0.031 mm/s just below 1,000 m/s. The drift grows with the time that a thrust lasts and not with its size, so the clock counts only time.

Distances, 2^23 m and 2^22 m:

- The distance controls found no error from distance up to 9×10^6 m, the largest distance that the runs reached. 2^23 m, about 8,389 km, is the largest power of two below it.
- Below 2^23 m, a float steps by at most 0.5 m, so Jolt's float bounds stay within 0.5 m. Loose bounds cost only broadphase work (see "Who gets a numeric frame").
- So a crate on Earth's ground, 6,371 km from the origin of a surface frame at Earth's center, stays in that frame's own system. A slow group gets a resting frame only from 2^23 m.
- The distance to give up a resting frame is half, 2^22 m, about 4,194 km. That is the same ratio as the start values of 10 and 5 km.
- The distances do not reach nesting. A numeric frame nests only beyond 10^9 m from its tree parent's origin, about 119 times the distance to get a resting frame.

## Moving bodies between Jolt systems

A move to another system loses Jolt's contact cache, warm start and constraint impulses, and Jolt recreates a character. So:

- The engine moves a whole group of members that touch or are joined together, never half of it.
- It adds sleeping bodies without waking them.
- It carries the held contact pairs of the group, so a contact that goes on is not reported again.
- No move between numeric frames, or between a numeric frame and its semantic frame, sends `FrameChanged` or changes a topic membership.
- A moved body keeps its ghosts in the systems of other frames. Every frame where a ghost can be depends only on the body's semantic frame, which a move does not change, and the ghost's velocity in its system does not change either. The ghost pass of the next step puts each ghost at the body's new state. Before `ter-dhkf`, a moved body lost every ghost for one step. Since rule 1 of "Anchors", a fast ship joins the system of a parked station's hull while their bubbles can overlap, and it then had no ghost in the station for that step.
- A moved hull also takes along the ghost of each body that sticks out of its frame (`JoltPhysicsBackend.MoveGhostsThatStickOut`). That ghost lives in the frame's tree parent, the hull's home, so it is the one ghost whose frame a move changes: the move puts the frame under the hull's new home. The ghost goes from the old home into the new one at once, with its place and velocity in the semantic frame, as the hull's own state does. Only the ghosts of the bodies of that frame, or of numeric frames below it, move. The ghost of a crate that sticks out of a parked station stays in space when a ship's hull leaves space. Before the review of `ter-4psc`, the reconcile dropped such a ghost, and the ghost pass made the new one only at the end of the next step. A rock that met the ghost in that step flew on into the place of the crate. A ship that starts to fall then let a rock that came at its crate head on at 60 m/s pass through the crate, while without numeric frames the ghost stopped it.

## Frame owners in a numeric frame

The ship's frame follows its hull, and the hull lives in the system of the ship frame's tree parent. When the hull joins a numeric frame, the ship frame moves under that numeric frame in the tree. Its semantic parent stays earth. A person who leaves the ship's bubble hands off to the ship frame's tree parent, which is then the numeric frame. The person's row frame becomes earth, as it does today. Rule 3 of "Anchors" adds a second kind of frame under a numeric frame: the frame of a kinematic hull that follows hangs at the origin of the hull's following frame K (see "Following frames"). A body that leaves the frame's bubble hands off to K, as it hands off to a ship's numeric frame. A frame with a static hull, or with a kinematic hull that does not follow, still hangs in its semantic parent, and so does a frame without a hull. A plain kinematic body that follows is the other kind of anchor, a member of K's system with no frame of its own, so no frame hangs in K for it. When a hull's motion changes so that the hull no longer follows, its following frame lets it go at step 4 of the sync point (`FrameTree.PlanHullMotion`), and the numeric update moves the hull and its frame into the semantic parent's own system. Before `ter-ru5u`, `World.SetFrameDriver` ran the numeric update when the game gave a frame a driver. When a frame dies, each frame in it moves to the dying frame's semantic parent, whatever its hull's motion (`FrameTree.Destroy`). The World moves the bodies of the dying frame into the own system of a semantic frame, the hull of each such frame among them, so each frame hangs where its hull lives. The next numeric update moves the hull, with its frame, into a numeric frame when its group needs one. So only the update puts a frame in a numeric frame, and only for a hull in its input. A frame without a hull never hangs in a numeric frame, and the tree's invariants say so. A station in its following frame K that dies leaves K falling, and a room in the station, a frame without a body, which no numeric update would move, holds still in the station's semantic parent where it was, as without numeric frames. So does a room without a body in a ship that dies while its dynamic hull falls in a numeric frame. Before the review of `ter-4psc`, a frame that physics drove went to the dying frame's tree parent. Such a room then fell on in the numeric frame, and the update took the places of the ghosts of its bodies to be fixed in the semantic parent's own system, while physics kept those ghosts in the numeric frame.

Every frame carries its children through the tree, so no force has to carry them. The ship's contents live in the ship's own Jolt system, at small positions relative to the ship. They move with the ship by composition, as they do today. When the ship frame moves under a falling numeric frame, the ship and its contents move with that numeric frame in the same way. Only the ship's proper acceleration, such as its thrust, acts on its contents, as one uniform force. Friction and inertia never have to drag cargo along, so they cannot make it jitter. The rules can also make a numeric frame inside the ship. That frame moves with the ship for the same reason.

## Bubbles and ghosts

An anchor is a static body, a hull that is not dynamic, such as a parked station's hull, the interior of a frame owner, or a kinematic body in a following frame of its own (see "Anchors"). An anchor at rest never moves between frames, and the anchor of a following frame moves only with that frame. A ghost is a kinematic copy of a body in the Jolt system of another frame, so that the bodies there collide with it, or a static copy of a static body that holds still there (see below). A numeric frame has no bubble, so it does not change which bubbles a body, a hull or an anchor meets. Every one of them gets the same hand-offs and the same ghosts in semantic frames as in the same scene without numeric frames. (User) Physics finds the bubbles through the numeric frames in these places:

- A body in the system of a frame meets the bubbles of the semantic children of that frame's semantic frame, wherever they hang among its numeric frames. It enters the deepest one that it touches, or it has a ghost in each one that it touches without entering, for example a hull that does not fit. A person in a falling frame enters a parked station, and a fast ship's hull has a ghost in a station that is too small for it.
- An anchor never enters a bubble, but a ghost is about touching, not entering, so the hull of a frame that physics does not drive meets the same bubbles. A static body that is not a hull has a ghost in each child bubble whose content it reaches (`ter-imi5`, `docs/plans/complete-ghosts.md`). Numeric frames do not change that. A parked station's hull lies in the system of the station's semantic parent. A ship that falls past it hangs under a falling frame until the merge test links the two hulls, and then under the station's semantic parent for the encounter (see "Anchors"). Either way the station's hull has a ghost in the ship while their bubbles overlap. So the crates deep inside the ship hit the station's hull.
- A body that sticks out of its frame's bubble has a ghost in the frame's tree parent, which can be a numeric frame. That tree parent is the home of the frame's hull. The body is partly in that parent, so it also meets the bubbles that the members of the parent's system meet: the semantic siblings of its frame, wherever they hang. A crate that sticks out of a falling ship pushes a crate in a parked station beside it, and a crate that sticks out of the station stops a crate in the ship.
- The bubble that a body sticks out of and leaves is that of the semantic frame of its system's frame. A numeric frame has no bubble, so a body in a numeric frame sticks out of and leaves the bubble of the numeric frame's semantic frame, as a body in that frame's own system does. It has the same ghosts in that frame's tree parent and siblings, and it leaves into the same tree parent (`ter-kz89`). A crate thrown at 1,000 m/s inside a ship, beyond the merge test's reach of the interior, falls in a numeric frame below the ship (see "Anchors"). When it sticks out of the ship's bubble, it has a ghost in the ship's parent and pushes a crate in a parked station beside the ship. When it is fully outside the bubble plus the exit margin, it moves into the ship's parent, and it hears `FrameChanged`, at the same step, place and velocity as without numeric frames. Without this rule the crate never left the ship.
- No other system of the semantic parent needs a ghost of a body that sticks out. The numeric update links each body of the semantic parent that can touch the ghost before the next update to the frame's hull. So that body lives in the hull's home, with the ghost (see "Ghosts as infinitely heavy"). A rock at 60 m/s in a falling frame of space joins the hull of a parked station in the system of space. There it hits a crate that sticks out of the station. A crate that sticks out of a falling ship pushes a rock that rests in space in its path. The rock joins the ship's falling frame for the push. Both happen as without numeric frames. The one exception is a frame whose kinematic hull lives apart from its group, in a following frame of its own. Before `ter-4psc`, the merge test took no ghosts as inputs, so the body that sticks out needed more ghosts. When the tree parent was numeric, it had a ghost in the semantic parent's own system (`ter-wllb`). It also had a ghost in each numeric frame that the update named for the bubble (`ter-2gg0`).

The update tests the bodies against the bubbles at one moment, the end of the tick. The bodies' rows hold the end of the step from step 2 of the sync point on, and so does every frame, which takes its step record at step 2 (see "One motion record per frame" in [Rails components](rails-components.md)). From `ter-dhkf` to `ter-6r8c`, the sync point moved the frames that physics did not drive, Kepler and Spin frames and the static frames that gameplay moved, to the end of the tick before the update, at step 6b. Before `ter-dhkf`, it moved them after the update. A station that orbits at 7.5 km/s was then 126 m behind the rock that co-orbits beside it. So the update of that time never named the rock's falling frame, and the rock passed through a crate that sticks out of the station.

The place and velocity of each bubble's parent relative to the body's system are sums through the numeric frames, in doubles. So are those of the body's system relative to the bubble that the body sticks out of. So are those of an anchor's following frame relative to each home that keeps a ghost of the anchor. The hand-off uses instant velocities, the velocities at the end of the step. The ghosts use chord velocities, the change of position over the last step divided by its length. Jolt's velocity of a body is its chord, and so is the velocity that the World reports, so a ghost moves as its real body does. In free fall the two kinds of velocity differ by half a step of gravity. The home of a group also keeps a ghost of each anchor of a following frame that the group links but cannot share a home with: a hull or a plain kinematic body in a following frame of its own, when the group lives in the home of an anchor at rest or in the following frame of an earlier anchor (see "Anchors", "Following frames", and the "Anchors" row in `ARCHITECTURE.md`, "Numeric frames"). For a hull, that home also keeps a ghost of each body that sticks out of the hull's frame (see "Ghosts as infinitely heavy"). Every other anchor shares the group's home instead.

The reconcile allows a ghost of a body in exactly these frames, so it drops none of the ghosts that the ghost pass makes:

- The tree parent of the bubble that the body sticks out of. This is the home of that bubble's hull.
- Each semantic sibling or child whose bubble the body touches.
- Each home that keeps a ghost of the body as an anchor.
- Each home that keeps a ghost of the hull of the bubble that the body sticks out of.

Apart from the tree parent, none of these frames is numeric unless it keeps a ghost of an anchor. No frame gets two ghosts of one body. The homes that keep a ghost of an anchor are frames of the anchor's own semantic frame, which no other ghost of it enters. The homes that keep a ghost of a hull are frames of the semantic parent other than the bubble's tree parent. No bubble that a body touches is numeric or the semantic parent, so none of them is the tree parent. The tree keeps the homes of the ghosts of anchors coherent with the frames that they name. A frame that leaves the tree takes them with it, and so does a following frame that loses its anchor. When an update stops naming such a home, physics drops the ghosts there at the same sync point. It does so also when the tree did not change (see "Ghosts as infinitely heavy"). So physics keeps a ghost only where the tree allows one, between every two ticks.

A ghost is kinematic, so only a body that moves, or a sensor, collides with it. Jolt makes no contact between a kinematic body and a static one unless one of them is a sensor or a body asks for it, and the engine never asks. So a static rock beside a ship never meets the ghost of a crate that sticks out of the ship, with numeric frames or without. A contact with a ghost is a contact of the two entities (`ter-ejx9`, see `ARCHITECTURE.md`, "Contacts"). So a cargo pod in the home of a static rock that the ghost of a kinematic hull hits hears a `Collision` with the hull. The ghost appears only at the end of the first step in the frame, and the move's contacts wait for it. A ghost stands for its real body, so the ghost of an anchor meets only what the anchor meets: a kinematic sensor that falls past a static rock detects neither the rock nor its ghost, although that ghost is kinematic, the ghost of a kinematic hull has the hull's layer, and the ghost of a plain kinematic anchor has the body's own layer and collision groups. When the numeric update stops naming a home for the ghost of an anchor, physics drops the ghost there at the same sync point. It does so whether or not the tree changed (see "Ghosts as infinitely heavy"). A body that rests on a static floor and starts to fall stays linked to the floor, by the merge test with the split margin, until it is well clear of it, so it leaves no contact behind.

A hand-off happens in the step, but the update of the last sync point chose the numeric frames for the places before the step. So after a hand-off, a contact can be in no system for one step, when a body of it or a ghost of one is in a numeric frame. Without numeric frames, the semantic parent's own system takes it over. This happens in two ways (`JoltPhysicsBackend.ResolveLooseContacts`):

- A crate enters a frame while it touches a rock of the semantic parent. Among the frames of the semantic parent, its ghost is only in the frame's tree parent, the home of the frame's hull, which can be a numeric frame. The update linked the rock to the crate, not to the hull, so the rock can live in another frame of the semantic parent: a numeric frame, or the semantic parent's own system while the ghost is in a numeric frame. A crate that rests against a rock in space and enters a ship that falls in its own numeric frame has its ghost in that frame, and the rock stays in space until the next update.
- A crate that leaves a frame whose kinematic hull lives apart from its group leaves into the hull's following frame. It drops its ghost in the home of the group, where the bodies that the merge test linked to the hull live.

The next sync point's numeric update links the touching bodies, or names the home of their group for the ghost of the hull. So the rule has two conditions. A hand-off of the step moved one body of a contact, and one body of the contact, or a ghost of one, is in a numeric frame. If both hold and no system takes the contact over, the contact waits one step with its count. Gameplay then hears the same events as without numeric frames. A contact that really ended at the hand-off ends one step late (reviews of `ter-ejx9` and `ter-2gg0`). Without numeric frames no body and no ghost is in a numeric frame, so no contact waits. Before the review of `ter-4psc`, the rule asked for a body in a numeric frame, so the crate that entered the falling ship lost its contact with the rock in space: gameplay heard an end, and a second start one step later. Before `ter-4psc`, a crate that left a ship in a numeric frame, or a parked station, lost contacts this way too: the bodies that touched its ghost lived in other systems than the hull's home. The merge test now links those bodies to the hull, so they share its home.

The ghost of an anchor moves in its falling frame, so it must be a body that Jolt moves with that velocity, also when the anchor is a mesh (`ter-dq92`). Jolt moves a kinematic mesh against dynamic bodies, given a mass, which physics provides because Jolt cannot compute one for a mesh. Before, the ghost of a mesh was always static, and the ghost pass moved it after every step, about 34 cm into a crate at 20 m/s. Jolt saw it at rest, so it neither stopped the crate, which passed into the wall, nor bounced a ball off it, while both happen without numeric frames. Now every ghost is kinematic, except the ghost of a static body while it holds still in its system, which is static, as its real body is (since ter-imi5; before, only the ghost of a mesh): Jolt never simulates it, while it searches the contacts of an awake kinematic body in every step, also with the bodies asleep on it (37 µs against 116 µs a tick with 100 boxes asleep on the ghost of a parked station's floor, `MeshGhostBenchmarks`). The ghost pass turns such a ghost kinematic when it starts to move and static when it holds still, and a drain that sets a static ghost moving turns it kinematic at once, so it moves in the next step. A crate in a falling frame that reaches a mesh wall within Jolt's speculative contact distance at the start of a step, or that sinks less than its own half into the wall, stops at the wall, and a ball bounces off it, with the same contact events as without numeric frames. Since `ter-dhkf`, a static mesh joins the group of a fast body that the merge test links to it (see "Anchors"). So the ghost of an anchor is a mesh only when the anchor of a following frame that lives apart from its group is a mesh. The rules of this paragraph still hold for it, and for the ghost of a mesh hull in a bubble that it touches.

A kinematic body has no linear cast in Jolt: a cast follows only the motion of the dynamic body that it belongs to. Without numeric frames, a fast crate that flies at a static wall has a linear cast, which stops it at the wall. In its falling frame the crate is slow, and the ghost of the wall comes at it fast, so no cast runs. When that ghost comes from beyond the speculative contact distance and moves farther in one step than the crate's half or the wall's thickness, it can pass through the crate. This held for the ghost of every anchor, a mesh or not. Since `ter-dhkf`, it holds only for the ghosts that "Limits" names. The same holds for a kinematic member of a falling group that its frame's velocity moves fast, such as a shut door in the group of a fast crate. Since `ter-dhkf`, a kinematic body counts as infinitely heavy in its group (see "Anchors"), so the group moves with its kinematic bodies, and they are slow in its frame.

## Ghosts as infinitely heavy

Ticket: `ter-4psc`, which fixes `ter-tenw`, item 2 of `ter-me1y`, and item 1 of `ter-me1y` for the ghosts of the bodies that stick out of a moved hull's frame (see "Moving bodies between Jolt systems"). Item 1 still holds for a home that an update names for the first time to keep a ghost of an anchor: the ghost there, and the ghosts beside it, appear at the end of the next step. This section is the binding design for ghosts in the merge test.

The rule extends rule 2 of "Anchors": everything that a group cannot move counts as infinitely heavy, ghosts included. (User) No body of a group can move a ghost, so a group that can touch a ghost takes the home where the ghost lives. The ghost is then in the group's own system, and no numeric frame needs a ghost of its own for the group. The one exception is a hull that lives apart from its group (see "A hull that lives apart").

A body sticks out of a bubble while its bounding sphere crosses the bubble's boundary. Before `ter-4psc`, a numeric frame kept a ghost of each body that sticks out of a semantic child near its members (`ter-2gg0`). That ghost came at the members at the frame's speed. It was kinematic, so Jolt gave it no linear cast. So a member thinner than one step of the ghost's motion did not stop it. The ghost of an anchor had the same flaw (`ter-ykqs`). Also, a ghost in a child's own system never reached the child's numeric frames. So a fast crate in a numeric frame inside the child never met it (`ter-tenw`).

### The places of ghosts

The update of a semantic frame P takes the places of ghosts as inputs (`FrameTree.CollectGhostPlaces`). A place of a ghost (`GhostPlace`) is where physics can keep a ghost in a frame of P before the next update. A place has five parts:

- Its home, the frame whose system holds the ghost.
- The position of the ghost's center in that home.
- The ghost's velocity relative to P, in P's axes.
- The radius of the bounding sphere of the body behind the ghost.
- Its target: the hull among P's bodies whose home holds the ghost. A place that is fixed in P's own system has no target.

A place is not a body. It never moves, keeps no frame, and gets no move or clock. Two kinds of place exist.

A. A body of a semantic child C of P that can stick out of C's bubble before the next update (`NumericFrameRules.MayStickOut`). The body can move, and its bounding sphere reaches outside the bubble. The test grows the sphere by how far the body can move before the next update: its speed relative to C times the lookahead time, and at least the split margin of 10 m (see "Hysteresis"). A static body never sticks out. The hull of a frame in C that a `Kepler` or `Spin` driver moves never leaves C's bubble. So such a hull sticks out by as far as it lies. Physics keeps the ghost of the body in C's tree parent, the home of C's hull. So the place's home is C's tree parent, and its target is C's hull. The place's position and velocity go through C's pose, velocity and spin (`FrameTree.AddStickOutPlaces`). A child without a hull always hangs in P itself (see "Frame owners in a numeric frame"), so its places are fixed in P's own system.

B. A body of P's semantic parent that can reach P before the next update (`NumericFrameRules.MayTouchBubble`). Also a body of a semantic sibling of P that can stick out of its own bubble and reach P. Physics keeps the ghost of such a body in P's own system while it reaches P without being in it. A body reaches its bounding radius, and a hull reaches at least the longest radius of its own bubble. The test grows that reach by how far the body can move, as for kind A, with its speed relative to P. P's own hull lies inside P's bubble, so it leaves no place. Since `ter-imi5`, a static body that is not a hull leaves a place too, because it has a ghost in each child bubble whose content it reaches. A static body, a kinematic body or a hull reaches P's content: P's bubble grown by how far a body of P can reach beyond it before the next update (`NumericFrameRules.ContentExcess`). A dynamic body or a character reaches only the bubble (`NumericBody.GhostsReachContent`, fork F2 of `docs/plans/complete-ghosts.md`). The interior of the owner of P's semantic parent leaves no place, because it has no ghost in a child frame yet. A place of this kind is fixed in P's own system (`FrameTree.AddParentPlaces`, `FrameTree.AddSiblingPlaces`).

The lookahead time is 2 s, as in the merge test of two bodies.

The update links a body of P to a place by the merge test of two bodies (`NumericFrameRules.MayTouch`, `FrameTree.LinkGhostPlaces`). The test measures from the body's place in its home to the place of the ghost. It goes through the lowest common ancestor of the two homes. The reach is the two radii plus their relative speed times the lookahead time. A body in the place's home stays linked up to the split margin farther. These links come after the links of the bodies. They come before the split cooldown holds the members of each numeric frame together.

- A body that the test links to a place of kind A joins the group of C's hull.
- A body that the test links to a fixed place fixes its group, as a fixed hull does (`NumericBody.MayTouchFixedGhost`). The group stays in P's own system.

Only a body that meets the ghost is linked to its place (`NumericFrameRules.MeetsGhost`). The rule mirrors the two tests that physics runs on each pair of bodies:

- The table of layers (`Layers`). Physics gives a child ghost of a static body or of a hull the static role. It gives every other ghost behind a place the ghost role. The mask of a frame hull names only the static and hull roles, so a hull meets only a ghost of the first kind (`GhostPlace.MeetsHulls`, `ter-imi5`).
- The motions. The ghost of a body that can move is kinematic. Jolt makes a contact between a kinematic body and another body only when the other one is dynamic or either one is a sensor, and the engine never asks Jolt for other contacts. The ghost of a static body, such as a parked station's hull, meets only what the static body meets: a dynamic body, and a kinematic one when the static body is a sensor (`FrameWorld.GhostContactMotion`).

So a place carries whether its body is static and whether it is a sensor. A character counts as dynamic. The collision groups and masks of the two bodies do not count, as in the merge test of two bodies. A link to a body that never meets the ghost only changes the home of its group, for no contact. Before the review of `ter-4psc`, the rule linked a dynamic hull too. A ship whose hull passed 35 m from a crate that sticks out of a parked station then joined the station's system for about 93 m of its path, for a contact that cannot happen.

### Why a link to the hull is enough

Physics keeps the ghost of a body that sticks out of C in C's tree parent, and C's hull lives there too. The update can move the hull with its group, for example into the frame of a heavier body of the group. Wherever the update moves the hull, C's frame follows it, and the ghost lives in the hull's new home. A body that a place links to the hull is in the hull's group. So it lives in the same system as the ghost, and no other frame needs a ghost of the body that sticks out. The group takes the home that the hull gives it (see "Anchors"):

- P's own system, for the hull of a parked station: the hull of a `Static` or `Spin` frame, or of a slow `Kepler` frame near P's origin.
- The ship's falling or resting frame, for a ship whose hull lives in a numeric frame.
- The hull's following frame, for a kinematic hull that its motion intents move fast or far.

A fast rock comes at a crate that sticks out of a parked station. It joins the station's hull in P's own system for the encounter. There the rock is fast, and Jolt sweeps its linear cast against the slow ghost of the crate. The old precision returns for the rock during the encounter, as for an anchor (see "Limits").

### A hull that lives apart

One case needs more than the link. A group can link a kinematic hull in its following frame and also an anchor at rest. It can also link the anchors of two following frames (see "Anchors", the row about anchors that move differently). The group then lives in one home, and each other anchor stays in its own following frame. The group's home keeps a ghost of each such hull (`FrameTree.AnchorGhosts`). The frame C of such a hull hangs in its following frame. So the ghost of a body that sticks out of C lives in the following frame too. The bodies that the group links to the hull live elsewhere, in the group's home.

So physics gives each body that sticks out of C a ghost in each home that `AnchorGhosts` names for C's hull (`JoltPhysicsBackend.WantGhostsBesideHull`). The ghost goes where the ghost in the following frame will be at the end of the step. Physics places it relative to the home as it places the ghost of the hull. It has the ghost layer, so the bodies there that move meet it. Physics makes these ghosts whenever the tree names a ghost of C's hull. It makes them also for a group that links the hull through a body and not through a place. The reconcile allows these ghosts. The sync point that stops naming the home drops them with the ghost of the hull (see "How physics drops a withdrawn ghost").

A pod flies beside a far, slow `Kepler` station and shares the resting frame of a static rock. The station's hull lives apart in its following frame. In the rock's resting frame, the pod meets the ghost of a crate that sticks out of the station. Without numeric frames, it meets the crate's ghost in the system of space.

### What went

- `StickOutGhost` and `FrameTree.StickOutGhosts`, with the passes of the update that chose them. The pass of physics that followed them went too. They named the numeric frames of P that keep a ghost of a body that sticks out of a child (`ter-2gg0`).
- The sphere test of those passes: `StickOutRadius`, `StickOutReach` and `MayTouchStickOut`. Its first test in P's coordinates, whose reach grew by `PlaceSlack`, went with it. The place of each body replaces the sphere, and the benchmark numbers of that first test no longer apply. The review of `ter-4psc` gave the scan for places a first test of its own (see "Cost and order").
- The second ghost of a body that sticks out of a frame whose tree parent is numeric (`ter-wllb`). That ghost was in the semantic parent's own system. The merge test now links each body there that can touch the ghost to the frame's hull.
- `PhysicsChanges.GhostPlacesWithdrawn`, and `FrameTree.WithdrawGhostPlaces`, which filled it.

These ghosts stay:

- The ghost in the bubble's tree parent.
- The ghosts in siblings and children.
- The ghosts of a `Kepler` hull that lives apart from its group, and the ghosts beside such a hull.

The contact wait rule stays, for the two cases in "Bubbles and ghosts".

### How physics drops a withdrawn ghost

The numeric update names the homes that keep a ghost of an anchor. An update can stop naming a home without a change of the tree's version, so the reconcile does not see it. Physics keeps a list of the pairs of `AnchorGhosts` that its ghost pass followed. Each time physics applies a batch of changes, it compares that list with the pairs that the tree names now (`JoltPhysicsBackend.DropWithdrawnAnchorGhosts`). It does this after the moves and the reconcile of the batch. For each followed pair that the tree no longer names, it scans the system of the pair's home. It removes each ghost there that the tree no longer allows: the ghost of the hull and the ghosts beside it. A pair that the update names for the first time has no ghost yet. The next ghost pass makes it. Usually the next update names the same pairs in the same order. Then the drop costs one comparison of the two lists.

The World skips a batch with no change. But a sync point whose only change is a withdrawn home still needs a batch. So the World hands physics the last batch of every numeric update, also when it is empty (`World.UpdateNumericFrames`). An empty batch costs about 85 ns and allocates nothing, against 31 µs for a tick of a ship with 100 boxes asleep on its floor. Before the review of `ter-4psc`, the World sent the empty batch only after an update that named a home for an anchor's ghost. That put a rule of physics in the World, which read `FrameTree.AnchorGhosts` to predict when physics needs a batch. The batch also reconciles, so physics follows the tree after every sync point: a hull whose hand-off the sync point refused is back in the system of its frame's parent before the next tick, not only at the start of the next step.

The ghost of a body that sticks out of a frame needs none of this. Its home is the bubble's tree parent, which changes only when the frame's hull moves, and the move takes the ghost along (see "Moving bodies between Jolt systems"). A ship's hull that moves into a falling frame takes its crate's ghost from the system of space into the falling frame at that sync point, and a hull that moves back takes it back. A change of the tree that is no move of a numeric update, such as a frame that the game reparents, makes the reconcile drop the ghost, and the ghost pass makes the new one at the end of the next step, with numeric frames as without.

### Hysteresis

The stick-out test of `ter-2gg0` had no split margin. A member at the edge of its reach was able to make a ghost appear and vanish at each update. This is item 2 of `ter-me1y`. Two rules now hold the places steady.

The test of whether a place exists grows each body by how far it can move before the next update: its speed times the lookahead time, and at least the split margin of 10 m (`NumericFrameRules.MayStickOut`, `NumericFrameRules.MayTouchBubble`). So for a body slower than 5 m/s the answer does not depend on its speed. A crate that rests 1 mm inside its bubble gives a place while it is awake at 1 mm/s and while it sleeps. Without the floor it gave a place only while it was awake, and a fast rock in reach moved between the semantic frame's own system and a falling frame at each change (review of `ter-4psc`). A margin added to the motion only moves that edge 10 m inward: a crate that rests 10.001 m inside then gives a place only while it is awake.

The test of a link to a place has the split margin of the merge test. A body in the place's home stays linked up to 10 m beyond the reach. Beyond that, the split cooldown of 1 s holds the body, as it holds any body that leaves its group (see "Split with hysteresis"). A body in another system than the place gets no margin, so it comes back only within the reach. For example, a slow body far out stays in P's own system 7 m from a crate that sticks out of a parked station. After 1 s at 13 m, it leaves into a resting frame of its own. Back at 7 m, it stays in that frame.

### Cost and order

The update compares each body of P that meets a ghost with each place of P. It composes poses through the tree only for a body and a place in different homes. It collects the places from the bodies of P's semantic parent, children and siblings in its input. It does this before any frame of P shifts and any body of P moves. Each place comes from the tree and from the positions and velocities of the bodies in their semantic frames. No place comes from the homes of another semantic frame. The update of that frame can change those homes before the update of P. The places follow the fixed order of the update, so two runs with the same input make the same frames and moves. A warm update with places allocates nothing.

For each semantic frame whose update runs, the update scans every body of the frame's semantic parent and of each semantic sibling. A first test on distances and speeds alone skips each body of the parent, and each whole sibling, that cannot reach the bubble before the next update (`NumericFrameRules.MayReachBubble`). A bubble lies within its longest radius of its center, so a body farther than that radius plus its reach plus how far it can move never touches it. The speed in that test is a bound on the speed relative to P. For a body of the parent it is the body's speed relative to P plus the speed that P's spin gives a point at the body. For a sibling it is the speed of its fastest body, plus the speed that the sibling's spin gives its farthest body, plus the sibling's speed relative to P, plus the speed that P's spin gives a point at the sibling's center. A body beyond the sibling's center gets more speed from P's spin, but it is that much farther from the bubble too, so the bound needs no more. The update keeps the bounds of each semantic frame's bodies from the first sibling that needs them. The first test grows its reach by 1e-12 of the distances from the semantic frame's origin, for the rounding far out, so it drops nothing that the full test keeps.

`NumericFramePlaceBenchmarks` runs ten parked ships whose updates run, among 1,000 or 10,000 slow rocks in their semantic parent. An update took 182 µs and 1,354 µs before the first test, and 95 µs and 635 µs with it. Without the ships' updates it takes 29 µs and 288 µs, so the work of the ships' updates went from 1,066 µs to 347 µs at 10,000 rocks.

## Which gravity each reader uses

| Reader | Gravity |
|---|---|
| The force on a body | Effective gravity, which is tidal in a numeric frame |
| Whether physics stands a character | Effective gravity. In free fall it is below the zero-gravity threshold, so a character floats, as in orbit. Since ticket `ter-1w06` such a character is a rigid body that cannot turn, so it pushes the bodies of its numeric frame as any rigid body does. |
| A character's up while it touches something | The semantic frame's field at the character's true position |
| The ambient light's up | The row frame's field, which is semantic, as today |
| The sleep threshold of a system | Effective gravity, as today. A numeric frame sits at the floor of 0.001 m/s². |

## What gameplay never sees

The public surface reports only semantic frames. The work covers each of these surfaces:

- `Self.Frame`, `World.GetFrame` and `Snapshot.FrameOf` return the row frame, which is semantic.
- `FrameChanged` goes out only for a change of the semantic frame.
- Frame topics and `QueryRadius` use the row frame.
- `World.Frames` and `Snapshot.Frames` report the semantic tree. A numeric frame is invisible to `Parent`, `GetChildren` and `GetAll`.
- Contact points and normals reach gameplay in the semantic frame's coordinates.
- State dumps, scripts and the render snapshot use semantic frames. The camera's frame is semantic, so render interpolation and the shadow grid never restart because of a numeric frame.
- The frame outlines of the render snapshot (`World.ShowFrameOutlines`, docs/plans/blue-danube-demo.md, "Change 3: frame outlines") show numeric frames too: a numeric frame's outline is its contents sphere. That is so that a capture can show the frames that the engine makes. Gameplay cannot read the render snapshot, so no component can tell from it that a numeric frame exists.

## Limits

- Two bodies that pass each other at kilometers per second share one numeric frame for the encounter. One of them is fast inside it, so the old precision returns for that body during the encounter.
- A fast group that passes a static body, or a hull at rest in its semantic frame, joins the anchor's home for the encounter (see "Anchors"). Its fast bodies are fast in that home, so the old precision returns for them during the encounter, as in the limit above. The anchor is a real body there, so Jolt sweeps the fast bodies against it. Before `ter-dhkf`, the anchor stayed apart and had a fast ghost in the group's falling frame, which a fast body passed through (`ter-ykqs`).
- A group that links anchors that move differently, the anchor of a following frame and an anchor at rest or the anchor of another following frame, keeps a ghost of that anchor in its home, and that ghost moves fast in the home's system. A fast ghost has no linear cast, so it can pass through a body thinner than one step of its motion. The ghost of a hull has the hull's layer, so only a hull meets it, as only a hull meets the hull, and the ghost of a plain kinematic body meets what that body meets. Without numeric frames the anchor is a kinematic body of the semantic frame's own system, which has no linear cast either. Since `ter-zg77` a fast plain kinematic body is such an anchor: one that passes a static body keeps its own following frame, and the static body's home keeps its ghost, where before it joined the static body's home. The ghost moves with the chord of the anchor's last step, so during a push it lags the anchor by one step of its acceleration: 1.3 cm/s at 0.8 m/s². The limit also holds for the ghost of each body that sticks out of the hull's frame. The home keeps that ghost beside the ghost of the hull (see "Ghosts as infinitely heavy"). Unlike the ghost of the hull, it has the ghost layer, so the bodies there that move meet it.
- An encounter of a fast body with the anchor of a following frame runs at the encounter speed in that frame, as for any anchor.
- A fast body that the merge test links to the place of a ghost joins the ghost's home for the encounter. It is fast in that home, so the old precision returns for it during the encounter. The ghost is a slow body there, so Jolt sweeps the fast body against it (see "Ghosts as infinitely heavy"). Before `ter-4psc`, the fast body's numeric frame kept a fast ghost. A body thinner than one step of that ghost's motion did not stop it.
- A fast body within the merge test's reach of its frame owner's interior runs at full speed in the frame's own system, as without numeric frames. The reach is 2 s of the body's speed plus the bounding radii. That is 100 m for a character at 50 m/s, and 2 km for a free body at 1,000 m/s. So in a ship or a station, a fast body falls in a numeric frame only when the frame is far larger than its interior.
- A group of free bodies below 1,000 m/s runs in its semantic frame's own system. Its gaps stay nearly exact, but its common path drifts from the exact path as one piece, by rules A and B. A body there that starts to thrust drifts by up to 0.92 mm/s before its thrust clock counts it. Pulses of thrust less than 20 s apart add up. So pulses that the clock never counts drift by about 2 mm/s per minute at the measured rate. At the model's ceiling, they drift by 2.7 mm/s per minute. See "Thresholds from measured precision".
- Before `ter-6r8c`, a body that stuck out of the bubble of a `Kepler` frame in its orbit frame had its ghost there with the orbit's chord, and without numeric frames its ghost in the semantic parent had the orbit's instant velocity, half a step of the orbit's acceleration apart, 7 cm/s in low orbit. Since then the ghost of a frame on rails moves with the chord of its record in both cases (the decision "The velocity of a ghost of a frame on rails" in [Rails components](rails-components.md)), so this limit went.
- A following frame whose anchor has no motion intent falls, and its anchor holds still in it, hull or plain body (test 7 of [Rails components](rails-components.md)). So a still kinematic body 2^23 m or more from its semantic frame's origin, which has a following frame by the distance rule, falls in a gravity field, where without numeric frames it holds still. Before `ter-zg77`, a still plain kinematic body there had a resting frame and held still.
- A new following frame of a plain kinematic body starts with the chord of a free fall, because the tree keeps no proper acceleration of a plain body (see "Following frames"). An anchor on end poses in a gravity field does not fall, so its first planned step takes half the field, and the frame's system shows the other half for that one step, as for the frame of a hull that spawned at the sync point. The start also takes the float of the row's local velocity, about 0.12 mm/s at 2 km/s, which that step's landing acceleration takes in.
- A plain kinematic body takes no spawn velocity, so it is at rest at the sync point of its spawn. It runs its first step at full speed in the system of its semantic frame, or of the numeric frame that its group takes there, and gets its following frame at the sync point after it.
- A body that sleeps in a following frame does not feel its effective gravity, so it moves with the frame. When the anchor's motion intents change its velocity, as end poses that speed up or a teleport onto a new orbit do, the frame takes an acceleration that is not the field, and it drags the sleeping body along, where an awake body keeps its motion in the semantic frame. Physics wakes the bodies of a frame when its proper acceleration moves (see the sleep rules under "Physics" in `ARCHITECTURE.md`), but a numeric frame has none, so no rule wakes the bodies of a following frame when its acceleration moves. `ter-zg77` left this wake rule open.
- A read in a distant frame rounds at that distance. An example is the position of a ship near another star, read in the frame of the sun. Reads between nearby entities do not round.

## Tests

- `ter-fa7j`: across a minute of thrust in the frame earth, the person's sideways drift stays under 1 mm/s.
- `ter-r90w`: a person behind a body along the orbit, 1.2 m to the side, passes it.
- A crate released near the ship follows the orbit that its starting state gives in earth, to within a millimeter after ten minutes. This proves the tidal gravity.
- Every gameplay surface in the list above shows the same frames with numeric frames on and off.
- Two runs with the same input give the same clusters, in the same order.
- `ter-dhkf`: a fast crate inside a station-like frame hits a static wall, a box or a mesh, that is thinner than one step of its motion, and stops at it, with numeric frames as without. It also stops at a thin wall of its frame owner's interior, and a crate of 25 kg stops at a still kinematic door of 1 kg. A fast ball that links the interior of a frame and a static rock in a resting frame far out takes the interior's home, the frame's own system, for both. A room without a body in a `Kepler` station that dies in its orbit frame holds still in earth where it was. A fast crate that passes a static rock, a `Spin` hull, or two static rocks joins their home for the encounter, lets go and falls again, with the same events and motion as without numeric frames, with 1 and 4 threads.
- `ter-dhkf`, rule 3: a `Kepler` station's hull alone at orbital speed gets an orbit frame and rests in it, and every read of it in earth is the orbit's state for ten minutes: the place to the last bit, the velocity to 1e-8 m/s. Crates that drift out of the station's bubble at 1 m/s join the orbit frame and stay near 1 m/s there, and drift on in a straight line relative to the station. A cargo pod among them bumps the hull's plate at the tick that its drift predicts, as without numeric frames. A fast rock that passes the station runs in its orbit frame for the encounter. The bodies in the station's own frame move as before, and those that stick out of its bubble have their ghosts in the orbit frame. A group that links a fast `Kepler` hull and a static rock lives in the rock's home, which keeps a ghost of the hull that hits a pod as the hull does without numeric frames. A frame that gets the Static driver and then the Kepler driver again leaves its orbit frame and gets a new one, and a new orbit leaves the crates beside the hull where they were. The orbit scenes give the same events and homes with 1 and 4 threads.
- `ter-dhkf`, the checked-in scene `station` (`DemoScenes.Station`, `scripts/station.json`, `StationSliceTests`): a station on a `Kepler` orbit 400 km up, with a cabin, a bubble and an airlock that opens to the outside. Three cargo pods drift out of the airlock at 1 m/s, leave the bubble into the hull's orbit frame, keep their straight drift relative to the station to within 1 cm (2 mm in the run, 3 cm without the orbit frame), and bump the hull's radiator outside the bubble at the tick that the drift predicts, with a `Collision` with the station. After the pods have left the station's frame, the script throws a crate of 25 kg at 1,200 m/s at a thin static wall in the cabin. That is above the 1,000 m/s from which a group of free bodies falls. The merge test links it to the wall, to the station's interior and to the airlock's two kinematic doors. Each of them keeps its group out of a falling frame: the wall and the interior are anchors, and the still doors are infinitely heavy, so the group's velocity is theirs, zero. So the crate stays in the station frame's own system and stops at the wall with one `Collision`. When the merge test links none of them, the crate falls in a numeric frame of its own and passes through the wall. Before `ter-3xs6`, the scene threw the crate at 60 m/s, which every group fell from at that time. When no `Kepler` hull gets an orbit frame, the pods drift 3 cm off their line. The scene gives the same dumps with 1 and 4 threads.
- `ter-dhkf` with `ter-1w06`, the two forms of a character (`CharacterFormNumericFrameTests`): a character takes its form from the effective gravity of its physics home. Beside a `Kepler` hull in low orbit it is rigid in the hull's orbit frame K, which falls with the hull, and it walks in earth's own system without numeric frames, where the pull is 8.6 m/s². Either way it stays beside the hull. A rigid character that drifts out of a `Kepler` station has a ghost while it sticks out of the bubble. It stays rigid in K or in a falling frame of its own, and walks in earth's own system without numeric frames. An impulse that gameplay adds in the tick of a weightless character's hand-off into a ship with gravity converts into the ship's axes at sync step 7b and pushes the character, which walks there, along the axes of the frame that gameplay named.
- The scripted spacewalk thrusts for several seconds in earth, not only 0.4 s.
- `ter-3xs6`, the distances (`NumericFrameThresholdTests`): a crate at rest on a floor 6,371 km from its frame's origin stays in its frame's own system. A crate at rest on a floor 10,000 km out shares a resting frame with the floor from the first tick. It stays at rest on the floor.
- `ter-3xs6`, the speeds (`NumericFrameThresholdTests`, `FrameTreeThrustTests`, `NumericFrameRulesTests`): a free crate at 60 m/s stays in its semantic frame's own system. A free crate that coasts at 1,000 m/s falls from its first tick, and one at 999 m/s does not. A free body and a body that thrusts fall together when their common speed reaches 50 m/s. A weightless character at 60 m/s falls from its first tick, although it asks for no force. An anchor never makes its group thrust, and a `Kepler` hull never thrusts.
- `ter-3xs6`, the thrust clock: a single tick of thrust never counts, however strong. A tick below the minimum acceleration is no thrust tick. Thrust in pulses less than the window apart adds up. A crate at 60 m/s with a gentle force gets a falling frame at the sync point of its 30th tick of thrust. Its thrust time then reaches 0.5 s. A gentle impulse on every tick counts as the same force. When the thrust stops, the crate keeps its frame for the 20 s of the window, and then goes back to the system of space. A kinematic crate with a thruster never thrusts. A body that is gone from the numeric update, or turns static, loses its clock.
- `ter-4psc`, the places of ghosts (`FrameTreeGhostPlaceTests`, `NumericFrameRulesTests`): a fast body near a crate that can stick out of a child's bubble joins the hull's falling frame when the hull falls. It stays in the semantic frame's own system when the hull is parked or the child has no hull. A body far from every place keeps its own frame, and a static body that sticks out leaves no place. The place of a body turns and moves with a turned, spinning or moving child. A body of the parent, or of a sibling, that can touch a child's bubble leaves a place in the child. The child's bodies near that place stay in the child's own system. A static or kinematic body that is no sensor is not linked by a place, and a sensor is. A body at the edge of the reach of a place does not go in and out of its system. A place can link a group to a `Kepler` hull in its own orbit frame. The group then lives in the home of its static rock and keeps the hull's ghost. A warm update with places allocates nothing, and two runs with the same input make the same frames and moves.
- `ter-4psc`, physics (`GhostPlaceTests`): a fast rock in a falling frame hits a crate that sticks out of a parked station, in the system of the station's hull. A crate that sticks out of a falling ship pushes a rock that rests in space, in the ship's falling frame. A fast crate inside a frame meets the ghost of a hull that touches the frame's bubble without entering (`ter-tenw`). These three give the same hit tick, to within one tick, and the same contact news as without numeric frames. A body in the home of a group whose `Kepler` hull lives apart meets a crate that sticks out of the hull's frame. The ghosts of that hull and crate go at the sync point whose update stops naming their home, also when nothing else changes. A crate that sticks out of a falling ship has one ghost, in the ship's falling frame, and none in the system of space.
- The review of `ter-4psc` (`FrameTreeGhostPlaceTests`, `NumericFrameRulesTests`, `NumericFrameGhostTests`, `GhostPlaceTests`): a falling ship's hull that passes 35 m from a crate that sticks out of a parked station keeps falling in its numeric frame, because no hull meets a ghost. A sensor does not detect the ghost of a parked hull, and a kinematic body meets the ghost of a sensor. A fast rock near a crate that rests 1 mm inside a station's bubble stays in the semantic frame's own system whether the crate sleeps or not. A room with the `Physics` driver and no body in a ship that dies in a falling frame goes to space and holds still there, as without numeric frames. A body of the parent or of a sibling that reaches a bubble only through the speed of the body, of a frame or of a spin still leaves a place there. A crate that rests against a rock in space and enters a falling ship keeps its contact with the rock, with one `Collision` and no end, as without numeric frames (`NumericFrameStickOutGhostTests`). The sync point that moves a ship's hull into its falling frame, or back into space, moves the ghost of the crate that sticks out of the ship along at once, at the crate's place and velocity, and leaves the ghost of a station's crate in space where it is. A rock that comes at that crate head on at 60 m/s and meets its ghost in the step after the move stops at it at the same tick and place as without numeric frames. The World hands physics one empty batch for each numeric update (`FaultInjectingBackendTests`).
- `ter-zg77`, following frames (`FollowingFrameTests`, `FrameTreePlainAnchorTests`): a kinematic hull that end poses drive at 2 km/s along a straight line rests at the origin of a following frame, and a crate that drifts out of the hull's frame joins that following frame (test 8 of [Rails components](rails-components.md)). A plain kinematic body that end poses drive at 2 km/s rests at the origin of a following frame of its own, and a crate beside it joins that frame and drifts on there in a straight line. A plain kinematic body on a low Kepler orbit stands on its orbit to the last bit in its following frame. When a plain anchor dies, its frame falls on and keeps the crate in it. When it teleports, its frame jumps with it, and the crate stays where it was. A group that links a fast plain kinematic body and a static rock lives in the rock's home, which keeps a ghost of the body with the body's own layer, and the ghost hits a crate at the tick at which the body does without numeric frames. On the tree: a plain anchor's frame starts with the chord of a free fall, plans the anchor's end pose from its origin and lands on it, falls on when the anchor is no body in it, goes when the anchor slows down, and jumps without moving the frames in it. A plain body that follows in the frame of another anchor gets a following frame of its own. A plain kinematic body is an anchor only while it follows, and below the speeds of a free body it takes the speeds of a body that thrusts.
- A body that drifts a light-year away from its semantic frame's origin keeps millimeter steps relative to a body next to it.

## Order of the work

The tickets hold the order. In short:

1. Split the row into a semantic frame and a physics home. Replace every read without a frame by a read in a frame, make velocities `Double3`, and convert writes at apply time. Every body's home is still its semantic frame.
2. Add numeric frames to the frame tree: ownerless, hidden from the public surface, moved by integrated free fall, with tidal effective gravity in doubles and a Jolt system each.
3. Move groups of bodies between a semantic frame and its numeric frames, with no events. Move a frame owner's frame under the numeric frame of its hull.
4. Cluster automatically: create, merge, split, drain and collect, with resting and falling frames.
5. Nest numeric frames for vast distances.
6. Close `ter-fa7j` and `ter-r90w` with their scripted runs, and move the decisions into ARCHITECTURE.md.

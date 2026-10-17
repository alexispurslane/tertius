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
| Where a `Kepler` hull lives | In a numeric frame K that follows its driver, an orbit frame, when its orbit moves it fast or far. The hull's frame hangs under K. (User, `ter-dhkf`) | An anchor's home must hold it at rest, as a falling frame holds a ship's hull. |
| How an orbit frame moves | At each sync point its place in the semantic frame is the orbit's place, and its velocity is the orbit's chord over the step that ends then. During a step it falls with the orbit's acceleration, as a falling frame falls. (`ter-dhkf`) | The place follows the orbit exactly, so the hull's place does too, over any run. With the chord, the orbit frame moves as Jolt moves a body: its members move as they would in the semantic frame's own system. See "Orbit frames". |

## How a numeric frame moves

A numeric frame moves in one of two modes, and the speed of its members chooses the mode:

- A falling frame falls freely in the field of its semantic frame. Fast members get one. Without free fall, the 8.7 m/s² of Earth's gravity at the ship's orbit enters every member's float, and the drains never stop.
- A resting frame stays at one place in its tree parent. Slow members that are far from their home's origin get one. A slow body can rest on something, such as a crate on the ground, so a falling frame is wrong for it. The frame of Earth's surface sits at Earth's center, so a crate on the ground is 6,371 km from its frame's origin. A falling frame there falls away from the ground at 9.8 m/s².

A resting frame whose members become fast turns into a falling frame. Its first frame velocity is the common velocity of its members, as in a drain. A falling frame can also have slow members. If they are still far from the origin of the frame above it, the frame turns into a resting frame. Otherwise it goes away.

A falling frame moves as follows.

The engine steps the frame with the same scheme that Jolt uses for bodies, in doubles:

1. It takes the field of the semantic frame at the frame's origin, at the start of the step. This is the frame's acceleration for the step.
2. It adds the acceleration times the step to the frame velocity.
3. It adds the new frame velocity times the step to the frame's position.

Each member gets the effective gravity: the field at the member's true position minus the frame's acceleration from step 1. The engine computes both terms in doubles and subtracts them before it narrows the result to a float. Near the ship's orbit, this tidal gravity is about 0.00026 m/s² for 100 m of height difference.

A drift of the frame from the true orbit does not change the physics. A member feels the field at its true position, minus the acceleration that its frame really had. So the sum of the frame's motion and the member's local motion is the motion that the member has in the semantic frame. A drift only makes the local velocities grow slowly, until the next drain.

Without the tidal gravity, the clustering changes the physics. Two bodies 100 m apart near the ship's orbit move apart by tens of meters in ten minutes through tidal gravity.

### Orbit frames

An orbit frame (`NumericMode.Orbiting`) is the home of the hull of a `Kepler` frame whose orbit moves it fast or far: the rules that give any body a numeric frame give one to the hull alone (see "Anchors", rule 3). The orbit frame lies in the semantic frame. It holds the hull's frame, its orbiter, at its origin, and the orbiter keeps the orbit's rotation. The orbit frame never rotates. It moves as follows:

1. At each sync point, the orbiter puts the orbit frame at the orbit's place at the end of the tick, to the last bit (step 6b of the sync point in `ARCHITECTURE.md`). Its velocity is the orbit's chord over the step that ends then: the change of place over the step, divided by its length. A Taylor series of the two-body motion to the jerk gives it from the orbit's state, without a difference of two places, which would lose digits far from the parent (`NumericFrameRules.OrbitChord`).
2. At the start of each step, the orbit frame takes the orbit's acceleration at its origin as its step acceleration: the pull of the point gravity whose G·M the orbit takes.
3. In the step, it moves as a falling frame does: the velocity gains the acceleration times the step, then the place gains the velocity times the step. This ends the step on the orbit, to less than 1e-13 m in low orbit, and the next sync point puts it there exactly. The bodies in the frame move with it.

The chord is the velocity that Jolt gives a body that moves along the orbit. A crate in the orbit frame feels the field minus the orbit's acceleration, and its true motion is the orbit frame's motion plus its own. With the chord, that sum is the motion that Jolt gives the crate in the semantic frame's own system, so the crate moves as it does without numeric frames. With the orbit's instant velocity instead, it would drift by half a step of gravity, 7 cm/s in low orbit.

The tree keeps the orbiter's velocity relative to the orbit frame as the rest of the orbit's instant velocity, so that a read of the hull or its frame in the semantic frame gives the orbit's velocity. Physics reads a numeric home's velocity as a chord and its instant velocity as half a step of its acceleration further on (see "Instant velocities and chords" in `JoltPhysicsBackend.Chords.cs`). Both are the orbiter's own, to the orbit's jerk times the square of a step, so physics sees the orbiter at rest in the orbit frame (`FrameTree.KeplerMotion`).

An orbit frame never drains, never moves its origin to its members, and never nests: the orbit gives it its place and velocity. Only the group of its hull keeps it. A group in it without the hull finds a frame of its own, as a group does when another group keeps its frame. When the orbiter leaves, because its driver changes, because its hull no longer needs a frame, or because it dies, the orbit frame turns into a falling frame that falls on from its place, and the numeric update takes it as any falling frame. A new orbit outside a tick moves the orbiter to its new place at once, and the orbit frame with it, but the other bodies and frames in the orbit frame keep their places and velocities in the semantic frame, as they do without numeric frames (`FrameTree.MoveOnOrbit`).

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

A body gets a numeric frame for either of two reasons. No content flag is needed.

- Speed: the body's speed relative to its semantic frame passes a threshold. It gets a falling frame.
- Distance: the body's distance from the origin of its physics home passes a threshold. It gets a resting frame. If it is also fast, it gets a falling frame.

Distance matters to Jolt even where doubles are fine. Jolt keeps each body's bounds as floats in its system's coordinates. At 6,800 km from the origin, those bounds step by 0.5 m, and at 1 AU by 16 km. Loose bounds make the broadphase test pairs that cannot touch. Jolt's docs put floats at their best within about 5 km of the origin.

Inside the ship and in other small frames, nothing changes.

### Merge before contact

Bodies in two different Jolt systems cannot touch. So two numeric frames merge before any of their members can collide. The test looks ahead in time: it grows each member's bounds by its velocity relative to the other frame, times a lookahead time. Space Engineers grows its bounds by a distance in meters, but at orbital speed a fixed distance is crossed in a fraction of a second.

A numeric frame also takes in a body of its semantic frame by the same test. Examples are the ship's hull and a crate that a person throws out of the ship.

### Anchors

Ticket: `ter-dhkf`, which fixes `ter-ykqs`. This section is the binding design for anchors in the merge test.

An anchor is a static body, the hull of a frame that physics does not drive, which is a frame with the `Static`, `Kepler` or `Spin` driver, or the interior of a frame owner. Physics keeps the interior (`BodyDesc.InteriorShape`, or the hull's shape when there is none) as a static body in the frame's own system, at its origin, so the cabin walls of every ship and station are an anchor of that system. No force moves an anchor. Before `ter-dhkf`, the merge test linked an anchor only to a group that does not fall. A falling group stayed apart from the anchor, and its frame kept a kinematic ghost of the anchor. That ghost comes at the members at the frame's speed. Jolt gives a kinematic body no linear cast: a cast follows only the motion of the dynamic body that it belongs to. So a crate at 60 m/s in a falling frame passed through a wall that is thinner than one step of its motion, 1 m at 60 Hz. Without numeric frames, the crate's linear cast stops it at the wall (`ter-ykqs`).

The user decided three rules. They extend the merge rules above literally.

1. The merge test includes anchors for every group, also for a group that falls. (User)
2. An anchor counts as infinitely heavy when the group chooses its home. A group that links an anchor takes the anchor's home, as a group of bodies keeps the frame that holds the most of its mass. (User)
3. A moving anchor needs a home in which it is at rest. The hull of a frame whose `Kepler` driver moves it in its semantic parent becomes a member of a numeric frame K that follows the driver. The hull's frame hangs under K, as the frame of a physics-driven ship hangs under the numeric frame of its hull (see "Frame owners in a numeric frame"). The frame tree's invariant that only a physics-driven frame may hang under a numeric frame changes for this. (User) The hull gets K by the same speed and distance rules that give any body a numeric frame, and keeps it down to the lower thresholds. A slow hull near its semantic frame's origin stays in that frame's own system, as the hull of a `Static` or `Spin` frame does. See "Orbit frames".

With these rules the anchor is a real body in the group's Jolt system, so Jolt sweeps the fast body against it. The user's precision case still holds: crates that leave a `Kepler` station through its airlock join the hull's frame K, where both are slow, so float velocities stay small.

The open details have these decisions:

| Question | Decision | Why |
|---|---|---|
| The home that a static body gives its group | Its semantic frame's own system, or its resting frame when it lies far out. The group gets the mode that its static bodies get alone: velocity zero, the center of mass of the static bodies, and the numeric frame that holds one of them as the frame that the group keeps, or none. | A static body takes no velocity, so its group never falls. The static bodies alone decide where the group lives, so a group that passes cannot pull a wall out of its own system. A wall 7 km from the origin, in the semantic frame's own system, stays there. With the group's own center and the mode of the group's falling frame, the group would rest there and take the wall into its frame. |
| The home that a `Static` or `Spin` hull gives its group | Its semantic frame's own system, where the hull's center is at rest. The group gets the mode None. | The hull's frame hangs in its semantic parent, so the hull lives in the semantic parent's own system. A `Spin` hull turns in place, and its center does not move. |
| The home that the interior of a frame owner gives its group | The frame's own system. The numeric update has a body for the interior beside the one for the hull: a static body at the frame's origin, with the bounding radius of the interior's shape, that no update moves (`NumericBody.Fixed`). The group gets the mode None, as with the hull of a `Static` frame. | Physics creates the interior in the frame's own system and has no move for it, so the interior can only be a real body there. A crate thrown at 60 m/s at a cabin wall stays in the frame's own system while the merge test reaches the interior's bounding sphere, and Jolt sweeps it against the wall. Before, the update had no body for the interior, so the crate fell in a numeric frame of its own, whose system holds no copy of the interior, and passed through every cabin wall. A fast body inside a frame therefore falls in a numeric frame only when it is beyond the merge test's reach of the interior, for example in a frame whose bubble is far larger than its interior. |
| How a kinematic body weighs in its group | A kinematic body counts as infinitely heavy too, but it is no anchor: the velocity and the center of a group with a kinematic body and no static body are those of its kinematic bodies alone. So a group can fall with its kinematic bodies, and a group with a still kinematic body does not fall. A static body still outweighs a kinematic one. The frame that the group keeps is chosen by the masses as before, and the drain gives it the group's velocity. | No contact changes the velocity of a kinematic body, and Jolt treats it as infinitely heavy in a contact, so a shut door stops a crate as a static wall does. When the group weighed a door of 1 kg by its mass, a crate of 25 kg at 60 m/s and the still door moved at 57.7 m/s and fell in a numeric frame, where the door came at the crate at 57.7 m/s. Jolt gives a kinematic body no linear cast, so the door passed through the crate. A kinematic body can move between frames, unlike a static one, so it does not fix where the group lives. |
| The home that a `Kepler` hull gives its group | The orbit frame K of rule 3, when the hull's own motion gives it one. A group without an anchor at rest takes K and the mode `Orbiting`, and the update makes K when the hull has none. A hull without K is fixed in the semantic frame's own system. | The hull moves fast in its semantic frame's own system, so a group that joined it there would be fast too. In K, the hull rests. |
| How a group lets go of an anchor | By the split rules, when the merge test no longer links them. Two bodies of one system stay linked up to the split margin farther. The split cooldown holds only the members of a numeric frame that are not anchors, as before. So a group that falls leaves the semantic frame's own system at once. It leaves a resting frame at once too, unless that frame also holds other bodies that are not anchors, such as a crate on the wall: the cooldown then holds it with them for 1 s. | The semantic frame's own system lets a group that falls leave at once, because the speed thresholds give falling its own hysteresis. The cooldown of a numeric frame stays as it is, because it stops the members of one frame from splitting and merging at each sync point. The split margin stops a group at the edge of the reach from going in and out at each sync point. |
| A group that links two anchors whose homes differ, and both homes are at rest in the semantic frame | The group takes one home for all its anchors, and a static body moves into it, as before `ter-dhkf` for an anchor that a slow group links. A `Static` or `Spin` hull wins: the group goes to the semantic frame's own system. Else the resting frame that holds a static body of the group wins, the first in entity order on a tie, and the mode of the static bodies decides whether the group stays in it or goes to the semantic frame's own system. | Each home at rest in the semantic frame can hold a static body, so no anchor needs a ghost, and each one is a real body for the group. The choice reads only the anchors and their homes, which the moves of the group do not change. After the move the anchors share one home, so the next sync point makes the same choice. |
| A group that links two anchors that move differently: a `Kepler` hull and an anchor at rest, or two `Kepler` hulls in different frames K | The group takes one home, and each other anchor keeps a ghost in it, as before `ter-dhkf`. The home of the anchors at rest wins over K. Among frames K, the K of the first hull in entity order wins. | No home can hold both anchors at rest. When the anchors at rest win, the group's fast bodies are real bodies with linear casts against them, and the ghost of the hull moves with the bodies that move with the hull. The choice reads only which anchors the group links, so it does not flip at each sync point. |
| How the infinite mass is computed | No sum adds or divides an infinite mass. A numeric frame that holds a static body of the group gets the mass +∞, which only comparisons read: the choice of the frame that the group keeps, and of the group that keeps a frame. A group with a static body outweighs every group without one in that frame, and between two such groups the first in entity order keeps it. The velocity and the center of a group with a static body are those of its static bodies alone, and those of a group with a kinematic body and no static one are those of its kinematic bodies alone. | Infinity times a velocity, divided by infinity, is NaN. Those velocity and center are the limits of the mass-weighted means as the mass of the static or kinematic bodies grows without bound. The sums ∞ + m and ∞ + ∞ are exact, and so are comparisons with ∞. |

The work had two steps, both built in `ter-dhkf`:

1. Rules 1 and 2 for static bodies and for the hulls of `Static` and `Spin` frames, and the rule for a group that links anchors whose homes are at rest in the semantic frame. `NumericFrameRules.Summarize` gives a group with a static body the mode, center and frame of its static bodies.
2. Rule 3. A `Kepler` hull gets its orbit frame K (see "Orbit frames"), the tree's invariant changes, and a group that links anchors that move differently keeps a ghost of the other anchor. `FrameTree.UpdateSemanticFrame` links every pair of bodies in one pass. `NumericFrameRules.OrbitsInFrame` says which `Kepler` hulls live in an orbit frame, `NumericFrameRules.Summarize` gives a group its orbiter, and the update marks each such hull that its group does not follow: that hull stays in its own K, which the update makes when the hull has none, and the group's home keeps a ghost of it (`FrameTree.AnchorGhosts`).

An encounter of a fast group with an anchor runs at the encounter speed in the anchor's home. The doc already accepts this for two bodies that pass each other (see "Limits").

What the two steps changed outside the tree: the World tells the update which hulls have the `Kepler` driver (`NumericBody.Orbits`), and the tree fills in the mode of each body's home (`NumericBody.HomeMode`). Physics needed no new path for an anchor that a group shares, because a fast body that joins the anchor's home is a body there like any other, with its linear cast. A move keeps the moved body's ghosts in the systems of other frames (see "Moving bodies between Jolt systems"). A hull meets only static bodies and other hulls in its own system (`Layers`), so a fast body that joins the system of a hull passes it, as it does without numeric frames. Only a hull bumps a hull outside a bubble: a cargo pod, a frame with the Physics driver, that drifts out of a `Kepler` station bumps the station's hull in K, at 1 m/s. The old ghost of a `Static` or `Spin` hull in the body's falling frame met moving bodies, so it stopped the body there. So the ghost that rule 3 keeps for a `Kepler` hull in the home of a group has the hull's own layer, and it meets there only what the hull meets: static bodies and other hulls, and a kinematic ghost meets no static body (`GhostBody.Anchor`). Physics predicts a `Kepler` frame from the tree (`FrameTree.KeplerMotion`), at the origin of its orbit frame or at the orbit's place, and it reports the sleeping members of an orbit frame, which move with the frame, as it reports those of a falling frame.

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

### Starting values

These values are guesses. Tests tune them.

| Value | Start |
|---|---|
| Speed to get a numeric frame | 50 m/s |
| Speed to give it up | 20 m/s |
| Distance from the home's origin to get a numeric frame | 10 km |
| Distance from the semantic frame's origin below which a slow group gives it up | 5 km |
| Distance from the tree parent's origin to nest a numeric frame | 10^9 m |
| Grid of a resting frame that nests | 2^20 m |
| Lookahead time of the merge test | 2 s |
| Split margin beyond the merge test | 10 m |
| Split cooldown | 1 s |
| Common local speed that starts a drain | 1 m/s |
| Distance of the members' center of mass from the frame's origin that moves the origin | 1 km |

## Moving bodies between Jolt systems

A move to another system loses Jolt's contact cache, warm start and constraint impulses, and Jolt recreates a character. So:

- The engine moves a whole group of members that touch or are joined together, never half of it.
- It adds sleeping bodies without waking them.
- It carries the held contact pairs of the group, so a contact that goes on is not reported again.
- No move between numeric frames, or between a numeric frame and its semantic frame, sends `FrameChanged` or changes a topic membership.
- A moved body keeps its ghosts in the systems of other frames. Every frame where a ghost may be depends only on the body's semantic frame, which a move does not change, and the ghost's velocity in its system does not change either. The ghost pass of the next step puts each ghost at the body's new state. Before `ter-dhkf`, a moved body lost every ghost for one step. Since rule 1 of "Anchors", a fast ship joins the system of a parked station's hull while their bubbles can overlap, and it then had no ghost in the station for that step.

## Frame owners in a numeric frame

The ship's frame follows its hull, and the hull lives in the system of the ship frame's tree parent. When the hull joins a numeric frame, the ship frame moves under that numeric frame in the tree. Its semantic parent stays earth. A person who leaves the ship's bubble hands off to the ship frame's tree parent, which is then the numeric frame. The person's row frame becomes earth, as it does today. Rule 3 of "Anchors" adds a second kind of frame under a numeric frame: the frame of a `Kepler` hull hangs at the origin of the hull's orbit frame K (see "Orbit frames"). A body that leaves the frame's bubble hands off to K, as it hands off to a ship's numeric frame. A frame whose spin or whose owner's writes move it still hangs in its semantic parent: when the game gives a frame such a driver, `World.SetFrameDriver` runs the numeric update, which moves the hull and the frame there. When a frame dies, each frame in it whose driver is not `Physics` moves to the dying frame's semantic parent, and a frame with the `Physics` driver to the dying frame's tree parent, which can be a numeric frame (`FrameTree.Destroy`). A station in its orbit frame K that dies leaves K falling, and a room in the station, a frame with the `Static` driver and no body, which no numeric update would move, holds still in the station's semantic parent where it was, as without numeric frames.

Every frame carries its children through the tree, so no force has to carry them. The ship's contents live in the ship's own Jolt system, at small positions relative to the ship. They move with the ship by composition, as they do today. When the ship frame moves under a falling numeric frame, the ship and its contents move with that numeric frame in the same way. Only the ship's proper acceleration, such as its thrust, acts on its contents, as one uniform force. Friction and inertia never have to drag cargo along, so they cannot make it jitter. The rules can also make a numeric frame inside the ship. That frame moves with the ship for the same reason.

## Bubbles and ghosts

An anchor is a static body or the hull of a frame that physics does not drive, such as a parked station's hull. It never moves between frames. A ghost is a kinematic copy of a body in the Jolt system of another frame, so that the bodies there collide with it, or a static copy of a mesh that holds still there (see below). A numeric frame has no bubble, so it does not change which bubbles a body, a hull or an anchor meets. Every one of them gets the same hand-offs and the same ghosts in semantic frames as in the same scene without numeric frames. (User) Physics finds the bubbles through the numeric frames in these places:

- A body in the system of a frame meets the bubbles of the semantic children of that frame's semantic frame, wherever they hang among its numeric frames. It enters the deepest one that it touches, or it has a ghost in each one that it touches without entering, for example a hull that does not fit. A person in a falling frame enters a parked station, and a fast ship's hull has a ghost in a station that is too small for it.
- An anchor never enters a bubble, but a ghost is about touching, not entering, so the hull of a frame that physics does not drive meets the same bubbles. A static body that is not a hull has no ghost in a bubble, with numeric frames or without. A parked station's hull lies in the system of the station's semantic parent. A ship that falls past it hangs under a falling frame until the merge test links the two hulls, and then under the station's semantic parent for the encounter (see "Anchors"). Either way the station's hull has a ghost in the ship while their bubbles overlap. So the crates deep inside the ship hit the station's hull.
- A body that sticks out of its frame's bubble has a ghost in the frame's tree parent, which can be a numeric frame. The body is partly in that parent, so it also meets the bubbles that the members of the parent's system meet: the semantic siblings of its frame, wherever they hang. A crate that sticks out of a falling ship pushes a crate in a parked station beside it, and a crate that sticks out of the station stops a crate in the ship.
- The bubble that a body sticks out of and leaves is that of the semantic frame of its system's frame. A numeric frame has no bubble, so a body in a numeric frame sticks out of and leaves the bubble of the numeric frame's semantic frame, as a body in that frame's own system does. It has the same ghosts in that frame's tree parent and siblings, and it leaves into the same tree parent (`ter-kz89`). A crate thrown at 60 m/s inside a ship, beyond the merge test's reach of the ship's interior, falls in a numeric frame below the ship (see "Anchors"). When it sticks out of the ship's bubble, it has a ghost in the ship's parent and pushes a crate in a parked station beside the ship. When it is fully outside the bubble plus the exit margin, it moves into the ship's parent, and it hears `FrameChanged`, at the same step, place and velocity as without numeric frames. Without this rule the crate never left the ship.
- When that tree parent is a numeric frame, it holds only the bodies that the merge test links to the frame's hull. The merge test reaches the bounding spheres plus 2 s of relative speed, not the bubble. The frame's semantic parent keeps the other bodies in its own system, so the body that sticks out also has a ghost there (`ter-wllb`). A crate that sticks out of a falling ship pushes a body at rest in space 200 m below the hull's path, and a crate that sticks out of a ship that rests in a resting frame stops a body that flies at it. A nesting frame between the two holds no bodies, so it gets no ghost.
- The semantic parent's other numeric frames hold the bodies there that have a frame of their own, and the merge test runs per semantic frame, so it never links them to a body that sticks out of a semantic child. So at each sync point the numeric update tests each body of a group that gets a numeric frame against the bubble of each semantic child of its semantic frame that has a body that can move. The test is the merge test against the sphere around the child's origin that holds every body that sticks out of its bubble: the bubble's longest radius plus two radii of the child's largest body that can move plus the exit margin for that body, with the body's speed relative to the child's origin times the lookahead time, and no split margin. A hull moves as its frame's driver says, whatever its description says, so the hull of a Physics, Kepler or Spin frame can move, and a static hull cannot. The hull of a Kepler or Spin frame never leaves the bubble, so it can stick out by any amount, and the sphere reaches its distance from the child's origin plus its radius plus its speed times the lookahead time. A radar that a Spin frame turns at the edge of a parked station has a ghost in a resting frame 20 km out whose rock touches it. It measures the body from its place in its home, through the lowest common ancestor, as the merge test does. A first test in the semantic frame's coordinates, whose reach grows by a slack for their rounding, spares that measure for most pairs: with 100 falling frames beside 100 parked stations, the update takes about 150 µs more, against about 900 µs without the first test (`NumericFrameUpdateBenchmarks`). The update names the group's frame for that child, unless the frame is the child's tree parent, which has the ghosts already, and each body that sticks out of the child has a ghost there too (`ter-2gg0`). A rock at 60 m/s in a falling frame of space hits a crate that sticks out of a parked station that hangs directly in space, and a crate that sticks out of a falling ship pushes a body that rests 20 km out in a resting frame of its own, or that flies beside the ship in a falling frame of its own, as both do without numeric frames. A static body in a numeric frame needs no such ghost unless it is a sensor: a ghost is kinematic, and Jolt makes a contact between a kinematic body and a static one only when one of them is a sensor. So a static sensor 20 km out, in a resting frame of its own, detects a crate that sticks out of a parked station beside it, as it does without numeric frames.

The update tests the bodies against the bubbles at one moment, the end of the tick. The bodies' rows hold the end of the step from step 2 of the sync point on, and so do the frames that physics drives. So the sync point moves the frames that physics does not drive, Kepler and Spin frames and the static frames that gameplay moved, to the end of the tick before the update (step 6b in `ARCHITECTURE.md`). Before, it moved them after the update, and a station that orbits at 7.5 km/s was 126 m behind the rock that co-orbits beside it, so the update never named the rock's falling frame, and the rock passed through a crate that sticks out of the station.

The place and velocity of each bubble's parent relative to the body's system are sums through the numeric frames, in doubles, and so are those of the semantic parent relative to the tree parent, and those of the body's system relative to the bubble that the body sticks out of. The hand-off uses instant velocities, the velocities at the end of the step. The ghosts use chord velocities, the change of position over the last step divided by its length. Jolt's velocity of a body is its chord, and so is the velocity that the World reports, so a ghost moves as its real body does. In free fall the two kinds of velocity differ by half a step of gravity. The home of a group also keeps a ghost of each `Kepler` hull that the group links but cannot share a home with: a hull in an orbit frame of its own, when the group lives in the home of an anchor at rest or in the orbit frame of an earlier hull (see "Anchors", and the "Anchors" row in `ARCHITECTURE.md`, "Numeric frames"). Every other anchor shares the group's home instead. The reconcile allows a ghost in exactly these frames, those homes, and the numeric frames that the update names for the bubble that the body sticks out of, so it drops none of them. No frame gets two ghosts of one body. Those homes are frames of the anchor's own semantic frame, which no other ghost enters. The frames named for a bubble are numeric frames of its semantic parent other than its tree parent, a hand-off in the step that takes the bubble's hull to another semantic frame stops them, and only a body that sticks out of that bubble gets a ghost there. No bubble is numeric, and the semantic parent is an ancestor of the body's frame, so it is no bubble that the body touches. The tree keeps these places coherent with the frames that they name: a frame that leaves the tree takes its places with it, and a bubble that moves to another semantic parent, or under the numeric frame that kept the ghosts of its bodies, drops those places. When an update stops naming a place, physics drops the ghost there at the same sync point, also when the tree did not change, because the update sends the frame in its batch of changes. So physics keeps a ghost only where the tree allows one, between every two ticks.

A ghost is kinematic, so only a body that moves, or a sensor, collides with it. Jolt makes no contact between a kinematic body and a static one unless one of them is a sensor or a body asks for it, and the engine never asks. So a static rock beside a ship never meets the ghost of a crate that sticks out of the ship, with numeric frames or without. A contact with a ghost is a contact of the two entities (`ter-ejx9`, see `ARCHITECTURE.md`, "Contacts"). So a cargo pod in the home of a static rock that the ghost of a `Kepler` hull hits hears a `Collision` with the hull. The ghost appears only at the end of the first step in the frame, and the move's contacts wait for it. A ghost stands for its real body, so the ghost of an anchor meets only what the anchor meets: a kinematic sensor that falls past a static rock detects neither the rock nor its ghost, although that ghost is kinematic, and the ghost of a `Kepler` hull has the hull's layer. When the numeric update stops naming a home for the ghost of an anchor, physics drops the ghost there at the same sync point, whether or not the tree changed. A body that rests on a static floor and starts to fall stays linked to the floor, by the merge test with the split margin, until it is well clear of it, so it leaves no contact behind.

A hand-off happens in the step, but the update of the last sync point chose the numeric frames for the places before the step. So after a hand-off, a contact with a body in a numeric frame can be in no system for one step, where without numeric frames the semantic parent's own system takes it over. This happens in three ways. A crate that sticks out of a ship in a numeric frame and leaves the ship's bubble goes into that numeric frame, away from the bodies of the semantic parent that its ghost there touched (review of `ter-ejx9`). A crate that leaves a parked station into space drops its ghost in the resting frame of the rock that it pushes. A crate that enters a station with no body that can move has no ghost yet in the numeric frame of the rock that rides on it. The next sync point's numeric update links the bodies or names the numeric frame for the ghost. So the rule is: a contact that no system takes over waits one step with its count, when a hand-off of the step moved one of its bodies and one of its bodies is in a numeric frame. Gameplay then hears the same events as without numeric frames, and a contact that really ended at the hand-off ends one step late (review of `ter-2gg0`).

The ghost of an anchor moves in its falling frame, so it must be a body that Jolt moves with that velocity, also when the anchor is a mesh (`ter-dq92`). Jolt moves a kinematic mesh against dynamic bodies, given a mass, which physics provides because Jolt cannot compute one for a mesh. Before, the ghost of a mesh was always static, and the ghost pass moved it after every step, about 34 cm into a crate at 20 m/s. Jolt saw it at rest, so it neither stopped the crate, which passed into the wall, nor bounced a ball off it, while both happen without numeric frames. Now every ghost is kinematic, except the ghost of a mesh while it holds still in its system, which is static, as its real body is: Jolt never simulates it, while it searches the contacts of an awake kinematic body in every step, also with the bodies asleep on it (37 µs against 116 µs a tick with 100 boxes asleep on the ghost of a parked station's floor, `MeshGhostBenchmarks`). The ghost pass turns a mesh ghost kinematic when it starts to move and static when it holds still, and a drain that sets a static ghost moving turns it kinematic at once, so it moves in the next step. A crate in a falling frame that reaches a mesh wall within Jolt's speculative contact distance at the start of a step, or that sinks less than its own half into the wall, stops at the wall, and a ball bounces off it, with the same contact events as without numeric frames. Since `ter-dhkf`, a static mesh joins the group of a fast body that the merge test links to it (see "Anchors"), and a `Kepler` hull is never a mesh. So no anchor's ghost is a mesh any more. The rules of this paragraph still hold for the ghost of a mesh hull in a bubble that it touches.

A kinematic body has no linear cast in Jolt: a cast follows only the motion of the dynamic body that it belongs to. Without numeric frames, a fast crate that flies at a static wall has a linear cast, which stops it at the wall. In its falling frame the crate is slow, and the ghost of the wall comes at it fast, so no cast runs. When that ghost comes from beyond the speculative contact distance and moves farther in one step than the crate's half or the wall's thickness, it can pass through the crate. This held for the ghost of every anchor, a mesh or not. Since `ter-dhkf`, it holds only for the ghosts that "Limits" names. The same holds for a kinematic member of a falling group that its frame's velocity moves fast, such as a shut door in the group of a fast crate. Since `ter-dhkf`, a kinematic body counts as infinitely heavy in its group (see "Anchors"), so the group moves with its kinematic bodies, and they are slow in its frame.

## Which gravity each reader uses

| Reader | Gravity |
|---|---|
| The force on a body | Effective gravity, which is tidal in a numeric frame |
| Whether physics stands a character | Effective gravity. In free fall it is below the zero-gravity threshold, so a character floats, as in orbit. |
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

## Limits

- Two bodies that pass each other at kilometers per second share one numeric frame for the encounter. One of them is fast inside it, so the old precision returns for that body during the encounter.
- A fast group that passes a static body, or the hull of a `Static` or `Spin` frame, joins the anchor's home for the encounter (see "Anchors"). Its fast bodies are fast in that home, so the old precision returns for them during the encounter, as in the limit above. The anchor is a real body there, so Jolt sweeps the fast bodies against it. Before `ter-dhkf`, the anchor stayed apart and had a fast ghost in the group's falling frame, which a fast body could pass through.
- A group that links anchors that move differently, a `Kepler` hull in its orbit frame and an anchor at rest or another such hull, keeps a ghost of the hull in its home, and that ghost moves fast in the home's system. A fast ghost has no linear cast, so it can pass through a body thinner than one step of its motion. The ghost has the hull's layer, so only a hull meets it, as only a hull meets the hull. Without numeric frames the hull is a kinematic body of the semantic frame's own system, which has no linear cast either. The ghost moves with the chord of the hull's last step, so during a push it lags the hull by one step of the orbit's acceleration: 1.3 cm/s at 0.8 m/s². The limit also holds for the ghost of a body that sticks out of a frame that a fast numeric frame passes, such as a crate that sticks out of a large parked station whose hull the merge test does not reach.
- An encounter of a fast body with a `Kepler` hull runs at the encounter speed in the hull's orbit frame, as for any anchor.
- A fast body within the merge test's reach of the interior of its frame's owner runs in the frame's own system, at its full speed, as without numeric frames. At 60 m/s the reach is 120 m plus the bounding radii, so a fast body in a ship or a station falls in a numeric frame only in a frame that is far larger than its interior.
- A body that sticks out of the bubble of a `Kepler` frame in its orbit frame has a ghost in the semantic parent with the orbit frame's chord, where without numeric frames it has the orbit's instant velocity: half a step of the orbit's acceleration apart, 7 cm/s in low orbit. The ghost with numeric frames is the one that moves as the body does.
- A read in a distant frame rounds at that distance. An example is the position of a ship near another star, read in the frame of the sun. Reads between nearby entities do not round.

## Tests

- `ter-fa7j`: across a minute of thrust in the frame earth, the person's sideways drift stays under 1 mm/s.
- `ter-r90w`: a person behind a body along the orbit, 1.2 m to the side, passes it.
- A crate released near the ship follows the orbit that its starting state gives in earth, to within a millimeter after ten minutes. This proves the tidal gravity.
- Every gameplay surface in the list above shows the same frames with numeric frames on and off.
- Two runs with the same input give the same clusters, in the same order.
- `ter-dhkf`: a crate thrown at 60 m/s inside a station-like frame hits a static wall, a box or a mesh, that is thinner than one step of its motion, and stops at it, with numeric frames as without. It also stops at a thin wall of its frame owner's interior, and a crate of 25 kg stops at a still kinematic door of 1 kg. A fast ball that links the interior of a frame and a static rock in a resting frame far out takes the interior's home, the frame's own system, for both. A room without a body in a `Kepler` station that dies in its orbit frame holds still in earth where it was. A fast crate that passes a static rock, a `Spin` hull, or two static rocks joins their home for the encounter, lets go and falls again, with the same events and motion as without numeric frames, with 1 and 4 threads.
- `ter-dhkf`, rule 3: a `Kepler` station's hull alone at orbital speed gets an orbit frame and rests in it, and every read of it in earth is the orbit's state for ten minutes: the place to the last bit, the velocity to 1e-8 m/s. Crates that drift out of the station's bubble at 1 m/s join the orbit frame and stay near 1 m/s there, and drift on in a straight line relative to the station; a cargo pod among them bumps the hull's plate at the tick that its drift predicts, as without numeric frames. A fast rock that passes the station runs in its orbit frame for the encounter. The bodies in the station's own frame move as before, and those that stick out of its bubble have ghosts in the orbit frame and in earth. A group that links a fast `Kepler` hull and a static rock lives in the rock's home, which keeps a ghost of the hull that hits a pod as the hull does without numeric frames. A frame that gets the Static driver and then the Kepler driver again leaves its orbit frame and gets a new one, and a new orbit leaves the crates beside the hull where they were. The orbit scenes give the same events and homes with 1 and 4 threads.
- `ter-dhkf`, the checked-in scene `station` (`DemoScenes.Station`, `scripts/station.json`, `StationSliceTests`): a station on a `Kepler` orbit 400 km up, with a cabin, a bubble and an airlock that opens to the outside. Three cargo pods drift out of the airlock at 1 m/s, leave the bubble into the hull's orbit frame, keep their straight drift relative to the station to within 1 cm (2 mm in the run, 3 cm without the orbit frame), and bump the hull's radiator outside the bubble at the tick that the drift predicts, with a `Collision` with the station. After the pods have left the station's frame, the script throws a crate of 25 kg at 60 m/s at a thin static wall in the cabin. The merge test links it to the wall, to the station's interior and to the airlock's two kinematic doors. Each of them keeps its group out of a falling frame: the wall and the interior are anchors, and the still doors are infinitely heavy, so the group's velocity is theirs, zero. So the crate stays in the station frame's own system and stops at the wall with one `Collision`. When the merge test links none of them, the crate falls in a numeric frame of its own and passes through the wall, and when no `Kepler` hull gets an orbit frame, the pods drift 3 cm off their line. The scene gives the same dumps with 1 and 4 threads.
- The scripted spacewalk thrusts for several seconds in earth, not only 0.4 s.
- A crate at rest on a floor 6,371 km from its frame's origin gets a resting frame, and it stays at rest on the floor.
- A body that drifts a light-year away from its semantic frame's origin keeps millimeter steps relative to a body next to it.

## Order of the work

The tickets hold the order. In short:

1. Split the row into a semantic frame and a physics home. Replace every read without a frame by a read in a frame, make velocities `Double3`, and convert writes at apply time. Every body's home is still its semantic frame.
2. Add numeric frames to the frame tree: ownerless, hidden from the public surface, moved by integrated free fall, with tidal effective gravity in doubles and a Jolt system each.
3. Move groups of bodies between a semantic frame and its numeric frames, with no events. Move a frame owner's frame under the numeric frame of its hull.
4. Cluster automatically: create, merge, split, drain and collect, with resting and falling frames.
5. Nest numeric frames for vast distances.
6. Close `ter-fa7j` and `ter-r90w` with their scripted runs, and move the decisions into ARCHITECTURE.md.

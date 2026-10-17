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

## Frame owners in a numeric frame

The ship's frame follows its hull, and the hull lives in the system of the ship frame's tree parent. When the hull joins a numeric frame, the ship frame moves under that numeric frame in the tree. Its semantic parent stays earth. A person who leaves the ship's bubble hands off to the ship frame's tree parent, which is then the numeric frame. The person's row frame becomes earth, as it does today.

Every frame carries its children through the tree, so no force has to carry them. The ship's contents live in the ship's own Jolt system, at small positions relative to the ship. They move with the ship by composition, as they do today. When the ship frame moves under a falling numeric frame, the ship and its contents move with that numeric frame in the same way. Only the ship's proper acceleration, such as its thrust, acts on its contents, as one uniform force. Friction and inertia never have to drag cargo along, so they cannot make it jitter. The rules can also make a numeric frame inside the ship. That frame moves with the ship for the same reason.

## Bubbles and ghosts

An anchor is a static body or the hull of a frame that physics does not drive, such as a parked station's hull. It never moves between frames. A ghost is a kinematic copy of a body in the Jolt system of another frame, so that the bodies there collide with it. A numeric frame has no bubble, so it does not change which bubbles a body, a hull or an anchor meets. Every one of them gets the same hand-offs and the same ghosts in semantic frames as in the same scene without numeric frames. (User) Physics finds the bubbles through the numeric frames in three places:

- A body in the system of a frame meets the bubbles of the semantic children of that frame's semantic frame, wherever they hang among its numeric frames. It enters the deepest one that it touches, or it has a ghost in each one that it touches without entering, for example a hull that does not fit. A person in a falling frame enters a parked station, and a fast ship's hull has a ghost in a station that is too small for it.
- An anchor never enters a bubble, but a ghost is about touching, not entering, so the hull of a frame that physics does not drive meets the same bubbles. A static body that is not a hull has no ghost in a bubble, with numeric frames or without. A parked station's hull lies in the system of the station's semantic parent. A ship that falls past it hangs under a falling frame, and the hull has a ghost in the ship while their bubbles overlap. So the crates deep inside the ship hit the station's hull.
- A body that sticks out of its frame's bubble has a ghost in the frame's tree parent, which can be a numeric frame. The body is partly in that parent, so it also meets the bubbles that the members of the parent's system meet: the semantic siblings of its frame, wherever they hang. A crate that sticks out of a falling ship pushes a crate in a parked station beside it, and a crate that sticks out of the station stops a crate in the ship.
- When that tree parent is a numeric frame, it holds only the bodies that the merge test links to the frame's hull. The merge test reaches the bounding spheres plus 2 s of relative speed, not the bubble. The frame's semantic parent keeps the other bodies in its own system, so the body that sticks out also has a ghost there (`ter-wllb`). A crate that sticks out of a falling ship pushes a body at rest in space 200 m below the hull's path, and a crate that sticks out of a ship that rests in a resting frame stops a body that flies at it. A nesting frame between the two holds no bodies, so it gets no ghost.

The place and velocity of each bubble's parent relative to the body's system are sums through the numeric frames, in doubles, and so are those of the semantic parent relative to the tree parent. The hand-off uses instant velocities, the velocities at the end of the step. The ghosts use chord velocities, the change of position over the last step divided by its length. Jolt's velocity of a body is its chord, and so is the velocity that the World reports, so a ghost moves as its real body does. In free fall the two kinds of velocity differ by half a step of gravity. A falling frame also keeps a ghost of each anchor near its members, because an anchor stays in its own system while the bodies near it fall (see the "Anchors" row in `ARCHITECTURE.md`, "Numeric frames"). The reconcile allows a ghost in exactly these frames and those falling frames, so it drops none of them. No frame gets two ghosts of one body. Those falling frames are numeric frames of the anchor's own semantic frame, which no other ghost enters, no bubble is numeric, and the semantic parent is an ancestor of the body's frame, so it is no bubble that the body touches.

A ghost is kinematic, so only a body that moves collides with it. Jolt makes no contact between a kinematic body and a static one unless a body asks for it, and the engine never asks. So a static rock beside a ship never meets the ghost of a crate that sticks out of the ship, with numeric frames or without.

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
- Static bodies of a semantic frame stay in its own system. A fast numeric frame that passes one needs a ghost of it, which moves fast relative to the numeric frame. This is the same limit as above.
- A read in a distant frame rounds at that distance. An example is the position of a ship near another star, read in the frame of the sun. Reads between nearby entities do not round.

## Tests

- `ter-fa7j`: across a minute of thrust in the frame earth, the person's sideways drift stays under 1 mm/s.
- `ter-r90w`: a person behind a body along the orbit, 1.2 m to the side, passes it.
- A crate released near the ship follows the orbit that its starting state gives in earth, to within a millimeter after ten minutes. This proves the tidal gravity.
- Every gameplay surface in the list above shows the same frames with numeric frames on and off.
- Two runs with the same input give the same clusters, in the same order.
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

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
| How a numeric frame moves | Integrated free fall in doubles, in the semantic frame's field. (User) | It works in any field, and numeric frames can nest later. A drift from the true orbit is harmless, as the section on motion shows. A Kepler orbit needs new code that turns a state into orbit elements, and it blocks nesting. |
| Reads of positions and velocities | Every read names its frame. No read without a frame remains. (User) | A read can be about any entity, so it must say in which frame it wants the answer. No read can then be ambiguous, and what the row stores becomes an engine detail. A caller can name only semantic frames, so no read answers in a numeric frame. |
| Velocity storage | Jolt's float local velocity, plus the double frame velocity of the physics home. A read adds them in doubles. (User) | Jolt keeps its small float, and reads are exact. |
| Writes | Relative to `self.Frame`, as today. Physics converts a write at the moment that it applies it. (User) | An entity writes only itself, so its own frame is the natural frame for a write. |
| Directions | In `self.Frame`'s axes, as today: forces, impulses, torques, the walk velocity, contact normals, gravity and up. (User) | A direction depends only on axes, and numeric frames never rotate, so a direction cannot leak a numeric frame. |
| Rotation | None. A numeric frame keeps the axes of its semantic frame. | No Coriolis or centrifugal terms appear, and vectors need no rotation between the two frames. |
| Bubble | None. Membership is a list of bodies. | A numeric frame never sweeps up bodies as it moves, so it leaves no wake. |
| Owner | None. A numeric frame has no owner entity. | An owner entity shows in entity lists, broadcasts, dumps and handle numbers. |
| When a cluster drifts | The engine drains in place. It splits off only the members that leave the cluster. (User, for the split. The drain goes with it.) | A uniform shift of position and velocity keeps Jolt's contact cache and warm start. A move to another Jolt system loses both. |

## How a numeric frame moves

A numeric frame falls freely in the field of its semantic frame. Without free fall, the 8.7 m/s² of Earth's gravity at the ship's orbit enters every member's float, and the drains never stop.

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

If a body's speed relative to its semantic frame passes a threshold, the body gets a numeric frame. No content flag is needed. Inside the ship, on a planet's surface and in any slow frame, nothing changes.

### Merge before contact

Bodies in two different Jolt systems cannot touch. So two numeric frames merge before any of their members can collide. The test looks ahead in time: it grows each member's bounds by its velocity relative to the other frame, times a lookahead time. Space Engineers grows its bounds by a distance in meters, but at orbital speed a fixed distance is crossed in a fraction of a second.

A numeric frame also takes in a body of its semantic frame by the same test. Examples are the ship's hull and a crate that a person throws out of the ship.

### Split with hysteresis

Two conditions split a member, or a group of members that touch or are joined, off into a new numeric frame. First, it is farther from the other members than the merge test reaches, plus a margin. Second, this stays true for a cooldown time. The margin and the cooldown stop merge and split thrashing, a problem that Kitten Space Agency reported with a plain sphere test.

### Drain

At each sync point, the engine looks at the members' local velocities. If their common part, weighted by mass, passes a limit, the engine adds it to the frame velocity and subtracts it from each member. Jolt has no call that shifts a whole system, so the drain sets each body's velocity, one body at a time.

A sleeping body has a local velocity of exactly zero, and a nonzero velocity wakes it. So sleeping members count with a velocity of zero in the common part, and the drain runs rarely. If the members drift far from the frame's origin, a drain also moves the origin to their center of mass.

### Collect

A numeric frame with no members goes away. A numeric frame can also become slow relative to its semantic frame. If its speed falls below a second threshold, its members go back to the semantic frame's own system. The frame then goes away.

### Starting values

These values are guesses. Tests tune them.

| Value | Start |
|---|---|
| Speed to get a numeric frame | 50 m/s |
| Speed to give it up | 20 m/s |
| Lookahead time of the merge test | 2 s |
| Split margin beyond the merge test | 10 m |
| Split cooldown | 1 s |
| Common local speed that starts a drain | 1 m/s |

## Moving bodies between Jolt systems

A move to another system loses Jolt's contact cache, warm start and constraint impulses, and Jolt recreates a character. So:

- The engine moves a whole group of members that touch or are joined together, never half of it.
- It adds sleeping bodies without waking them.
- It carries the held contact pairs of the group, so a contact that goes on is not reported again.
- No move between numeric frames, or between a numeric frame and its semantic frame, sends `FrameChanged` or changes a topic membership.

## Frame owners in a numeric frame

The ship's frame follows its hull, and the hull lives in the system of the ship frame's tree parent. When the hull joins a numeric frame, the ship frame moves under that numeric frame in the tree. Its semantic parent stays earth. A person who leaves the ship's bubble hands off to the ship frame's tree parent, which is then the numeric frame. The person's row frame becomes earth, as it does today.

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
- Nested numeric frames, for distances between stars, are not part of this plan. The integrated free fall leaves the way open for them.

## Tests

- `ter-fa7j`: across a minute of thrust in the frame earth, the person's sideways drift stays under 1 mm/s.
- `ter-r90w`: a person behind a body along the orbit, 1.2 m to the side, passes it.
- A crate released near the ship follows the orbit that its starting state gives in earth, to within a millimeter after ten minutes. This proves the tidal gravity.
- Every gameplay surface in the list above shows the same frames with numeric frames on and off.
- Two runs with the same input give the same clusters, in the same order.
- The scripted spacewalk thrusts for several seconds in earth, not only 0.4 s.

## Order of the work

The tickets hold the order. In short:

1. Split the row into a semantic frame and a physics home. Replace every read without a frame by a read in a frame, make velocities `Double3`, and convert writes at apply time. Every body's home is still its semantic frame.
2. Add numeric frames to the frame tree: ownerless, hidden from the public surface, moved by integrated free fall, with tidal effective gravity in doubles and a Jolt system each.
3. Move groups of bodies between a semantic frame and its numeric frames, with no events. Move a frame owner's frame under the numeric frame of its hull.
4. Cluster automatically: create, merge, split, drain and collect.
5. Close `ter-fa7j` and `ter-r90w` with their scripted runs, and move the decisions into ARCHITECTURE.md.

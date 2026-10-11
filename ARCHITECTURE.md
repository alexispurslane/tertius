# Tertius architecture

Tertius is an engine for immersive sims and space sims. It combines a Caves of Qud style entity model with nested reference frames, double-precision physics and a deferred Vulkan renderer. This document records the design decisions. Each module must follow it. If a module must break a rule here, the change goes into this document first.

## Scope

These features are out of scope. Do not design for them.

- Multiplayer of any kind.
- Time warp. Every frame steps at the same fixed rate.
- Mods. The design keeps them possible, but no mod loading exists yet.
- Coriolis and centrifugal forces. Only linear (proper) acceleration of a frame acts on its contents.

## Toolchain

| Item | Choice |
|---|---|
| Language | C# 15 preview on .NET 11 (`LangVersion` `preview`) |
| Dev shell | `nix develop` (see `flake.nix`) |
| Physics | Jolt 5.6.0 through JoltPhysicsSharp 2.23.0, double-precision build `libjoltc_double` |
| Graphics | Vulkan 1.3 through Silk.NET 2.23.0, MoltenVK on macOS |
| Windowing and input | Silk.NET.Windowing and Silk.NET.Input (GLFW) |
| Shaders | GLSL 4.60, compiled to SPIR-V with `glslc` at build time |
| Tests | xUnit v3 |

You build the Jolt library with `native/build-joltc.sh`. The script pins the joltc commit and the Jolt tag. `Directory.Build.targets` copies the library next to the output of every project that sets `<UsesJolt>true</UsesJolt>`.

## Projects and dependency rules

```
src/Tertius.Core        math, handles, messages, PoseTree, FrameTree data, physics and render seams
src/Tertius.Simulation  entities, components, scheduler, router, sync point, snapshots, Kepler, blueprints
src/Tertius.Physics     Jolt backend: one PhysicsSystem per frame, intents, gravity, hand-off, ghosts
src/Tertius.Rendering   Vulkan deferred renderer: G-buffer, depth ranges, lighting, shadows, impostors
src/Tertius.Host        threads, window, input, frame pacing, wiring of all modules
src/Tertius.Demo        the vertical slice game
tests/*                 one test project per module
```

- `Tertius.Core` depends on nothing but the base library.
- `Tertius.Simulation`, `Tertius.Physics` and `Tertius.Rendering` depend only on `Tertius.Core`. They never reference each other.
- `Tertius.Host` references all modules and connects them through the seams in `Tertius.Core`.
- `Tertius.Demo` references `Tertius.Host`.

## Threads and the tick

The simulation runs at a fixed 60 ticks per second. Physics steps once per tick with the same time step.

| Thread | Work |
|---|---|
| Main | The window and input. macOS requires this. |
| Gameplay workers | Entity runs, in chunks of 64 entities, through `Parallel.For`. |
| Physics | One Jolt step for every active frame. Jolt uses its own job threads. |
| Render | Draws the latest render snapshot and interpolates between the last two. |

One tick has two parts. During the parallel part, gameplay and physics run at the same time. Both read only state that is fixed for the tick: the front snapshot of tick N-1 and the frame tree. During the sync part, one thread merges the results.

The sync point does these steps in this order:

1. Wait for all gameplay chunks and for the physics step.
2. Check the physics output before any of it lands. If an output can only come from a bug in the backend, skip the physics update of each entity that it names. "Output that only a bug produces" under "Physics" gives the checks. Then move each handed-off frame under its new parent in the frame tree. Copy body poses, velocities and hand-offs into the back transform table, and then apply the new states of physics-driven frames. A frame state is relative to the frame's parent after its hand-off. So it must come after the hand-offs, and before step 5 destroys any frame. Send the contacts as `Collision` events last.
3. Make sure that no entity with a body wrote its own transform. This is a debug assertion.
4. Swap the physics intent buffers, so that physics gets this tick's forces in the next step.
5. Free dead entities. Increment the generation of each slot and return the slot to the free list. Tell physics to destroy their bodies.
6. Make new entities live. If the tables are full, grow them. Tell physics to create the new bodies.
7. Advance the Kepler frames to the end of the tick.
8. Route the outboxes into inboxes.
9. Swap the snapshot. The back tables become the front tables. Copy only dirty slots forward.
10. Build the render snapshot and publish it to the render thread through a triple buffer.
11. Start the next tick.

Gameplay intents written during tick N reach physics in step N+1. That is one tick of latency, and it is acceptable.

## Entities

An entity is a 64-bit handle: a 32-bit slot index and a 32-bit generation. The generation increments every time the slot is freed. A handle with an old generation is stale, and every lookup must reject it.

Each live entity has a row in three parallel tables, indexed by the slot index:

- Transform: the frame that the entity is in, a frame-local `DPose`, and linear and angular velocity. Positions are double, because Jolt gives double positions and a planet frame is millions of meters across.
- Physics: an optional body description and the double-buffered `PhysicsIntent`.
- Render: mesh, material, scale, bounding radius, render flags and an optional light.

Each entity also has a list of components, which are classes that implement `IComponent`. The list is sorted by `Priority`. Components with the same priority keep the order in which they were added.

A frame is an entity that also has a row in the frame tree. `FrameHandle` holds the entity handle plus the frame index and generation. It converts to `Entity` implicitly.

### Component rules

- A component changes only its own state and the tables of its own entity.
- An entity never changes another entity. It sends a message.
- An entity reads other entities only through the front snapshot of the previous tick. It never reads another entity's components.
- A component never stores a `ref`, a span, an `EventList`, an `Outbox` or a `Self` beyond the call that received it. The compiler enforces this for ref structs.
- `Handle` never awaits. For a delay, a component sends a message to itself with a delay.

### Ownership of transforms

If an entity has a physics body, physics owns its transform. Gameplay moves it only through `self.Body`: forces, impulses, velocity changes and kinematic targets. These go into the entity's `PhysicsIntent` slot. Forces and impulses add up during the tick. Velocity changes and kinematic targets keep the last value written.

If an entity has no body, gameplay owns its transform and writes it through `self.Transform`.

### Creation and destruction

`self.Spawn(...)` takes a slot at once from an atomic free list and returns the new handle. The new entity starts to run on the next tick. The tables grow only at the sync point. An entity can destroy itself with `self.Destroy()`. To destroy another entity, send it a `Destroy` message. Both take effect at the sync point.

No other operation is deferred. There is no general command buffer.

## Messages

### The event envelope

```csharp
public struct Event
{
    public Target Target;         // [Union]: Entity | Topic | Broadcast
    public Entity Sender;
    public int Type;              // EventType<T>.Id of the payload struct
    public PayloadBytes Payload;  // 48 bytes, holds one unmanaged payload struct
    public object? Extra;         // rare managed data
}
```

A payload is an `unmanaged` struct that implements `IEventPayload` and is at most 48 bytes. `EventType<T>.Id` gives each payload type a dense integer the first time the type is used. The engine supports 512 event types. `EventMask` is a 512-bit set of event types.

`Target` is a custom C# 15 union with three cases: `Entity`, `Topic` and `Broadcast`. It implements `HasValue` and `TryGetValue`, so pattern matching never boxes.

### Delivery

There are three delivery modes:

- Direct: to one entity handle.
- Topic: to every subscriber of a topic. Every frame is a topic. An entity subscribes to its frame's topic automatically, and the subscription moves with it on hand-off.
- Broadcast: to every live entity.

A radius query is not a delivery mode. A component queries the spatial index of the snapshot and then sends direct messages.

Every message between entities arrives on the next tick. A question and its answer take two ticks.

### Outboxes, routing and order

Each chunk of 64 entities owns an outbox for the tick. Outboxes belong to chunks, not to worker threads, because the chunk-to-thread assignment changes from run to run. At the sync point, the router does these steps:

1. Join the outboxes in chunk order.
2. Expand each target into recipients: one for direct, the subscribers for a topic, every live entity for broadcast.
3. If the event type is not in a recipient's entity mask, drop that recipient.
4. Write `(recipient, eventIndex)` pairs. The payload is stored once.
5. Sort the pairs with a stable counting sort by recipient index.

Each inbox then has the same order in every run with the same inputs. Delayed messages wait in a timer queue keyed by tick, and they join the routing on the tick that they are due.

### The component pipeline

```csharp
public interface IComponent
{
    int Priority { get; }
    EventMask Wants { get; }
    void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox);
}
```

When an entity runs, the worker copies the entity's inbox into a scratch `EventList`. The worker then calls `Handle` on each component in priority order. Components edit the list in place: change payloads, remove events and add events. An event that a component adds is visible only to the components after it. Messages for other entities go into `outbox`, not into the list.

If a component's `Wants` mask has no type in common with the current list, the worker skips that component. Each entity caches the OR of the `Wants` masks of its components. After a component is added or removed, the entity recomputes the cache. After a component is attached, it must not change `Wants`.

An entity runs in a tick only in two cases: its inbox is not empty, or its mask contains the engine `Tick` event. In the second case, the scheduler adds a `Tick` event to the start of the list.

`EventList` has a small query API that reads like LINQ but allocates nothing and edits in place:

```csharp
foreach (ref var damage in events.Of<Damage>())
    damage.Amount = MathF.Max(0, damage.Amount - rating);
events.RemoveAll(static (ref Damage d) => d.Amount == 0);
```

Use normal LINQ outside hot paths.

### Containers

Events do not move to inventory or equipment automatically. A `Container` component can forward an event on purpose.

### Engine events

The engine defines these payload types in `Tertius.Core`:

| Event | Sent on |
|---|---|
| `Tick` | Every tick, to entities whose mask contains it |
| `Spawned` | On the first tick of a new entity |
| `Destroy` | A request to destroy the recipient |
| `FrameChanged` | After a hand-off moved the entity to another frame |
| `Collision` | Physics reported a contact that started |
| `InputEvent` | The host forwards input to the entities that subscribe to the input topic |

## Snapshots

The transform, physics and render tables are double-buffered. During a tick, everyone reads the front buffer. Each entity writes only its own row of the back buffer. At the sync point, the back buffer becomes the front buffer. Only slots that changed are copied forward. An entity that did not run costs nothing.

The snapshot also holds one spatial grid per frame for radius queries. The sync point rebuilds the grid of each frame.

## Reference frames

### The frame tree

Frames form a tree. Each frame has a double-precision `DPose` relative to its parent. `FrameTree` stores frames in a `PoseTree<DPose>` plus parallel arrays:

| Array | Meaning |
|---|---|
| `Owner` | The entity that is the frame |
| `Bounds` | The ellipsoid radii of the bubble, in frame axes |
| `Driver` | `MotionDriver.Kepler`, `MotionDriver.Physics` or `MotionDriver.Static` |
| `Orbit` | Kepler elements, for Kepler frames |
| `Gravity` | The gravity field of the frame |
| `Velocity` | Linear and angular velocity relative to the parent |
| `ProperAcceleration` | The acceleration that an accelerometer at the frame origin reads |

The bubble of a frame never changes after the frame is created. The simulation alone changes the driver, the orbit and the gravity field of a frame, through the `World` methods in "Motion drivers" and "Gravity". `FrameTree.Orbit`, `FrameTree.Gravity` and `FrameTree.Bounds` return copies, so no other code can change them.

`PoseTree<TPose>` is packed. Its arrays have no gaps. Each parent comes before its children, so one linear pass computes all poses. A reparent can break that order. The tree then marks itself dirty and restores the order before the next pass.

The scene graph inside a frame uses the same `PoseTree`, with the float `Pose`.

### Motion drivers

- Kepler: the pose is a function of time, computed in doubles from the orbital elements and a spin rate. The frame is in free fall, so its proper acceleration is zero.
- Physics: the frame's hull is a dynamic body in the parent frame's physics system. Its pose and velocity come from that body after each step. The proper acceleration is the velocity change over the step minus the gravity that the parent applied.
- Static: the pose does not change.

A game gives a frame a new driver between ticks with `World.SetFrameDriver`, for example to park a ship. `FrameTree.SetDriver` is internal to the simulation. The world refuses the call during a tick, because physics reads the frame tree on its own thread then. A frame that gets the static driver stops where it is: both of its velocities become zero. It keeps its proper acceleration, so the cargo of a ship that is parked on the ground keeps its weight. A frame that gets the Kepler driver moves to its place on its orbit at once, as it does for a new orbit. So physics never drives its hull across the gap from its old place. A frame that gets the physics driver keeps its pose and velocities until physics moves it. The transform of the frame's owner follows the frame at once.

The world refuses a driver that `MotionDriver` does not define, and a driver that the frame cannot follow. The physics driver needs a dynamic hull, which has mass and is not a mesh. Jolt never moves a mesh. The Kepler driver needs a hull that is not a mesh, and a valid orbit. A valid orbit is closed, with a positive semi-major axis around a parent with a positive mass, and each of its elements is finite. The world refuses the same hulls and orbits at spawn.

A root frame has no parent, so its owner has no hull. The body of its owner is only the static interior of the frame, so the rules for a hull do not apply to it. So if the owner of a frame that has a parent and the physics driver has a body, that body is a dynamic hull.

Each call to `World.SetFrameDriver` changes the version of the frame tree, also a call that gives a frame the driver that it has. Physics then follows the call at the start of its next step, as it follows a reparent. The hull keeps its body, and it gets the motion that the driver needs: dynamic for the physics driver, kinematic for the Kepler driver, and static for the static driver. It starts from the frame's pose and velocity in the tree. So a park and an unpark between two ticks stop the ship.

A driver that the hull cannot follow comes only from a bug. It breaks a contract in a Debug build. A Release build keeps the motion of the hull and records the bug in `World.Errors`. "Output that only a bug produces" under "Physics" tells how.

A game gives a frame a new orbit between ticks with `World.SetFrameOrbit`. The world refuses an orbit that is not valid, also for a frame with another driver. A Kepler frame moves to its place on the new orbit at once, and the transform of its owner follows. `FrameTree.SetOrbit` is internal to the simulation. Like a new driver, it changes the version of the frame tree. So physics puts the hull on the new orbit before its next step.

### Gravity

Each frame has one gravity field: `None`, `Uniform` (artificial gravity, with an optional region box) or `Point` (a planet). An object feels only the field of the frame that it is in. This is the one-dominant-body rule.

The effective gravity on a body inside a frame is the frame's own field minus the frame's proper acceleration, rotated into frame axes. As a result, loose objects inside an accelerating ship slide toward the stern.

A game gives a frame a new gravity field between ticks with `World.SetFrameGravity`. The sleeping bodies of the frame wake at the next step. The world refuses a field that is not valid, at this call and at spawn. Such a field has a kind that `GravityKind` does not define, or a value that is not finite. A negative G·M is not valid either. `FrameTree.SetGravity` is internal to the simulation.

### Bubbles and nesting

A frame's bounds are an ellipsoid or a sphere. No other shape is allowed. The bubble decides ownership: which frame an object belongs to. The physical shape of a frame in its parent frame is its real hull. This lets a ship land on a planet with its real hull.

A frame that fits inside a sibling frame enters it, like any other object. A frame fits inside another frame in one case: its largest bubble radius is less than half of the smallest radius of the other bubble. A fighter that flies into a carrier's bubble becomes a child of the carrier. Two frames that do not fit inside each other stay siblings. Their real hulls collide in the parent frame. If their bubbles overlap, objects in the overlap stay in the frame that they were in, and each frame's hull is ghosted into the other.

Docking two frames of similar size merges them explicitly into one frame, and undocking splits them again.

### Destroying a frame

When a frame dies, its contents move to the nearest ancestor frame that survives. Pose and velocity convert through the tree, each moved entity gets `FrameChanged`, and its body moves to the new frame's physics system. Child frames move up the same way. If no ancestor survives, as with a root frame, the contents die with it.

### Hand-off

An object belongs to the deepest frame whose bubble it is in. These rules control its moves between frames:

1. The first contact of its bounding sphere with a child's bubble moves it into the child frame. The hull is inside the bubble. So the object is in the child frame before any part of it can reach the child's real hull.
2. It leaves a frame only after its bounding sphere is fully outside the bubble, plus a margin. The margin is the larger of 2% of the bubble radius and the object's radius. This gap stops the object from moving back and forth every tick.
3. If it touches two sibling bubbles at once, it enters the one that it penetrates more deeply.
4. Physics checks after each step, on the physics thread.
5. The pose converts in double precision. Velocity converts as `v' = R⁻¹(v − v_frame − ω × r)` on entry, and the reverse on exit. No Coriolis force acts, but the conversion keeps the ω × r term. An object thrown out of a spinning station then leaves with the correct velocity.
6. The entity handle does not change. The body moves from one physics system to the other. The entity moves to the new frame's topic and gets a `FrameChanged` event.

Physics follows the frame tree, and the tree is the authority. Each hand-off is a proposal that the sync point checks and applies in step 2. A correct backend never proposes a hand-off that the tree refuses, so a refusal comes only from a bug. "Output that only a bug produces" under "Physics" tells what the sync point does then.

At the start of every step, physics reconciles with the tree. First it drops every hand-off of a frame from the last step, whether the tree took it or not. Then it puts the hull of each frame into the physics system of the frame's parent in the tree. A hull that changes systems this way restarts from the frame's pose and velocity in the tree. So after a refused hand-off, the hull loses the motion of the refused step. The same reconcile follows the changes that the game makes to the tree between ticks. Examples are a reparent that docks a ship and a new driver that parks it. Physics also reconciles each time that the simulation gives it a batch of structural changes.

If the system of the parent is full, that system grows first. "Physics systems that grow" under "Physics" tells how. So every hull gets its body in the system that the tree names.

### Ghosts

While an object's bounding sphere crosses a bubble boundary, the other physics system holds a ghost. A ghost is a kinematic body that copies the real body's pose every step. Bodies in the other system collide with the ghost, but the ghost does not push back on the real body. After the object is fully on one side, physics removes the ghost.

A ghost is only in the system of a frame next to the body's frame in the tree: its parent, a sibling or a child. The ghost of a hull is never in the hull's own frame. When the tree changes, the reconcile at the start of the next step removes each ghost that the new tree does not allow. The ghost pass at the end of that step adds the ghosts that the new tree needs.

## Physics

Tertius uses Jolt in double-precision mode. Each frame that contains bodies has its own `PhysicsSystem`. A frame with no awake bodies is not stepped.

The backend owns each native Jolt object that it creates, and it frees each one in the order that Jolt needs. For example, it frees a character before the system of the character. JoltPhysicsSharp does not mark every native object as owned, so the backend marks each one itself. The C API of Jolt frees the layer filter tables of a system with the system, so each system has its own tables.

Object layers:

| Layer | Collides with |
|---|---|
| `Static` | `Moving`, `FrameHull` |
| `Moving` | `Static`, `Moving`, `Ghost` |
| `FrameHull` | `Static`, `FrameHull` |
| `Ghost` | `Moving` |

A frame entity has two bodies. In the parent system, its hull is a body on the `FrameHull` layer. In its own system, the same hull shape is static geometry on the `Static` layer. This includes the exterior surfaces, so that a person on a spacewalk can stand on the hull. Objects inside do not push the hull body.

Spawn refuses every body that physics cannot create, before anything changes. `BodyDesc.Problem` and `ShapeDesc.Problem` in `Tertius.Core` hold these rules, and the World and the backend use the same ones. `IPhysicsBackend.ShapeProblem` adds what only the backend can tell, for example a convex hull whose points coincide. So a change batch holds only bodies that physics can create.

Physics turns some values into NaN in its first step, so spawn refuses them too, for every entity. A position or a velocity must be finite. A rotation must be finite and not zero. A mass must be 0 or a finite positive number of at least 1.2E-38, because Jolt divides by it. Friction, restitution and damping must be finite. `new DPose { Position = p }` has the identity rotation. Only `default(DPose)` has the zero rotation, and spawn refuses it.

Gravity is a force that physics applies to each awake dynamic body before each step. The Jolt system gravity is zero. The force does not wake a sleeping body, and it does not reset the sleep timer of an awake body.

If three points of a body stay in a small sphere for 0.5 s, Jolt puts the body to sleep. The radius of the sphere is the sleep threshold, a speed, times 0.5 s. Jolt's own threshold is 0.03 m/s.

Take a body that a net acceleration moves from rest. If the body does not cross the sphere in its first 0.5 s, Jolt puts it to sleep. To stay awake, the net acceleration must be more than 8 per second times the threshold. With Jolt's own threshold, that is 0.24 m/s², which is more than all of a weak gravity.

So each physics system has a sleep threshold that follows the weakest effective gravity on its awake dynamic bodies. At standard gravity (9.81 m/s²) and above, the threshold is Jolt's own 0.03 m/s. Below standard gravity, it falls in proportion to the gravity, down to 3.1 µm/s at 0.001 m/s². It does not fall further. So a body whose net acceleration is more than 2.4 % of the gravity stays awake, in every gravity up to standard gravity.

This keeps a ball on a ramp moving, and a box that tips over a ledge. It also keeps cargo moving that slides on the deck of a thrusting ship, and a body that falls slowly against its damping. A body at rest on something falls asleep, and it costs nothing until something wakes it. A falling body stays awake, and so does a stack of bodies that falls together. Without gravity, a body that drifts keeps drifting until it moves slower than 6 µm/s.

The sleep rule has these limits:

- A body that a net acceleration below 2.4 % of the gravity moves from rest can fall asleep, and it stays there. Above standard gravity, the limit is 0.24 m/s². An example is a ball on a ramp of 0.5° in 9.81 m/s², whose net acceleration is 0.6 % of the gravity.
- A body whose motion turns around under a net acceleration below 9.8 % of the gravity can fall asleep at the turning point. An example is a ball that rolls up a ramp of 5°, whose net acceleration is 6.2 % of the gravity.
- Below 0.001 m/s², the threshold does not fall further. There, a body that a net acceleration below 0.000024 m/s² moves from rest can fall asleep.
- One system has one threshold, which its weakest gravity sets. An example is one awake body far out in the frame of a planet, where the gravity is weak. It lowers the threshold for every body of that frame, so bodies at rest there take longer to fall asleep.
- Jolt puts a group of bodies that touch to sleep only all at once. In the benchmark, a heap of 400 boxes that fell and tumbled in 9.81 m/s² took about 55 s to fall asleep. Jolt's own rule takes about as long.
- In weak gravity the threshold is lower, so a heap that tumbles takes longer to fall asleep. The review of round eight measured 125 s for the same heap in 1.62 m/s², the gravity of the Moon. Before round eight, it took 76 s there.

If a body that Jolt simulates touches a sleeping body, Jolt wakes the sleeping body. Nothing else wakes it. So before physics removes a body, teleports it, or moves it without a simulated step, physics wakes the bodies around it. One example of such a move is a static hull that the game moves through the frame tree. Others are a hull that the reconcile puts in another place, and a ghost.

Physics wakes each body that is near the moved body and can touch it. Near means in the bounds of the moved body, plus 0.05 m. Can touch means that the two layers collide. If Jolt does not simulate the body at its new place either, physics wakes the bodies there too. This is the case for a static body and for a character. So if the table under a sleeping box goes, the box falls, and the bodies farther away sleep on.

If the world destroys a character, or if the character moves to another frame, the character takes its inner body out of its old system. So a box that sleeps on the head of a character that the world destroys falls. A character that walks into another frame leaves nothing behind that a body can hit.

If the gravity field of a frame changes, the sleeping bodies of the frame wake. If the proper acceleration of the frame moved more than `WakeAccelerationThreshold` away from its value at their last wake, they wake too. So if the thrust of a ship changes, even slowly, its cargo wakes.

These Jolt settings differ from the Jolt defaults:

| Setting | Value | Reason |
|---|---|---|
| Penetration slop | 0.005 m | Jolt uses 0.02 m, which is visible on small props. |
| Maximum linear velocity | 30 km/s | Orbital speeds reach about 8 km/s, and Jolt clamps at 500 m/s. |
| Motion quality | Linear cast | Fast bodies must not tunnel through thin walls. |
| Bodies per frame system | 4,096 at first | Jolt sizes its buffers up front. A full system grows to twice the size. |

Proper acceleration has a dead band. A component below the float resolution of the hull velocity, divided by the time step, reads as zero. Without it, cargo in an orbiting ship drifts on float noise.

A physics-driven hull in orbit is integrated with float velocities, so its orbit drifts slowly. A frame that coasts for a long time uses a Kepler driver instead.

The character controller is a capsule. It stands on whatever is below it and inherits that surface's velocity. It uses the frame's effective gravity. Near zero effective gravity, it switches to zero-g movement.

### Contacts

If two bodies start to touch, physics reports the contact once. It reports the contact again only after the bodies separate. Jolt finds contacts only for the bodies that it simulates, and it does not simulate a sleeping or a static body. A body can fall asleep on something static or asleep, or a hull can become static on a floor. If the rest of the system keeps stepping then, Jolt drops the contact. After the body wakes or moves, Jolt finds the contact again.

Physics holds each contact that Jolt drops. If Jolt finds a held contact again, the contact continues, and physics does not report it. A held contact ends at the first step that simulates one of its two bodies without finding the contact. For example, the game can lift a sleeping box off the floor. The box then reports the floor again at its landing. In each step of a system, physics visits every held contact of that system.

Jolt never simulates the inner body of a character. A character also keeps a gap of 0.02 m from what it touches, which is Jolt's speculative contact distance. So Jolt loses and finds the contact of a character and a box again and again while the character pushes the box. For this reason, the character's own contact list decides the end of a held contact of the character. This list holds each body within 0.1 m of the character, asleep or awake. If the list no longer has the other body, the held contact ends.

The list does not start a contact. Only a contact that Jolt finds for the inner body starts a contact with a character. The gap of 0.02 m puts that contact at the edge of Jolt's speculative contact distance, so Jolt can miss it. Physics then reports no contact for a character that stands on a box, for a ball that shoves a character, or for a slow push. Ticket ter-84co tracks this gap.

### Physics systems that grow

Jolt creates each physics system with buffers of a fixed size. So a system holds a fixed number of bodies. Ghosts and the inner bodies of characters count too. Physics never refuses a body for lack of room. It adds a body or a character to a full system in three steps. First, it builds a new system with twice the limits for bodies, body pairs and contact constraints. Then it moves every body of the old system into the new one, and it frees the old system. Last, it adds the new body. A character moves as a new native character in the new system. A native character that physics frees takes its inner body out of its system. So physics frees the old native character before the old system. This happens in one place, so every path that adds a body gets it: a new body, an interior, a ghost, a hand-off, and a hull that the reconcile moves.

The move keeps the state of each body. A rigid body keeps its ID, its shape, motion type, layer, mass, material and user data, its pose and velocities, and whether it sleeps. A character keeps its pose, velocity and up direction, and it finds its ground again. Each pair of bodies that touches stays in contact, so physics does not report the contact again. The new system starts with no contacts, so physics holds each pair, as it holds a contact that Jolt drops (see "Contacts"). This holds for any number of growths. Two things stay behind, because the C API of Jolt cannot save them. The first step in the new system solves its contacts without the impulses of the last step. Also, the sleep timer of each body starts again.

The bodies move in the order of their IDs, and the characters after them. So two runs that grow at the same tick stay the same, bit for bit. A growth costs one pause, in proportion to the number of bodies in the frame, each time that the frame doubles. Physics writes a warning each time. A frame that is known to be large can start larger: raise `MaxBodiesPerFrame` in the backend options.

### Output that only a bug produces

A state that only a bug can produce breaks a contract in a Debug build. A Release build skips it and records it in `World.Errors`. A contract that the physics backend breaks is a bug too. It escapes `World.Tick` on the ticking thread, with the stack of the physics thread, and the world does not record it as a failed step.

The physics backend reports each such state that it finds in the `Errors` list of the change batch or of the step output. Examples are a frame whose driver its hull cannot follow, and a change that the backend cannot apply. The World records each message in `World.Errors`. Spawn refuses every body that physics cannot create, so a change that fails in the backend comes only from a bug. The backend leaves nothing of that change behind, and it applies the rest of the batch.

For physics output, the facts below define a correct backend. Entities and frames die only in step 5 of the sync point or outside a tick. Physics hears of each death before its next step. An entity keeps its body for its whole life, because a full physics system grows. Each hull starts a step in the system of its frame's parent. So its frame can only enter a sibling or leave for the parent's parent. Physics moves a frame only through the hull of a frame with the physics driver. The world refuses that driver for a frame with a parent whose hull cannot be dynamic, at spawn and at a driver change. So that hull is a dynamic body. Physics follows a new driver at the start of its next step. So the hull of a frame with another driver is static or kinematic. Physics never hands such a hull off. A root frame has no parent, so its owner has no hull.

So step 2 of the sync point checks each output before any state lands:

1. A body state names a live entity that has a body, and a live frame.
2. A hand-off names a live entity that has a body, and a live target frame. It leaves the frame that the entity is in at the start of the step. For the owner of a frame, that is the frame's parent in the tree. The owner of a root frame is in no frame, so it never hands off. If the entity owns a frame, that frame has the physics driver. The target is a different frame. Each entity has at most one hand-off in a step.
3. The hand-offs of a step, applied in their order, never put a frame under itself or under a frame inside it.
4. A frame state names a live frame with the physics driver and a parent. The owner of that frame has a body, which is the hull. So a frame state never names a root frame.
5. A contact names two different live entities that have a body, and a live frame.

The class `PhysicsOutputCheck` in `Tertius.Core` holds these checks. The sync point uses it. The physics tests use it too, in the class that stands in for the sync point. So the tests apply the same rules as the engine.

If an output fails a check, a Release build skips the whole physics update of each entity that the output names, for that step. For a frame state, the entity is the owner of the frame. The whole update of an entity is its body state, its hand-off, the state of its own frame, and its contacts. The sync point records exactly one error for each output that fails. An output that fails more than one check still records one error. An output whose entities another failed output skipped already still records its own error.

The skip does not change physics. At the next reconcile, a frame hull goes back to where the tree says. A plain body that physics moved into a frame that is gone stays in that system. When physics finds that the frame is gone, it removes the body with a warning.

## Rendering

The renderer is a deferred Vulkan renderer. It gets a flat list of instances, each relative to the render origin, and it knows nothing about frames.

### The render snapshot

The snapshot builder runs at the sync point. It computes every frame's pose relative to the camera's frame in doubles, along the path through the common ancestor. It then writes each instance relative to the render origin, which is the camera position at the current tick. Each instance has a current and a previous transform relative to the same origin, so that the render thread can interpolate.

### Instances and draws

All instances go into one large instance buffer. Textures live in one bindless array. Draws use multi-draw indirect, with one indirect command per mesh. MoltenVK does not support `drawIndirectCount`, so the CPU sets the draw count.

### Depth ranges

There are three depth ranges, drawn from far to near into the same G-buffer. An instance belongs to every range that its bounding sphere touches, measured in view depth, so an object that straddles a boundary is drawn twice.

| Range | Distance |
|---|---|
| Far | 2,000 km to infinity (infinite reversed-Z) |
| Middle | 2 km to 2,000 km |
| Near | 5 cm to 2 km |

Each range uses an infinite reversed-Z projection. Before a range draws, the depth buffer is cleared to the depth of the range's far boundary, so the GREATER test rejects anything beyond it. A finite far plane is simpler, but on MoltenVK a triangle clipped against both the near and the far plane gets the wrong depth.

The G-buffer stores the distance from the eye in an R32F target, so one lighting pass serves all three ranges.

### Shading

The lighting model is energy-normalized Blinn-Phong, not PBR:

- The specular term is multiplied by (n + 8) / 8π.
- Schlick Fresnel.
- Normal maps.
- A gloss map that sets the exponent.
- Hemisphere ambient light.

Lighting is in linear color space, into an HDR target, with tone mapping at the end. Lighting works in world axes centered on the camera. The specular exponent is n = 2^(1 + 11 · gloss), from 2 to 4096. The Fresnel F0 is 0.16 · specular².

The G-buffer has these targets:

| Target | Format | Contents |
|---|---|---|
| Albedo | RGBA8 sRGB | Base color. Alpha 0 marks an unlit impostor pixel. |
| Normals | RGBA16 SNORM | The shading normal and the geometric normal, both octahedral-encoded, in world axes. Shadow biasing uses the geometric normal. |
| Specular | RG8 | Specular intensity and gloss. |
| Emissive | RGBA16F | Emitted light. Impostors write their baked color here. |
| Distance | R32F | Distance from the eye. Zero is sky. |

Transparent objects use one forward pass after the lighting pass, sorted back to front across ranges. They test occlusion against the distance target, not the depth buffer.

Dynamic point and spot lights are shaded in the deferred lighting pass. The sun is a directional light.

### Shadows

Shadow casters are selected by distance from the camera, not by frame. Cascaded shadow maps for the sun cover the first 2 km, with four cascades and blending at the seams. Planet shadows and eclipses use a sphere test in the shader.

The texel grid of each cascade snaps to a world-fixed anchor: the origin of the camera's frame. As a result, shadows do not shimmer as the camera moves. On MoltenVK, a steep caster clipped at the edge of the shadow map gets a flat, wrong depth plane. To avoid it, each cascade box is 16 times deeper than it is wide.

### Impostors

- Below a projected height of 200 px, an object becomes an impostor. Above 300 px, it returns to geometry.
- The impostor is captured on demand, into a 512 px texture.
- If the view direction changes by more than 2 / h radians, the renderer captures it again. Here h is the projected height in pixels. A change in sun direction uses the same rule.
- Captures go through a priority queue ordered by error, with a budget of 4 captures per frame.
- A capture uses a perspective camera at the real eye position, fitted to the bounding sphere. An orthographic capture looked wrong at the switch distance.
- A capture bakes the sun, ambient light, eclipses and point and spot lights, but not shadow-map shadows.
- A billboard writes its per-pixel depth, so impostors intersect the ground like the real object.
- The renderer keys impostor state on the entity index and the render part.

## Blueprints

A blueprint is a name, a list of component types with optional field values, and optional transform, body and render data. Reflection runs only at blueprint load time. For each component type, the loader compiles one factory delegate. Spawning then costs one delegate call per component.

## Rules for references

- A `ref` into a table never lives past one `Handle` call.
- Tables grow only at the sync point, so a `ref` taken during a tick is always valid for that tick.
- A component that needs another entity later stores its `Entity` handle and checks it on use.
- Mod support will need a registry that drops every reference to mod types before unloading. That work is deferred.

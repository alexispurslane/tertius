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
| Simulation | Runs the ticks and their sync points, and a scripted run's steps. In the window, it paces the ticks by the wall clock. |
| Render | Draws the latest render snapshot and interpolates between the last two. |

One tick has two parts. During the parallel part, gameplay and physics run at the same time. Both read only state that is fixed for the tick: the front snapshot of tick N-1 and the frame tree. During the sync part, one thread merges the results.

The sync point does these steps in this order:

1. Wait for all gameplay chunks and for the physics step.
2. Check the physics output before any of it lands. If an output can only come from a bug in the backend, skip the physics update of each entity that it names. "Output that only a bug produces" under "Physics" gives the checks. Then move each handed-off frame under its new parent in the frame tree. Copy body poses, velocities and hand-offs into the back transform table. Keep whether physics stood each character. Then apply the new states of physics-driven frames. A frame state is relative to the frame's parent after its hand-off. So it must come after the hand-offs, and before step 5 destroys any frame. Send the contacts that started as `Collision` events last, and then the contacts that ended as `CollisionEnded` events, so that a contact that starts and ends in one step reaches each entity in that order.
3. Make sure that no entity with a body wrote its own transform. This is a debug assertion.
4. Swap the physics intent buffers, so that physics gets this tick's forces in the next step.
5. Free dead entities. Increment the generation of each slot and return the slot to the free list. Tell physics to destroy their bodies.
6. Make new entities live. If the tables are full, grow them. Tell physics to create the new bodies. An attached entity becomes live in the frame of its parent at this moment, after steps 2 and 5 have moved the parent.
   - 6b. Advance the Kepler and Spin frames to the end of the tick, and move each static frame whose owner gameplay moved. The owner of each of these frames takes its frame's pose and velocities. This comes before the numeric update, because the update tests the bodies, whose rows hold the end of the step since step 2, against the bubbles of these frames, and both must describe the same moment. (Before the review of ter-2gg0, this ran in step 7, and the update placed the bubble of a station that orbits at 7.5 km/s 126 m behind the bodies beside it.) It comes after steps 5 and 6, because it mirrors the owners of the frames that they move and create.
   - 6c. Run the numeric update (`FrameTree.UpdateNumericFrames`, see "Numeric frames"), after the physics output, the destroys, the spawns and the frames of step 6b, so that it clusters every live body where it is at the end of the tick. Then hand this sync point's changes to physics.
7. The owner of each frame that physics drives takes its frame's pose and velocities, after the numeric update, which can move such a frame among numeric frames. Then every attached entity takes its frame, pose and velocities from the root of its chain.
8. Swap the snapshot. The back tables become the front tables. Copy only dirty slots forward.
9. Run the physics queries that components asked for during the tick, the ray casts, shape casts and overlap queries, in entity order. Jolt is at the end of the step, with the destroys and spawns of steps 5 and 6, and the new front snapshot describes the same moment: its component flags, collision groups, poses and frames. Queue each answer as an engine event.
10. Route the outboxes, and the answers of step 9, into inboxes.
11. Build the render snapshot and publish it to the render thread through a triple buffer.
12. Start the next tick.

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
- An entity reads other entities only through the front snapshot of the previous tick. It never reads another entity's components. The snapshot publishes each entity's component flags, the types of the components that it had, and each body's collision groups, so a component can ask whether another entity has a component, or find entities with an entity query (see docs/plans/queries-and-sensors.md). `Snapshot.TryGetBody` gives the physics facts of another entity's body: its motion, mass, shape kind, bounding radius, collision groups and mask, and whether it is a sensor. A body's description does not change after spawn, so only the collision groups and mask, which an intent can change, have double-buffered rows. The motion of a frame's hull follows the frame's driver in the frame tree, which changes only between ticks.
- A component never stores a `ref`, a span, an `EventList`, an `Outbox` or a `Self` beyond the call that received it. The compiler enforces this for ref structs.
- `Handle` never awaits. For a delay, a component sends a message to itself with a delay.

### Ownership of transforms

If an entity has a physics body, physics owns its transform. Gameplay moves it only through `self.Body`: forces, impulses, velocity changes and kinematic targets. These go into the entity's `PhysicsIntent` slot. Forces and impulses add up during the tick. Velocity changes and kinematic targets keep the last value written.

If an entity has no body, gameplay owns its transform and writes it through `self.SetTransform`, relative to `self.Frame`.

### Creation and destruction

`self.Spawn(...)` takes a slot at once from an atomic free list and returns the new handle. The new entity starts to run on the next tick. The tables grow only at the sync point. An entity can destroy itself with `self.Destroy()`. To destroy another entity, send it a `Destroy` message. Both take effect at the sync point.

No other operation is deferred. There is no general command buffer.

A spawn can attach the new entity to a parent entity with `SpawnDesc.AttachTo`. The attached entity follows its parent with a fixed offset in the parent's axes. It has no body, it cannot be a frame, and it dies with its parent. A chain of attached entities is always in the frame of its root, the one entity of the chain that is not attached. In step 7 of the sync point, each attached entity takes its frame, pose and velocities from that root. So an attached spawn becomes live in the frame of its parent at that moment, and the frame that its description names does not count. A component that spawns during a tick saw the parent in the front snapshot. In the same tick, physics can hand the parent into another frame, or the parent's frame can die and move the parent to an ancestor frame. The component cannot know that, so it is not a bug: the new entity becomes live in the parent's new frame. A parent that is not live at the sync point, or that owns a root frame and so is in no frame, cannot take the new entity. The world then drops the spawn and records an error in `World.Errors`. Outside a tick, `World.Spawn` throws for such a parent instead.

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
events.Of<Damage>().Where(static (ref Damage d) => d.Amount == 0).Remove();
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
| `CollisionEnded` | Physics reported that a contact which started ended |
| `RayHit` | The answer to a ray cast that the entity asked for in the previous tick: the entity hit, or a miss |
| `ShapeHit` | The answer to a shape cast that the entity asked for in the previous tick: the first entity that the shape touched, or a miss |
| `OverlapHit` | One answer to an overlap query that the entity asked for in the previous tick: one entity that overlaps the shape, or no entity when none does |
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
| `Driver` | `MotionDriver.Kepler`, `MotionDriver.Spin`, `MotionDriver.Physics` or `MotionDriver.Static` |
| `Orbit` | Kepler elements, for Kepler frames. They hold no G·M: the frame takes it from the gravity of its parent |
| `Spin` | The tilt and steady turn, for Spin frames |
| `Gravity` | The gravity field of the frame, a `GravityField` union (see "Gravity") |
| `Velocity` | Linear and angular velocity relative to the parent |
| `ProperAcceleration` | The acceleration that an accelerometer at the frame origin reads |

The bubble of a frame never changes after the frame is created. The simulation alone changes the driver, the orbit and the gravity field of a frame, through the `World` methods in "Motion drivers" and "Gravity". `FrameTree.Orbit`, `FrameTree.Gravity` and `FrameTree.Bounds` return copies, so no other code can change them.

`PoseTree<TPose>` is packed. Its arrays have no gaps. Each parent comes before its children, so one linear pass computes all poses. A reparent can break that order. The tree then marks itself dirty and restores the order before the next pass.

The scene graph inside a frame uses the same `PoseTree`, with the float `Pose`.

The rotation of every local pose in the frame tree is a unit quaternion: the square of its length is within `1E-12` of 1. `FrameTree.AssertInvariants` checks it. The world normalizes a rotation that a caller gives it, in a spawn description or in a write to the transform of the owner of a static frame, and it does not refuse it. A rotation that is off by `1E-7` in the square of its length would put a parent that is `6.8E6` m away about a meter off, as seen from the frame.

### Motion drivers

- Kepler: the pose is a function of time, computed in doubles from the orbital elements and the attitude. The attitude `Spin` turns the frame by its axial tilt and a steady spin about its own +Y, as a planet turns. The attitude `FacesParent` points the frame's belly (-Y) at its parent and its nose (-Z) along its motion across the horizon, as a station in low orbit flies. It turns with the true anomaly, so it is exact on an oval orbit too. The frame is in free fall, so its proper acceleration is zero. The orbit holds no G·M. It takes μ from the point gravity of the parent frame (`FrameTree.KeplerMu`), so a Kepler frame and the loose bodies in its parent fall on the same G·M. (User) G·M lives in one place only, so the two can never differ. Each `KeplerOrbit` member that needs it takes the parent's G·M as a parameter.
- Spin: the frame stays at its place in its parent and turns in place, as the ground of a planet does. Its `FrameSpin` gives the rotation as a function of time: a tilt, then a steady turn about the frame's own +Y. The same math gives the Kepler attitude `Spin`. Like a static frame, it keeps its proper acceleration: a new frame has none, and a landed ship that starts to turn keeps its cargo's weight. No centrifugal force acts on its contents.
- Physics: the frame's hull is a dynamic body in the parent frame's physics system. Its pose and velocity come from that body after each step. The proper acceleration is the velocity change over the step minus the gravity that the parent applied.
- Static: the pose does not change.

A Kepler orbit is in the axes of its parent, so in a parent that turns, the orbit turns with it. A planet that turns therefore has two frames, as real spaceflight has an Earth-centered inertial frame and an Earth-fixed one. The planet's frame does not turn: it goes round its star and holds the orbits of ships and moons. A Spin frame at its center turns and holds the ground. The orbit scene of the vertical slice is built this way.

A game gives a frame a new driver between ticks with `World.SetFrameDriver`, for example to park a ship. `FrameTree.SetDriver` is internal to the simulation. The world refuses the call during a tick, because physics reads the frame tree on its own thread then. A frame that gets the static driver stops where it is: both of its velocities become zero. It keeps its proper acceleration, so the cargo of a ship that is parked on the ground keeps its weight. A frame that gets the Kepler driver moves to its place on its orbit at once, as it does for a new orbit. So physics never drives its hull across the gap from its old place. A frame that gets the Spin driver keeps its place and turns to its spin at once. A frame that gets the physics driver keeps its pose and velocities until physics moves it. The transform of the frame's owner follows the frame at once.

The world refuses a driver that `MotionDriver` does not define, and a driver that the frame cannot follow. The physics driver needs a dynamic hull, which has mass and is not a mesh. A body with a mesh is always static, and only its ghosts move (see "Bubbles and ghosts"). The Kepler driver needs a hull that is not a mesh, a valid orbit, and a parent frame with a point gravity whose G·M is above 0. The Spin driver needs a hull that is not a mesh, and a spin whose values are all finite. A valid orbit is closed, with a positive semi-major axis, and each of its elements is finite. It has no mass of its own, so the parent's gravity gives the G·M. The world refuses a Kepler frame that has no parent, or whose parent has no point gravity, with an `ArgumentException` that names the frame and the reason. It does so at spawn, in `SetFrameDriver` and in `SetFrameOrbit`. It refuses the same hulls and orbits at spawn.

A root frame has no parent, so its owner has no hull. The body of its owner is only the static interior of the frame, so the rules for a hull do not apply to it. So if the owner of a frame that has a parent and the physics driver has a body, that body is a dynamic hull.

Each call to `World.SetFrameDriver` changes the version of the frame tree, also a call that gives a frame the driver that it has. Physics then follows the call at the start of its next step, as it follows a reparent. The hull keeps its body, and it gets the motion that the driver needs: dynamic for the physics driver, kinematic for the Kepler and Spin drivers, and static for the static driver. A kinematic hull carries the frame hulls that rest on it, for example a small ship on a turntable whose bubble is small. Loose bodies never touch a hull: they enter its frame's bubble first. It starts from the frame's pose and velocity in the tree. So a park and an unpark between two ticks stop the ship.

A driver that the hull cannot follow comes only from a bug. It breaks a contract in a Debug build. A Release build keeps the motion of the hull and records the bug in `World.Errors`. "Output that only a bug produces" under "Physics" tells how.

A game gives a frame a new orbit between ticks with `World.SetFrameOrbit`. The world refuses an orbit that is not valid, also for a frame with another driver. It also refuses an orbit for a root frame, or for a frame whose parent has no point gravity with a G·M above 0, because there is no G·M to orbit. A Kepler frame moves to its place on the new orbit at once, and the transform of its owner follows. `FrameTree.SetOrbit` is internal to the simulation. Like a new driver, it changes the version of the frame tree. So physics puts the hull on the new orbit before its next step. `World.SetFrameSpin` does the same for a frame's spin.

### Gravity

Each frame has one gravity field. `GravityField` is a custom C# 15 union, as `Target` is, so a field holds the values of its own case and nothing else. (User) The cases are `UniformGravity(Acceleration, Region)`, which is artificial gravity with an optional region box, and `PointGravity(Mu)`, which is a planet. The empty union is no gravity, and it is the default. So a uniform field cannot carry a G·M, and a point field cannot carry an acceleration. The factories `GravityField.None`, `GravityField.Uniform` and `GravityField.Point` make a field. Code reads a field with a `switch` over the cases or with `TryGetValue`, and neither boxes. An object feels only the field of the frame that it is in. This is the one-dominant-body rule.

The effective gravity on a body inside a frame is the frame's own field minus the frame's proper acceleration, rotated into frame axes. As a result, loose objects inside an accelerating ship slide toward the stern.

A game gives a frame a new gravity field between ticks with `World.SetFrameGravity`. The sleeping bodies of the frame wake at the next step. The world refuses a field that is not valid, at this call and at spawn. Such a field has a value that is not finite. A negative G·M is not valid either. `FrameTree.SetGravity` is internal to the simulation. A frame that has a Kepler child takes only a point field with the same G·M as its current one: the child orbits that G·M, and another one would make the child jump. The world refuses any other field for such a frame, with an `ArgumentException` that names the frame and the child. Its invariant check (`World.AssertInvariants`) states the rule from the other side: every Kepler frame has a parent with a point gravity whose G·M is above 0.

### Bubbles and nesting

A frame's bounds are an ellipsoid or a sphere. No other shape is allowed. The bubble decides ownership: which frame an object belongs to. The physical shape of a frame in its parent frame is its real hull. This lets a ship land on a planet with its real hull.

A frame that fits inside a sibling frame enters it, like any other object. A frame fits inside another frame in one case: its largest bubble radius is less than half of the smallest radius of the other bubble. A fighter that flies into a carrier's bubble becomes a child of the carrier. Two frames that do not fit inside each other stay siblings. Their real hulls collide in the parent frame. If their bubbles overlap, objects in the overlap stay in the frame that they were in, and each frame's hull is ghosted into the other.

Docking two frames of similar size merges them explicitly into one frame, and undocking splits them again.

### Destroying a frame

When a frame dies, its contents move to the nearest ancestor frame that survives. Pose and velocity convert through the tree, each moved entity gets `FrameChanged`, and its body moves to the new frame's physics system. Child frames move up the same way. If no ancestor survives, as with a root frame, the contents die with it. A Kepler child frame that moves up orbits the point gravity of its new parent. If the new parent has none, the Kepler frame has no G·M to orbit, so it gets the static driver and stops where it is, and the world logs it. The game caused it by destroying the parent, so it is not an error.

### Hand-off

An object belongs to the deepest frame whose bubble it is in. These rules control its moves between frames:

1. The first contact of its bounding sphere with a child's bubble moves it into the child frame. The hull is inside the bubble. So the object is in the child frame before any part of it can reach the child's real hull.
2. It leaves a frame only after its bounding sphere is fully outside the bubble, plus a margin. The margin is the larger of 2% of the bubble radius and the object's radius. This gap stops the object from moving back and forth every tick.
3. If it touches two sibling bubbles at once, it enters the one that it penetrates more deeply.
4. Physics checks after each step, on the physics thread.
5. The pose converts in double precision. Velocity converts as `v' = R⁻¹(v − v_frame − ω × r)` on entry, and the reverse on exit. No Coriolis force acts, but the conversion keeps the ω × r term. An object thrown out of a spinning station then leaves with the correct velocity.
6. The entity handle does not change. The body moves from one physics system to the other. The entity moves to the new frame's topic and gets a `FrameChanged` event.

A numeric frame (see "Numeric frames" below) has no bubble. A body whose system is a numeric frame enters and leaves bubbles as a body in the system of the numeric frame's semantic frame does, at the same places and into the same frames. A crate thrown at 60 m/s inside a ship falls in a numeric frame below the ship. When it is fully outside the ship's bubble plus the margin, it moves into the ship's parent in the tree and gets a `FrameChanged` event. (ter-kz89)

Physics follows the frame tree, and the tree is the authority. Each hand-off is a proposal that the sync point checks and applies in step 2. A correct backend never proposes a hand-off that the tree refuses, so a refusal comes only from a bug. "Output that only a bug produces" under "Physics" tells what the sync point does then.

At the start of every step, physics reconciles with the tree. First it drops every hand-off of a frame from the last step, whether the tree took it or not. Then it puts the hull of each frame into the physics system of the frame's parent in the tree. A hull that changes systems this way restarts from the frame's pose and velocity in the tree. So after a refused hand-off, the hull loses the motion of the refused step. The same reconcile follows the changes that the game makes to the tree between ticks. Examples are a reparent that docks a ship and a new driver that parks it. Physics also reconciles each time that the simulation gives it a batch of structural changes.

If the system of the parent is full, that system grows first. "Physics systems that grow" under "Physics" tells how. So every hull gets its body in the system that the tree names.

### Ghosts

While an object's bounding sphere crosses a bubble boundary, the other physics system holds a ghost. A ghost is a kinematic body that copies the real body's pose every step, and that Jolt moves with the real body's velocity during the step. The one exception is the ghost of a mesh while it holds still in its system, such as the ghost of a parked station's hull in a parked ship beside it: that ghost is static, as its real body is, so Jolt never simulates it. When it starts to move, it turns kinematic, and when it holds still again, static again. Jolt cannot compute the mass of a mesh, so physics gives each ghost of a mesh a mass of 1 kg, which Jolt reads only for a dynamic body. (ter-dq92) Bodies in the other system collide with the ghost, but the ghost does not push back on the real body. After the object is fully on one side, physics removes the ghost. A contact with a ghost is a contact of the two entities (see "Contacts"). Physics sets the pose and velocity of each kinematic ghost in every step, and Jolt resets the sleep timer of a body whose pose is set, so such a ghost never falls asleep. Jolt puts an awake kinematic body in the island of each dynamic body that it touches, so a body that rests on such a ghost never falls asleep either. In return, Jolt finds every contact of the ghost in every step, so a held pair with it never ends while the two touch. A ghost stands for its real body, so a ghost of a static body, such as the hull of a parked station or an anchor, meets only what the static body meets. Jolt makes a contact of a static body only with a dynamic body, or with a kinematic body when the static body is a sensor. The ghost of a static body is kinematic, unless it is a mesh that holds still, so that a dynamic body that hits it meets a body that moves as the real body does in the ghost's frame, and Jolt also makes a contact between that ghost and every sensor. Physics does not report those contacts. So a kinematic sensor never detects the ghost of a static body, as it never detects the static body, and numeric frames, whose anchors have such ghosts, change nothing. When the game parks a frame, the ghosts of its hull follow the hull's new motion. (review of ter-ejx9)

A ghost is only in the system of a frame next to the body's frame, the semantic frame of the frame whose system holds the body: its parent in the tree, a sibling or a child. The siblings and the children are semantic frames, wherever they hang among numeric frames. Numeric frames have no bubbles, so they do not change which bubbles a body, a hull or an anchor touches, and each gets the same ghosts in semantic frames as it gets without numeric frames. So a crate that falls in a numeric frame below a ship and sticks out of the ship's bubble has a ghost in the ship's parent, and in each sibling of the ship whose bubble it touches (ter-kz89). The hull of a frame that physics does not drive, such as a parked station, never enters a bubble, but it has a ghost in each bubble that its own bubble overlaps. A falling numeric frame also keeps a ghost of each Kepler hull near its members (see "Numeric frames" below). A numeric frame of a frame's semantic parent whose members may touch a body that sticks out of the frame keeps a ghost of each such body, as the semantic parent's own system does (ter-2gg0). When the numeric update stops naming a numeric frame for an anchor or for the bodies that stick out of a frame, physics removes the ghost there at the same sync point, also when the tree did not change: the update sends the frame in its batch of changes (`PhysicsChanges.GhostPlacesWithdrawn`). A frame that leaves the tree takes these places with it, and so does a frame that moves to another semantic parent. The ghost of a hull is never in the hull's own frame. When the tree changes, the reconcile at the start of the next step removes each ghost that the new tree does not allow. The ghost pass at the end of that step adds the ghosts that the new tree needs.

### Numeric frames

The engine makes and removes frames of its own around groups of bodies, only for precision. These numeric frames keep Jolt's float velocities and float bounds small. Near a ship in orbit, every body moves at about 7.7 km/s in the frame of the planet, and a float velocity there steps by 0.0005 m/s. The design is in `docs/plans/numeric-frames.md`.

| Question | Rule |
|---|---|
| Where a numeric frame lives | In the frame tree, with the `NumericMode` `Falling`, `Resting` or `Nesting`, and a Jolt system of its own. It has no owner, no bubble, no gravity of its own and no rotation. |
| What gameplay sees | Nothing. `Self.Frame`, `World.Frames`, `Snapshot`, `FrameChanged`, the topics, `QueryRadius`, the state dumps and the render snapshot's camera frame show only semantic frames, the frames that content or gameplay made. The public members of `FrameTree` answer relative to `Parent`, the semantic parent, and list frames in an order that numeric frames do not change. The members that show the tree as it is stored are internal. |
| The entity row | `Frame` is the semantic frame. `PhysicsHome` is the frame whose Jolt system holds the body. `Pose` is in `Frame`, `HomePosition` is the position in `PhysicsHome`, and `LinearVelocity` is a float relative to `PhysicsHome`. |
| Reads | Every read of a position or velocity names its frame. A read in another frame starts from the physics home and composes through the lowest common ancestor. |
| Writes and directions | Relative to `self.Frame`, in its axes. Physics converts a write when it applies it. |
| Velocity | Jolt's float local velocity plus the double frame velocity of the home, added in doubles. |
| Who gets one | A group whose common speed reaches 50 m/s gets a falling frame. A slow group 10 km or more from its semantic frame's origin gets a resting frame. Below 20 m/s and nearer than 5 km, the group goes back to the semantic frame's own system. |
| How a falling frame moves | It falls freely in the semantic frame's field, integrated in doubles. Each member feels the field at its true position minus the frame's acceleration, so the tidal gravity stays. |
| Membership | At each sync point, in entity order, in `FrameTree.UpdateNumericFrames`, the only code that makes, shifts, moves or destroys numeric frames. Two bodies that can touch within 2 s share a frame. Two bodies of one system, a numeric frame's or the semantic frame's own, stay linked 10 m farther. Members split only after 1 s apart, and a body that the semantic frame's own system keeps leaves it for a new resting frame only after 1 s. The group with the most mass in a frame keeps it, and the smaller groups move. The merge test measures two bodies from their places in their homes, through the homes' lowest common ancestor. |
| Anchors | An anchor is a static body, or the hull of a frame that physics does not drive. The merge test links an anchor to every group, also to one that falls, and the group takes the anchor's home: an anchor counts as infinitely heavy (ter-dhkf, `docs/plans/numeric-frames.md`, "Anchors"). A group with a static body never falls, and it gets the mode, the center and the frame of its static bodies alone: their resting frame when they lie far out, or else the semantic frame's own system. A group with the hull of a `Static` or `Spin` frame stays in the semantic frame's own system. So the anchor is a real body of the group's system, and Jolt sweeps a fast body against it: before, the anchor kept a kinematic ghost in the group's falling frame, which has no linear cast, and a crate at 60 m/s passed through a thin wall (ter-ykqs). A frame that holds a static body of a group has the mass +∞, which only comparisons read, so no sum gives NaN. A group lets go of an anchor when the merge test no longer links them. The split cooldown holds only the members of a numeric frame that are not anchors. Two anchors whose homes are at rest in the semantic frame share the home that the group takes. The hull of a `Kepler` frame moves fast in its own system, so it keeps the older rule until it gets a numeric frame of its own: it joins only a group that does not fall, and the falling frame keeps a ghost of it. The update links the anchors at rest in its first pass, with every other pair, so that the hold of the members of a numeric frame sees the members that an anchor links as together. It links the `Kepler` hulls after the hold, because such a hull joins only a group that does not fall, and the first pass and the hold decide which groups fall. |
| Bodies that stick out of a child | The merge test runs per semantic frame, so it never links a body of a numeric frame to a body that sticks out of a semantic child of the frame's semantic frame. So the update tests each body of a group that gets a numeric frame against each such child that has a body that can move, with the merge test against the sphere around the child's origin that holds every body that sticks out of its bubble. A static body counts only when it is a sensor, because a ghost is kinematic. A hull moves as its frame's driver says, whatever its description says, and a hull that a Kepler or Spin driver moves never leaves the bubble, so the sphere reaches as far as that hull does. The group's frame then keeps a ghost of each body that sticks out of the child, unless it is the child's tree parent, which has them already (`FrameTree.StickOutGhosts`, ter-2gg0). |
| Drain and recenter | A frame takes over its members' common velocity above 1 m/s, and moves its origin to their center of mass beyond 1 km. Both are uniform shifts, so Jolt keeps its contact cache. The ghosts in the frame's system shift too, and wake nothing. A static ghost, of a mesh that held still in the frame, moves after a drain, so the drain turns it kinematic, and Jolt moves it in the next step. Each velocity loses the shift in doubles before it narrows to a float. |
| Moves between systems | A whole touching group moves at once, with no `FrameChanged` and no topic change. A sleeping body stays asleep unless the move changes its velocity. A body that physics has not created yet, such as a spawn, is created in its numeric frame, with its state converted in doubles. A moved body keeps its ghosts in the systems of other frames, because every frame where a ghost may be depends only on the body's semantic frame, which a move does not change. So a fast ship that joins the system of a station's hull while their bubbles overlap keeps its ghost in the station. |
| Frame owners | A hull that joins a numeric frame takes its frame under that numeric frame in the tree. The frame's semantic parent does not change. A frame that physics does not drive lies in its semantic parent: `World.SetFrameDriver` runs the numeric update before the new driver places the frame. A semantic frame that dies takes its numeric frames with it, and the frames in them move to its parent. |
| Vast distances | A numeric frame more than 10^9 m from its tree parent's origin moves under a nesting frame. A nesting frame rests on a grid of 2^20 m in its semantic frame, so its position is exact in a double, and nearby frames share it. It jumps by whole grid steps after its frames, and they shift back by the same exact amount. |
| Render snapshot | It places everything relative to the camera's physics home, which has the axes of the camera's frame. |
| Thresholds | `NumericFrameOptions`, through `WorldOptions.NumericFrames`. |

A read in a distant frame, such as the frame of a star, rounds at that distance. The merge test does not: it measures two bodies from their homes, so two bodies a light-year out keep millimeter steps relative to each other.

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

Collision groups refine this table (`docs/plans/queries-and-sensors.md`, "Collision groups and masks"). Each Jolt object layer holds 16 group bits and 16 mask bits, which Jolt's `ObjectLayerPairFilterMask` tests both ways. Bits 0 to 11 are content's collision groups, and bits 12 to 15 are the four layers above, which the engine calls roles. A body's groups are its role and its collision groups. Its mask is the any-of set of its collision mask, plus the `FrameHull` role for a static body. So two bodies that are not hulls collide only when each body's mask matches the other body's groups. A hull is in the `FrameHull` role alone, with the `Static` and `FrameHull` roles as its mask, so it ignores collision groups. The interior of a frame, the inner body and the movement of a character, and each ghost carry the groups and the mask of their entity. Every body starts in the group `Solid` with the mask `InAny(Solid)`, which gives the table above for every pair in which a body can be dynamic. Two bodies that are never dynamic, such as a ghost and a static body, can pass the filter, but Jolt makes no contact between them. A component changes its entity's groups and mask with `BodyHandle.SetCollision`, and physics applies the change at the start of the next step. Each role has its own broad-phase layer.

A frame entity has two bodies. In the parent system, its hull is a body on the `FrameHull` layer. In its own system, the same hull shape is static geometry on the `Static` layer. This includes the exterior surfaces, so that a person on a spacewalk can stand on the hull. Objects inside do not push the hull body.

Spawn refuses every body that physics cannot create, before anything changes. `BodyDesc.Problem` and `ShapeDesc.Problem` in `Tertius.Core` hold these rules, and the World and the backend use the same ones. `IPhysicsBackend.ShapeProblem` adds what only the backend can tell, for example a convex hull whose points coincide. So a change batch holds only bodies that physics can create.

Physics turns some values into NaN in its first step, so spawn refuses them too, for every entity. A position or a velocity must be finite. A rotation must be finite and not zero. A mass must be 0 or a finite positive number of at least 1.2E-38, because Jolt divides by it. Friction, restitution and damping must be finite. `new DPose { Position = p }` has the identity rotation. Only `default(DPose)` has the zero rotation, and spawn refuses it. Two gaps remain, tracked by ticket `ter-d0t6`. Spawn tests a rotation in double precision, but physics normalizes it in single precision. So a rotation whose single-precision length rounds to 0 or to infinity still passes. A mass above about 3.5E+37 kg also passes, and its gravity force overflows.

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
- Jolt puts a group of bodies that touch to sleep only all at once. In the benchmark, a heap of 400 boxes that fell and tumbled in 9.81 m/s² took about 25 s to fall asleep. Before round eight it took about 57 s.
- In weak gravity the threshold is lower, so a heap that tumbles takes longer to fall asleep. The same heap took about 125 s in 1.62 m/s², the gravity of the Moon. Before round eight, it took 76 s there.

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

Jolt keeps rotations as float quaternions, and the square of the length of one is up to `2E-7` away from 1. So the backend normalizes every rotation in double precision where it leaves Jolt, because the hull's rotation becomes the local pose of its frame.

The character controller is a capsule. It uses the frame's effective gravity. Physics stands a character only on walkable ground under gravity. That is, the effective gravity is at least `FrameTree.ZeroGravityThreshold`, 0.05 m/s², Jolt found ground under the character that is not too steep, and the character does not move away from that ground. A standing character inherits the velocity of the surface under it, walks with the part of its desired velocity across its up, and can jump. A character that does not stand is weightless, in any frame and any gravity, because it falls freely or no gravity acts on it. (User) It keeps its momentum, and gravity, forces and impulses change its velocity. Walking and jumping do not move it. The ground holds a standing character against gravity, so gravity does not change the velocity of a standing character, except in the step in which it jumps, and a character that stands still on a floor has no velocity. Gravity in that velocity would press the character into its ground in every step. Jolt then reported a false velocity of one step of gravity, and it pushed each dynamic body that the character leaned on a little in every step, so a stack that a character stood against woke each time it fell asleep. Forces and impulses change the velocity of every character. Physics keeps the desired velocity of a walk, which applies once the character stands. After each step in which it did not stand, the character loses the part of its velocity that goes into each static or kinematic surface that it hit. So a character that jumps into a low ceiling falls back at once. That part is relative to the surface, so a wall that moves into the character gives it the wall's velocity. Against a dynamic body the character keeps its momentum and pushes the body. The direction into a surface is the face's own normal if Jolt's contact normal lies within 10 degrees of it, and Jolt's contact normal otherwise, because Jolt's contact normal can lean several degrees on a flat face, while at an edge or a corner the face's normal would cancel motion that nothing blocks. Jolt pushes a character out of a surface that it sank into by moving it, so that push gives the character no velocity.

Under gravity, the up of a character turns against the gravity whenever the character touches anything. That is, Jolt's ground state from the last update of the character is anything but "in the air": walkable ground, steep ground, or a contact that does not support the character. So a character that lands on its side or on its head turns upright, and then it can stand. A character in free fall that touches nothing keeps its last up. So a person who drifts out of a ship in orbit does not turn toward the planet. At a hand-off, a character that stood in its last step takes its up from the gravity of its new frame. Any other character keeps its up and its rotation, turned into the axes of its new frame. The hand-off reports the rotation that the character got in its new frame.

In each step, physics reports for each character whether it stood. A character that a hand-off moved has not stood in its new frame yet. Physics looks up the contacts of that character in its new frame at once, so a character that walks on across the boundary stands again in the next step. Components read the report as `self.Body.Standing`, and other code as `Snapshot.IsStanding`. Tick N sees the step that ran during tick N - 1. So the components that steer a character agree with physics on where weightlessness begins.

### Contacts

If two bodies start to touch, physics reports the contact once, as a `Collision` to each of the two entities. A contact starts at a real touch: the first step in which the two bodies are at most 1 mm apart, or approach fast enough to close their gap within the step, so that the solver pushes them apart. Jolt finds a pair earlier, as soon as the bodies are within its speculative contact distance of 0.02 m. Such a pair can touch many steps later or never, so it starts no contact. (User) Each contact that starts ends later with exactly one `CollisionEnded` to each of the two entities. Physics reports the contact again only after it ended. Jolt finds contacts only for the bodies that it simulates, and it does not simulate a sleeping or a static body. A body can fall asleep on something static or asleep, or a hull can become static on a floor. If the rest of the system keeps stepping then, Jolt drops the contact. After the body wakes or moves, Jolt finds the contact again.

A bounce leaves Jolt's speculative contact distance (User). When a landing step begins within that distance, Jolt gives a bouncing body its rebound at once, from where it is. So a small bounce from a step that began near the surface stayed within the distance, while the same bounce from a step that began near its edge left it: one drop reported one landing or two, by where its landing step began. Physics therefore applies a minimum rebound. A contact does not bounce when the bounce that Jolt would give it rises less than 4 cm, twice the distance. Jolt applies the accumulated forces at the start of a step, so it takes the speed that they added off a rebound: the rebound is the combined restitution (the larger of its two bodies') times the approach speed before the solver runs, less the pull toward the surface along the contact's normal times the step. The pull comes from the forces and inverse masses of the dynamic bodies, and gravity is one of those forces. A numeric frame falls with its semantic frame's gravity, so its bodies get only the gravity that it leaves, and a ghost or kinematic body there comes at them at the frame's acceleration, which Jolt does not see. So in a numeric frame the rule counts each body that is not dynamic as accelerating at the frame's acceleration backward, which gives the pull of the semantic frame, and numeric frames change no decision. Under that pull the bounce rises the rebound squared over twice the pull, so the limit grows with the pull: at 1 g the slowest rebound that remains is about 0.89 m/s, and with no pull toward the surface every bounce remains, because it never comes back. The rule reads what Jolt simulates: Jolt's own velocities, in which every ghost moves as its real body does (ter-dq92), and the contact point that approaches fastest, because Jolt decides restitution at each point and a report sets one restitution for the whole manifold. Jolt computes the settings of each contact again in every step, so both of Jolt's reports of a contact, a new one and one found again, set the combined restitution of such a contact to 0 in every step. Jolt's linear cast of a fast body calls the same reports and uses the settings that they leave; the cast does not take the forces off its rebound, so the rule judges a cast's bounce a little short. A landing whose bounce is just below the minimum in its first step of touch can still bounce in the next step: the speculative contact lets the body reach the surface at almost its full speed, and the gravity of the next step raises the approach above the minimum. That bounce leaves the distance too. The first version of the rule asked for a rebound of 1 m/s without the pull at any gravity, which kept bounces inside the distance at 2 g and more and stopped slow bounces in zero gravity (review of ter-ejx9). The default restitution of a body is 0, as in Jolt, so a body bounces only when its content names a restitution. (ter-4kaa)

Physics holds each contact that Jolt drops. If Jolt finds a held contact again, the contact continues, and physics does not report it. A held contact ends at the first step that simulates one of its two bodies without finding the contact. So sleep and wake end nothing. For example, the game can lift a sleeping box off the floor. Its contact with the floor ends, and the box reports the floor again at its landing. In each step of a system, physics visits every held contact of that system.

A contact between the body of one entity and the ghost of another entity's body is a contact of the two entities, and it can start through the ghost. (User) A ghost stands for its real body, so the impulse of such a contact uses the masses of both real bodies, and it does not depend on which system reports the contact. Jolt moves every ghost with its real body's velocity, and the ghost of a mesh is static only while it holds still (see "Bubbles and ghosts"), so the test of a touch and the impulse read Jolt's velocity of the ghost, which is the real body's. (Before ter-dq92, the ghost of a mesh was always static, and physics put it at its real body's place after each step, so Jolt neither stopped nor bounced a body by the mesh's motion.) A pair of two ghosts is no contact: the real bodies' own systems report their contact. So the two entities of a contact can touch in several systems at once. For example, a body that sticks out of a child frame has a ghost in the parent, and a body in the parent touches that ghost, while that body touches the child's bubble and has a ghost in the child, which the first body touches. Physics counts, for each pair of entities, the body pairs that hold their contact as started, in every system. The contact starts when the count goes from 0 to 1, and it ends when the count goes back to 0. So the two entities hear one `Collision` and one `CollisionEnded`, however many systems hold the contact in between. When several systems start the contact in one step, the system of the frame with the lowest index gives the point, the normal and the frame of the `Collision`, so that the order of the systems does not matter. In each step, the body pairs that end count down after the ones that start, so a contact that one system drops while another system starts it goes on. (ter-ejx9)

A contact also ends when one of its bodies is destroyed. The other entity hears it, and the end names the entity that is gone. A body that leaves its system, by a hand-off, a move between numeric frames or a destroy and create at one sync point, keeps each contact in every system that holds a body of each of the two entities, real or ghost but not two ghosts, that can touch the other one. That is the new system when the other entity has its body or a ghost there, and the old system when the body sticks out of its new frame's bubble and leaves a ghost there. Two touching bodies that move together keep their contact, and so does a body that enters a frame ahead of a body that it touches. A ghost that physics removes takes its contacts along in the same way. Physics settles these contacts after the hand-offs and the ghosts of the step, so the new ghost of a body that left can carry them. A move between numeric frames happens at the sync point, and the ghost pass puts the moved body's ghosts at its new state, and makes the ones that its new place wants, only at the end of the next step, so its contacts wait until then. In each system that takes a contact over, the contact is held, and it ends as a held contact does. Every other contact of the body ends, with one exception. A hand-off happens in the step, and the numeric update of the last sync point chose the numeric frames for the bodies' places before the step. So after a hand-off, a contact with a body in a numeric frame can be in no system, where without numeric frames one system takes it over. A body that sticks out of a frame whose tree parent is a numeric frame leaves the frame into that numeric frame, which holds only the bodies that the merge test linked to the frame's hull. A body that leaves a frame drops its ghost in each numeric frame that the update named for the bodies that stick out of that frame. A body that enters a frame with no body that can move has no ghost yet in the numeric frames of the semantic parent. The next sync point's numeric update links the touching bodies, or names the numeric frame that keeps the ghost. So the rule is: a contact that no system takes over waits with its count until the end of the next step, as the contacts of a move do, when a hand-off of the step moved one of its bodies and one of its bodies is in a numeric frame. A contact that really ended at the hand-off ends one step late. Without numeric frames, no body is in a numeric frame, and the system that takes the contact over is the semantic parent's own, so the two entities hear the same events either way. (reviews of ter-ejx9 and ter-2gg0) A system takes a contact over only for two bodies that Jolt can find: their layers meet, so a hull never takes over a contact with a body that moves, and Jolt makes a contact only when one of the two bodies is dynamic, or when one is kinematic and the other is a sensor. A character's own collisions find every body. A ghost is kinematic, or static for a mesh that holds still, and a ghost of a static body meets only what that body meets. So a ghost takes over a contact only with such a body. A held pair that Jolt can never find would end a step late, or never while both of its bodies sleep. For example, a crate that slides on a static floor into a bubble ends its contact with the floor at the hand-off: its ghost on the floor's side cannot touch the floor. An interior never takes over a contact of its owner's hull: the hull lies inside its frame's bubble, so a body is in the frame before it can touch the hull.

Jolt reports the contact of a pair that it found before in every step, which is a call from Jolt into C# for every contact of every simulated body. Only a pair that Jolt found without a touch needs these reports. So physics hears Jolt's contacts through its own contact listener of the C API, whose callbacks are C# functions that Jolt calls directly, and not through the contact events of JoltPhysicsSharp. Those events look up a C# object for each of the two bodies, in a table behind a lock, which made each tick of a heap of 300 awake boxes about 15 % slower while one pair waited for its touch. The callback for a contact that Jolt found again leaves at once while no pair waits for its touch and the contact's restitution is 0. When a pair waits, it reads the two body IDs and leaves after one set lookup, unless the pair is the one that waits. When the contact may bounce, it also reads the approach speed of each contact point for the minimum rebound, and the bodies' accumulated forces only when a point approaches faster than Jolt's 1 m/s for restitution. The C API keeps one table of contact callbacks for the whole process, so this table replaces the one of JoltPhysicsSharp: its contact events never fire, and the backend replaces the listener that JoltPhysicsSharp gives each new system before the system first steps. (ter-pnco) The impulse of a `Collision` is an estimate from the approach speed. Jolt's C API cannot read the impulse that the solver applied.

Jolt never simulates the inner body of a character. A character also keeps a gap of 0.02 m from what it touches, which is Jolt's speculative contact distance. So Jolt loses and finds the contact of a character and a box again and again while the character pushes the box. For this reason, the character's own contact list decides the end of a held contact of the character. This list holds each body within 0.1 m of the character, asleep or awake. If the list no longer has the other body, the held contact ends.

The gap of 0.02 m puts the contact of the inner body at the edge of Jolt's speculative contact distance, so Jolt can miss it, and Jolt never finds a contact of the inner body with a static body. So the character's own collisions start a contact too, also with a ghost. A hit is a real touch. If the character hits a body in its update, and the contact of the pair has not started, physics reports the contact. If Jolt does not hold the pair, physics holds it. The list then ends it, as above. If Jolt finds the contact later, the contact continues. The impulse of such a contact is the speed with which the character moved into the body along the contact's normal, times the reduced mass of the two. So physics reports the contact of a character that walks into a box, pushes it slowly, or lands on a box or on a floor. A body that moves into a character that stands still starts a contact only when Jolt finds it for the inner body.

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
5. A contact names two different live entities that have a body, and a live frame. An ended contact names two different entities. One of them may be gone or have no body, because destroying a body ends its contacts, and physics hears of the destroy before its next step.
6. A character state names a live entity whose body is a character. Each entity has at most one character state in a step.

The class `PhysicsOutputCheck` in `Tertius.Core` holds these checks. The sync point uses it. The physics tests use it too, in the class that stands in for the sync point. So the tests apply the same rules as the engine.

A broken character state is the one exception to the rule below. It changes only whether its character stands, so a Release build records one error and skips only that state. The character state of an entity whose update the sync point skips is part of that update, so it is skipped too.

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
- Hemisphere ambient light: the sky color from above and the ground color from below.

Lighting is in linear color space, into an HDR target, with tone mapping at the end. Lighting works in world axes centered on the camera. The specular exponent is n = 2^(1 + 11 · gloss), from 2 to 4096. The Fresnel F0 is 0.16 · specular².

The G-buffer has these targets:

| Target | Format | Contents |
|---|---|---|
| Albedo | RGBA8 sRGB | Base color. Alpha 0 marks an unlit impostor pixel. |
| Normals | RGBA16 SNORM | The shading normal and the geometric normal, both octahedral-encoded, in world axes. Shadow biasing uses the geometric normal. |
| Specular | RG8 | Specular intensity and gloss. |
| Emissive | RGBA16F | Emitted light. For a lit pixel, alpha is the share of the hemisphere ambient light that comes from the sky. Impostors write their baked color here. |
| Distance | R32F | Distance from the eye. Zero is sky. |

Transparent objects use one forward pass after the lighting pass, sorted back to front across ranges. They test occlusion against the distance target, not the depth buffer.

Dynamic point and spot lights are shaded in the deferred lighting pass. The sun is a directional light.

The sky is a procedural star field, fixed to the axes of the root frame of the camera's frame tree. The render snapshot carries the root's axes in the axes of the camera's frame, as `RenderSnapshot.RootRotation`, and the renderer interpolates them as it interpolates the camera. So the stars do not turn with a ship that the camera rides in, and a switch of the camera to another frame does not move them.

The hemisphere ambient light needs an up, and each instance takes it from the gravity field of its own frame, at its place. (User) Up points against a uniform field, inside the field's region, and away from the frame's origin in a point field. In a frame without gravity, every surface gets the mean of the sky and the ground color. A frame owner, such as a ship's hull, takes its own frame's field at the frame's origin. Only the field counts, not the frame's proper acceleration, so thrust never turns the up. (User) The ambient light of a surface therefore does not depend on the frame that the camera rides in, a planet sees the sky on every side, and an empty frame in space mixes evenly by itself.

The snapshot gives each instance its up as a `HemisphereUp`. It is a custom C# 15 union, as `GravityField` is. (User) Its cases are `UpAlong(Direction)`, a direction in the axes of the camera's frame, and `UpAwayFrom(Center)`, a point given relative to the instance, so that a planet's own center moves with it exactly. The empty union is the even mix. It takes 16 bytes, one vector and a tag, because a value holds one case. `GpuInstance` still packs it into one `Vector4` for the shaders: the direction with `w` 1, the point with `w` 2, and zero for the even mix. The G-buffer pass stores the sky's share in the alpha of the emissive target. The up is not interpolated between ticks. The impostor manager keeps each capture's up in object space in its record of the light, so an object that turns against its up is captured again.

The up costs about 5 ns per instance in the snapshot build. On one thread, `RenderSnapshotBenchmarks` builds a snapshot of 20,000 visible entities in 784 µs, against 675 µs without the up. Each GPU instance grows from 64 to 80 bytes. The renderer has no GPU timer to measure that yet (ticket `ter-yiii`).

### Shadows

Shadow casters are selected by distance from the camera, not by frame, so an object shadows objects in any frame. Every caster has the `CastsShadow` flag, and the sun has none. A caster casts two kinds of shadow:

- Map shadows. Cascaded shadow maps for the sun cover receivers in the first 2 km of view depth, with four cascades and blending at the seams. They draw every caster whose bounding sphere comes within 2 km of the camera, however big it is, for example a ground plane or a planet under the camera. Map shadows have the caster's real shape.
- Far shadows. Every caster is also a sphere of its bounding radius, tested analytically in the lighting shader against the sun's disc, so the edge is soft. A sphere never shadows a point inside it, so a caster never shadows its own surface, and a planet's ground needs no such shadow: it faces away from the sun at night. A caster that the maps draw counts as a sphere only beyond the maps' reach, faded in over the last cascade's blend, so no receiver gets both shadows from one caster. A long or flat caster shadows as a rough sphere there.

A far shadow counts only while the sun lights the scene, and only when it can be noticed. A caster beyond the maps must cover a pixel, or a fifth of the sun's angular radius, seen from the camera. A caster in the maps must hide 4% of the sun from 2 km away, where its far receivers start: a 50 m ship 500 m away qualifies, and a crate or a person does not. So the cost per pixel grows only with the shadows that can show. The sun is still the one light with shadows; making every far light work the same way belongs to the point and spot light shadows epic.

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

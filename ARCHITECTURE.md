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
| Tests | xUnit v3 on Microsoft.Testing.Platform, with AwesomeAssertions. Coverage uses Microsoft.Testing.Extensions.CodeCoverage. |
| Images | StbImageWriteSharp writes the PNG captures. The tests read PNG files with StbImageSharp. |
| Benchmarks | BenchmarkDotNet 0.15.8 |
| Platforms | The flake supports `aarch64-darwin`, `x86_64-linux` and `aarch64-linux`. |

The dev shell builds the double-precision Jolt library with Nix from `native/joltc.nix`. That file pins the joltc commit and the Jolt tag by hash. The dev shell sets `TERTIUS_JOLTC_DIR` to the folder of the library. The script `native/build-joltc.sh` is a fallback. It builds the same sources without a hash check, into `native/osx-arm64` or `native/linux-x64`, and it supports only these two platforms.

`Directory.Build.targets` copies the Jolt library next to the output of every project that sets `<UsesJolt>true</UsesJolt>`. It takes the library from `TERTIUS_JOLTC_DIR`. When that variable is not set, it takes the library from the folder of the script. It also copies the Vulkan loader from `TERTIUS_VULKAN_DIR` next to the output of every project that sets `<UsesVulkan>true</UsesVulkan>`. It defines `TERTIUS_CONTRACTS` only in Debug builds, so the contract checks compile out of Release builds.

## Projects and dependency rules

```
src/Tertius.Core        math, handles, messages, contracts, TripleBuffer, PoseTree, FrameTree with the numeric
                        frame rules and the motion sample table, the rails library (Kepler and spin math),
                        gravity fields, physics and render seams
src/Tertius.Simulation  World, entities, components, rails components, chunked gameplay runs, router, sync point,
                        snapshots, spatial index, render snapshot builder, blueprints
src/Tertius.Physics     Jolt backend: one PhysicsSystem per frame, intents, gravity, hand-off, ghosts
src/Tertius.Rendering   Vulkan deferred renderer: G-buffer, depth ranges, lighting, shadows, impostors
src/Tertius.Host        threads, window, input, tick pacing, scripted and headless runs, state dumps,
                        wiring of all modules
src/Tertius.Demo        the vertical slice game: its scenes and components (its scripts are in scripts/)
tests/Tertius.*.Tests   one test project per engine module, and Tertius.Host.Tests also tests the Demo
tests/Tertius.Testing   shared test helpers: FaultInjectingBackend, WorldTesting, Scenes, test components
benchmarks/             BenchmarkDotNet benchmarks
```

- `Tertius.Core` depends on nothing but the base library.
- `Tertius.Simulation`, `Tertius.Physics` and `Tertius.Rendering` depend only on `Tertius.Core`. They never reference each other. The test projects are the exception. `Tertius.Testing` and the Simulation, Physics and Host test projects reference several modules together. The reason is that every physics test runs the real World on the real Jolt backend.
- `Tertius.Host` references all modules and connects them through the seams in `Tertius.Core`.
- `Tertius.Demo` references `Tertius.Host`.

## Threads and the tick

The simulation runs at a fixed 60 ticks per second. Physics steps once per tick with the same time step.

| Thread | Work |
|---|---|
| Main | The window and input. macOS requires this. |
| Gameplay workers | Entity runs, in chunks of 64 entities, through `Parallel.For`. In a large world, also four pieces of the sync point (see below). |
| Physics | One Jolt step for every active frame. Jolt uses its own job threads. |
| Simulation | Runs the ticks and their sync points, and a scripted run's steps. In the window, it paces the ticks by the wall clock. |
| Render | Draws the latest render snapshot and interpolates between the last two. |

The Main, Simulation and Render threads exist only in the window. A headless run has none of them. Its calling thread runs the ticks, the steps of the script and the captures (see "Host and scripted runs").

One tick has three parts. First, one thread prepares the tick. Then, during the parallel part, gameplay and physics run at the same time. Both read only state that is fixed for the tick: the front snapshot of tick N-1 and the frame tree. Last, during the sync part, one thread merges the results.

In a large world, the sync thread hands four pieces of the sync part to the gameplay workers, as independent work items. Each one runs on the workers only when `WorldOptions.MaxDegreeOfParallelism` is not 1 and the piece has at least a threshold of work. One `Parallel.For` costs about 5 us and 2 KB, so a small world runs every piece on the sync thread and pays nothing. The thresholds of steps 8 and 10 were measured on an Apple M1 Max with all workers (ter-sn28), and the comment on each constant gives its measurement. Step 8 also ends each change that a game makes outside a tick, through the same method, with the same pieces and thresholds.

| Step | Piece on the workers | Threshold | Work item |
|---|---|---|---|
| 8 | Copy the rows that gameplay wrote | 8,192 rows that gameplay wrote | One chunk of 64 slots |
| 8 | Find the new grid cell of each moved entity | 65,536 moved entities | One of 32 equal ranges of the moved list |
| 10 | Join the outboxes, expand the targets, sort the pairs by recipient | 6,000 messages | A block of chunks, then a slot range |
| 11 | Build the render snapshot | 8,192 slots | 1,024 slots |

Each piece ends in the same state, bit for bit, as on the sync thread, in every run. A work item writes only its own chunks, slots or range of a list. What it gives to state that items share waits in a per-item list, and the sync thread joins those lists in chunk, slot or range order, never in the order in which the workers finish. The outboxes of the gameplay chunks join in the same way. Each step below gives its own ordering rule. Tests compare each piece on the workers with its path on one thread. Each subsystem with a piece takes the workers, or none when the world has one thread, and keeps its threshold in an internal property that refuses a value below 1. The tests of all four pieces force the threshold down to 1, so that a small world runs the piece on the workers, and a test of the render snapshot also builds a world of more than 8,192 slots at the default threshold.

The engine's part of a tick follows the rule that hot paths allocate nothing after warm-up, with one accepted exception: the bookkeeping of each `Parallel.For` call, about 2 KB of short-lived garbage. The gameplay part makes one call in every tick of a world with more than one chunk, when there is more than one worker. In a large world, the sync part adds one call for each piece in the table above, and two for the routing, one for each phase. So a tick of a large world on all workers allocates about 12 KB, under 1 MB per second at 60 ticks per second, which the garbage collector frees in its cheapest generation. A set of worker threads that the world keeps for its whole life would remove this garbage. But it would replace the .NET task scheduler in every parallel part of the tick, so it waits until a measurement shows that the garbage costs time. With one worker, or below its threshold, a piece runs in a plain loop on the sync thread and adds nothing to this garbage. A test checks that a routing on the sync thread allocates nothing after warm-up.

The preparation does these steps in this order:

- If a change outside a tick made the numeric update due, run it, derive the attachments and swap the snapshot. So a body that a spawn outside a tick made joins its numeric frame before physics creates it.
- Hand physics the batch of spawns and destroys made outside the tick, before the step that can touch them.
- Update the frame tree. Fix the accelerations of the falling numeric frames and the orbit frames for the step. Physics reads them, and the sync point follows them.
- Apply the queued changes of the indexes that entity queries start from, so that the workers only read them.
- Reserve a pool of free slots for each chunk that has live entities, in chunk order. A pool holds the larger of `WorldOptions.SpawnPoolSize` (8 by default) and twice the spawns of its chunk in the last tick, up to 1,024. When the pools need more slots, the tables grow here. A spawn during the tick takes its slot from the pool of its chunk, so spawned handles are the same in every run.

The sync point does these steps in this order:

1. Wait for all gameplay chunks and for the physics step. Grow the tables over the slots that spawns took after the pool of their chunk ran dry. Such a spawn takes a slot with an atomic increment. Its slot then depends on thread timing, so the world logs a note that the run is no longer deterministic.
2. Move the falling numeric frames over the step, with the acceleration that physics used. Then check the physics output before any of it lands. If an output can only come from a bug in the backend, skip the physics update of each entity that it names. "Output that only a bug produces" under "Physics" gives the checks. Then move each handed-off frame under its new parent in the frame tree, and put each orbit frame on its anchor's sample. Copy body poses, velocities and hand-offs into the back transform table. A body state of a hull writes no row. Keep whether physics stood each character. Then every frame takes its step record, in one loop (`FollowOwners`, see "Rails components and the sample table"), and the float view of each frame entity, its transform row, is filled from its exact table, its row in the frame tree. The frame entity also moves to the member set of its frame's parent there. So the owner of a frame that a hand-off or the game moved under another parent changes topic and hears one `FrameChanged`. Each of the two loops is one walk over the frame tree's own storage, in dense order (`FrameTree.VisitFrames` and `FrameTree.VisitOwnerViews`), which hands each frame to a struct visitor with no handle check. In a Debug build, a contract checks after each visit that the visitor moved no frame, or changed nothing that an owner view reads. A record from physics is relative to the frame's parent after its hand-off. So it must come after the hand-offs, and before step 5 destroys any frame. A frame without a body takes its owner's write of its transform here too, so a write in the tick in which the frame's parent dies converts with the frame. Then each plain body that a hand-off moved changes topic and hears one `FrameChanged`. Send the contacts that started as `Collision` events last, and then the contacts that ended as `CollisionEnded` events. So a contact that starts and ends in one step reaches each entity in that order.
3. Make sure that no entity with a body wrote its own transform. In every build, such a write throws `InvalidOperationException`, so none gets through. When `WorldOptions.DebugChecks` is on, this step checks every invariant of the entity table, this one included. That check costs time in proportion to the capacity of the table.
4. The step applied the intents of the last tick. So their collision groups and masks become the back collision rows of the bodies, which step 8 publishes, and their changes of motion become the stored motions of the hulls in the frame tree (`FrameTree.HullMotion`), also without a physics backend. Both describe the step that ended, as all the state that the next tick reads does. Then each owner that wrote a change of motion in this tick gets its frame planned for the next step, which applies the change: the frame and the intent drop what the new motion refuses (see "Changes of motion"). The entity table lists the owners that wrote a change in each chunk, and the swap hands that list on with the intents, so step 4 visits only them, not every body that wrote a force or a velocity. Then swap the physics intent buffers, so that physics gets this tick's forces in the next step. With no physics backend, the swap drops every request but the changes of motion. The sample table moves its buffers on with them, also without a physics backend, because a frame without a body follows its samples too.
5. Apply the subscribe and unsubscribe requests of the chunks, in chunk order, while every entity that asked is still live. Then free dead entities. The destroy batch grows to the attached children of each dying entity. It also grows to the members of each dying frame that has no surviving ancestor. The survivors in a dying frame move into the nearest ancestor frame that survives. Increment the generation of each freed slot and return the slot to the free list. Queue the destroys of their bodies for physics, which gets the batch in step 6c. Before the dying frames go, convert each intent of the next step that names one of them into its entity's new frame. "Ownership of transforms" tells how. Step 7b cannot relate a frame that is gone. Each motion sample that is relative to one of them, in the applied and the followed buffers, converts in the same way.
6. Make new entities live, in chunk order. Each spawn reserved its slot during the tick, so no table grows here. The world drops a spawn into a frame that step 5 destroyed, and it records an error. Queue the new bodies for physics. An attached entity becomes live in the frame of its parent at this moment, after steps 2 and 5 moved the parent. A new frame stands at the pose of its spawn description, and holds still there for its first step, because it has no sample yet.
   - 6c. Run the numeric update (`FrameTree.UpdateNumericFrames`, see "Numeric frames"), after the physics output, the destroys and the spawns, so that it clusters every live body where it is at the end of the tick. Every frame took its step record in step 2, so the rows of the bodies and the frames both hold the end of the step, and the update tests the bodies against bubbles of the same moment. (Before the review of ter-2gg0, the frames on rails moved after the update, and the update placed the bubble of a station that orbits at 7.5 km/s 126 m behind the bodies beside it.) A new orbit frame starts from its anchor's sample of the step that ended. Then hand this sync point's changes to physics. The update before this one can name a home for the ghost of an anchor. Then physics gets the changes even when there are none, so that it drops the ghosts whose home this update stops naming. The update gathers the bodies in slot order from a bit per slot, which the entity table sets while the slot holds a live entity with a body. It takes each body's bounding radii as the table kept them at the spawn.
7. The float view of each frame entity is filled from its exact table again, after the numeric update, which can move a frame among numeric frames. A nesting frame that jumps moves no frame and writes no row, but it changes the sum that gives the place of each frame below it in its semantic frame, which can round to another double. This pass runs only when something wrote the frame tree since the pass of step 2. The tree keeps a stamp, `FrameTree.OwnerViewStamp`, that every write of a frame's local pose, velocities or parent changes, and the world compares it with the stamp at the end of the pass of step 2. A change of a hull's motion at step 4 writes none of these: a hull that leaves its orbit frame leaves it falling, and a falling frame is still a numeric frame, so no owner view changes. With frames on rails and nothing else, nothing writes the tree between the two passes, and the second pass found every row equal. When `WorldOptions.DebugChecks` is on, the world checks at the end of every sync point that the float view of every frame entity, its transform row, equals what its exact table gives, bit for bit. So a write of the frame tree that does not change the stamp fails a test. Then every attached entity takes its frame, pose and velocities from the root of its chain.
   - 7b. Each intent that the next step applies and that names another frame than its entity's converts into the entity's frame (see "Ownership of transforms"). This comes after steps 2 and 5, which move entities between frames, and after step 7, so that every entity is in its frame for the next tick. It comes after step 2, so that every frame stands where the next step starts, the moment at which a velocity of the intent applies. The motion samples need no conversion here: only a dynamic hull moves between frames at a sync point, its owner writes no sample, and a hull that stops being kinematic at step 4 drops the sample written in the tick. A contract checks it in a Debug build.
8. Swap the snapshot. The back tables become the front tables. Copy only dirty slots forward: first the rows that gameplay wrote, in chunk order, then the rows that the sync point wrote. With 8,192 or more rows that gameplay wrote, the workers copy them, one chunk per work item. A chunk's dirty list holds only its own slots, so the items write disjoint rows. Each chunk keeps its moved entities and its changed component flags in its own lists, and the sync thread joins those lists in chunk order after the workers finish. Then it copies the rows that the sync point wrote, and the component index takes the changed flags, on the sync thread, in the order of the copy on one thread. Then the frame tree and the spatial grids catch up with the new front rows (see "Snapshots").
9. Run the physics queries that components asked for during the tick, the ray casts, shape casts and overlap queries, in entity order. Jolt is at the end of the step, with the destroys and spawns of steps 5 and 6, and the new front snapshot describes the same moment: its component flags, collision groups, poses and frames. Queue each answer as an engine event.
10. Route the outboxes, and the answers of step 9, into inboxes (see "Outboxes, routing and order"). A routing of 6,000 messages or more runs on the workers.
11. Build the render snapshot and publish it to the render thread through a triple buffer. A build of 8,192 slots or more runs on the workers (see "The render snapshot").
12. Start the next tick.

Gameplay intents written during tick N reach physics in step N+1. That is one tick of latency, and it is acceptable.

## Entities

An entity is a 64-bit handle: a 32-bit slot index and a 32-bit generation. The generation increments every time the slot is freed. A handle with an old generation is stale, and every lookup must reject it.

Each live entity has a row in three parallel tables, indexed by the slot index:

- Transform: the frame that the entity is in, a frame-local `DPose`, and linear and angular velocity. Positions are double, because Jolt gives double positions and a planet frame is millions of meters across.
- Physics: an optional body description and the double-buffered `PhysicsIntent`.
- Render: mesh, material, scale, bounding radius, render flags and an optional light (a `LightData`: a point light, a spot light or none).

Each entity also has a list of components, which are classes that implement `IComponent`. The list is sorted by `Priority`. Components with the same priority keep the order in which they were added.

A frame is an entity that also has a row in the frame tree. `FrameHandle` holds the entity handle plus the frame index and generation. It converts to `Entity` implicitly.

### Component rules

- A component changes only its own state and the tables of its own entity.
- An entity never changes another entity. It sends a message.
- An entity reads other entities only through the front snapshot of the previous tick. It never reads another entity's components. The snapshot publishes each entity's component flags, the types of the components that it had, and each body's collision groups, so a component can ask whether another entity has a component, or find entities with an entity query (see docs/plans/queries-and-sensors.md). `Snapshot.TryGetBody` gives the physics facts of another entity's body: its motion, mass, shape kind, bounding radius, collision groups and mask, and whether it is a sensor. A body's description does not change after spawn, so only the collision groups and mask, which an intent can change, have double-buffered rows. The motion of a frame's hull can change too, with the intent `self.Body.SetMotion`, but the frame tree stores it (`FrameTree.HullMotion`), and it changes only at step 4 of a sync point, so it is the same for the whole tick. Like the collision groups, the facts show the last finished step, so a change shows from the second tick after the one that writes it. The entity reads the motion of the step that applies its own writes with `self.Body.NextStepMotion` (see "Changes of motion").
- A component never stores a `ref`, a span, an `EventList`, an `Outbox` or a `Self` beyond the call that received it. The compiler enforces this for ref structs.
- `Handle` never awaits. For a delay, a component sends a message to itself with a delay.

### Ownership of transforms

If an entity has a physics body, physics owns its transform. Gameplay moves it only through `self.Body`: forces, impulses, velocity changes, kinematic targets and teleports. These go into the entity's `PhysicsIntent` slot. Forces and impulses add up during the tick. Velocity changes and kinematic targets keep the last value written. A kinematic hull, the kinematic body of the owner of a frame that has a parent, moves only by its motion samples (`self.MoveOnRails`, see "Rails components and the sample table") and by teleports. `MoveKinematic`, `SetVelocity` and `SetAngularVelocity` throw an `InvalidOperationException` for it, because only a sample tells its frame how to follow. The guards judge each write by the motion of the step that applies it (see "Changes of motion"). The owner of a static hull moves it only by a teleport: physics moves the hull at the start of the next step, and the frame takes the teleport's pose at the next sync point. The owner of a hull changes the hull's motion with `self.Body.SetMotion` (see "Changes of motion").

An intent is relative to `self.Frame` during the tick in which gameplay writes it, and it names that frame (`PhysicsIntent.Frame`). Physics applies it in the next step, and the sync point between can move the entity to another frame: the step that ran during the same tick hands the body into a bubble or out of one, or the destroy batch moves it out of a frame that dies. The sync point then converts the intent into the entity's new frame, with the frames' relation at the end of the tick: poses as poses, velocities with the motion of one frame relative to the other, and forces, torques, impulses and a character's walk as directions. So physics always applies an intent in the entity's frame. A velocity applies at the start of the step, the moment of the relation, so it converts exactly. A kinematic target is a place at the end of the step, so it misses by how far the two frames move relative to each other in one step. (Before ter-dhkf, physics applied the intent in the new frame as it was. A kinematic door that gameplay moved in every tick entered a passing pod's bubble, got the target of the station's frame in the pod's frame, jumped out of the bubble, came back in the next tick, and changed frames in every tick.)

If an entity has no body, gameplay owns its transform and writes it through `self.SetTransform`, relative to `self.Frame`, unless the entity is attached. This holds for the owner of a frame too, whatever moves the frame otherwise: the frame takes the write at the sync point of the same tick, after its sample, so the write wins for one tick, and the next sample puts a frame on rails back on its rails.

### Creation and destruction

`self.Spawn(...)` takes a slot at once from the spawn pool of its chunk and returns the new handle. The preparation of the tick reserves the pools in chunk order, so a spawn gets the same handle in every run. If a pool runs dry, the spawn takes a new slot with an atomic increment, and the run is no longer deterministic. The new entity starts to run on the next tick. The tables never grow during the parallel part of a tick. They grow in two places. The preparation of the tick grows them to fill the pools. Step 1 of the sync point grows them over the slots that dry pools gave out. An entity can destroy itself with `self.Destroy()`. To destroy another entity, send it a `Destroy` message. Both take effect at the sync point.

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

A routing of 6,000 messages or more runs on the workers, in two phases of work items. In the first phase, each block of chunks copies its outboxes into the routed list and expands its messages. A block keeps its pairs in one cell per slot range, which is a run of entity slots. In the second phase, each slot range sorts its cells into the inboxes of its slots. A smaller routing runs the same phases with one block and one slot range on the sync thread. Both give the same inboxes bit for bit, because no order depends on the threads:

- Each outbox goes to an offset in the routed list that a serial pass computes in chunk order.
- Every message of a block comes before every message of the next block.
- A slot range reads its cells in block order, so each inbox lists its messages in routing order, as one stable sort of the whole list does.
- Each slot range writes its pairs from an offset that a serial pass computes in slot order.

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
| `FrameChanged` | After the entity's frame changed: a hand-off moved it to another frame, its frame died and it moved to the nearest ancestor that survives, the game moved the frame that it owns under another parent, or its attachment chain's root changed frame. Once for each change. Not for the frame that a new entity spawns into, and not for a move between numeric frames |
| `Collision` | Physics reported a contact that started |
| `CollisionEnded` | Physics reported that a contact which started ended |
| `RayHit` | The answer to a ray cast that the entity asked for in the previous tick: the entity hit, or a miss |
| `ShapeHit` | The answer to a shape cast that the entity asked for in the previous tick: the first entity that the shape touched, or a miss |
| `OverlapHit` | One answer to an overlap query that the entity asked for in the previous tick: one entity that overlaps the shape, or no entity when none does |
| `InputEvent` | The host forwards input to the entities that subscribe to the input topic, between two ticks (see "Host and scripted runs"). It is a custom C# 15 union of four cases: `KeyChanged(Code, IsDown)`, `MouseButtonChanged(Code, IsDown)`, `MouseMoved(Delta)` and `Scrolled(Delta)`. A code is the integer value of the Silk.NET `Key` or `MouseButton`, because Core does not reference Silk.NET. The default value holds no case, and a component that matches the cases ignores it |

## Snapshots

The transform, render, component flag and collision rows are double-buffered. During a tick, everyone reads the front rows. Each entity writes only its own back rows. In step 8 of the sync point, the back rows become the front rows. That step copies forward only the slots that changed. An entity that did not run costs nothing. The physics intents are double-buffered too, but step 4 swaps them instead of copying them forward.

The world also keeps one spatial grid per frame for radius queries. `SpatialIndex` owns the grids, and `Snapshot.QueryRadius` and entity queries read them. Each grid is a sparse hash grid of 32 m cubes with 64-bit cell coordinates. The owner of a root frame is in no grid. In step 8, the index moves only the entities whose front transform changed, from their old cell to their new one. It first finds the new cell of each moved entity, and then changes the cells in the order of the moved list. Finding a cell reads only that entity's rows, so with more than one worker and at least 65,536 moved entities, the workers find the cells, each in its own range of the list. The sync thread then applies the ranges in order, and the grids end as they do on one thread.

## Reference frames

### The frame tree

Frames form a tree. Each frame has a double-precision `DPose` relative to its parent. `FrameTree` stores frames in a `PoseTree<DPose>` plus parallel arrays:

| Array | Meaning |
|---|---|
| `Owner` | The entity that is the frame |
| `Bounds` | The ellipsoid radii of the bubble, in frame axes |
| `HullMotion` | The stored `BodyMotion` of the frame's hull, the motion that it had in the last finished step, or null for a frame without a hull (see "Changes of motion") |
| `HullMotionInStep` | The `BodyMotion` of the frame's hull in the step that applies the given intents, derived from the owner's change of motion there or else from `HullMotion` (see "Changes of motion") |
| `Gravity` | The gravity field of the frame, a `GravityField` union (see "Gravity") |
| `Velocity` | Linear and angular velocity relative to the parent |
| `ProperAcceleration` | The acceleration that an accelerometer at the frame origin reads |

The bubble of a frame never changes after the frame is created. The simulation alone changes the hull motion and the gravity field of a frame: the stored hull motion at step 4 of a sync point, from the intent of its owner that the step that ended applied (see "Changes of motion"), and the gravity field through `World.SetFrameGravity` (see "Gravity"). `FrameTree.Gravity` and `FrameTree.Bounds` return copies, so no other code can change them. The tree keeps no orbit and no spin: they live on the rails components of the frame's entity (see "Rails components and the sample table").

`PoseTree<TPose>` is packed. Its arrays have no gaps. Each parent comes before its children, so one linear pass computes all poses. A reparent can break that order. The tree then marks itself dirty and restores the order before the next pass.

The scene graph inside a frame uses the same `PoseTree`, with the float `Pose`.

The rotation of every local pose in the frame tree is a unit quaternion: the square of its length is within `1E-12` of 1. `FrameTree.AssertInvariants` checks it. The world normalizes a rotation that a caller gives it, in a spawn description or in a write to the transform of the owner of a frame without a body, and it does not refuse it. A rotation that is off by `1E-7` in the square of its length would put a parent that is `6.8E6` m away about a meter off, as seen from the frame.

### How a frame moves

A frame follows its owner, the entity that is the frame, at every sync point (see "Rails components and the sample table"):

- A frame whose owner has a dynamic hull follows the hull, which physics moves. Its pose and velocity come from that body after each step. The proper acceleration is the velocity change over the step minus the gravity that the parent applied.
- A frame on rails follows the motion samples of a rails component of its owner, `KeplerMotion` or `SpinMotion`. Its hull, if it has one, is kinematic.
- Any other frame holds still in its tree parent. It keeps its pose and its proper acceleration, and it has no velocity there, so it moves with its tree parent. Its owner can still move it: an owner without a body writes its transform, and the owner of a static hull teleports it (see "Ownership of transforms").

The rails components are in `src/Tertius.Simulation/Components/`. Each holds an exact description of its motion as a function of time, and no integration state, so no error builds up, and a save needs only the description.

- `KeplerMotion`: the pose is a function of time, computed in doubles from the orbital elements and the attitude. The attitude `Spin` turns the frame by its axial tilt and a steady spin about its own +Y, as a planet turns. The attitude `FacesParent` points the frame's belly (-Y) at its parent and its nose (-Z) along its motion across the horizon, as a station in low orbit flies. It turns with the true anomaly, so it is exact on an oval orbit too. The frame is in free fall, so its proper acceleration is zero. The orbit holds no G·M. The component takes μ from the point gravity of the parent frame in the front snapshot (`KeplerOrbit.MuFrom`), so a Kepler frame and the loose bodies in its parent fall on the same G·M. (User) G·M lives in one place only, so the two can never differ. The Kepler math is the struct `KeplerOrbit`, with `KeplerAttitude`, in the namespace `Tertius.Frames.Rails`. Each `KeplerOrbit` member that needs the G·M takes it as a parameter.
- `SpinMotion`: the frame stays at its place in its parent and turns in place, as the ground of a planet does. Its `FrameSpin` (in `Tertius.Frames.Rails`) gives the rotation as a function of time: a tilt, then a steady turn about the frame's own +Y. The same math gives the Kepler attitude `Spin`. Its property `ProperAcceleration`, zero by default, is the frame's proper acceleration. A component that starts a turntable copies the frame's value into it, so a landed ship that starts to turn keeps its cargo's weight. No centrifugal force acts on the frame's contents.

The components check their own rules, so the World checks none, and a mod's own component follows the same rules. A property setter refuses an orbit or a spin that is not valid, with an `ArgumentException`. A valid orbit is closed, with a positive semi-major axis, and each of its elements is finite. A valid spin has finite values. A `KeplerMotion` whose parent has no point gravity with a G·M above 0 holds its frame still and logs once. A rails component on a hull that is not kinematic in the next step, the one that applies its sample (`self.Body.NextStepMotion`), writes no sample and logs once. These logs go to `World.Log`, never to `World.Errors`.

A Kepler orbit is in the axes of its parent, so in a parent that turns, the orbit turns with it. A planet that turns therefore has two frames, as real spaceflight has an Earth-centered inertial frame and an Earth-fixed one. The planet's frame does not turn: it goes round its star and holds the orbits of ships and moons. A Spin frame at its center turns and holds the ground. The orbit scene of the vertical slice is built this way.

### Changes of motion

A frame has no driver. Every frame follows its owner, and the owner's components move it (see "How a frame moves"). The motion of a frame's hull is its body's `BodyMotion`. At spawn it comes from `BodyDesc.Motion`. After spawn, the owner's components change it in a tick with the intent `self.Body.SetMotion(BodyMotion)`, for example to park a ship. (User) The frame tree stores the motion of the hull in one place, `FrameTree.HullMotion`, and no entity-table row keeps it beside the tree. The stored motion is the motion of the last finished step (see below). It is null for a frame without a hull: a root frame, a frame whose owner has no body, and a numeric frame. In the World, a frame never becomes a root after its spawn: when a root frame dies, everything in it dies too, the frames inside it included (see "Destroying a frame"). The body description keeps the motion of the spawn. Only a hull can change its motion. Until ter-ru5u, a `MotionDriver` enum in the frame tree chose the motion of the hull. A second value beside the body's motion can disagree with it, so the enum went.

Step k is the physics step that runs beside tick k. A `SetMotion` written in tick k is in the intents that step k + 1 applies. Physics gives the Jolt body the new motion at the start of step k + 1, after the reconcile and before anything reads the body's motion. At step 4 of the sync point after step k + 1, the tree stores the change, from the intents that the step applied, where the collision rows take their groups from the same intents. So the stored motion lags physics by one step, like the collision rows: the tick beside step k + 1 still reads the old motion in the tree and in the body facts of the snapshot, and the tick after it reads the new one. The state that a tick reads describes the last finished step. (User, 2026-10-18)

The motion of a step that has not finished is derived, never stored. Each question has one read over the same data (the intents written in the tick, the front intents and the stored motion), named by the step that it answers. No reader combines the intents and the store on its own.

| Read | The step that it answers | Its value | Readers |
|---|---|---|---|
| `FrameTree.HullMotion` | The last finished step | The stored motion | The body facts of the snapshot (`Snapshot.TryGetBody`), the World's invariants |
| `FrameTree.HullMotionInStep(frame, stepIntents)` | The step that applies `stepIntents`, the step after the last finished step. With the front intents during a tick, and at the sync point until step 4, that is the running step: the step beside the tick, whose output the sync point reads. After step 4 stores the changes and swaps the intents, the front intents are those of the next step | The change of motion in the owner's intent there, or else the stored motion | Physics at the start of each step (`FollowHullMotions`), the check of the step's output and the kind of each frame's step record at step 2 (`FollowOwners`, so the `follows` of a state dump), and the tree's invariants, with the intents of the step that the numeric frames are planned for |
| `World.NextStepHullMotion(frame)`, and `self.Body.NextStepMotion` for a component | The next step, which applies the intents written in this tick | The change that the owner wrote in this tick so far, or else the motion in the step that applies the front intents: the running step during a tick, and the next step itself after step 4 swaps the intents | The guards of `MoveOnRails`, `MoveKinematic`, `SetVelocity` and `SetAngularVelocity`, the checks of `KeplerMotion` and `SpinMotion`, the numeric update of step 6c, which plans the next step, and the body that a rehome in step 5 creates for the next step |

So the motion that Jolt uses in each step, and every rule that acts on that step, takes the change at once. The first hand-off of a hull that the step made dynamic is no bug, and its frame follows its step record as a move of physics. `KeplerMotion` finds its hull kinematic in the tick after "Into orbit". Physics follows the derived motion at every step, as the reconcile follows the tree's structure. The tree stores a change also when the step that applied it failed, so a step that fails loses no change: the next step that runs takes it from the stored motion. Without a physics backend the World still counts its steps: the swap keeps the changes of motion in the front intents, and the tree stores each one at the same sync point as with a backend. The last `SetMotion` in a tick wins, and a change to the motion that the hull has changes nothing, in the tree or in Jolt.

At step 4 of the sync point of the tick that writes a change, before the intents swap, the World keeps the frame coherent with the motion of the next step (`FrameTree.PlanHullMotion`). These drops follow the motion of the next step, whatever the motion of the step that ended. The guards of a tick judge each write by the motion at its moment, and a later change in the same tick can turn that motion:

- A hull that is not kinematic in the next step drops the sample that its owner wrote in the tick, so no step applies it. The sample that the running step applies stays: the frame follows it at the next sync point.
- A hull that is kinematic in the next step drops the velocities and the kinematic target of the tick (`SetVelocity`, `SetAngularVelocity` and `MoveKinematic`). A kinematic hull moves only by its samples and its teleports. Its forces and impulses stay, because physics applies them only to a dynamic body.
- A frame that hangs at the origin of its orbit frame and is not kinematic in the next step leaves that frame falling. The numeric update of step 6c moves the hull with its frame into the home that its new motion gives it (see "Numeric frames").
- The change sets no velocity of the frame. So a parked frame keeps its old velocity in the tree for one tick, while its static hull holds still and its pose stays where the step left it. At the next sync point, in step 2, a static hull without a step record holds still, and the frame's velocity becomes zero. The frame keeps its proper acceleration, so the cargo of a ship that is parked on the ground keeps its weight.

In physics, the hull keeps its body ID, its system and its contacts through every change. It takes the frame's pose and velocity from the tree. So a hull whose engines come on coasts on with the velocity of its last sample. The ghosts of the hull meet what a body of its new motion meets (see "Ghosts"). A kinematic hull carries the frame hulls that rest on it, for example a small ship on a turntable whose bubble is small. Loose bodies never touch a hull: they enter its frame's bubble first.

So a change of motion is a swap of components plus a change of the body's motion, in one tick:

| Change | What gameplay does in one tick | What happens |
|---|---|---|
| Park | `SetMotion(Static)` | The hull holds still where the step leaves it, and its frame stops in its semantic parent. |
| Unpark | `SetMotion(Dynamic)` | The hull falls or coasts from rest. |
| Engines on, Kepler to dynamic | Remove `KeplerMotion`, then `SetMotion(Dynamic)` | The hull coasts on with the velocity of its last sample. Its orbit frame falls on. |
| Into orbit, dynamic to Kepler | Add `KeplerMotion`, then `SetMotion(Kinematic)` | The hull holds still in its physics home for one step, as a kinematic hull without a sample does. Then the component's first run teleports it onto its orbit. |
| Kepler to static | Remove `KeplerMotion`, then `SetMotion(Static)` | The frame stops. The numeric update moves the hull out of its orbit frame into the semantic frame's own system. |
| A new orbit or spin | Replace the rails component | The new component's first run teleports the hull onto its rails at the start of the step. So physics never sweeps the hull across the gap. A hull in an orbit frame jumps with that frame, and the frame's other contents keep their places in the semantic frame. |

A swap of rails components has no gap: the old component's last sample covers the step before the new component's first sample. Outside a tick, a game adds and removes components with `World.AddComponent` and `World.RemoveComponent`.

`SetMotion` checks its call with always-on guards. It throws an `InvalidOperationException` for a body that is no hull: an entity without a body, one that owns no frame, and the owner of a root frame. It throws an `ArgumentException` for a motion that the hull cannot take. `BodyDesc.Problem` with the role `BodyRole.Hull` holds these rules, and the world checks the same rules at spawn. A hull is not a character and not a sensor. A dynamic hull has mass. A body whose shape is or holds a mesh is always static, so only its ghosts move. A root frame has no parent, so its owner has no hull. The body of its owner is only the static interior of the frame (`BodyRole.OwnsRootFrame`). So the rules of motion and mass for a hull do not apply to it.

A motion that the hull cannot take comes only from a bug, because the world checks the motion at spawn and when the owner writes it. Physics then breaks a contract in a Debug build. A Release build keeps the motion of the hull and records the bug in `World.Errors`, in every step that finds that motion. "Output that only a bug produces" under "Physics" tells how. Physics gives the body of each hull the motion of each step at the start of that step. A body that the World creates between two steps, at a spawn or a rehome, starts with the motion of the next step, which it runs first.

### Rails components and the sample table

In each tick, a rails component hands the engine one motion sample, `MotionSample` (in `Tertius.Frames.Rails`), with `self.MoveOnRails`. A sample describes the frame's motion over the step in which the tick's intents act, which ends at `self.TimeAt(self.Tick + 2)`: the pose at the end of the step, the chord velocity over the step, the instant velocity at its end, the acceleration over the step, the angular velocity and the angular acceleration, all in doubles and relative to `self.Frame`, and the proper acceleration in the frame's own axes. `Self.TimeAt` gives the same double that `World.Time` holds in that tick, so a place on an orbit matches the World's time to the last bit. The engine never calls into a component at the sync point: the solve runs in the parallel part of the tick, and the sync point only copies samples. A sample is a pure function of the component's description, the time, the step, the frame's place and the parent's gravity in the front snapshot, so the result does not depend on the order of the chunks. The design is in `docs/plans/rails-components.md`.

The sample table (`MotionSampleTable`, reached as `FrameTree.Samples`) is indexed by the frame's index. Each row holds the frame that it moves, with its generation, the frame that it is relative to, and what made it (`FollowKind`): `Rails` for a sample that the owner wrote, `Physics` for the record of a frame whose hull is dynamic, and `Static` for any other record. It has three buffers:

| Buffer | What it holds |
|---|---|
| Written | The samples that gameplay writes in this tick. The last write in a tick wins. |
| Applied | The samples that the physics step of this tick applies, which gameplay wrote one tick before. Physics gets this buffer in `PhysicsStepContext.Samples`. |
| Followed | The step record of each frame that moved in the step that ended. The numeric update reads it. `FrameTree.Followed` reads the kind of its row: how the frame followed its owner at the last sync point, `Static` for a frame without a row. A state dump reports it as `follows`. |

The steps of the sync point that touch the table:

- Step 2: `FollowOwners` is the one loop in which every frame takes its step record, a `MotionSample`. It runs in the tree's dense order, whatever the owner of each frame is. Physics releases the record of every hull that moved. A dynamic hull's record comes from Jolt's state right after the update. A hull that handed off in the step has its record relative to its new parent, in the new parent's axes: the acceleration is the one that made Jolt's chord there, and the angular acceleration of the update turns into the new axes by the rule of `FrameMotion`, with which step 5 converts it. A kinematic hull's record is the sample that it applied, with every field exact. A static hull, or a kinematic hull without a sample, that teleported has a record at the teleport's pose, at rest in its home. A frame without a body takes its applied sample, and then its owner's write of its transform in the tick, which wins for one tick. A frame without a record holds still in its tree parent. The tree keeps the record's instant velocity for every frame. Under a numeric home it keeps it relative to the home's frame velocity, the chord of the step, so that a read in the semantic parent gives the instant velocity. Physics keeps a velocity relative to the home's instant velocity. One term converts between the two: the home's instant velocity past its frame velocity at the end of the step (`FrameTree.HomeInstantVelocityOverChord`, zero in a semantic home). Every record that physics releases adds it, a frame that holds still in a numeric home keeps it (`FrameTree.HoldStill`), and a velocity that gameplay gives a rigid body or a character loses it when physics makes the body's chord. A falling frame's term is half a step of its acceleration. An orbit frame whose hull has a sample for the step keeps the sample's instant velocity less the sample's chord as its term, exactly, beside its step acceleration (`FrameTree.BeginNumericStep`): half a step of the orbit's pull would miss the change of the pull over the step. A hull at the origin of its orbit frame rests there, so its record is that term alone, and every other frame in the orbit frame reads its velocity in the semantic parent relative to the hull's. Each record goes into the frame's slot of the applied buffer, unless a sample is there already. A record from physics is relative to the frame's tree parent, so the record of a frame in a numeric home converts into the frame's semantic parent as it goes in, by the rule of step 5.
- Step 4: first, a hull that is not kinematic in the next step drops its row in the written buffer (see "Changes of motion"). Then the buffers move on, as the intents swap. The applied buffer becomes the followed buffer, the written buffer becomes the applied buffer, and the new written buffer starts empty. A round number that every row stores makes this cost no work per row. So a frame whose component writes no sample in a tick has no sample in the step that follows, and holds still.
- Step 5: before a frame dies, each row of the applied and the followed buffers that is relative to it converts into the frame's new parent (`MotionSampleTable.ConvertInto`). The pose, the velocities, the angular velocity and the angular acceleration convert with the motion of one frame relative to the other, by the rule of a hand-off (`FrameMotion`), so a sample and a body that hands off convert by one rule. The acceleration converts with the acceleration of one frame relative to the other (`FrameTree.AccelerationRelative`). The tree keeps the acceleration of a frame in its parent only for a numeric frame: its step acceleration. So an acceleration in a numeric home converts exactly into the semantic parent. A semantic frame between the two frames adds no acceleration of its own yet, because only its step record holds that. This is a known gap: the numeric update can take the acceleration of a converted applied sample as the step acceleration of an orbit frame. A turning frame adds no rotation terms, and the angular acceleration only turns. Spin gravity brings the rotation terms (docs/plans/spin-gravity.md, "Conversions between frames").
- Step 7b: no row needs a conversion, because only a dynamic hull moves between frames at a sync point. Its owner writes no sample, and a hull that stops being kinematic dropped the sample of the tick at step 4. A contract checks it in a Debug build.

In physics, `MoveOnRails` of a kinematic hull acts as a kinematic target. Physics aims each kinematic hull that has a sample at the sample's pose at the end of the step. It does this after the intents, so that a teleport in the same step applies first. Right after the Jolt update, physics puts each such hull exactly on its target (`SetRPositionAndRotation`, without a change of activation). This snap comes before the contacts, the hand-offs and the ghosts read the end of the step. Jolt reaches a kinematic target only to the float rounding of the velocity that it gives the hull, about 4 µm per step at 7.6 km/s. With the snap, a place on an orbit stays exact. A kinematic hull without a sample holds still in its tree parent. Physics predicts every frame at the end of the step from its step record: a ghost moves with the record's chord velocity, and a hand-off carries its instant velocity. A frame without a record predicts its tree pose with no velocity.

A new frame stands at the pose of its spawn description. It holds still there for its first step, as a frame without a sample does. The first run of its rails component writes its first sample. It also teleports the hull to the rails pose at the start of the step that applies the sample. So a frame on rails stands on its rails from the second sync point after its spawn. A swap of rails components leaves no step without a sample.

### Gravity

Each frame has one gravity field. `GravityField` is a custom C# 15 union, as `Target` is, so a field holds the values of its own case and nothing else. (User) The cases are `UniformGravity(Acceleration, Region)`, which is artificial gravity with an optional region box, and `PointGravity(Mu)`, which is a planet. The empty union is no gravity, and it is the default. So a uniform field cannot carry a G·M, and a point field cannot carry an acceleration. The factories `GravityField.None`, `GravityField.Uniform` and `GravityField.Point` make a field. Code reads a field with a `switch` over the cases or with `TryGetValue`, and neither boxes. An object feels only the field of the frame that it is in. This is the one-dominant-body rule.

The effective gravity on a body inside a frame is the frame's own field minus the frame's proper acceleration, rotated into frame axes. As a result, loose objects inside an accelerating ship slide toward the stern.

A game gives a frame a new gravity field between ticks with `World.SetFrameGravity`. The sleeping bodies of the frame wake at the next step. The world refuses a field that is not valid, at this call and at spawn. Such a field has a value that is not finite. A negative G·M is not valid either. `FrameTree.SetGravity` is internal to the simulation. The `KeplerMotion` of a child frame reads the G·M of the new field in its next tick. So a new G·M moves the child from its next sample on. A field without a G·M holds the child still.

### Bubbles and nesting

A frame's bounds are an ellipsoid or a sphere. No other shape is allowed. The bubble decides ownership: which frame an object belongs to. The physical shape of a frame in its parent frame is its real hull. This lets a ship land on a planet with its real hull.

A frame that fits inside a sibling frame enters it, like any other object. A frame fits inside another frame in one case: its largest bubble radius is less than half of the smallest radius of the other bubble. A fighter that flies into a carrier's bubble becomes a child of the carrier. Two frames that do not fit inside each other stay siblings. Their real hulls collide in the parent frame. If their bubbles overlap, objects in the overlap stay in the frame that they were in, and each frame's hull is ghosted into the other.

Docking two frames of similar size merges them explicitly into one frame, and undocking splits them again.

### Destroying a frame

When a frame dies, its contents move to the nearest ancestor frame that survives. Pose and velocity convert through the tree, each moved entity gets `FrameChanged`, and its body moves to the new frame's physics system. Child frames move up the same way. If no ancestor survives, as with a root frame, the contents die with it. The samples of a child frame convert into its new parent with it, and its `KeplerMotion` orbits the point gravity of the new parent from its next tick on. If the new parent has none, the component has no G·M to orbit, so it holds the frame still and logs once. The game caused it by destroying the parent, so it is not an error.

### Hand-off

An object belongs to the deepest frame whose bubble it is in. These rules control its moves between frames:

1. The first contact of its bounding sphere with a child's bubble moves it into the child frame. The hull is inside the bubble. So the object is in the child frame before any part of it can reach the child's real hull.
2. It leaves a frame only after its bounding sphere is fully outside the bubble, plus a margin. The margin is the larger of 2% of the bubble radius and the object's radius. This gap stops the object from moving back and forth every tick.
3. If it touches two sibling bubbles at once, it enters the one that it penetrates more deeply.
4. Physics checks after each step, on the physics thread.
5. The pose converts in double precision. Velocity converts as `v' = R⁻¹(v − v_frame − ω × r)` on entry, and the reverse on exit. No Coriolis force acts, but the conversion keeps the ω × r term. An object thrown out of a spinning station then leaves with the correct velocity. Angular velocity converts as `ω' = R⁻¹(ω_body − ω)`. The angular acceleration of a dynamic hull over the step converts as a direction, `α' = R⁻¹α`. `FrameMotion` holds these rules, and step 5 of the sync point converts a sample with the same type. The full rule for the angular acceleration, the change of `ω'` over time, also has the frame's angular acceleration and the term `ω × ω_body`. Spin gravity brings both terms to `FrameMotion`, which serves both places.
6. The entity handle does not change. The body moves from one physics system to the other. The entity moves to the new frame's topic and gets a `FrameChanged` event.

A numeric frame (see "Numeric frames" below) has no bubble. A body whose system is a numeric frame enters and leaves bubbles as a body in the system of the numeric frame's semantic frame does, at the same places and into the same frames. A crate thrown at 1,000 m/s inside a ship, beyond the merge test's reach of the interior, falls in a numeric frame below the ship. When it is fully outside the ship's bubble plus the margin, it moves into the ship's parent in the tree and gets a `FrameChanged` event. (ter-kz89)

Physics follows the frame tree, and the tree is the authority. Each hand-off is a proposal that the sync point checks and applies in step 2. A correct backend never proposes a hand-off that the tree refuses, so a refusal comes only from a bug. "Output that only a bug produces" under "Physics" tells what the sync point does then.

At the start of every step, physics reconciles with the tree. First it drops every hand-off of a frame from the last step, whether the tree took it or not. Then it puts the hull of each frame into the physics system of the frame's parent in the tree. A hull that changes systems this way restarts from the frame's pose and velocity in the tree. So after a refused hand-off, the hull loses the motion of the refused step. The same reconcile follows the changes that the game makes to the tree between ticks, such as a reparent that docks a ship. A change of a hull's motion changes no structure of the tree: right after the reconcile, physics gives each hull the motion that it has in the step, from the change in the step's intents or else from the stored motion (see "Changes of motion"). An edit of a frame's pose with `FrameTree.SetLocalPose` changes no version of the tree, so the reconcile does not see it. So at the start of each step, physics compares each static hull with its frame's pose in the tree, and puts a hull that differs at that pose. This check runs before the intents. So it never moves a hull back from its owner's teleport, which the tree learns only at the next sync point. Physics also reconciles each time that the simulation gives it a batch of structural changes. The numeric update hands physics its last batch at every sync point, also an empty one, so physics follows the tree at the end of every sync point.

If the system of the parent is full, that system grows first. "Physics systems that grow" under "Physics" tells how. So every hull gets its body in the system that the tree names.

### Ghosts

While an object's bounding sphere crosses a bubble boundary, the other physics system holds a ghost. A ghost is a kinematic body that copies the real body's pose every step, and that Jolt moves with the real body's velocity during the step. The one exception is the ghost of a mesh while it holds still in its system, such as the ghost of a parked station's hull in a parked ship beside it: that ghost is static, as its real body is, so Jolt never simulates it. When it starts to move, it turns kinematic, and when it holds still again, static again. Jolt cannot compute the mass of a mesh, so physics gives each ghost of a mesh a mass of 1 kg, which Jolt reads only for a dynamic body. (ter-dq92) Bodies in the other system collide with the ghost, but the ghost does not push back on the real body. After the object is fully on one side, physics removes the ghost. A contact with a ghost is a contact of the two entities (see "Contacts"). Physics sets the pose and velocity of each kinematic ghost in every step, and Jolt resets the sleep timer of a body whose pose is set, so such a ghost never falls asleep. Jolt puts an awake kinematic body in the island of each dynamic body that it touches, so a body that rests on such a ghost never falls asleep either. In return, Jolt finds every contact of the ghost in every step, so a held pair with it never ends while the two touch. A ghost stands for its real body, so a ghost of a static body, such as the hull of a parked station or an anchor, meets only what the static body meets. Jolt makes a contact of a static body only with a dynamic body, or with a kinematic body when the static body is a sensor. The ghost of a static body is kinematic, unless it is a mesh that holds still, so that a dynamic body that hits it meets a body that moves as the real body does in the ghost's frame, and Jolt also makes a contact between that ghost and every sensor. Physics does not report those contacts. So a kinematic sensor never detects the ghost of a static body, as it never detects the static body, and numeric frames, whose anchors have such ghosts, change nothing. When a hull changes its motion, for example when its owner parks it, its ghosts follow the new motion at the start of the next step. (review of ter-ejx9)

A ghost is only in the system of a frame next to the body's frame, the semantic frame of the frame whose system holds the body: its parent in the tree, a sibling or a child. The siblings and the children are semantic frames, wherever they hang among numeric frames. Numeric frames have no bubbles, so they do not change which bubbles a body, a hull or an anchor touches, and each gets the same ghosts in semantic frames as it gets without numeric frames. So a crate that falls in a numeric frame below a ship and sticks out of the ship's bubble has a ghost in the ship's parent, and in each sibling of the ship whose bubble it touches (ter-kz89). A hull that is not dynamic, such as the hull of a parked station, never enters a bubble, but it has a ghost in each bubble that its own bubble overlaps. The home of a group that links a Kepler hull in an orbit frame of its own and an anchor at rest, or an earlier such hull, keeps a ghost of the hull, with the hull's own layer (see "Numeric frames" below). The ghost of a body that sticks out of a frame is in the frame's tree parent, the home of the frame's hull. The numeric update links each body that can touch that ghost to the hull, so that body lives in the same home. So no other system needs a ghost of the body that sticks out (ter-4psc). The one exception is a frame whose Kepler hull lives apart from its group. Each home that keeps a ghost of that hull also keeps a ghost of each body that sticks out of the frame. The numeric update can stop naming a home for the ghost of an anchor without a change of the tree. Physics then removes the ghosts that the tree no longer allows there, at the same sync point (`JoltPhysicsBackend.DropWithdrawnAnchorGhosts`), in the update's last batch, which goes also when it is empty. A frame that leaves the tree takes these homes with it, and so does an orbit frame that loses its orbiter. The ghost of a hull is never in the hull's own frame. When the tree changes, the next reconcile removes each ghost that the new tree does not allow. The ghost pass at the end of the next step adds the ghosts that the new tree needs. A move of a numeric update that takes a frame's hull into another home takes the ghosts of the bodies that stick out of the frame along at once, because the frame's tree parent is the hull's home (review of ter-4psc).

### Numeric frames

The engine makes and removes frames of its own around groups of bodies, only for precision. These numeric frames keep Jolt's float velocities and float bounds small. Near a ship in orbit, every body moves at about 7.7 km/s in the frame of the planet, and a float velocity there steps by 0.0005 m/s. The design is in `docs/plans/numeric-frames.md`.

| Question | Rule |
|---|---|
| Where a numeric frame lives | In the frame tree, with the `NumericMode` `Falling`, `Resting`, `Nesting` or `Orbiting`, and a Jolt system of its own. It has no owner, no bubble, no gravity of its own and no rotation. |
| What gameplay sees | Nothing. `Self.Frame`, `World.Frames`, `Snapshot`, `FrameChanged`, the topics, `QueryRadius`, the state dumps and the render snapshot's camera frame show only semantic frames, the frames that content or gameplay made. The public members of `FrameTree` answer relative to `Parent`, the semantic parent, and list frames in an order that numeric frames do not change. The members that show the tree as it is stored are internal. |
| The entity row | `Frame` is the semantic frame. `PhysicsHome` is the frame whose Jolt system holds the body. `Pose` is in `Frame`, `HomePosition` is the position in `PhysicsHome`, and `LinearVelocity` is a float relative to `PhysicsHome`. |
| Reads | Every read of a position or velocity names its frame. A read in another frame starts from the physics home and composes through the lowest common ancestor. |
| Writes and directions | Relative to `self.Frame`, in its axes. Physics converts a write when it applies it. |
| Velocity | Jolt's float local velocity plus the double frame velocity of the home, added in doubles. |
| Who gets one | A group of free bodies only gets a falling frame from a common speed of 1,000 m/s, and keeps it down to 400 m/s. Free bodies coast, collide, push each other or fall together. A group with a body that thrusts gets a falling frame from 50 m/s and keeps it down to 20 m/s. Every character thrusts. Any other body thrusts after 0.5 s of ticks whose force and impulse intents ask for at least 1e-3/60 m/s². So a single impulse never counts. Such a body keeps thrusting for 20 s after its last such tick. A slow group 2^23 m (8,389 km) or more from its semantic frame's origin gets a resting frame. It keeps the frame down to 2^22 m (4,194 km). Below the speed and the distance to give up its frame, a group goes back to the semantic frame's own system. (ter-3xs6) |
| How a falling frame moves | It falls freely in the semantic frame's field, integrated in doubles. Each member feels the field at its true position minus the frame's acceleration, so the tidal gravity stays. |
| Membership | At each sync point, in entity order, in `FrameTree.UpdateNumericFrames`, the only code that makes, shifts, moves or destroys numeric frames. Two bodies that can touch within 2 s share a frame. Two bodies of one system, a numeric frame's or the semantic frame's own, stay linked 10 m farther. Members split only after 1 s apart, and a body that the semantic frame's own system keeps leaves it for a new resting frame only after 1 s. The group with the most mass in a frame keeps it, and the smaller groups move. The merge test measures two bodies from their places in their homes, through the homes' lowest common ancestor. |
| Anchors | An anchor is a static body, a hull that is not dynamic, or the interior of a frame owner, which physics keeps as a static body in the frame's own system. The merge test links every pair of bodies, an anchor to every group, also to one that falls, and the group takes the anchor's home: an anchor counts as infinitely heavy (ter-dhkf, `docs/plans/numeric-frames.md`, "Anchors"). A group with a static body never falls, and it gets the mode, the center and the frame of its static bodies alone: their resting frame when they lie far out, or else the semantic frame's own system. A group with a static hull, or with a slow kinematic hull near its semantic frame's origin, or with the interior of a frame owner, stays in the semantic frame's own system. So a fast body within the merge test's reach of its frame's interior runs in the frame's own system. A kinematic body is no anchor, but it counts as infinitely heavy too: a group with a kinematic body and no static one has the velocity and the center of its kinematic bodies alone, so a still door keeps a fast crate's group from falling. The hull of a kinematic frame that its samples move fast or far lives in an orbit frame of its own (see the next row). Fast or far means from 1,000 m/s or 2^23 m, the thresholds of free bodies. A group without an anchor at rest takes that orbit frame. So the anchor is a real body of the group's system, and Jolt sweeps a fast body against it: before, the anchor kept a kinematic ghost in the group's falling frame, which has no linear cast, and a crate at 60 m/s passed through a thin wall (ter-ykqs). A frame that holds a static body of a group, or the orbit frame of its hull, has the mass +∞, which only comparisons read, so no sum gives NaN. A group lets go of an anchor when the merge test no longer links them. The split cooldown holds only the members of a numeric frame that are not anchors. Two anchors whose homes are at rest in the semantic frame share the home that the group takes. Two anchors that move differently, a kinematic hull in its orbit frame and an anchor at rest or another such hull, cannot share a home: the anchor at rest wins, and among orbit frames the one of the first hull in entity order. Each other hull stays in its orbit frame, and the group's home keeps a ghost of it (`FrameTree.AnchorGhosts`), which has the hull's own layer, so it meets only what the hull meets. That home also keeps a ghost of each body that sticks out of the hull's frame, with the ghost layer. |
| Orbit frames | An orbit frame (`NumericMode.Orbiting`, ter-dhkf rule 3) lies in its semantic frame and holds the frame of its kinematic hull, its orbiter, at its origin, with the rotation of the orbiter's sample. At each sync point, in step 2, it takes the place of its orbiter's sample, to the last bit, with the sample's chord over the step as its velocity. In a step it falls with the acceleration of the sample that the step applies, as a falling frame falls, and so ends the step on the sample's place. When its orbiter has no sample, it falls by its semantic frame's field, as a falling frame, and the orbiter holds still at its origin, so a read in the semantic parent gives the orbiter the orbit frame's instant velocity. The chord is the velocity that Jolt gives a body on the orbit, so the bodies in the frame move as they do in the semantic frame's own system. It never drains, moves its origin or nests, and only its hull's group keeps it. The tree keeps the orbiter's velocity relative to it as the rest of the orbit's velocity, so that a read in the semantic parent gives the orbit's velocity. When the orbiter leaves it, by a change of its hull's motion, a hull that no longer needs a frame, or a destroy, the orbit frame falls on as a falling frame. When the orbiter's hull teleports, as with the first sample of a new rails component, the orbit frame jumps to the sample's place with it at the next sync point, and the other bodies and frames in it keep their places in the semantic frame (`FrameTree.JumpFollowingFrame`). |
| Ghosts in the merge test | Everything that a group cannot move counts as infinitely heavy, ghosts included (ter-4psc, `docs/plans/numeric-frames.md`, "Ghosts as infinitely heavy"). The update of a semantic frame P takes the places of the ghosts in P's frames as inputs (`GhostPlace`). A body of a semantic child C that can stick out of C's bubble before the next update leaves a place in C's tree parent. That tree parent is the home of C's hull. A body of P's semantic parent that can touch P's bubble before the next update leaves a place fixed in P's own system. So does a body of a sibling of P that can stick out of its own bubble and touch P's bubble. These tests grow each body by 2 s of its speed, and by at least the split margin of 10 m, so a crate that rests 1 mm inside its bubble gives a place whether it sleeps or not. A first test on distances alone skips each body of P's parent and each whole sibling that cannot reach P's bubble. The merge test links a body of P to each place that it can touch, as it links two bodies. A body linked to a place in C's tree parent joins the group of C's hull. A body linked to a fixed place keeps its group in P's own system. The ghost lives in the hull's home wherever the update moves the hull, so the body meets the ghost there. Only a body that meets the ghost is linked to its place, as physics has it. A frame hull never meets it, because the mask of a hull does not name the ghost role. A dynamic body or a character always meets it. A static or kinematic body meets it only when one of the two is a sensor, and the ghost of a static body meets only what that body meets. A body in the place's system stays linked 10 m farther. Before, a numeric frame of P kept a ghost of each body that sticks out of a child near its members (ter-2gg0). That ghost came at the members fast, with no linear cast. A body that sticks out of a frame whose tree parent is numeric also had a ghost in the semantic parent's own system (ter-wllb). Both are gone. |
| Drain and recenter | A frame takes over its members' common velocity above 1 m/s, and moves its origin to their center of mass beyond 1 km. Both are uniform shifts, so Jolt keeps its contact cache. The ghosts in the frame's system shift too, and wake nothing. A static ghost, of a mesh that held still in the frame, moves after a drain, so the drain turns it kinematic, and Jolt moves it in the next step. Each velocity loses the shift in doubles before it narrows to a float. |
| Moves between systems | A whole touching group moves at once, with no `FrameChanged` and no topic change. A sleeping body stays asleep unless the move changes its velocity. A body that physics has not created yet, such as a spawn, is created in its numeric frame, with its state converted in doubles. A moved body keeps its ghosts in the systems of other frames, because every frame where a ghost can be depends only on the body's semantic frame, which a move does not change. So a fast ship that joins the system of a station's hull while their bubbles overlap keeps its ghost in the station. A moved hull takes along the ghost of each body that sticks out of its frame, which lives in the hull's home, with the ghost's place and velocity in the semantic frame. So a rock that meets a crate's ghost in the step after a ship starts to fall stops at it, as without numeric frames. |
| Frame owners | A hull that joins a numeric frame takes its frame under that numeric frame in the tree. The frame's semantic parent does not change. A frame with a static hull or without a body lies in its semantic parent, and a frame with a kinematic hull lies in its semantic parent or at the origin of its orbit frame. When the frame hangs in another numeric frame and its owner writes a change of motion, the numeric update of step 6c of that tick's sync point plans the next step with the new motion, and moves the hull with its frame into the home that the new motion gives it. The frame keeps its place, so the merge test sees the hull where it is. The move carries the old home's velocity into the frame. So a parked frame keeps that velocity in the tree while its static hull holds still, and the velocity becomes zero at the next sync point (see "Changes of motion"). A semantic frame that dies takes its numeric frames with it, and the frames in them move to its parent. Every frame that moves up this way goes to the dying frame's semantic parent, whatever the motion of its hull, where the World also moves the frame's hull. The next numeric update moves the hull and its frame into a numeric frame when the group needs one, so only a frame with a hull hangs in a numeric frame. A room without a body in a station on rails that dies in its orbit frame holds still in the station's semantic parent, and so does a room without a body in a dynamic ship that dies in a falling frame. No numeric update moves such a room. Before the review of ter-4psc, a frame that the engine marked as moved by physics went to the dying frame's tree parent, and such a room fell on in the numeric frame. |
| Vast distances | A numeric frame more than 10^9 m from its tree parent's origin moves under a nesting frame. A nesting frame rests on a grid of 2^20 m in its semantic frame, so its position is exact in a double, and nearby frames share it. It jumps by whole grid steps after its frames, and they shift back by the same exact amount. |
| Render snapshot | It places everything relative to the camera's physics home, which has the axes of the camera's frame. |
| Thresholds | `NumericFrameOptions`, through `WorldOptions.NumericFrames`. The precision measurements of ter-kugq set the speeds, the thrust clock and the distances (`docs/plans/numeric-frames.md`, "Thresholds from measured precision"). Bodies that move together keep their gaps nearly exact up to 1,000 m/s. A body that thrusts drifts from its neighbors by about 2 mm/s per minute of thrust at 50 m/s. Distance added no error up to 9,000 km. |

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

If a body that Jolt simulates touches a sleeping body, Jolt wakes the sleeping body. Nothing else wakes it. So before physics removes a body, teleports it, or moves it without a simulated step, physics wakes the bodies around it. Examples of such a move are a static hull that the game moves through the frame tree, and a static hull that its owner teleports. Others are a hull that the reconcile puts in another place, and a ghost.

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

A dynamic hull in orbit is integrated with float velocities, so its orbit drifts slowly. A frame that coasts for a long time uses a kinematic hull and a `KeplerMotion` instead.

Jolt keeps rotations as float quaternions, and the square of the length of one is up to `2E-7` away from 1. So the backend normalizes every rotation in double precision where it leaves Jolt, because the hull's rotation becomes the local pose of its frame.

The character controller is a capsule. It uses the frame's effective gravity. Physics stands a character only on walkable ground under gravity. That is, the effective gravity is at least `FrameTree.ZeroGravityThreshold`, 0.05 m/s², Jolt found ground under the character that is not too steep, and the character does not move away from that ground. A standing character inherits the velocity of the surface under it, walks with the part of its desired velocity across its up, and can jump. A character that does not stand is weightless, in any frame and any gravity, because it falls freely or no gravity acts on it. (User) It keeps its momentum, and gravity, forces and impulses change its velocity. Walking and jumping do not move it. The ground holds a standing character against gravity, so gravity does not change the velocity of a standing character, except in the step in which it jumps, and a character that stands still on a floor has no velocity. Gravity in that velocity would press the character into its ground in every step. Jolt then reported a false velocity of one step of gravity, and it pushed each dynamic body that the character leaned on a little in every step, so a stack that a character stood against woke each time it fell asleep. Forces and impulses change the velocity of every character. Physics keeps the desired velocity of a walk, which applies once the character stands. After each step in which a character in the walking form (see below) did not stand, it loses the part of its velocity that goes into each static or kinematic surface that it hit. So a character that jumps into a low ceiling falls back at once. That part is relative to the surface, so a wall that moves into the character gives it the wall's velocity. Against a dynamic body the walking form keeps its momentum and pushes the body with Jolt's push, which is capped at `CharacterMaxStrength` and takes no reaction from the character (ticket ter-w7cw). The direction into a surface is the face's own normal if Jolt's contact normal lies within 10 degrees of it, and Jolt's contact normal otherwise, because Jolt's contact normal can lean several degrees on a flat face, while at an edge or a corner the face's normal would cancel motion that nothing blocks. Jolt pushes a character out of a surface that it sank into by moving it, so that push gives the character no velocity.

A character has two forms (ticket ter-1w06). In the walking form it is a Jolt `CharacterVirtual`, as the paragraph above describes. When the effective gravity at it falls below `FrameTree.ZeroGravityThreshold`, physics replaces it with the rigid form: a dynamic Jolt body with the character's material, its mass and no damping, whose rotation is locked. Jolt's solver handles every contact of the rigid form as it does for any rigid body, with friction. So a weightless push conserves momentum and has no strength cap: a weightless person who presses into a crate moves the crate and the person together, and a person and a crate that drift together keep their places. Forces and impulses act on the rigid form. A walk and a jump do not move it, as they do not move any character that does not stand, and a kinematic target and a spin do nothing to it. The rigid form keeps the character's up and rotation. It never stands. When the effective gravity reaches twice the threshold, 0.1 m/s², physics turns it back into the walking form. Between the two values a rigid character that touches nothing keeps its form, so a floating character whose gravity hovers near the threshold switches once. The band is wider than the noise of a frame's proper acceleration, which physics measures from float velocities. A rigid character that touches something, as one that lies on a floor does, walks again from the threshold on, because only the walking form stands, and in free flight the two forms move alike. So between the two values a character that touches something has the walking form, whether it came from a weaker or a stronger gravity: a person who lies weightless on a floor when a weak drive starts stands and walks, as one who spawned there does. A ship whose thrust dips below the threshold makes its standing crew rigid for that step. Physics decides the form of every character at the start of each step, from the gravity at it, before it applies intents and gravity, so each step acts on one form. A switch keeps the frame, the pose, the velocity, the up and the contacts: the contacts of the old body carry over to the new one, as in a hand-off, and both forms end a held contact by one contact list (see the contacts of a character below), so no `Collision` or `CollisionEnded` comes from a switch. The rigid form's capsule reaches as far as the walking form. It is 0.02 m wider, the gap that a Jolt character keeps from a wall at its side, and its cylinder is as much shorter, because a standing Jolt character rests its capsule on a floor. So a switch moves nothing. The rigid form takes the path of every dynamic body: gravity, hand-offs, moves between numeric frames, shifts, growth, ghosts, collision groups, sensors, contacts, queries and reports. Only the end of a held contact follows the character's contact list, as for the walking form. A character in the air under gravity keeps the walking form, but Jolt's plain update moves it, not the extended update that a standing character gets, which cancels the velocity into each steep surface that the character touched in its last update and sticks the character to the floor when it loses a touch.

Under gravity, the up of a character turns against the gravity whenever the character touches anything. That is, Jolt's ground state from the last update of the character is anything but "in the air": walkable ground, steep ground, or a contact that does not support the character. So a character that lands on its side or on its head turns upright, and then it can stand. A character in free fall that touches nothing keeps its last up. So a person who drifts out of a ship in orbit does not turn toward the planet. At a hand-off, a character that stood in its last step takes its up from the gravity of its new frame. Any other character keeps its up and its rotation, turned into the axes of its new frame. The hand-off reports the rotation that the character got in its new frame.

In each step, physics reports for each character whether it stood. A character that a hand-off moved has not stood in its new frame yet. Physics looks up the contacts of that character in its new frame at once, so a character that walks on across the boundary stands again in the next step. Components read the report as `self.Body.Standing`, and other code as `Snapshot.IsStanding`. Tick N sees the step that ran during tick N - 1. So the components that steer a character agree with physics on where weightlessness begins.

### Physics queries

A physics query that a component asks for becomes a `PhysicsRequest` in its chunk's list of physics requests, and the sync point runs it. A request holds what every request has: the asker, the frame that the asker was in, the entity query that says what may be hit, and the tag of the answer. It also holds a `PhysicsQuestion`, which says what the request asks. `PhysicsQuestion` is a custom C# 15 union, as `Target` is, and its cases are `RayQuestion(Origin, Direction, Length, Kind)`, `ShapeCastQuestion(Shape, Start, Direction, Length)` and `OverlapQuestion(Shape, Pose)`. The kind of a ray is `RayCastKind.Nearest` for `CastRay` or `RayCastKind.All` for `CastRayAll`. The cases share their private fields, and a case reads only its own. The World checks every argument and makes the direction a unit vector before it builds the question, so a request with the default question, which holds no case, comes only from a bug: a Debug build breaks a contract at the sync point, and a Release build gives it no answer and records the state in `World.Errors`. Every question has the same sweep, which the runner walks through the frames: a ball of radius `Reach` moves from `Origin` along `Direction` for `Length` meters. A ray reaches 0 sideways, and an overlap moves nowhere (its direction and length are zero) and reaches as far as its shape's bounding radius. `FirstHitOnly` is true for the nearest hit of a ray and for a shape cast. These sweep properties are `switch` expressions over the cases with no discard arm, so a new case breaks the build until it answers them. The runner reads the rest of what each case decides with a `switch` over the cases too: the line along which the walk tests the bubbles (an overlap, which moves nowhere, takes any line through its center), the Jolt call, whether an entity counts once however many of its bodies are hit (only for an overlap), and the answer event.

A shape cast and an overlap query take a `QueryShape`. It is a custom C# 15 union, as `Target` is, and its cases are `QuerySphere(Radius)`, `QueryBox(HalfExtents)` and `QueryCapsule(HalfHeight, Radius)`. The factories `QueryShape.Sphere`, `Box` and `Capsule` build them. A constructor does not check sizes. `QueryShape.Problem()` says why no query can use a shape, and the World refuses such a query at once, in the call that asks. The default value holds no case (`HasValue` is false), so `Problem()` refuses it, and its bounding radius is 0. The backend's `QueryShapeCache` makes one native Jolt shape for each shape value, with a `switch` over the cases. A capsule with a half height of 0 becomes a sphere there, because Jolt does not accept a capsule without a cylinder part.

### Contacts

If two bodies start to touch, physics reports the contact once, as a `Collision` to each of the two entities. A contact starts at a real touch: the first step in which the two bodies are at most 1 mm apart, or approach fast enough to close their gap within the step, so that the solver pushes them apart. Jolt finds a pair earlier, as soon as the bodies are within its speculative contact distance of 0.02 m. Such a pair can touch many steps later or never, so it starts no contact. (User) Each contact that starts ends later with exactly one `CollisionEnded` to each of the two entities. Physics reports the contact again only after it ended. Jolt finds contacts only for the bodies that it simulates, and it does not simulate a sleeping or a static body. A body can fall asleep on something static or asleep, or a hull can become static on a floor. If the rest of the system keeps stepping then, Jolt drops the contact. After the body wakes or moves, Jolt finds the contact again.

A bounce leaves Jolt's speculative contact distance (User). When a landing step begins within that distance, Jolt gives a bouncing body its rebound at once, from where it is. So a small bounce from a step that began near the surface stayed within the distance, while the same bounce from a step that began near its edge left it: one drop reported one landing or two, by where its landing step began. Physics therefore applies a minimum rebound. A contact does not bounce when the bounce that Jolt would give it rises less than 4 cm, twice the distance. Jolt applies the accumulated forces at the start of a step, so it takes the speed that they added off a rebound: the rebound is the combined restitution (the larger of its two bodies') times the approach speed before the solver runs, less the pull toward the surface along the contact's normal times the step. The pull comes from the forces and inverse masses of the dynamic bodies, and gravity is one of those forces. A numeric frame falls with its semantic frame's gravity, so its bodies get only the gravity that it leaves, and a ghost or kinematic body there comes at them at the frame's acceleration, which Jolt does not see. So in a numeric frame the rule counts each body that is not dynamic as accelerating at the frame's acceleration backward, which gives the pull of the semantic frame, and numeric frames change no decision. Under that pull the bounce rises the rebound squared over twice the pull, so the limit grows with the pull: at 1 g the slowest rebound that remains is about 0.89 m/s, and with no pull toward the surface every bounce remains, because it never comes back. The rule reads what Jolt simulates: Jolt's own velocities, in which every ghost moves as its real body does (ter-dq92), and the contact point that approaches fastest, because Jolt decides restitution at each point and a report sets one restitution for the whole manifold. Jolt computes the settings of each contact again in every step, so both of Jolt's reports of a contact, a new one and one found again, set the combined restitution of such a contact to 0 in every step. Jolt's linear cast of a fast body calls the same reports and uses the settings that they leave. The cast does not take the forces off its rebound, so the rule judges a cast's bounce a little short. A landing whose bounce is just below the minimum in its first step of touch can still bounce in the next step: the speculative contact lets the body reach the surface at almost its full speed, and the gravity of the next step raises the approach above the minimum. That bounce leaves the distance too. The first version of the rule asked for a rebound of 1 m/s without the pull at any gravity, which kept bounces inside the distance at 2 g and more and stopped slow bounces in zero gravity (review of ter-ejx9). The default restitution of a body is 0, as in Jolt, so a body bounces only when its content names a restitution. (ter-4kaa)

Physics holds each contact that Jolt drops. If Jolt finds a held contact again, the contact continues, and physics does not report it. A held contact ends at the first step that simulates one of its two bodies without finding the contact. So sleep and wake end nothing. For example, the game can lift a sleeping box off the floor. Its contact with the floor ends, and the box reports the floor again at its landing. In each step of a system, physics visits every held contact of that system.

A contact between the body of one entity and the ghost of another entity's body is a contact of the two entities, and it can start through the ghost. (User) A ghost stands for its real body, so the impulse of such a contact uses the masses of both real bodies, and it does not depend on which system reports the contact. Jolt moves every ghost with its real body's velocity, and the ghost of a mesh is static only while it holds still (see "Bubbles and ghosts"), so the test of a touch and the impulse read Jolt's velocity of the ghost, which is the real body's. (Before ter-dq92, the ghost of a mesh was always static, and physics put it at its real body's place after each step, so Jolt neither stopped nor bounced a body by the mesh's motion.) A pair of two ghosts is no contact: the real bodies' own systems report their contact. So the two entities of a contact can touch in several systems at once. For example, a body that sticks out of a child frame has a ghost in the parent, and a body in the parent touches that ghost, while that body touches the child's bubble and has a ghost in the child, which the first body touches. Physics counts, for each pair of entities, the body pairs that hold their contact as started, in every system. The contact starts when the count goes from 0 to 1, and it ends when the count goes back to 0. So the two entities hear one `Collision` and one `CollisionEnded`, however many systems hold the contact in between. When several systems start the contact in one step, the system of the frame with the lowest index gives the point, the normal and the frame of the `Collision`, so that the order of the systems does not matter. In each step, the body pairs that end count down after the ones that start, so a contact that one system drops while another system starts it goes on. (ter-ejx9)

A contact also ends when one of its bodies is destroyed. The other entity hears it, and the end names the entity that is gone. A body that leaves its system, by a hand-off, a move between numeric frames or a destroy and create at one sync point, keeps each contact in every system that holds a body of each of the two entities, real or ghost but not two ghosts, that can touch the other one. That is the new system when the other entity has its body or a ghost there, and the old system when the body sticks out of its new frame's bubble and leaves a ghost there. Two touching bodies that move together keep their contact, and so does a body that enters a frame ahead of a body that it touches. A ghost that physics removes takes its contacts along in the same way. Physics settles these contacts after the hand-offs and the ghosts of the step, so the new ghost of a body that left can carry them. A move between numeric frames happens at the sync point, and the ghost pass puts the moved body's ghosts at its new state, and makes the ones that its new place wants, only at the end of the next step, so its contacts wait until then. In each system that takes a contact over, the contact is held, and it ends as a held contact does. Every other contact of the body ends, with one exception. A hand-off happens in the step, and the numeric update of the last sync point chose the numeric frames for the bodies' places before the step. So after a hand-off, a contact with a body or a ghost in a numeric frame can be in no system, where without numeric frames one system takes it over. This happens in two ways. A body can enter a frame while it touches a body of the semantic parent. Among the frames of the semantic parent, its ghost is then only in the frame's tree parent, the home of the frame's hull. That tree parent can be a numeric frame. The last update linked the other body to the body that entered, not to the hull. So the other body can live in another frame of the semantic parent: a numeric frame, or the semantic parent's own system while the ghost is in a numeric frame. A body that leaves a frame whose Kepler hull lives apart from its group leaves into the hull's orbit frame. It drops its ghost in the group's home, where the bodies that the merge test linked to the hull live. The next sync point's numeric update links the touching bodies, or names the group's home for the ghost of the hull. So the rule is: a contact that no system takes over waits with its count until the end of the next step, as the contacts of a move do, when a hand-off of the step moved one of its bodies and one of its bodies, or a ghost of one, is in a numeric frame. A contact that really ended at the hand-off ends one step late. Without numeric frames, no body and no ghost is in a numeric frame, and the system that takes the contact over is the semantic parent's own, so the two entities hear the same events either way. (reviews of ter-ejx9 and ter-2gg0, and ter-4psc) A system takes a contact over only for two bodies that Jolt can find: their layers meet, so a hull never takes over a contact with a body that moves, and Jolt makes a contact only when one of the two bodies is dynamic, or when one is kinematic and the other is a sensor. A character's own collisions find every body. A ghost is kinematic, or static for a mesh that holds still, and a ghost of a static body meets only what that body meets. So a ghost takes over a contact only with such a body. A held pair that Jolt can never find would end a step late, or never while both of its bodies sleep. For example, a crate that slides on a static floor into a bubble ends its contact with the floor at the hand-off: its ghost on the floor's side cannot touch the floor. An interior never takes over a contact of its owner's hull: the hull lies inside its frame's bubble, so a body is in the frame before it can touch the hull.

Jolt reports the contact of a pair that it found before in every step, which is a call from Jolt into C# for every contact of every simulated body. Only a pair that Jolt found without a touch needs these reports. So physics hears Jolt's contacts through its own contact listener of the C API, whose callbacks are C# functions that Jolt calls directly, and not through the contact events of JoltPhysicsSharp. Those events look up a C# object for each of the two bodies, in a table behind a lock, which made each tick of a heap of 300 awake boxes about 15 % slower while one pair waited for its touch. The callback for a contact that Jolt found again leaves at once while no pair waits for its touch and the contact's restitution is 0. When a pair waits, it reads the two body IDs and leaves after one set lookup, unless the pair is the one that waits. When the contact can bounce, it also reads the approach speed of each contact point for the minimum rebound, and the bodies' accumulated forces only when a point approaches faster than Jolt's 1 m/s for restitution. The C API keeps one table of contact callbacks for the whole process, so this table replaces the one of JoltPhysicsSharp: its contact events never fire, and the backend replaces the listener that JoltPhysicsSharp gives each new system before the system first steps. (ter-pnco) The impulse of a `Collision` is an estimate from the approach speed. Jolt's C API cannot read the impulse that the solver applied.

Jolt never simulates the inner body of a character. A character also keeps a gap of 0.02 m from what it touches, which is Jolt's speculative contact distance. So Jolt loses and finds the contact of a character and a box again and again while the character pushes the box. For this reason, the character's own contact list decides the end of a held contact of a character. This list holds each body within 0.1 m of the character, asleep or awake. If the list no longer has the other body, the held contact ends. A character in the rigid form has the same rule. Physics gives it the list that its walking form would have, by Jolt's query for the contacts of a character at its pose, after each update of its system. Without that, a switch to the rigid form would end each contact with a body between 0.02 m and 0.1 m away, which the walking form still held, though nothing moved. A character's contacts that Jolt holds end as those of any body do.

The gap of 0.02 m puts the contact of the inner body at the edge of Jolt's speculative contact distance, so Jolt can miss it, and Jolt never finds a contact of the inner body with a static body. So the character's own collisions start a contact too, also with a ghost. A hit is a real touch. If the character hits a body in its update, and the contact of the pair has not started, physics reports the contact. If Jolt does not hold the pair, physics holds it. The list then ends it, as above. If Jolt finds the contact later, the contact continues. The impulse of such a contact is the speed with which the character moved into the body along the contact's normal, times the reduced mass of the two. So physics reports the contact of a character that walks into a box, pushes it slowly, or lands on a box or on a floor. A body that moves into a character that stands still starts a contact only when Jolt finds it for the inner body.

### Physics systems that grow

Jolt creates each physics system with buffers of a fixed size. So a system holds a fixed number of bodies. Ghosts and the inner bodies of characters count too. Physics never refuses a body for lack of room. It adds a body or a character to a full system in three steps. First, it builds a new system with twice the limits for bodies, body pairs and contact constraints. Then it moves every body of the old system into the new one, and it frees the old system. Last, it adds the new body. A character in the walking form moves as a new native character in the new system, and one in the rigid form moves as a rigid body. A native character that physics frees takes its inner body out of its system. So physics frees the old native character before the old system. This happens in one place, so every path that adds a body gets it: a new body, an interior, a ghost, a hand-off, and a hull that the reconcile moves.

The move keeps the state of each body. A rigid body keeps its ID, its shape, motion type, layer, mass, material and user data, its pose and velocities, and whether it sleeps. A character keeps its form, pose, velocity and up direction, and a walking one finds its ground again. Each pair of bodies that touches stays in contact, so physics does not report the contact again. The new system starts with no contacts, so physics holds each pair, as it holds a contact that Jolt drops (see "Contacts"). This holds for any number of growths. Two things stay behind, because the C API of Jolt cannot save them. The first step in the new system solves its contacts without the impulses of the last step. Also, the sleep timer of each body starts again.

The bodies move in the order of their IDs, and the characters after them. So two runs that grow at the same tick stay the same, bit for bit. A growth costs one pause, in proportion to the number of bodies in the frame, each time that the frame doubles. Physics writes a warning each time. A frame that is known to be large can start larger: raise `MaxBodiesPerFrame` in the backend options.

### Output that only a bug produces

A state that only a bug can produce breaks a contract in a Debug build. A Release build skips it and records it in `World.Errors`. A contract that the physics backend breaks is a bug too. It escapes `World.Tick` on the ticking thread, with the stack of the physics thread, and the world does not record it as a failed step.

The physics backend reports each such state that it finds in the `Errors` list of the change batch or of the step output. Examples are a motion that the hull cannot take, and a change that the backend cannot apply. The World records each message in `World.Errors`. Spawn refuses every body that physics cannot create, so a change that fails in the backend comes only from a bug. The backend leaves nothing of that change behind, and it applies the rest of the batch.

For physics output, the facts below define a correct backend. Entities and frames die only in step 5 of the sync point or outside a tick. Physics hears of each death before its next step. An entity keeps its body for its whole life, because a full physics system grows. Each hull starts a step in the system of its frame's parent. So its frame can only enter a sibling or leave for the parent's parent. Physics moves a frame only through its hull: a dynamic hull by the update, a kinematic hull by the sample that it applied, and a static hull by its owner's teleport. Only a dynamic hull hands off. Physics and the check of its output derive the motion of a hull in a step from the same intents, the ones that the step applied, or else from the stored motion (`FrameTree.HullMotionInStep`). So a hull is dynamic in a step exactly when that read gives `Dynamic` for the step, and physics never hands off a hull whose motion in the step is static or kinematic. A root frame has no parent, so its owner has no hull.

So step 2 of the sync point checks each output before any state lands:

1. A body state names a live entity that has a body, and a live frame.
2. A hand-off names a live entity that has a body, and a live target frame. It leaves the frame that the entity is in at the start of the step. For the owner of a frame, that is the frame's parent in the tree. The owner of a root frame is in no frame, so it never hands off. If the entity owns a frame, that frame's hull has the motion `Dynamic` in the step (`FrameTree.HullMotionInStep` with the intents that the step applied). The target is a different frame. Each entity has at most one hand-off in a step.
3. The hand-offs of a step, applied in their order, never put a frame under itself or under a frame inside it.
4. A step record names a live frame with a parent. The owner of that frame has a body, which is the hull. So a step record never names a root frame.
5. A contact names two different live entities that have a body, and a live frame. An ended contact names two different entities. One of them can be gone or have no body, because destroying a body ends its contacts, and physics hears of the destroy before its next step.
6. A character state names a live entity whose body is a character. Each entity has at most one character state in a step.

The class `PhysicsOutputCheck` in `Tertius.Core` holds these checks. The sync point uses it. The physics tests use it too, in the class that stands in for the sync point. So the tests apply the same rules as the engine.

A broken character state is the one exception to the rule below. It changes only whether its character stands, so a Release build records one error and skips only that state. The character state of an entity whose update the sync point skips is part of that update, so it is skipped too.

If an output fails a check, a Release build skips the whole physics update of each entity that the output names, for that step. For a step record, the entity is the owner of the frame. The whole update of an entity is its body state, its hand-off, the step record of its own frame, and its contacts. The sync point records exactly one error for each output that fails. An output that fails more than one check still records one error. An output whose entities another failed output skipped already still records its own error.

The skip does not change physics. At the next reconcile, a frame hull goes back to where the tree says. A plain body that physics moved into a frame that is gone stays in that system. When physics finds that the frame is gone, it removes the body with a warning.

## Rendering

The renderer is a deferred Vulkan renderer. It gets a flat list of instances, each relative to the render origin, and it knows nothing about frames.

### The order of a rendered frame

The renderer records each rendered frame in this order. `FrameGraph.Stage` fixes the order of the passes.

1. Uploads of new meshes, textures and materials.
2. Batching on the CPU (`SceneBatcher`): interpolation by alpha, frustum culling, depth ranges, sorting and indirect commands. The impostor manager takes its impostors out of the batch and chooses the captures of the rendered frame.
3. The shadow maps of the sun.
4. Impostor captures.
5. The G-buffer pass: the far, middle and near ranges. Each range draws the impostor billboards in it after its geometry.
6. The lighting pass, into the HDR target.
7. The transparent forward pass.
8. Tone mapping: an exposure scale, then the ACES curve in Stephen Hill's fit.
9. The readback of a requested capture.

A pass is a `RenderPass` in one stage of the `FrameGraph`. Shadows and impostors use three hooks:

- A `RenderPass` in a stage.
- An `IRangeDrawer`, which draws inside each depth range of the G-buffer pass.
- An `IInstanceProcessor`, which gets the culled instances before batching.

### The render snapshot

The snapshot builder runs at the sync point, in step 11. It computes the pose of every frame relative to the camera's physics home (see "Numeric frames"). It computes in doubles, along the path through the lowest common ancestor. The home has the axes of the camera's frame, and it is near the camera. So positions near the camera stay exact far from the origin of the camera's frame. The builder then writes each instance relative to the render origin, which is the camera position at the current tick. Each instance has a current and a previous transform relative to the same origin, so that the render thread can interpolate.

The builder interpolates only between two consecutive builds of the same camera in the same frame. In any other build, the previous transform is the current one. The previous pose of a frame uses the tree of this build with the local poses of the last build. So a frame that moved between the builds carries its contents along. The builder keeps the last pose of each entity and the frame that the pose was in, in the `PoseMemory` column of the entity table. So an entity that a hand-off moved to another frame interpolates smoothly.

The snapshot also carries:

- The camera: its current and previous position and rotation, and its vertical field of view.
- The sun: its direction from the render origin toward the origin of `World.SunFrame`, and its angular radius, atan(r / d). Here r is the bounding radius of the owner of the sun's frame, and d is its distance.
- The ambient light, and one `RenderLight` for each light that an entity carries: a `PointRenderLight` or a `SpotRenderLight` (see "Shading"). A light sits at the pose of its entity composed with the offset of its `LightData`.
- `OriginInFrame` and `OriginFrameId`: the render origin in the camera's frame, and an id of that frame. The shadow texel grid uses them.
- `RootRotation` and `PreviousRootRotation`, for the star field (see "Shading").

A build of 8,192 or more slots runs on the gameplay workers when the world has more than one, in chunks of 1,024 slots. The chunks join in slot order, so the order of the instances does not depend on threads.

### Instances and draws

All instances of a rendered frame go into one instance array, shadow casters and impostor billboards included. The array goes into one storage buffer for each frame in flight. Textures live in one bindless array. The batcher sorts the opaque instances by depth range and mesh. It writes one indirect command for each run of instances with the same range and mesh. It sorts the transparent instances by range and then back to front, so a transparent command usually holds one object. Each depth range, each shadow cascade and the transparent pass issue one `vkCmdDrawIndexedIndirect` over their part of the commands. MoltenVK does not support `drawIndirectCount`, so the CPU sets the draw count.

### Depth ranges

There are three depth ranges, drawn from far to near into the same G-buffer. Each range is a slab of view depth that reaches 1% past its nominal boundaries in the table below, so neighbor slabs overlap. The batcher puts an opaque instance in each slab that its bounding sphere overlaps, from near to far. It stops at the first slab that holds the far side of the sphere. So an object that straddles a boundary draws twice, and an object that fits in one slab draws once. A transparent instance draws once, in the range that holds its center, with depth clamping on.

| Range | Nominal view depth |
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

An entity carries its light as `RenderData.Light`, a `LightData`. It is a custom C# 15 union, as `HemisphereUp` is. Its cases are `PointLightData(Offset, Color, Intensity, Range)` and `SpotLightData(Offset, Color, Intensity, Range, InnerConeAngle, OuterConeAngle)`. The empty union holds no light, so there is no `Enabled` flag: an entity whose light is the default carries none, and a component that switches a light off and on keeps the light value and writes the default over it. The light sits at the pose of the entity composed with `Offset`, and a spot light points along the local -Z axis of that pose.

The snapshot gives each light as a `RenderLight`, a second custom union. Its cases are `PointRenderLight(Position, Color, Intensity, Range)` and `SpotRenderLight(Position, Direction, Color, Intensity, Range, InnerConeAngle, OuterConeAngle)`. The two unions have their own case types and do not share them. The snapshot builder switches on the case of the `LightData` and makes the matching case of the `RenderLight`, with the position relative to the render origin and, for a spot light, the direction in the axes of the camera's frame. `SceneBatcher` switches on the case again and writes one `GpuLight`. It leaves out a light of either case whose range or intensity is zero or less, or whose sphere of reach is outside the frustum. `GpuLight` keeps its std430 layout of 64 bytes. A point light writes kind 0 and leaves its direction and cone fields zero, because the shader and the impostor lighting read them only for a spot light. A spot light writes kind 1, a unit direction (straight down when the direction is zero), the cosine of the outer angle, and the cosine of the inner angle, which is never less than the outer cosine plus 1e-4, so that the shader's smoothstep has a non-empty interval.

The sky is a procedural star field, fixed to the axes of the root frame of the camera's frame tree. The render snapshot carries the root's axes in the axes of the camera's frame, as `RenderSnapshot.RootRotation`, and the renderer interpolates them as it interpolates the camera. So the stars do not turn with a ship that the camera rides in, and a switch of the camera to another frame does not move them.

The hemisphere ambient light needs an up, and each instance takes it from the gravity field of its own frame, at its place. (User) Up points against a uniform field, inside the field's region, and away from the frame's origin in a point field. In a frame without gravity, every surface gets the mean of the sky and the ground color. A frame owner, such as a ship's hull, takes its own frame's field at the frame's origin. Only the field counts, not the frame's proper acceleration, so thrust never turns the up. (User) The ambient light of a surface therefore does not depend on the frame that the camera rides in, a planet sees the sky on every side, and an empty frame in space mixes evenly by itself.

The snapshot gives each instance its up as a `HemisphereUp`. It is a custom C# 15 union, as `GravityField` is. (User) Its cases are `UpAlong(Direction)`, a direction in the axes of the camera's frame, and `UpAwayFrom(Center)`, a point given relative to the instance, so that a planet's own center moves with it exactly. The empty union is the even mix. It takes 16 bytes, one vector and a tag, because a value holds one case. `GpuInstance` still packs it into one `Vector4` for the shaders: the direction with `w` 1, the point with `w` 2, and zero for the even mix. The G-buffer pass stores the sky's share in the alpha of the emissive target. The up is not interpolated between ticks. The impostor manager keeps each capture's up in object space in its record of the light, so an object that turns against its up is captured again.

The up costs about 5 ns per instance in the snapshot build. On one thread, `RenderSnapshotBenchmarks` builds a snapshot of 20,000 visible entities in 784 µs, against 675 µs without the up. Each GPU instance grows from 64 to 80 bytes. The renderer has no GPU timer to measure that yet (ticket `ter-yiii`).

### Shadows

Shadow casters are selected by distance from the camera, not by frame, so an object shadows objects in any frame. Every caster has the `CastsShadow` flag, and the sun has none. The renderer lets an instance cast a shadow without the `Visible` flag. But the snapshot builder emits only visible instances, so today an entity without `Visible` casts no shadow. A caster casts two kinds of shadow:

- Map shadows. Cascaded shadow maps for the sun cover receivers in the first 2 km of view depth, with four cascades and blending at the seams. They draw every caster whose bounding sphere comes within 2 km of the camera, however big it is, for example a ground plane or a planet under the camera. Map shadows have the caster's real shape.
- Far shadows. Every caster is also a sphere of its bounding radius, tested analytically in the lighting shader against the sun's disc, so the edge is soft. A sphere never shadows a point inside it, so a caster never shadows its own surface, and a planet's ground needs no such shadow: it faces away from the sun at night. A caster that the maps draw counts as a sphere only beyond the maps' reach, faded in over the last cascade's blend, so no receiver gets both shadows from one caster. A long or flat caster shadows as a rough sphere there.

A far shadow counts only while the sun lights the scene. It must also be big enough to notice. A caster beyond the maps must cover a pixel, or a fifth of the sun's angular radius, seen from the camera. A caster in the maps must hide 4% of the sun, seen from where its far receivers start. They start 2 km from the camera. So the distance is 2 km minus the distance of the caster from the camera, and at least the radius of the caster. A 50 m ship 500 m away qualifies, and a crate or a person does not. So the cost per pixel grows only with the shadows that can show. The sun is still the one light with shadows. Making every far light work the same way belongs to the point and spot light shadows epic, which is planned (ticket `ter-s9cv`, `docs/plans/local-light-shadows.md`).

The texel grid of each cascade snaps to a world-fixed anchor: the origin of the camera's frame. As a result, shadows do not shimmer as the camera moves. On MoltenVK, a steep caster clipped at the edge of the shadow map gets a flat, wrong depth plane. To avoid it, each cascade box is 16 times deeper than it is wide, with a D32 shadow map. When the device cannot filter D32, the map uses D16, which has less precision to spare. The box is then 4 times deeper than it is wide.

### Impostors

- The projected height h of an object is the diameter in pixels of the image of its bounding sphere at the center of the screen. Below 200 px, an object becomes an impostor. Above 300 px, it returns to geometry.
- An instance with the `NoImpostor`, `Celestial` or `Transparent` flag never becomes an impostor. Neither does an instance with a bounding radius of 0, or with the eye closer than 1.05 bounding radii to its center.
- The impostor is captured on demand, into a 512 px tile of the impostor atlas, which has `RendererOptions.ImpostorTiles` tiles. A first capture takes a free tile. If none is free, it takes the least recently used tile whose object is out of view or back to geometry. Without such a tile, the first capture waits. Until it has a capture, an impostor draws as geometry.
- A capture records the view direction, the sun direction, the light that reaches the object and the scale, in object space. It also records r / d, the bounding radius over the distance of the eye. The error of a capture is the largest of these changes since the capture:
  - The angle between the current and the captured view direction.
  - The change of r / d, the parallax between the front and the back of the sphere.
  - The angle between the current and the captured sun direction.
  - The relative change of the light that reaches the object. That light counts the radiance of the sun times its visibility past the far shadows. It also counts both ambient colors and the hemisphere up in object space. It also counts the irradiance of the point and spot lights, with its direction.
  - How far the scale has become non-uniform.
- If error × h is more than 2, about one pixel, the capture is stale. For the view direction, that is a change of more than 2 / h radians.
- Each rendered frame makes the 4 captures of highest priority. They come from two binary heaps, one of recaptures and one of first captures, merged by priority. A recapture of an impostor that the last rendered frame drew has the priority error × h. That impostor draws its stale capture until the recapture. Any other capture has the priority π × h, the error of the worst view. Its object draws as geometry until the capture.
- A capture uses a perspective camera at the real eye position, fitted to the bounding sphere. An orthographic capture looked wrong at the switch distance.
- A capture bakes the sun, ambient light, far shadows and point and spot lights, but not shadow-map shadows.
- A billboard writes its per-pixel depth, so impostors intersect the ground like the real object.
- The renderer keys impostor state on the entity index, the render part, the mesh and the resolved material slot (`ImpostorKey`). So a part that changes its mesh or its material gets a new key, and it never draws an old capture.

### Switches

`RendererOptions` holds the switches of the renderer.

| Option | Default | Effect |
|---|---|---|
| `Validation` | Off | The layer `VK_LAYER_KHRONOS_validation`, with synchronization checks. It costs CPU time. |
| `FramesInFlight` | 2 | How many frames the CPU records while the GPU works on earlier ones, from 1 to 4. |
| `VSync` | On | FIFO presentation. A headless renderer ignores it. |
| `Width`, `Height` | 0 | The output size in pixels. A headless renderer needs both. For a window, 0 means the framebuffer size. |
| `ShadowMapSize` | 2048 | The side of each of the four shadow cascades, in texels. The maps take size² × 16 bytes, 64 MB at the default. |
| `Impostors` | On | Draws small objects as impostors. |
| `ImpostorTiles` | 64 | The number of 512 px tiles in the impostor atlas. Each tile takes about 3.3 MB, so the default takes about 213 MB. |

A switch that makes rendering deterministic for snapshot tests is planned, not built (ticket `ter-rtxu`, `docs/plans/renderer-snapshot-tests.md`).

## Blueprints

A blueprint is a name, a list of component types with optional field values, optional body, render and frame data, and an optional parent. It has no pose. The spawn call gives the frame and the pose (`Blueprints.CreateSpawnDesc`). Frame data, a `FrameSpec`, makes the new entity the owner of a frame.

A blueprint with a parent starts from the resolved blueprint of its parent, which must be registered first. A component type that the parent also lists keeps its place in the list of the parent. Its field values merge, and the values of the child win. The other types of the child come after the types of the parent.

Reflection runs only in `Blueprints.Register`, which C# code calls. For each component type and set of field names, it compiles one factory delegate, which every registry shares. Spawning then costs one delegate call per component. No loader reads blueprint files yet. The file formats and the loader are planned (ticket `ter-jwzd`, `docs/plans/content-formats.md`).

## Host and scripted runs

`Tertius.Host` wires the modules into a program. `ScriptCommand.Run` reads the command line and starts one of three runs. The design is in `docs/plans/host-and-scripted-runs.md`.

| Run | Command line | What it does |
|---|---|---|
| Headless script | `--script <file> --headless --out <folder>` | No window and no threads of its own. The calling thread runs the steps of the script, with the ticks back to back. It makes a headless renderer only for a script with a capture step. So a script with dumps only runs on a machine with no GPU. |
| Script in the window | `--script <file> --out <folder> [--speed <factor>]` | The same script in a window. The simulation thread paces the ticks by the wall clock, times the speed factor. |
| Free play | `--scene <name> [--speed <factor>] [--record <file>]` | The scene in a window, until the window closes. With `--record`, the host writes the session to the file as a script that replays it. |

- A scene is a named C# function (`Scene`). It fills a new `World` and gives scene ids to some of its entities (`SceneInstance`). The table `DemoScenes.All` lists the scenes of the Demo. A script names its scene.
- A script is a JSON file of steps, which run in file order: `run`, `post`, `dump`, `capture` and `camera`. Only a `run` step advances time. The ticks run in lock-step with the steps, not with the wall clock.
- The closed sets of the script format are custom C# 15 unions, as `Target` is. `ScriptStep` has the cases `RunStep`, `PostStep`, `DumpStep`, `CaptureStep` and `CameraStep`. `MessageTarget` has the cases `ToEntity`, `ToTopic`, `ToFrame` and `ToEveryone`. The runner's private `ReadyStep` has one case for each step after its checks. A case is a sealed record, so `Value` does not box. The default value of a union holds no case: it is the JSON `null`. `Script.CheckSteps` refuses a step that holds no case, and a post step whose target holds no case, with its step number, before the first tick. JSON refuses a `null` target when it reads it.
- A JSON converter for each of the two unions (`ScriptStepJsonConverter` and `MessageTargetJsonConverter`, in `UnionJson.cs`) reads and writes the `do` key of a step and the `kind` key of a target, as `InputEventJsonConverter` does for an input event. Reading finds the key in a copy of the reader. Then it gives the whole object to System.Text.Json as the record of that case, with the same strict options. `ScriptJson` gives the contract of each case record the key as one more member. So reading accepts the key in any place of the object, and writing puts it first. An unknown member, a duplicate member, a missing member and a wrong value are errors, with the words of System.Text.Json. A key that is missing, given twice, not a string or unknown is an error with the words of the converter. The message for a missing or unknown key lists the kinds. A key that is given twice or is not a string gets its own short message. The path in a message about a member is relative to the record of the case, such as `$.ticks`, because System.Text.Json reads that record in a call of its own. The line and the byte position in the line are those of the whole text. The JSON of scripts, recordings and dumps is the same as before the unions, byte for byte.
- A `dump` step writes a JSON state dump: poses, frames, routed events and errors. A `capture` step renders the current state and writes a PNG image, at the size of the window of the script.
- A headless capture builds the render snapshot at that moment. It draws the snapshot on the calling thread at interpolation alpha 1, with impostors off. In the window, the simulation thread gives the path of the capture to the render thread. Then it waits until the render thread saves the image.
- A `post` step reads its payload into the fields of the payload type by their camelCase names. `InputEvent` is the exception, because it is a union: it has its own JSON form, in scripts, in recordings and in dumps. A key or a mouse button is `{"kind":"keyDown","code":87}`, and a move or a scroll is `{"kind":"mouseMove","delta":[3,-4]}`. The kinds are `keyDown`, `keyUp`, `mouseDown`, `mouseUp`, `mouseMove` and `scroll`. Reading refuses a kind that is missing or unknown, a member that its kind needs and lacks, and a member that its kind does not have (`delta` on a key, `code` on a move). The one exception is that member with its zero value (`"delta":[0, 0]` on a key, `"code":0` on a move), which reading ignores: the recorder wrote it into every input post before `InputEvent` was a union, so recordings from then still play. A delta reads and writes in the form of every other vector of a script or a dump, because the converter of `InputEvent` and the vector converters of the host call the same methods (`JsonVectors`).
- During a script, live input reaches no entity, so a script runs the same with or without a person at the keyboard.
- In free play, the main thread decides where each input event goes (`InputRouter`). Esc closes the window, F1 switches the debug fly camera on and off, and V makes the next view of the scene the camera. The game sees none of these keys. The window host queues every other event. The simulation thread posts the queued events to the `input` topic before its next tick. So each event joins a known tick, and a recording replays it exactly.
- The command returns an exit code (`ScriptCommand`). 0 is success, 1 a wrong command line, 2 a file problem, 3 a script problem and 4 a bug. 5 means that the renderer or the window cannot start.

## Rules for references

- A `ref` into a table never lives past one `Handle` call.
- Tables grow only outside the parallel part of a tick, so a `ref` taken during a tick is always valid for that tick.
- A component that needs another entity later stores its `Entity` handle and checks it on use.
- Mod support will need a registry that drops every reference to mod types before unloading. That work is deferred.

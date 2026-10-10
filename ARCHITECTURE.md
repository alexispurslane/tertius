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
2. Copy body poses and velocities from the physics output into the back transform table. Apply the hand-offs that physics reported.
3. Make sure that no entity with a body wrote its own transform. This is a debug assertion.
4. Swap the physics intent buffers, so that physics gets this tick's forces in the next step.
5. Free dead entities. Increment the generation of each slot and return the slot to the free list. Tell physics to destroy their bodies.
6. Make new entities live. If the tables are full, grow them. Tell physics to create the new bodies.
7. Advance the frame tree: Kepler frames from time, physics frames from the physics output.
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

`PoseTree<TPose>` is packed. Its arrays have no gaps. Each parent comes before its children, so one linear pass computes all poses. A reparent can break that order. The tree then marks itself dirty and restores the order before the next pass.

The scene graph inside a frame uses the same `PoseTree`, with the float `Pose`.

### Motion drivers

- Kepler: the pose is a function of time, computed in doubles from the orbital elements and a spin rate. The frame is in free fall, so its proper acceleration is zero.
- Physics: the frame's hull is a dynamic body in the parent frame's physics system. Its pose and velocity come from that body after each step. The proper acceleration is the velocity change over the step minus the gravity that the parent applied.
- Static: the pose does not change.

### Gravity

Each frame has one gravity field: `None`, `Uniform` (artificial gravity, with an optional region box) or `Point` (a planet). An object feels only the field of the frame that it is in. This is the one-dominant-body rule.

The effective gravity on a body inside a frame is the frame's own field minus the frame's proper acceleration, rotated into frame axes. As a result, loose objects inside an accelerating ship slide toward the stern.

### Bubbles and nesting

A frame's bounds are an ellipsoid or a sphere. No other shape is allowed. The bubble decides ownership: which frame an object belongs to. The physical shape of a frame in its parent frame is its real hull. This lets a ship land on a planet with its real hull.

A frame that fits inside a sibling frame enters it, like any other object. A frame fits inside another frame in one case: its largest bubble radius is less than half of the smallest radius of the other bubble. A fighter that flies into a carrier's bubble becomes a child of the carrier. Two frames that do not fit inside each other stay siblings. Their real hulls collide in the parent frame. If their bubbles overlap, objects in the overlap stay in the frame that they were in, and each frame's hull is ghosted into the other.

Docking two frames of similar size merges them explicitly into one frame, and undocking splits them again.

### Hand-off

An object belongs to the deepest frame whose bubble it is in. These rules control its moves between frames:

1. The first contact of its bounding sphere with a child's bubble moves it into the child frame. The hull is inside the bubble. So the object is in the child frame before any part of it can reach the child's real hull.
2. It leaves a frame only after its bounding sphere is fully outside the bubble, plus a margin. The margin is the larger of 2% of the bubble radius and the object's radius. This gap stops the object from moving back and forth every tick.
3. If it touches two sibling bubbles at once, it enters the one that it penetrates more deeply.
4. Physics checks after each step, on the physics thread.
5. The pose converts in double precision. Velocity converts as `v' = R⁻¹(v − v_frame − ω × r)` on entry, and the reverse on exit. No Coriolis force acts, but the conversion keeps the ω × r term. An object thrown out of a spinning station then leaves with the correct velocity.
6. The entity handle does not change. The body moves from one physics system to the other. The entity moves to the new frame's topic and gets a `FrameChanged` event.

### Ghosts

While an object's bounding sphere crosses a bubble boundary, the other physics system holds a ghost. A ghost is a kinematic body that copies the real body's pose every step. Bodies in the other system collide with the ghost, but the ghost does not push back on the real body. After the object is fully on one side, physics removes the ghost.

## Physics

Tertius uses Jolt in double-precision mode. Each frame that contains bodies has its own `PhysicsSystem`. A frame with no awake bodies is not stepped.

Object layers:

| Layer | Collides with |
|---|---|
| `Static` | `Moving`, `FrameHull` |
| `Moving` | `Static`, `Moving`, `Ghost` |
| `FrameHull` | `Static`, `FrameHull` |
| `Ghost` | `Moving` |

A frame entity has two bodies. In the parent system, its hull is a body on the `FrameHull` layer. In its own system, the same hull shape is static geometry on the `Static` layer. This includes the exterior surfaces, so that a person on a spacewalk can stand on the hull. Objects inside do not push the hull body.

Point gravity is applied as a force on each awake body before each step. The Jolt system gravity is zero.

The character controller is a capsule. It stands on whatever is below it and inherits that surface's velocity. It uses the frame's effective gravity. Near zero effective gravity, it switches to zero-g movement.

## Rendering

The renderer is a deferred Vulkan renderer. It gets a flat list of instances, each relative to the render origin, and it knows nothing about frames.

### The render snapshot

The snapshot builder runs at the sync point. It computes every frame's pose relative to the camera's frame in doubles, along the path through the common ancestor. It then writes each instance relative to the render origin, which is the camera position at the current tick. Each instance has a current and a previous transform relative to the same origin, so that the render thread can interpolate.

### Instances and draws

All instances go into one large instance buffer. Textures live in one bindless array. Draws use multi-draw indirect, with one indirect command per mesh. MoltenVK does not support `drawIndirectCount`, so the CPU sets the draw count.

### Depth ranges

There are three depth ranges, drawn from far to near into the same G-buffer:

| Range | Distance |
|---|---|
| Far | 2,000 km to infinity (infinite reversed-Z) |
| Middle | 2 km to 2,000 km |
| Near | 5 cm to 2 km |

The hardware depth buffer is cleared between ranges. The G-buffer stores linear view distance in an R32F target, so one lighting pass serves all three ranges.

### Shading

The lighting model is energy-normalized Blinn-Phong, not PBR:

- The specular term is multiplied by (n + 8) / 8π.
- Schlick Fresnel.
- Normal maps.
- A gloss map that sets the exponent.
- Hemisphere ambient light.

Lighting is in linear color space, into an HDR target, with tone mapping at the end. The G-buffer holds albedo, an octahedral-encoded normal, specular intensity and gloss, emissive, and linear view distance. Transparent objects use a forward pass per range after the lighting pass.

Dynamic point and spot lights are shaded in the deferred lighting pass. The sun is a directional light.

### Shadows

Shadow casters are selected by distance from the camera, not by frame. Cascaded shadow maps for the sun cover the first 2 km. Planet shadows and eclipses use a sphere test in the shader.

### Impostors

- Below a projected height of 200 px, an object becomes an impostor. Above 300 px, it returns to geometry.
- The impostor is captured on demand, into a 512 px texture.
- If the view direction changes by more than 2 / h radians, the renderer captures it again. Here h is the projected height in pixels. A change in sun direction uses the same rule.
- Captures go through a priority queue ordered by error, with a budget of 4 captures per frame.

## Blueprints

A blueprint is a name, a list of component types with optional field values, and optional transform, body and render data. Reflection runs only at blueprint load time. For each component type, the loader compiles one factory delegate. Spawning then costs one delegate call per component.

## Rules for references

- A `ref` into a table never lives past one `Handle` call.
- Tables grow only at the sync point, so a `ref` taken during a tick is always valid for that tick.
- A component that needs another entity later stores its `Entity` handle and checks it on use.
- Mod support will need a registry that drops every reference to mod types before unloading. That work is deferred.

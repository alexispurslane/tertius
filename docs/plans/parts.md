# Parts

Ticket: `ter-ftk4`. It serves the particle epic `ter-4zt6` and the demo's plumes `ter-6u2m`, which waits for both.

## Goal

An attached entity can push the body of its chain's root. So the shuttle's main engine, each of its jets and the person's back jet can each be a separate entity, attached to the hull or to the person, with one particle emitter each, and each one pushes the body that carries it. Such an attached entity is a part.

## The model

The user proposed this on 2026-10-21. It seems odd to the user that several jets and an engine are part of one shuttle entity. An attachment is like the frame tree: an attached thing moves with its parent. The difference is that an attachment has no physics system and no coordinate system of its own, and that effects can bubble up from a part to its parent, not only down from the parent to the part.

The orchestrator refined the model in the chat, and the user did not object:

1. A part is an attached entity. A part writes only its own row, as every entity does. Its component adds a force, a torque or an impulse to its own intent.
2. At the sync point, the engine carries the additive intents of each part up its attachment chain to the body of the chain's root.
3. A part's force acts at the part's place. So it gives the root's body a force and a torque about the body's center of mass.
4. Only the additive intents flow up: the force, the torque, the impulse and the angular impulse. They compose by a sum, with nothing to decide. The exclusive intents stay with the root alone, because two writers of one would need a decider. These are the change of motion, an end pose, a full sample, a steer target, a velocity, a teleport, a character's walk and jump, and a change of collision groups.
5. A part's push is physics, not a message. The root's components do not interpret it, as they do not interpret a contact.
6. The sum runs in a fixed order, so it repeats bit for bit, also when the entities run on several workers.
7. Every reader of a body's force sees the sum, for example the numeric update's estimate of the thrust of a body.
8. Commands from the root's components reach its parts by message, one tick later than today. The demo's turning jets become pairs of forces, so its attitude autopilot gives way to an allocation of jet throttles. That is demo work, not this ticket.

The rest of this doc designs that model and nothing else. It needs no exception: every kind of root and every additive intent takes one path, and each difference in the effect comes from a rule that physics already has for the root's kind of body.

## Terms

| Term | Meaning |
|---|---|
| Part | An attached entity (`SpawnDesc.AttachTo`) that writes an additive intent. Every attached entity can be a part. A part has no body, and it cannot be a frame, as every attached entity today. |
| Chain root | The one entity of an attachment chain that is not attached (ARCHITECTURE.md, "Creation and destruction"). A part's chain root never changes while the part lives, because an entity is attached only at its spawn and dies with its parent. |
| Root body | The body of a part's chain root: a plain body, a hull or a character |
| Additive intent | A field of `PhysicsIntent` that adds up during a tick: `Force`, `Torque`, `Impulse` and `AngularImpulse` (`src/Tertius.Core/Seams/PhysicsSeam.cs`, lines 449 to 452) |
| Exclusive intent | Every other request of `PhysicsIntent`, whose last write in a tick wins: `Velocity`, `AngularVelocity`, `TeleportPose`, `CharacterVelocity`, `JumpSpeed`, the collision groups and mask, `Motion` and `Move` |
| Push | The four additive intents of one entity in one tick, as one value |
| Place in the body | The position of a part relative to the origin of its root body, in the root body's own axes, composed from the offsets of the chain |
| Center of mass | The center of mass of the root body's shape, in the body's own axes, as Jolt computes it for the shape |
| Lever | The place in the body minus the center of mass, turned into the axes of the frame of the intents. It is the arm of the part's force about the center of mass. |
| Front frame | The frame in the front transform row of an entity during a tick. Every intent of the tick names it (`PhysicsIntent.Frame`). |
| Carrying the pushes | The engine's step that moves the push of every part into the intent of its chain root, at step 5 of the sync point (see "Where the push flows up") |
| Tick k, step k | Step k is the physics step that runs beside tick k. The intents that tick k writes act in step k + 1. |

## Cause, storage and effect

Who causes each change is apart from where its result is stored:

| Change | Who causes it | Where it is stored | When |
|---|---|---|---|
| A part asks for a push | The part's own component, by `BodyHandle.AddForce`, `AddTorque`, `AddImpulse` or `AddAngularImpulse` | The part's back intent, in the axes of the part's front frame | Tick k, on the part's worker |
| The push becomes an input of the next step | The swap of the intent buffers | The part's front intent | Step 4 of the sync point of tick k |
| The push joins the root body's push | The engine, in one choke-point method (`Attachments.CarryPushes`) | The chain root's front intent. The part's front intent keeps zeros. | Step 5 of the sync point of tick k, after the destroy batch is known |
| The root's sum moves with the root | The conversions of an intent into its entity's new frame | The chain root's front intent | Steps 5 and 7b of the same sync point |
| The body moves | Physics | Jolt's body, then the root's transform row, or its frame tree row for a hull | Step k + 1, then step 2 of the sync point of tick k + 1 |

```mermaid
flowchart TD
    A["Tick k: the part's component calls AddForce"] --> B["The part's back intent"]
    B -->|"step 4: swap of the intent buffers"| C["The part's front intent"]
    C -->|"step 5: the destroy batch grows, then CarryPushes"| D["The chain root's front intent: its own push plus the part's"]
    D -->|"step 5 and 7b: conversions into the root's new frame"| E["The root's front intent, in the root's frame"]
    E -->|"step 6c reads the thrust"| F["The numeric update"]
    E -->|"step k + 1"| G["Physics applies the root's intent to its body"]
    G -->|"step 2 of the next sync point"| H["The root's row, or its frame's row, and the part's row at step 7"]
```

A component changes only its own entity. A part changes only its own intent. The engine then carries the push, as physics carries a contact from one body to another. No component of the root runs on it, and no message carries it.

## Where the push flows up

The engine carries the pushes at step 5 of the sync point: after the swap of step 4 and after the destroy batch has grown, before any entity of the batch moves to another frame or dies. Each fact below fixes one bound of that place.

- The swap of step 4. The flow writes the intents of the step that applies them, the front intents after the swap. The swap also hands on the list of the parts that pushed in the tick, as it hands on the list of the owners that wrote a change of motion (see "The state that the change adds"). Before the swap, the written intents hold the same values, but the destroy batch is not known yet.
- The destroy batch. A part that dies at this sync point is not live in step k + 1, so its push must not act, as the last intent of any entity that dies at the sync point does not act (see "Deaths and changes of frame in the same tick"). The batch is known only after it grows in step 5, to the attached children of each dying entity and to the members of each dying frame that has no surviving ancestor. So the flow comes after the growth.
- The changes of frame. A hand-off at step 2 can move the root to another frame, and step 5 can rehome it out of a frame that dies. Both leave the root's row in its new frame and the intents of the tick in the front frame. Before the rehomes of step 5 and before the conversions of steps 5 and 7b, the intents of the part and of its root name the same frame, the front frame of the chain, and the front rows describe the chain in that frame. So the flow adds vectors in one frame and needs no relation between frames. The sum then converts once, as part of the root's intent, by the rule that converts every force today.
- The numeric update of step 6c. It reads the force and the impulse of each body's front intent for its thrust clock (`World.ThrustAcceleration`, see "Readers and writers"). So the flow comes before it, and the update counts the parts' pushes.
- The step in flight. Physics step k ended before the sync point went past its step 1, which waits for it. The flow changes only the intents of step k + 1, which no step reads until the next tick starts that step.
- The rule that stored state describes the last finished step. The flow stores no state that a tick reads. It writes an input of the next step, as the swap does. It reads only the front rows, which describe the end of step k - 1, the state that the part's component read when it wrote its push.

Without a physics backend, the swap clears every additive intent (`PhysicsIntent.KeepOnlyMotion`), so the World skips the flow, as it skips `ApplyCollisionIntents`.

So step 5 becomes:

1. Apply the subscribe and unsubscribe requests of the chunks, as today.
2. Mark the destroy batch and grow it, as today.
3. Carry the pushes (`Attachments.CarryPushes`), with the marks of the batch.
4. Rehome the survivors, convert the intents that name a dying frame, and free the dead, as today.

Today `World.DestroyRequestedEntities` (`src/Tertius.Simulation/World.Sync.cs`, line 570) marks the batch and calls `DestroyDying`, which grows the batch and then rehomes, converts and frees. The build splits `DestroyDying` after the growth, so that the World calls the three pieces in that order, also in a sync point without destroys.

## How a part's push converts into the root's push

The part writes its push in the axes of its front frame, as every entity writes every direction. The user's rule: every write stays relative to `self.Frame`, and every direction stays in its axes. A part is always in the frame of its chain root, so its front frame is the root's front frame, and both intents name it. So the direction of each vector needs no conversion. Only the lever is new.

The force of a part acts at the part's place. A rigid body takes a force at a place as the same force at its center of mass plus the torque of the force's arm about the center of mass. So for each part, in doubles:

```
placeInBody = Compose(offset_1, Compose(offset_2, ... offset_n)).Position
lever       = q.Rotate(placeInBody - centerOfMass)
force       += F
torque      += T + Cross(lever, F)
impulse     += J
angular     += L + Cross(lever, J)
```

where

- `offset_1` to `offset_n` = the offsets of the chain from the root down to the part (`Self.AttachOffset` of each link), each widened to a double pose by `Poses.Safe(offset).ToDPose()`, as `Attachments.Derive` widens them (`src/Tertius.Simulation/Internal/Attachments/Attachments.cs`, line 128 on). These are the offsets at the sync point, with the writes of the tick: the part's place in the body during the step that applies the push, where step 7 puts the part's row.
- `placeInBody` = the place in the body, in meters, in the root body's own axes, relative to the body's origin.
- `centerOfMass` = the center of mass of the root body's shape, in meters, in the body's own axes. Jolt keeps it as a float vector (`Shape::GetCenterOfMass`), so its widening to doubles is exact. The entity table keeps it from the spawn (see "The state that the change adds").
- `q` = the rotation of the root's front transform row, `entities.FrontRow(root).Pose.Rotation`: the root body's axes in the axes of the front frame. For a hull it is the rotation of the frame in its semantic parent, which is the owner's row. A numeric frame never turns, so its axes are the semantic frame's.
- `lever` = the lever, in meters, in the axes of the front frame.
- `F`, `T`, `J`, `L` = the part's force (N), torque (N·m), impulse (N·s) and angular impulse (N·m·s) from its front intent, widened from floats.
- `force`, `torque`, `impulse`, `angular` = the root's push in doubles: its own four fields, widened, plus the contribution of each of its parts.

The sum for one root starts from the root's own fields and adds its parts' contributions in the order of the list of parts that pushed (see "The order of the sum"). It narrows to floats once, into the root's four fields. A sum of two floats in doubles rounds only when their sizes differ by a factor of more than about 2^28. So two jets whose forces are exact negatives cancel to exactly zero, and the root's own force survives them exactly, as long as no sum on the way rounds. Each narrowing per part would round the root's force at every part.

Why `q` comes from the front row, and not from the root's pose where step k + 1 starts. The part's component wrote `F` in the front frame's axes from the pose that it read, the front row. A jet writes its thrust along its own nozzle, `self.PoseIn(self.Frame).Rotation.Rotate(-Double3.UnitZ) * thrust`, as `MainEngine` does today (`src/Tertius.Demo/MainEngine.cs`, line 51). Its lever then turns by the same rotation as its force. So a jet's push equals the push that the root's own component would write today for the same jet, a force and the torque `Cross(q.Rotate(placeInBody - centerOfMass), F)`, both from the front snapshot, and a pair of opposite jets gives exactly the couple of its jets in the body's axes, turned by one rotation. The root's pose at the start of the step would turn the lever but not the force, by the turn of one step: 2.5 mrad at the demo's fastest turn of 0.15 rad/s. It also needs the relation of the front frame to the root's frame after a hand-off, which this place in the sync point avoids. Every force of every entity today lags by the same step, because its writer reads the front snapshot.

## The flow per kind of root

The flow takes one path for every root: it adds the part's contribution to the root's front intent. What physics does with the sum is the rule that it has today for the root's own push of the same kind of body. The table says, for each kind of root, what a part's push does, and why.

| Kind of chain root | What a part's push does | Why |
|---|---|---|
| Dynamic plain body | The force acts at the center of mass, with the torque of its lever. The impulse and the angular impulse act at the start of the step. The two-turn update of a turning frame turns the force and the torque as it turns the root's own (`JoltPhysicsBackend.ApplyTurningUpdate`). | Physics applies all four fields of a dynamic body's intent (`src/Tertius.Physics/JoltPhysicsBackend.Step.cs`, lines 265 to 283). |
| Dynamic hull | The same. The hull's frame follows the hull, and physics measures the frame's proper acceleration from Jolt's change of velocity, so the frame's contents feel the parts' thrust. | A hull is a dynamic body in its parent's system. |
| Kinematic body, hull or plain | Nothing moves. The sum stays in the intent, and physics ignores it. | Physics applies forces and impulses only to a dynamic body. Step 4 keeps the forces of a hull that turns kinematic for the same reason (`World.ApplyMotionChanges`). A change of motion in the same tick can make the root dynamic in the step, so the write cannot refuse the push. |
| Static body, hull or plain | Nothing moves, as for a kinematic root | The same rule |
| Character, walking or rigid | The force and the impulse change the character's velocity, as its own do. The torque and the angular impulse do nothing. | Physics adds `(Impulse + Force · dt) / mass` to a walking character and reads no torque (`src/Tertius.Physics/JoltPhysicsBackend.Step.cs`, line 386). The rigid form takes the intent as a dynamic body whose rotation is locked (`ApplyRigidCharacterIntent`, line 342), so a torque turns nothing. A character's rotation follows its up. |
| Entity without a body, the owner of a frame without a hull included | The part cannot push. Its write throws an `InvalidOperationException`. | The push has no body to act on. `AddForce` on the root itself throws for the same reason (`World.WritableIntent`, `src/Tertius.Simulation/World.Tick.cs`, line 330). The chain root never changes, so the write can know it. |
| Owner of a root frame | It has no parts. | A root frame's owner is in no frame, so it cannot take an attached child (`Attachments.CheckParent`). |

## The flow per kind of additive intent

| Additive intent | What the root gets | The moment at which it acts |
|---|---|---|
| Force `F` | `F`, and the torque `Cross(lever, F)` | Over the whole step, held in the frame's axes, as every force |
| Torque `T` | `T` | Over the whole step |
| Impulse `J` | `J`, and the angular impulse `Cross(lever, J)` | At the start of the step |
| Angular impulse `L` | `L` | At the start of the step |

A torque and an angular impulse are free vectors, so the part's place does not change them. A force and an impulse act at the part's place, so they get the term of their lever. A part that wants a force at another place is a part at that place.

## The order of the sum

The flow runs on the sync thread. It visits the parts that pushed in the order of the list that the swap handed on: the chunks in chunk order, and in each chunk the parts in the order of their first push in the tick, which is the order in which the chunk ran them. That order is the same in every run, whatever worker ran each chunk, because a chunk runs on one worker and its list belongs to the chunk. For each root, the sum starts from the root's own fields, which the root's components added in the order of their run, and it adds the contributions of its parts in that order. The flow does nothing with the order of the roots, because each root's sum is apart from every other.

The flow has no piece on the workers. The build measures its cost (see "Costs and measurements"). A piece on the workers would come with a threshold, as the four pieces of the sync point have.

## Deaths and changes of frame in the same tick

| Case | What happens | Why |
|---|---|---|
| The part dies at this sync point: it destroys itself, or it gets a `Destroy` message that it acts on | The flow skips it. Its last push does not act. | An entity that dies at the sync point is not live in the next step, and its last intent does not act, as the intent of a body that dies goes with the body. |
| A parent between the part and its root dies | The part dies with its parent, so the flow skips it. | The destroy batch grows to the attached children of each dying entity before the flow. |
| The root dies | The flow skips the root and all its parts. Nothing acts. | Every part of the chain dies with the root, and the root's intent goes with its body. |
| The root and the part die because their frame dies with no surviving ancestor | The same as a dying root | The batch grows to the members of the frame before the flow. |
| The root's frame dies and the root moves to an ancestor | The flow adds the push in the front frame. Then step 5 converts the root's intent, with the sum, into the ancestor, before the frame dies. | The flow comes before the rehomes. The sum converts as the root's own force does. |
| Physics handed the root into another frame in step k | The flow adds the push in the front frame. Step 7b converts the root's intent, with the sum, into the root's new frame. Step 7 moves the part's row into that frame. | The intents of the tick name the front frame. The flow comes before the conversion of 7b. |
| The numeric update moves the root to another numeric frame | Nothing changes for the push. | A numeric frame does not change the semantic frame that the intents name, and it does not turn. |
| The part wrote a new `AttachOffset` in the same tick | The lever takes the new offset. | The new offset is where the part is in the step that applies the push. Step 7 derives the part's row from it too. |
| The root wrote a change of motion in the same tick | The sum stays. Physics applies it if the root is dynamic in step k + 1. | Physics follows the motion of the step, and the forces of the root's own intent follow the same rule. |
| The part or its root spawned in this tick | No push. | A spawn becomes live at step 6 and runs from the next tick, so it wrote nothing. |

## Exclusive intents stay with the root

A part takes only the additive intents. Every exclusive request on a part throws, as it throws today, because the part has no body: `SetVelocity`, `SetAngularVelocity`, `Teleport`, `MoveCharacter`, `Jump`, `SetCollision`, `SetMotion` and `SteerTo`. `Self.MoveKinematic` and `Self.MoveOnRails` refuse an attached entity by the first rule of the guard of every motion intent (`World.MotionIntentProblem`). Nothing changes in these guards. Two writers of one exclusive intent would need a decider, so there is one writer: the chain root.

## The public API

A part pushes with the same members as a body: `self.Body.AddForce`, `AddTorque`, `AddImpulse` and `AddAngularImpulse`. Today they are:

```csharp
    /// <summary>Adds a force for the next step, in newtons.</summary>
    public void AddForce(Vector3 force) => Intent.Force += force;

    /// <summary>Adds a torque for the next step, in newton-meters.</summary>
    public void AddTorque(Vector3 torque) => Intent.Torque += torque;

    /// <summary>Adds a linear impulse, in newton-seconds.</summary>
    public void AddImpulse(Vector3 impulse) => Intent.Impulse += impulse;

    /// <summary>Adds an angular impulse.</summary>
    public void AddAngularImpulse(Vector3 impulse) => Intent.AngularImpulse += impulse;
...
    private ref PhysicsIntent Intent => ref world.WritableIntent(entity);
```

(`src/Tertius.Simulation/Entities/Self.cs`, lines 230 to 240 and 351)

where

- `Intent` = the running entity's back intent, for writing. `World.WritableIntent` throws an `InvalidOperationException` for an entity without a body.
- `force`, `torque`, `impulse` = a vector in the axes of `self.Frame`.

After the change, the four additive members go through a new `World.WritablePush(entity)` instead of `WritableIntent`. It gives the back intent of an entity with a body, as today. For an attached entity whose chain root has a body, it gives the part's own back intent, and its first call in the tick lists the part among the parts that pushed. For every other entity it throws an `InvalidOperationException` that says that the entity has no body and is not part of a chain whose root has one. The other members of `BodyHandle` keep `WritableIntent`, so they throw for a part. The documentation of the four members says that a part's push acts on its chain root's body at the part's place.

So a component that pushes runs on a hull or on a part with the same code: `MainEngine`, `TranslationThrusters` and the test helper `Thruster` need no change to push from a part. `AddForce` keeps one meaning, add a force to my own intent, on every entity that can push.

The reads do not change. `self.Body.Exists` stays false for a part, because the part has no body. `self.Body.NextStepMotion` stays null for a part. `self.Body.Pending` gives the entity's own writes in the tick: a part sees its own push, and a root sees only its own writes, never its parts' pushes, which join at the sync point.

The engine needs no member for a force at a place. A part is that member: a force at a place is a part at that place. A body's own components push at its center of mass, as today.

## The state that the change adds

| State | Owner | Writers | Readers |
|---|---|---|---|
| `Record.BodyCenterOfMass`, a `Vector3` in the body's own axes, in meters, beside `BodyBoundingRadius` | `EntityTable`, in the cold record of each slot | `EntityTable.Activate` at the spawn, with the value that the World asks of `IPhysicsBackend.ShapeCenterOfMass(BodyDesc.Shape)`. Zero without a body or without a backend. | `Attachments.CarryPushes`, through `EntityTable.BodyCenterOfMass(entity)` |
| `ChunkBook.Pushers`, a list per chunk of the parts that pushed in the tick, in the order of their first push | `EntityTable` | `EntityTable.WritablePush` at a part's first push of the tick. `SwapIntents` hands it on and clears it. | `SwapIntents` |
| `frontPushers`, the list of the parts whose pushes are in the front intents, in chunk order | `EntityTable` | `SwapIntents`, which fills it from the chunk lists, after it clears the old one | `Attachments.CarryPushes`, through `EntityTable.FrontPushers` |
| `Link.Root`, the chain root of each attached entity | `Attachments` | `Attachments.Attach`. It never changes, because an entity is attached only at its spawn and dies with its parent. | `World.WritablePush`, for the guard, and `Attachments.CarryPushes` |
| The sum of each root in doubles, a scratch list and a map from root to its index | `Attachments` | `Attachments.CarryPushes`, which empties both before it returns | `Attachments.CarryPushes` |
| `IPhysicsBackend.ShapeCenterOfMass(ShapeDesc)` | The physics seam. `JoltPhysicsBackend` reads it from the native shape that it builds and keeps for the description, as `ShapeProblem` does, and keeps the answer. It is thread-safe, so a spawn during a tick can ask. `FaultInjectingBackend` forwards it. | None: it is a pure answer | `World` at the spawn |

The pure part of the flow is one value type, `Push` (internal, in `src/Tertius.Simulation/Internal/Attachments/Push.cs`): the four vectors in doubles, with the methods that belong to it. `Push.Of(in PhysicsIntent intent)` widens an intent's four fields. `Push.Lever(DQuaternion rotation, Double3 placeInBody, Double3 centerOfMass)` is static, because it takes no push. `push.At(Double3 lever)` gives the push of the root about its center of mass. `sum.Plus(in Push other)` adds. `push.WriteInto(ref PhysicsIntent intent)` narrows once. Unit tests pin each one without a World. `Attachments.CarryPushes` is the one choke-point method: the World calls it once per sync point, it calls the pure methods, and it acts on the intents through the entity table directly. There is no plan object.

The flow adds no closed set. The four additive intents are four fields that all sum, and the flow does not branch on the kind of root, so the change needs no `[Union]` and no kind enum.

### Invariants

`EntityTable.AssertInvariants` gains:

- Each live entity in a chunk's `Pushers` is listed there once, has no body, and has a written intent (`SlotFlags.IntentRegistered`).
- Each live entity in `frontPushers` is listed once, has no body, and is in `IntentEntities`.
- A record without a body has a zero `BodyCenterOfMass`. A record with a body has a finite one.

`Attachments.AssertInvariants` gains:

- The `Root` of every link is the chain root that a walk up the parents finds, and the root is live.
- Each live entity in the table's `frontPushers` and in each chunk's `Pushers` is attached, and its chain root has a body.
- The scratch of `CarryPushes` is empty.

`World.AssertInvariants`, at the end of a sync point, gains:

- Every live attached entity's front intent holds a zero force, torque, impulse and angular impulse, and no exclusive request: no flag and no motion intent.
- The front row of every live attached entity names the frame of its chain root's front row. Step 7 keeps this, and the flow relies on it.
- With a backend, every body's `BodyCenterOfMass` equals `ShapeCenterOfMass` of its shape.

### Contracts

The operations that change this state get preconditions and postconditions that compile out of Release builds:

- `EntityTable.WritablePush(Entity part)`. Requires: gameplay runs, and the part has no body. Ensures: the part is in its chunk's `Pushers` once, and its intent is registered and names its front frame.
- `EntityTable.AddPush(Entity root, in Push sum)`, which writes a root's sum into its front intent. Requires: gameplay does not run, the root is live and has a body, every vector of the sum is finite, and the root's front intent names the root's front frame or is empty. Ensures: the root is in `IntentEntities` once, and its front intent names its front frame. A root that wrote nothing in the tick gets its intent listed and named here, as `EntityTable.SetFirstMove` lists a spawn's first motion intent.
- `EntityTable.ClearPush(Entity part)`. Ensures: the part's front intent holds a zero push.
- `Attachments.CarryPushes`. Requires: gameplay does not run, and the scratch is empty. For each part that it visits, it requires that the part's front intent names the frame of the root's front row. Ensures: every live part in `frontPushers` holds a zero push, and the scratch is empty.

The guard of the public write is an always-on guard: `World.WritablePush` throws an `InvalidOperationException` for an entity that has no body and is not part of a chain whose root has one, in every build.

A part whose front intent names another frame than its root's front row comes only from a bug. So a Debug build fails the contract, and a Release build skips that part and records the bug in `World.Errors`, as the user decided for the physics output that names a dead frame.

## Readers and writers

Every reader and every writer of a body's force, torque, impulse and angular impulse in the code today, and what each one does after the change:

| Reader or writer | File and line | Today | After the change |
|---|---|---|---|
| `BodyHandle.AddForce`, `AddTorque`, `AddImpulse`, `AddAngularImpulse` (writers) | `src/Tertius.Simulation/Entities/Self.cs`, lines 231 to 240 | Add to the running entity's back intent. Throw for an entity without a body. | The same for a body. A part writes its own back intent, and its first push lists it. |
| `Attachments.CarryPushes` (new writer) | New | None | Adds each part's push to its root's front intent, and zeroes the part's |
| `EntityTable.SwapIntents` (writer) | `src/Tertius.Simulation/Internal/Entities/EntityTable.cs`, line 1042 | Exchanges the buffers. Without a backend, clears the additive fields through `KeepOnlyMotion`. | The same, and it hands on the list of the parts that pushed |
| `PhysicsIntent.KeepOnlyMotion` (writer) | `src/Tertius.Core/Seams/PhysicsSeam.cs`, line 519 | Clears the four fields | The same. A part's push is cleared before it can flow, so the World skips the flow. |
| `PhysicsIntent.IsEmpty` (reader) | `src/Tertius.Core/Seams/PhysicsSeam.cs`, line 539 | Reads the four fields, for the swap without a backend | The same |
| `World.ConvertIntent` (writer and reader), at steps 5 and 7b | `src/Tertius.Simulation/World.Sync.cs`, lines 720 to 723 | Turns the four fields into the entity's new frame by the relation where the step starts | The same. It converts the root's sum. The part's intent holds zeros. |
| `World.ThrustAcceleration`, the thrust clock of the numeric update, at step 6c | `src/Tertius.Simulation/World.NumericFrames.cs`, lines 113 to 117 | `(|Force| + |Impulse| / dt) / mass` from the body's front intent | The same read, and it reads the sum, because the flow runs at step 5. A pair of opposite jets gives no thrust, as a torque gives none today. |
| `JoltPhysicsBackend.ApplyBodyIntent` | `src/Tertius.Physics/JoltPhysicsBackend.Step.cs`, lines 265 to 283 | Adds the impulses and records their change of velocity, then adds the force and the torque of a dynamic body | The same, with the sum |
| `JoltPhysicsBackend.ApplyCharacterIntent` | `src/Tertius.Physics/JoltPhysicsBackend.Step.cs`, line 386 | Adds `(Impulse + Force · dt) / mass` to a walking character's pending change of velocity | The same, with the sum. The parts' torques go unread, as the character's own. |
| `JoltPhysicsBackend.ApplyRigidCharacterIntent` | `src/Tertius.Physics/JoltPhysicsBackend.Step.cs`, line 342 | Passes the intent of the rigid form to `ApplyBodyIntent`. The locked rotation takes no torque. | The same, with the sum |
| `JoltPhysicsBackend.ApplyTurningUpdate` | `src/Tertius.Physics/JoltPhysicsBackend.Step.cs`, lines 707 to 719 and 750 to 762 | Reads Jolt's accumulated force and torque, which hold the intent's, and adds their turn in a turning frame | The same. Jolt's accumulated values hold the sum. |
| `FrameWorld.ApproachAcceleration` and `ForceAcceleration` | `src/Tertius.Physics/Internal/FrameWorld.cs`, lines 857 to 879 | Reads Jolt's accumulated force of a dynamic body for the rebound threshold | The same. It holds the sum. |
| The proper acceleration of a dynamic hull | `JoltPhysicsBackend` at the end of the step | Measured from Jolt's whole change of velocity | The same. It holds the parts' thrust. |
| `BodyHandle.Pending` (reader, gameplay) | `src/Tertius.Simulation/Entities/Self.cs`, line 228 | The running entity's writes so far in the tick | The same. A root never sees its parts' pushes, which do not exist until step 5. |
| `FaultInjectingBackend` (reader, tests) | `tests/Tertius.Testing/FaultInjectingBackend.cs`, lines 131 and 158 | Records the intents of each step | Records the sum in the root's intent and zeros in the part's |

No other code reads or writes these fields. The demo's writers are the components that call the four members above.

## Left out

- A force at a place for a body's own components. A part is that member.
- A part's push on another chain than its own. A part pushes only its chain root.
- The parts' pushes in the body facts of the snapshot or in a state dump. No reader needs them.
- A piece of the flow on the workers. It waits for a measurement that shows the need.
- A torque on a character. A character's rotation follows its up, so physics reads no torque of it, as today.

## Tests

The tests use the real World and the real Jolt backend. A test that compares a part's push with a root's own push runs both worlds from the same scene, and the root's own component computes the same expression in doubles and narrows once. "Bit for bit" means the poses and velocities of every row are equal as doubles.

Physics, in `tests/Tertius.Physics.Tests/PartPushTests.cs`:

- `APartOnTheLineThroughTheCenterOfMassPushesItsRootAsTheRootsOwnForceDoesBitForBit`
- `APartOffTheCenterOfMassPushesItsRootAsTheRootsOwnForceAndTheTorqueOfTheLeverDoBitForBit`
- `TheLeverOfAPartIsMeasuredFromTheCenterOfMassOfAHullShapeAndNotFromTheBodysOrigin`
- `TwoOppositeJetsAtOppositePlacesTurnTheirRootAndLeaveItsCenterOfMassAtRest`
- `APartsPushAddsToTheForceThatTheRootWritesItself`
- `ATorqueOfAPartTurnsItsRootAsTheRootsOwnTorqueDoes`
- `AnImpulseOfAPartGivesItsRootTheAngularImpulseOfItsLever`
- `AGrandchildPushesTheChainRootAtTheGrandchildsPlace`
- `APartPushesFromTheOffsetThatItWroteInTheSameTick`
- `APartsPushActsInTheStepAfterTheTickThatWritesIt`
- `ThePushOfAPartOnAKinematicRootMovesNothing`
- `ThePushOfAPartOnAStaticRootMovesNothing`
- `ThePushOfAPartOnACharacterChangesItsVelocityAsTheCharactersOwnForceDoes`
- `APartWhoseRootHasNoBodyCannotPush`
- `APartCannotSetAVelocityTeleportOrWriteAMotionIntent`
- `APartThatDestroysItselfGivesNoPushInTheNextStep`
- `APartWhoseParentDiesGivesNoPushInTheNextStep`
- `APartsPushInTheTickOfItsRootsHandOffConvertsWithTheRootsOwnForce`, in `Scenes.ApproachToBubble`
- `APartsPushInTheTickThatItsRootsFrameDiesConvertsIntoTheSurvivingAncestor`
- `TheThrustClockCountsThePushOfAPartAsTheRootsOwnForce`
- `TheProperAccelerationOfAHullThatItsPartsPushIsTheirThrustOverItsMass`
- `APartsPushInASpinningStationTakesTheTurnOfTheStepAsTheRootsOwnForceDoes`
- `ThePushesOfManyPartsSumToTheSameIntentsOnOneThreadAndOnTheWorkers`
- `TheComponentsOfTheRootDoNotSeeThePushesOfItsParts`
- `CarryingThePushesAllocatesNothingAfterWarmUp`

The backend, in `tests/Tertius.Physics.Tests/ShapeCenterOfMassTests.cs`:

- `TheCenterOfMassOfABoxASphereAndACapsuleIsTheirOrigin`
- `TheCenterOfMassOfAShapeIsWhereJoltPutsTheCenterOfMassOfABodyAtRestAtTheOrigin`, for a convex hull and a compound
- `TheShuttlesHullHasItsCenterOfMassWithinAMillimeterOfTheLoftsIntegral`

The pure value, in `tests/Tertius.Simulation.Tests/PushTests.cs`, with no World:

- `AForceAtTheCenterOfMassGivesNoTorque`
- `TheLeverIsThePlaceInTheBodyLessTheCenterOfMassTurnedByTheRootsRotation`
- `AnImpulseAtALeverGivesItsCrossProductAsAngularImpulse`
- `ATorqueAndAnAngularImpulseIgnoreTheLever`
- `TwoOppositeForcesAtOppositePlacesSumToExactlyZeroForce`
- `TheRootsOwnForceSurvivesAPairOfOppositeJetsExactly`
- `TheSumNarrowsToFloatsOnceAndNotOncePerPart`

The entity table, in `tests/Tertius.Simulation.Tests/EntityTablePushTests.cs`:

- `TheFirstPushOfAPartInATickListsItOnce`
- `TheSwapHandsThePartsThatPushedOnWithTheIntents`
- `ARootThatWroteNothingGetsAnIntentThatNamesItsFrontFrame`
- `TheCenterOfMassOfABodyIsKeptFromItsSpawn`

Tests that lock in the decisions:

- `TheLeverTurnsWithTheRotationThatTheWriterRead`: a root that spins fast, with one jet that pushes along its own axis. The root's torque equals the jet's couple in the body's axes turned by the front rotation, and not by the rotation where the step starts.
- `APartsPushNamesTheFrontFrameOfItsChainAtTheFlow`: the invariant that the flow relies on, after a hand-off and after a rehome in the same tick.
- `ThePushOfADyingPartIsSkippedBecauseTheDestroyBatchIsKnownFirst`: the place of the flow after the growth of the batch.

The hand mutations of each new test name the line that the test protects, as every brief asks.

## The changes to ARCHITECTURE.md

The build makes these changes before its merge:

1. "Creation and destruction", the paragraph on `SpawnDesc.AttachTo`: an attached entity can push the body of its chain's root with the additive intents, as a part. Its push acts at its place. It still has no body and cannot be a frame. Its chain root never changes.
2. "Component rules": a part changes only its own intent. The engine carries its push to the root's body at the sync point, as physics carries a contact, and the root's components do not see it.
3. "Ownership of transforms": the additive intents of a part flow to its root's body, and the exclusive intents stay with the root. A part's push needs a root with a body.
4. "Threads and the tick", step 4: the swap hands on the list of the parts that pushed, as it hands on the list of the owners that wrote a change of motion.
5. "Threads and the tick", step 5: the engine carries the pushes after the destroy batch grows and before its rehomes, with the reasons of "Where the push flows up".
6. "Entities": the cold record of a body keeps its center of mass from the spawn, beside its bounding radius.
7. "Physics": `IPhysicsBackend.ShapeCenterOfMass` and where Jolt's value comes from.
8. "Numeric frames", the row "Who gets one": the force and impulse intents of a body include the pushes of its parts.
9. "Turning frames": a force and a torque of the intent include the pushes of the parts, which the update turns as the root's own.
10. The invariant lists of the entity table, the attachments and the World, as "Invariants" above gives them.

## Costs and measurements

Costs:

- Commands from a root's components reach its parts by message, one tick later than today. A hull's autopilot sends each jet its throttle in tick k, the jet pushes in tick k + 1, and physics applies the push in step k + 2. The attitude autopilot reads the hull's state two steps late today, and three steps late after the change, against a `RateTime` of 20 steps.
- The flow visits only the parts that pushed. For each one it reads one link, composes the offsets of its chain, turns one vector and takes two cross products, and it looks up its root's sum.
- Each slot's cold record grows by 12 bytes, the center of mass.
- A main engine that a part pushes gives the hull a torque when its line of thrust misses the center of mass that Jolt computes. Today `MainEngine` pushes at the center of mass, whatever the nozzle's place.

The build must measure:

- The lap of the flow in `SyncProfile`, with 10,000 parts that push in every tick, on 1 worker and on all workers, and with no part. With no part, the lap reads nothing measurable.
- The allocations of a sync point with parts that push in every tick, after warm-up: none.
- In the demo, after its own ticket moves the jets onto parts: the miss of the docking and the settle time of the attitude loop with the extra tick, beside today's.

## The demo's use

The demo work is in `ter-6u2m`, not in this ticket. Each actuator becomes a part with one emitter:

- The main engine: one part at `Shuttle.NozzlePose`, on the line of thrust through the center of mass, with `MainEngine` on it. The hull's flight program sends it the `Throttle` of each tick as a message.
- The side jets: one part for each of the six directions of `TranslationThrusters`, on a line through the center of mass, or as pairs placed on both sides of it so that their torques cancel. The flight program sends the `Translation`, and the jets split it.
- The turning jets: pairs of opposite jets at opposite places, so that each pair gives a couple about one of the hull's axes. `AttitudeAutopilot` keeps its law, and it ends in an allocation of the throttles of the pairs, which gives the torque that it asks for within each jet's thrust. Each jet gets its throttle by message. The two jets of a pair compute their forces as exact negatives, for example from the same axis of the hull, so that the pair gives the hull no net force at all. Forces that only nearly cancel leave a force of the order of 1e-7 of the thrust.
- The person's back jet: one part on the person, which `Passenger.Steer` and `PersonControl` send the float force as a message. A character reads no torque, so the jet's place off the person's center of mass turns nothing. The jet can turn its own `AttachOffset` toward the force, so that its plume points against the push.

## Open forks

### Fork 1: the center of mass in the body facts

The engine now keeps each body's center of mass, which Jolt computes. The demo keeps its own value from the integral of the loft (`Shuttle.CenterOfMass`, `src/Tertius.Demo/DemoScenes.Shuttle.cs`, line 183), which a test holds within 1 mm of Jolt's. The demo's jet allocation needs the levers of the jets about the center of mass, and the main engine's part must sit on the line through it.

- Side A: `BodyFacts` gains `CenterOfMass`, read through `Snapshot.TryGetBody`. The demo places its parts and allocates its throttles from the engine's one value, and it drops its own. The value then lives in one place, as the G·M of a planet does, so a jet pair's couple and the main engine's line never miss Jolt's center of mass. It costs one more public fact with its documentation and test, and a change of the demo's constructors that take the center of mass today.
- Side B: the engine keeps the value internal, and the demo keeps its own. The public API gets no new fact. It costs two values that can differ by up to the 1 mm that the test allows. At the main engine's full thrust of 588,600 N, a line of thrust 1 mm off the center of mass gives a torque of up to 589 N·m, which the attitude autopilot must hold against.

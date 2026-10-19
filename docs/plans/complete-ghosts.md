# Complete ghosts between semantic frames

Epic: `ter-imi5`. It closes `ter-xxmj`, the first half of `ter-3mwi` and `ter-7ul9`. The user decided its three items on 2026-10-17. This doc is the design and the plan in one: it says what to build, and "Order of the work" says in which order. The ticket, the code and the rules did not settle four choices, and the user settled them on 2026-10-18. "Decided forks" keeps both designs of each choice and the user's answer, and "Decisions" gives the reasons. The rest of the doc describes the chosen end state.

## Goal

A ghost between semantic frames stands in for its real body as fully as Jolt allows. (User) Three gaps go:

1. A static body that is not a hull gets a ghost in each child frame whose bubble it reaches, as a hull already does. So the bodies of the child meet it, and a query that starts inside a bubble meets the static bodies of the parent there.
2. A contact of two ghosts, one of them a sensor, is a contact of their entities. So a sensor and a body that touch only through two ghosts hear their touch.
3. A sensor detects the same things of a frame owner in every frame: the hull in the frame's parent, the hull's ghost in other frames, and the interior in the frame itself. A change of the hull's motion ends each pair that the new motion cannot make, and starts each pair that it can make.

## Why now

The boarding of the Blue Danube demo is blocked. A shuttle parks on a static slab of the station's frame. The slab has no ghost in the shuttle's bubble. A person who walks into the bubble moves into the shuttle's frame, and falls through the slab. The test `HandOffTests.CharacterWalkingOutOfAFrameIsRecreatedInTheParent` works around the same hole: it gives the building's frame a second floor "so the walker has ground".

## Terms

| Term | Meaning |
|---|---|
| System | The Jolt physics system of one frame. Jolt makes a contact only between two bodies of one system. |
| Home | The frame whose system holds a body. |
| Ghost | A Jolt body in the system of another frame than its real body's home, with the real body's shape, and its pose and velocity there. Its record is `GhostBody` in `BodyRecord.Ghosts`. A record has at most one ghost in each system. |
| Child ghost | The ghost of a body in a child frame whose bubble the body reaches. The child frame is a semantic child of the semantic frame of the body's system (`UpdateGhosts`, the loop over `ghostBubbles`). |
| Stick-out ghost | The ghost of a movable body that sticks out of its own bubble: in the bubble's tree parent, and in each sibling bubble that it reaches. |
| Anchor ghost | The ghost of a hull in a home that `FrameTree.AnchorGhosts` names. |
| Mirror pair | The contact of two bodies of two systems that each system holds between one real body and the other body's ghost (`JoltPhysicsBackend.Contacts.cs`). When both bodies are dynamic, both ghosts are kinematic, so each real body meets the other as an immovable body. |
| Frame owner | The entity that owns a frame. Its bodies are the hull, in the frame's tree parent, the interior, a body in the frame's own system with the hull's shape or `BodyDesc.InteriorShape`, and the hull's ghosts. A frame owner is never a sensor (`BodyDesc.IsSensor`). |
| Plain body | The body of an entity that owns no frame. |
| Role | One of the four engine bits of an object layer: `Static`, `Moving`, `FrameHull` and `Ghost` (`BodyLayer`, `Layers`). A hull's mask names only the `Static` and `FrameHull` roles, so a hull meets static bodies and hulls, and nothing else. |
| Jolt motion | The motion type that Jolt holds for a body. |
| Contact motion | The motion that decides what a body meets. For a real body it is its Jolt motion, and a character's inner body counts as a character. For a ghost it is the motion of its real body. |
| Jolt's rule | Jolt makes a contact of two bodies whose layers meet only when one of them is dynamic, or one is kinematic and the other is a sensor. A character counts as dynamic. The engine never sets `CollideKinematicVsNonDynamic`. `MotionsMeet` (`JoltPhysicsBackend.Contacts.cs`) mirrors it. |
| Content of a frame | The frame's bubble, grown until it holds the bounding sphere of every member of the frame's systems. The query runner uses the same notion for the part of a query that a child frame gets ([Queries and sensors](queries-and-sensors.md)). The excess of the content is how far it reaches beyond the bubble, in meters. It is zero while every member is inside. |
| Pair test | The one predicate that says whether Jolt and the contact filter can make a contact between two bodies of one system (see "The pair test and the filter"). |
| Started pair, held pair, ledger | As in `JoltPhysicsBackend.Contacts.cs`: a started pair is a body pair that holds a contact as started in one system. A held pair is a started pair that Jolt dropped. The ledger, `contactCounts`, counts the started pairs of each entity pair over all systems. |

## A ghost from scratch

Start from Jolt. Jolt makes contacts only inside one system, so two bodies in two systems touch only through a ghost. A ghost moves with its real body's velocity relative to its system. So a ghost that moves is kinematic in Jolt, and by Jolt's rule it meets only dynamic bodies and sensors.

A ghost that stands in for its body as fully as Jolt allows then follows six rules:

1. Placement. A body has a ghost in every system whose bodies can touch it. Take a static or kinematic body X and a dynamic body Y in two systems. Y's ghost in X's system is kinematic, so it meets X only when X is a sensor. So their contact can happen only in Y's system, through X's ghost. Every body, whatever its motion, needs a ghost where a dynamic body can touch it.
2. Reach. A body can touch the bodies of a frame once its bounding sphere reaches the frame's content. The bubble is not enough. A body of the frame stays in the frame until its center is its radius plus its exit margin outside the bubble (`FrameBounds.ExitMargin`). So it can stick out by about twice its radius plus the margin. There is one exception. A member that sticks out has a stick-out ghost in the frame's tree parent, which is kinematic and has the `Ghost` role. That ghost meets a dynamic body, a character or a sensor of the parent, but no other static or kinematic body, and no hull. So a static body, a kinematic body or a hull needs a ghost where it reaches the content, and a dynamic body or a character needs one only where it reaches the bubble. A ghost of a dynamic body or a character in the content would make a mirror pair of two kinematic ghosts with the member, in which each real body meets the other as an immovable body (case 1d).
3. Meeting. A ghost meets what its real body meets in the ghost's system: its layer is its body's, and Jolt's rule applies to the contact motions. There is one exception. A ghost never meets the hull of a frame that holds its real body. Otherwise a body pushes the hull of its own frame. Only a stick-out ghost can be in the system of such a hull.
4. Motion. A ghost has the Jolt motion that its real body has relative to the ghost's system. A ghost of a static body is static while it holds still there, and kinematic while it moves. A ghost of a movable body is kinematic.
5. Reports. Every contact through a ghost is a contact of the two entities, also a contact of two ghosts. The ledger counts it once, however many systems hold it.
6. Change. When a real body changes its motion, every pair of its bodies follows the new motion at once: each pair that the new motion cannot make ends, and each pair that it can make starts.

How the plan compares:

| Rule | Today | This plan | Left after this plan |
|---|---|---|---|
| 1. Placement | Movable bodies have child ghosts and stick-out ghosts. Hulls have child ghosts and anchor ghosts. A static plain body has only anchor ghosts. An interior has none. | A static plain body has child ghosts too. | Stick-out ghosts of static bodies, child ghosts of interiors in the frames inside the owner's frame, and the carry of contacts between an interior and its ghosts. A follow-up ticket builds them, and the orchestrator files it at the merge (fork F3). |
| 2. Reach | The body's bounding sphere touches the bubble. | The sphere of a static body, a kinematic body or a hull reaches the content (fork F2). A dynamic plain body or a character keeps the bubble. | Two dynamic bodies or characters that stick out of two sibling frames, and touch where neither one's sphere reaches the other's bubble, pass through each other, as today. Their stick-out ghosts in the parent are both kinematic, and a ghost of each in the other's frame would make a mirror pair. |
| 3. Meeting | Every ghost but an anchor ghost has the `Ghost` role, so it never meets a hull. The contact filter covers only ghosts of static bodies, and it reads the other body's Jolt motion. | A child ghost of a static body or a hull meets hulls (fork F1). The filter compares the contact motions of every pair with a ghost. | None. |
| 4. Motion | Only the ghost of a mesh is static while it holds still. | Every ghost of a static body is. | None. |
| 5. Reports | A pair of two ghosts is dropped. | It is reported. | None. |
| 6. Change | Only the filter's table changes. Jolt keeps the pairs that it holds, and never offers again a pair that the filter dropped. | Ghosts are made again, the started pairs of the real bodies are judged again, and the interior's motion follows the hull's (fork F4). | None. |

Jolt sets these limits, and they stay:

- A ghost of a dynamic body is kinematic, so it never meets a static body. The static body's child ghost carries that contact instead (rule 1).
- A static sensor never detects a static body, and a kinematic sensor never detects a static body.
- The ghost of a dynamic sensor detects the ghost of a static body only while that ghost moves in Jolt. While it holds still, it is static in Jolt, and Jolt makes no contact between a kinematic body and a static one. This needs two ghosts in one system, a sensor's stick-out ghost and a static body's child ghost, which is rare.

## Decisions

The user made the decisions marked (User).

| Question | Decision | Why |
|---|---|---|
| What a static plain body gets | A child ghost in each child bubble that it reaches, as a hull has. (User) | The ticket. Without it, the bodies of a child pass through the static bodies of the parent (`ter-xxmj`). |
| A pair of two ghosts | A contact of their entities. (User) The ghost clause of `FrameWorld.IsReported` goes. | Jolt makes a contact of two ghosts only when one of them is a sensor, because no ghost is dynamic. So the clause dropped only sensor pairs, which no real-ghost pair reports when the bodies touch only outside both bubbles. |
| Sensors and frame owners | One answer for each motion of the sensor and of the hull, in every frame. (User) The row of fork F4 gives the answer. | The ticket (`ter-7ul9`). |
| A pair after a change of motion | It ends when the new motion cannot make it. (User) A pair that the new motion can make starts if its bodies overlap. | One rule both ways. Jolt offers a pair again only to a new body, or to a body that it starts to simulate. |
| The motion of a ghost of a static body | Static while it holds still in its system, kinematic while it moves, as the ghost of a mesh is today. This rule replaces the mesh rule, because a real body with a mesh is always static. | Jolt never simulates a static ghost, so the bodies on it sleep, and a child system with no other active body does not step. With 100 boxes asleep on the ghost of a station's floor, a tick took 37 µs with a static ghost and 116 µs with a kinematic one (`MeshGhostBenchmarks`). A static ghost also meets exactly what its static body meets by Jolt's rule. |
| How a ghost follows a change of its body's motion | Physics removes the ghost and makes a new one at once, at the old one's pose and velocity, with the new motion's layer, Jolt motion and contact motion. | Jolt offers every pair of a new body to the filter. Removing the old ghost loosens its contacts, and the carry keeps each one that the new motion allows (`ResolveLooseContacts`). No new path. The new ghost lags the hull by one step, as every ghost lags an intent that the step applies. The ghost pass at the end of the step puts it where the hull is. |
| How a real body follows a change of its motion | It keeps its body. Physics ends each started pair of the body that the pair test refuses under the new motion. | A hull keeps its body id and its system through a change (User, [Rails components](rails-components.md)). Jolt drops a pair that it can no longer make when it simulates one of the two bodies. Only a pair of two bodies that Jolt does not simulate, such as a parked hull and a static sensor, outlives the change. |
| The contact filter | For every pair with a ghost, Jolt's rule on the contact motions of both bodies. | Today the filter reads the Jolt motion of the other body, which is wrong when the other body is a ghost too: the ghost of a dynamic sensor is kinematic in Jolt. Item 2 lets such pairs through. |
| The pair test of the ledger | One predicate replaces `PrimariesTouch` and `GhostTouches` (see "The pair test and the filter"). | The carry and the new check after a change of motion must ask exactly what Jolt and the filter make. |
| A system without child bubbles | Its static members cost nothing in the ghost pass, as today. | Most systems hold no child frame. |
| Overlap with `ter-xdt8` | `ter-xdt8` builds motion intents for every entity in another worktree. Both tickets touch `FollowHullMotions` and `ApplyHullMotion` in `JoltPhysicsBackend.Bodies.cs`. The second ticket to merge resolves the conflicts. | This ticket stays out of rails motion. |
| Do child ghosts meet hulls (fork F1) | Design B. A child ghost of a static body has the body's own layer, and a child ghost of a hull has the interior's layer. Stick-out ghosts keep the `Ghost` role. `GhostBody.Role` replaces `GhostBody.Anchor`. (User, 2026-10-18) | It is the ticket's model, a ghost that stands in for its body as fully as Jolt allows. The `Ghost` role is the engine's own limit, not Jolt's. A dynamic hull of a frame inside a ship then meets the ghosts of the parent's static bodies and of other hulls, as the real bodies meet each other, and `SetCollision` gets right the layer of a static anchor ghost, which it leaves stale today. |
| How far a ghost reaches into a child frame (fork F2) | Design B, with one exception. A static body, a kinematic body or a hull has a child ghost when its bounding sphere reaches the child's content, the bubble grown by its excess. A dynamic plain body or a character keeps today's reach, the bubble. The sibling test of a stick-out ghost grows the same way, for a kinematic body or a hull. (User, 2026-10-18, the exception after the first build) | With the bubble alone, item 1 fixes the slab under the shuttle but not the rock at the bubble's edge or the kinematic pusher that `ter-xxmj` names. The exception: the stick-out ghost of a member that sticks out meets a dynamic body or a character, so such a body needs no ghost of its own there. With one, the member and the body make a mirror pair of two kinematic ghosts, in which each real body meets the other as an immovable body: a ball of 1 kg that hits a crate of 10 kg at 5 m/s stops, and the crate is shoved into its bubble at about the ball's full speed (case 1d). The first build gave the growth to every body, and 10 existing tests (11 cases) of contacts at a bubble's edge failed. In the scripts the growth adds a ghost only in station (see "Cost"). |
| Which static bodies get ghosts now (fork F3) | Design A. Only static plain bodies get child ghosts. Interior ghosts, stick-out ghosts of static bodies and the carry of contacts between an interior and its ghosts go to a follow-up ticket, which the orchestrator files at the merge. (User, 2026-10-18) | The smallest change that unblocks the demo. The interior's ghosts belong with the interior's carry, the second half of `ter-3mwi`, which this ticket does not build. Without that carry, a crate that moves from a room into the ship hears CollisionEnded and Collision again from the walls. |
| What a sensor detects of a frame owner (fork F4) | Design A. A body of the hull's motion, in every frame. The interior is kinematic and at rest while the hull moves, and static while the hull is static. (User, 2026-10-18) | It keeps every detection that works today, adds the interior cells that `ter-7ul9` names, and matches the contract of `BodyDesc.IsSensor`, that a sensor detects dynamic and kinematic bodies. The hull's motion stays the one store that decides (`FrameTree.HullMotion`). |

## The flow per kind of body

### Where each kind has ghosts

| Kind | Child ghosts | Stick-out ghosts | Anchor ghosts | A child ghost or a sibling's stick-out ghost needs |
|---|---|---|---|---|
| Static plain body | Yes (new) | No, until the follow-up ticket of fork F3 | No | The content |
| Kinematic plain body | Yes | Yes | No | The content |
| Dynamic plain body | Yes | Yes | No | The bubble |
| Character, in either form | Yes | Yes | No | The bubble |
| Sensor | As a body of its motion | As a body of its motion | No | As a body of its motion |
| Static hull | Yes | No | Yes, when the numeric update names a home | The content |
| Kinematic or dynamic hull | Yes | Yes | Yes, when the numeric update names a home | The content |
| Interior | No, until the follow-up ticket of fork F3 | No | No | None |

A body has a child ghost in a child frame when its bounding sphere reaches what the last column names. For the content, the signed distance of its center from the bubble is at most its reach plus the content's excess. For the bubble, it is at most its reach. The reach is the body's radius, and for a hull the larger of its radius and its own bubble's largest radius, as today. The test of a stick-out ghost for a sibling bubble grows the same way by the sibling's excess (fork F2). The exception of rule 2 decides the last column: a stick-out ghost meets a dynamic body or a character, so such a body needs no ghost from the content (case 1d). `BodyRecord.GhostsReachContent` says which bodies reach the content, and `NumericBody.GhostsReachContent` mirrors it in the numeric update.

### The layer and the motion of each ghost

| Ghost | Role | Jolt motion | Contact motion |
|---|---|---|---|
| Child ghost of a static plain body | `Static`, with the body's groups and mask: the body's own layer | Static while it holds still in its system, kinematic while it moves | Static |
| Child ghost of a hull | `Static`, with the owner's groups and mask: the interior's layer | As the ghost of a static body for a static hull, or else kinematic | The hull's motion |
| Child ghost of a movable plain body | `Ghost` | Kinematic | The body's motion |
| Stick-out ghost | `Ghost` | Kinematic | The body's motion |
| Anchor ghost | `FrameHull` | As the ghost of a static body for a static hull, or else kinematic | The hull's motion |

The `Static` role is safe in a child bubble. The child's system holds the child's interior and the hulls of the child's own children, and none of those frames holds the real body. A stick-out ghost keeps the `Ghost` role, because its system holds the hull of the bubble that the body sticks out of. A child ghost of a hull takes the interior's layer, not the hull's: in another frame, the ghost is the owner's surface that the bodies of that frame meet, which the interior is in the owner's own frame. The `FrameHull` layer meets no moving body.

### The pair test and the filter

Two bodies of one system can make a contact when all three tests pass:

1. Their layers meet (`Layers.Meet`).
2. Jolt's rule passes on their Jolt motions.
3. When one of them is a ghost, Jolt's rule passes on their contact motions.

Jolt runs tests 1 and 2. The contact filter in `FrameWorld.OnContactAdded` runs test 3, and so takes the place of today's `MeetsAsItsRealBody`. It reads the contact motion of a ghost from a table of the system (see "New and changed values"), and that of a real body from Jolt. The filter acts only on pairs that Jolt adds. That is why a change of motion needs its own check (see "Case 3b: a hull changes its motion").

The pair test runs all three on the physics thread, between updates, with the Jolt motions read from the bodies. `CarryContact` and the check after a change of motion use it. Today `PrimariesTouch` and `GhostTouches` mirror the same tests from the records, and they give the same answers for every pair that exists today.

`IsReported` keeps only its first part: both bodies stand for an entity.

### Case 1: a person boards a parked shuttle

The station's frame P holds the static slab S and, at first, the person. The shuttle's frame C is a child of P, at rest in it. Its bubble holds part of S.

| Moment | Bodies in P's system | Bodies in C's system | Contacts that Jolt makes | Ledger |
|---|---|---|---|---|
| The ghost pass of every step | S, the shuttle's hull, the person | C's interior, S's child ghost S′, static, with S's layer | None in C until a body moves there | No change |
| The person walks on S in P | The person on S | S′ | Person and S in P | Collision once, at the first touch |
| The person straddles the bubble | The person, S | The person's child ghost, kinematic, S′, the interior | Person and S in P. The person's ghost meets neither S′ nor the interior, because Jolt makes no contact between a kinematic and a static body. | The count stays 1. |
| The hand-off moves the person into C | S, the person's stick-out ghost | The person, S′ | Person and S′ in C. The stick-out ghost meets no static body. | The hand-off loosens the pair in P. `CarryContact` finds `GhostIn(S, C)`, and the pair test passes: the character's movement meets S′'s layer, and a character meets a static body. C takes the pair over as a held pair, so the count stays 1. No CollisionEnded. |

The person's movement in C collides with S′ as with any static body (`NoteCharacterContacts`). S′ holds still while C does not move in P, so Jolt never simulates it. The bodies that rest on it sleep, and C's system does not step while nothing in it moves.

### Case 1b: a crate at the edge of a bubble, and a kinematic pusher

A crate X in C sticks out of C's bubble by more than its radius. A static rock R of P lies just outside the bubble, and R's sphere does not touch the bubble. C's content reaches R, so R has a child ghost R′ in C, and X stops at R′ in C. X's stick-out ghost in P is kinematic and meets no static body.

A kinematic body K of P pushes a crate into C. Once the crate is in C, K touches the crate's stick-out ghost, and Jolt makes no contact between two kinematic bodies. C's content holds the crate, so K reaches it and has a child ghost in C, which pushes the crate.

### Case 1c: a query that starts inside a bubble

The query runner asks C's system for the part of the line within C's content, and P's system for the part beyond the bubble ([Queries and sensors](queries-and-sensors.md)). S′ is in C's system, and the backend's casts hit a ghost as its entity (`QueryBodyFilter`, `EntityOf`). The runner keeps the nearest hit of each entity. So a ray from inside the bubble hits S at the same distance as from P. The runner needs no change, and the known limit in [Queries and sensors](queries-and-sensors.md) goes.

### Case 1d: a dynamic body hits a crate that sticks out

A crate X of 10 kg in C sticks 1 m out of C's bubble, so its bounding sphere grows C's content 1.7 m beyond the bubble. A ball D of 1 kg in P flies at X at 5 m/s. D's sphere stops 1 m short of the bubble, within the content.

| | With the exception: D reaches only the bubble | Without it: D reaches the content |
|---|---|---|
| Bodies in P's system | D, X's stick-out ghost X′, kinematic | D, X′ |
| Bodies in C's system | X | X, D's child ghost D′, kinematic |
| Contacts that Jolt makes | D and X′ in P | D and X′ in P, and X and D′ in C: a mirror pair |
| What happens | D meets X′ as an immovable body and stops. X does not move. | D stops at X′. D′ keeps D's velocity of the step and meets X as an immovable body, so X is shoved into C at about 5 m/s. |

The left column keeps the known limit of a ghost of a dynamic body: the other system meets it as an immovable body. The right column adds a second contact with the same limit, in which neither real body feels the other's mass. The growth also makes such ghosts and drops them again at every bounce of a body at the edge of a content, which allocates (`ContactAllocationTests`). A kinematic body in place of D does not meet X′, since Jolt makes no contact of two kinematic bodies, so it needs its ghost in C (case 1b). A hull in place of D does not meet X′, whose `Ghost` role the hull's mask does not name, so it needs its ghost in C too, with the interior's layer.

### Case 2: a sensor and a body that touch only through two ghosts

Two sibling frames B1 and B2 lie in P. A kinematic sensor Σ sticks out of B1, and a dynamic body Y sticks out of B2. They overlap only in P, outside both bubbles. Σ's sphere does not reach B2's content, and Y's sphere does not reach B1's bubble, which is as far as a dynamic body reaches (rule 2). So P's system holds Σ's stick-out ghost Σ′ and Y's stick-out ghost Y′, and no system holds a real body of one and a ghost of the other.

| Test | Result |
|---|---|
| Layers | Both have the `Ghost` role with the default groups, which meet. |
| Jolt's rule on Jolt motions | Σ′ and Y′ are kinematic, and Σ′ is a sensor, so Jolt makes the contact. |
| The filter on contact motions | A kinematic sensor and a dynamic body meet. |
| `IsReported` | Both stand for an entity. |
| Ledger | 0 to 1: Collision. When they part, 1 to 0: CollisionEnded. |

When the two bodies also touch through a real body and a ghost in B1 or B2, the ledger counts each system once, and the entities hear one Collision. When a numeric move or a ghost pass replaces Σ′ or Y′, the carry finds both ghosts in P's system and keeps the contact there (see `CarryContact` below).

### Case 3: a sensor and a frame owner

A frame owner shows one body in each frame: the hull in the frame's tree parent, the interior in the frame itself, and the hull's ghost in other frames. The sensor detects the owner as a body of the hull's motion, in every frame (fork F4):

| Sensor | Static hull | Kinematic hull | Dynamic hull |
|---|---|---|---|
| Static sensor | No | Yes | Yes |
| Kinematic sensor | No | Yes | Yes |
| Dynamic sensor | Yes | Yes | Yes |

This is Jolt's rule with the owner as a body of the hull's motion. It matches the contract of `BodyDesc.IsSensor`: a sensor detects dynamic and kinematic bodies and characters. Each place gets the table this way:

| Place of the sensor | The owner's body there | How the table holds |
|---|---|---|
| The frame's tree parent | The hull, `FrameHull` layer, the hull's motion | Jolt, for a static sensor. A movable sensor never meets the hull's layer, but the hull lies inside the frame's bubble, so a movable sensor moves into the frame before it can reach the hull, and meets the interior there. |
| The frame itself | The interior | The interior is kinematic and at rest while the hull moves, and static while the hull is static. Jolt then gives the table. |
| Any other frame: a sibling of the frame, a frame that the hull sticks out into, or an anchor home | The hull's ghost | The filter, from the ghost's contact motion, which is the hull's. |

Today a static or kinematic sensor never detects the interior, because the interior is always static (`ter-7ul9`). A kinematic interior at rest falls asleep as any body does. A static sensor that appears beside a sleeping interior then waits for the interior to wake, as it waits for any sleeping body today.

### Case 3b: a hull changes its motion

`FollowHullMotions` runs at the start of the step, and gives the hull its new motion with `ApplyHullMotion`. Then, for the owner:

1. The interior takes its new Jolt motion: kinematic and at rest while the hull moves, static while it is static (fork F4).
2. The hull and the interior keep their bodies. Physics ends each started pair of each of them that the pair test refuses now. Each such pair counts down in the ledger, and an entity pair that reaches 0 ends at the end of the step.
3. Each ghost of the hull is removed, which loosens its contacts, and a new one takes its place at once. At the end of the step, the carry keeps each loose contact that the pair test allows with the new ghost, and ends the rest.
4. Jolt offers the new pairs. A ghost is a new body, so Jolt adds each of its pairs, and the filter judges them with the new contact motion. A hull or an interior that starts to move is active, so Jolt finds its pairs and adds them.

| Change | Static sensor beside the hull in the parent | Static sensor in the frame, on the interior | Static sensor in a sibling, on the ghost |
|---|---|---|---|
| Kinematic to static | Today the pair stays held, because neither body is ever awake. Now it ends at step 2. | Today there is no pair. Now it ends at step 2. | Today Jolt keeps the pair, and the filter never sees it again. Now it ends at step 3. |
| Static to kinematic, while they overlap | Jolt adds the pair, as today. | Jolt adds the pair once the interior is kinematic. | Today the filter dropped the pair while the hull was static, and Jolt never adds it again. Now the new ghost's pair is added and starts. |

A pair that both motions allow goes on through the change: it is not refused at step 2, and the carry keeps it at step 3.

## New and changed values

Each value names its existing analogue and follows that analogue's model. "Step start", "update", "ghost pass" and "end of step" are the phases of `JoltPhysicsBackend.Step`.

### `GhostBody.Role`

- Analogue: `GhostBody.Anchor`, which it replaces.
- Store: each `GhostBody` in `BodyRecord.Ghosts`. A `BodyLayer`. The ghost's object layer is `Layers.FrameHull` for `FrameHull`, and `Layers.For(Role, groups, mask)` of the record otherwise.
- Change: set when the ghost is made, never changed. A ghost that needs another role is made again.

| Reader or writer | Phase | What it does |
|---|---|---|
| `WantGhost`, writes | Ghost pass, step start (a change of motion) | Picks the role from the table "The layer and the motion of each ghost". Its `Contract.Assert` compares the role of a ghost that it reuses. |
| `SetCollision`, reads | Step start, intents | Sets every ghost's layer from its role. Today it skips anchor ghosts. That skip is right only for the `FrameHull` role, which ignores groups, so a static anchor ghost keeps a stale layer and fails `AssertInvariants` (a latent bug). |
| The pair test, reads | End of step, step start | The ghost's layer. |
| `AssertInvariants`, reads | On demand | Each ghost's layer is its role's layer. |
| Growth | Sync point, step start | Reads no role. It copies each ghost's layer from Jolt, as today, so it needs no change. |
### The contact motion of each ghost

- Analogue: `FrameWorld.ghostsOfStaticBodies`, the set of ghosts whose real body is static, which it replaces. `StandsForStaticBody` and `StaticBodyGhostCount` become reads of it.
- Store: `FrameWorld`, a table from the ghost's body ID to its contact motion: `Static`, `Kinematic` or `Dynamic`. A character's ghost holds `Dynamic`, since a character counts as dynamic in either form.
- Change: written when the ghost is added, removed when it is removed. A ghost's contact motion never changes, because a change of its body's motion makes a new ghost. `SetGhostOfStaticBody` goes.

| Reader or writer | Phase | What it does |
|---|---|---|
| `FrameWorld.AddGhost`, writes | Ghost pass, step start | Records the contact motion. |
| `FrameWorld.RemoveGhost`, writes | Ghost pass, sync point, step start | Forgets it. |
| The filter in `OnContactAdded`, reads | Update, on Jolt's worker threads | Test 3 of the pair test. Only the physics thread writes the table, between updates, so the workers read it without the gate, as today. |
| The pair test, reads | End of step, step start | Test 3. |
| `AssertInvariants`, reads | On demand | Each ghost's entry is its record's contact motion. |

### The motion rule of ghosts

- Analogue: today's rule for the ghost of a mesh in `GhostMotion`, and `MotionChanges: record.HasMesh` in `GhostSpec`.
- Store: the Jolt motion type of the ghost.
- Change: the ghost of a static body is made with `MotionChanges`, so that Jolt lets it change its motion type, and `Settings` gives it a mass: 1 kg for a mesh, as today, or else the body's own mass, or Jolt's mass from the shape when the body has none. `WantGhost` makes it static while its velocity in its system is zero, and kinematic otherwise, as it does for a mesh today. A drain of a numeric frame turns a static ghost kinematic, as it does for a mesh today.

| Reader or writer | Phase | What it does |
|---|---|---|
| `WantGhost`, writes | Ghost pass | As for a mesh today, for every ghost of a static body. |
| `FrameWorld.ShiftGhosts`, writes | Sync point | As for a mesh today. |
| The pair test, reads | End of step, step start | Test 2. |
| `AssertInvariants`, reads | On demand | A ghost of a movable body is kinematic. A ghost of a static body can change its motion type. |

### The content excess of a frame

- Analogue: the query runner's content of a frame ([Queries and sensors](queries-and-sensors.md)), which it measures the same way, and `FrameBounds.ExitMargin`, which bounds how far a member sticks out.
- Store: scratch of the ghost pass, one value for each semantic frame with a bubble, at the end of the step.
- Change: computed by a first pass of `UpdateGhosts` over the members of every system, before any ghost is placed. For each member, the signed distance of its center from its semantic frame's bubble, plus its radius. The pass keeps each member's center for the second pass, which places the ghosts, so no position is read twice. The stick-out test today reads the same distance for each movable member.

| Reader or writer | Phase | What it does |
|---|---|---|
| The first pass of `UpdateGhosts`, writes | Ghost pass | The largest excess of each frame's members, at least 0. |
| The child ghost test, reads | Ghost pass | Grows the reach by the child's excess, when `GhostsReachContent` is true. |
| The sibling test of a stick-out ghost, reads | Ghost pass | Grows the radius by the sibling's excess, when `GhostsReachContent` is true. |
| `FrameTree.AddParentPlaces` and `AddSiblingPlaces`, their own copy | Sync point, numeric update | The update computes the same excess from the child's bodies, grown by its allowance of 2 s of speed, as `MayStickOut` grows a body. Each grows the reach of a body by it when `NumericBody.GhostsReachContent` is true. |

### `GhostsReachContent`

- Analogue: `BodyRecord.IsMovable` and `NumericBody.Anchor`, facts of a body that its motion and its owned frame decide.
- Store: none. `BodyRecord.GhostsReachContent` is true for a hull, and for a record whose motion is `Static` or `Kinematic`. A character's record holds `Character` or `Dynamic`, so a character reaches only the bubble in either form. `NumericBody.GhostsReachContent` is true for a body that is `Static` or `Kinematic` or owns a frame, the same set in the numeric update.
- Change: it follows the body's motion. A hull that changes its motion keeps it true.

| Reader | Phase | What it does |
|---|---|---|
| The child ghost test and the sibling test of a stick-out ghost | Ghost pass | Grow the reach by the content's excess only when it is true (rule 2, case 1d). |
| `FrameTree.AddParentPlaces` and `AddSiblingPlaces` | Sync point, numeric update | The same, so that a place exists where physics keeps a ghost. |

### The Jolt motion of an interior

- Analogue: the Jolt motion of the hull, `ToMotionType(owner.Motion)`, which `FollowHullMotions` and `ApplyHullMotion` keep.
- Store: the Jolt motion type of the interior body. It follows the record's motion: static while the hull is static, kinematic and at rest otherwise. The interior of a root frame's owner is always static.
- Change: `InteriorSpec` gets `MotionChanges` and the mass of a body that can change its motion, 1 kg for a mesh. `ApplyHullMotion` sets the interior's motion type with the hull's, and activates it when it becomes kinematic.

| Reader or writer | Phase | What it does |
|---|---|---|
| `CreateInterior`, writes | Sync point, step start | The motion of the record's motion. |
| `ApplyHullMotion`, writes | Step start | Follows a change of the hull's motion. |
| Jolt and the filter, read | Update | Jolt's rule on the interior's Jolt motion. |
| The pair test, reads | Step start, end of step | Test 2. |
| `AssertInvariants`, reads | On demand | The interior's motion type follows the record's motion. |
### `GhostPlace.MeetsHulls`

- Analogue: `GhostPlace.OfStaticBody` and `GhostPlace.Sensor`, the facts of the ghost behind a place that `NumericFrameRules.MeetsGhost` reads.
- Store: the `GhostPlace` record of the numeric update.
- Change: true for a place in a child frame of a static body or a hull, false for a place of a stick-out ghost.

| Reader or writer | Phase | What it does |
|---|---|---|
| `FrameTree.AddParentPlaces`, writes | Sync point, numeric update | True for a static body or a hull. |
| `AddStickOutPlaces` and `AddSiblingPlaces`, write | Sync point, numeric update | False. |
| `NumericFrameRules.MeetsGhost`, reads | Sync point, numeric update | A hull meets a place that meets hulls when the hull is dynamic, by Jolt's rule. Today a hull meets no place. |
### The pair test

- Analogue: `PrimariesTouch` and `GhostTouches`, which it replaces.
- Store: none. It is a pure function of the two bodies of a system, their layers, their Jolt motions and their contact motions.

| Reader | Phase | What it does |
|---|---|---|
| `CarryContact` | End of step | Each system that holds a body of each of two entities takes a loose contact over when the pair test passes. It gets a third case: a ghost of each, which only a sensor pair passes. |
| The check after a change of motion | Step start | Ends each started pair of the hull and of the interior that it refuses. |

### `FrameWorld.IsReported`

- Change: it keeps only its first part, both bodies stand for an entity.
- Readers: `OnContactAdded`, in the update.

### The started pairs of one body

- Analogue: `FrameWorld.StartedPairs` and `FrameWorld.ForgetContacts`, the forms for a whole system and for a body that leaves.
- Store: none new. Two members of `FrameWorld` read and change the existing tables: `StartedPairsOf(body, into)` lists the started pairs of one body, and `DropPair(a, b)` forgets one pair, touching or held.

| Reader or writer | Phase | What it does |
|---|---|---|
| The check after a change of motion | Step start | Lists the pairs of the hull and of the interior, and drops each one that the pair test refuses. The ledger counts each one down (`Recount`). |

### `DisposeWorld`

- Change: it loosens the contacts of each ghost that another record keeps in the disposed system, before it drops the ghost. Its comment says that a pair of two ghosts is no contact, which item 2 makes false. Without the change, a started pair of two ghosts keeps a count with no body pair, and `AssertContactInvariants` fails.

## Invariants

`AssertInvariants` checks, in addition to today's checks:

- Each ghost's object layer is the layer of its role.
- Each ghost's entry in its system's table of contact motions is its record's contact motion. The table has no other entry.
- A ghost of a movable body is kinematic in Jolt. A ghost of a static body can change its motion type.
- The interior's motion type follows its record's motion (fork F4).

A state that only a bug produces fails a contract in a Debug build, and goes to `World.Errors` in a Release build (`ReportBug`). In this design, such states are: a started pair whose other body has no record, found by the check after a change of motion, and a ghost that the filter finds without an entry in the table. The Release build ends the pair in the first case. The filter cannot report from a worker thread, so in the second case it applies Jolt's rule alone, and `AssertInvariants` finds the missing entry.

## Numeric frames

The numeric update mirrors the ghost rules in the merge test, so that a body that can touch a ghost lives in the ghost's system.

- `FrameTree.AddParentPlaces` stops skipping a static body that is not a hull (`FrameTree.Numeric.cs`, the test `body.Static && body.OwnedFrame.IsNull`). It still skips an interior (`Static`, `Fixed` and no owned frame), because an interior has no child ghosts (fork F3). Without this change, a fast crate in a numeric frame of the shuttle never shares a system with the slab's ghost, and passes through it.
- It grows the reach of a static body, a kinematic body or a hull by the child's excess, in `AddParentPlaces` and in `AddSiblingPlaces` (fork F2, see "The content excess of a frame" and `GhostsReachContent`). A dynamic plain body or a character keeps the bubble, as physics has it.
- `MeetsGhost` lets a dynamic hull meet a place whose ghost meets hulls (fork F1, `GhostPlace.MeetsHulls`).
- Two tests of `FrameTreeGhostPlaceTests` pin the old rule and change.
- The interior's kinematic motion (fork F4) needs no change. The interior and every place that it can meet are fixed in the frame's own system, so a link between them moves nothing.

## Cost

I counted the new ghosts in every checked-in script with the real World and Jolt, through a measurement in the ghost pass that I did not commit. "New ghosts" are the static plain bodies whose sphere reaches a child bubble in a step. "With content" grows the bubble by the child's excess, which is the rule that this plan builds (fork F2).

| Script | Steps | Ghosts today, mean (max) | Static plain bodies per step, max | New ghosts per step, max | With content, max |
|---|---|---|---|---|---|
| airlock | 1,673 | 0.1 (1) | 0 | 0 | 0 |
| deep-yard | 1,500 | 0 (0) | 0 | 0 | 0 |
| doorway | 350 | 0 (0) | 2 | 0 | 0 |
| drop-box | 120 | 0 (0) | 1 | 1 | 1 |
| orbit-cluster | 18,000 | 0 (1) | 0 | 0 | 0 |
| orbit | 332,692 | 0 (0) | 0 | 0 | 0 |
| spacewalk | 1,968 | 0.2 (1) | 0 | 0 | 0 |
| station | 1,560 | 1.9 (3) | 1 | 0 | 0 |
| surface-yard | 3,720 | 0 (0) | 1 | 0 | 0 |
| throw | 1,075 | 0 (1) | 0 | 0 | 0 |
| walk | 567 | 0 (0) | 0 | 0 | 0 |
| weightless-push | 280 | 0 (0) | 0 | 0 | 0 |
| windows | 1,243 | 0 (0) | 0 | 0 | 0 |

The one new ghost is the floor of drop-box in the frame `sun`, a marker frame with a 1 m bubble 50 m above the floor. The floor is 100 m by 100 m, so its bounding sphere, 70.7 m, reaches that bubble. The ghost makes the Jolt system of an empty frame, with buffers for 4,096 bodies. The ghost holds still, so it is static, and that system never steps. The demo's boarding adds one ghost, the slab's, plus one for each other static body that the shuttle's bubble reaches.

A second measurement, in the ghost pass of step 1 with the exception of rule 2, counted by kind the ghosts that only the growth gives: those of bodies whose sphere reaches a child's content, or a sibling's content for a stick-out ghost, but not the bubble. Every script ran to its end, with the step counts above.

| Kind | Scripts with such a ghost | Such ghosts per step, max | Steps with one |
|---|---|---|---|
| Static plain body | None | 0 | 0 |
| Kinematic body or hull | station | 2 | 68 of 1,560 |
| Dynamic plain body or character, which the exception withholds | None | 0 | 0 |

So the exception changes no checked-in script. Its effect shows at the edges of bubbles in the tests. With the growth for every body, as step 1 first built it, 10 existing tests (11 cases) failed. The child ghosts of dynamic bodies and characters caused 10 cases: mirror pairs as in case 1d, rocks near a crate that sticks out that passed through it or lost their contact, and ghosts that touch nothing but were made and dropped again at every bounce, which allocates (`ContactAllocationTests`). The growth of the sibling test caused the 11th (`GhostContactTests.AMirrorPairIsReportedOnceAndEndsOnceAfterTheLastSystemDropsIt`). With the exception, all of them pass.

The work of the ghost pass grows as follows:

- A system whose semantic frame has a child bubble reads the position of each static member once per step, and measures one distance for each child bubble. A system with no child bubble skips its static members, as today. A station with 2,000 static props and three docked ships costs 2,000 position reads and 6,000 distance tests per step. "Order of the work" adds a benchmark of that scene. If it shows a cost that matters, a later ticket caches the place of static bodies, which move only at a shift or a teleport, or asks Jolt's broad phase for the static bodies near each child bubble.
- The first pass measures one distance for every movable member, which the stick-out test measures today, and keeps the center for the second pass.
- A ghost of a static body that holds still costs Jolt nothing in a step. One that moves is awake for good, as every kinematic ghost is.
- A change of a hull's motion makes each of the hull's ghosts again, and lists the started pairs of the hull and the interior. Changes of motion are rare.

## Tests

Every test runs the real World on the real Jolt backend (`tests/Tertius.Testing`). Each test of a scene across frames has a twin with the same bodies in one frame, and both assert the same events. The names state the behavior.

Item 1, static bodies in child frames:

| Test | Its twin in one frame |
|---|---|
| A person who walks from a station into a parked shuttle's bubble stays on the slab under it, and hears no CollisionEnded at the hand-off | A person who walks across the same slab in one frame |
| A crate that slides on a static floor into a bubble keeps its contact with the floor. It replaces `WorldContactEndTests.AHandOffThatEndsAContactReportsTheEndWithTheFrameChange`, which expects an end. | A crate that slides the same distance on the floor in one frame |
| A crate that sticks out of a ship's bubble stops at a static rock of the parent beyond the bubble's edge (fork F2) | The crate and the rock in one frame |
| A kinematic pusher keeps pushing a crate that enters a frame ahead of it (fork F2) | The pusher and the crate in one frame |
| A ray that starts inside a bubble hits a static body of the parent inside the bubble, at the same distance | The same ray from the parent frame |
| A crate that falls fast in a numeric frame inside a ship stops at the parent's static slab | The same fall in one frame |
| The boxes that rest on the ghost of a static floor in a parked frame fall asleep, and the frame's system stops stepping | The same boxes on the real floor |
| A dynamic hull of a frame inside a ship lands on the ghost of the parent's static slab (fork F1) | The hull lands on the slab in one frame |
| A crate that sticks out of a bubble stops at the hull of a ship of the parent, static or dynamic, beyond the bubble's edge (fork F2) | The crate and the static rock, across frames |
| A ball, and a person who walks, that hit a crate that sticks out of a bubble get no ghost in the frame and leave the crate where it is (rule 2, case 1d) | None. The scene keeps the behavior from before the content, which `GhostContactTests.TheImpulseOfAHitOnTheGhostOfADynamicBodyUsesTheMassesOfBothRealBodies` pins too. |

Item 2, pairs of two ghosts:

| Test | Its twin in one frame |
|---|---|
| A sensor and a body that stick out of sibling frames and overlap only outside both bubbles hear one Collision, and one CollisionEnded when they part | The same sensor and body in the parent frame |
| A dynamic sensor that sticks out of a room inside a ship detects the moving ghost of a static body of the ship's parent | The sensor and the static body in one frame |
| A contact through two ghosts ends when the frame that holds the ghosts is destroyed | The contact of the two real bodies ends when their frame is destroyed |
| A contact through two ghosts goes on when a numeric move replaces one of the ghosts | The same contact in one frame across the same move |
| Two sensors that overlap only through their ghosts report what two real sensors report in one frame | Two real sensors in one frame |

Item 3, sensors and frame owners. Here the twin in one frame is the same sensor beside a plain body with the hull's shape and motion. A plain body cannot change its motion, so the twin of a change of motion is a static sensor beside the real hull in the frame's tree parent.

| Test | Its twin in one frame |
|---|---|
| A sensor of each motion detects a ship of each motion in the ship's parent, in a sibling frame and in the ship's own frame by the table of case 3 (a theory over both motions and the places) | The sensor beside a plain body with the hull's shape and motion |
| A static sensor hears Left when the ship that it detects parks, in the ship's frame and in a sibling | A static sensor beside the hull hears Left when the ship parks |
| A static sensor hears Entered when a parked ship that it overlaps starts to move, in the ship's frame and in a sibling | The same beside the hull |
| A dynamic sensor's touch of a ship goes on when the ship parks | A dynamic sensor touches a plain body of the hull's shape, both static and kinematic |
| A crate that rests on a ship's interior hears no CollisionEnded when the ship's engines start or when the ship parks | None: the crate and the interior are in one frame already |

These existing tests change:

- `WorldContactEndTests.AHandOffThatEndsAContactReportsTheEndWithTheFrameChange` (replaced, above).
- `GhostContactTests.AKinematicSensorDoesNotDetectTheGhostOfAParkedStationsHull` stays green. It now also runs as the case of a kinematic sensor and a static hull in the theory of item 3.
- `MotionChangeTests` (a ghost follows its hull's motion) checks the new ghost and the ledger, not only the invariants.
- `HandOffTests.CharacterWalkingOutOfAFrameIsRecreatedInTheParent` keeps its second floor, which is coplanar with the ground's new ghost. A new case without that floor joins it.
- `FrameTreeGhostPlaceTests` in Core: the two tests that pin "a static body that is not a hull leaves no place". New tests pin the reach by kind: a static body, a kinematic body and a dynamic hull of the parent, and a kinematic body and a dynamic hull of a sibling, leave a place within the content, and a dynamic body or a character of either leaves none.
- The ghost counts in `GhostContactTests`, `MeshGhostTests` and the drop-box scene tests, where a child bubble is near a static body.

## Docs to change

- ARCHITECTURE.md: "Ghosts", the rows "Anchors" and "Ghosts in the merge test" of "Numeric frames", the table of object layers, "Contacts" (the sentence that a pair of two ghosts is no contact, and the example of a crate that slides on a static floor into a bubble), and "Changes of motion" (a hull keeps its contacts only where the new motion allows them).
- [Queries and sensors](queries-and-sensors.md): the row "A contact with a ghost", the known limit of a query that starts inside a bubble, and the paragraph on contacts with ghosts.
- [Numeric frames](numeric-frames.md): the sentence that a static body that is not a hull has no ghost in a bubble.
- The doc of `BodyDesc.IsSensor` (`PhysicsSeam.cs`): what a sensor detects of a frame owner.
- The doc comments that state the old rules: `UpdateGhosts`, `CarryContact`, `IsReported`, `MeetsAsItsRealBody`, `GhostSpec`, `GhostMotion`, `DisposeWorld` and the remarks of `JoltPhysicsBackend.Contacts.cs`.

## Order of the work

All of it is one branch, `ter-imi5-complete-ghosts`, with one commit for each step. The steps run in this order, because each one builds on the last.

1. Static bodies in child frames (`ter-xxmj`). The first pass of the ghost pass and the content excess (F2), which a dynamic plain body or a character does not get (the exception of rule 2). `GhostBody.Role` (F1). The motion rule of ghosts. The two lines of `UpdateGhosts` that skip static plain bodies. The Core mirror in `AddParentPlaces` and `MeetsGhost`. The tests of item 1, and the existing tests that change with it. This step unblocks the demo's boarding.
2. Pairs of two ghosts (`ter-3mwi`, first half). The table of contact motions and the filter on contact motions. The pair test, which replaces `PrimariesTouch` and `GhostTouches`, and the third case of `CarryContact`. `IsReported`. The loosening in `DisposeWorld`. The tests of item 2.
3. Sensors and frame owners (`ter-7ul9`). The Jolt motion of an interior (F4). The check after a change of motion: the ghosts made again, and the started pairs of the hull and the interior judged again. The tests of item 3.
4. A benchmark of the ghost pass in a station with 2,000 static props and three docked ships, in `Tertius.Benchmarks`, beside `MeshGhostBenchmarks`. Its result goes into ARCHITECTURE.md.
5. The docs of "Docs to change", ARCHITECTURE.md last, before the merge into main.

The orchestrator runs coverage on the changed lines after step 5, and Stryker on the changed lines of `Tertius.Physics` and of `FrameTree.Numeric.cs`, since this ticket changes core logic.

## Decided forks

The user settled each fork on 2026-10-18, on the recommended side. Each fork keeps both designs, so that the reasons stay on record. The rest of the doc describes only the chosen side.

### F1. Do child ghosts meet hulls? Design B (User, 2026-10-18)

A child ghost is in the system of a child frame C. That system holds the hulls of C's own child frames. The real static body, or the real hull, meets hulls.

| | A: every child ghost has the `Ghost` role | B: a child ghost of a static body or a hull meets hulls |
|---|---|---|
| Rule | As today: every ghost but an anchor ghost has the `Ghost` role, which meets no hull. | A child ghost of a static body has the body's own layer. A child ghost of a hull has the interior's layer. Stick-out ghosts keep the `Ghost` role. `GhostBody.Role` replaces `GhostBody.Anchor`. |
| Gives | The smallest change. | A dynamic hull of a frame inside a ship meets the ghost of the parent's slab and the ghost of a passing station's hull, as the real bodies meet each other. `SetCollision` also gets right the layer of a static anchor ghost, which it leaves stale today. |
| Costs | A frame inside a ship passes through the ghosts of the parent's static bodies and of other hulls. No scene has such a frame yet. | `GhostBody.Role`, the change in `SetCollision` and in the invariant, `GhostPlace.MeetsHulls` and the change in `MeetsGhost`. Hull ghosts change their layer, so the tests of hull ghosts run again. |

Recommendation: B, which the user chose. It is the ticket's model, a ghost that stands in for its body as fully as Jolt allows. The layer is the engine's own limit, not Jolt's.

### F2. How far does a ghost reach into a child frame? Design B, with an exception (User, 2026-10-18)

| | A: the bubble | B: the content |
|---|---|---|
| Rule | As today: a body has a child ghost when its bounding sphere touches the child's bubble. | A body has a child ghost when its bounding sphere reaches the child's content. The same growth applies to the sibling test of a stick-out ghost. One rule for every kind of body. |
| Gives | No first pass in the ghost pass, and no excess in the numeric update. | The ticket's cases: a crate that sticks out of a ship stops at a rock of the parent just beyond the bubble, and a kinematic pusher keeps its crate when the crate enters a frame ahead of it (`ter-xxmj`). In the scripts it adds ghosts only in station (see "Cost"). |
| Costs | A body of a child that sticks out passes through each static body and each kinematic body of the parent whose sphere misses the bubble. | A first pass over the members of every system in the ghost pass, which reads the distances that the stick-out test reads today. The excess in `AddParentPlaces` and `AddSiblingPlaces`. A mirror pair of two kinematic ghosts for each dynamic body or character near a member that sticks out (case 1d). |

Recommendation: B, which the user chose. Without it, item 1 fixes the slab under the shuttle but not the rock at the bubble's edge that `ter-xxmj` names.

The first build of step 1 built B for every body, and 10 existing tests (11 cases) of contacts at a bubble's edge failed. A dynamic body of the parent near a dynamic member of the child that sticks out got a ghost in the child. The member already had a stick-out ghost in the parent, so the two made a mirror pair, in which each real body met the other as an immovable body (case 1d). The builder measured that the growth is needed only for the bodies that a stick-out ghost cannot meet, and the user decided on 2026-10-18 to keep it only for them. A stick-out ghost is kinematic and has the `Ghost` role, so it meets no static body, no kinematic body and no hull, other than a sensor. So the content growth applies to static bodies, kinematic bodies and hulls. A dynamic plain body or a character keeps today's reach, the bubble, in the child ghost test and in the sibling test of a stick-out ghost, because the member that sticks out meets it through its own stick-out ghost. The exception leaves one case as it is today: two dynamic bodies or characters that stick out of two sibling frames, and touch where neither one's sphere reaches the other's bubble, pass through each other (see "A ghost from scratch").

### F3. Which static bodies get ghosts now? Design A (User, 2026-10-18)

The pure model gives a ghost to every static body that a body of another system can touch. That includes two cases beyond the ticket. The first is an interior, which is a static body in its owner's frame, in the frames inside that frame, such as rooms. The second is a static body that sticks out of its own frame's bubble, such as a ramp of a shuttle that reaches beyond the bubble.

| | A: only the ticket's case now | B: all three now |
|---|---|---|
| Rule | Static plain bodies get child ghosts. Interiors and static bodies that stick out get none yet. A follow-up ticket builds both, with the carry of contacts between an interior and its ghosts across a hand-off (the second half of `ter-3mwi`). | Static plain bodies and interiors get child ghosts, and every static body gets stick-out ghosts, with the `Ghost` role. `GhostBody` records whether it copies the hull or the interior. |
| Gives | The smallest change that unblocks the demo, and no new case of a contact that ends and starts again at a hand-off. | One rule for every static body now. A crate in a room inside a ship meets the ship's walls, and a person of the station meets the outside part of the shuttle's ramp. |
| Costs | A body in a room inside a ship passes through the ship's walls where it sticks out of the room. A body of the parent passes through the outside part of a static body that sticks out of a child. No scene has either yet. | Interior ghosts need the shape of the interior and the carry between an interior and its ghosts, which is the second half of `ter-3mwi`. Without that carry, a crate that moves from a room into the ship hears CollisionEnded and Collision again from the walls. |

Recommendation: A, which the user chose. The orchestrator files the follow-up ticket at the merge. The interior's ghosts belong with the interior's carry, which this ticket does not build.

### F4. What does a sensor detect of a frame owner? Design A (User, 2026-10-18)

| | A: by the hull's motion, everywhere | B: as a static body, everywhere |
|---|---|---|
| Rule | A sensor detects the owner as a body of the hull's motion, in every frame (the table of case 3). The interior is kinematic and at rest while the hull moves, and static while it is static. | Only a dynamic sensor detects a frame owner. The filter judges the hull and its ghosts as static bodies for every sensor. The interior stays static. |
| Gives | What works today stays: a static sensor in a station detects a ship that arrives, and a sensor in another ship detects a passing ship's ghost. The interior cells that `ter-7ul9` names start to work. It matches the contract of `BodyDesc.IsSensor`, that a sensor detects dynamic and kinematic bodies. | Nothing inside a ship changes when its engines start. No change to the interior's motion, and a smaller filter. |
| Costs | A static sensor inside a ship that overlaps a wall hears Entered when the engines start and Left when the ship parks. A game keeps a sensor from detecting its own ship with collision groups. The interior's motion type follows the hull's, with `MotionChanges` and a mass in `InteriorSpec`. | A sensor no longer detects a moving ship, in the station or in another ship. A filter on the real hull's pairs, which have none today. |

Recommendation: A, which the user chose. It keeps every detection that works today and adds the missing ones, and the hull's motion stays the one store that decides (`FrameTree.HullMotion`).

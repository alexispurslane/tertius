# Tertius: notes for Claude

ARCHITECTURE.md is the binding design. This file records what the user prefers, so that new work starts from those preferences instead of rediscovering them. The section "Where the user changed my proposals" matters most: those are places where my first instinct was not what the user wanted.

## Working in this repository

- Run every build and test command in the dev shell: `nix develop --command bash -c '<command>'`.
- Build: `dotnet build Tertius.slnx`. Test one module: `dotnet test --project tests/Tertius.<Module>.Tests`.
- The tests use xUnit v3 on Microsoft.Testing.Platform, with AwesomeAssertions and NSubstitute.
- The double-precision Jolt library comes from `native/joltc.nix`. Read the Jolt pitfalls in project memory before you touch Jolt interop.
- This project uses a CLI ticket system for task management. When you need it, run `tk help`. The tickets in `.tickets/` track what to do and in which order, and `tk ready` lists the work whose dependencies are done. Design docs live in `docs/plans/`, and each epic ticket names its design doc. When you start a ticket, run `tk start`. When its work is merged, run `tk close`. Commit ticket changes together with the work.
- Do each epic or ticket on its own branch, named after the ticket, for example `ter-dtw2-vertical-slice`. Never commit work straight to main. The diff pane of the Claude desktop app shows a branch's changes against main, and the user reads the code there. When the ticket closes, merge the branch into main in this checkout. Then leave the checkout on main until the next epic branches off. Other sessions commit ticket notes in this checkout, and the user wants those commits on main.
- Commit with git. Never push without the user's permission. This project stays on git, not jj. The user chose this, and it overrides the jj rule in their other agent configuration.

## General architectural preferences

- The engine is a Caves of Qud style entity system. An entity is a handle (an index plus a generation) into struct-of-arrays tables for transform, physics and render data. It also has a list of component classes that share one interface.
- Components change only their own entity. Entities talk to each other only through messages, because a component decides what a message means for its entity.
- The language is C#. It has runtime code loading, reflection and late binding, and it can still be fast with spans, refs and ref structs. Use those performance features on hot paths.
- The user likes type-safe variants: C# 15 unions for closed sets such as message targets. A standard union boxes struct cases. To avoid that, use a custom `[Union]` struct with `TryGetValue` methods.
- Prefer simple, honest mechanisms over clever ones. When a feature is not needed yet, leave it out and say so. Examples are read-only "public facts" on entities and automatic event propagation into containers.
- Reflection is fine at load time (blueprints are lists of component types instantiated through reflection), as long as the hot path pays nothing for it.
- The engine must be fast. It has to handle space-scale scenes and dense immersive-sim interactions at once. Measure the cost of every rendering feature, and keep expensive ones switchable.
- Small, focused classes are better than one large class. The user approved splitting the `World` class into subsystems as soon as it grew too big.
- The user values determinism and wants reasons for every ordering rule.
- Automatic engine machinery sits behind the state that it keeps coherent. The owner of that state (for numeric frames, `FrameTree`) grows one choke-point method that World calls once per sync point. That method calls internal pure predicates, which unit tests pin without physics or game objects, and then acts on its own state directly.

## Scope decisions

- No multiplayer, ever. No time warp. Mods come later.
- Only the linear (proper) acceleration of a frame acts on its contents. No Coriolis or centrifugal forces.
- A frame's bubble is an ellipsoid or a sphere, nothing else. In-universe, the engine is a creation of Orbis Tertius, so oddities like a ship's "wake" are acceptable.
- Rendering uses Blinn-Phong (the best version available), not PBR, to keep things simple. It uses deferred shading, multiple depth ranges and impostors captured at render time.

## Where the user changed my proposals

| My proposal | What the user wanted | Notes |
|---|---|---|
| A command buffer for deferred structural changes | No command buffer. The user dislikes the pattern because it duplicates every operation. | Only creation and destruction wait for the sync point. Everything else applies at once. |
| Parallel arrays `pairRecipients` and `pairEvents` | One array of `Delivery(Recipient, Event)` structs | Data that is used together goes in one struct. |
| An n + 1 prefix array `inboxStart`, read as `[s]` and `[s + 1]` | One `InboxRange(Start, Count)` per slot | Explicit ranges are clearer than prefix sums. |
| A chain of `TryGetValue` calls on the target union | A `switch` statement with pattern matching | Prefer pattern matching for variants. |
| Copying each event per recipient at the serial sync point (the user's first idea) | The hybrid: store each event once, keep the per-recipient copy on the workers | The user accepted my performance argument here. Keep work off the serial sync point. |
| Bepu with automatic frames, or float positions between frames | Jolt in double-precision mode with explicit frames only | The user found that Jolt's double mode costs only 5 to 10 percent. |
| Impostors below 48 px, back to geometry above 64 px | Below 200 px, back above 300 px, with the recapture angle derived from the projected size | |
| A boolean for whether a frame moves on rails | The `MotionDriver` enum (`Static`, `Kepler`, `Physics`) | The user agreed to this one of mine. |
| Frames separate from entities | Frames are entities with an extra handle into the frame tree. Frames are also a kind of scene graph that shares code with the normal scene graph. | Hence `FrameHandle` and the generic `PoseTree<TPose>`. |
| A lock-free shared message list | Per-chunk outboxes merged in chunk order | This was my refinement of the user's per-entity worker idea. The user agreed. |
| An entity table whose members take bare slot indices, with the generation check left to the caller | Handles everywhere: every way from an `Entity` to its data checks the generation, and stale handles give an explicit `null`, `false` or exception | Slots are a private storage index. No interface between modules takes or returns a bare slot. The user also rejected a "proof of liveness" marker type that lets later calls skip the check: every member that takes an `Entity` checks it, in every build. |
| Ignore and report physics output that names a destroyed frame, in every build | A state that only a bug can produce fails a contract in Debug builds. Release builds skip it and record it in `World.Errors`. | Example: the proper destroy path rehomes every surviving body into the parent frame, in the same physics batch. So physics can never report a body in a dead frame. |
| A hand-rolled JSON reader and type model (`TypeShape`), to keep comments and exact error positions | The built-in System.Text.Json, with its attributes and contract metadata (`JsonTypeInfo`). Content files are plain JSON with no comments. | Prefer a library with attribute-driven metaprogramming (like Serde) over a hand-rolled type model. Prefer what ships with .NET over an extra dependency, even at a small cost in features. |
| A float gameplay velocity relative to the semantic frame, or `Double3` velocities stored in every row | A `Double3` that each read computes from the home's double frame velocity and Jolt's float local velocity | Storage stays one float vector per body, and reads are exact. |
| Frameless reads such as `Self.Pose` and `Self.Velocity` beside reads in a frame | Every read of a position or velocity names its frame. Writes stay relative to `self.Frame`, and directions stay in its axes. | A read can be about any entity, but an entity writes only itself. |
| A pure planner that returns a plan, plus a separate module that keeps numeric frames coherent | One choke-point method on `FrameTree` that calls internal pure predicates and acts directly | No plan object and no second module beside the owner of the state. |
| Fake physics backends, kept in line with Jolt by a conformance suite | Every test uses the real World and the real Jolt backend. The only physics test double is a wrapper around real Jolt that injects faults. | A stand-in drifts from the code that it copies. A survey found 20 gaps between the stand-ins and the real code. See `docs/plans/real-physics-in-tests.md`. |

## Code style

- Prefer declarative code that states its intent. The user expected deferred LINQ to be as fast as loops. The benchmark showed LINQ is 5 to 26 times slower on hot paths. So give hot paths LINQ-like APIs that allocate nothing, such as `EventList.Of<T>()`, and use normal LINQ elsewhere.
- Name each piece of state for what it holds, and remove variables that only exist as scratch for another variable.
- When the user asks how something works, show the real code, with elisions marked `...` and a `where` list for every name in it.

## Contracts

Follow the global contracts rule. In this repository, the helper is `Tertius.Contract` in `src/Tertius.Core/Contracts/Contract.cs`. `Requires`, `Ensures` and `Assert` compile out of Release builds. `Invariant` is for `AssertInvariants` methods. `WorldOptions.DebugChecks` runs the world's invariant checks at the end of every sync point.

## Testing

- Use red-green TDD. First write tests that define the core behavior, in the spirit of BDD but in xUnit. Then implement. Then use coverage and mutation testing (Stryker.NET with the MTP runner) to make sure that the tests are meaningful. Also add tests that lock in the decisions made along the way.
- Mutation testing is not a gate on every epic. The user decided this on 2026-10-12, after a Stryker run over the whole vertical slice ran for hours. The rules:
  - Every implementation brief asks for hand mutations: for each new test, the agent breaks the production line that the test protects, watches the test fail, and restores the line. The report names each mutation.
  - Each epic still runs coverage on its changed lines, in the orchestrator, before the merge.
  - Stryker.NET (MTP runner) runs on the core modules (Tertius.Core, Simulation, Physics) in the test-hardening epic ter-7e55, scoped to files, not on Host, Demo or the renderer.
  - When an epic changes core logic a lot, a Stryker run on the changed lines of that core module only is fine. It takes minutes, not hours.
  - The helpers in `tools/` run coverage and Stryker on changed lines or on whole files. `tools/README.md` gives the commands and the Stryker pitfalls.
- NSubstitute cannot substitute interfaces with ref struct parameters, such as `IComponent`. Use small hand-written test components for those.
- Most tests run single-threaded, so that they are deterministic and fast under mutation testing. A separate test covers parallel determinism.
- Renderer tests are snapshot tests plus CPU tests. A snapshot test renders a scene headlessly and compares it with an approved reference image through the FLIP perceptual metric. A failure writes the actual image and a FLIP heat map next to the reference. An environment variable re-approves on purpose.
- A snapshot proves only that nothing changed. So the first approval of every reference image is a real review against the scene description.
- Some agents add or re-approve a reference image, or see a snapshot failure. Each such agent must open the reference, the actual image and the heat map with the Read tool. Its report describes what it saw.
- Deterministic rendering for tests (alpha 1, fixed seeds, impostor captures forced or off, no temporal effects) is one easy switch in `RendererOptions`.
- Everything else in the renderer that is C#, such as batching, culling, cascade fitting and impostor decisions, gets ordinary CPU tests.

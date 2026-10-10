# Tertius: notes for Claude

ARCHITECTURE.md is the binding design. This file records what the user prefers, so that new work starts from those preferences instead of rediscovering them. The section "Where the user changed my proposals" matters most: those are places where my first instinct was not what the user wanted.

## Working in this repository

- Run every build and test command in the dev shell: `nix develop --command bash -c '<command>'`.
- Build: `dotnet build Tertius.slnx`. Test one module: `dotnet test --project tests/Tertius.<Module>.Tests`.
- The tests use xUnit v3 on Microsoft.Testing.Platform, with AwesomeAssertions and NSubstitute.
- The double-precision Jolt library comes from `native/joltc.nix`. Read the Jolt pitfalls in project memory before you touch Jolt interop.
- Commit with git. Never push without the user's permission. This project stays on git, not jj. The user chose this, and it overrides the jj rule in their other agent configuration.

## General architectural preferences

- The engine is a Caves of Qud style entity system. An entity is a handle (an index plus a generation) into struct-of-arrays tables for transform, physics and render data. It also has a list of component classes that share one interface.
- Components change only their own entity. Entities talk to each other only through messages, because a component decides what a message means for its entity.
- The language is C#. It has runtime code loading, reflection and late binding, and it can still be fast with spans, refs and ref structs. Use those performance features on hot paths.
- The user likes type-safe variants: C# 15 unions for closed sets such as message targets. A standard union boxes struct cases. To avoid that, use a custom `[Union]` struct with `TryGetValue` methods.
- Prefer simple, honest mechanisms over clever ones. When a feature is not needed yet, leave it out and say so. Examples are read-only "public facts" on entities and automatic event propagation into containers.
- Reflection is fine at load time (blueprints are lists of component types instantiated through reflection), as long as the hot path pays nothing for it.
- Small, focused classes are better than one large class. The user approved splitting the `World` class into subsystems as soon as it grew too big.
- The user values determinism and wants reasons for every ordering rule.

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

## Code style

- Prefer declarative code that states its intent. The user expected deferred LINQ to be as fast as loops. The benchmark showed LINQ is 5 to 26 times slower on hot paths. So give hot paths LINQ-like APIs that allocate nothing, such as `EventList.Of<T>()`, and use normal LINQ elsewhere.
- Name each piece of state for what it holds, and remove variables that only exist as scratch for another variable.
- When the user asks how something works, show the real code, with elisions marked `...` and a `where` list for every name in it.

## Contracts

Follow the global contracts rule. In this repository, the helper is `Tertius.Contract` in `src/Tertius.Core/Contracts/Contract.cs`. `Requires`, `Ensures` and `Assert` compile out of Release builds. `Invariant` is for `AssertInvariants` methods. `WorldOptions.DebugChecks` runs the world's invariant checks at the end of every sync point.

## Testing

- Use red-green TDD. First write tests that define the core behavior, in the spirit of BDD but in xUnit. Then implement. Then use coverage and mutation testing (Stryker.NET with the MTP runner) to make sure that the tests are meaningful. Also add tests that lock in the decisions made along the way.
- NSubstitute cannot substitute interfaces with ref struct parameters, such as `IComponent`. Use small hand-written test components for those.
- Most tests run single-threaded, so that they are deterministic and fast under mutation testing. A separate test covers parallel determinism.

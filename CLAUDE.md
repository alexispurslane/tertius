# Tertius: notes for Claude

ARCHITECTURE.md is the binding design, and the code and `tools/README.md` say how things work. This file holds only what they do not show: the user's rules for this project, the places where my first instinct was wrong, and the gotchas that cost time before.

## Workflow

- Run every build and test command in the dev shell: `nix develop --command bash -c '<command>'`.
- `bash tools/test-all.sh` runs every test module in Checked and Release. Checked is the test configuration: Debug's contracts with Release's optimized code. The script does not run in an agent's worktree, so such an agent runs `dotnet build Tertius.slnx -c <config>` and `dotnet test --project tests/Tertius.<Module>.Tests -c <config>` by hand, for both configurations.
- Do each ticket or epic on its own branch, named after the ticket, such as `ter-dtw2-vertical-slice`. Never commit work straight to main. The user reads the code in the diff pane of the Claude desktop app, against main.
- When the ticket closes, merge its branch into main in this checkout, and leave the checkout on main. Other sessions commit ticket notes in this checkout, and the user wants those on main.
- Use git, not jj. This overrides the jj rule in the user's other agent configuration. Never push without the user's permission.
- The last step of every ticket that changes the engine updates ARCHITECTURE.md to match the code, before the merge. It is part of the public engine docs.
- At the end of every piece of work that shows on screen, send the user two to four screenshots of the rendered game. The user often follows from a phone and cannot run the game. Take them with a headless run of the real renderer, `dotnet run --project src/Tertius.Demo -- --script scripts/<name>.json --headless --out <folder>`. Look at each image first, and give each a one-line caption.
- Read the Jolt pitfalls in project memory before you touch Jolt interop. The double-precision Jolt library comes from `native/joltc.nix`.

## Priorities

When speed and a project-wide rule pull apart, use this order. The user set it on 2026-10-24.

1. Performance beats sloppiness. Do not accept slow code because it was easier to write.
2. Consistency beats general performance. When another form is a little faster in theory, keep the uniform form, such as the switch rule.
3. Benchmarked performance where it matters beats consistency. Break the uniform form only where a profile or a benchmark shows that the spot costs real time.

An exception to a rule for speed needs a measurement of the cost in context first. Example: a switch over the 184-byte `MotionIntent` copies the union, but the copy costs 2 to 5 ns per call, so the switches stay.

## Design preferences

- Prefer simple, honest mechanisms over clever ones. Leave out a feature that is not needed yet, and say so.
- Prefer the real mechanism over a look-alike. If the real one needs an engine epic, the epic comes first. Example: the demo's exhaust plumes waited for real particles instead of glow meshes.
- Keep one store for each piece of state. A second store beside it, such as a side table, a mirror row or a mode enum, can disagree with the first.
- Stored state describes the last finished step, like the collision rows. A step in flight is a named, derived read of the pending intent.
- Automatic machinery lives in one choke-point method on the owner of the state, which calls internal pure predicates and acts directly. Use no planner and no plan object.
- Every read of a position or a velocity names its frame. An entity writes only itself, relative to `self.Frame`.
- Handles everywhere. Every member that takes an `Entity` checks its generation, in every build. No interface between modules takes a bare slot.
- A state that only a bug produces fails a contract in Debug, and Release records it in `World.Errors` and skips it.
- A function goes into the struct that it belongs to, such as `KeplerOrbit.Lambert`, never into a static helper class. This holds for demo needs too. If that means an engine change, make the engine change.
- A staged component is one flat state machine: one `[GeneratedUnion]` of every phase, one pure `Next(state, event)`, and a switch to delegates.
- Data that is used together goes into one struct. Prefer an explicit `Range(Start, Count)` over a prefix-sum array.
- Use no command buffer for deferred changes. Only creation and destruction wait for the sync point.
- Prefer what ships with .NET, and libraries with attribute-driven metaprogramming, over a hand-rolled model.
- The user values determinism and wants a reason for every ordering rule.
- No multiplayer and no time warp, ever. Mods come later.

## Gotchas

- Deferred LINQ is 5 to 26 times slower than loops on hot paths. Hot paths use the allocation-free LINQ-like APIs, such as `EventList.Of<T>()`.
- A union switch must end in a `null` arm, but the compiler never asks for it. Without the arm, the empty value throws.
- In `Case c => c.Moved(...), null => default`, the switch takes the type of the case. So `default` becomes a real case, and the implicit conversion makes a union that holds it. Name the union in an arm, such as `Case c => new U(c.Moved(...))`.
- A generated constructor cannot refuse a bad value. A union with invariants checks them in its factory methods.
- `IComponent` methods take ref struct parameters, which no mocking library can substitute. Tests use small hand-written components.
- The pre-commit hook builds and tests the changed modules, so a commit takes minutes. Its report of complex, long or duplicated code asks you to refactor the touched code in the same commit.

## Testing

- Every test of physics runs the real World on the real Jolt backend. The only physics test double is `FaultInjectingBackend` in `tests/Tertius.Testing`, a wrapper around real Jolt. A test of a bug-only state edits one step's output with `EditOutput`. A test of a valid hand-off flies a ball into a bubble with `Scenes.ApproachToBubble`. Fake backends drifted from the real code before.
- Mutation testing is not a gate on every epic, because a Stryker run over the whole vertical slice took hours.
  - Every implementation brief asks for hand mutations. For each new test, the agent breaks the line that the test protects, watches the test fail, and restores the line. The report names each mutation. Mutation builds need `--no-incremental`.
  - Each epic runs coverage on its changed lines before the merge.
  - Stryker runs only on Core, Simulation and Physics, scoped to files or to changed lines.
- A snapshot test proves only that nothing changed. So the first approval of a reference image is a real review against the scene description. If an agent adds or re-approves a reference image, or sees a snapshot fail, it opens three images with the Read tool. They are the reference, the actual image and the FLIP heat map, and its report describes them.
- `WorldOptions.DebugChecks` runs the world's invariant checks at the end of every sync point. `RendererOptions` has one switch for deterministic rendering in tests.

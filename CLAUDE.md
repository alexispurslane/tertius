# Tertius: notes for Claude

ARCHITECTURE.md is the binding design, and the code and `tools/README.md` say how things work. This file holds only what they do not show: the user's rules for this project, the places where my first instinct was wrong, and the gotchas that cost time before.

## Workflow

- Run every build and test command in the dev shell: `nix develop --command bash -c '<command>'`.
- `bash tools/test-all.sh` runs every test module in Checked and Release. Checked is the test configuration: Debug's contracts with Release's optimized code. An agent in an isolated worktree cannot run it, because Claude Code refuses a script there: it cannot prove that the script runs no git outside the worktree. Such an agent runs `nix develop --command dotnet test --solution Tertius.slnx -c Checked`, then the same with `-c Release`. Each command builds and runs every test project in about two minutes.
- Do each ticket or epic on its own branch, named after the ticket, such as `ter-dtw2-vertical-slice`. Never commit work straight to main. The user reads the code in the diff pane of the Claude desktop app, against main.
- When the ticket closes, merge its branch into main in this checkout, and leave the checkout on main. Other sessions commit ticket notes in this checkout, and the user wants those on main.
- Use git, not jj. This overrides the jj rule in the user's other agent configuration.
- Push main to `origin` without asking, at least after each epic merges. The user decided on 2026-10-24 that a regular push is better than none.
- The last step of every ticket that changes the engine updates ARCHITECTURE.md to match the code, before the merge. It is part of the public engine docs.
- At the end of every piece of work that shows on screen, send the user two to four screenshots of the rendered game. The user often follows from a phone and cannot run the game. Take them with a headless run of the real renderer, `dotnet run --project src/Tertius.Demo -- --script scripts/<name>.json --headless --out <folder>`. Look at each image first, and give each a one-line caption.
- Read the Jolt pitfalls in project memory before you touch Jolt interop. The double-precision Jolt library comes from `native/joltc.nix`.

## Style

[STYLE.md](STYLE.md) holds the C# style of `src`, with the priority order of performance and consistency. [tests/STYLE.md](tests/STYLE.md) holds the style of the tests. Read both before you write code.

## Design preferences

- Prefer simple, honest mechanisms over clever ones. Leave out a feature that is not needed yet, and say so.
- Prefer the real mechanism over a look-alike. If the real one needs an engine epic, the epic comes first. Example: the demo's exhaust plumes waited for real particles instead of glow meshes.
- Keep one store for each piece of state. A second store beside it, such as a side table, a mirror row or a mode enum, can disagree with the first.
- Stored state describes the last finished step, like the collision rows. A step in flight is a named, derived read of the pending intent.
- Automatic machinery lives in one choke-point method on the owner of the state, which calls internal pure predicates and acts directly. Use no planner and no plan object.
- Use no command buffer for deferred changes. Only creation and destruction wait for the sync point.
- An entity writes only itself, relative to `self.Frame`. Entities talk to each other only through messages.
- The user values determinism and wants a reason for every ordering rule.
- No multiplayer and no time warp, ever. Mods come later.

## Gotchas

- The pre-commit hook builds and tests the changed modules, so a commit takes minutes. Its report of complex, long or duplicated code asks you to refactor the touched code in the same commit.

## Testing

- `WorldOptions.DebugChecks` runs the world's invariant checks at the end of every sync point.
- Mutation testing is not a gate on every epic, because a Stryker run over the whole vertical slice took hours.
  - Every implementation brief asks for hand mutations. For each new test, the agent breaks the line that the test protects, watches the test fail, and restores the line. The report names each mutation. Mutation builds need `--no-incremental`.
  - Each epic runs coverage on its changed lines before the merge.
  - Stryker runs only on Core, Simulation and Physics, scoped to files or to changed lines.
- A snapshot test proves only that nothing changed. So the first approval of a reference image is a real review against the scene description. If an agent adds or re-approves a reference image, or sees a snapshot fail, it opens three images with the Read tool. They are the reference, the actual image and the FLIP heat map, and its report describes them.

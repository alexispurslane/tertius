# Code quality tooling

Epic ticket: `ter-9gj5`.

This epic adds the checks that keep the code readable: compiler checks of doc comments, reports of complex, long and duplicated code, a docs site, and a cleanup that formats and modernizes the code before every push. The user chose each tool on 2026-10-21, after a comparison of the options, and chose where each one runs on the same day.

## Where each check runs

| Stage | Check | Blocks what |
|---|---|---|
| `git commit` (`.githooks/pre-commit`) | An incremental build of the changed projects and their test projects, with every analyzer | The commit, on an error |
| `git commit` | The tests of the changed code, in Debug | The commit, on a failed test |
| `git commit` | jscpd duplicates, CA1502 complexity and MA0051 length, for the changed lines only | Nothing. It is a report. |
| `git push` (`.githooks/pre-push`) | `tools/cleanup.sh` on the C# files that the pushed commits change | The push, if the cleanup changes a file. The hook commits the cleanup, and the next push sends it. |
| `git push` | DocFX builds the docs site | The push, on a DocFX error |
| Before a merge into main | `tools/test-all.sh`: every test, in Debug and Release | The merge |

The commit hook changes no file and runs only what the commit touches, so that a commit stays fast. The cleanup loads a whole solution, which takes tens of seconds, so it runs once per push instead.

## Decisions

1. The build writes the XML documentation file (`GenerateDocumentationFile`), so that the compiler checks every doc comment: `cref` targets, `param` and `paramref` names, and the XML itself. CS1591 (a public member without a doc comment) stays off until the doc comment rewrite, because 805 public members in `src` have none.
2. `AnalysisLevel` is `latest-recommended`, and `TreatWarningsAsErrors` is on. The root `.editorconfig` turns off each rule that flags a deliberate pattern of this codebase, with the reason in a comment above it. For example, CA1707 (the `_` character in names) stays off in test projects, because test names contain `_`. A deliberate exception in one type is a `SuppressMessage` attribute on that type, with a `Justification`. CS1573 (a `param` tag for only some parameters) is off too, because a doc comment documents only the parameters that need more than their name and type.
3. CA1502 measures the cyclomatic complexity of a method: the number of branches plus one. Its limit is 15, in `analyzers/CodeMetricsConfig.txt`. Meziantou MA0051 measures the length of a method: 60 lines or 40 statements, its defaults. `MeziantouAnalysisMode=None` turns off every other Meziantou rule. Both rules are suggestions, so they never fail a build, and the existing code needs no baseline. After the first cleanup on 2026-10-21, 55 methods were over the complexity limit and 108 were over the length limit, in src, tests and benchmarks.
4. Every compile writes its diagnostics, suggestions included, to `obj/<configuration>/<framework>/analyzers.sarif` (`ErrorLog` in `Directory.Build.targets`). `tools/commit_report.py` reads CA1502 and MA0051 from it and prints each one whose method holds a changed line. It finds the end of a method by matching its braces. An up-to-date project does not compile again, so the file always describes the last compile, whoever ran it.
5. jscpd 5 is a Rust crate. `native/jscpd.nix` builds it from crates.io with a pinned hash, and the dev shell has it. `tools/duplicates.sh` writes an HTML and a console report of the whole code. The commit report prints only the clones with a copy that holds a changed line.
6. DocFX reads the compiled assemblies and their XML files, not the source. Thus its own C# parser never meets C# 15 syntax. The site also holds ARCHITECTURE.md, GUIDE.md, tools/README.md, the design docs and the source files. DocFX is a .NET local tool, pinned in `.config/dotnet-tools.json`, because the nixpkgs build cannot start on macOS. `docfx.json` sits at the repository root, because DocFX refuses content outside the folder of its configuration. `tools/docs.sh` builds the site, and it can serve it.
7. The commit hook tests the changed code: a change in `src/Tertius.X` runs every test of `tests/Tertius.X.Tests`, and a change in a test file runs only the test classes in that file. `src/Tertius.Demo` maps to `tests/Tertius.Host.Tests`. A change in `tests/Tertius.Testing` or in the benchmarks only builds. The hook builds only those projects and the projects that they reference, so a change that breaks a project downstream shows in the full run before the merge, not in the commit.
8. The cleanup has two passes, in `tools/cleanup.sh`. First, `dotnet format style` applies the Roslyn modernization fixes that ReSharper does not make: collection expressions, primary constructors, pattern matching, switch expressions, `using` declarations, index and range operators, static lambdas and local functions, readonly structs and members, and more. `.editorconfig` sets each of these IDE rules to suggestion, and the script reads the list from there. Builds do not report them, because `EnforceCodeStyleInBuild` is off. Then `jb cleanupcode` runs ReSharper's cleanup with every C# task on: syntax style, redundancies, auto-properties, readonly fields, `using` order, member layout and formatting. ReSharper runs last, so its formatting wins.
9. The ReSharper cleanup is the custom profile "Tertius" in `Tertius.slnx.DotSettings`. It is the built-in Full Cleanup, minus the arrangement of arguments, which removes named arguments. `.editorconfig` holds the syntax style and the formatting: modern syntax, `var` only where the type is evident, lines of at most 120 characters, and doc comment text as written. The file layout groups members by kind (constants, static fields and properties, instance fields, constructors, instance properties, everything else) and keeps the author's order inside each group. Static fields and static properties share one group, because a static initializer can read a static property above it. Rider reads both files, so its cleanup gives the same result.
10. The pre-push hook cleans the C# files that the pushed commits change, builds the solution, and commits the result as one commit. Git chose the commits to push before the hook ran, so the hook then stops the push, and the next push sends the cleanup commit too. A file with uncommitted changes stops the hook before the cleanup, so that the cleanup commit holds only the cleanup. The first cleanup of the whole codebase is one commit, and `.git-blame-ignore-revs` names it.
11. The dev shell sets `core.hooksPath` to `.githooks`. Outside the dev shell, a hook runs itself again inside it.

## Not in this epic

- The rewrite of the doc comments. It is its own epic, after this one: a short style guide, then one pass per module, with the docs site and the `cref` checks as guides.
- IDE code style rules in the build (`EnforceCodeStyleInBuild`). The user decided on 2026-10-21 that the pre-push cleanup modernizes the code and that the build never fails on style. CA1822 (members that could be static) stays off too.
- A gate on complexity and length. The user chose a report on the changed code.

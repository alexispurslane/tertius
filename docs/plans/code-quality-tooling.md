# Code quality tooling

Epic ticket: `ter-9gj5`.

This epic adds the checks that keep the code readable: compiler checks of doc comments, limits on method complexity and length, a duplicate-code report, a docs site, and a formatter that runs before every push. The user chose each tool on 2026-10-21, after a comparison of the options.

## Where each check runs

| Stage | Check | Blocks what |
|---|---|---|
| `git push` (a pre-push hook) | ReSharper `jb cleanupcode` formats the C# files that the pushed commits change | If a file changes, the push |
| `dotnet build` | The compiler checks doc comments. The .NET analyzers, CA1502 and Meziantou MA0051 check the code. | The build, through `TreatWarningsAsErrors` |
| `tools/` scripts | jscpd reports duplicate code. DocFX builds the docs site. | Nothing. They are reports. |

Fast checks block, and slow checks are reports. The formatter runs before a push and not before a commit, because `jb cleanupcode` loads the whole solution first, and agents commit often.

## Decisions

1. The build writes the XML documentation file (`GenerateDocumentationFile`), so that the compiler checks every doc comment: `cref` targets, `param` and `paramref` names, and the XML itself. CS1591 (a public member without a doc comment) stays off until the doc comment rewrite, because 805 public members in `src` have none.
2. `AnalysisLevel` is `latest-recommended`. The root `.editorconfig` turns off each rule that flags a deliberate pattern of this codebase, with the reason in a comment above it. For example, CA1707 (the `_` character in names) stays off in test projects, because test names contain `_`. A deliberate exception in one type is a `SuppressMessage` attribute on that type, with a `Justification`. CS1573 (a `param` tag for only some parameters) is off too, because a doc comment documents only the parameters that need more than their name and type.
3. CA1502 limits the cyclomatic complexity of a method. Cyclomatic complexity is the number of branches plus one. Meziantou MA0051 limits the lines and the statements of a method. `CodeMetricsConfig.txt` and `.editorconfig` hold the limits. Only MA0051 of the Meziantou rules is on.
4. A method that breaks a limit on the day the rule arrives gets a named suppression in its project's `GlobalSuppressions.cs`, with the justification "Baseline: existing code, see ter-gpee". A follow-up ticket removes them. New code fails the build at once.
5. jscpd 5 is a Rust crate. `native/jscpd.nix` builds it from crates.io with a pinned hash, and the dev shell has it. `tools/duplicates.sh` writes an HTML and a console report. It never fails.
6. DocFX reads the compiled assemblies and their XML files, not the source. Thus its own C# parser never meets C# 15 syntax. The site also holds ARCHITECTURE.md, GUIDE.md, tools/README.md, the design docs and the source files. DocFX is a .NET local tool, pinned in `.config/dotnet-tools.json`, because the nixpkgs build cannot start on macOS. `docfx.json` sits at the repository root, because DocFX refuses content outside the folder of its configuration. `tools/docs.sh` builds the site, and it can serve it.
7. The formatter is `jb cleanupcode` from the `JetBrains.ReSharper.GlobalTools` local tool, pinned in `.config/dotnet-tools.json`. It runs the reformat-only profile, so it changes whitespace and line breaks, not code. `Tertius.slnx.DotSettings` holds the formatting rules, so that Rider and the hook format the same way. The hook lives in `.githooks/pre-push`. The dev shell sets `core.hooksPath` to that folder. The first reformat of the whole codebase is one commit, and `.git-blame-ignore-revs` names it.
8. The order: doc checks and analyzers first, then the formatter and the reformat, then the complexity limits. The reformat changes line counts, so MA0051 must measure the code after the reformat.

## Not in this epic

- The rewrite of the doc comments. It is its own epic, after this one: a short style guide, then one pass per module, with the docs site and the `cref` checks as guides.
- IDE code style rules in the build (`EnforceCodeStyleInBuild`). The user decided on 2026-10-21 that the pre-push cleanup modernizes the code and that the build never fails on style. CA1822 (members that could be static) stays off too.

# Tools

These helpers run every test project in one command, start JetBrains Rider inside the dev shell, run code coverage and Stryker.NET mutation testing on the lines that a branch changed, report duplicated code, measure the sloppiness of the code, build the docs site, and clean up the code. The git hooks in `.githooks/` call some of them. For the rules on which helper to run, read the Testing section of CLAUDE.md. The scripts are tool code: they never ship, so they skip polish.

Run every command in the dev shell, from the repository root. The build copies the Jolt library and the Vulkan loader next to each output, so the tests need no `DYLD_*` variable. Outside the dev shell, the GPU tests cannot find `libvulkan`, and the scripts stop with a message. Write the output to a folder outside the repository, for example `/tmp/tertius-coverage`.

## Every test project in one command

`tools/test-all.sh` builds the solution once per build configuration. Then it runs every test project in every configuration with `--no-build`, all at the same time, one process each. Each process also runs its own test classes in parallel. The script works from any folder in the repository:

```bash
nix develop --command bash -c 'bash tools/test-all.sh'
```

For each configuration and module, the script prints one line with the counts, and one indented line for each failed test:

```
Checked Core: total 512, failed 1, succeeded 511, skipped 0, exit code 2
    failed: Tertius.Core.Tests.FrameTreeTests.Some_test
```

The options:

- `-c Checked,Release` gives the build configurations, separated by commas. The default is Checked and Release. Checked has the contracts of Debug and the optimized code of Release (`Directory.Build.targets`). The tests run about five times faster in Checked than in Debug, and Debug stays unoptimized for the debugger.
- `-m Core,Physics` gives the test modules, separated by commas. The default is all eight: Core, Simulation, Physics, Rendering, Host, Unions (the tests of the union generator), Style (the tests of the style analyzer) and Slop (the tests of the slop metrics).
- `-j <runs>` gives the most test processes that run at once. The default, 0, runs every configuration and module at once.
- `-o <folder>` gives the folder for the logs. The default is a new temporary folder, and the first line of the output names it. The script writes `<folder>/<configuration>/build.log` and one `<folder>/<configuration>/<module>.log` with the full test output of each module.
- Everything after `--` goes to each `dotnet test` command unchanged, for example a filter:

  ```bash
  nix develop --command bash -c 'bash tools/test-all.sh -c Checked -m Core,Physics -- --filter-class "*FrameTree*"'
  ```

The exit code is 1 when a build fails, a test fails, or a test run ends without its summary. A module in which no test matches the filter is not a failure: its line ends with "(no tests ran)". When a build fails, the script prints the first compiler errors and skips the tests of that configuration. The exit code is 2 when an option is wrong or names a module that has no test project.

## JetBrains Rider

Rider must use the dev shell's .NET SDK and variables. If you open the project from the Dock, Rider uses an SDK of its own and cannot find the Jolt library or Vulkan. `tools/rider.sh` starts Rider inside the dev shell. It also keeps `.nix/dotnet-sdk`, a stable link to the dev shell's SDK. The link is also a root for the Nix garbage collector, so the SDK stays in the store.

1. Quit Rider. A running Rider opens the folder with its own variables, and the script stops with a message.
2. Start Rider from the repository root:

   ```bash
   bash tools/rider.sh
   ```

3. Do this step once. In Rider, open Settings > Build, Execution, Deployment > Toolset and Build. Set ".NET CLI executable path" to `.nix/dotnet-sdk/share/dotnet/dotnet` in the repository, as an absolute path. Then set "MSBuild version" to the MSBuild of that SDK.
4. Do this step once. In Settings > Build, Execution, Deployment > Unit Testing, turn on the support for Microsoft.Testing.Platform. The test projects use it (see `global.json`).

The script starts Rider EAP (`/Applications/Rider EAP.app`) by default, because the stable Rider 2026.2 does not understand C# 15 unions and marks every pattern match on a union as an error. The Rider 2026.3 EAP understands them. Set `RIDER` to start another Rider, for example `RIDER=/Applications/Rider.app bash tools/rider.sh`.

Start Rider with the script every time. When the flake changes the SDK, the script moves the link, and the setting of step 3 stays correct.

The build copies the Jolt library and the Vulkan loader next to each output, from `TERTIUS_JOLTC_DIR` and `TERTIUS_VULKAN_DIR`. macOS removes `DYLD_*` variables from Rider, because Rider has the hardened runtime, so a test that Rider starts never sees `DYLD_FALLBACK_LIBRARY_PATH`. The loader finds MoltenVK through `VK_ICD_FILENAMES`, which Rider keeps.

## Coverage on changed lines

1. Run every test project with coverage, in both build configurations:

   ```bash
   nix develop --command bash -c 'bash tools/coverage/run.sh /tmp/tertius-coverage Debug Release'
   ```

2. Print the coverage of the lines that this branch changed in `src/`, against `main`:

   ```bash
   nix develop --command bash -c 'python3 tools/coverage/changed-lines.py /tmp/tertius-coverage/Debug /tmp/tertius-coverage/Release'
   ```

If either configuration covers a line, the line counts as covered. Debug covers the contracts, and Release covers the code behind `#if !DEBUG`. The script lists the files with the most uncovered lines first, with the uncovered lines and the lines whose branches are only partly covered.

## Mutation testing with Stryker.NET

1. Write a configuration for one source project. Name the test projects that exercise it:

   ```bash
   nix develop --command bash -c 'python3 tools/stryker/config.py --project Core --tests Core Simulation Physics --out /tmp/tertius-stryker/Core'
   ```

   By default, the configuration mutates only the lines that this branch changed. To mutate whole files, add `--files` with paths relative to the project folder, for example `--files Frames/FrameTree.cs`.

2. Run Stryker on that project:

   ```bash
   nix develop --command bash -c 'bash tools/stryker/run.sh Core /tmp/tertius-stryker/Core'
   ```

3. List the mutants that no test caught, and the mutants in the spans that did not compile:

   ```bash
   nix develop --command bash -c 'python3 tools/stryker/survivors.py /tmp/tertius-stryker/Core/out/reports/mutation-report.json --config /tmp/tertius-stryker/Core/stryker-config.json'
   ```

Triage each mutant in the list as the `test-hardening` skill says: kill it with a test, or mark it equivalent with a one-sentence reason in a Stryker ignore comment.

Break each line with a mutant that did not compile by hand. Run the tests that cover the line. Then restore the line. Some of those mutants are invalid, for example `*` to `/` on a quaternion type that has no `/`. Skip those.

## Stryker.NET pitfalls

These cost time on the vertical slice, so the scripts handle them:

- Stryker.NET 5.0.0 reads `coverage-analysis` only from a configuration file, not from the command line. Without `perTest`, every mutant runs every test.
- Stryker.NET matches a `mutate` pattern against the full path. A pattern without the `**/` prefix matches no file, and the run then has no mutants.
- In a pattern such as `**/Frames/FrameTree.cs{402..1704}`, the numbers are character offsets in the file, not line numbers.
- Stryker.NET leaves a mutant that does not compile out of the score. A method with a pattern variable in a condition, such as `x is { } box && ...`, often makes every mutant in the method fail to compile (error CS0165). On the vertical slice, the Core score was 100% of 155 mutants, but 52 more mutants never ran. Hand mutations of their lines found three gaps.
- The progress bar shows only on a terminal. The `-L` flag writes the log to `out/logs`, so that you can follow a run in the background.

Do not run two Stryker runs at once, and do not run Stryker in subagents. Two runs compete for the CPU, and their tests time out.

## Duplicate code

`tools/duplicates.sh` reports duplicated C# code with jscpd. jscpd 5 is a Rust program, and `native/jscpd.nix` builds it from crates.io for the dev shell. A scan of `src/` takes less than a second. The report never fails, so it is a guide for refactoring and not a gate.

```bash
nix develop --command bash -c 'bash tools/duplicates.sh'
```

The script prints each clone with its two places, then a table with the share of duplicated lines and tokens. A clone is a block of at least 70 tokens, about seven lines, that occurs twice. The options:

- `-t` scans `tests/` and `benchmarks/` too. The default is `src/` only.
- `-r` also reports renamed copies: blocks that differ only in names and literals.
- `-s` also reports near-miss methods: methods whose syntax trees are at least 80 percent alike.
- `-o <folder>` also writes an HTML report to `<folder>/jscpd-report.html`.
- Everything after `--` goes to jscpd unchanged, for example `-- --min-tokens 50`.

## Slop metrics

`tools/Tertius.Slop` is a console program that measures how sloppy the code in `src/` is, after the post [Measuring code sloppiness](https://earendil.com/posts/measuring-code-sloppiness/). Its first metric is the erosion. It reads the syntax of each file with Roslyn and builds no compilation, so it reads all of `src/` in about half a second. `docs/plans/code-quality-tooling.md`, decision 16, has the rules and their reasons.

```bash
nix develop --command dotnet run --project tools/Tertius.Slop -c Release -- erosion
nix develop --command dotnet run --project tools/Tertius.Slop -c Release -- erosion /path/to/repository --text
```

The command line is `Tertius.Slop <metric> [root] [--text]`. The metric is a subcommand, so that a new metric is a new subcommand beside `erosion`. The root is the repository, the current folder by default. The program reads every `.cs` file under `<root>/src`, but not the `obj` and `bin` folders, a file whose name ends in `.g.cs`, or a file that starts with an `<auto-generated` comment.

- The cyclomatic complexity (CC) of a function is 1, plus 1 for each `if`, `while`, `do`, `for`, `foreach`, `catch`, `?:`, `??`, `??=`, `&&`, `||`, and pattern `and` or `or`. A switch statement or a switch expression adds 1 as a whole, whatever its sections, arms, `when` guards and patterns. The code inside a section or an arm still counts.
- The SLOC of a function is the number of its lines that hold code, not blank lines or lines with only comments.
- The mass of a function is CC times the square root of SLOC. The erosion is the mass of the functions with CC above 10, divided by the mass of all functions. It goes from 0 to 1, and less is better.

A function is a method, a constructor, a destructor, an operator, an accessor with a body, an expression-bodied property or indexer, or a local function. A lambda counts toward the function that holds it. A local function is its own function.

The program prints JSON by default, for the tools that read it, and text with `--text`. The JSON holds `erosion`, `totalMass`, `highCcMass`, `functionCount`, `highCcFunctionCount`, `sloc` (every line of code in `src/`, inside functions or not), `fileCount`, and `topFunctions`: the 10 functions of the greatest mass, each with `file`, `line`, `name`, `cc`, `sloc` and `mass`. The exit code is 0 after a report and 2 for wrong arguments or a root without a `src` folder.

A run of the built program takes about 0.5 s for all of `src/` (268 files). `dotnet run --no-build` adds about 0.5 s, and `dotnet run` with its check of the build adds about 0.9 s.

## Docs site

`tools/docs.sh` builds a website from the doc comments and the Markdown docs with DocFX. DocFX is a .NET local tool, pinned in `.config/dotnet-tools.json`. The nixpkgs build of DocFX cannot start on macOS, because it lacks the ASP.NET runtime.

```bash
nix develop --command bash -c 'bash tools/docs.sh -s'
```

The script builds the solution in Release, because DocFX reads the compiled assemblies and the XML documentation files beside them. Thus DocFX never parses C# 15 syntax itself. Then it writes the site to `artifacts/docs/`. The site holds the API reference of every module, private members included, and README.md, ARCHITECTURE.md, GUIDE.md, this file and the design docs. The source files are in the site too, so the links in GUIDE.md open them. `docfx.json` at the repository root holds the configuration, and `docs/site/` holds the navigation. The options:

- `-s` serves the site at `http://localhost:8080` after the build. Stop it with Ctrl-C.
- `-p <port>` gives another port for `-s`.
- `-n` skips the Release build and uses the assemblies that are there.

The build prints a warning for each link that has no page in the site. For example, a link to a folder such as `src` works on the git host, but the site has no page for a folder.

## Git hooks

The folder `.githooks/` holds two git hooks. At its start, the dev shell sets `core.hooksPath` to that folder. Thus the hooks run after you enter the dev shell once. Outside the dev shell, a hook runs itself again inside it. To skip a hook once, add `--no-verify` to `git commit` or `git push`.

### Before a commit

The pre-commit hook runs on every commit that stages C# files or projects. A commit with no such file, such as a ticket note, skips it. The hook changes no file. It does three things:

1. It builds the projects that the commit changes and the test projects that test them, incrementally. The build runs every analyzer, and an error stops the commit.
2. It prints a report for the changed lines only: the duplicated blocks (jscpd) and the methods over the complexity limit (CA1502) or the length limit (MA0051). Test code gets the length limit only. If the report names anything, it ends with a prompt to refactor that code in the same commit. The report never stops the commit.
3. It runs the tests of the changed code in Checked, one process per test project, in parallel. A failed test stops the commit.

The tests of the changed code are:

| Change | Tests |
|---|---|
| A file in `src/Tertius.X` | Every test in `tests/Tertius.X.Tests` |
| A file in `src/Tertius.Demo` | Every test in `tests/Tertius.Host.Tests` |
| A file in `analyzers/Tertius.Unions` | Every test in `tests/Tertius.Unions.Tests` |
| A file in `analyzers/Tertius.Style` | Every test in `tests/Tertius.Style.Tests` |
| A file in `tools/Tertius.Slop` | Every test in `tests/Tertius.Slop.Tests` |
| A file in `tests/Tertius.X.Tests` | The test classes in that file |
| A file in `tests/Tertius.Testing` or `benchmarks/` | None. The hook only builds it. |

The hook does not build or test the projects downstream of a change. Run `tools/test-all.sh` before a merge into main for that.

If a staged file also holds unstaged changes, the hook says so. The build and the tests see the working tree, not only the staged changes.

A changed line is a line that the staged diff adds or edits. Each compile writes its diagnostics to `obj/<configuration>/<framework>/analyzers.sarif`, and the report reads the complexity and length results from there. The limits are in `analyzers/CodeMetricsConfig.txt` (complexity) and `.editorconfig` (MA0051).

### Before a push

The pre-push hook does two things:

1. It runs `tools/cleanup.sh` on the C# files that the pushed commits change. If the cleanup changes a file, the hook builds the solution and commits the result as "Clean up code before push". Then it stops the push, because git chose the commits to push before the hook ran. Push again to send the cleanup commit too.
2. It builds the docs site with `tools/docs.sh`. A DocFX error stops the push, and a link warning does not.

If a file that the cleanup must clean holds uncommitted changes, the hook stops before the cleanup. Commit or stash those changes, then push again. The cleanup can add a commit only to the branch that is checked out. For another pushed branch, it prints a note and skips that branch.

## Code cleanup

`tools/cleanup.sh` formats and modernizes C# files in two passes:

1. `dotnet format style` applies Roslyn's modernization fixes, such as collection expressions, primary constructors, pattern matching and static lambdas. `.editorconfig` lists them as the IDE rules at suggestion level, and the script reads the list from there.
2. `jb cleanupcode` runs ReSharper's cleanup with the Tertius profile: syntax style, redundancies, member layout and formatting. It runs last, so its formatting wins.

Run it in the dev shell:

```bash
bash tools/cleanup.sh
```

With no argument, it cleans the C# files that differ from main, committed or not. `--all` cleans every C# file, which takes about 10 minutes. A list of files cleans only those files. ReSharper loads a solution with only the projects of the files, which takes 10 to 20 seconds.

The cleanup reads its rules from two files. `.editorconfig` holds the Roslyn rules, the syntax style, the line length of 120 characters and the doc comment rules. `Tertius.slnx.DotSettings` holds the ReSharper profile and the member layout: members grouped by kind (constants, static fields and properties, instance fields, constructors, instance properties, everything else), with no sort inside a group. Rider reads both files, so its own cleanup with the Tertius profile gives the same result.

The cleanup of the whole codebase is one commit. `.git-blame-ignore-revs` names it. Run this command once, so that `git blame` skips that commit:

```bash
git config blame.ignoreRevsFile .git-blame-ignore-revs
```

## Named arguments

The style analyzer reports a call whose target takes more than 3 parameters and has an argument without a name (`TS0002`, ARCHITECTURE.md, "Style analyzer"). Its code fix names the arguments. To fix every such call of the solution at once, build the analyzer and the union generator in Debug, which `dotnet format` loads, then run the fix:

```bash
nix develop --command dotnet build Tertius.slnx -c Debug
nix develop --command dotnet format analyzers Tertius.slnx --diagnostics TS0002 --severity warn
```

Three more rules of the style analyzer have code fixes that `dotnet format` applies the same way: `TS0004` writes a property pattern for a positional one, `TS0005` marks a default interface method for inlining, and `TS0006` seals a class. Name the rules after `--diagnostics`, for example `--diagnostics TS0004 TS0006`. `TS0005` and `TS0006` report at the end of a compilation, which `dotnet format analyzers` runs too.

`dotnet format` reads the projects in Debug, so it does not see the code behind `#if !TERTIUS_CONTRACTS`. For that code, build in Release and run the fix again with the configuration in the environment: `Configuration=Release nix develop --command dotnet format analyzers Tertius.slnx --diagnostics TS0002 --severity warn`. Without the union generator's Debug build, the calls that take a generated union do not bind, and the fix skips them.

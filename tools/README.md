# Tools

These helpers run every test project in one command, start JetBrains Rider inside the dev shell, run code coverage and Stryker.NET mutation testing on the lines that a branch changed, report duplicated code, and build the docs site. For the rules on which helper to run, read the Testing section of CLAUDE.md. The scripts are tool code: they never ship, so they skip polish.

Run every command in the dev shell, from the repository root. The build copies the Jolt library and the Vulkan loader next to each output, so the tests need no `DYLD_*` variable. Outside the dev shell, the GPU tests cannot find `libvulkan`, and the scripts stop with a message. Write the output to a folder outside the repository, for example `/tmp/tertius-coverage`.

## Every test project in one command

`tools/test-all.sh` builds the solution once per build configuration. Then it runs every test project in that configuration with `--no-build`. It works from any folder in the repository:

```bash
nix develop --command bash -c 'bash tools/test-all.sh'
```

For each configuration and module, the script prints one line with the counts, and one indented line for each failed test:

```
Debug Core: total 512, failed 1, succeeded 511, skipped 0, exit code 2
    failed: Tertius.Core.Tests.FrameTreeTests.Some_test
```

The options:

- `-c Debug,Release` gives the build configurations, separated by commas. The default is both.
- `-m Core,Physics` gives the test modules, separated by commas. The default is all five: Core, Simulation, Physics, Rendering and Host.
- `-o <folder>` gives the folder for the logs. The default is a new temporary folder, and the first line of the output names it. The script writes `<folder>/<configuration>/build.log` and one `<folder>/<configuration>/<module>.log` with the full test output of each module.
- Everything after `--` goes to each `dotnet test` command unchanged, for example a filter:

  ```bash
  nix develop --command bash -c 'bash tools/test-all.sh -c Debug -m Core,Physics -- --filter-class "*FrameTree*"'
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

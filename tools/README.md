# Tools

These helpers run every test project in one command, start JetBrains Rider inside the dev shell, run code coverage and Stryker.NET mutation testing on the lines that a branch changed, report duplicated code, measure the sloppiness of the code, build the docs site, trace the GPU passes of a demo run, build scripted models in Blender, and clean up the code. The git hooks in `.githooks/` call some of them. For the rules on which helper to run, read the Testing section of CLAUDE.md. The scripts are tool code: they never ship, so they skip polish.

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

`tools/Tertius.Slop` is a console program that measures how sloppy the code in `src/` is, after the post [Measuring code sloppiness](https://earendil.com/posts/measuring-code-sloppiness/). Erosion and verbosity read syntax. Type coherence uses a Roslyn source compilation for symbol binding, without building or emitting the engine. `docs/plans/code-quality-tooling.md` has the rules and their reasons: decision 16 for the erosion and decision 17 for the verbosity.

```bash
nix develop --command dotnet run --project tools/Tertius.Slop -c Release -- erosion
nix develop --command dotnet run --project tools/Tertius.Slop -c Release -- verbosity --text
nix develop --command dotnet run --project tools/Tertius.Slop -c Release -- all /path/to/repository
```

The command line is `Tertius.Slop <metric> [root] [--text]`. The metric is a subcommand: `erosion`, `verbosity`, or `all` for all metrics from one parse of `src/`. The commit hooks run `all`. The root is the repository, the current folder by default. The program reads every `.cs` file under `<root>/src`, but not the `obj` and `bin` folders, a file whose name ends in `.g.cs`, or a file that starts with an `<auto-generated` comment. It also reads the `.cs` and `.json` files under `<root>/tests`, `<root>/benchmarks` and `<root>/scripts` as plain text, but not their `obj` and `bin` folders. It measures none of them. It only collects their names, because some rules of the verbosity ask whether code outside `src/` uses a name.

### Erosion

- The cyclomatic complexity (CC) of a function is 1, plus 1 for each `if`, `while`, `do`, `for`, `foreach`, `catch`, `?:`, `??`, `??=`, `&&`, `||`, and pattern `and` or `or`. A switch statement or a switch expression adds 1 as a whole, whatever its sections, arms, `when` guards and patterns. The code inside a section or an arm still counts.
- The SLOC of a function is the number of its lines that hold code, not blank lines or lines with only comments.
- The mass of a function is CC times the square root of SLOC. The erosion is the mass of the functions with CC above 10, divided by the mass of all functions. It goes from 0 to 1, and less is better.

A function is a method, a constructor, a destructor, an operator, an accessor with a body, an expression-bodied property or indexer, or a local function. A lambda counts toward the function that holds it. A local function is its own function.

The JSON of `erosion` holds `complexityLimit` (10), `erosion`, `totalMass`, `highCcMass`, `functionCount`, `highCcFunctionCount`, `sloc` (every line of code in `src/`, inside functions or not), `fileCount`, `topFunctions` and `highCcFunctions`. `topFunctions` holds the 10 functions of the greatest mass. `highCcFunctions` holds every function with CC above 10, the greatest mass first. Each function has `file`, `line` (the first line after its attributes), `endLine` (the line of its closing brace or semicolon), `name`, `cc`, `sloc` and `mass`. Functions of equal mass are ordered by file, then by line.

### Verbosity

The verbosity is the number of lines that a rule flags or that jscpd finds in a clone, divided by the SLOC of `src/`. A line counts once, however many findings and clones cover it. A blank line never counts. It goes from 0 to 1, and less is better.

- A clone line is a line of either copy of a clone that jscpd finds in `src/`, with the settings `--format csharp --min-tokens 70 --mode mild`. The program runs the `jscpd` of the search path, which the dev shell has. Without it, `verbosity` and `all` print a message and exit with code 2.
- A flagged line is a line that a finding covers. A rule reads the syntax of one node and flags a span of the source. A finding covers every line of its span, not only the first line. The whole span is the code that the rewrite removes or replaces. Five rules flag lines without code: a comment that repeats the code under it, a doc comment that repeats a name, commented-out code, a TODO without a ticket, and a `#region`. Those lines count too.
- There are 48 rules: the C# forms of the slop rules of [scb-check](https://github.com/gabeorlanski/scb-check), and 20 rules of the user's. Each rule has an id, a message that says what to write instead, and its source. Decision 17 lists them, and the rules that the program leaves out, with the reasons.
- Five rules ask where a name is used: low-use-short-function, constant-argument, unused-optional-parameter, single-implementation-interface and unused-code. They read the uses in the syntax of `src/`, and the names in the files outside `src/` as text. A name is matched by its text, so two members with one name share their uses. Thus such a rule flags less, never more.

The JSON of `verbosity` holds `verbosity`, `verboseLines`, `flaggedLines`, `cloneLines`, `sloc`, `fileCount`, `rules`, `findings`, `clones` and `topFindings`. `rules` holds every rule, ordered by id. Each one has its `id`, `message` and `source`, the number of its `findings`, and the number of `lines` that they cover. `findings` holds every finding, ordered by file and then in the order of the source, each with `rule`, `file`, `line` and `endLine`. `clones` holds every clone, each with its `first` and `second` copy, and each copy with `file`, `line` and `endLine`. The clones are ordered by the file and line of the first copy, then of the second. `topFindings` holds the 20 findings of `findings` that cover the most lines. The JSON of `all` holds `erosion`, `verbosity` and `classMetrics` (see "Type coherence and source evidence").

Every `file` is the path in the repository, such as `src/Tertius.Core/World.cs`, so that the commit report can match it with the lines of the staged diff.

### History

The git hooks keep the metrics of each commit in a git note under `refs/notes/slop` ("Git hooks" below). A note is one line of JSON:

```json
{"erosion":0.20000179206800917,"verbosity":0.03323593209400714,"highCcMass":8261.413478927696,"highCcFunctionCount":73,"verboseLines":1854,"sloc":55783,"branch":"main","source":"pre-push","measured":"2026-10-27T18:47:21.215107+00:00"}
```

`highCcMass` is the sum of complexity times the square root of code lines for functions with cyclomatic complexity above 10. `highCcFunctionCount` counts those functions. `verboseLines` counts distinct lines flagged by a verbosity rule or clone. An unfilled older note lacks these three fields; readers leave its absolute count cells blank and show a new count without a change against it. The source is `pre-commit`, `pre-push` or `seed`. Five commands of the tool write and read the notes. `-C <folder>` before a command runs it in that repository.

- `measure <tree> --branch <name> --source <source> [--details <file> [--parent <commit>]]` prints the note of a tree or a commit. It measures a copy of the tree's `src`, so the working tree does not matter. The copy also holds the tree's `tests`, `benchmarks` and `scripts`, for the names that they use. With `--details`, it also writes a JSON object to the file: `report`, the JSON of `all` for the tree, and `baseline`, the commit that the report compares the tree with, as `commit` (the short hash) and `note`. The baseline is the nearest noted commit on the first-parent line of `--parent`, by the rule of `summary`, and null without `--parent` or when no commit up to it has a note. The pre-commit hook gives that file to the commit report, with `--parent HEAD` ("Before a commit" below). The note on the output stays one line.
- `note <from> <tip> --branch <name> --source <source>` attaches notes to the first-parent commits after `<from>` up to `<tip>`. It notes `<tip>`, and each commit that changes C# code under `src` and has no note.
- `history [git log options and revisions]` prints the noted commits as CSV, newest first. The first columns are commit, date, branch, tickets, erosion, highCcMass, highCcFunctionCount, verbosity, verboseLines and sloc. Type columns follow them (see "Type coherence and source evidence"). The tickets are the ticket ids in the commit message.
- `summary <from> <tip>` prints the summary of the pre-push hook.
- `backfill` fills missing absolute counts and the type-summary data in every older slop note, including notes of commits off the current branch. It keeps each note's branch, source, measurement time and other text. It preserves existing counts and unknown top-level JSON properties, enriches every readable JSON line, and skips notes that already have all counts and can resume after an interruption.

The trend line of main:

```bash
nix develop --command dotnet run --project tools/Tertius.Slop -c Release -- history --first-parent main
```

```text
commit,date,branch,tickets,erosion,highCcMass,highCcFunctionCount,verbosity,verboseLines,sloc
425971fcd8f38f4ac915f01ed399f0a3adbbd1d8,2026-10-27T14:46:56-04:00,main,,0.20000179206800917,8261.413478927696,73,0.03323593209400714,1854,55783
17b60932415e135b684414c70bc30d5eae840b04,2026-10-27T14:45:14-04:00,main,ter-tf17,0.20000179206800917,8261.413478927696,73,0.03323593209400714,1854,55783
aced093a70125d9216036e490e38b3b996153b8f,2026-10-27T11:31:26-04:00,main,ter-vtc7 ter-ylzk,0.20000179206800922,8261.413478927696,73,0.03323593209400714,1854,55783
```

To read the notes of another clone, fetch them first: `git fetch origin refs/notes/slop:refs/notes/slop`.

### Exit codes and run time

The exit code is 0 after a command. If git or jscpd fails, the exit code is 1. For wrong arguments, a root without a `src` folder, or a verbosity without jscpd, the exit code is 2.

The built program reads all of `src/` (268 files), and the names of the tests, the benchmarks and the scripts. On a quiet machine, before the rules of ticket `ter-a3ya`, a run took about 0.5 s for `erosion`, 0.9 s for `verbosity` and 1.2 s for `all`, of which jscpd took 0.04 s. On a busy machine (load 13 to 22) after that ticket, a run took 1.2 s for `erosion`, 1.7 s for `verbosity` and 2.8 s for `all`. The erosion did not change, so most of that rise is the load. `dotnet run --no-build` adds about 0.5 s, and `dotnet run` with its check of the build adds about 0.9 s.

## Docs site

`tools/docs.sh` builds a website from the doc comments and the Markdown docs with DocFX. DocFX is a .NET local tool, pinned in `.config/dotnet-tools.json`. The nixpkgs build of DocFX cannot start on macOS, because it lacks the ASP.NET runtime.

```bash
nix develop --command bash -c 'bash tools/docs.sh -s'
```

The script builds the solution in Release, because DocFX reads the compiled assemblies and the XML documentation files beside them. Thus DocFX never parses C# 15 syntax itself. Then it writes the site to `artifacts/docs/`. The site holds the API reference of every module, private members included, and README.md, ARCHITECTURE.md, GUIDE.md, this file and the design docs. The source files are in the site too, so the links in GUIDE.md open them. `docfx.json` at the repository root holds the configuration, and `docs/site/` holds the navigation. The options:

- `-s` serves the site at `http://localhost:8080` after the build. Stop it with Ctrl-C.
- `-p <port>` gives another port for `-s`.
- `-n` skips the Release build and uses the assemblies that are there.

No hook builds the site, so a DocFX error shows only when you run the script.

The build prints a warning for each link that has no page in the site. For example, a link to a folder such as `src` works on the git host, but the site has no page for a folder.

## Benchmarks

`benchmarks/Tertius.Benchmarks` holds the BenchmarkDotNet benchmarks. `Program.cs` gives every run one job: the short run, in the benchmark process, with the memory diagnoser. Do not add a `--job` option. It adds a second job outside the process, and that job fails on the preview runtime with "GetRuntimeVersion not implemented for NotRecognized".

Run one benchmark method with a filter, in Release:

```bash
nix develop --command dotnet run -c Release --project benchmarks/Tertius.Benchmarks -- --filter '*RenderSnapshotBenchmarks.Build*'
```

The filter matches the full name of the method, with wildcards. A filter that names only the class runs every method of the class with every parameter value.

## GPU trace

`tools/gpu-trace.sh` records a Metal System Trace of a headless demo run, and writes a table of each pass's GPU time and limiters. It runs only on macOS, with Xcode's `xctrace` (27.0 on the development Mac). No engine code depends on it: the GPU timings of a headless run (ARCHITECTURE.md, "GPU timings") work on every Vulkan driver, and this script adds why a pass takes its time.

```bash
nix develop --command bash -c 'bash tools/gpu-trace.sh --start 399 --frames 120 scripts/demo-exit.json'
```

The script builds the demo in Release, and traces the built binary, so the trace holds no build. It keeps the frames from `--start` (counted from 0 over the script's captures and film frames, default 0) for `--frames` frames (default 120, and 0 keeps every frame to the end of the script). The frames before `--start` become run steps of the same ticks, so the world reaches the same state without a render. Frame 399 is the first frame of demo-exit's deck camera. The size is 1920x1080 unless `--size` gives another. `--window FROM TO` adds the window row of the table (see below). `--paired PASS` and `--control PASS` add the paired table (see "The paired mode" below). Arguments after the script go to the demo's headless run, such as `--screen-space-light off` (ARCHITECTURE.md, "Host and scripted runs"). The output goes to `captures/gpu-trace/<script>-<start>`, which git ignores, or to `--out <dir>`:

- `run.trace`: the trace. Open it in Instruments to see the timeline and the counters.
- `run/`: the run's `gpu-timings.json` and `exposure.json`, and its printed lines in `run.log`. The run passes `--video none`, so it draws and times each frame as the cost recipe does (docs/plans/rendering-upgrade.md, "Measuring the cost of a feature") and writes no image.
- `passes.md`: the table, which the script also prints. `passes.json` holds every counter of every pass, and the summary of each pass's shared time.

The table has one row per pass, a row for the encoders before the first pass (the uploads and the exposure's readback copy), a row for those after the last pass, and the frame's span. For each, it gives the frames, the mean, median and 95th percentile of the busy time and of the time alone in milliseconds, the two highest limiters, and the read and write bandwidth in GB/s. A limiter is the share of the time in which one GPU unit was busy or stalled, such as the ALU, texture sampling or the last-level cache. The highest limiter is the unit that bounds the pass. The first line of `passes.md` gives the load averages at the start. Measure on a quiet machine: the counters sample the whole GPU, not one process.

How the script reads the trace:

- The renderer runs on MoltenVK, so each Vulkan pass shows as one or more Metal encoders. MoltenVK names the encoders after the Vulkan command, such as "vkCmdBeginRendering RenderEncoder", not after the pass. A headless run turns on no validation, so the renderer sets no debug labels.
- MoltenVK writes each timestamp of the renderer's GPU timer as an encoder of its own, "Record GPU Counter Sample BlitEncoder". The timer writes one before the first pass and one after each pass. So in a command buffer with one more such encoder than passes, the encoders between two of them belong to one pass. The pass names come from the run's `gpu-timings.json`, in recording order. The script names no pass in a command buffer of the encoder list with another number of stamps.
- A frame of the trace is a command buffer whose GPU intervals hold a timestamp encoder or an encoder of a pass. The renderer writes stamps in each frame's command buffer and in no other. A command buffer without stamps, such as an upload at the start, is not a frame. The script counts the frames from 0 in the order of their first GPU start.
- The trace can lose a frame's rows in its encoder list, or list a frame with another number of stamps. Then the script cannot name the frame's passes. The table of intervals still labels each interval with its encoder, so the script still finds the frame. Such a frame keeps its index and gives no values, and a line below the table names it. 6 of the 32 kept traces of ter-bgpv, ter-0y0z and ter-r5hb hold 1 to 5 such frames. Before ter-svcs, the script counted only the frames that it read. So each frame after a lost frame took the index of the frame before it (see "Reading the control" below).
- The table's first line counts the frames, also those that the script cannot read. It also counts the other command buffers: those of the encoder list that are not frames, such as an upload. Before ter-svcs, the first line counted only the frames that the script could read, and it counted a frame with another number of stamps as another command buffer.
- A pass's GPU time, its busy time, is the union of its encoders' intervals (table `metal-gpu-intervals`), so the overlap of its own vertex and fragment work counts once. The frame span row is the time from the start of the first pass's first encoder to the end of the last pass's last encoder, and it agrees with the GPU timer's span to about 1 %. The pass times do not agree with the timer's, and on this Mac each pass's own time is the trace's. The Apple GPU starts an encoder before the one before it ends, so the encoders of two passes can overlap, and the sum of the passes' times can be more than the span. The timer's pass times in `gpu-timings.json` hold only for the span and for the sum of a pass and the next one: a stamp's blit encoder starts beside the next pass's first encoder, which can hold it back by up to about 1 ms, so a pass's time can hold part of the next pass's work (ARCHITECTURE.md, "GPU timings"). On 120 frames of demo-exit's deck (2026-10-26), the occlusion upsample took 1.459 ms by the timer and 0.300 ms in the trace, and the lighting after it 8.770 ms and 9.906 ms, while their sums agree within 0.03 ms. The timer also counts the idle time before a pass and between its encoders, which the trace leaves out. So a profile of a pass reads the trace's time, and the frame's budget reads the timer's span. The table says so above its rows.
- A pass's time alone is the part of its busy time in which no other encoder that the table counts runs. The table counts the encoders of the passes and those before the first pass and after the last pass, in every frame. It does not count the timestamp encoders, the encoders of the other command buffers, or the encoders of a frame that the script cannot read, so the time alone can overlap them. The script cuts the timeline at every start and end of an interval, and adds each segment in which only the pass is open.
- The busy time minus the time alone is the time that the pass shares with other passes. In that time, the GPU runs the pass beside other work, so the busy time can be more than the pass costs the frame. The means in the table subtract: the mean busy time minus the mean time alone is the mean shared time. For example, in mars-air's view above (ter-0y0z), the haze pass had a mean busy time of 0.173 ms and a mean time alone of 0.034 ms, so it shared 0.139 ms. The exposure pass's histogram clear runs beside it. The medians and the 95th percentiles do not subtract, because each one comes from its own frame. `passes.json` gives them for the shared time of each frame, as `"shared"` beside `"alone"`: the haze's median shared time was 0.141 ms.
- `--window FROM TO` adds a row after the frame span, with the window of each frame in milliseconds. The window is the wall-clock GPU time from the end of pass FROM's last interval to the next start of pass TO. That start is the first start of an interval of TO at or after the end of FROM. It holds all the GPU work between the two passes and the idle time, so it is what the frame pays for that part. The frame span row and the window row put a wall-clock time in the busy columns. A frame in which FROM or TO has no GPU work, or in which TO starts only before FROM ends, has no window. A line below the table counts those frames.
- A pass between FROM and TO costs the frame the difference of the window with and without the pass. `--paired` gives that difference for one trace whose frames alternate (see "The paired mode" below).
- The counters come from the instrument "Metal GPU Counters", which records the counter set "Performance Limiters" headless (tables `gpu-counter-info` and `gpu-counter-value`). The script averages each counter over the samples that fall inside a pass's intervals. A sample comes about every 10 µs, so a pass of 0.05 ms has a few samples per frame, and its limiters are rough.
- The counter instrument also records Instruments' shader timeline, and both slow the GPU a little. So take the frame span and the timer's pass times from `gpu-timings.json` of a run without the trace, each pass's own busy time from a trace with `--no-counters`, and the limiters from a trace with counters. `--no-counters` records a smaller trace with times only.
- The trace counts every frame's command buffer, and the metering draws too: under `AutoExposure`, the first frame after the run steps that replace the frames before `--start`, and each still after a long step, draw once more to meter the exposure (ARCHITECTURE.md, "The metering draw"), and `gpu-timings.json` leaves those draws out. So the trace's frame count can be larger than the file's: 121 against 120 on demo-exit's deck. For a script of stills after long steps, half of the trace's frames are metering draws.
- The exported counters of 120 frames take more than a gigabyte of XML. A trace of 120 frames at 1080p takes about two minutes in all, and about 80 MB. The script deletes the exported tables after the report, unless `--keep-exports` is given.

What the trace does not give headless: the counters per draw or per dispatch, and the shader costs per line. In Instruments, open `run.trace`, select a frame's encoder on the GPU track, and read the counters and the shader timeline in the detail pane below it.

Give `--window` to `gpu-trace.sh` when you record. If a pass name is not in the run's `gpu-timings.json`, the report stops with exit code 1 after the recording. Then the trace and the exported tables stay in the output folder.

To read a window of a trace that you recorded before, run the report again with an output folder of its own, for example:

```bash
nix develop --command python3 -I tools/gpu-trace.py report captures/gpu-trace/demo-exit-399/run.trace captures/gpu-trace/demo-exit-399/run/gpu-timings.json captures/gpu-trace/demo-exit-399/window --window reflections bloom
```

The new `passes.md` has no line with the load averages, because only `gpu-trace.sh` writes that line. A new folder keeps the first `passes.md` with its load line. The report exports the trace's tables again into the new folder and keeps them there. For a trace with counters, that export takes more than a gigabyte. Delete the `export` folder after the report.

### The paired mode

The paired mode measures what one pass costs each pass of the frame, and the frame. Use it for a cost that is smaller than the drift between two runs. On a loaded machine, separate runs drift by about 13 % in every pass (ter-0y0z). So two runs with and without a pass resolve only a cost larger than that drift. The paired mode compares frames of one trace, so a slow change of the GPU clock or of the load cancels.

The trace needs frames that alternate with and without the pass. Make them with a temporary probe in the renderer that skips the pass on every other frame, and keep the probe out of the commits. ter-bgpv's probe dropped the atmosphere record, and ter-0y0z's probe returned from `HazePass.Record` on every other call. Then record the trace and give the pass's name to `--paired`:

```bash
nix develop --command bash -c 'bash tools/gpu-trace.sh --no-counters --start 20 --frames 200 --window reflections bloom --paired haze <script.json>'
```

`--paired PASS` adds a second table to `passes.md`, and an entry `"paired"` to `passes.json`. The method:

1. The report orders the frames by their first GPU start and counts them from 0. A frame whose passes the report cannot read keeps its index and is in no pair.
2. A frame in which PASS has GPU work is a frame with the pass. A frame in which PASS has no GPU work is a frame without it.
3. Each frame without the pass whose two neighbors both have it makes one pair. The pair's difference is the mean of the two neighbors minus the frame, so a positive value is time that the pass adds.
4. For each pass, the report computes the difference of the busy time and of the time alone. A pass with no GPU work in a frame takes 0 ms there. So the row of PASS itself gives its own busy time and time alone.
5. The frame span row gives the difference of the frame span. With `--window FROM TO`, the window row gives the difference of the window. A pair is left out of the window row when one of its three frames has no window.
6. Each row gives the median of its differences over the pairs, the paired median, in milliseconds. A row of at least 30 pairs also gives the moving-block bootstrap standard error of that median in milliseconds.

Neighboring pairs share a frame. In an alternating trace, the pairs at frames 1 and 3 both use frame 2. So the differences of neighboring pairs correlate, and a bootstrap of single differences gives standard errors that are too small. The report uses a moving-block bootstrap instead. A block is a run of 6 consecutive differences in the order of the trace. Each resample joins blocks with random starts, drawn with replacement, until it holds as many differences as there are pairs, and takes their median. The report does this 2000 times. The standard error is the standard deviation of those 2000 medians.

The draws use the fixed seed 1, so two reports of one trace give the same errors. A row of fewer than 30 pairs, 5 blocks, gives no standard error, because the bootstrap of so few blocks gives one that is much too small. The text above the table and `passes.json` (`"resamples"`, `"blockPairs"`, `"seed"`, `"minErrorPairs"`) state the resample count, the block length, the seed and that minimum.

A block keeps the correlation of the pairs inside it. `tools/test_gpu_trace.py` compares the errors with the real standard error of the paired median, on synthetic traces of 100 pairs with independent frame noise. The result depends on the kind of row:

- In the rows of the other passes, every frame has noise, and neighboring differences correlate by 1/6. The block errors average 0.98 times the real error, and single differences 0.93 times.
- In the row of PASS itself, the frames without the pass have no time and no noise, and neighboring differences correlate by 1/2. The block errors average 0.96 times the real error, and single differences 0.79 times.

A pair that a row leaves out leaves a gap, and a block can hold the differences on its two sides as they are.

Two limits remain, so read the standard errors beside the control's medians, which show the real noise floor:

- A drift that correlates pairs farther apart than a block still makes the errors too small. In mars-air's view above (ter-0y0z), the exposure pass's busy differences correlate by 0.58, 0.29, 0.36, 0.28 and 0.19 at 1 to 5 pairs apart. Their error grows from 0.0097 ms with blocks of 6 pairs to 0.0131 ms with blocks of 20.
- A pass whose work follows a cycle of frames gives an error that depends on the block length. In ter-0y0z's traces, the busy time of the environment faces follows a cycle of 12 frames, 6 pairs. In the control, their error falls from 0.0040 ms with single pairs to 0.0015 ms with blocks of 6. Such a cycle also gives the row's median an offset (see "Reading the control" below).

How to read the paired table:

- A paired median is the typical cost of the pass in one frame. Read it with its standard error: a median smaller than about two standard errors is not resolved.
- The paired median is the median of the differences, not the difference of two medians. Medians do not subtract: the median busy time of the frames with the pass minus that of the frames without it can differ from the paired median.
- The busy column of PASS gives the pass's own busy time. The busy columns of the other passes show how the pass changes them. For example, take mars-air's view above (ter-0y0z). The haze pass's paired median was +0.162 ms, and the exposure pass's was +0.102 ms (standard error 0.010 ms). The exposure pass grows because its histogram clear runs beside the haze pass.
- The alone columns show what the pass costs the other passes outside their shared time. In the same trace, the exposure pass's time alone fell by 0.022 ms in the frames with the haze.
- The window row is the cost that the frame pays between FROM and TO. In the same trace, the window from the reflections to the bloom grew by 0.163 ms (standard error 0.006 ms).
- The frame span row has a large standard error, 0.011 to 0.076 ms in ter-0y0z's traces. For a small cost, read the window or the passes.

The text above the table says whether the frames without the pass are the odd frames or the even frames.

A line below the table names each frame without the pass that makes no pair, by its index from 0. Two kinds of frames without the pass make no pair: a frame at an end of the trace, and a frame that has a neighbor without the pass. A frame with the pass is never named, even when no pair uses it. A second line names the pairs that the window row leaves out, by their middle frame. A line with more than 10 frames gives their count and the first 10 frames, and `passes.json` lists all of them.

The control measures the noise of the method. Record a trace with the same script and no probe, so that every frame runs the pass. Give the pass's name to `--control` instead of `--paired`. The control pairs each odd frame with its two even neighbors. A frame without GPU work of the pass pairs with none, so the control compares only frames that all run the pass. The control's line below the table names each odd frame that makes no pair and each frame without GPU work of the pass. In an alternating trace whose first frame has the pass, the frames without the pass are the odd frames. So the control compares the same slots of the frame order. Its medians give the noise floor. If a row's paired median in the alternating trace is not clearly larger than the control's median of that row, the row shows no cost. In ter-0y0z's control, the window's median was -0.001 ms (standard error 0.003 ms) and the haze pass's busy median was -0.000 ms. A cost smaller than the control's median is not resolved.

#### Reading the control

A control and its alternating trace must pair the same frames. ter-0y0z's control lost the encoders of frame 21. The report then counted only the frames that it read. So from frame 21 on, its odd frames were the trace's even frames. Thus 89 of its 99 pairs used the frames opposite to those of the alternating traces. That report gave the window a median of +0.0077 ms (standard error 0.0027 ms), and the environment faces +0.0040 ms (0.0014 ms). Now the lost frame keeps its index, and the window's median is -0.0008 ms (0.0028 ms). Three new controls of ter-svcs of the same view, c1, c1-shift (with `--start 21`) and c-fixedface, gave -0.0027 (0.0032), +0.0003 (0.0011) and +0.0041 (0.0022) ms. c-fixedface held the face schedule fixed, as below. So in the four controls, the pairs of the odd frames give a window within 2 standard errors of 0.

But a run can carry a difference between the window of its odd frames and that of its even frames. That difference changes from run to run. The median of the signed second difference measures it: for each frame, the mean of its two neighbors minus the frame, with the sign that makes a longer odd window positive. With blocks of 12 values, it is +0.0055 (0.0021) ms in ter-0y0z's control, and +0.0015 (0.0037), -0.0004 (0.0012) and -0.0045 (0.0023) ms in the three new controls. In ter-0y0z's control, the median window of the odd frames is 0.3289 ms, and that of the even frames is 0.3197 ms. Its pairs of the even frames give +0.0082 (0.0022) ms, and +0.0080 and +0.0085 ms in the two halves of the trace. So the shifted order did not make that difference. It only made the control pair the even frames, which showed it. The cause of the difference is not established, and ter-ku9c tests the frame slot. So an odd-even difference of about 0.005 ms, of either sign, can enter the window row of an alternating trace, and a control from another run does not show it.

The row of the environment faces has an offset that the face schedule gives. Take one face per frame and M maps. Then the frame of number n draws slot n mod 6M of the schedule (ARCHITECTURE.md, "Which faces a frame draws"). So the faces' work follows a cycle of 6M frames: 12 in mars-air, 6 in scripts/reflections.json. The cycle has an even length, so the odd frames always draw the same faces. Then the paired differences of the faces row take one value for each odd phase of the cycle, plus noise. When the faces take different times, their median is not 0. The frame number counts every drawn frame from the start of the run, and the trace holds every drawn frame. So every trace of one script draws the same faces on its odd frames, also with another `--start`.

A control of ter-svcs shows the cycle. It held the schedule fixed with a temporary probe: `EnvironmentMap.FacesFor` passed a constant frame number to `EnvironmentFaces.InTurn`, so every frame drew the same face. The table compares it with a control of the same view and the real schedule. Both are controls of mars-air's view above. The faces row gives the median and, in parentheses, the standard error, in ms. A phase is the frame's index mod 12.

| Control | Faces, pairs of the odd frames | Faces, pairs of the even frames | Faces' error for blocks of 1, 6 and 20 pairs | Faces' busy median by phase |
|---|---|---|---|---|
| c1, the real schedule | -0.0013 (0.0015) | +0.0030 (0.0006) | 0.0038, 0.0015, 0.0019 | 0.197 to 0.221 ms at phases 6, 9, 10 and 11, 0.028 to 0.037 ms at the others |
| c-fixedface, the schedule held fixed | +0.0006 (0.0009) | -0.0007 (0.0009) | 0.0008, 0.0009, 0.0013 | 0.222 to 0.226 ms at every phase |

With the real schedule, four phases of the cycle draw a costly face. The error then falls with the block length, and the pairs of the even frames lie 5 standard errors from 0. With the schedule held fixed, every phase takes the same time, the error no longer falls with the block length, and both medians lie within 1 standard error of 0. So the faces' offset comes from the schedule.

The offset differs between scripts and views, so read the faces row against the faces row of a control of the same script and view, not against 0. In mars-air's view above, the alternating trace gave the faces -0.0035 (0.0011) ms. ter-0y0z's control gave -0.0021 (0.0015) ms, and c1 gave -0.0013 (0.0015) ms. The differences, -0.0014 and -0.0022 ms, lie within 1.2 standard errors of 0. So these traces show no change of the faces by the haze pass. mars-air's day view has no control. Its alternating trace gave the faces -0.0065 (0.0025) ms, and nothing shows how much of that the cycle gives. In ter-r5hb's control of the front view of scripts/reflections.json, the faces row is -0.0391 (0.0022) ms on the odd frames and +0.0212 (0.0007) ms on the even frames. That script has a cycle of 6 frames. One phase of the cycle takes 0.034 ms, and the others take 0.122 to 0.160 ms. For each odd phase, the mean of the two phase medians beside it minus its own phase median predicts the offset of the pairs at that phase. The median of these three predictions is -0.046 ms, near the measured -0.039 ms. No trace of that script held the schedule fixed.

Do not remove the cycle by subtraction. A scratch script of ter-svcs subtracted from each frame the median of the frames at the same phase of the cycle. The cycle has an even length, so all the frames of one phase have the same parity. So that subtraction also removes every difference between the odd and the even frames, such as the cost of a pass that runs on every other frame. Its result is near 0 by construction and shows no cause.

If the work of another row depends on the face that the frame draws, that row can carry the cycle too. ter-u102 tests this for the frame span. To measure the faces row without the cycle, record a control and an alternating trace with the probe above, and keep the probe out of the commits.

`--paired` and `--control` work on a trace recorded before, as `--window` does:

```bash
nix develop --command python3 -I tools/gpu-trace.py report <folder>/run.trace <folder>/run/gpu-timings.json <folder>/paired --paired haze --window reflections bloom
```

### Tests

`tools/test_gpu_trace.py` tests the functions of `tools/gpu-trace.py` on synthetic intervals: the busy time, the time alone, the window and the paired mode with its control and its bootstrap. It also reads small synthetic tables of encoders and of GPU intervals from temporary files, for the frames and the counts of the table's first line. It uses the Python standard library only, so it needs no trace and no Mac. Run it in the dev shell:

```bash
nix develop --command python3 -I tools/test_gpu_trace.py
```

## Scripted models in Blender

A scripted model is a model whose only source is a folder of Blender Python scripts, with a name that ends in `.model`. The `.blend` file, the `.glb` file, the baked textures and the views are build outputs. The design is in `docs/plans/asset-pipeline.md`, "Scripted models". The project skill `.claude/skills/procedural-modeling/` teaches the way of work, with the Nostromo, `content/tertius.demo/assets/ships/nostromo.model/`, as its worked example. `tools/blender/` holds the tools:

| Path | What it is |
|---|---|
| `tertius_blender/` | The model library: the Python package that model scripts import. `run` runs scripts. `geometry` and `nodes` build meshes and shader node trees. `checks` holds the attachment check and the view check. `bake` and `export` make the maps and the files. `stage` adds the cameras and lights of the views. `mpfb` makes humans with MPFB (see "Humans from MPFB"). |
| `build.sh`, `build.py` | The build tool. It builds one model folder in headless Blender, with no MCP server. |
| `bridge.sh` | Starts and stops the bridge: a headless Blender that serves the MCP server of `.mcp.json` |
| `tests/` | The example model `crate.model`, the tests of the build tool, and the tests of `mpfb` |

All of them need the assets shell, `nix develop .#assets`, which adds Blender 5.2.2 and `blender-mcp` 1.0.3. Outside it, the scripts run themselves again inside it. The shell also gives:

| Variable | What it names |
|---|---|
| `BLENDER_SYSTEM_EXTENSIONS` | A folder in the Nix store with the MCP add-on and MPFB as Blender system extensions (`native/blender-extensions.nix`). Every Blender of the shell finds them, and nothing is installed into your Blender folders. |
| `TERTIUS_MPFB` | The MPFB 2.0.18 add-on, unpacked from a fetch pinned by hash in `native/mpfb.nix` (44 MiB to download) |
| `TERTIUS_MAKEHUMAN_ASSETS` | The skins, eyes, eyebrows, eyelashes, teeth, tongues and hair of the MakeHuman system assets, from a fetch pinned by hash in `native/makehuman-assets.nix` (268 MiB to download, 139 MiB kept). The clothes of the pack are left out. |

The first start of the shell downloads about 870 MiB, which take about 2.1 GiB in the Nix store, the downloaded archives included (measured on 2026-10-26).

### Build a model

```bash
tools/blender/build.sh content/tertius.demo/assets/ships/nostromo.model --out /tmp/nostromo-build --views
```

The build starts from an empty scene and runs the scripts that `model.json` names, in order. Then it bakes the objects of `bake.objects`, saves `model.blend`, and with `--views` renders each shot into `views/<name>.png`. A second Blender with one thread opens `model.blend` and exports `model.glb`: with more threads, the tangents differ in their last bits between builds. It prints `build.json`, the report, and also writes it into the output folder. Blender's own output goes to `blender.log` there.

- `--out` is required. The folder must be new, empty, or the output of an earlier build, which the build replaces. A folder with other files stops the build with exit code 2, so a wrong path never loses files. Put the outputs in a scratch folder, not in `bin/assets/`: the importer owns that folder.
- `--device cpu` bakes and renders on the CPU. With the default, `auto`, the build uses the GPU that Cycles finds, such as Metal on this Mac. Without one, it uses the CPU. On Linux, the Blender of nixpkgs has no CUDA or OptiX, so it uses the CPU. Two builds with `--device cpu` give the same `.glb` bytes. A bake on Metal differs in a few pixels, so the importer bakes on the CPU.
- The first bake on Metal in a new Blender compiles the GPU kernels, and that takes about three minutes. Later bakes start at once.
- Exit codes: 0 built, 2 bad input, 3 the build failed, 5 Blender is missing, 6 less free space than the build needs, 130 SIGINT or SIGTERM stopped the build. The build needs 1 GB plus the space of its baked maps and of Blender's temporary files while it bakes (`tools/blender/needed_mb.py`): 3 GB for the Nostromo. Bad input is a wrong argument, a broken `model.json`, a missing script or an output folder with other files. After a failed build, `build.json` names the step and the script.

The build tool stops Blender on every exit and deletes Blender's temporary folder. Blender ignores Ctrl+C, so do not start Blender for a build by hand.

### Model through the MCP server

`.mcp.json` registers the official Blender Lab MCP server as the server `blender`. Claude Code starts it with `nix develop .#assets --command blender-mcp`, and asks once to approve the servers of the project. The server sends each tool call to the bridge.

1. Start the bridge: `tools/blender/bridge.sh start`. It listens on `localhost:9876` and writes its log to `~/.local/state/tertius/blender-bridge-9876/bridge.log`. Its Blender runs with `--factory-startup` and enables the MCP add-on from the system extensions. Its temporary folder and its user resource folder are in that state folder too, and the stop deletes them. One bridge serves the machine, and it belongs to the checkout or worktree that started it. A start or a stop from another checkout fails with exit code 1 and names the owner. `bridge.sh status` prints the process id and the owner.
2. Run model scripts through the tool `execute_blender_code`, with the same function as the build tool:

   ```python
   import importlib, sys
   sys.path.insert(0, "<repository>/tools/blender")
   from tertius_blender import run
   importlib.reload(run)

   run.new_scene()
   result = run.run_script("<repository>/content/tertius.demo/assets/ships/nostromo.model", "s10_hull.py")
   ```

   `<repository>` is the absolute path of the checkout. `run_script` reads `model.json` again and reloads the other modules of the model library on each call, and the snippet reloads `run`, so an edit applies at once. `run_script` raises an error when the bridge runs the library of another checkout. The code of a tool call runs in a new namespace each time, but `sys.path` and the scene stay.
3. To look at the scene, add the stage and pick a shot, then call a render tool:

   ```python
   from tertius_blender import run, stage
   views = run.load_model(MODEL)["views"]
   stage.build(views)
   stage.use_shot(views["shots"][0])
   ```

   `stage.build` sets Cycles to the GPU and saves the render settings that it changes. `render_thumbnail_to_path` makes a fast, small image, and `render_viewport_to_path` uses the render settings of the stage. The MCP server waits at most 300 s for one tool call. Both write into the temporary folder of the bridge and return the path. Open that path with the Read tool before you stop the bridge, because the stop deletes the folder. Call `stage.remove()` before you run model scripts again. It restores the render settings, and a camera in the scene fails the build.
4. At the end of the work, stop the bridge: `tools/blender/bridge.sh stop`. A running bridge holds memory, and other agents wait for Blender to quit before they time GPU work.

The render tools need a camera, and only the stage adds one. The other tools of the server, such as `get_objects_summary` and `search_api_docs`, need no camera.

### Rules for model scripts

- Use `model.rng` for every random choice. It is seeded from the `seed` of `model.json` and the script name. The build also seeds the `random` module the same way before each script, as a guard.
- Never iterate over a `set` of Blender objects or BMesh elements: its order follows memory addresses. `dict.fromkeys(...)` keeps a fixed order.
- Build meshes with `geometry.MB` and end each with `MB.finish`. It puts the vertices and faces into an order of their coordinates, because `create_uvsphere`, `spin` and the weld order them by memory address.
- Write no files. The bake, the save and the export belong to the build tool. MPFB writes logs, but only into the temporary user resource folder that the build tool and the bridge give Blender.
- Build only the model, in meters, with +Z up and the front toward -Y. Add no cameras and no lights for the views.
- Mark a piece below the root with the custom property `tertius_piece = 1`.
- Set `result` to a dictionary to report numbers. `build.json` keeps the `result` of each script.

### Humans from MPFB

A model of a person makes the person with MPFB's own code, through `tertius_blender.mpfb`. The design and the rules are in `docs/plans/asset-pipeline.md`, "Humans from MPFB". The astronaut (`content/tertius.demo/assets/characters/astronaut.model/`) and the crew member (`crew.model/` next to it) are the examples, and the skill `procedural-modeling` teaches the way of work.

```python
from tertius_blender import mpfb

body = mpfb.human(model.params["human"], "Crew")
mpfb.pose(body.parent, model.params["pose"])
joints = mpfb.joints(body)
parts = mpfb.freeze(body)
```

- `mpfb.human` takes MPFB's own human description, as MPFB saves it in a human preset `.json` file: `phenotype` (the macro sliders), `targets` (the detail targets), `rig`, `skin_mhmat`, `eyes`, `eyebrows`, `eyelashes`, `teeth`, `tongue`, `hair` and `alternative_materials` (another eye color). An asset is named by its folder and file in the asset pack, such as `afro01/afro01.mhclo`. An asset or a detail target that MPFB does not hold by that name stops the build with an error that names it, and so does a key that MPFB's presets lack. The skin and eye material types are `MAKESKIN`, the only kind that `mpfb.freeze` rewires for glTF, and any other type stops the build.
- `mpfb.freeze` applies the pose, the shape keys and the helper mask, removes the rig and returns plain meshes by part name: `Body`, `Eyes`, `Eyebrows`, `Eyelashes`, `Teeth`, `Tongue` and `Hair`. Each keeps the vertex groups of the bones, for the garment scripts.
- MPFB writes its logs into Blender's user resource folder. `mpfb.services()` stops with an error unless `BLENDER_USER_RESOURCES` names a folder, and the build tool and the bridge set it. If you start Blender by hand for a test, set it to a scratch folder.

## Git hooks

The folder `.githooks/` holds three git hooks. At its start, the dev shell sets `core.hooksPath` to that folder. Thus the hooks run after you enter the dev shell once. Outside the dev shell, the pre-commit and pre-push hooks run themselves again inside it. The post-commit hook needs only git, so it never enters the dev shell. To skip the pre-commit or the pre-push hook once, add `--no-verify` to `git commit` or `git push`.

### Before a commit

The pre-commit hook runs on every commit that stages C# files or projects. A commit with no such file, such as a ticket note, skips it. The hook changes no file in the working tree. It does four things:

1. It builds the projects that the commit changes and the test projects that test them, incrementally. The build runs every analyzer, and an error stops the commit.
2. It runs the tests of the changed code in Checked, one process per test project, in parallel. A failed test stops the commit, after the length part of the commit report (step 4). The hook skips a test project that has a test record for the staged code ("The test record" below).
3. It measures the slop metrics of the staged tree with `tools/Tertius.Slop measure --details`, in parallel with the tests. After the tests pass, it leaves the note in the file `slop-pending` of the worktree's git folder. The metrics never stop the commit.
4. After the measure ends, it prints the commit report (`tools/commit_report.py`). The report never stops the commit. Findings come first and name only code touched by the commit:
   - Length: methods over the length limit (MA0051), in `src` and `tests`.
   - Complexity: functions that hold a changed line and have cyclomatic complexity (CC) above 10, with file, line, name, CC, source lines of code (SLOC) and high CC mass. The greatest mass comes first.
   - Verbosity: each rule finding that covers a changed line, with its message.
   - Duplication: each clone pair with a changed line in either copy, with both locations.

   Any finding triggers the refactoring prompt, even when project ratios are flat or falling. The prompt comes before a compact project addendum: erosion, verbosity, tight class cohesion (TCC) and depth, each with a comparable ratio change. Project absolute counts belong in the push summary. TCC and depth have no agreed finding thresholds, so they add no type-specific warnings.

   The addendum names its baseline once: the nearest commit up to HEAD on the first-parent line that has a note. That is the parent of a new commit, or an older commit when the parent has no note. In `git commit --amend`, HEAD is the commit being replaced. A missing baseline gives no change. Missing legacy type data is unavailable; a measured empty denominator is `n/a`. Different counting/reference provenance suppresses type-metric changes. Applicability populations, incomplete evidence, binding warnings and provenance warnings follow the ratios.

   When no finding touches changed code, the report shows only the addendum and no refactoring prompt. If the measurement fails, the length report still runs. A summary failure cannot suppress a prompt for findings already printed. A failed test prints the length part only, and then stops the commit.

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

A changed line is a line that the staged diff adds or edits. A deletion changes the line after it, so a deletion inside a function changes that function, and a deletion of the lines that follow a function does not. Each compile writes its diagnostics to `obj/<configuration>/<framework>/analyzers.sarif`, and the report reads the length results from there. The length limit is in `.editorconfig` (MA0051). The erosion and the verbosity measure only `src`, so the report names no complexity or clone in `tests`. The complexity of the report is the erosion's own CC with its limit of 10, so the build's CA1502 is off.

### The test record

A test record says that every test of one test project passed on one exact version of the code. With it, the pre-commit hook skips the tests that you ran just before the commit. Three parts make it:

1. The code hash. `tools/code-tree.sh` prints the git tree hash of the code, then the git folder of the worktree. The code is every file under `src`, `tests`, `analyzers`, `tools`, `benchmarks`, `scripts`, `native` and `.config`, plus the build files at the root. Untracked files count, but Markdown files and ignored files do not. With `--staged`, the script hashes the staged code. It works on a copy of the index, so it never changes what is staged.
2. The stamp. After each build of a test project, the target `StampCodeTree` in `Directory.Build.targets` writes the code hash to `code-tree.txt` next to the test assembly. A run with `--no-build` tests the binaries of the last build, and the stamp holds the code hash of that build.
3. The record. Every test project loads the test platform extension in `tools/Tertius.TestRecord` (`Directory.Build.props`). After a full run with at least one passed test and no failed test, the extension copies the hash from the stamp to `tertius-tested/<configuration>/<test project>` in the git folder. A full run with a failed test deletes the record. A run with a `--filter-*`, `--treenode-filter`, `--list-tests` or `--explicit` option, or with a response file, is not full and leaves the record as it was.

The pre-commit hook computes the staged code hash. For each test project that it must run, it reads the Checked record. If the record holds the staged code hash, the hook prints a line and skips that project. The build, the slop metrics and the commit report still run.

So any full Checked run just before the commit lets the commit skip its tests:

```bash
nix develop --command dotnet test --solution Tertius.slnx -c Checked
```

A full run of one test project records only that project. `tools/test-all.sh` records every project in both configurations. If you edit code after the run, the hash changes, and the hook runs the tests again. An edit to a ticket or a Markdown file leaves the hash as it was.

### After a commit

The post-commit hook attaches the pending slop note to the new commit, under `refs/notes/slop`. The hook compares the tree of the commit with the tree that the pre-commit hook measured. If the two trees are different, the commit gets no note. A commit without a pending note also gets no note here: a commit with `--no-verify`, a commit without C# changes, and a merge, which runs no post-commit hook. If such a commit changes C# code under `src`, the pre-push hook notes it.

### Before a push

The pre-push hook does two things:

1. It runs `tools/cleanup.sh` on the C# files that the pushed commits change. If the cleanup changes a file, the hook builds the solution and commits the result as "Clean up code before push". Then it stops the push, because git chose the commits to push before the hook ran. Push again to send the cleanup commit too. If the cleanup fails, or the cleaned code does not build, the push stops and the changes stay uncommitted.
2. It updates the slop history, and it never stops the push. For each pushed branch, it notes the pushed tip. It also notes each commit since the last push that changes C# code under `src` and has no note. Then it leads with each current project ratio and its change since the last push, with constituent absolute counts and their changes in parentheses on the same line. It ranks commits by their increase in high CC mass or verbose lines, even if a ratio fell. Last, it merges the remote's notes into `refs/notes/slop` and pushes them to the same remote.

An illustrative summary (counts are examples):

```text
pre-push: Project metrics since the last push:
pre-push:   Erosion 0.3648 (+0.0057) (high CC mass 12060.2 [+18.7]; high CC functions 93 [+1])
pre-push:   Verbosity 0.0363 (+0.0009) (verbose lines 1261 [+11])
pre-push:   TCC 0.4000 (+0.1500) (2/5 shared/possible method pairs; shared pairs +1, possible pairs +1)
pre-push:   Depth 2.0000 (+0.0000) (8/4 WMC/API entries; WMC +2, API entries +1)
pre-push:   Cohesion types: 3 included, 2 not applicable, 1 incomplete.
pre-push:   Cohesion excludes incomplete evidence; known counts remain in type details.
pre-push:   Depth types: 3 included, 3 not applicable; declaration-only API entries excluded.
pre-push:   Unresolved state access locations: 1 (see details).
pre-push:   Type metrics type-metrics-v3
pre-push:   SLOC 34450 [-34]
pre-push: the commits that raised the high CC mass the most:
pre-push:   +18.7  c774e05  ter-m0db  Merge the generated unions of Demo (ter-m0db)
pre-push: the commits that raised the verbose lines the most:
pre-push:   +11  e771003  (no ticket)  Clean up code before push
```

If a file that the cleanup must clean holds uncommitted changes, the hook stops before the cleanup. Commit or stash those changes, then push again. The cleanup can add a commit only to the branch that is checked out. For another pushed branch, it prints a note and skips that branch.

## Code cleanup

`tools/cleanup.sh` cleans up C# files in two steps:

1. Roslyn, the C# compiler, makes every fix that changes the code but not its meaning. `dotnet format style` applies the IDE rules, such as redundant `this.` and `using` directives, auto properties, `readonly` fields, `var`, expression bodies, collection expressions, primary constructors and pattern matching. `dotnet format analyzers` applies the Roslynator rules (RCS), such as a get-only auto property for one with a private setter that only a constructor uses. `.editorconfig` lists both kinds of rules, each with its reason, and the script reads the list from there: every rule at suggestion or warning. The same `dotnet format analyzers` call also applies the fix of `TS0016` (ARCHITECTURE.md, "Style analyzer"), because `IDE0066` turns a switch statement into a switch expression and carries its default section over as the arm `_ => throw new UnreachableException()`, which `TS0016` forbids over a closed set. Its fix deletes that arm in the same pass. One fix can make room for another, so the step repeats until it changes nothing, at most 3 times.
2. CSharpier formats the files, with lines of at most 120 characters (`.csharpierrc.yaml`). It also sorts the `using` directives. It runs last, so its formatting wins.

Run it in the dev shell:

```bash
bash tools/cleanup.sh
```

With no argument, it cleans the C# files that differ from main, committed or not. `--all` cleans every C# file. A list of files cleans only those files. For a list, `dotnet format` loads a solution with only the projects of the files.

Times on 2026-10-24, on an M-series Mac:

| Files | Nothing to fix | Something to fix |
|---|---|---|
| 1 file | about 20 s | about 33 s |
| 20 files in 10 projects | about 36 s | about 64 s |
| `--all` | about 70 s | about 3 minutes (the first cleanup with these rules) |

Before Roslyn runs, the script builds the union generator and the style analyzer in Debug, because `dotnet format` loads that build. Without the generator, the generated members of the unions are missing, and the fixes misread the code.

CSharpier 1.3.0 cannot parse the C# 15 `union` declaration and the `closed` modifier. `.csharpierignore` lists the files that hold them, and CSharpier leaves them as they are. Roslyn still fixes them. A file that CSharpier cannot parse and that is not in the list stops the cleanup with an error, and CSharpier leaves that file as it was.

Some IDE rules stay off, each with its reason in `.editorconfig`. Two of them matter most. `IDE0004` (redundant casts) takes a cast to a C# 15 union for redundant and changes the meaning of the code. `IDE0045` and `IDE0046` write nested conditional expressions, which `TS0011` forbids. No rule orders the members of a type, so the cleanup leaves the order as the author wrote it (STYLE.md, "Member order").

Each cleanup of the whole codebase is one commit, and `.git-blame-ignore-revs` names each one. Run this command once, so that `git blame` skips those commits:

```bash
git config blame.ignoreRevsFile .git-blame-ignore-revs
```

## Named arguments

The style analyzer reports a call whose target takes more than 3 parameters and has an argument without a name (`TS0002`, ARCHITECTURE.md, "Style analyzer"). Its code fix names the arguments. To fix every such call of the solution at once, build the analyzer and the union generator in Debug, which `dotnet format` loads, then run the fix:

```bash
nix develop --command dotnet build Tertius.slnx -c Debug
nix develop --command dotnet format analyzers Tertius.slnx --diagnostics TS0002 --severity warn
```

Four more rules of the style analyzer have code fixes that `dotnet format` applies the same way: `TS0004` writes a property pattern for a positional one, `TS0005` marks a default interface method for inlining, `TS0006` seals a class, and `TS0016` deletes a discard arm that only throws `UnreachableException`. Name the rules after `--diagnostics`, for example `--diagnostics TS0004 TS0006`. `TS0005` and `TS0006` report at the end of a compilation, which `dotnet format analyzers` runs too.

`dotnet format` reads the projects in Debug, so it does not see the code behind `#if !TERTIUS_CONTRACTS`. For that code, build in Release and run the fix again with the configuration in the environment: `Configuration=Release nix develop --command dotnet format analyzers Tertius.slnx --diagnostics TS0002 --severity warn`. Without the union generator's Debug build, the calls that take a generated union do not bind, and the fix skips them.


## Type coherence and source evidence (ter-dyss)

For interpretation, numerical anchors and reviewed examples, see [Interpreting code quality metrics](../CLAUDE.md#interpreting-code-quality-metrics).

The project reports TCC (tight class cohesion, measured through instance storage) and depth (function complexity per API entry). Counting version `type-metrics-v3` follows properties and helpers on the same instance. It also records when a measure does not apply or its evidence is incomplete. There is no dependency score, role classifier, threshold or retired score. Depth and erosion stay separate. Neither a higher depth nor a higher WMC proves better design.

TCC is `sum(sharedMethodPairs) / sum(possibleMethodPairs)` over complete, applicable types. Two methods share state when their sets of instance storage locations intersect. A field, a property over that field and a helper that uses the field identify the same storage. Pairs never cross type boundaries. Each type retains `m*(m-1)/2` raw possible pairs for its `m` declared visible ordinary instance methods. Constructors, accessors, operators, static methods and private methods do not add eligible pairs. Private helpers and accessors supply storage to callers instead. TCC ranges from zero to one. Methods are not complexity-weighted.

Automatic properties resolve to their compiler backing fields. C# field-backed properties use the same actual storage. Explicit properties follow only the getter or setter that the operation executes. Property and field patterns use the matched receiver, including switch patterns; nested patterns on another object do not supply this instance's storage. Compound writes follow both; assignment through a reference-returning property follows its getter. Same-instance helpers, invoked nonstatic local functions, helper chains and recursive calls use this same analysis. Recursion terminates without dropping reachable storage. Private, sealed and explicit base dispatch retain their determinate implementation. A sealed receiver selects its actual override, including calls inside inherited helpers.

Attributes, `nameof`, unused local functions and unexecuted lambda bodies contribute no storage. Another receiver does not supply this instance's fields. Object and `with` initializer targets belong to another instance, but evaluated arguments and initializer values can still read this instance. Calls on dependencies do not traverse dependency bodies. Reading the dependency field itself still counts. Identity-preserving reference conversions and conditional access preserve the receiver; user-defined conversions do not. Local aliases of `this` are not analyzed.

Unavailable helper/accessor bodies, unresolved same-instance binding, runtime-ambiguous virtual calls and dynamic dispatch produce incomplete evidence. Unknown base storage cannot establish absence of storage. Primary-constructor captures have no available lowered field symbol, so their use is incomplete rather than an invented field or a no-storage result. Known fields, shared pairs and raw possible pairs remain visible when a ratio is unavailable.

| Cohesion outcome | Meaning | Project contribution |
|---|---|---|
| `complete` | At least two eligible methods, instance storage, and complete state-access evidence. Zero sharing is a real zero. | Shared and possible pairs enter the pool. |
| `not-applicable`, `fewer-than-two-methods` | Fewer than two eligible methods. Unresolved detail is still retained. | Neither count enters the pool. |
| `not-applicable`, `no-instance-storage` | Source and metadata establish the absence of instance storage, with no unresolved state evidence. | Neither count enters the pool. |
| `incomplete`, `unresolved-state-access` | State access or base storage cannot be resolved. | Neither count enters the pool. |

The summary reports included, not-applicable and incomplete type counts. An empty complete pool with incomplete types is `incomplete`, not zero or ordinary `n/a`. An empty pool without incomplete types is `n/a`.

Interfaces cannot own instance fields. Their contract declarations do not produce unresolved storage warnings; their cohesion is not applicable. A default interface implementation still contributes to depth under the rules below.

Depth is `sum(WMC) / sum(apiEntries)`, not an average of per-type ratios. WMC (weighted method count) here sums the existing `Function.Complexity` of all handwritten functions owned by a type, including private and static functions. This uses the existing CC (cyclomatic complexity) definition: one plus syntactic branches, with a switch counted once. Constructors, destructors, operators, body-bearing accessors, expression-bodied properties/indexers and local functions count under the existing function rules. Lambdas remain in their enclosing function. Bodyless declarations, auto-accessors and field initializers add no function weight. Nested types own their functions separately. A primary constructor has an API entry but no separate function body under these rules.

An API entry is a declared member available outside its declaring type: public, internal, protected, protected-internal or private-protected. This is member accessibility relative to the declaring type, not effective visibility outside the assembly. Both static and instance members count. Each method overload, explicit constructor, operator/conversion, property, indexer, event and field counts once; a property counts once regardless of its accessors. Each variable in a multi-field declaration counts once. Private members, inherited members and synthesized members do not add entries. Explicit interface implementations have private declared accessibility and do not add entries; the interface's own declared member is an entry. All entries retain signatures, parameter counts, visibility, static status and locations, plus `isApiEntry`, so the total and detail share the same decision.

Both depth sums include only types with handwritten functions. Declaration-only interfaces, enums, delegates and field/automatic-property-only types keep their raw API entries in detail, but add no pooled surface. Their depth is `not-applicable`, reason `no-handwritten-functions`. An interface with a handwritten default implementation and a constructor with a handwritten body have implementation. A type with implementation but no API entries has per-type depth `not-applicable`, reason `no-api-entries`. Its WMC still enters the pooled numerator, as before. With no pooled API entries, project depth is `n/a` and keeps its raw WMC. Summary depth counts distinguish per-type included and not-applicable types, including private-only implementation in the latter.

Partial declarations merge by symbol. Nested types have separate rows. Extension blocks belong to the containing type. Enum constants count as fields. A written record primary constructor counts as an API entry, but synthesized positional properties and delegate Invoke do not. Source-generated declarations do not enter the source population. Compiler-synthesized storage can identify a property's actual backing field without adding API entries or functions. Depth is nonnegative and has no upper bound.

The JSON of `all` and `measure --details` has `classMetrics.summary` and `classMetrics.types`. Summary `tcc` and `depth` hold `numerator`, `denominator` and nullable `ratio`. `cohesionPopulation` and `depthPopulation` each hold `included`, `notApplicable` and `incomplete` type counts; depth has no incomplete category in this source-structural policy. `unresolvedStateAccessCount` counts distinct unresolved source spans and reasons across the type evidence.

Type rows retain `wmc`, `apiEntries`, declaration locations, member detail, function measures, method field names and named shared pairs. Their `tcc` and `depth` keep raw numerator and denominator even when `ratio` is null. `tcc.applicability` is the authority for the derived `tccApplicability`; `depthApplicability` describes the depth decision. Applicability holds `status` and, when needed, `reason`. Each method and type retain `unresolvedAccesses`, with `location` (file and one-based start/end lines), one-based `column` and exclusive `endColumn`, plus `reason`. Ordering is deterministic. Partial methods read their implementation once. Inherited storage can connect declared methods, but inherited methods do not add pairs.

For name binding the tool reads only snapshot source and the runtime's platform metadata, plus standard SDK implicit usings. It never restores or builds historical projects, discovers `bin`/`obj`, or reads current engine assemblies. `--reference-list /absolute/path/references.json` optionally supplies a JSON array of absolute third-party metadata paths; `TERTIUS_SLOP_REFERENCE_LIST` supplies the same input to hooks without shell changes. Configure that variable in the dev shell, not a shell profile. Current engine outputs must not be listed. Metadata contents are SHA-256 fingerprinted and stored with the counting version. Different provenance suppresses deltas.

This is a combined source compilation, not a historical build. Project-specific defines, source-generated APIs, separate assembly boundaries, duplicate fully qualified names across projects and historical compiler behavior are not reproduced. Compiler errors are retained as `bindingDiagnostics`, with a count in summaries and hook output. Review those errors before treating available-symbol evidence as exhaustive. Source-only field binding does not invent fields for unresolved generated state or primary-constructor captures.

Notes store the optional summary, not type details. Old v2 summaries remain readable with unavailable applicability metadata, not fabricated zero counts. Backfill fills missing data only when counting/reference provenance matches, preserves existing counts and metadata, and never replaces an incompatible summary. Missing v3 applicability counts can be filled under matching provenance. Unchanged note text is not rewritten, including retired nested fields. Repeating backfill changes nothing. Readers prefer lines with more complete counts.

CSV retains the existing columns and appends `cohesionIncludedTypeCount`, `cohesionNotApplicableTypeCount`, `cohesionIncompleteTypeCount`, `depthIncludedTypeCount`, `depthNotApplicableTypeCount` and `unresolvedStateAccessCount`. Legacy metadata cells are empty. The TCC cell is `incomplete` when no complete type can be measured but incomplete evidence exists. CLI and pre-push summaries show ratios, raw counts and population limitations. The pre-commit numeric addendum remains ratio-only, followed by population and incomplete-evidence limitations. No type-metric deltas cross versions or reference fingerprints. The v2 dataset and old notes remain v2; this command does not migrate or overwrite them. Erosion, verbosity and touched-code findings are unchanged.

```sh
nix develop --command dotnet run --project tools/Tertius.Slop -- all .
nix develop --command dotnet run --project tools/Tertius.Slop -- measure HEAD --branch ter-dyss-class-metrics --source seed --details /tmp/slop-details.json --parent HEAD
nix develop --command dotnet run --project tools/Tertius.Slop -- backfill
```

Use the same metadata configuration for hooks and backfill. The default configuration needs no reference list. An explicit reference list changes the stored metadata fingerprint.

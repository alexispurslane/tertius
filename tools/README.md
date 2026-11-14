# Tools

These helpers run every test project in one command, start JetBrains Rider inside the dev shell, run code coverage and mutation testing (Stryker.NET and listed mutants) on the lines that a branch changed, report duplicated code, measure the sloppiness of the code, build the docs site, trace the GPU passes of a demo run, survey every scene of the renderer, build scripted models in Blender, and clean up the code. The git hooks in `.githooks/` call some of them. For the rules on which helper to run, read the Testing section of CLAUDE.md. The scripts are tool code: they never ship, so they skip polish.

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

## Vulkan drivers

On the Mac, the dev shell offers two Vulkan drivers. MoltenVK is the default: the shell sets `VK_ICD_FILENAMES` to its ICD manifest. The shell's MoltenVK is the nixpkgs build with two patches (see "The patched MoltenVK"). KosmicKrisp is LunarG's Mesa driver for Apple GPUs, from the pinned nixpkgs' `mesa`. The shell sets `TERTIUS_KOSMICKRISP_ICD` to its ICD manifest. To run a command on KosmicKrisp, set `VK_ICD_FILENAMES` from that variable for the command alone:

```bash
nix develop --command bash -c 'VK_ICD_FILENAMES=$TERTIUS_KOSMICKRISP_ICD dotnet test --solution Tertius.slnx -c Checked'
nix develop --command bash -c 'VK_ICD_FILENAMES=$TERTIUS_KOSMICKRISP_ICD dotnet run --project src/Tertius.Demo -c Release -- --script scripts/lamp-hall-16.json --headless --out <folder>'
```

`vulkaninfo --summary` names the driver of a shell. On Linux the shell sets no `VK_ICD_FILENAMES`, so a value from outside the shell stays. If the variable is empty and the host has NVIDIA's driver, the shell's hook sets it to NVIDIA's manifest (see "Linux NVIDIA machines"). [docs/research/kosmickrisp.md](../docs/research/kosmickrisp.md) compares the two drivers, and lists the tests that fail on KosmicKrisp and why.

### The patched MoltenVK

The Mac's dev shell builds MoltenVK 1.4.2 with two patches, from `native/moltenvk.nix`. A Linux shell does not build it. `nix build .#moltenvk` builds the driver alone.

Why the patch exists. A Metal encoder is one group of GPU commands of one kind: a render pass, compute dispatches, or copies. MoltenVK 1.4.2 ends the open encoder at every pipeline barrier outside a render pass. So each compute dispatch behind a barrier was an encoder of its own. The GPU is idle for about 20 µs at each encoder boundary. On the station deck that was 1.10 ms of a 24.0 ms frame. The patch lowered the frame span from 24.00 to 23.07 ms, and the encoders of a frame from 116 to 79. Every image stayed the same, byte for byte. The measurements are in [the frame-time sweep](../docs/research/frame-time-sweep-2026-11-11/addendum.md), sections A5 and A6. [The acceptance record](../docs/research/frame-time-sweep-2026-11-11/acceptance-ter-2zga/README.md) holds the frames, the traces and the test runs of the flake's build.

The two patches:

- `native/moltenvk-execution-dependencies.patch` is upstream's fix of MoltenVK issue 2805, from pull request 2819. Release 1.4.2 does not hold it. A compute encoder that several kinds of command share then updates the fence of every barrier stage that it used.
- `native/moltenvk-keep-dispatch-encoder.patch` is ours. Under three conditions, a barrier then keeps the open encoder.

The three conditions of a kept barrier:

1. Every destination stage of the barrier is the compute stage.
2. The open encoder is a compute encoder that holds only dispatches.
3. No time stamp waits for the end of the encoder. MoltenVK reads a stamp at the end of the encoder, so the encoder must end there.

A Metal fence is an object that one encoder updates at its end, and that another encoder waits for. MoltenVK keeps fences for four barrier stages: vertex, fragment, compute and copy. Inside one compute encoder, Metal runs the dispatches in their order. Three ordering rules give a kept barrier the order that Vulkan asks for:

1. The open encoder waits for the latest fence of each source stage of the barrier. So its later dispatches run after the work of those stages.
2. The open encoder updates its compute fence only at its end. So each destination stage waits for that fence, and a later encoder runs after the whole open encoder.
3. For every other source stage, the destination stages wait for the latest fence of that stage, as stock MoltenVK does.

The engine does not depend on the patch. The renderer records every barrier that the Vulkan spec asks for, and stock MoltenVK and a Linux driver render the same frame.

To compare with the stock behavior, use one of two switches for one command:

```bash
# The patched build, with the stock behavior at barriers: every barrier ends the encoder.
nix develop --command bash -c 'TERTIUS_MOLTENVK_STOCK_BARRIERS=1 dotnet run --project src/Tertius.Demo -c Release -- --script <script> --headless --out <folder>'
# The nixpkgs build without the two patches.
nix develop --command bash -c 'VK_ICD_FILENAMES=$TERTIUS_STOCK_MOLTENVK_ICD dotnet run --project src/Tertius.Demo -c Release -- --script <script> --headless --out <folder>'
```

Before you merge a change of a patch, do three things:

1. Run the test suites in Checked and in Release.
2. Run the Rendering tests and the Host tests once with `MTL_DEBUG_LAYER=1`, which turns on Metal's API validation. A wrong use of a fence stops the process there.
3. Compare the frames of one headless run with those of the stock build, byte for byte. `diff -rq <folder a> <folder b>` lists the files that differ. Between two correct runs, only `gpu-timings.json` differs.

A wrong fence does not fail a test reliably. It shows as a rare flicker, because two encoders then run in the wrong order only sometimes.

A later MoltenVK release holds the fix of issue 2805. After nixpkgs moves to such a release, the first patch fails to apply, and the shell does not build. Remove that patch from `native/moltenvk.nix` then. Upstream's issue 2893 proposes the change of the second patch for MoltenVK itself.

## Linux NVIDIA machines

The dev shell runs on `x86_64-linux` with NVIDIA's driver, in a container or on a desktop. Ticket ter-libf brought it up on two Runpod pods on 2026-11-12: an RTX 2000 Ada and an L4, both with driver 595.91.07, in Ubuntu 22.04 containers.

### What the shell does on Linux

The Nix-built Vulkan loader does not search `/usr/share/vulkan/icd.d`, and Nix's glibc does not search the system library folders. So the shellHook sources `native/host-nvidia-vulkan.sh`, which does three things:

1. It sets `VK_ICD_FILENAMES` to NVIDIA's manifest, `/etc/vulkan/icd.d/nvidia_icd.json` or `/usr/share/vulkan/icd.d/nvidia_icd.json`.
2. It links only NVIDIA's libraries into `$XDG_RUNTIME_DIR/tertius-nvidia-<uid>` (or `/tmp`) and puts that folder first on `LD_LIBRARY_PATH`. The system library folder itself must stay off the path, because its libstdc++ and other libraries would replace the Nix builds.
3. It sets `GLIBC_TUNABLES=glibc.rtld.optional_static_tls=65536`. The Vulkan loader loads the driver at each `vkCreateInstance` and unloads it after the last instance. glibc does not take back the static TLS of `libnvidia-tls` after an unload, so with the default reserve the 38th instance of one process fails with `ErrorIncompatibleDriver`. A test process makes hundreds of instances. A reserve of 1 MB broke thread creation in the .NET CLI, because glibc puts the reserve on every thread's stack.

A `VK_ICD_FILENAMES` from outside the shell wins. A host without NVIDIA's manifest gets none of the three. The shell also adds `libglvnd`, `libx11` and `libxext` to the library path, because NVIDIA's `libGLX_nvidia.so.0` needs them and a container lacks them. If `vulkaninfo --summary` names `llvmpipe` or finds no driver, the container lacks the graphics part of the driver: set `NVIDIA_DRIVER_CAPABILITIES=all`, or use another host.

### A container

`tools/runpod/provision.sh` makes an Ubuntu container ready, as root. A pod runs it, and `tools/runpod/Dockerfile` runs it for the serverless image:

```bash
bash tools/runpod/provision.sh <flake folder> [<source folder>]
```

It installs a pinned single-user Nix (2.34.8), builds or downloads the dev shell and keeps it as the profile `/nix/var/nix/profiles/tertius-dev`. It pins the registry's `nixpkgs` to the flake's lock, so `nix develop /nix/var/nix/profiles/tertius-dev --offline --command <command>` downloads nothing. With a source folder, it also restores the NuGet packages. Two container limits shape it:

- A container allows no user namespaces, so Nix builds without a sandbox. Then a build's HOME is the real folder `/homeless-shelter`, which the cargo step of jscpd creates, and Nix refuses the next build. The script removes the folder and runs the step again, at most three times.
- On the pods, the step took 197 to 216 s, mostly downloads, and joltc and jscpd built from source. The demo's Release build then took 42 to 57 s.

tools/runpod/README.md says how to build the serverless image.

### A pod for a bring-up

1. Create the pod with API v2 (`POST https://api.runpod.io/v2/pods`), with `NVIDIA_DRIVER_CAPABILITIES=all` and `PUBLIC_KEY` in its environment, and `22/tcp` in its ports.
2. Copy the tracked source: `git ls-files -co --exclude-standard | rsync -az --files-from=- ./ root@<host>:/root/tertius/`.
3. On the pod, run `bash tools/runpod/provision.sh /root/tertius`.
4. Make sure that `vulkaninfo --summary` in the shell names the NVIDIA GPU.
5. Delete the pod with `DELETE /v2/pods/<id>` when it has no work. A stopped pod still bills its disk.

### Timing on a cloud GPU

- `nvidia-smi -lgc` fails in a container: "The current user does not have permission to change clocks" (exit code 4). Read the clock with `nvidia-smi --query-gpu=timestamp,clocks.sm,clocks.mem,temperature.gpu,power.draw --format=csv -lms 500`. `tools/gpu-clock.sh` does not read it yet (ter-az8f).
- The `capture` pass of a headless run copies each frame over PCIe. It took 2.6 ms per 1920 × 1080 frame on the RTX 2000 Ada, and 0.9 to 4.5 ms on the L4, against 0.03 ms on the Mac. The frame span counts it (ter-fw73). Compare passes, or the span without it.
- Compare a change with main only inside one job: cards, hosts and the capture time differ between pods.

## GPU jobs on Runpod

An agent runs a cloud job, on a GPU worker or on a CPU worker, through one script, `tools/runpod/gpu_job.py`, which keeps the spending within the caps. [tools/runpod/WORKBENCH.md](runpod/WORKBENCH.md) is the guide for agents: the A/B against main, the quality A/B, what still needs the Mac, the tests in the cloud, and the costs. [tools/runpod/README.md](runpod/README.md) describes how the queue works: the jobs, the images and endpoints of each kind, the caps, the audit log and the measurements. It also says how to build the serverless image.

## The remotes: GitHub and the knot mirror

`https://github.com/alexispurslane/tertius` is the primary home of the repository, and it is public. GitHub Actions builds the GPU image there. The knot, `git@knot.xenolandscapes.com:did:plc:6mfsw4jthsacfcdbfmfetvin`, is a push mirror: it gets every push, but nothing reads from it. Alexis moved the primary from the knot to GitHub on 2026-11-12.

The remote `origin` fetches from GitHub. It has two push URLs, GitHub first and the knot second, so every `git push origin <ref>` sends the ref to both. The repository's git configuration holds the URLs, so every worktree uses them. To set them up in a new clone, run these commands:

```bash
git remote set-url origin git@github.com:alexispurslane/tertius.git
git remote set-url --add --push origin git@github.com:alexispurslane/tertius.git
git remote set-url --add --push origin git@knot.xenolandscapes.com:did:plc:6mfsw4jthsacfcdbfmfetvin
```

Git pushes to the URLs in order, and it runs the pre-push hook once for each URL. If one URL refuses the push, git reports an error and still pushes to the other URL. Then push again: the URL that has the ref already reports it as up to date. A push that fails only on the knot leaves GitHub correct.

If the pre-push hook makes a cleanup commit, the first push stops for GitHub. The knot still gets the commits without the cleanup commit, because git chose them before the hook ran. Its hook run says that the branch is not checked out and skips the cleanup. The second push sends the cleanup commit to both URLs, and both then have the same commit. A test with two local bare repositories and git 2.54.0 showed this order on 2026-11-12.

To stop the mirror, remove the knot's push URL. Then pushes go to GitHub only:

```bash
git remote set-url --delete --push origin git@knot.xenolandscapes.com:did:plc:6mfsw4jthsacfcdbfmfetvin
```

The knot has no Git LFS. As long as the mirror stays, decision 21 of `docs/plans/asset-pipeline.md` keeps binary build output out of git.

Before the first push to GitHub on 2026-11-12, gitleaks 8.30.1 scanned every commit of every ref. It found three matches, and all three were the words "key generation" in `docs/research/temporal-renderer/walk-hill-climb.md`. A search for each stored secret and for key prefixes found nothing.

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

## Mutation testing

The user decided on 2026-11-01 (tickets ter-u63a and ter-hxca) how a ticket's hardening tests its tests with mutants. A mutant is a small, deliberate change to the code, which a good test fails on. The mutation step of a ticket makes one table of mutants with one result each. Two tools make its rows, and both print the same table:

- `tools/stryker/changed.py`, the official Stryker run, mutates the C# lines that the branch changed in Core, Simulation and Physics. Their tests run on the CPU only.
- `tools/mutate.py` runs a list of mutants that the agent writes. The list holds every other changed code line: Rendering (C# and GLSL), Host and Demo, whose tests run on the GPU, and the analyzers in `analyzers/`. It also holds the lines that the Stryker run cannot mutate: its no-mutant rows, its removed rows, and the lines that it lists without any Stryker mutant.

`changed.py` prints the changed code outside its run, so that no changed line falls between the two tools. For an analyzer mutant, the test project is `tests/Tertius.Unions.Tests` for `analyzers/Tertius.Unions` and `tests/Tertius.Style.Tests` for `analyzers/Tertius.Style`.

### The shared table

The table has one Markdown row per mutant, then one summary line with the count of every result:

```
| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| S09 | src/Tertius.Rendering/Shaders/own_rays.glsl:27 | entry packing y << 15 | killed | ScreenTraceRenderTests.AFrameAfterOne..., ScreenTraceRenderTests.TheOwnRay... | 41.2 |
| Core-4489 | src/Tertius.Core/Unions/Result.cs:44 | holds != Holds.Nothing -> holds == Holds.Nothing | killed | ResultTests.TheDefaultResultHoldsNeitherCase | - |
16 rows: killed 10, timeout 0, survived 5, no-coverage 0, build-failed 1, no-tests 0, no-mutant 0, removed 0
```

The killed-by column names at most three failed tests. The last column gives the seconds of the mutant's build and test run in `tools/mutate.py`. Stryker reports no time per mutant, so its rows show `-`. The results:

| Result | Meaning | Triage |
|---|---|---|
| killed | A test failed, or the test host crashed. | None. |
| timeout | The tests ran past the time limit. This counts as killed, but the table lists it apart, because a timeout under load can hide a survivor. | None, unless the official run is not valid. |
| survived | Every test passed with the mutant. | A test or a reason. |
| no-coverage | Stryker: no test ran the mutant. This counts as survived (the user's rule from ter-u63a). | A test or a reason. |
| build-failed | `tools/mutate.py`: the mutant does not compile, so it is not a valid mutant. | Write a mutant that compiles, or drop it. |
| no-tests | `tools/mutate.py`: the test filter ran zero tests. This is an error in the list. | Fix the class names. |
| no-mutant | Stryker: a changed line where every Stryker mutant failed to compile. | Put the line in the `tools/mutate.py` list. |
| removed | Stryker: the branch removed lines here. | If the removed lines did something, put back their behavior as a mutant in the list. |

### The triage rule

Give each survived or no-coverage mutant one of three answers, as the `test-hardening` skill says:

1. Write a test that kills it. The test must fail on the mutant first. With `tools/mutate.py`, run the mutant again with `--only <id>` and see `killed`. With Stryker, run the official command again.
2. Write a one-sentence reason why the mutant is equivalent: no test can tell it from the original code. For a Stryker mutant, put the reason in a Stryker ignore comment above the line, for example `// Stryker disable once Equality: the guard and its negation give the same value at the bound`. For a listed mutant, put the reason in the ticket note.
3. Write a judgment call: a one-sentence reason why the behavior is not worth a test, or why no test can reach it. An example is ter-rjts's C09, a range check that no device reaches. Put the reason where an equivalent reason goes. A judgment call is not a claim that the mutant is equivalent, so say that it is a judgment call.

Never leave a survivor without one of the three answers.

### Listed mutants: tools/mutate.py

Write the list as a JSON array outside the repository, for example `/tmp/<ticket>-mutants.json`:

```json
[
 {"id": "S09", "file": "src/Tertius.Rendering/Shaders/own_rays.glsl", "line": 27,
  "original": "uint(pixel.y) << 16", "replacement": "uint(pixel.y) << 15",
  "project": "tests/Tertius.Rendering.Tests", "class": ["ScreenTraceRenderTests"],
  "note": "entry packing y << 15"}
]
```

- `id` is a unique name, for example C01 for C# and S01 for a shader.
- `file` is relative to the repository root. `line` is the 1-based line where the original text starts.
- `original` is the exact text to replace. It must start exactly once on that line, and it can span lines. `replacement` takes its place. An empty replacement deletes the original.
- `project` is the test project folder. `class` is the test class that covers the mutant, or a list of them. A bare class name matches that class in any namespace. A qualified name or a pattern with `*` goes to `--filter-class` unchanged.
- `note` is optional: the change in a few words, for the table.

Run the list in the dev shell. The second argument is a folder for the logs:

```bash
nix develop --command python3 -I tools/mutate.py /tmp/<ticket>-mutants.json /tmp/<ticket>-mutate
```

The script works in this order:

1. It reads the whole list and refuses to start on any error. Every file must exist, git must track it, and the file must have no uncommitted change. The file must belong to a project: a folder above it must have a `.csproj`. The original text must start on its line, and the ids must be unique. The exit code is 2.
2. It builds each distinct pair of test project and classes once, and runs it without a mutant. If a baseline fails or runs zero tests, the run stops before any change, because a failing baseline makes every mutant look killed.
3. For each mutant, it applies the mutant and builds only its test project in Checked, with a plain incremental build. It makes sure that the build wrote a new assembly of the mutated file's project into the test project's output. Then it runs only the mutant's classes in Checked with `--no-build`, under a timeout of 120 s. At last it restores the exact original bytes and touches the file, so that the next build compiles it again. The restore also runs on a failure, an exception, Ctrl-C, SIGTERM and SIGHUP.
4. After the last mutant, it verifies the starting source and Git state, then builds each test project once more, so that the output holds no mutant. It verifies the starting state again and prints the table. An unexpected source or Git change fails the run without overwriting that change or building corrupted source.

A GLSL mutant reaches the tests through the build: the target `CompileGlslShaders` in `src/Tertius.Rendering/Tertius.Rendering.csproj` compiles a stage again when the stage or an include is newer than its `.spv`. The build then embeds the new SPIR-V in `Tertius.Rendering.dll`. Step 3 makes sure that this new assembly reached the test project's output.

The options:

- `--only C01,S03` runs only these ids.
- `--keep-going` goes on after a no-tests result. Without it, the run stops there, because the list has an error.
- `--allow-staged` admits tracked regular files with exactly one stage-0 entry, only when their worktree bytes and executable mode equal their staged version. The default still rejects uncommitted mutated files. Unstaged changes, unresolved stages, symlinks and other unsupported modes remain errors. This permits a resolved pending merge without an early commit.

The folder gets `inputs.json` (the source SHA256 hashes, executable modes, all semantic staged entries, HEAD and pending merge metadata), `results.json` (every row, with its original, replacement and classes) and, for each mutant, `<id>.build.log` and `<id>.test.log`. Both admission modes use the same byte-exact restoration and final baseline check. Git index refreshes may change index-file bytes without changing semantic entries. The runner never rewrites the index or merge metadata. The exit code is 0 when every mutant ran, and 1 when the run stopped early or its starting state changed. If SIGKILL leaves a mutant behind, it differs from its staged bytes and neither mode admits it. Inspect the saved baseline and recover only that run's source change; do not restore a pending merge input from HEAD.

A mutant of the Rendering GPU tests takes about 40 s: about 10 s of build and 30 s of tests. On ter-hxca's replay of 15 ter-rjts mutants, the three runs took 763 s in all, including 7 baselines.

### The official Stryker run: tools/stryker/changed.py

By default, run this one official run in the cloud. Run the command from the branch's checkout on the Mac, with the base branch, a folder for the output and your name:

```bash
python3 -I tools/stryker/cloud.py main /tmp/<ticket>-stryker --agent <your name>
```

`cloud.py` runs `changed.py` on each module on its own CPU worker, all at once, and prints the merged table ("A run on another machine" below). Only the official Stryker run goes to the cloud by default. The `tools/mutate.py` list, the pre-commit hook, the Checked and Release suites and the Mac pass of the GPU tests before a merge stay on the Mac. To run the official run on the Mac, run it in the dev shell on the branch:

```bash
nix develop --command python3 -I tools/stryker/changed.py main /tmp/<ticket>-stryker
```

For each of Core, Simulation and Physics with changed lines, the script writes `<out>/<module>/stryker-config.json` with `tools/stryker/config.py`. Then it runs Stryker on it with `tools/stryker/run.py`, one module after another. A module's test projects are its own and every other test project whose references reach it, also through `tests/Tertius.Testing`. The script never uses `tests/Tertius.Rendering.Tests` or `tests/Tertius.Host.Tests`, because their GPU tests are slow and depend on the load.

Before the runs, the script lists every other changed code file for the `tools/mutate.py` list, with its changed lines and the places of its removed lines. A code file is C#, a shader stage (`.vert`, `.frag`, `.comp`) or a shader include (`.glsl`, `.hlsl`) under `src/` or `analyzers/`. So the list holds Rendering, Host, Demo, the analyzers, and any new module that the official run does not name.

The official settings are in `tools/stryker/config.py`:

| Setting | Value | Why |
|---|---|---|
| `coverage-analysis` | `perTest` | Each mutant runs only the tests that reach it. `off` was slower, because every test is slow on the mutated assembly (t9-al5u, "Run time" below). |
| `configuration` | `Checked` | The same build as `tools/mutate.py` and the pre-commit hook. |
| `concurrency` | 1 | One test host at a time. At a higher concurrency, Stryker.NET 5.0.0 reports killed mutants as Survived (issue #3832, "Stryker.NET pitfalls"). |
| xunit threads | 4 per run | `changed.py` sets `DOTNET_PROCESSOR_COUNT` to 4 / concurrency for Stryker and its test hosts. xunit's default runs one test class per processor at once, so the hosts together run at most 4 threads. |
| priority | nice 20 | `changed.py` starts Stryker at the lowest priority, and its test hosts inherit it. The builders and GPU captures of other agents go first. |
| `timeout-ratio`, `additional-timeout` | 2, 300000 ms | A mutant's limit is 2 x its tests' initial time + 300 s. The per-test coverage capture uses 1.5 x a test's initial time + 300 s. |
| `break-on-initial-test-failure` | true | A test that fails without a mutant stops the run. |
| timeout share | at most 25% | Above it, the run is not valid. Quiet runs gave 0 of 3 timeouts (Core) and 3 of 20 (Simulation). The same 3 Simulation mutants also timed out at a 1-minute load of 173, so they make a test hang, and a hang is a kill. ter-u63a's trial, which the load broke, gave 65 of 76. |
| timeout load | at most 17.5 per core | A timeout counts as a kill only at a 1-minute load of at most 17.5 times the cores. The 3 Simulation hangs repeated at a maximum load of 30.5 and of 173.4 on this 10-core machine. Above that, the calibration has no evidence that a timeout is a hang. |

The run is not valid in four cases. The script then prints NOT VALID with the fix, and its exit code is 1:

- More than 25% of the mutants that ran ended in a timeout. Run it again on a quieter machine. If `tools/stryker/compare.py` then shows the same timeouts, the mutants make a test hang, and the share needs a new calibration.
- A mutant ended in a timeout, and the 1-minute load of the module's run passed 17.5 times the cores. Run it again on a quieter machine.
- Stryker marked a test Dubious. Its coverage capture failed, so every mutant also runs that test, and the run is not scoped. Run it again on a quieter machine.
- A build replaced Stryker's mutated assembly in a test project's output during the run. The mutants after that build ran the plain code, and they can show Survived. Run it again, and build or test no Checked test project of this worktree while it runs.

Do not build or test the Checked test projects of the run in the same worktree while an official run goes on. This includes `dotnet test -c Checked`, `tools/mutate.py` with one of those test projects (also `tests/Tertius.Unions.Tests` for an analyzer line), `tools/test-all.sh` and the pre-commit hook. Stryker copies its mutated module assembly into the output of the test projects before its coverage capture, and such a build replaces it. Stryker restores the plain copy about 1 s before it ends. `run.py` reads each copy in every sample between the two: a copy without Stryker's helper class `MutantControl` in two samples in a row makes the run not valid. A run in another worktree or on another machine does not interfere.

Under the table, the script lists the changed lines on which no Stryker mutant starts, because Stryker 5.0.0 has no mutator for them. Examples are an enum value in an assignment, such as `holds = Holds.Value;`, and the result of a switch arm. The list keeps only the lines that can hold behavior: lines with an assignment, an arrow, a `return`, a `throw`, a `yield` or a call statement. Judge each line, and put a mutant in the `tools/mutate.py` list for each line whose behavior a test must check. Two rules decide which mutants count:

- A mutant counts only for the line where it starts. A Block removal mutant over a whole method shows only that the method does something, not that a test covers each of its lines.
- A mutant that a Stryker filter removed counts for no line, because no test ran it. Its status is Ignored, with a reason such as `Removed by block already covered filter`. A mutant that an ignore comment disabled counts for its line, because the comment holds its triage.

On ter-9xl7's `Result.cs`, the list names 12 lines. Among them are the 4 lines of the hand mutants that Stryker missed, and lines 64 and 74, whose only mutants a filter removed.

A no-mutant row has the id `<module>-<path in the project>-L<line>`, for example `Core-Unions/Result.cs-L88`, and a removed row has `R` in place of `L`. The path keeps the ids of two files apart.

After each module's run, `run.py` builds its test projects again in Checked. Stryker restores the plain module assembly in their output when it ends normally. A run that stops early, for example on a signal, leaves its mutated assembly there, and a later `dotnet test --no-build` runs it. Under the table, the script prints the evidence of each module's log. It gives the compile tries of the one schemata build, and the initial test run with its count of tests. It also gives the time of the per-test coverage capture, or "no per-test coverage capture" for a run with `coverage-analysis` `off`, the Dubious tests, and the mean count of tests that each mutant ran. It also gives the 1-minute load and the test hosts, from the samples of `run.py`: their nice value, and the CPU of all hosts of one sample together. Stryker starts one host per test project at once, but it runs one of them at a time. So their CPU together shows the thread bound, and their count does not. `<module>/processes.log` holds each sample of Stryker's processes. `<out>/results.json` holds the rows, the evidence, the settings and the load samples.

`tools/stryker/run.py` is the only script that starts Stryker. It takes the machine-wide lock `/tmp/tertius-stryker.lock`, a folder, because two Stryker runs at once compete for the CPU and their tests time out. `changed.py` holds the lock over all its modules. The record in the folder, the release rule and the liveness rule are those of every machine lock ("The record of a machine lock"). The record's label is the run's folder. The lock works in this way:

- `os.mkdir` makes the folder for one process only. Right after it, the run writes its record into the folder.
- A second run waits while another run holds the lock, and prints a message every 5 minutes.
- The run removes the folder after its last module, also on a failure, an exception, Ctrl-C, SIGTERM or SIGHUP. It removes the folder only when the record holds its own token.
- The run stops if the holder of the record is gone, or if the folder has had no complete record for 60 s. It prints the command that removes the lock. It never clears the lock itself ("The record of a machine lock" gives the reason).
- `run.py` refuses to start Stryker if this run does not hold the lock.

A `run.py` from before ter-s4fg writes the file `owner` (pid, folder, local time) and no record. A current run judges such a lock by `kill -0` of that pid alone. The older `run.py` finds no `owner` file in a lock with a record, so after 60 s it stops and prints the command that removes the lock. If a lock holds a record, do not remove it while its holder runs: `pgrep -fl "stryker/(run|changed)\.py"` lists the runs.

With `--head <commit>`, the script measures a merged range, for example the commits of a closed ticket from its base. The files of the range must be the same in the working tree as at `<commit>`, and the script refuses otherwise. `--concurrency` (1 to 4) and `--additional-timeout` are for a calibration only, and the script marks such a run as not official.

#### Run time

Most of a run is the per-test coverage capture. The capture runs every test of the test projects alone, one at a time, on the mutated assembly. That assembly holds a switch for every mutant that Stryker compiled, and on run3 below that was 1,175 switches for 10 mutants in the spans. So every test is slower there than in the initial test run, which uses the plain assembly. The capture's time does not depend on the size of the change, and it uses about one core. On tiunr-e8xk's run1:

| Module | Mutants | Wall time | Coverage capture | Initial test run (plain assembly) |
|---|---:|---:|---:|---:|
| Core | 0 | 2,674 s | 2,517 s | 28 s (4,622 tests) |
| Simulation | 10 | 3,813 s | 3,674 s | 44 s (4,563 tests) |

The capture is not slow because it runs the tests one at a time. It is slow because the tests run on the mutated assembly. So `coverage-analysis` `off`, where every mutant runs every test, does not save time. It runs the slow full test pass once for each mutant. t9-al5u tried it on the same lines (run3, 2026-11-08), and these results made the official run go back to `perTest`:

- Each mutant's run of Core's tests took 3 to 12 s.
- Each mutant's run of the Simulation tests and of the Physics tests reached Stryker's limit, 314 s and 342 s. In those runs, Physics finished 361, 326 and 79 of its 1,492 tests. The initial run finished all of them in less than 33 s.
- So each Simulation mutant took about 11 minutes, and the 10 mutants would take about 2 hours. Run1 with `perTest` took 64 minutes for the module.
- The load and the explicit list of tests did not cause it. The initial run ran at the same nice value and a similar load, and it also sent Stryker's explicit list of tests.

Ticket t9-q4qe looks for a way to keep the mutants outside the spans out of the compiled assembly. That would shorten both the capture and the mutants.

Earlier runs, for comparison:

- On ter-hxca's official calibration, a run of Core on the 3 mutants of ter-9xl7's `Result.cs` took 2,107 s. The initial test run of 4,593 tests took 26 s. The per-test coverage capture took 2,017 s, 96% of the run, and the 3 mutants took seconds. At concurrency 3, the capture took 990 s of 1,092 s, because three test hosts shared the tests.
- On ter-2y6i's 20 mutants, a run of Simulation took 8,656 s: 2,576 s of coverage capture and 5,951 s of mutants. Most mutants of these spans are on hot paths, with 1,250 to 2,463 covering tests each. Such a mutant makes some Physics and Simulation tests run until Stryker's limit, about 335 s and 300 s, so it takes about 10 minutes. At concurrency 3, the same run took 3,094 s.

These changes can shorten the capture or the mutants:

- A Stryker.NET release that fixes issue #3832 lets the concurrency rise, and the capture spreads over the test hosts.
- Fewer test projects make the capture shorter. But the run then misses the kills of the tests in the projects that it leaves out.
- Faster cores make each test shorter. More cores do not help at concurrency 1.
- A smaller `additional-timeout` shortens each mutant that makes a test run to the limit. But the capture uses the same setting, and a test such as `DeterminismTests` needs 80 to 90 s alone there.

### A run on another machine

`tools/stryker/cloud.py` runs the official run on Runpod's CPU workers, one module per worker, through the cloud queue (tools/runpod/WORKBENCH.md, "Tests in the cloud"). Each worker has 2 vCPUs and 4 GB, and runs one module at concurrency 1. The modules run at once, so a full hardening takes the time of its slowest module, not the sum. On 2026-11-12, Core took 2,731 s for $0.056, Simulation 1,866 s for $0.039, and Physics 641 s for $0.0145. tools/runpod/README.md, "Cloud tests", gives the measurements.

On a worker, the 1-minute load and `os.cpu_count()` both read the shared host, with 192 processors. So the load rule above allows a load of 3,360 there, and in effect never applies. The timeout share rule still applies. A timeout on a worker counts as a kill, because its limit is 2 times the test's initial time on the same worker plus 300 s.

The official run needs these things on any other machine, and `cloud.py` gives them to each worker:

- A checkout of the commit to measure. The script reads only committed changes: it diffs from the merge base to `--head`, and it refuses a changed file that differs in the working tree.
- The dev shell. `changed.py` and `run.py` refuse to start without `TERTIUS_VULKAN_DIR`, although the CPU test projects use no GPU.
- One run at a time on that machine. The lock is a folder in `/tmp`, so it keeps apart only the runs of one machine.
- Nothing more for the priority and the thread bound. On Linux, `nice -n 20` gives 19, the lowest priority there, and `DOTNET_PROCESSOR_COUNT` works the same way. The process samples use `ps -A -o pid=,pgid=,ni=,pcpu=,command=`, which Linux and macOS both read.
- No other build of the test projects in that checkout while the run goes on.

Copy the output folder back. It holds `results.json`, each module's reports and its logs. `cloud.py` brings back each module's `results.json`, configuration, console output and process samples, because Stryker's log and report pass the 8 MB that a job's result carries.

### Stryker on one module by hand

The official command uses these three scripts. Call them yourself to mutate whole files, or to try other test projects. A run by hand is not an official run, and its rows do not go into a ticket's table.

1. Write a configuration for one source project. Name the test projects that exercise it:

   ```bash
   nix develop --command bash -c 'python3 tools/stryker/config.py --project Core --tests Core Simulation Physics --out /tmp/tertius-stryker/Core'
   ```

   By default, the configuration mutates only the lines that this branch changed, with the official settings. To mutate whole files, add `--files` with paths relative to the project folder, for example `--files Frames/FrameTree.cs`.

2. Run Stryker on that project. `run.py` takes the lock, runs Stryker at nice 20 with the xunit threads of the configuration's concurrency, and builds the test projects again afterwards:

   ```bash
   nix develop --command python3 -I tools/stryker/run.py Core /tmp/tertius-stryker/Core
   ```

3. Print the shared table of the mutants in the spans:

   ```bash
   nix develop --command bash -c 'python3 tools/stryker/survivors.py /tmp/tertius-stryker/Core/out/reports/mutation-report.json --config /tmp/tertius-stryker/Core/stryker-config.json --module Core'
   ```

## Stryker.NET pitfalls

These cost time on the vertical slice and on ter-hxca, so the scripts handle them:

- Stryker.NET 5.0.0 reads `coverage-analysis` only from a configuration file, not from the command line. Without `perTest`, every mutant runs every test, on the slow mutated assembly ("Run time").
- Stryker.NET matches a `mutate` pattern against the full path. A pattern without the `**/` prefix matches no file, and the run then has no mutants.
- In a pattern such as `**/Frames/FrameTree.cs{402..1704}`, the numbers are character offsets in the file, not line numbers.
- Stryker.NET leaves a mutant that does not compile out of the score. A method with a pattern variable in a condition, such as `x is { } box && ...`, often makes every mutant in the method fail to compile (error CS0165). On the vertical slice, the Core score was 100% of 155 mutants, but 52 more mutants never ran. Hand mutations of their lines found three gaps. The official run lists such lines as no-mutant rows.
- The progress bar shows only on a terminal. `run.py` passes the `-L` flag, which writes the log to `out/logs`, so that you can follow a run in the background.
- The MTP runner captures per-test coverage one test at a time, in a test host that it reuses. Each test gets a limit of 1.5 x its initial time + `additional-timeout`, at least 30 s. A test whose class fixture does the work, such as `DeterminismTests`, has an initial time near 0, but it took 80 to 90 s alone under the capture. With the default `additional-timeout` of 10 s, Stryker marked such tests Dubious.
- After such a timeout, Stryker 5.0.0 kills the test host and starts a new one. Then it waits 10 s for the coverage relay of the killed host on every later test of that runner, and it marks each of them Dubious. One slow test can make hundreds of Dubious tests, and every mutant then runs them all.
- Stryker.NET 5.0.0 under the MTP runner reports killed mutants as Survived at a concurrency above 1 ([issue #3832](https://github.com/stryker-mutator/stryker-net/issues/3832), open on 2026-11-01). The count of kills changes from run to run on unchanged code, but Killed + Survived stays the same. The reporter measured kill rates of 70.59% at concurrency 1, 38.45% at 2 and 27.10% at 4, against 70.80% on 4.16.0. `coverage-analysis` off does not help. So the official run uses concurrency 1, and a survivor of a run at a higher concurrency proves nothing.
- To raise the concurrency when a fixed release comes, run the official command once at concurrency 1 and at least twice at the higher concurrency, on the same spans. The fault is nondeterministic, so one equal run is not enough. Then compare the runs mutant by mutant:

  ```bash
  python3 -I tools/stryker/compare.py /tmp/c1/results.json /tmp/c2-a/results.json /tmp/c2-b/results.json
  ```

  An equal score is not enough, because other mutants can change places. Raise `CONCURRENCY` in `tools/stryker/config.py` only if the script finds no difference, with exit code 0. Use spans with at least 20 mutants and some survivors, for example ter-2y6i's Simulation spans: `changed.py '84d4a894b^1' <out> --head 84d4a894b --exclude World.NumericFrames.cs`.
- Stryker.NET starts each test host as `dotnet <tests>.dll --server`, with no place for an xunit option such as `--max-threads`. So `run.py` bounds the xunit threads through `DOTNET_PROCESSOR_COUNT`, which every host inherits. xunit's default thread count reads it: a plain `dotnet test` with the variable at 4 printed `[4 threads]`, against 10 without it. ter-hxca's calibration saw one host at nice 20 with that variable, at about 400% CPU during the mutants. Each run's `processes.log` records the nice value and the CPU share of every test host. The variable also sets the number of Jolt workers of a backend with the default `JoltBackendOptions.WorkerThreads` (ter-773q). A test that sets its own count keeps it. Most physics tests run one or two workers, and the determinism tests compare one worker with four. A Physics host with the variable at 4 still reaches about 570% CPU for a second at its start, from about 38 busy threads. Ticket ter-lbyp holds the evidence and the search for their cause.
- Stryker.NET 5.0.0 copies its mutated module assembly over the module assembly in the output of each test project (log: `Injected the mutated assembly file into`). It keeps the plain one as `<name>.dll.stryker-unchanged`, and it copies that back about 1 s before its `Time Elapsed` line. The backup file stays after the run. A run that stops early leaves the mutated assembly in place, so `run.py` builds the test projects again. A build during the run replaces the mutated assembly, and the later mutants then run the plain code. `run.py` finds this from the missing helper class `MutantControl` while the mutants run.

Stryker runs only through `tools/stryker/run.py`, under the lock `/tmp/tertius-stryker.lock`. Any agent, also a subagent, can run the official command, because `run.py` takes the lock itself. The user lifted the ban on Stryker in subagents on 2026-11-01, once a run was efficient. ter-hxca's official calibration at concurrency 1 showed all three conditions. Its log has one schemata build with 9 compile tries, and each failed try removed the mutants that broke it. It has one initial test run of 4,593 tests in 26 s and no Dubious test. Each mutant ran a mean of 4.3 of the 4,593 tests.

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

## The LTC table

`tools/Tertius.LtcFit` fits the LTC table of the rectangle lights to the renderer's own `GgxLobe` (ARCHITECTURE.md, "Rectangle lights"). It writes `src/Tertius.Rendering/Shading/LtcTable.bin`, 64 × 64 texels of four half floats, which the Rendering assembly embeds. Run it in the dev shell from the repository root:

```bash
nix develop --command dotnet run --project tools/Tertius.LtcFit -c Release -- src/Tertius.Rendering/Shading/LtcTable.bin
```

The fit takes about 6 s on 10 cores. It runs the rows in parallel, and each row's fit is deterministic, so a new run writes the same file. If you change `GgxLobe`, the roughness floor or the view cosine's floor, run the tool again and commit the new table. `LtcTableTests` fits rows 3, 32 and 63 again and fails when the stored table differs from them.

## FSR 3.1 golden values

`tools/fsr3-golden/` holds tool code for the FSR 3.1 host (`Fsr3Upscaler`, ARCHITECTURE.md, "Upscaling: FSR 3.1"). The dev shell fetches AMD's FidelityFX SDK at tag `v1.1.4` into the variable `TERTIUS_FFX_SDK` (`native/fidelityfx-sdk.nix`). Both scripts read AMD's code from there and never from the network.

`build.sh` compiles `golden.cpp` with AMD's unchanged host code, `ffx_fsr3upscaler.cpp` and its shared files, and writes `tests/Tertius.Rendering.Tests/Fsr3Golden.json`. Run it after a change to `golden.cpp` or to the pin:

```bash
nix develop --command bash tools/fsr3-golden/build.sh
```

- `golden.cpp` is a backend of AMD's `FfxInterface` that runs no GPU. It records the resources that AMD's host creates, the constant buffers that it stages, and the jobs that it schedules: the clears, the dispatches and the discards. It reports waves of 32 lanes and no 16-bit floats, so AMD picks the permutation without the Lanczos table.
- It prints AMD's jitter phase counts, AMD's jitter offsets, the layout of the constant buffer, and three sequences of frames with their inputs, constants, pyramid constants and jobs. The "budget" sequence, from 1920 × 1080, has resets, render size changes, odd frame times and changes of the pre-exposure. The "odd" sequence has odd sizes, and the "small" sequence the sizes of the GPU tests.
- `compat.h` gives the few Windows names that AMD's code uses. The build uses `-fshort-wchar`, because AMD's private context assumes the 2-byte `wchar_t` of Windows.
- `Fsr3HostTests` hold the file as an embedded resource and compare the host with it. A value from the math library, such as the tangent of the field of view, can differ by one unit in the last place on another C library. So those values compare within one unit.

`check-pin.sh` compares the pinned GLSL in `src/Tertius.Rendering/ThirdParty/Fsr3` with AMD's tag. It passes when the only differences are those in `pin.diff`, the two declarations and the port of AMD's 3.1.6 fix that the folder's README records:

```bash
nix develop --command bash tools/fsr3-golden/check-pin.sh
```

`Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTime` measures FSR's GPU time by timestamps around `Record`. It runs only with `TERTIUS_FSR_TIMING=1`. It prints the median and the percentiles of 300 frames, and then the time of each frame in their order. Run it in Release under the GPU lock (see "The GPU lock"). The test's output goes to the log:

```bash
nix develop --command dotnet build tests/Tertius.Rendering.Tests -c Release
mkdir -p captures
TERTIUS_FSR_TIMING=1 bash tools/gpu-lock.sh captures/fsr-timing-test.log nix develop --command dotnet test --project tests/Tertius.Rendering.Tests -c Release --no-build --filter-method "*ReportsItsGpuTime" --output detailed
```

## FSR 3.1 timing

The maximum-allocation measurement is `Fsr3UpscalerTests.TheMaximumWindowAllocationReportsItsGpuTimeAtEachActiveSize`, gated by `TERTIUS_FSR_TIMING=1`. It allocates 1920 × 1080 internally and outputs 1920 × 1080. Its active sizes are 960 × 536, 1280 × 720 and 1920 × 1080. The smallest size follows the per-axis rounding to eight pixels. It shares the existing batched timing procedure: 60 warm-up batches, 150 timed batches, eight frames per batch and two batches queued. Run it in Release under `tools/gpu-lock.sh` and `tools/gpu-clock.sh`. The normal test `TheLargestWindowAllocationHandlesTheControllersActiveSizesUnderValidation` checks the same size transitions with Vulkan validation. The older 1280 × 720 allocation measurement keeps its original entry point for existing timing tools.

`tools/fsr3-timing/` holds tool code that times builds of FSR's passes against each other (ticket `ter-7m38`, `src/Tertius.Rendering/ThirdParty/Fsr3/README.md`, "The 16-bit and subgroup builds"). It does not ship. The method runs each build once per round, in many rounds, so that the load of the machine falls on every build alike.

Each run is FSR's batched measurement, `Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTimePerFrameWhenEightFramesShareASubmission`, which runs only with `TERTIUS_FSR_TIMING=1`. It upscales from 1280 × 720 to 1920 × 1080, 8 frames per command buffer, so that the GPU does not idle between frames. First it runs 60 batches of warm-up, about 1 s on the development Mac, so that the GPU's governor reaches its clock under full load. Then it times 150 batches, about 2.4 s. A frame time is the span of a batch's timestamps over 8. Two batches wait on the queue at once, so that the GPU runs one batch while the CPU records the next. The upscaler of the measurement has 16 frames in flight, so that no frame writes a descriptor set that a command buffer on the queue binds.

The batched measurement holds the GPU clock from run to run, and the per-frame measurement does not. On 2026-11-04, 14 runs of the batched measurement from one build and its byte copy ran at 1295.9 to 1296.0 MHz over their timed spans. A comparison holds only at the same clock (see "The GPU clock of a timed run"). The per-frame measurement, `AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTime`, waits for each frame. So its clock moved from 886.6 to 1008.5 MHz over four runs of one build, and `analyze.py` refused nearly every round. It stays for the calibration rounds of `tools/gpu-calibration/`, which measure FSR with the GPU idle between frames.

1. If a build uses `FFX_HALF=1`, enable its two device features in `src/Tertius.Rendering/Vulkan/VulkanDevice.cs` for every build of the comparison. Add `ShaderFloat16 = true` to `EnabledVulkan12Features` and `ShaderInt16 = true` to `EnabledCoreFeatures`. The engine does not enable them, and the validation layer refuses a 16-bit module without them. Revert the change after the builds. Two fixtures of `DeviceFeatureTests` fail while it stands. The quad operations of the subgroup builds need no enabled feature.
2. Build each variant into its own copy of the Release test build under `captures/fsr-builds`. The arguments are `FFX_HALF` in AMD's four 16-bit passes, subgroup operations in the pyramids, and the name of the copy:

   ```bash
   nix develop --command bash tools/fsr3-timing/build-variant.sh 0 0 base
   nix develop --command bash tools/fsr3-timing/build-variant.sh 1 1 halfwave
   ```

3. Make the control, a byte copy of the engine's build. It shows the spread between two runs of the same code:

   ```bash
   cp -R captures/fsr-builds/base captures/fsr-builds/control
   ```

4. Run at least 5 rounds, more on a busy machine. Each run takes and releases the GPU lock through `tools/gpu-lock.sh` (see "The GPU lock"), and records `uptime` at its start. It runs beside `gpu-clock` through `tools/gpu-clock.sh`, so its log holds the clock over the measurement's 150 timed batches (see "The GPU clock of a timed run"). The log also holds the frame time of each batch, which `analyze.py` reads. The order of the builds rotates by one build per round:

   ```bash
   mkdir -p /private/tmp/fsr-timing
   for round in $(seq 0 59); do nix develop --command bash tools/fsr3-timing/time-round.sh $round /private/tmp/fsr-timing base control halfwave >> /private/tmp/fsr-timing/rounds.log; done
   ```

5. Compare the builds with the first build named as the reference:

   ```bash
   python3 -I tools/fsr3-timing/analyze.py /private/tmp/fsr-timing base control halfwave
   ```

`build-variant.sh` builds the test project, writes the variant's modules over the engine's in `obj`, and builds again. The modules are then newer than their sources, so the build embeds them without compiling them again. At the end, the script touches the csproj and builds again, so that `obj` and `bin` hold the engine's modules. The script prints the SPIR-V capabilities of each module.

`analyze.py` reads the frame time of each batch from the line `frame times, ms: ...` of each run's log, and the frames per batch from the line of the median. Below, a frame is the mean frame of one batch. A log without the two lines, such as the log of a per-frame run, stops the tool with a `ValueError` that names the log. The first line of the output counts the batches of each run and the frames of each batch. It prints two readings of "the build's median minus the reference's", each with the 90 % interval of 2,000 resamples of the rounds:

- The pooled frame median is the median of all frames of a build. A resample pools the frames of the rounds that it draws.
- The paired run median is the median over the rounds of the run median of the build minus the run median of the reference in the same round. A resample draws these per-round differences.

It prints the same two readings of the 10th percentile, and the least, median and most 1-minute load at the run starts. The frame times of FSR on the M1 Max have several modes, so the two readings can disagree, as they did in `ter-7m38`.

It also checks the clocks (see "The GPU clock of a timed run"):

- For each build, it prints the median of its runs' clocks with the 10th and 90th percentile of the runs, and the median of its run medians converted to the windowed clock, an estimate.
- A round in which a build's clock and the reference's clock differ by more than 3 % does not compare. The paired reading leaves such a round out, and a line counts the rounds at the same clock, at different clocks and unchecked.
- A round with an unknown clock stays in the paired reading, unchecked, so that the tool works where `gpu-clock` cannot run. The last column of the paired table counts these rounds.
- A build whose checked rounds all ran at different clocks gets no paired reading, even with unchecked rounds: the table says `refused`.
- The pooled reading mixes every round. The line above the tables marks it `INVALID` when the medians of the two builds' clocks differ by more than 3 %.

On 2026-11-04 (ticket `ter-q58r`), 7 rounds of the engine's build against its byte copy ran at a 1-minute load of 3.0 to 6.5. Every round ran at the same clock, and `analyze.py` found no difference. The paired median of the control minus the base was +0.001 ms, with a 90 % interval of −0.002 to +0.006 ms.

The two batches in flight keep the clock when another process holds the CPU. An earlier form of the measurement waited for each batch before it recorded the next. In 3 of its 24 runs, the GPU's busy share fell to a mean of 0.917 to 0.962 over the timed span, and the clock fell to between 791 and 1013 MHz at its lowest sample. The busy share is the share of the time in which the GPU is not off. Work from another process can only raise it. So the measuring process stopped feeding the GPU, most likely because other processes took the CPU between two batches. Then the GPU's governor lowered the clock. With two batches in flight, a stall of the CPU shorter than one batch, about 16 ms, leaves the GPU busy.

If a round still fails the check, compare the busy share of `gpu-clock`'s samples between the timed-start and timed-end markers of the two logs. A busy share below about 0.99 means that the measuring process did not feed the GPU. Look for CPU load from other processes, and run the round again.

`tools/test_fsr3_timing.py` tests the reading of the batches from the logs and the per-round decision of `analyze.py` on small synthetic out folders. It uses the Python standard library only:

```bash
nix develop --command python3 -I tools/test_fsr3_timing.py
```

The rotation gives each build the same predecessor in most runs. So an effect of one run on the next mixes with the effect of the build. A Williams design, an order that gives each build every predecessor equally often, removes this. The tool does not use one yet.

## The record of a machine lock

Two locks keep runs apart on this machine: the GPU lock of the timed runs, `/tmp/tertius-gpu-measure.lock`, and the Stryker lock, `/tmp/tertius-stryker.lock`. Each lock is a directory, and `mkdir` makes it for one process only. Both locks use one record format, one release rule and one liveness rule. `tools/gpu-lock.sh` holds them in bash, and `Lock` in `tools/stryker/run.py` holds them in Python.

The holder's record is the file `holder` in the lock directory. Right after its `mkdir`, the holder writes the record into `holder.new` and renames it to `holder`. A shared holder of the GPU lock writes the same record into a file of its own in the folder `shared` of the lock ("The shared mode"). The record holds four lines:

- `pid=`, the process id of the holder.
- `start=`, the start time of that process, as `TZ=UTC0 LC_ALL=C ps -o lstart= -p <pid>` gives it, with each run of spaces squeezed to one space. The fixed time zone and locale make every run write and compare the same text.
- `token=`, a value that no other run has.
- `label=`, what the run is. For the GPU lock, it is the log path and the command. For the Stryker lock, it is the run's folder. A newline in the label becomes a space, so that it cannot split the record.

A reader reads the record as the `read` builtin of bash gives it, so that both implementations read every file alike. It drops NUL bytes. A line ends only at a newline, and a last line without its newline is not part of the record. Each line splits at its first `=`, and a line without `=` is not part of the record. If a key comes twice, the later line wins. A record whose pid is not all ASCII digits, or that lacks its `pid=`, `start=` or `token=` value, counts as no record. If `ps` gives no start time for the holder itself, the holder removes its new lock and stops.

The release rule: a run removes the lock only when the record's token is its own. A removal first renames the lock to a name of the remover's own, and then deletes it. So no run sees a lock that lost its record because a removal stopped halfway.

The liveness rule: the holder of a record is gone in two cases only.

- `kill -0` of its pid fails with "No such process".
- `ps` gives a start time for its pid that is not empty and differs from the record's. Then a later process reuses the pid.

In every other case, the holder counts as alive. Two examples are a permission error of `kill -0`, which a process of another user gives, and an empty answer of `ps`. So no error makes a live holder look gone.

A lock is stale when its record names a holder that is gone. The two locks treat a stale lock differently, for this reason:

- The GPU lock has a queue, in which the waiters take turns in the order in which they came. Exactly one waiter, the oldest live one, can clear a stale lock and take it. So the clear is safe, and the next timed run does not wait for a person. In a shared lock, the oldest waiter removes the record of each shared holder that is gone.
- The Stryker lock has no queue, and its runs take hours. On a stale lock, a run stops and prints the command that removes the lock. A person makes sure that no Stryker run goes on and then removes the lock.

The same rules have two implementations, because the GPU helper must stay plain bash that runs outside the dev shell, and its waiter must not fork more often than before. A Python process for each check would break both. `TheSharedRecord` in `tools/test_gpu_lock.py` keeps the two in step. A record that one lock writes, the other lock judges as the writer does: alive, gone by pid, gone by start, an incomplete record, no record and a holder that `kill -0` cannot signal. The same test also gives both locks records at each edge of the format, such as a carriage return in the label, a last line without its newline, a NUL byte, a pid of other digits and a label that is not UTF-8.

## The GPU lock

Every timed run on the development Mac takes the GPU lock, the directory `/tmp/tertius-gpu-measure.lock`, so that two timed runs never share the GPU. `tools/gpu-lock.sh` is the one helper of the lock for every timing tool. Take the lock only through it, and never with `mkdir` or `rmdir` by hand:

```bash
bash tools/gpu-lock.sh <log> <command> [arguments]
```

It waits for the lock and runs the command with its output in the log. The log gets the uptime at the start and the end, and the seconds that the run waited. The exit code is the command's.

A run that measures no time, such as a still, a film for image metrics or a GPU test, takes the shared mode with `--shared` before the log. Call the main checkout's helper by its absolute path, so that an older copy in a worktree does not take the lock (see "The shared mode"):

```bash
bash /Users/alexispurslane/Development/tertius/tools/gpu-lock.sh --shared <log> <command> [arguments]
```

The helper takes the lock with `mkdir`, and right after it writes the holder's record ("The record of a machine lock"). The record's label is the log path and the command. A plain `rmdir` of a lock with a record fails, because the directory is not empty. On 2026-11-04 a command line ran `mkdir` of the lock and then `rmdir` after `;`. The `mkdir` failed, and the `rmdir` removed the lock of another run. The record now stops that `rmdir`. If the record cannot be written, or `ps` gives no start time, the helper removes its new lock, does not run the command and exits with 1.

The helper follows the release rule of every machine lock when the command ends, when it fails and when INT, TERM or HUP ends the helper. If the lock vanished, or the record of another run is in it, the helper removes nothing. It then writes one line into its log, which starts with `gpu-lock: the lock was not this run's at its release` and ends with `Discard this run's timing.` Discard the timing of such a run, because another run could share the GPU with it. During the take and the release, INT, TERM and HUP only mark the run, and the run ends after that step. So no signal leaves an empty lock or a renamed lock behind.

The stale rule: the oldest live waiter clears a stale lock and takes it. The liveness rule of every machine lock says when a holder is gone. A waiter asks `ps` at most once a second, because a fork every 0.1 s loads the CPU of the measured run. In the other seconds, a holder that `kill -0` finds counts as alive. A lock without a record is never stale: a run takes it at this moment, or the helper before ter-r88x took it.

The waiters take turns in the order in which they came. Each waiter writes a ticket into `/tmp/tertius-gpu-measure.queue`, a file named by its start time and its process id. The ticket holds the waiter's record, the same text that the lock gets when the waiter takes it. The liveness rule judges each ticket, so a ticket whose pid a later process reuses does not block the queue. The ticket of a helper before ter-s4fg is empty, and `kill -0` of the pid in its name alone judges it. A waiter tries `mkdir`, or clears a stale lock, only while its ticket is the oldest ticket of a live waiter, every 0.1 s. Without the order, a loop of runs that tried every 0.1 s won nearly every release against a waiter that tried every 5 s. On 2026-11-03 that went on for more than 20 minutes. The order also makes the clear safe when two waiters see the same stale lock. Only the oldest waiter clears, and only the oldest waiter takes the lock that the clear freed.

The helper before ter-r88x, as in `93e46dd7f`, makes an empty lock and does not clear. It and the current helper exclude each other: each one's `mkdir` fails while the other holds the lock. If such an older helper is the oldest waiter on a stale lock, no waiter clears the lock. The older helper waits until a person stops it.

Two cases need a person:

- A holder ended by `SIGKILL` between its `mkdir` and its record. Its empty lock stays, and every run waits.
- An older helper is the oldest waiter on a stale lock. The stale lock stays, and every run waits.

To unblock the waiters, do these steps:

1. List the helpers with `pgrep -fl gpu-lock.sh`.
2. List the children of each helper with `pgrep -lP <pid>`. A waiter has no child, or only `sleep`. A holder has its timed command.
3. If an older helper waits, stop it with `kill <pid>`. Its trap removes its ticket, and the next waiter clears the stale lock.
4. If the lock stays empty for a minute (`ls -A /tmp/tertius-gpu-measure.lock` prints nothing) and no helper has a timed command, remove the lock with `rmdir /tmp/tertius-gpu-measure.lock`.

This `rmdir` cannot remove a lock with a record, because such a lock is not empty.

### The shared mode

On 2026-11-11, five agents shared the GPU lock, and each run waited about an hour for it. Most takes were untimed: stills, films for image metrics, survey splits and GPU tests. A sample of `ioreg -r -d 1 -c IOAccelerator` every 2 s over such a take gave a mean GPU use of 10 %. So the exclusive lock mostly kept CPU work apart, such as process start, shader compiles, image writes and metric analysis. The shared mode lets such runs hold the GPU side by side. A timed run still holds it alone, because a run beside it changes its time.

The rules of the two modes are these:

- A run without `--shared` is exclusive. It holds the GPU alone. Every timed run is exclusive: `tools/gpu-trace.sh`, `tools/demo-timing.sh`, `tools/fsr3-timing/time-round.sh`, the scripts of `tools/gpu-calibration/` and the timing part of the renderer survey.
- Up to `TERTIUS_GPU_SHARED_CAP` shared runs hold the GPU at once, 3 by default. A cap that is not a whole number of at least 1 ends the run with exit code 2 before it waits.
- A shared run never holds the GPU beside an exclusive run.
- The queue stays first in, first out across both modes. Each run, shared or exclusive, takes the GPU only when its ticket is the oldest ticket of a live waiter. So a shared run that comes after a waiting exclusive run waits behind it, and a stream of shared runs cannot starve a timed run. The shared runs at the head of the queue start one after the other, about 0.1 s apart, until the cap is reached.
- The log of a shared run holds the line `mode: shared, with up to <cap> shared runs at once`.

A shared run uses the same lock directory. It writes no file `holder` into it. It writes its record into the file `shared/<ticket>` of the lock, through `shared/<ticket>.new`. The first shared run makes the lock and the folder `shared`. A later shared run adds its record while the folder holds fewer files than the cap. At its release, a shared run removes its own record, if the record holds its token. It then removes the folder `shared` with `rmdir`, which fails while the folder holds a record. Only the run whose `rmdir` removed the folder removes the lock, also with `rmdir`. So of several shared runs that end at once, exactly one removes the lock. If the record is not the run's own at the release, the run writes the line `gpu-lock: the lock was not this run's at its release` into its log, as an exclusive run does.

The stale rule covers shared holders too. The oldest waiter reads each record in `shared`, removes the record of each holder that is gone by the liveness rule, and removes the lock when no record stays. A record that lacks its pid, start or token is never removed. A waiter asks `ps` about each holder at most once a second.

The helpers before the shared mode, such as the one in `93e46dd7f` or `8fe7ac7cf`, still run in agents' worktrees. They work beside the current helper in this way:

- An older helper writes a ticket without a mode. The current helper does not read the mode of a ticket, so an older helper's ticket keeps its turn as an exclusive ticket does. No shared run goes past it.
- An older helper waits while shared runs hold the GPU, because its `mkdir` of the lock fails.
- An older helper does not know the folder `shared`, so it never clears a shared holder that is gone. If an older helper is the oldest waiter while a dead shared holder's record stays in the lock, every run waits. This is the one gap. Stop the older helper as the steps above say. Then the next waiter removes the dead record.
- An older helper has no `--shared`. It reads `--shared` as its log and runs the real log path as the command. So the run waits its turn, holds the GPU alone for a moment and ends with an error.

To close the gap and to get the shared mode, call the main checkout's helper by its absolute path, `/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh`, and never a copy in a worktree.

### GPU test runs

Every run of a GPU test project, `Tertius.Rendering.Tests` or `Tertius.Host.Tests`, holds the GPU lock in its shared mode from its first test to its last (ticket ter-zoof). So no timed run, which holds the lock alone, runs beside the GPU tests, wherever they start: the pre-commit hook, `tools/test-all.sh`, `tools/mutate.py` or a plain `dotnet test` in a worktree. Before, on 2026-11-12, other agents' suites loaded the Mac to 44 to 53 beside a timed run, and three of its eight traces were lost.

`GpuTestLock` in `tests/Tertius.Testing` does it. Each GPU test project names it as an xunit assembly fixture. It runs the main checkout's helper with `--shared`, and the helper's command marks the take with a file. The command holds the lock until the fixture's `Dispose` or until the test process is gone, so a killed test run frees the lock within 0.2 s. The log of each take goes to `/tmp/tertius-gpu-test-lock/` and keeps its `waited:` line. The fixture prints `Waiting for the GPU lock` once the take waits for more than 2 s.

These rules apply:

- The helper runs its command with `TERTIUS_GPU_LOCK_HELD` set to `exclusive` or `shared`. A test run that sees it takes nothing, because it would wait for its own parent's lock forever. So the FSR timing test still runs under an exclusive take, and a list of mutants under a shared take runs as before.
- A test run joins beside up to 15 other shared holders (`GpuTestLock.TestRunCap`, 16), because before ter-zoof test runs had no cap, and one Checked suite runs two GPU projects at once. A still keeps its own cap of 3: it joins only beside fewer than 3 holders, because each run checks its own cap.
- The helper needs bash 5, so run the tests in the dev shell, as every build and test command here does. The Mac's own `/bin/bash` 3.2 lacks `SRANDOM`, and the fixture then fails every test with the helper's error.
- `tools/mutate.py` takes no lock of its own, and needs none: each mutant's test run takes the shared lock. Do not wrap it in an exclusive take. It measures no time, so an exclusive take only makes the timed runs wait for it: on 2026-11-12 an agent waited 28 minutes for an exclusive take that only ran tests. A shared wrapper does no harm, because the test runs inside it see `TERTIUS_GPU_LOCK_HELD` and take nothing.
- A helper process that started before ter-zoof does not set `TERTIUS_GPU_LOCK_HELD`. A test run of a branch with ter-zoof under such a helper's exclusive take waits for that take forever. End the take, and run it again with the current helper.

The cost, measured on 2026-11-12: a take and a release of a free lock cost 0.08 s per test project. Over the 94 hours before, timed runs held the lock alone 36 % of the time, with holds of 1 minute at the median, 15 minutes at the 90th percentile and 91 minutes at most. So a suite that starts at a random moment waits about 5.6 minutes on average: 0.36 times the mean remaining hold of 15.5 minutes. A timed run in turn waits for the GPU test runs ahead of it in the queue, up to the 18 minutes of the Rendering project.

### Variables and tests

Three environment variables give other values, for the tests. Their defaults are the real values:

| Variable | Default |
|---|---|
| `TERTIUS_GPU_LOCK` | `/tmp/tertius-gpu-measure.lock` |
| `TERTIUS_GPU_QUEUE` | `/tmp/tertius-gpu-measure.queue` |
| `TERTIUS_GPU_SHARED_CAP` | `3` |

`tools/test_gpu_lock.py` tests the record, the release rule, the stale rule, the queue's tickets, the queue's race on a stale lock, the shared mode, the order across both modes, the exclusion with the helpers of `93e46dd7f` and `8fe7ac7cf` and the record that the helper shares with the Stryker lock. Scripts named `ps` or `mv` in front of `PATH` open the short windows of the take, the release and the clear, so that a test can send a signal or change the lock inside them. Each test uses a new folder under `/private/tmp`, and no test touches the real GPU lock, its queue or the real Stryker lock. It uses the Python standard library only, and it runs in about 150 s:

```bash
nix develop --command python3 -I tools/test_gpu_lock.py
```

Every timing tool takes the lock through the helper: `tools/gpu-trace.sh`, `tools/demo-timing.sh`, `tools/fsr3-timing/time-round.sh` and the scripts of `tools/gpu-calibration/`. The renderer survey takes it in the shared mode for every render that is not timed. No tool holds a copy of the lock's logic.

## The GPU clock of a timed run

The GPU's clock on the development Mac moves with the GPU's duty cycle, from 648 to 1296 MHz in our runs (docs/research/temporal-renderer/gpu-calibration.md, "The clock under our runs"). A pass whose time follows the clock takes a time inversely proportional to the clock. So every timing tool records the clock over its timed span beside its times, and a comparison of two runs holds only at the same clock.

The timed span is the part of a run that the measurement times, without its setup. The setup runs at a low clock: FSR's batched measurement ran at 1208 MHz over the whole run and at 1257 MHz over its timed batches. Three parts record the clock of the span:

1. The timed work prints the line `timed-start` before its timed work and `timed-end` after it. A headless demo run prints `timed-start` when its first shown frame has finished, and `timed-end` when the script's steps end. FSR's timing test, FSR's batched measurement, the Vulkan probes, `probes.swift` and `metalfx.swift` print them around their timed frames or dispatches.
2. `tools/gpu-clock.sh` runs the work beside `gpu-clock`, which samples the clock every 100 ms, so the log holds the samples and the markers in the order in which they happened. At the end it prints the line `GPU clock over the timed span: ...`. It is the one launcher of `gpu-clock` for every tool:

   ```bash
   bash tools/gpu-clock.sh <command> [arguments]
   ```

3. `tools/gpu_clock.py` reads the log. `span_clock` averages the samples between each `timed-start` and the next `timed-end`. A span that no `timed-end` closes, as in a run that failed inside it, does not count. Each sample weighs by the share of its 100 ms in which the GPU was not off. A sample covers the 100 ms before its line, so the first sample of a span holds part of the work before it. The window summary of `tools/gpu-calibration/analyze.py` uses the same parser of samples and the same mean, `samples` and `clock_of`.

The record has the span's mean clock in MHz, its spread as the 10th and 90th percentile of the samples' clocks, and the number of samples. A sample in which the GPU was off does not count. A headless demo run keeps the record in its `gpu-timings.json` as the entry `gpuClock`:

```json
"gpuClock": { "meanMhz": 1035.9, "p10Mhz": 743.0, "p90Mhz": 1294.0, "samples": 17 }
```

The clock is unknown in four cases, and the record then says why, as `{ "unknown": "<reason>" }` or as `unknown (<reason>)` in the printed line. The tool still runs the work and reports its times:

- The system is not macOS, such as Linux. `gpu-clock` reads IOReport, which only macOS has.
- `gpu-clock` is not built in `$GPU_CALIBRATION_BIN` (see "GPU calibration").
- IOReport gives no GPU clock, such as on another machine. `gpu-clock` then prints `clock unknown: <reason>` and runs the command alone.
- The log has no marker or no sample in the timed span, such as a span shorter than 100 ms.

### Comparing two runs

Two runs compare when the higher clock of their spans exceeds the lower one by at most 3 %. A pass whose time follows the clock then differs by at most 3 % because of the clock. The tolerance comes from the calibration runs of 2026-11-03 (`captures/3ug8/upscalers-2` of ter-3ug8):

- Five runs of one build of FSR's batched measurement ran at 1241.0 to 1272.4 MHz over their timed spans, 2.5 % apart. Their medians did not follow those clocks: the run at 1272.4 MHz was the slowest. So 3 % holds the spread of one build at a held clock.
- Seven headless runs of the deck from one build ran at 748 to 990 MHz. Their frame span fell from 11.80 to 9.61 ms as the clock rose. A difference of that size is the clock, not the build.

A comparison at different clocks prints `INVALID` with both clocks and their difference. Its runs do not compare: run them again, for example in interleaved rounds, until a pair falls within the tolerance. A comparison with an unknown clock prints `UNCHECKED`. Each tool also prints each time converted to the windowed clock, 1296 MHz, as an estimate, by the report's rule:

```text
time at the windowed clock = measured time × the span's clock / 1296 MHz
```

The rule is an estimate within about 15 %. It holds best for a pass bound by its ALU work or its cached reads, and a pass bound by DRAM bandwidth gains less (gpu-calibration.md, "The rule that converts a headless time to the windowed clock"). Use an estimate to hold a time against a budget, such as those of docs/plans/temporal-renderer.md, section 11. Near a budget's limit, measure at the windowed clock.

Do not compare two builds by their estimates. The estimates of one build spread by about 8 % between runs at different clocks:

- Four FSR timing runs of one build and its byte copy on 2026-11-04 gave estimates of 1.893 to 2.058 ms, 8.7 % apart. Their raw medians were 5.1 % apart. In one round, the estimate reversed the sign of the raw difference.
- The seven headless deck runs of one build in ter-3ug8 gave estimates of 6.81 to 7.34 ms, 7.8 % apart.

So a difference between two builds' estimates below about 8 % says nothing about their cost.

### A timed demo run

`tools/demo-timing.sh` builds the demo in Release and runs the built binary headless with `--video none`, under the lock and beside `gpu-clock`. It writes the run's output to `<out>/run.log` and the record into `<out>/gpu-timings.json`, and it prints the GPU lines of the run, the clock last:

```bash
nix develop --command bash tools/demo-timing.sh captures/gpu-branch scripts/demo-exit.json --size 1920x1080
```

A GPU run is an inclusive range of consecutive recorded passes. Add repeatable `--gpu-run "FIRST..LAST"` options to request their sums:

```bash
nix develop --command bash tools/demo-timing.sh captures/gpu-branch scripts/demo-exit.json --size 1920x1080 \
  --gpu-run "bloom..capture" --gpu-run "shadow maps..direct light"
```

Use exact pass names from `gpu-timings.json`, including case and spaces. The range includes both endpoints and every actual pass between them in each frame. It can overlap another range. Equal endpoints select one pass. Outer whitespace around each endpoint is trimmed. The command rejects malformed ranges, unknown names, reversed ranges, and endpoints missing from any recorded frame. An empty recorded pass still counts. A skipped interior pass has no sample. A missing endpoint never becomes zero, and no frame is silently dropped. A script without rendered frames cannot report requested ranges.

Each frame contributes one range sum in milliseconds. The report then takes the mean, median and nearest-rank p95 across those sums. It never adds pass medians or pass p95 values. The frame span remains the measured GPU time from the first timestamp to the last, including gaps between passes.

On a driver whose GPU timestamps latch late, such as MoltenVK, the console hides individual pass times. It prints the frame span and only the requested range sums. Both the terminal output of `tools/demo-timing.sh` and the full run log explain why individual pass times are omitted. The wrapper reads the existing `warning` from `gpu-timings.json`; it keeps no separate warning message or driver check. Without `--gpu-run`, the frame span is the only timing line before the GPU clock, alongside that explanation. Other drivers keep their existing individual pass lines and add requested range sums.

`gpu-timings.json` preserves `device`, `width`, `height`, `frame` and raw `passes` with their existing fields. Added `runs` entries hold `first`, `last` and `timing`. The `timing` object holds `frames`, `meanMs`, `medianMs` and `p95Ms`. Added `stampsLatchLate` and `warning` describe whether raw individual pass values are valid alone. When `stampsLatchLate` is true, read the warning before using those values. A range request does not change which frames the run records.

`tools/gpu_clock.py compare` compares two such runs. It prints the check of the two clocks, then the mean frame span and requested range means, with their difference and their estimates at the windowed clock. It uses the range means already calculated from per-frame sums; it never adds individual-pass statistics. A range matches by both endpoint names. A range present in only one report has empty cells for the other report and the difference, just as a pass present in only one report does.

Individual-pass comparisons appear only when neither report has `stampsLatchLate: true` or a nonempty `warning`. If either report identifies unreliable individual times, the comparison omits all individual-pass rows, including for a comparison with an ordinary driver. It retains frame and range rows, explains the omission and shows the reports' warnings. No driver name detection is added. Legacy reports without these fields keep their individual-pass comparisons; reports without `runs` still compare normally:

A headless run writes a GPU timestamp after each pass, so the timing tools and the trace get each pass's time. A window writes one stamp after each cost group of the render scale, five per frame (ARCHITECTURE.md, "GPU timings"). On MoltenVK each stamp is an encoder of its own and costs GPU time. To time a run with the window's stamps, add `--pass-timestamps off`. Its `gpu-timings.json` then names the four cost groups, such as `at the render size`, in place of the passes. Compare the frame spans of two runs with `on` and `off` to measure what the stamps cost.

A headless run waits for each captured frame, so its GPU idles between frames. At 1280 × 720, the deck then runs far below the top clock. The demo option `--in-flight 2` sets `FramesInFlight` to 2 and keeps captures in flight. `--in-flight 1` sets one frame and waits for its capture. Without the option, both defaults stay unchanged. Two frames keep work on the GPU, as a window does, and the film's images and exposures stay the same. Make the 120 frames of the deck from frame 399 with `gpu-trace.py trim` (see "GPU calibration"), and time them:

```bash
nix develop --command python3 -I tools/gpu-trace.py trim scripts/demo-exit.json 399 120 /private/tmp/deck-120.json
nix develop --command bash tools/demo-timing.sh captures/deck /private/tmp/deck-120.json --size 1280x720 --in-flight 2
```

On 2026-11-04 (ter-gayt), one run that waited for each frame ran at 683.0 MHz, with a mean frame span of 13.011 ms. Three runs with `--in-flight 2` ran at 1293.2, 1276.0 and 1275.4 MHz, all within the tolerance of the top clock (see "Comparing two runs"). Their mean frame spans were 7.769, 7.765 and 7.759 ms.

```bash
python3 -I tools/gpu_clock.py compare captures/gpu-base/gpu-timings.json captures/gpu-branch/gpu-timings.json
```

`python3 -I tools/gpu_clock.py record <log> <gpu-timings.json>` writes the record of any log into a `gpu-timings.json`, for a demo run that a tool other than `demo-timing.sh` ran beside `gpu-clock`. A `gpu-timings.json` without the entry has an unknown clock.

### What each tool records

| Tool | The timed span | Where the record goes | Comparison |
|---|---|---|---|
| `tools/demo-timing.sh` | The demo's frames after the first | `gpu-timings.json` and the printed lines | `tools/gpu_clock.py compare` |
| `tools/gpu-trace.sh` | The demo's frames after the first | `run/gpu-timings.json`, `passes.md` and `passes.json` | None: the paired mode compares frames of one trace |
| `tools/fsr3-timing/time-round.sh` | FSR's 150 timed batches | Each run's log and its line in `rounds.log` | `tools/fsr3-timing/analyze.py` checks each round |
| `tools/gpu-calibration/rounds.sh` | Each probe's timed dispatches, or the demo's or FSR's timed frames | Each run's log | `analyze.py rounds` prints the clock of each run kind |
| `tools/gpu-calibration/upscalers.sh` | Each upscaler's timed frames or batches | Each run's log | `analyze.py upscalers` prints the clock of each kind |
| `tools/gpu-calibration/fsr-trace.sh` | FSR's timed batches | `run.log` and `passes.md` | None |

`tools/gpu-calibration/window.sh` times no span. Its clock is the windowed clock over the steady span of a window run (see "GPU calibration"), and the launcher's line says that the clock of the timed span is unknown.

`tools/test_gpu_clock.py` tests `tools/gpu_clock.py` on synthetic logs: the average between the markers, the unknown cases, the tolerance and the conversion. It also runs `tools/gpu-clock.sh` without `gpu-clock`, for its unknown case and its exit code. It uses the Python standard library only:

```bash
nix develop --command python3 -I tools/test_gpu_clock.py
```

## The screen walk's buffer reads

After each change to `walkScreen`, dump the final ray kernels with `MVK_CONFIG_SHADER_DUMP_DIR` set to a fresh directory. Run the renderer inside the Nix dev shell. Keep both the block and own ray kernels, for both ray kinds. MoltenVK names the dumps by hash and strips the source names. Match each dumped `.spv` byte for byte with `screen_trace_block_rays.comp.spv` or `screen_trace_own_rays.comp.spv` in the engine's build output, then inspect its sibling `.metal` file. Both ray kinds use each kernel, selected by specialization constant 5.

`tools/sol-walk/loop-reads.py` counts device-buffer accesses inside the unbounded walk loop in a Metal dump. The count must be 0 (temporal-renderer.md, decision 44). Texture fetches and the frame uniform do not count as per-ray buffer inputs. Inspect the file's `device` parameters first. The regex below matches the device-buffer access syntax in MoltenVK's current dumps:

```sh
nix develop --command python3 -I tools/sol-walk/loop-reads.py /private/tmp/trace-msl/shader-cs-HASH.metal 'm_[0-9]+\)\._m0\['
```

The directory above is an example, not a shared output directory. Check the actual `device` buffer names in each dump. The array-access regex above is not enough for scalar members such as exposure. Also run with a regex for every device pointer, such as `m_(756|3879|3887)\)`, using that dump's names. An own-ray list read in its outer stride loop is one read per ray, not one read per walk step. The tool recognizes unbounded `for (;;)` and `while` loops, not structured `for (init; condition; step)` loops. Inspect that outer loop separately. A zero count from an older kernel does not check a changed kernel.

## GPU calibration

`tools/gpu-calibration/` holds tool code that measures what the development Mac's GPU can do, apart from the engine's passes. Ticket `ter-3ug8` wrote it, and `docs/research/temporal-renderer/gpu-calibration.md` holds its results. It does not ship, and no engine code depends on it. It runs only on macOS.

| File | What it does |
|---|---|
| `probes.swift` | The native Metal probes: FMAs in an unrolled and a rolled loop, cached and streamed texture samples, and buffer reads. Each checks its result against the CPU. It also times the FMA probe cold, after a sustained load and as a short pulse every 20 ms. `fma-variants` times seven variants of chains and steps per pass. |
| `metalfx.swift` | Times Apple's MetalFX temporal upscaler from 1280 × 720 to 1920 × 1080 on the inputs of FSR's timing test. Each frame has its own command buffer, with an optional gap after it. |
| `gpu-clock.c` | Samples the GPU's performance states through IOReport while a command runs, and prints the mean clock over the active time. It needs no root. Where IOReport gives no GPU clock, it prints `clock unknown: <reason>` and runs the command alone. The tools start it through `tools/gpu-clock.sh`. |
| `kernel-code.swift` | Serializes each kernel of a Metal source file into a binary archive with this GPU's native code. |
| `count.sh` | Counts the FMAs, samples and loads of each probe in SPIR-V, in AIR and in the size of the native code. |
| `rounds.sh` | One round of the calibration runs in a rotated order, each under `tools/gpu-lock.sh` and beside `gpu-clock` through `tools/gpu-clock.sh`. |
| `upscalers.sh` | One round of the upscaler runs in a rotated order: FSR's timing test, FSR's batched measurement, and MetalFX without and with a gap, each under the lock and beside `gpu-clock`. |
| `window.sh` | One run of the Release demo in a window, under the lock and beside `gpu-clock`. It records the GPU's busy share and MoltenVK's log of the frame rate, and prints one summary line from `analyze.py window`. |
| `window-deck.json`, `window-lamp-hall-16.json` | The scripts of the window runs: the deck camera of demo-exit for 1,119 ticks, and lamp-hall-16 for 1,080 ticks, each with one capture at the end. |
| `fsr-trace.sh` | A Metal System Trace of FSR's batched measurement under the lock and beside `gpu-clock`, then `fsr-passes.py` on it. |
| `fsr-passes.py` | The GPU time of each of FSR's passes from the trace. It skips the warm-up batches, whose count it reads from the measurement's line `warm-up batches: N` in `run.log`. It keeps the frames that no other process overlapped, and gives the times of every kept frame and of the frames at the top clock. |
| `work.py` | The counted work of compute and fragment shaders from MoltenVK's MSL: lane operations, texture reads and the loops left after an unroll. `work.py fsr` adds the roofline of each of FSR's passes against its time from `fsr-passes.py`. |
| `analyze.py` | The tables: `rounds` for the rounds, `upscalers` for the upscaler runs, `windows` and `window` for the window runs, and `probes` for any folder of probe logs. |

The Vulkan twins of the probes are `GpuCalibrationTests` in `tests/Tertius.Rendering.Tests`. Their checks against the CPU run in every test run. Their measurements run only with `TERTIUS_GPU_CALIBRATION=1`, as FSR's two measurements in `Fsr3UpscalerTests`, per frame and in batches, run only with `TERTIUS_FSR_TIMING=1`. Every measurement of the rounds prints the lines `timed-start` and `timed-end` around its timed work, and runs beside `gpu-clock` through `tools/gpu-clock.sh`. `analyze.py upscalers` and `analyze.py rounds` then give `gpu-clock`'s clock over that span beside the clock over the whole run, because the whole run holds the setup at 648 MHz (see "The GPU clock of a timed run"). The rebuilt `probes` and `metalfx` print the markers. A binary built before 2026-11-04 does not, and its span clock is unknown.

`tools/test_gpu_calibration.py` tests the summaries of `analyze.py`, the frames of `fsr-passes.py` and the counts of `work.py` on synthetic logs and AIR. `tools/test_gpu_clock.py` tests the clock of the timed span. It uses the Python standard library only:

```bash
nix develop --command python3 -I tools/test_gpu_calibration.py
```

The tools' inputs live in one folder, `captures/gpu-calibration`, which git ignores. Its `bin/` holds the native binaries, and the scripts read it from `GPU_CALIBRATION_BIN`, which defaults to `captures/gpu-calibration/bin`. The native tools use Xcode's command line tools outside the dev shell, because the dev shell hides Xcode's Metal toolchain from `xcrun`. `count.sh` and `work.py` also need the Metal toolchain component (`xcodebuild -downloadComponent MetalToolchain`). Build them from the worktree root:

```bash
mkdir -p captures/gpu-calibration/bin
xcrun swiftc -O -o captures/gpu-calibration/bin/probes tools/gpu-calibration/probes.swift -framework Metal
xcrun swiftc -O -o captures/gpu-calibration/bin/metalfx tools/gpu-calibration/metalfx.swift -framework Metal -framework MetalFX
xcrun swiftc -O -o captures/gpu-calibration/bin/kernel-code tools/gpu-calibration/kernel-code.swift -framework Metal
xcrun clang -O2 -o captures/gpu-calibration/bin/gpu-clock tools/gpu-calibration/gpu-clock.c -framework CoreFoundation -framework IOKit -lIOReport
```

Then build the Release test project and demo, and the 120 frames of demo-exit's deck, in the dev shell:

```bash
nix develop --command bash -c 'dotnet build tests/Tertius.Rendering.Tests -c Release && dotnet build src/Tertius.Demo -c Release'
nix develop --command python3 -I tools/gpu-trace.py trim scripts/demo-exit.json 399 120 captures/gpu-calibration/deck-120.json
```

Run at least 5 rounds, then the tables:

```bash
for round in $(seq 0 6); do nix develop --command bash tools/gpu-calibration/rounds.sh $round captures/gpu-calibration/rounds; done
python3 -I tools/gpu-calibration/analyze.py rounds captures/gpu-calibration/rounds
for round in $(seq 0 4); do nix develop --command bash tools/gpu-calibration/upscalers.sh $round captures/gpu-calibration/upscalers; done
python3 -I tools/gpu-calibration/analyze.py upscalers captures/gpu-calibration/upscalers
```

To time the variants of the FMA probe, run them under the lock beside `gpu-clock`, then pool the rounds:

```bash
for round in $(seq 0 6); do bash tools/gpu-lock.sh captures/gpu-calibration/variants/$round-variants.log bash tools/gpu-clock.sh captures/gpu-calibration/bin/probes fma-variants 30; done
python3 -I tools/gpu-calibration/analyze.py probes captures/gpu-calibration/variants
```

To count the work in each probe, dump MoltenVK's MSL from a run of the Vulkan checks, then count outside the dev shell:

```bash
nix develop --command bash -c 'MVK_CONFIG_SHADER_DUMP_DIR=$PWD/captures/gpu-calibration/mvk-dump tests/Tertius.Rendering.Tests/bin/Release/net11.0/Tertius.Rendering.Tests -class Tertius.Rendering.Tests.GpuCalibrationTests'
bash tools/gpu-calibration/count.sh captures/gpu-calibration/mvk-dump captures/gpu-calibration/counts
```

To hold each of FSR's passes against its roofline, follow these steps. Trace FSR's batched measurement three times. Dump the MSL of FSR's shaders. Count outside the dev shell:

```bash
for trace in 0 1 2; do nix develop --command bash tools/gpu-calibration/fsr-trace.sh captures/gpu-calibration/fsr-trace/$trace; done
nix develop --command bash -c 'MVK_CONFIG_SHADER_DUMP_DIR=$PWD/captures/gpu-calibration/fsr-dump TERTIUS_FSR_TIMING=1 tests/Tertius.Rendering.Tests/bin/Release/net11.0/Tertius.Rendering.Tests -method Tertius.Rendering.Tests.Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTimePerFrameWhenEightFramesShareASubmission'
python3 -I tools/gpu-calibration/work.py fsr captures/gpu-calibration/fsr-dump captures/gpu-calibration/fsr-counts src/Tertius.Rendering/obj/Release/net11.0/shaders captures/gpu-calibration/fsr-trace/0 captures/gpu-calibration/fsr-trace/1 captures/gpu-calibration/fsr-trace/2
```

`work.py count <dump> <out>` counts any MSL dump, such as one from a headless demo run with `MVK_CONFIG_SHADER_DUMP_DIR`. Match a dumped `.spv` to its engine shader by its hash against `src/Tertius.Rendering/obj/Release/net11.0/shaders`.

To measure the windowed clock, run the demo in a window. Each run opens a window on the display for about 20 s. The window sizes of the report are `1920x1080` and `1080x1920`. If a window does not fit on the display, macOS makes it smaller:

```bash
nix develop --command bash tools/gpu-calibration/window.sh tools/gpu-calibration/window-deck.json 1080x1920 captures/gpu-calibration/window 1-deck-portrait
python3 -I tools/gpu-calibration/analyze.py windows captures/gpu-calibration/window
```

`bash tools/gpu-clock.sh <command>` runs `gpu-clock` beside any run, such as a timed demo run. Its clock is the mean over the time that the GPU was not off, which includes short idle times between command buffers.

## GPU trace

`tools/gpu-trace.sh` records a Metal System Trace of a headless demo run, and writes a table of each pass's GPU time and limiters. It runs only on macOS, with Xcode's `xctrace` (27.0 on the development Mac). No engine code depends on it: the GPU timings of a headless run (ARCHITECTURE.md, "GPU timings") work on every Vulkan driver, and this script adds why a pass takes its time.

```bash
nix develop --command bash -c 'bash tools/gpu-trace.sh --start 399 --frames 120 scripts/demo-exit.json'
```

The script builds the demo in Release, and traces the built binary, so the trace holds no build. The recording takes the GPU lock (see "The GPU lock") and runs beside `gpu-clock` (see "The GPU clock of a timed run"). The first lines of `passes.md` give the clock over the run's timed span. xctrace launches the demo itself, because a process between them leaves the trace's list of encoders empty. So `gpu-clock` samples outside xctrace, and xctrace passes the demo's output on to `run.log`. It keeps the frames from `--start` (counted from 0 over the script's captures and film frames, default 0) for `--frames` frames (default 120, and 0 keeps every frame to the end of the script). The frames before `--start` become run steps of the same ticks, so the world reaches the same state without a render. Frame 399 is the first frame of demo-exit's deck camera. The size is 1920x1080 unless `--size` gives another. `--window FROM TO` adds the window row of the table (see below). `--paired PASS` and `--control PASS` add the paired table (see "The paired mode" below). Arguments after the script go to the demo's headless run, such as `--screen-trace off` (ARCHITECTURE.md, "Host and scripted runs"). The output goes to `captures/gpu-trace/<script>-<start>`, which git ignores, or to `--out <dir>`:

- `run.trace`: the trace. Open it in Instruments to see the timeline and the counters.
- `run/`: the run's `gpu-timings.json`, with the clock over the run's timed span as `gpuClock`, and `exposure.json`. The run passes `--video none`, so it draws and times each frame as the cost recipe does (docs/plans/rendering-upgrade.md, "Measuring the cost of a feature") and writes no image.
- `run.log`: the uptime at the start and the end of the recording, xctrace's messages, `gpu-clock`'s samples and the demo's printed lines.
- `passes.md`: the table, which the script also prints. `passes.json` holds every counter of every pass, and the summary of each pass's shared time.

The table has one row per pass, a row for the encoders before the first pass (the uploads and the exposure's readback copy), a row for those after the last pass, and the frame's span. For each, it gives the frames, the mean, median and 95th percentile of the busy time and of the time alone in milliseconds, the two highest limiters, and the read and write bandwidth in GB/s. A limiter is the share of the time in which one GPU unit was busy or stalled, such as the ALU, texture sampling or the last-level cache. The highest limiter is the unit that bounds the pass. A line of `passes.md` gives the load averages when the recording started. `gpu-trace.py report --log` reads them from the line `uptime-start` that `tools/gpu-lock.sh` writes into `run.log` after any wait for the lock. Measure on a quiet machine: the counters sample the whole GPU, not one process.

How the script reads the trace:

- The renderer runs on MoltenVK, so each Vulkan pass shows as one or more Metal encoders. MoltenVK names the encoders after the Vulkan command, such as "vkCmdBeginRendering RenderEncoder", not after the pass. A headless run turns on no validation, so the renderer sets no debug labels.
- MoltenVK writes each timestamp of the renderer's GPU timer as an encoder of its own, "Record GPU Counter Sample BlitEncoder". The timer writes one before the first pass and one after each pass. So in a frame with one more such encoder than passes, the encoders between two of them belong to one pass. The pass names come from the run's `gpu-timings.json`, in recording order. The script names no pass in a frame of the encoder list with another number of stamps.
- A frame can take more than one command buffer: the renderer submits the output's commands in a second command buffer (`QueueSubmission.Submit`), which holds the last stamps. The frame's shape is the stamps of each of its command buffers in order, 25 and 3 with FSR 3.1 at the time of ticket `t2-rhja`. The script takes the shape from the encoder list: the most common run of consecutive command buffers with stamps whose stamps add up to one more than the passes. A frame of the encoder list is then a run of command buffers that follows the shape. So the second command buffer of a frame whose first the trace lost does not join the next frame's first, though their stamps also add up to one more than the passes. Before ticket `t2-rhja`, the report of a renderer with FSR 3.1 stopped with "No command buffer of Tertius.Demo holds 28 timestamp encoders".
- A frame of the trace comes from the table of GPU intervals, which the script reads apart from the encoder list. A command buffer with stamps there is one whose GPU intervals hold a timestamp encoder or an encoder of a pass. The renderer writes stamps in each frame's command buffers and in no other. A command buffer without stamps, such as an upload at the start, is not a frame. In the order of their first GPU start, each run of as many command buffers with stamps as the shape has makes one frame. The script counts the frames from 0 in that order.
- The trace can lose a frame's rows in its encoder list, or list a frame with another number of stamps. Then the script cannot name the frame's passes. The table of intervals still labels each interval with its encoder, so the script still finds the frame. Such a frame keeps its index and gives no values, and a line below the table names it. 6 of the 32 kept traces of ter-bgpv, ter-0y0z and ter-r5hb hold 1 to 5 such frames. Before ter-svcs, the script counted only the frames that it read. So each frame after a lost frame took the index of the frame before it (see "Reading the control" below).
- The table's first line counts the frames, also those that the script cannot read. It also counts the other command buffers: those of the encoder list that are not frames, such as an upload. Before ter-svcs, the first line counted only the frames that the script could read, and it counted a frame with another number of stamps as another command buffer.
- A pass's GPU time, its busy time, is the union of its encoders' intervals (table `metal-gpu-intervals`), so the overlap of its own vertex and fragment work counts once. The frame span row is the time from the start of the first pass's first encoder to the end of the last pass's last encoder, and it agrees with the GPU timer's span to about 1 %. The pass times do not agree with the timer's, and on this Mac each pass's own time is the trace's. The Apple GPU starts an encoder before the one before it ends, so the encoders of two passes can overlap, and the sum of the passes' times can be more than the span. The timer's pass times in `gpu-timings.json` hold only for the span and for the sum of a pass and the next one: a stamp's blit encoder starts beside the next pass's first encoder, which can hold it back by up to about 1 ms, so a pass's time can hold part of the next pass's work (ARCHITECTURE.md, "GPU timings"). On 120 frames of demo-exit's deck (2026-10-26), the occlusion upsample took 1.459 ms by the timer and 0.300 ms in the trace, and the lighting after it 8.770 ms and 9.906 ms, while their sums agree within 0.03 ms. The timer also counts the idle time before a pass and between its encoders, which the trace leaves out. So a profile of a pass reads the trace's time, and the frame's budget reads the timer's span. The table says so above its rows.
- A pass's time alone is the part of its busy time in which no other encoder that the table counts runs. The table counts the encoders of the passes and those before the first pass and after the last pass, in every frame. It does not count the timestamp encoders, the encoders of the other command buffers, or the encoders of a frame that the script cannot read, so the time alone can overlap them. The script cuts the timeline at every start and end of an interval, and adds each segment in which only the pass is open.
- The busy time minus the time alone is the time that the pass shares with other passes. In that time, the GPU runs the pass beside other work, so the busy time can be more than the pass costs the frame. The means in the table subtract: the mean busy time minus the mean time alone is the mean shared time. For example, in mars-air's view above (ter-0y0z), the haze pass had a mean busy time of 0.173 ms and a mean time alone of 0.034 ms, so it shared 0.139 ms. The exposure pass's histogram clear runs beside it. The medians and the 95th percentiles do not subtract, because each one comes from its own frame. `passes.json` gives them for the shared time of each frame, as `"shared"` beside `"alone"`: the haze's median shared time was 0.141 ms.
- `--window FROM TO` adds a row after the frame span, with the window of each frame in milliseconds. The window is the wall-clock GPU time from the end of pass FROM's last interval to the next start of pass TO. That start is the first start of an interval of TO at or after the end of FROM. It holds all the GPU work between the two passes and the idle time, so it is what the frame pays for that part. The frame span row and the window row put a wall-clock time in the busy columns. A frame in which FROM or TO has no GPU work, or in which TO starts only before FROM ends, has no window. A line below the table counts those frames.
- A pass between FROM and TO costs the frame the difference of the window with and without the pass. `--paired` gives that difference for one trace whose frames alternate (see "The paired mode" below).
- The counters come from the instrument "Metal GPU Counters", which records the counter set "Performance Limiters" headless (tables `gpu-counter-info` and `gpu-counter-value`). The script averages each counter over the samples that fall inside a pass's intervals. A sample comes about every 10 µs, so a pass of 0.05 ms has a few samples per frame, and its limiters are rough.
- The counter instrument also records Instruments' shader timeline, and both slow the GPU a little. So take the frame span and the timer's pass times from `gpu-timings.json` of a run without the trace, each pass's own busy time from a trace with `--no-counters`, and the limiters from a trace with counters. `--no-counters` records a smaller trace with times only.
- The trace counts every frame's command buffer, and the warm-up draws of the stills too: the first frame after the run steps that replace the frames before `--start`, and each still after a long step, draw their warm-up draws first (ARCHITECTURE.md, "The still rule"), and `gpu-timings.json` leaves those draws out. So the trace's frame count can be larger than the file's: 121 against 120 on `lamp-hall-64`'s film from frame 1 under `AutoExposure`, whose still takes one warm-up draw. Until `ter-9nrc` it was 152 against 120 under `ResampledLights`, whose still took 32. For a script of stills after long steps, most of the trace's frames are warm-up draws.
- The exported counters of 120 frames take more than a gigabyte of XML. A trace of 120 frames at 1080p takes about two minutes in all, and about 80 MB. The script deletes the exported tables after the report, unless `--keep-exports` is given.

What the trace does not give headless: the counters per draw or per dispatch, and the shader costs per line. In Instruments, open `run.trace`, select a frame's encoder on the GPU track, and read the counters and the shader timeline in the detail pane below it.

Give `--window` to `gpu-trace.sh` when you record. If a pass name is not in the run's `gpu-timings.json`, the report stops with exit code 1 after the recording. Then the trace and the exported tables stay in the output folder.

To read a window of a trace that you recorded before, run the report again with an output folder of its own, for example:

```bash
nix develop --command python3 -I tools/gpu-trace.py report captures/gpu-trace/demo-exit-399/run.trace captures/gpu-trace/demo-exit-399/run/gpu-timings.json captures/gpu-trace/demo-exit-399/window --window reflections bloom
```

The new `passes.md` gives the load averages at the start as `unknown`, unless you also pass `--log` with the trace's `run.log`. A new folder keeps the first `passes.md` with its load line. The report exports the trace's tables again into the new folder and keeps them there. For a trace with counters, that export takes more than a gigabyte. Delete the `export` folder after the report.

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

## The renderer survey

`tools/renderer-survey.py` measures every scene of `scripts/*.json` against the goal of epic `ter-8hyd`: frame time, specks and blotches in stills, flicker in motion, and the draws until the light settles after a change. Ticket `ter-6k3k` wrote it, and `docs/plans/renderer-finish.md` holds its first results, the reasons for its thresholds and the ranked failures. Each later ticket of the epic runs it again and compares its table with that baseline.

Every render runs at the budget's configuration: the Release demo, headless, with `--size 1920x1080`. A headless renderer draws at its fixed render scale of 2/3, so FSR 3.1 upscales 1280 × 720 to 1920 × 1080 (docs/plans/rendering-performance.md, decision 1).

### The cases

`tools/renderer-survey/cases.json` lists the cases, and the tool reads nothing else about them. A case names a frame of a script, counted from 0 over the script's captures and film frames, as `gpu-trace.py trim` counts them. A case can also name a scene, a camera and the ticks before the view, for a moment that no script captures, such as the station bay when the sunbeam crosses it. The file has eight lists:

| List | What each case gives | Build |
|---|---|---|
| `stills` | A view for the timing and the still measures | Main for the timing, probe for the stills |
| `motion` | A film of up to 180 frames for the flicker and the motion blotches | Main for the film, probe for its stills |
| `switches` | Lamps that switch off and on again | Probe |
| `lags` | Lights that move, such as the bay's sun | Probe |
| `walks` | A walk of the person, then a stop | Probe |
| `stands` | A stand film: a view without a change, one light signal, against its own end level | Probe |
| `climbs` | Continued stills of fixed states, one light signal, with the diffuse filter and without it | Probe |
| `skip` | A script that the survey leaves out, with the reason | None |

To add a scene, add a case to the file. A new script in `scripts/` needs at least one still case or a `skip` entry.

### Run it

Run the commands in the dev shell, from the repository root. The output folder must be outside the repository, for example `/private/tmp/tertius-survey`:

```bash
nix develop --command python3 -I tools/renderer-survey.py probe-build /private/tmp/tertius-survey
nix develop --command python3 -I tools/renderer-survey.py run /private/tmp/tertius-survey --clock-bin <folder of gpu-clock>
nix develop --command python3 -I tools/renderer-survey.py trace /private/tmp/tertius-survey --slowest 5
nix develop --command python3 -I tools/renderer-survey.py report /private/tmp/tertius-survey
nix develop --command python3 -I tools/renderer-survey.py sheet /private/tmp/tertius-survey
```

The commands do these things:

1. `probe-build` builds the probe demo into `<out>/probe-bin`. It extracts `git archive HEAD` into a temporary folder, applies `tools/renderer-survey/probe.patch`, and builds without the analyzers. The probe never enters `src`. If the patch no longer applies to a later `HEAD`, update the patch.
2. `run` builds the Release demo, renders each case, measures its images and deletes them. It keeps the shown still, the reference, and the first, middle and last frames of each film in `<out>/images`. It writes one result file per case into `<out>/results/<part>/<name>.json`. `--parts` selects the parts (default `timing,still,motion,switch,lag,walk,stand,climb`), and `--only` selects cases by name. The part `evidence` runs only when `--parts` names it ("The block test's evidence").
3. `trace` records a GPU trace without counters (`tools/gpu-trace.sh --no-counters`) of the timing film of each of the slowest views.
4. `report` prints the tables with the verdicts, in Markdown.
5. `sheet` writes `<out>/contact-sheet.png`, one labeled still of every view.

A case whose result file exists does not run again. So a stopped run goes on where it stopped. To run a case again, delete its result file. A full run on 2026-11-09 took about four hours, most of it in the wait for the GPU lock.

`run` takes the GPU lock once for each chunk of cases, 8 by default (`--chunk`), so that a chunk waits once for the other users of the lock. `--chunk 0` runs in one process and takes the lock for each render. A caller that already holds the lock sets `TERTIUS_SURVEY_HOLDS_LOCK=1`. Then `run` and `split` render in their own process and do not take the lock again, whatever `--chunk` says. A chunk of the timing part, and each timed render, holds the GPU alone. Every other chunk and render, also of `split`, takes the shared mode, because its images do not depend on the time (see "The shared mode"). `trace` records through `tools/gpu-trace.sh`, which holds the GPU alone. Before each render, the tool samples the GPU's busy share three times with `ioreg` and records it as `gpuBusyBefore`. Another process, such as an Android emulator, can use the GPU while the survey holds the lock. Before a timing run, make sure that no other process uses the GPU heavily. An Android emulator puts the GPU in a slow mode. So if an emulator runs before or during a view's timing, the tool writes no result for that view. When no emulator runs, run the timing part again.

The run stops before a render when less than 5 GB are free on `/`. A film of 180 frames at 1920 × 1080 takes about 700 MB until the tool measures it. The switch, lag, walk, stand and climb runs write a series file and few PNGs.

### The measures

The renderer resets its history at the first draw of every still (ARCHITECTURE.md, "The still rule"). So on main, each capture of an unchanged state is the same fresh still of 33 draws, with the same random numbers. The probe's setting `TERTIUS_PROBE_STILL_CONTINUES=1` keeps the history through the stills of an unchanged state, as a window keeps drawing an unchanged view. An unchanged state is a capture right after a drawn frame, with no tick between them. A still after a step of time still resets. Before 2026-11-10, the probe also kept the history across a step of time. So a lag still and a film's last reference started from the history of an earlier state. The still part, the stills of the motion part and the lag stills use it. The still and motion parts also render with `--bloom off`, because a bright glow's bloom changes from draw to draw with its sub-pixel place and is no error of the light. So the contact sheet shows the scenes without bloom.

The still part renders 28 stills of each view after the view's state, each with 33 draws, and the history goes on through them. The shown still is the first capture with at least 120 draws: 132 draws, or 133 when the first capture follows a drawn frame by fewer than 6 ticks. The reference is the mean of the last 20 captures, from draw 297 to draw 924. Their histories are nearly independent of the shown still's, because the slow history keeps at most 32 frames. The measures work on the display-linear luminance of the 8-bit sRGB captures:

- The speck count is ter-9371's rule: a pixel brighter than twice the median of its 7 × 7 window and than that median plus 0.03. A speck counts only when the reference has no speck within 2 pixels, so that stars and small lamps do not count. The count is the number of connected speck groups per 10,000 visible pixels. A pixel is visible when its blurred reference exceeds 0.002.
- The level is the median ratio of the blurred still to the blurred reference over the visible pixels, minus 1. It shows a light or an exposure that still changes at the still's draw.
- The blotch score compares the blurred still with the blurred reference. The blur is a Gaussian with a standard deviation of 4 render pixels (6 output pixels). The tool first divides the still by its level. Each pixel's error is then the difference over the reference, with a floor of 0.01 in the denominator. The tool gives the RMS and the 90th percentile of the errors over the visible pixels away from emitters. The 90th percentile is the score.

The reference comes from the same renderer. So the blotch score measures the low-frequency error that changes from draw to draw. It cannot show an error that every draw repeats.

The timing part renders each still view's state and then 120 frames one tick apart, with `--video none` and `--in-flight 2`, beside `gpu-clock` (see "The GPU clock of a timed run"). It records the median and the 95th percentile of the frame span in `gpu-timings.json` and their estimates at the windowed clock. `--clock-bin` names the folder of the built `gpu-clock`. Without it, the clock is unknown and the verdict uses the raw times.

The motion part first renders stills of 132 draws of the states of a film's middle and last frames, with the auto exposure. The mean of their exposures becomes the fixed exposure of the part (the probe's `TERTIUS_PROBE_EXPOSURE`). The part then renders the same stills again and up to 180 consecutive frames of the film, all at that fixed exposure. A film starts at a camera cut and adapts its exposure over seconds, while a still adapts at once. So without one shared exposure, the two differ by their exposure and not by their light. Before 2026-11-10, the stills kept their own auto exposure, and the film results of the first survey and of the baseline hold that error. The part gives the blotch score and the level of each sampled frame against its still. The film draws a frame every tick, as a window does. A script frame of N ticks becomes N frames, and a run of N ticks becomes N frames. Headless, a draw after 6 ticks or more is a still with warm-up draws, which a window never draws. The flicker is the median over the frames of the mean relative second difference, `y[t] - (y[t-1] + y[t+1]) / 2`, after the blur of the blotch score. A light that changes at a steady rate and a steady pan give a second difference near 0. Each pixel's share is capped at 0.5, so that a moving object adds at most its share of the image. The grain flicker is the same without the blur.

A case that measures nothing fails with the verdict "not measured". Three examples follow. In a still, every visible pixel lies near an emitter. A switch changes no region by more than 10 %. A lag case's light changes too little for a lag.

The probe parts use the probe demo with a fixed exposure (`--exposure`, 4.08 stops by default), no bloom, and the Hable tone curve, which the probe replaces with 24-bit log luminance. So they read scene luminance without the auto exposure. Each frame becomes the mean luminance of the whole image and of a 3 × 3 grid of regions. The probe computes these 10 means itself from each capture's readback, with the decode of `probe_luminance` and the grid of `region_means`. It appends one line per capture to `series.txt` in the run's folder (`TERTIUS_PROBE_SERIES`). It writes a PNG only for the capture names in `TERTIUS_PROBE_KEEP`, which are the four frames of a switch that the report keeps. Before ticket ter-d7qy, every frame went through a PNG. A 460-frame switch run then took 545 s on the cloud's GPU worker, about 1.2 s per frame. With the series, a frame took 14.6 ms there, against a GPU frame span of 10.0 ms. The probe's means differ from those of the PNG path by about 2 parts in 10^13, because the two sum the pixels in a different order. The settle tolerance is 5 %.


- A switch case turns the selected lamps off at film frame 160 and on at frame 360. It films 100 more frames (`TERTIUS_PROBE_LAMP_OFF`, `TERTIUS_PROBE_LAMP_SEL` and `TERTIUS_PROBE_SUN` in the patch). So the lamps-off window is 200 draws and the lamps-on window is 100 draws. Two control runs hold the lamps off or on from the start. The settle is the draws after the change until the light of every region that the switch changes by more than 10 % stays within 5 % of its control. This is the method of `settle2.py` of 2026-11-08.
- A lag case renders a film of moving lights, and stills of 132 draws of the same states at its sample frames. The lag is the film's difference from the still divided by the film's change per draw, so it is in draws. It counts only regions whose light changes by more than 10 % in 60 draws.
- A walk case stands for `stand` frames, walks the person forward for `walk` frames, stops, and then films `after` frames. A control run walks right after the case's head ticks without drawing, so the person stands where the walk ends, to about 1e-14 m. The control then draws through the same world ticks. The settle is the draws after the stop until every region stays within 5 % of the control's frame at the same tick. So a change of the world's light in the window counts in neither run. In the orbit cabin, the sunlit sill strips change the bounce light at world tick 303 (ticket ter-g3tk), inside the old window. The stand of 180 frames lets the control's own start after the load settle before the window. It also moves the window to ticks 361 to 560, after the tick-303 change. The control still counts, because a slow tail of that change, or a later change of the world's light, shows in both runs.

- A stand case (part `stand`) is the stand film of ticket `ter-g3tk`: the cabin walk's view without the walk, as in `/private/tmp/g3tk/run_dgate.sh`. It shows one light signal alone, `signal`, with the probe's `TERTIUS_PROBE_SIGNAL`. It renders a still, then `film` frames one tick apart, then `end` stills right after the last frame, which continue the film's history. Film frame k shows world tick `ticks` + k + 1. A region's end level is the mean of the last half of the end stills. The result gives each region's excess over its end level for each window of 40 film draws: the window's mean light over the end level, less 1. It gives each region's settle against its end level, 5 %, from the film's first frame (`settleDraws`) and from the frame that shows world tick `settleTick` (`settleDrawsAfterTick`). It also keeps the region means of every frame (`film`). The verdict reads only the settle of the case's `region` after `settleTick`. `cabin-stand` films 600 frames of the bounce light from tick 61, and its verdict is acceptance 7 of `docs/plans/bounce-light-confidence.md`: r11, the centre cell, settles within 60 draws after tick 306. In the orbit cabin, the sun turns against the ship at ticks 300 to 306. Its sunlit sill strips are thinner than a render pixel, and the bounce light that they give falls by 35 to 40 %. Regions r10 and r12 wander by about 5 % with that sampling fault, so they do not decide the verdict.
- A climb case (part `climb`) is the climb test of ticket `ter-g3tk`, the script `/private/tmp/g3tk/look/seeds.json`. For each world tick of `states`, it renders `stills` continued stills of 33 draws, `t<tick>-s00` and on, of one light signal alone. Each state's first still comes 6 or more ticks after the last drawn frame, so it starts a new history. The case renders twice, one run after the other: with the renderer's diffuse filter, and with `TERTIUS_DIFFUSE_FILTER=off` ("The diffuse filter's switch"). A region's plateau is the mean of its unfiltered stills from still 5 on, draw 198 to 396; the light without the filter reached its plateau by about draw 132 in take T2a of `t0-f7zv`. A region's peak share is its largest light over the filtered stills of a state, over its plateau, and its last share takes the last filtered still. The result keeps the region means of every still of both runs. A case can fix its own exposure with `exposure`, in place of `--exposure`. `cabin-climb` uses 3.15 stops, as the takes of `ter-g3tk` and T2a did, so its light compares with their notes. Its verdict is the target of acceptance 11: the band, r11, peaks at most 10 % above its plateau, at both ticks 300 and 306. The required check of acceptance 11 compares two builds: the candidate's peak share must not exceed the control's. Main's clamp loses part of the band's light, so a share and not the band's light compares two builds. The filter-off run also changes the light that the rays read at their hits. So the plateau is not the single-bounce reference of take T2b, which needs another probe patch.

A light that is still outside the tolerance at the last frame of its window has no settle count. The result file then holds `null`, and the report prints "not settled within N draws", with N the window's length. Such a case fails. Before 2026-11-09, the tool gave the window's length as the count. So a value of 100 in the first survey means "not settled within 100 draws".

### The stand and the climb on the GPU workbench

The stand and the climb measure light, not time, so they run on the GPU workbench too (`tools/runpod/WORKBENCH.md`). Compare the numbers of two builds inside one job only, because determinism holds per computer. A job sends back at most 8 MB, so it sends back the result files only: the stand's result is about 150 KB, the climb's about 15 KB. The parts render one image at a time. Four 1920 × 1080 renders at once ran out of memory on the workbench's 16 GB card.

The worker's tree is a partial clone without `docs/`, so `probe-build`, which archives `HEAD`, does not suit it. The job applies the probe patch to its throwaway tree in place, builds the probe into `out/probe-bin`, and runs the parts with `TERTIUS_SURVEY_HOLDS_LOCK=1`, because the worker has no GPU lock. Without `ioreg`, the GPU's busy share stays unknown, which the two parts do not read:

```bash
python3 -I tools/runpod/gpu_job.py submit --agent <your name> --tree t=HEAD \
    --run 'set -o pipefail; cd t && patch -p1 --forward -i tools/renderer-survey/probe.patch && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o ../out/probe-bin | tail -3' \
    --run 'cd t && TERTIUS_SURVEY_HOLDS_LOCK=1 python3 -I tools/renderer-survey.py run ../out --parts stand,climb --no-build' \
    --output 'out/results/*/*.json' --timeout 600 --out /tmp/<your name>/stand-climb
nix develop --command python3 -I tools/renderer-survey.py report /tmp/<your name>/stand-climb/out
```

A switch case does not fit in one job. A GPU job gets at most 840 s, and one switch run renders 460 film frames. So `TERTIUS_SURVEY_SWITCH_RUNS` names the runs of a switch case that one call renders, by label, comma-separated: `switch`, `off` and `on`. Each call keeps the series of its runs in `<out>/series/switch/<case>-<label>.json`, and a run whose series is kept does not render again. Return the series with `--output 'out/series/*/*.json'`. Then copy the series of all three runs into one folder, and run the switch part there on the Mac. That call finds the three series, renders nothing and needs no probe build. It writes the result.

`set -o pipefail` makes a failed build end the job. Without it, the pipe through `tail` hides the failure, and the job renders with no probe. For a candidate and its control in one job, name two trees, give each its own output folder, such as `../out-main`, and run the parts once for each. With `--output` globs for both folders, the job sends back both.

On the Mac, the two parts run as the other probe parts do, under the shared GPU lock:

```bash
nix develop --command python3 -I tools/renderer-survey.py probe-build /private/tmp/tertius-stand
nix develop --command python3 -I tools/renderer-survey.py run /private/tmp/tertius-stand --parts stand,climb --no-build
nix develop --command python3 -I tools/renderer-survey.py report /private/tmp/tertius-stand
```

The stand and the climb write a series file and no PNG, so their runs take little disk.

### The block test's evidence

The part `evidence` and the command `evidence` measure the block test of `docs/plans/bounce-light-confidence.md` (section 8) in long stills. They are take T3 of its section 20. The block test sums each block of 8 × 8 pixels into one texel, (Σd, Σv, n, Σs), and fires a window of 1 × 1, 3 × 3 or 7 × 7 blocks on evidence of a change. In a still, nothing changes, so every window that fires is false evidence.

The probe patch adds these settings. Each one is read once from the environment. A setting that is not set keeps the build's value.

| Setting | What it does |
|---|---|
| `TERTIUS_A_BLOCK_CAPTURE=<folder>` | Appends the block sums of each signal that runs the block test to `<folder>`, every draw |
| `TERTIUS_A_BLOCK_EVERY=<k>` | Captures every k-th draw only, counted from 0 over the run |
| `TERTIUS_A_BLOCK_ACTION=0` | Keeps the sums and removes their action: no window test, no narrower box, no cut of the length |
| `TERTIUS_A_BLOCK_SIGMAS`, `TERTIUS_A_TAIL_STREAMS`, `TERTIUS_A_NOISE_SCALE` | Set `BlockSigmas`, `TailStreams` and `BlockNoiseScale` of `accumulate_blocks.glsl` |
| `TERTIUS_A_DIFFUSE_CHANGE_SHARE`, `TERTIUS_A_SPECULAR_CHANGE_SHARE` | Set the floor `ChangeShare` of the bounce light and of the reflections |
| `TERTIUS_A_RETAINED_SIGMAS` | Sets `RetainedSigmas` of a widened box |
| `TERTIUS_A_NO_CLAMP=1` | Turns both signals' clamps off, and so the anti-lag, and removes the block test's action. It renders the truth |

The constants stay in their one place in `accumulate_blocks.glsl`. The patch sends a set value to the shader in three new push constants of the accumulation, where −1 keeps the shader's constant. A push constant `blockTest` of 2 writes the sums without their action. The `evidence` command reads the constants from the shaders.

A capture folder holds these files:

- `header.json`: the grid of blocks, `every`, and for each captured signal its file, its light, its push constants (cap, fast cap, floor, `RetainedSigmas`, the probe's three constants) and whether its history is uncapped. The reflections' precise history is uncapped by default.
- `<light>-<part>.f32`, such as `diffuse-0.f32`: for each captured draw, the blocks row by row from the top, four little-endian 32-bit floats per block. At 1280 × 720 that is 160 × 90 blocks, about 230 KB per draw and signal.
- `draws.txt`: one line per captured draw, `<draw> <draw index since the last reset> <1 if the draw reset, else 0>`.

The probe captures each signal whose push constant `blockTest` is not 0. Each readback waits for the device, so a captured render runs slower than a plain one.

`run --parts evidence` renders the still part's long still of a case, 28 captures of 33 draws to draw 924, with the history continued through them. It captures the sums every draw, with `TERTIUS_A_BLOCK_ACTION=0` unless the caller sets that setting. Then it measures the capture, deletes it, and writes `<out>/results/evidence/<name>.json`. The part takes the still views and the switches' views with their lamps on, such as `cabin`, the orbit cabin. `--keep-capture` moves the capture to `<out>/captures/<name>` instead. `--skip` (default 64) leaves out the draws after a reset while the slow history fills. The result names the `TERTIUS_A_*` settings of its run in `knobs`.

The `evidence` command measures a kept capture, so the windows and the constants can change offline. `--sweep` names a JSON file in the form of `EVIDENCE_SWEEP`, and `--write` names the output file:

```bash
nix develop --command python3 -I tools/renderer-survey.py evidence /private/tmp/t3/captures/rect-lamps --write /private/tmp/t3/rect-lamps.json
```

The measures, for each captured signal. A block-draw is a block with a contributing stream in a measured draw. A window fires only at such a block, because evidence acts only on the block's contributing pixels:

- `fires` and `falsePerBlockDraw`: the windows that fire at the capture's own rule, per window size and for any window, and their count per block-draw.
- `repeats`, `repeatShare` and `longestRun`: the fires of a block that also fired in the draw before, their share of all fires, and the longest run of draws in a row. With the action on (`TERTIUS_A_BLOCK_ACTION=1`), these count the repeated cuts of point 5 of the design's section 18.
- `deviationRatio`: the measured over the predicted deviation of the window's mean difference Σd / n, per window size. The measured deviation is the spread over the draws around the position's own mean. The predicted one is √(Σv) / n. `pooled` pools every window position with at least 64 draws. `noiseScaleForRatio1` is the `BlockNoiseScale` that makes `pooled` 1.
- `cv`: the CV of a block's samples, √(Σv · n) / Σs / (κ · `BlockNoiseScale`), with κ at the signal's cap (0.319 for the bounce light, 0.378 for an uncapped history).
- `stationaryDifference`: F̄ − R̄ over R̄ in the still, with R the own-sample mean that the sums hold since `t0-y4qv` (before it, the slow history S), per window position from its sums over the draws: the quantiles of its size up to the 99.99th percentile, and its mean. The floor `ChangeShare` must lie above it.
- `firesKept`: the first 100 fires, each with its draw, block, window, n, Σd / Σs, Σd / √Σv and Σs² / Σv (the tail).
- `sweep`: the fires of every combination of `BlockSigmas`, `TailStreams`, `BlockNoiseScale` and the signal's `ChangeShare` in `EVIDENCE_SWEEP`. Another noise scale multiplies Σv by the square of its ratio to the capture's.

The verdict is acceptance 12 of the design: at most 10^−4 false windows per block-draw for each signal, and a pooled ratio of the 1 × 1 window from 0.8 to 1.25. `report` prints one row per case and signal.

`TailStreams`, `BlockSigmas`, `BlockNoiseScale` and `ChangeShare` change only the windows' test, so the sweep covers them on one capture. `RetainedSigmas`, the clamps and the action change the histories themselves, so each of their variants needs its own render. Run it into its own output folder with its settings in the environment.

T3 on the GPU workbench, as one job for the four stills of the design, `rect-lamps`, `bay-sunbeam`, `docking-bay` and the orbit cabin `cabin`. The job sends back only the result files, about 80 KB per case:

```bash
python3 -I tools/runpod/gpu_job.py submit --agent <your name> --tree t=HEAD \
    --run 'set -o pipefail; cd t && patch -p1 --forward -i tools/renderer-survey/probe.patch && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o ../out/probe-bin | tail -3' \
    --run 'cd t && TERTIUS_SURVEY_HOLDS_LOCK=1 python3 -I tools/renderer-survey.py run ../out --parts evidence --only rect-lamps bay-sunbeam docking-bay cabin --no-build' \
    --output 'out/results/*/*.json' --timeout <seconds> --out /tmp/<your name>/t3
nix develop --command python3 -I tools/renderer-survey.py report /tmp/<your name>/t3/out
```

On 2026-11-12 a smoke job built the probe in 45 s and ran the part for `rect-lamps` in 94 s, the evaluation included. Its bounce light's capture took 213 MB on the worker, and the job counted $0.034. So the four stills take about 8 minutes in one job. Set `--timeout 1000`, which leaves room for the reflections' capture after they get the block test.

On the Mac, the part runs as the other probe parts do, under the shared GPU lock: `probe-build`, then `run <out> --parts evidence --only ... --no-build`.

### The split by light signal

`split` renders a film or a still once for each light signal and tells which signal carries its error. The light signals are the direct light with the emission, the bounce light (indirect diffuse) and the reflections (indirect specular). The probe's setting `TERTIUS_PROBE_SIGNAL` (1, 2 or 3) shows one signal alone, and 0 shows the whole image. The command needs a probe built from the current `probe.patch` in its own output folder:

```bash
nix develop --command python3 -I tools/renderer-survey.py probe-build /private/tmp/tertius-split
nix develop --command python3 -I tools/renderer-survey.py split /private/tmp/tertius-split --films bay-sunbeam rect-bay --stills rect-lamps
nix develop --command python3 -I tools/renderer-survey.py split-report /private/tmp/tertius-split
```

A film takes the fixed exposure of its result in the survey folder that `--survey` names. Without such a result, it takes its exposure as the motion part does: the mean auto exposure of the stills of its middle and last frames. A still first renders its shown still with the auto exposure and takes that exposure. Each render then uses the fixed exposure and the probe's log luminance, so the four renders read scene luminance and add up.

The direct light's render also holds the sky, the stars and an impostor's baked light, so that the four renders add up. Every pass after the composite, such as glass, particles and haze, adds its light to each of the four renders. So the split computes that light as (direct + bounce + reflections − all) / 2 and takes it out of each signal. The report names it "after". For each sample, the report gives the whole image's blotch score and level, and each part's blotch score, level and share of the light. A part's blotch score is the score of that part's difference alone, relative to the whole image's light. So the part with the largest score carries the error. A film also gets the flicker of each part.

With `--native`, the split of a film renders at 1280 × 720 without FSR 3.1 (`--upscaling off`). It scales each image up to 1920 × 1080 before it measures. A part whose error stays without FSR is the renderer's, and a part whose error goes away comes from FSR's own history. The result file then ends in `-native`.

The split measures in scene luminance, not in the display luminance of the survey. Its masks and its floor come from an ACES fit of the scene luminance. So its scores are close to the survey's scores but not equal to them. FSR 3.1 upscales each render on its own, and its clamp is not linear. So the parts do not add up exactly where FSR clamps.

### Tests

`tools/test_renderer_survey.py` tests the scripts that the tool writes and the measures, on synthetic scripts and images. It needs NumPy, SciPy and Pillow, which the dev shell gives. It runs in about a second:

```bash
nix develop --command python3 -I tools/test_renderer_survey.py
```

## The diffuse filter's switch

The environment variable `TERTIUS_DIFFUSE_FILTER` selects the diffuse filter's kernel, or no filter (ARCHITECTURE.md, "The diffuse filter"). The renderer reads it once, when it is made. Unset, empty, `on` and `thin` select the thin kernel, the default: six steps of 3 × 3 taps (ticket `ter-hxhl`). With `wide`, the filter takes the paper's kernel of five steps of 5 × 5 taps, the default before `ter-hxhl`. With `off`, the renderer makes no filter: the composite shows the diffuse slow history, and the rays read the diffuse fast history at their hits, as before ticket `ter-9371`. Any other value stops the renderer with an `ArgumentException`.

xctrace starts the demo from a clean environment, so `tools/gpu-trace.sh` passes `TERTIUS_DIFFUSE_FILTER` on through `--env`. Unset, it passes an empty value, which selects the thin kernel.

To compare two settings, render the same script twice into two folders:

```bash
nix develop --command bash -c 'dotnet run --project src/Tertius.Demo -c Release -- --script scripts/orbit.json --headless --out captures/filter-on'
nix develop --command bash -c 'TERTIUS_DIFFUSE_FILTER=off dotnet run --project src/Tertius.Demo -c Release -- --script scripts/orbit.json --headless --out captures/filter-off'
```

The auto exposure meters each run's own image, so the two runs can take different exposures. Compare the light of two runs at one fixed exposure.

## The guided rays' switch

The environment variable `TERTIUS_GUIDED_RAYS` turns the guided diffuse rays on or off (ARCHITECTURE.md, "Guided diffuse rays"). The renderer reads it once, when it is made. Unset, empty and `on` keep the guiding. With `off`, the renderer records no "light pyramid" pass, and every diffuse ray takes the cosine draw, as before ticket `ter-05wb`. The light pyramid's images stay, because the tracer's sets bind them. Any other value stops the renderer with an `ArgumentException`.

To compare the two, render the same script twice into two folders:

```bash
nix develop --command bash -c 'dotnet run --project src/Tertius.Demo -c Release -- --script scripts/orbit.json --headless --out captures/guided-on'
nix develop --command bash -c 'TERTIUS_GUIDED_RAYS=off dotnet run --project src/Tertius.Demo -c Release -- --script scripts/orbit.json --headless --out captures/guided-off'
```

As with the filter's switch, compare the light of two runs at one fixed exposure.

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

A file that is not code can have staged content that differs from both the disk and HEAD, for example after a partial `git add -p`. The script removes such a file from its copy of the index with `git rm -f`, so the hash stays as it was. `tools/test_code_tree.py` tests this in new repositories under `/private/tmp`, with the standard library only:

```bash
nix develop --command python3 -I tools/test_code_tree.py
```

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

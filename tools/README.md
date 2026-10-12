# Test tools

These helpers run code coverage and Stryker.NET mutation testing on the lines that a branch changed. For the rules on which helper to run, read the Testing section of CLAUDE.md. The scripts are tool code: they never ship, so they skip polish.

Run every command in the dev shell, from the repository root. Write the output to a folder outside the repository, for example `/tmp/tertius-coverage`.

## Coverage on changed lines

1. Run every test project with coverage, in both build configurations:

   ```bash
   nix develop --command bash -c 'tools/coverage/run.sh /tmp/tertius-coverage Debug Release'
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
   nix develop --command bash -c 'tools/stryker/run.sh Core /tmp/tertius-stryker/Core'
   ```

3. List the mutants that no test caught:

   ```bash
   nix develop --command bash -c 'python3 tools/stryker/survivors.py /tmp/tertius-stryker/Core/out/reports/mutation-report.json'
   ```

Triage each mutant in the list as the `test-hardening` skill says: kill it with a test, or mark it equivalent with a one-sentence reason in a Stryker ignore comment.

## Stryker.NET pitfalls

These cost time on the vertical slice, so the scripts handle them:

- Stryker.NET 5.0.0 reads `coverage-analysis` only from a configuration file, not from the command line. Without `perTest`, every mutant runs every test.
- Stryker.NET matches a `mutate` pattern against the full path. A pattern without the `**/` prefix matches no file, and the run then has no mutants.
- In a pattern such as `**/Frames/FrameTree.cs{402..1704}`, the numbers are character offsets in the file, not line numbers.
- The progress bar shows only on a terminal. The `-L` flag writes the log to `out/logs`, so that you can follow a run in the background.

Do not run two Stryker runs at once, and do not run Stryker in subagents. Two runs compete for the CPU, and their tests time out.

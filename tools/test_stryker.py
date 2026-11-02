#!/usr/bin/env python3
"""Tool code. The tests of the pure parts of the official Stryker run: the diff reader of tools/changed_lines.py, the
report reader of tools/stryker/survivors.py, and the test projects, the log evidence, the validity rule and the lock of
tools/stryker/changed.py.

    nix develop --command python3 -I tools/test_stryker.py

The standard library only. The file loads its modules by their paths, because -I keeps the tools folder off sys.path.
"""

import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parent


def _load(name, file):
    spec = importlib.util.spec_from_file_location(name, TOOLS / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


changed_lines = _load("changed_lines", "changed_lines.py")
survivors = _load("survivors", "stryker/survivors.py")
changed = _load("changed", "stryker/changed.py")


class ReadingADiff(unittest.TestCase):
    """parse_diff: a diff with no context lines gives the added or changed lines of each .cs file, and the places
    where the branch removed more lines than it added."""

    DIFF = (
        "diff --git a/src/A/X.cs b/src/A/X.cs\n"
        "--- a/src/A/X.cs\n"
        "+++ b/src/A/X.cs\n"
        "@@ -3 +3 @@ class X\n"
        "@@ -10,3 +9,0 @@ void F()\n"
        "@@ -20,4 +17,2 @@ void G()\n"
        "@@ -30,0 +26,2 @@ void H()\n"
        "diff --git a/src/A/Gone.cs b/src/A/Gone.cs\n"
        "--- a/src/A/Gone.cs\n"
        "+++ /dev/null\n"
        "@@ -1,5 +0,0 @@\n"
        "diff --git a/src/A/shader.comp b/src/A/shader.comp\n"
        "--- a/src/A/shader.comp\n"
        "+++ b/src/A/shader.comp\n"
        "@@ -1 +1 @@\n"
    )

    def test_the_changed_lines_are_the_new_lines_of_each_hunk(self):
        lines, _ = changed_lines.parse_diff(self.DIFF)
        self.assertEqual(dict(lines), {"src/A/X.cs": {3, 17, 18, 26, 27}})

    def test_a_hunk_that_only_deletes_is_removed_at_the_line_after_the_deletion(self):
        _, removed = changed_lines.parse_diff(self.DIFF)
        self.assertIn((10, 3), removed["src/A/X.cs"])

    def test_a_hunk_that_deletes_more_than_it_adds_is_removed_at_its_first_new_line(self):
        _, removed = changed_lines.parse_diff(self.DIFF)
        self.assertEqual(removed["src/A/X.cs"], [(10, 3), (17, 2)])

    def test_a_deleted_file_and_a_file_that_is_not_csharp_give_nothing(self):
        lines, removed = changed_lines.parse_diff(self.DIFF)
        self.assertEqual(set(lines) | set(removed), {"src/A/X.cs"})


class TestProjectsOfAModule(unittest.TestCase):
    """test_projects: the test projects whose references reach a module, directly or through other projects, without
    the test projects of GPU code."""

    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        self.root = Path(self.dir.name)
        self._project("src/Tertius.Core/Tertius.Core.csproj")
        self._project("src/Tertius.Physics/Tertius.Physics.csproj", "../Tertius.Core/Tertius.Core.csproj")
        self._project("tests/Tertius.Testing/Tertius.Testing.csproj", "../../src/Tertius.Physics/Tertius.Physics.csproj")
        self._project("tests/Tertius.Core.Tests/Tertius.Core.Tests.csproj", "../../src/Tertius.Core/Tertius.Core.csproj")
        self._project("tests/Tertius.Physics.Tests/Tertius.Physics.Tests.csproj", "../Tertius.Testing/Tertius.Testing.csproj")
        self._project("tests/Tertius.Rendering.Tests/Tertius.Rendering.Tests.csproj", "../Tertius.Testing/Tertius.Testing.csproj")
        self._project("tests/Tertius.Host.Tests/Tertius.Host.Tests.csproj", "../Tertius.Testing/Tertius.Testing.csproj")
        self._project("tests/Tertius.Style.Tests/Tertius.Style.Tests.csproj")

    def _project(self, path, *references):
        file = self.root / path
        file.parent.mkdir(parents=True, exist_ok=True)
        items = "".join(f'<ProjectReference Include="{r}" />' for r in references)
        file.write_text(f"<Project><ItemGroup>{items}</ItemGroup></Project>\n")

    def test_a_module_has_its_own_tests_and_the_tests_that_reach_it_through_a_shared_helper_project(self):
        self.assertEqual(changed.test_projects(self.root, "Core"), ["Core", "Physics"])

    def test_the_tests_of_a_module_never_include_the_gpu_test_projects(self):
        self.assertNotIn("Rendering", changed.test_projects(self.root, "Physics"))
        self.assertNotIn("Host", changed.test_projects(self.root, "Physics"))

    def test_a_test_project_that_does_not_reach_the_module_is_left_out(self):
        self.assertEqual(changed.test_projects(self.root, "Physics"), ["Physics"])


def _report(root, statuses):
    """A Stryker JSON report of one file, src/Tertius.Core/R.cs, with one mutant per (line, status) of statuses, and two
    tests."""
    source = "".join(f"int v{n} = {n} + 1;\n" for n in range(1, 11))
    mutants = []
    for n, (line, status) in enumerate(statuses):
        mutants.append({
            "id": str(n), "mutatorName": "Arithmetic mutation", "replacement": f"{line} - 1", "status": status,
            "location": {"start": {"line": line, "column": 10}, "end": {"line": line, "column": 15}},
            "coveredBy": ["t1", "t2"], "killedBy": ["t2"] if status == "Killed" else [],
        })
    return {
        "files": {f"{root}/src/Tertius.Core/R.cs": {"source": source, "mutants": mutants}},
        "testFiles": {"T.cs": {"tests": [{"id": "t1", "name": "Tertius.Core.Tests.RTests.One"},
                                         {"id": "t2", "name": "Tertius.Core.Tests.RTests.Two(x: 1)"}]}},
    }


class ReadingAReport(unittest.TestCase):
    """rows: a Stryker report becomes the rows of the shared table, for the mutants inside the configuration's spans."""

    ROOT = "/repo"

    def _rows(self, statuses, first=2, last=6):
        report = _report(self.ROOT, statuses)
        # Lines 1 to 9 of R.cs are 'int vN = N + 1;\n', 16 characters each.
        spans = {"R.cs": [(16 * (first - 1), 16 * last)]}
        return survivors.rows(report, spans, "Core", self.ROOT)

    def test_each_status_becomes_its_result_in_the_shared_table(self):
        rows = self._rows([(2, "Killed"), (3, "Survived"), (4, "NoCoverage"), (5, "Timeout"), (6, "RuntimeError")])
        self.assertEqual([r["result"] for r in rows], ["killed", "survived", "no-coverage", "timeout", "killed"])

    def test_a_killed_row_names_its_killing_tests_without_their_namespace(self):
        rows = self._rows([(2, "Killed")])
        self.assertEqual(rows[0]["killed_by"], ["RTests.Two(x: 1)"])

    def test_the_cases_of_one_theory_name_their_test_once(self):
        report = _report(self.ROOT, [(2, "Killed")])
        report["testFiles"]["T.cs"]["tests"].append({"id": "t3", "name": "Tertius.Core.Tests.RTests.Two(x: 1)"})
        report["files"]["/repo/src/Tertius.Core/R.cs"]["mutants"][0]["killedBy"] = ["t2", "t3"]
        rows = survivors.rows(report, {"R.cs": [(16, 96)]}, "Core", self.ROOT)
        self.assertEqual(rows[0]["killed_by"], ["RTests.Two(x: 1)"])

    def test_a_row_shows_the_mutated_text_and_its_replacement(self):
        row = self._rows([(2, "Survived")])[0]
        self.assertEqual((row["file"], row["line"], row["change"]), ("src/Tertius.Core/R.cs", 2, "2 + 1 -> 2 - 1"))

    def test_a_mutant_outside_the_spans_gives_no_row(self):
        self.assertEqual(self._rows([(8, "Survived"), (1, "Killed")]), [])

    def test_an_ignored_mutant_gives_no_row_because_its_ignore_comment_already_holds_its_triage(self):
        self.assertEqual(self._rows([(2, "Ignored")]), [])

    def test_a_line_whose_every_mutant_failed_to_compile_is_one_no_mutant_row(self):
        rows = self._rows([(3, "CompileError"), (3, "CompileError")])
        self.assertEqual([(r["line"], r["result"]) for r in rows], [(3, "no-mutant")])

    def test_a_line_with_one_compiled_mutant_gives_no_no_mutant_row(self):
        rows = self._rows([(3, "CompileError"), (3, "Survived")])
        self.assertEqual([r["result"] for r in rows], ["survived"])


LOG = """\
2026-11-01T13:29:03.3306410-05:00  [INF] Number of tests found: 54 for project "/r/src/Tertius.Core/Tertius.Core.csproj". Initial test run started. (640e6501)
2026-11-01T13:29:10.7659590-05:00  [DBG] Initial test run output: "". (13f68f50)
2026-11-01T13:29:14.1915080-05:00  [DBG] Trying compilation for the "first" time. (f2d91e8b)
2026-11-01T13:29:15.8712630-05:00  [DBG] Compilation failed (5193a3b0)
2026-11-01T13:29:15.8762420-05:00  [DBG] Trying compilation for the "second" time. (f2d91e8b)
2026-11-01T13:29:25.3528590-05:00  [INF] Starting per-test coverage capture for MTP runner (01300839)
2026-11-01T13:29:55.5253460-05:00  [WRN] "MtpRunner-1": Test run timed out while capturing per-test coverage for "92c3"; marking as Dubious (7df5bff6)
2026-11-01T13:30:06.8152410-05:00  [WRN] "MtpRunner-0": Timed out waiting for coverage relay ack for test "9995"; marking as Dubious (580544c0)
2026-11-01T13:38:09.6937190-05:00  [INF] Per-test coverage capture complete: 54 tests captured (82710f72)
2026-11-01T13:38:09.7092670-05:00  [DBG] Mutant 0 will be tested against (37) tests. (e6422138)
2026-11-01T13:38:09.7092890-05:00  [DBG] Mutant 1 will be tested against (all) tests. (e6422138)
2026-11-01T13:38:09.7092910-05:00  [DBG] Mutant 2 will be tested against (3) tests. (e6422138)
2026-11-01T13:38:09.7092930-05:00  [DBG] Mutant 9 will be tested against (5) tests. (e6422138)
2026-11-01T13:50:51.4205300-05:00  [INF] Time Elapsed 00:21:56.1189923 (f4427d77)
"""


class EvidenceInTheLog(unittest.TestCase):
    """evidence: the Stryker log shows how many compiles, initial test runs and Dubious tests a run had, and how many
    tests each of the run's mutants ran."""

    def setUp(self):
        self.evidence = changed.evidence(LOG, {"0", "1", "2"})

    def test_the_compile_tries_of_the_one_schemata_build_are_counted(self):
        self.assertEqual((self.evidence["compiles"], self.evidence["compile_failures"]), (2, 1))

    def test_the_initial_test_run_gives_its_count_of_tests_and_its_seconds(self):
        self.assertEqual(self.evidence["initial_runs"], 1)
        self.assertEqual(self.evidence["tests"], 54)
        self.assertAlmostEqual(self.evidence["initial_seconds"], 7.435, places=2)

    def test_the_coverage_capture_gives_its_seconds(self):
        self.assertAlmostEqual(self.evidence["coverage_seconds"], 524.34, places=1)

    def test_every_test_that_stryker_marks_dubious_is_counted(self):
        self.assertEqual(self.evidence["dubious"], 2)

    def test_the_scope_of_each_mutant_of_the_run_is_its_count_of_tests_or_all(self):
        self.assertEqual(self.evidence["scopes"], {"0": 37, "1": "all", "2": 3})


class ValidityOfARun(unittest.TestCase):
    """problems: what makes an official run not valid, each with its fix."""

    @staticmethod
    def _rows(*results):
        return [{"id": str(n), "result": r} for n, r in enumerate(results)]

    def _evidence(self, **fields):
        evidence = {"dubious": 0, "initial_runs": 1, "scopes": {}, "tests": 100}
        evidence.update(fields)
        return evidence

    def test_a_run_whose_timeouts_stay_at_the_share_is_valid(self):
        rows = self._rows("killed", "killed", "killed", "killed", "killed", "killed", "killed", "killed", "killed", "timeout")
        self.assertEqual(changed.problems(rows, self._evidence(), max_timeout_share=0.10), [])

    def test_a_run_whose_timeouts_pass_the_share_is_not_valid_and_names_the_fix(self):
        rows = self._rows("killed", "timeout", "survived")
        problems = changed.problems(rows, self._evidence(), max_timeout_share=0.10)
        self.assertEqual(len(problems), 1)
        self.assertIn("timeout", problems[0])
        self.assertIn("quieter machine", problems[0])

    def test_the_share_counts_only_mutants_that_ran_not_lines_for_the_hand_list(self):
        rows = self._rows("killed", "killed", "killed", "killed", "killed", "killed", "killed", "killed", "killed",
                          "timeout", "no-mutant", "removed")
        self.assertEqual(changed.problems(rows, self._evidence(), max_timeout_share=0.10), [])

    def test_a_dubious_test_makes_the_run_not_valid_because_it_widens_every_mutant_to_that_test(self):
        problems = changed.problems(self._rows("killed"), self._evidence(dubious=3), max_timeout_share=0.10)
        self.assertTrue(any("Dubious" in p for p in problems), problems)


class TheLock(unittest.TestCase):
    """Lock: one Stryker run at a time on the machine, through a folder that mkdir makes or refuses atomically."""

    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        self.path = Path(self.dir.name) / "stryker.lock"

    def test_inside_the_block_the_lock_folder_names_this_process_and_after_it_the_folder_is_gone(self):
        with changed.Lock(self.path, poll=0.01):
            self.assertIn(str(os.getpid()), (self.path / "owner").read_text())
        self.assertFalse(self.path.exists())

    def test_an_exception_inside_the_block_still_releases_the_lock(self):
        with self.assertRaises(RuntimeError):
            with changed.Lock(self.path, poll=0.01):
                raise RuntimeError("Stryker failed")
        self.assertFalse(self.path.exists())

    def test_a_second_run_waits_until_the_first_releases_the_lock(self):
        order = []
        holder = changed.Lock(self.path, poll=0.01)
        holder.__enter__()

        def second():
            with changed.Lock(self.path, poll=0.01, say=lambda _: None):
                order.append("second")

        thread = threading.Thread(target=second)
        thread.start()
        time.sleep(0.2)
        order.append("first released")
        holder.__exit__(None, None, None)
        thread.join(timeout=5)
        self.assertEqual(order, ["first released", "second"])

    def test_a_release_leaves_a_lock_that_another_process_holds(self):
        self.path.mkdir()
        (self.path / "owner").write_text(f"{os.getppid()} another run\n")
        lock = changed.Lock(self.path, poll=0.01)
        lock.release()
        self.assertTrue((self.path / "owner").exists())

    def test_a_lock_whose_owner_process_is_gone_stops_the_run_with_the_command_that_removes_it(self):
        process = subprocess.Popen([sys.executable, "-c", "pass"])
        process.wait()
        self.path.mkdir()
        (self.path / "owner").write_text(f"{process.pid} a run that died\n")
        with self.assertRaises(SystemExit) as stop:
            with changed.Lock(self.path, poll=0.01):
                pass
        self.assertIn("rm -r", str(stop.exception))
        self.assertTrue(self.path.exists())


if __name__ == "__main__":
    unittest.main()

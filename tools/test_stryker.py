#!/usr/bin/env python3
"""Tool code. The tests of the pure parts of the official Stryker run: the diff reader of tools/changed_lines.py, the
report reader of tools/stryker/survivors.py, the test threads of tools/stryker/config.py, the test projects, the log
evidence, the validity rule and the split of the changed lines of tools/stryker/changed.py, the lock, the Stryker command
and the process samples of tools/stryker/run.py, and the comparison of runs in tools/stryker/compare.py.

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
from unittest import mock

TOOLS = Path(__file__).resolve().parent


def _load(name, file):
    spec = importlib.util.spec_from_file_location(name, TOOLS / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


changed_lines = _load("changed_lines", "changed_lines.py")
survivors = _load("survivors", "stryker/survivors.py")
changed = _load("changed", "stryker/changed.py")
config = _load("config", "stryker/config.py")
compare = _load("compare", "stryker/compare.py")
run = _load("run", "stryker/run.py")
cloud = _load("cloud", "stryker/cloud.py")


class TheCloudRun(unittest.TestCase):
    """tools/stryker/cloud.py runs one module per CPU worker and prints the table of changed.py over all of them."""

    def result(self, module, problems=(), official=True):
        return {"rows": [{"id": f"{module}-1", "result": "killed"}], "unmutated": [[f"src/{module}.cs", 3]],
                "evidence": [f"{module}: evidence"], "problems": list(problems), "settings": "concurrency 1",
                "official": official, "load": [1.0]}

    def test_the_merged_run_keeps_every_modules_rows_in_the_order_of_the_official_run(self):
        run = cloud.merged({"Physics": self.result("Physics"), "Core": self.result("Core")}, {})
        self.assertEqual([row["id"] for row in run["rows"]], ["Core-1", "Physics-1"])
        self.assertEqual(run["unmutated"], [("src/Core.cs", 3), ("src/Physics.cs", 3)])
        self.assertEqual(run["problems"], [])

    def test_a_module_whose_job_gave_no_results_makes_the_run_not_valid(self):
        run = cloud.merged({"Core": self.result("Core")}, {"Simulation": "exit code 1"})
        self.assertEqual(len(run["problems"]), 1)
        self.assertIn("Simulation", run["problems"][0])

    def test_one_modules_calibration_settings_make_the_whole_run_unofficial(self):
        run = cloud.merged({"Core": self.result("Core"), "Physics": self.result("Physics", official=False)}, {})
        self.assertFalse(run["official"])

    def test_an_output_folder_that_holds_files_is_refused_so_an_old_result_cannot_count_for_this_run(self):
        with tempfile.TemporaryDirectory() as folder:
            (Path(folder) / "Core").mkdir()
            (Path(folder) / "Core" / "results.json").write_text("{}")
            self.assertIn("not empty", cloud.unusable(Path(folder)))
            self.assertIsNone(cloud.unusable(Path(folder) / "new"))
            (Path(folder) / "empty").mkdir()
            self.assertIsNone(cloud.unusable(Path(folder) / "empty"))

    def test_the_job_asks_only_for_files_that_fit_the_8_mb_a_result_carries(self):
        # On 2026-11-12, Stryker's log was 8 to 21 MB and the mutation report 6 to 85 MB per module.
        self.assertFalse([name for name in cloud.OUTPUTS if "mutation-report" in name or "logs" in name])
        self.assertIn("results.json", cloud.OUTPUTS)

    def test_a_signal_to_the_cloud_run_reaches_every_job_that_it_started(self):
        class Child:
            def __init__(self):
                self.signals = []

            def send_signal(self, signum):
                self.signals.append(signum)

        children = [Child(), Child()]
        cloud.forward(children, 15)
        self.assertEqual([child.signals for child in children], [[15], [15]])

    def test_the_job_deepens_its_shallow_tree_and_runs_one_module_from_the_merge_base(self):
        command = cloud.job_command("a" * 40, "Simulation", ["Program.cs"])
        self.assertIn("--unshallow", command)
        self.assertTrue(command.endswith(f"changed.py {'a' * 40} ../out --modules Simulation --exclude Program.cs"))


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

    def test_with_the_code_suffixes_the_shader_files_of_a_diff_count_too(self):
        changed_files, _ = changed_lines.parse_diff(self.DIFF, changed.CODE_SUFFIXES)
        self.assertEqual(changed_files["src/A/shader.comp"], {1})

    def test_a_deleted_file_and_a_file_that_is_not_csharp_give_nothing(self):
        lines, removed = changed_lines.parse_diff(self.DIFF)
        self.assertEqual(set(lines) | set(removed), {"src/A/X.cs"})


class SplittingTheChangedLines(unittest.TestCase):
    """hand_files: every changed code file outside the C# of the official run's modules goes to the tools/mutate.py
    list, so that no changed line falls between the two tools."""

    def test_the_csharp_files_of_core_simulation_and_physics_go_to_stryker(self):
        self.assertEqual(changed.hand_files(["src/Tertius.Core/A.cs", "src/Tertius.Simulation/B/C.cs",
                                             "src/Tertius.Physics/D.cs"]), [])

    def test_rendering_host_and_demo_files_go_to_the_list(self):
        files = ["src/Tertius.Demo/D.cs", "src/Tertius.Host/H.cs", "src/Tertius.Rendering/R.cs"]
        self.assertEqual(changed.hand_files(files), files)

    def test_a_shader_goes_to_the_list(self):
        self.assertEqual(changed.hand_files(["src/Tertius.Rendering/Shaders/blur.comp"]),
                         ["src/Tertius.Rendering/Shaders/blur.comp"])

    def test_an_analyzer_goes_to_the_list(self):
        self.assertEqual(changed.hand_files(["analyzers/Tertius.Unions/UnionGenerator.cs"]),
                         ["analyzers/Tertius.Unions/UnionGenerator.cs"])

    def test_a_new_module_goes_to_the_list_until_the_official_run_names_it(self):
        self.assertEqual(changed.hand_files(["src/Tertius.Audio/Mixer.cs"]), ["src/Tertius.Audio/Mixer.cs"])


class RemovedLines(unittest.TestCase):
    """removed_rows: one row for each place where the branch removed lines in a module of the run."""

    def test_removed_rows_of_two_files_at_the_same_line_have_different_ids(self):
        removed = {"src/Tertius.Core/A.cs": [(88, 2)], "src/Tertius.Core/B/A.cs": [(88, 1)]}
        rows = changed.removed_rows(removed, {"Core"}, [])
        self.assertEqual([r["id"] for r in rows], ["Core-A.cs-R88", "Core-B/A.cs-R88"])
        self.assertEqual({r["result"] for r in rows}, {"removed"})

    def test_a_removal_outside_the_modules_of_the_run_or_in_an_excluded_file_gives_no_row(self):
        removed = {"src/Tertius.Physics/A.cs": [(3, 1)], "src/Tertius.Core/Program.cs": [(5, 1)]}
        self.assertEqual(changed.removed_rows(removed, {"Core"}, ["Program.cs"]), [])


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

    def test_no_mutant_rows_of_two_files_at_the_same_line_have_different_ids(self):
        report = _report(self.ROOT, [(3, "CompileError")])
        first = report["files"]["/repo/src/Tertius.Core/R.cs"]
        report["files"]["/repo/src/Tertius.Core/Sub/R.cs"] = json.loads(json.dumps(first))
        rows = survivors.rows(report, {"R.cs": [(16, 96)]}, "Core", self.ROOT)
        self.assertEqual([r["id"] for r in rows], ["Core-R.cs-L3", "Core-Sub/R.cs-L3"])

    def test_a_line_with_one_compiled_mutant_gives_no_no_mutant_row(self):
        rows = self._rows([(3, "CompileError"), (3, "Survived")])
        self.assertEqual([r["result"] for r in rows], ["survived"])


class LinesWithoutAMutant(unittest.TestCase):
    """unmutated_lines: the changed code lines on which Stryker made no mutant at all, for the agent to judge for the
    tools/mutate.py list. Stryker 5.0.0 has no mutator for some changes, such as an enum value in an assignment."""

    ROOT = "/repo"

    def _lines(self, statuses, source=None, first=2, last=6):
        report = _report(self.ROOT, statuses)
        if source is not None:
            report["files"]["/repo/src/Tertius.Core/R.cs"]["source"] = source
        spans = {"R.cs": [(16 * (first - 1), 16 * last)]}
        return survivors.unmutated_lines(report, spans, self.ROOT)

    def test_a_changed_code_line_without_any_stryker_mutant_is_listed(self):
        self.assertEqual(self._lines([(2, "Killed"), (4, "Survived")]),
                         [("src/Tertius.Core/R.cs", 3), ("src/Tertius.Core/R.cs", 5), ("src/Tertius.Core/R.cs", 6)])

    def test_a_line_outside_the_spans_is_not_listed(self):
        self.assertNotIn(("src/Tertius.Core/R.cs", 7), self._lines([]))
        self.assertNotIn(("src/Tertius.Core/R.cs", 1), self._lines([]))

    def _with_mutant(self, status, first, last, reason=""):
        report = _report(self.ROOT, [])
        report["files"]["/repo/src/Tertius.Core/R.cs"]["mutants"].append({
            "id": "9", "mutatorName": "Block removal mutation", "replacement": "{}", "status": status,
            "statusReason": reason, "location": {"start": {"line": first, "column": 1}, "end": {"line": last, "column": 15}}})
        return [line for _, line in survivors.unmutated_lines(report, {"R.cs": [(16, 96)]}, self.ROOT)]

    def test_a_line_whose_only_mutant_is_ignored_by_a_comment_is_not_listed_because_the_comment_holds_its_triage(self):
        self.assertNotIn(3, self._with_mutant("Ignored", 3, 3, "the guard and its negation agree at the bound"))

    def test_an_ignored_mutant_that_a_stryker_filter_removed_hides_no_line_because_no_test_ever_ran_it(self):
        # Result.cs of ter-9xl7: Stryker removed the Block removal mutant of lines 61 to 67 with this reason.
        self.assertEqual(self._with_mutant("Ignored", 2, 6, "Removed by block already covered filter"), [2, 3, 4, 5, 6])

    def test_a_mutant_over_several_lines_counts_only_for_its_first_line(self):
        self.assertEqual(self._with_mutant("Killed", 2, 6), [3, 4, 5, 6])

    def test_a_line_whose_mutants_failed_to_compile_is_not_listed_because_it_is_a_no_mutant_row(self):
        self.assertNotIn(("src/Tertius.Core/R.cs", 3), self._lines([(3, "CompileError")]))

    @staticmethod
    def _source(*lines):
        # Each line gets 16 characters, so that the spans of lines 2 to 6 stay the same.
        return "".join(line.ljust(15)[:15] + "\n" for line in lines)

    def test_declarations_signatures_and_constraints_without_behavior_are_not_listed(self):
        source = self._source("int v1 = 1;", "using System;", "namespace N;", "[Union]", "int F(int x)", "where T : new()",
                              "int v7 = 7;", "int v8 = 8;", "int v9 = 9;", "int v10 = 10;")
        self.assertEqual(self._lines([], source), [])

    def test_an_assignment_an_arrow_a_return_a_throw_and_a_call_statement_are_listed(self):
        source = self._source("int v1 = 1;", "a = Holds.Value;", "A => a,", "return a;", "throw new E();", "Clear();",
                              "int v7 = 7;", "int v8 = 8;", "int v9 = 9;", "int v10 = 10;")
        self.assertEqual([line for _, line in self._lines([], source)], [2, 3, 4, 5, 6])

    def test_blank_lines_comments_and_lines_of_only_braces_are_not_listed(self):
        # Each line has 16 characters, so the spans of lines 2 to 6 stay the same.
        source = ("int v1 = 1 + 1;\n" + " " * 15 + "\n" + "// a comment   \n" + "    {          \n"
                  + "    });        \n" + "/// <summary>  \n" + "".join(f"int v{n} = {n} + 1;\n" for n in range(7, 11)))
        self.assertEqual(self._lines([], source), [])


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


LOG_WITHOUT_CAPTURE = "".join(line + "\n" for line in LOG.splitlines() if "coverage" not in line)


class EvidenceWithoutTheCoverageCapture(unittest.TestCase):
    """evidence and its text for a run with coverage-analysis off, whose log has no per-test coverage capture."""

    def setUp(self):
        self.evidence = changed.evidence(LOG_WITHOUT_CAPTURE, {"0", "1", "2"})

    def test_a_log_without_the_capture_gives_no_capture_seconds(self):
        self.assertIsNone(self.evidence["coverage_seconds"])

    def test_the_text_says_that_the_run_had_no_capture_not_that_the_capture_lost_its_end(self):
        text = changed._evidence_text("Core", self.evidence, {"seconds": 600, "watch": "load"})
        self.assertIn("no per-test coverage capture", text)
        self.assertNotIn("no end mark", text)

    def test_a_capture_that_started_and_never_ended_still_says_that_it_lost_its_end(self):
        log = LOG.replace("Per-test coverage capture complete", "Something else")
        text = changed._evidence_text("Core", changed.evidence(log, {"0"}), {"seconds": 600, "watch": "load"})
        self.assertIn("per-test coverage capture no end mark", text)


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

    def test_three_timeouts_of_twenty_from_mutants_that_hang_on_a_quiet_machine_leave_the_official_run_valid(self):
        # ter-2y6i's Simulation spans: the same three mutants timed out at a 1-minute load of 9 and of 173.
        rows = self._rows(*(["killed"] * 14 + ["survived"] * 3 + ["timeout"] * 3))
        self.assertEqual(changed.problems(rows, self._evidence()), [])

    def test_the_timeouts_of_a_run_that_load_broke_make_the_official_run_not_valid(self):
        # ter-u63a's trial at a 1-minute load of about 233: 65 of 76 mutants timed out.
        rows = self._rows(*(["killed"] * 11 + ["timeout"] * 65))
        problems = changed.problems(rows, self._evidence())
        self.assertEqual(len(problems), 1)
        self.assertIn("compare.py", problems[0])

    def test_a_timeout_under_a_load_above_the_calibrated_limit_makes_the_run_not_valid(self):
        rows = self._rows(*(["killed"] * 19 + ["timeout"]))
        problems = changed.problems(rows, self._evidence(), load_max=180.0, cores=10)
        self.assertEqual(len(problems), 1)
        self.assertIn("load", problems[0])
        self.assertIn("quieter machine", problems[0])

    def test_a_timeout_under_a_load_within_the_calibrated_limit_counts_as_a_kill(self):
        # ter-2y6i's three hangs repeated at a 1-minute load of 173 on this 10-core machine.
        rows = self._rows(*(["killed"] * 17 + ["timeout"] * 3))
        self.assertEqual(changed.problems(rows, self._evidence(), load_max=173.0, cores=10), [])

    def test_a_high_load_without_a_timeout_leaves_the_run_valid_because_load_only_slows_a_kill(self):
        rows = self._rows("killed", "survived")
        self.assertEqual(changed.problems(rows, self._evidence(), load_max=400.0, cores=10), [])

    def test_a_dubious_test_makes_the_run_not_valid_because_it_widens_every_mutant_to_that_test(self):
        problems = changed.problems(self._rows("killed"), self._evidence(dubious=3), max_timeout_share=0.10)
        self.assertTrue(any("Dubious" in p for p in problems), problems)


class TheLoadOfARun(unittest.TestCase):
    """test_threads and stryker_command: a run keeps its test threads within MAX_TEST_THREADS and runs at the lowest
    priority, so that the builders and GPU captures of other agents go first."""

    def test_the_test_hosts_of_a_run_together_use_at_most_four_threads_at_any_concurrency(self):
        for concurrency in range(1, 5):
            self.assertLessEqual(concurrency * config.test_threads(concurrency), 4, concurrency)

    def test_every_test_host_keeps_at_least_one_thread(self):
        self.assertEqual(config.test_threads(4), 1)

    def test_a_concurrency_above_four_is_refused_because_its_hosts_would_pass_the_bound(self):
        with self.assertRaises(ValueError):
            config.test_threads(5)

    def test_the_one_test_host_of_the_official_run_gets_all_four_threads(self):
        self.assertEqual(config.test_threads(config.CONCURRENCY), 4)

    def test_the_official_run_has_one_test_host_because_stryker_5_under_reports_kills_above_one(self):
        self.assertEqual(config.CONCURRENCY, 1)

    def test_each_mutant_runs_only_its_covering_tests_because_every_test_is_slow_on_the_mutated_assembly(self):
        # t9-al5u: with coverage-analysis off, each Simulation mutant of tiunr-e8xk's run3 ran every test on the mutated
        # assembly and took about 11 minutes, while the initial run of every test on the plain assembly took 33 s.
        with tempfile.TemporaryDirectory() as folder:
            path = config.write_config(folder, ["**/A.cs"], ["Core"])
            with open(path) as f:
                written = json.load(f)["stryker-config"]
        self.assertEqual(written["coverage-analysis"], "perTest")

    def test_stryker_starts_at_nice_20_with_the_processor_count_that_bounds_the_xunit_threads_of_its_configuration(self):
        with tempfile.TemporaryDirectory() as folder:
            config.write_config(folder, ["**/A.cs"], ["Core"], concurrency=2)
            command = run.stryker_command(Path(folder))
        self.assertEqual(command[:5], ["nice", "-n", "20", "env", "DOTNET_PROCESSOR_COUNT=2"])
        self.assertEqual(command[5:10], ["dotnet", "tool", "run", "dotnet-stryker", "--"])
        self.assertEqual(command[10:], ["-f", f"{folder}/stryker-config.json", "-O", f"{folder}/out", "-L"])

    def test_the_process_samples_keep_the_processes_of_the_stryker_group_with_their_priority_and_cpu(self):
        ps = ("  101   100  20  12.0 dotnet dotnet-stryker.dll -f c.json\n"
              "  102   100  20 398.5 dotnet /r/tests/Tertius.Core.Tests/bin/Checked/net10.0/Tertius.Core.Tests.dll --server\n"
              "  200   200   0 950.0 dotnet build Tertius.slnx\n")
        self.assertEqual(run.group_processes(ps, 100), [
            {"pid": 101, "nice": 20, "cpu": 12.0, "command": "dotnet dotnet-stryker.dll -f c.json"},
            {"pid": 102, "nice": 20, "cpu": 398.5,
             "command": "dotnet /r/tests/Tertius.Core.Tests/bin/Checked/net10.0/Tertius.Core.Tests.dll --server"}])


class TheTestHostsOfARun(unittest.TestCase):
    """hosts_text: the samples of the test hosts show the thread bound as the CPU of all hosts together, because Stryker
    starts one host per test project at once but runs one at a time."""

    @staticmethod
    def _host(cpu, nice=20):
        return {"pid": 1, "nice": nice, "cpu": cpu, "command": "dotnet T.Tests.dll --server"}

    def test_the_cpu_of_the_hosts_of_one_sample_adds_up(self):
        text = run.hosts_text([[self._host(380.0), self._host(0.0), self._host(5.0)], [self._host(100.0)]])
        self.assertIn("together at most 385% and on mean 242% of one core", text)

    def test_the_text_names_every_nice_value_and_the_most_host_processes_of_one_sample(self):
        text = run.hosts_text([[self._host(1.0), self._host(1.0, nice=0)], [self._host(1.0)]])
        self.assertIn("2 test host processes at most", text)
        self.assertIn("at nice 0, 20", text)

    def test_samples_without_a_host_say_so(self):
        self.assertEqual(run.hosts_text([[], []]), "no test host in the samples")


class TheMutatedAssemblyDuringARun(unittest.TestCase):
    """While its mutants run, Stryker keeps its mutated module assembly in each test project's output, and it restores
    the plain one just before it ends. A plain copy during that time is a build that replaced it."""

    INJECTED = '2026-11-02T01:38:50.50-05:00  [DBG] Injected the mutated assembly file into "/r/tests/T/bin/Core.dll" (b285eb76)\n'
    ENDED = "2026-11-02T02:10:37.61-05:00  [INF] Time Elapsed 00:33:09.1878252 (f4427d77)\n"

    def test_the_mutated_phase_starts_when_stryker_injects_its_assembly(self):
        self.assertFalse(run.mutated_phase("2026-11-02T01:37:53  [INF] Initial test run started.\n"))
        self.assertTrue(run.mutated_phase(self.INJECTED))

    def test_the_mutated_phase_ends_when_stryker_reports_its_time(self):
        self.assertFalse(run.mutated_phase(self.INJECTED + self.ENDED))

    def test_a_copy_without_strykers_helper_class_is_plain(self):
        with tempfile.TemporaryDirectory() as folder:
            mutated, plain = Path(folder) / "a.dll", Path(folder) / "b.dll"
            mutated.write_bytes(b"MZ...StrykerNamespace.MutantControl...")
            plain.write_bytes(b"MZ...Tertius.Core.Frames...")
            self.assertEqual(run.plain_assemblies([mutated, plain]), [plain])

    def test_a_copy_plain_in_two_samples_in_a_row_of_the_mutated_phase_was_replaced_by_a_build(self):
        samples = [(False, {"a"}), (True, set()), (True, {"a"}), (True, {"a"}), (True, set())]
        self.assertEqual(run.replaced_copies(samples), ["a"])

    def test_a_copy_plain_in_one_sample_only_is_strykers_own_restore_at_its_end(self):
        # Stryker restores the plain copy about 1 s before its Time Elapsed line, and the samples are 15 s apart.
        self.assertEqual(run.replaced_copies([(True, set()), (True, {"a"}), (False, {"a"}), (False, {"a"})]), [])

    def test_plain_copies_before_the_injection_count_for_nothing(self):
        self.assertEqual(run.replaced_copies([(False, {"a"}), (False, {"a"}), (True, set())]), [])


class ComparingRuns(unittest.TestCase):
    """differences: the per-mutant outcomes of two runs on the same spans, the check that can raise the concurrency."""

    @staticmethod
    def _rows(*results):
        return [{"id": f"Core-{n}", "file": "src/Tertius.Core/R.cs", "line": n, "change": "a -> b", "result": r}
                for n, r in enumerate(results, 1)]

    def test_runs_with_the_same_result_for_every_mutant_have_no_differences(self):
        self.assertEqual(compare.differences(self._rows("killed", "survived"), self._rows("killed", "survived")), [])

    def test_a_mutant_that_one_run_kills_and_the_other_reports_survived_is_a_difference(self):
        found = compare.differences(self._rows("killed", "killed"), self._rows("killed", "survived"))
        self.assertEqual(len(found), 1)
        self.assertIn("Core-2", found[0])
        self.assertIn("killed", found[0])
        self.assertIn("survived", found[0])

    def test_the_same_score_with_other_mutants_killed_is_still_a_difference(self):
        found = compare.differences(self._rows("killed", "survived"), self._rows("survived", "killed"))
        self.assertEqual(len(found), 2)

    def test_a_mutant_that_only_one_run_has_is_a_difference(self):
        found = compare.differences(self._rows("killed", "killed"), self._rows("killed"))
        self.assertEqual(len(found), 1)
        self.assertIn("Core-2", found[0])

    def test_a_mutant_id_at_another_place_is_a_difference_because_the_spans_differ(self):
        other = self._rows("killed")
        other[0]["line"] = 9
        self.assertEqual(len(compare.differences(self._rows("killed"), other)), 2)


# Each lock test gets a fresh folder under /private/tmp, never the real lock /tmp/tertius-stryker.lock.
SCRATCH = "/private/tmp" if os.path.isdir("/private/tmp") else None
# The time that a test gives a run to stop on a lock that it must wait for.
SETTLE = 1.0
# A start time that no live process has.
NEVER_STARTED = "Thu Jan 1 00:00:00 1970"


def start_time(pid):
    """The start time of the process pid as the record holds it: in UTC, with its spaces squeezed."""
    out = subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], env={**os.environ, "TZ": "UTC0", "LC_ALL": "C"},
                         capture_output=True, text=True, check=False).stdout
    return " ".join(out.split())


def read_record(path):
    holder = Path(path) / "holder"
    if not holder.exists():
        return {}
    return dict(line.split("=", 1) for line in holder.read_text().splitlines() if "=" in line)


class TheLock(unittest.TestCase):
    """Lock: one Stryker run at a time on the machine, through a folder that mkdir makes or refuses atomically. The
    folder holds the holder's record, in the format of the GPU lock (tools/README.md, "The record of a machine lock")."""

    def setUp(self):
        self.dir = tempfile.TemporaryDirectory(dir=SCRATCH, prefix="stryker-lock-test-")
        self.addCleanup(self.dir.cleanup)
        self.path = Path(self.dir.name) / "stryker.lock"
        assert str(self.path) != str(run.LOCK_PATH)

    def write_record(self, pid, start, token="someone-else", label="another run"):
        self.path.mkdir()
        (self.path / "holder").write_text(f"pid={pid}\nstart={start}\ntoken={token}\nlabel={label}\n")

    def sleeper(self):
        process = subprocess.Popen(["sleep", "60"])
        self.addCleanup(process.wait)
        self.addCleanup(process.kill)
        return process

    def dead_pid(self):
        process = subprocess.Popen(["true"])
        process.wait()
        return process.pid

    def waiting_run(self):
        """A run that waits for the lock in a thread. Its list gets "took" or the message with which it stopped. The
        end of the test removes the lock, so that a run that still waits takes it, releases it and ends."""
        outcome = []

        def acquire():
            try:
                with run.Lock(self.path, poll=0.01, say=lambda _: None):
                    outcome.append("took")
            except SystemExit as stop:
                outcome.append(str(stop))

        thread = threading.Thread(target=acquire, daemon=True)
        self.addCleanup(thread.join, 10)
        self.addCleanup(lambda: subprocess.run(["rm", "-rf", str(self.path)], check=False))
        thread.start()
        return outcome

    def test_inside_the_block_the_lock_folder_holds_the_record_of_this_process_and_after_it_the_folder_is_gone(self):
        with run.Lock(self.path, poll=0.01):
            record = read_record(self.path)
            self.assertEqual(record.get("pid"), str(os.getpid()))
            self.assertEqual(record.get("start"), start_time(os.getpid()))
            self.assertRegex(record.get("token", ""), r"\S{8,}")
            self.assertEqual(record.get("label"), os.getcwd())
        self.assertEqual(os.listdir(self.dir.name), [])

    def test_a_newline_in_the_runs_folder_cannot_split_the_holders_record(self):
        folder = Path(self.dir.name) / "run\ntoken=foreign"
        folder.mkdir()
        self.addCleanup(os.chdir, os.getcwd())
        os.chdir(folder)
        with run.Lock(self.path, poll=0.01) as lock:
            self.assertEqual(len((self.path / "holder").read_text().splitlines()), 4)
            self.assertTrue(lock.held())
        self.assertFalse(self.path.exists())

    def test_a_run_whose_ps_gives_no_start_time_removes_the_lock_and_stops(self):
        with mock.patch.object(run, "start_of", lambda pid: ""):
            with self.assertRaises(SystemExit) as stop:
                with run.Lock(self.path, poll=0.01):
                    self.fail("the run went on without a record")
        self.assertIn("record", str(stop.exception))
        self.assertEqual(os.listdir(self.dir.name), [])

    def test_two_runs_get_different_tokens(self):
        with run.Lock(self.path, poll=0.01):
            token = read_record(self.path)["token"]
        with run.Lock(self.path, poll=0.01):
            self.assertNotEqual(read_record(self.path)["token"], token)

    def test_an_exception_inside_the_block_still_releases_the_lock(self):
        with self.assertRaises(RuntimeError):
            with run.Lock(self.path, poll=0.01):
                raise RuntimeError("Stryker failed")
        self.assertFalse(self.path.exists())

    def test_a_second_run_waits_until_the_first_releases_the_lock(self):
        order = []
        holder = run.Lock(self.path, poll=0.01)
        holder.__enter__()

        def second():
            with run.Lock(self.path, poll=0.01, say=lambda _: None):
                order.append("second")

        thread = threading.Thread(target=second)
        thread.start()
        time.sleep(0.2)
        order.append("first released")
        holder.__exit__(None, None, None)
        thread.join(timeout=5)
        self.assertEqual(order, ["first released", "second"])

    def test_a_release_keeps_its_record_until_the_public_lock_name_is_free_for_the_next_run(self):
        first = run.Lock(self.path, poll=0.01)
        second = run.Lock(self.path, poll=0.01)
        self.addCleanup(first.release)
        self.addCleanup(second.release)
        first.acquire()
        remove = run.shutil.rmtree

        def delete_released(folder):
            self.assertFalse(self.path.exists())
            self.assertEqual(read_record(Path(folder)).get("token"), first.token)
            second.acquire()
            remove(folder)

        with mock.patch.object(run.shutil, "rmtree", side_effect=delete_released):
            first.release()
        self.assertTrue(second.held())
        second.release()
        self.assertEqual(os.listdir(self.dir.name), [])

    def test_a_release_leaves_a_lock_that_another_process_holds(self):
        self.write_record(os.getppid(), start_time(os.getppid()))
        lock = run.Lock(self.path, poll=0.01)
        lock.release()
        self.assertEqual(read_record(self.path).get("token"), "someone-else")

    def test_a_release_leaves_a_lock_that_another_run_of_this_process_took_since(self):
        # The first run's lock was removed by hand, and a second run took it. Both runs have this process id.
        first = run.Lock(self.path, poll=0.01)
        first.acquire()
        subprocess.run(["rm", "-r", str(self.path)], check=True)
        second = run.Lock(self.path, poll=0.01)
        second.acquire()
        self.assertFalse(first.held())
        first.release()
        self.assertTrue(second.held())
        second.release()
        self.assertEqual(os.listdir(self.dir.name), [])

    def test_a_lock_folder_without_a_complete_record_for_more_than_a_minute_stops_the_run_with_the_command_that_removes_it(self):
        # A run that died between its mkdir and its record, or a mkdir by hand. A record that lacks its pid, start or
        # token counts as no record.
        for lacks in [None, "pid", "start", "token"]:
            with self.subTest(lacks=lacks):
                subprocess.run(["rm", "-rf", str(self.path)], check=True)
                record = {"pid": self.dead_pid(), "start": NEVER_STARTED, "token": "someone-else"}
                if lacks is None:
                    self.path.mkdir()
                else:
                    record[lacks] = ""
                    self.write_record(**record)
                old = time.time() - 2 * run.STALE_SECONDS
                os.utime(self.path, (old, old))
                with self.assertRaises(SystemExit) as stop:
                    with run.Lock(self.path, poll=0.01):
                        pass
                self.assertIn("no complete record", str(stop.exception))
                self.assertIn(f"rm -r {self.path}", str(stop.exception))
                self.assertTrue(self.path.exists())

    def test_a_stryker_run_without_the_lock_of_this_process_is_refused_before_it_starts(self):
        with tempfile.TemporaryDirectory(dir=SCRATCH) as folder:
            with self.assertRaises(RuntimeError):
                run.run_module(folder, "Core", Path(folder), run.Lock(self.path))
            self.assertEqual(os.listdir(folder), [])

    def test_a_lock_whose_holder_process_is_gone_stops_the_run_with_the_command_that_removes_it(self):
        self.write_record(self.dead_pid(), NEVER_STARTED, label="a run that died")
        outcome = self.waiting_run()
        self.assertTrue(wait_for(lambda: outcome))
        self.assertIn("a run that died", outcome[0])
        self.assertIn(f"rm -r {self.path}", outcome[0])
        self.assertEqual(read_record(self.path).get("token"), "someone-else")

    def test_a_reused_process_id_with_another_start_time_stops_the_run_with_the_command_that_removes_it(self):
        self.write_record(self.sleeper().pid, NEVER_STARTED)
        outcome = self.waiting_run()
        self.assertTrue(wait_for(lambda: outcome))
        self.assertIn(f"rm -r {self.path}", outcome[0])
        self.assertTrue(self.path.exists())

    def test_a_live_holder_is_waited_for_when_ps_gives_nothing_and_judged_gone_once_its_process_ends(self):
        holder = self.sleeper()
        self.write_record(holder.pid, start_time(holder.pid))
        with mock.patch.object(run, "start_of", lambda pid: ""):
            outcome = self.waiting_run()
            time.sleep(SETTLE)
            self.assertEqual(outcome, [])
            holder.kill()
            holder.wait()
            self.assertTrue(wait_for(lambda: outcome))
        self.assertIn(f"rm -r {self.path}", outcome[0])

    def test_a_live_holder_in_another_time_zone_is_waited_for(self):
        held, release = Path(self.dir.name) / "held", Path(self.dir.name) / "release"
        script = ("import importlib.util, os, sys, time\n"
                  "spec = importlib.util.spec_from_file_location('run', sys.argv[1])\n"
                  "run = importlib.util.module_from_spec(spec)\n"
                  "spec.loader.exec_module(run)\n"
                  "with run.Lock(sys.argv[2], poll=0.01):\n"
                  "    open(sys.argv[3], 'w').close()\n"
                  "    while not os.path.exists(sys.argv[4]):\n"
                  "        time.sleep(0.02)\n")
        holder = subprocess.Popen([sys.executable, "-I", "-c", script, str(TOOLS / "stryker" / "run.py"), str(self.path),
                                   str(held), str(release)], env={**os.environ, "TZ": "Asia/Tokyo"})
        self.addCleanup(holder.wait, 10)
        self.addCleanup(release.touch)
        self.assertTrue(wait_for(held.exists))
        self.assertEqual(read_record(self.path).get("start"), start_time(holder.pid))
        with mock.patch.dict(os.environ, {"TZ": "America/Los_Angeles"}):
            outcome = self.waiting_run()
            time.sleep(SETTLE)
            self.assertEqual(outcome, [])
            release.touch()
            self.assertTrue(wait_for(lambda: outcome))
        self.assertEqual(outcome, ["took"])

    def test_a_lock_of_a_run_before_ter_s4fg_counts_as_held_while_its_process_runs(self):
        # The run.py of a worktree from before ter-s4fg writes the file owner (pid, folder, local time) and no record.
        holder = self.sleeper()
        self.path.mkdir()
        (self.path / "owner").write_text(f"{holder.pid} /some/worktree 2026-11-04T12:00:00\n")
        old = time.time() - 2 * run.STALE_SECONDS
        os.utime(self.path, (old, old))
        outcome = self.waiting_run()
        time.sleep(SETTLE)
        self.assertEqual(outcome, [])
        holder.kill()
        holder.wait()
        self.assertTrue(wait_for(lambda: outcome))
        self.assertIn(f"rm -r {self.path}", outcome[0])
        self.assertTrue((self.path / "owner").exists())

    def test_the_waiting_message_names_the_holder_of_the_lock(self):
        holder = self.sleeper()
        old_owner = f"process {holder.pid} of a run.py from before ter-s4fg"
        for case, writes, named in [
            ("record", lambda: self.write_record(holder.pid, start_time(holder.pid), label="/other/worktree"),
             f"/other/worktree (process {holder.pid})"),
            ("owner of a run.py before ter-s4fg", lambda: (self.path.mkdir(), (self.path / "owner").write_text(
                f"{holder.pid} /some/worktree 2026-11-04T12:00:00\n")), old_owner),
            ("nothing yet", self.path.mkdir, "a run that is starting"),
        ]:
            with self.subTest(case=case):
                subprocess.run(["rm", "-rf", str(self.path)], check=True)
                writes()
                said = []
                waiter = threading.Thread(target=lambda: run.Lock(self.path, poll=0.01, say=said.append).acquire(),
                                          daemon=True)
                waiter.start()
                self.assertTrue(wait_for(lambda: said))
                self.assertEqual(said[0], f"waiting for the Stryker lock {self.path}, held by {named}")
                # The waiter takes the empty path, gets its own record, and the thread ends.
                subprocess.run(["rm", "-rf", str(self.path)], check=True)
                waiter.join(10)
                subprocess.run(["rm", "-rf", str(self.path)], check=True)


def wait_for(condition, timeout=10.0):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if condition():
            return True
        time.sleep(0.02)
    return condition()


if __name__ == "__main__":
    unittest.main()

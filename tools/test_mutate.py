#!/usr/bin/env python3
"""Tool code. The tests of the pure parts of tools/mutate.py and tools/mutation_table.py: reading and checking the list
of mutants, applying and restoring a mutant, classifying a test run, and the shared table.

    nix develop --command python3 -I tools/test_mutate.py

The standard library only. The file loads its modules by their paths, because -I keeps the tools folder off sys.path.
"""

import importlib.util
import json
import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parent


def _load(name, file):
    spec = importlib.util.spec_from_file_location(name, TOOLS / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


mutate = _load("mutate", "mutate.py")
table = _load("mutation_table", "mutation_table.py")


def _mutant(**fields):
    """One valid entry of a mutant list for the repository of _Repo, with fields replaced."""
    entry = {
        "id": "C01",
        "file": "src/A/Thing.cs",
        "line": 2,
        "original": "x + 1",
        "replacement": "x - 1",
        "project": "tests/A.Tests",
        "class": "ThingTests",
    }
    entry.update(fields)
    return entry


class _Repo:
    """A folder that looks like the repository to validate: src/A/Thing.cs with three lines and a test project folder
    tests/A.Tests with its .csproj. The git state comes in as two sets, so the tests need no git."""

    def __init__(self):
        self.dir = tempfile.TemporaryDirectory()
        self.root = Path(self.dir.name)
        (self.root / "src/A").mkdir(parents=True)
        (self.root / "src/A/Thing.cs").write_text("int F(int x)\n    => x + 1;\n// x + 1 once more\n")
        (self.root / "tests/A.Tests").mkdir(parents=True)
        (self.root / "tests/A.Tests/A.Tests.csproj").write_text("<Project />\n")
        self.tracked = {"src/A/Thing.cs", "tests/A.Tests/A.Tests.csproj"}
        self.dirty = set()

    def errors(self, mutants):
        return mutate.validate(mutants, self.root, self.tracked, self.dirty)

    def close(self):
        self.dir.cleanup()


class ReadingTheList(unittest.TestCase):
    """parse_list: the JSON text of a mutant list becomes a list of mutants, or the errors that stop the run."""

    def test_a_list_of_complete_entries_reads_as_mutants_with_their_classes_as_a_list(self):
        mutants, errors = mutate.parse_list(json.dumps([_mutant(), _mutant(id="C02", **{"class": ["A", "B"]})]))
        self.assertEqual(errors, [])
        self.assertEqual([m["classes"] for m in mutants], [["ThingTests"], ["A", "B"]])

    def test_an_entry_without_a_required_field_is_an_error_that_names_the_entry_and_the_field(self):
        entry = _mutant()
        del entry["replacement"]
        _, errors = mutate.parse_list(json.dumps([entry]))
        self.assertEqual(len(errors), 1)
        self.assertIn("C01", errors[0])
        self.assertIn("replacement", errors[0])

    def test_a_field_with_an_unknown_name_is_an_error_so_that_a_misspelled_field_is_never_ignored(self):
        _, errors = mutate.parse_list(json.dumps([_mutant(classes="ThingTests")]))
        self.assertTrue(any("classes" in e for e in errors), errors)

    def test_two_entries_with_the_same_id_are_an_error(self):
        _, errors = mutate.parse_list(json.dumps([_mutant(), _mutant(line=3)]))
        self.assertTrue(any("C01" in e and "twice" in e for e in errors), errors)

    def test_a_line_that_is_not_a_positive_whole_number_is_an_error(self):
        for line in (0, "2", 1.5, True):
            _, errors = mutate.parse_list(json.dumps([_mutant(line=line)]))
            self.assertTrue(errors, line)

    def test_a_replacement_equal_to_the_original_is_an_error_because_it_changes_nothing(self):
        _, errors = mutate.parse_list(json.dumps([_mutant(replacement="x + 1")]))
        self.assertTrue(errors)

    def test_an_empty_original_is_an_error_because_it_has_no_place(self):
        _, errors = mutate.parse_list(json.dumps([_mutant(original="")]))
        self.assertTrue(errors)

    def test_an_empty_replacement_is_a_valid_mutant_that_deletes_the_original(self):
        _, errors = mutate.parse_list(json.dumps([_mutant(replacement="")]))
        self.assertEqual(errors, [])

    def test_an_empty_list_of_classes_is_an_error_because_the_run_would_test_nothing(self):
        _, errors = mutate.parse_list(json.dumps([_mutant(**{"class": []})]))
        self.assertTrue(errors)

    def test_text_that_is_not_a_json_list_is_one_error(self):
        for text in ("{", '{"id": "C01"}'):
            mutants, errors = mutate.parse_list(text)
            self.assertEqual((mutants, len(errors)), ([], 1), text)

    def test_a_note_is_optional_and_kept(self):
        mutants, errors = mutate.parse_list(json.dumps([_mutant(note="off by one")]))
        self.assertEqual((errors, mutants[0]["note"]), ([], "off by one"))


class CheckingTheListAgainstTheRepository(unittest.TestCase):
    """validate: every mutant of the list must apply to the repository as it is, before the first change."""

    def setUp(self):
        self.repo = _Repo()
        self.addCleanup(self.repo.close)

    def _parsed(self, *entries):
        mutants, errors = mutate.parse_list(json.dumps(list(entries)))
        self.assertEqual(errors, [])
        return mutants

    def test_a_mutant_whose_original_starts_on_its_line_is_valid(self):
        self.assertEqual(self.repo.errors(self._parsed(_mutant())), [])

    def test_a_mutant_whose_original_text_starts_on_another_line_is_an_error(self):
        errors = self.repo.errors(self._parsed(_mutant(line=1)))
        self.assertTrue(any("line 1" in e for e in errors), errors)

    def test_an_original_that_spans_lines_is_valid_when_it_starts_on_its_line(self):
        self.assertEqual(self.repo.errors(self._parsed(_mutant(line=1, original="x)\n    => x"))), [])

    def test_an_original_that_starts_twice_on_its_line_is_an_error_because_the_place_is_ambiguous(self):
        (self.repo.root / "src/A/Thing.cs").write_text("int F(int x)\n    => x + 1 + x + 1;\n")
        errors = self.repo.errors(self._parsed(_mutant()))
        self.assertTrue(any("twice" in e for e in errors), errors)

    def test_a_file_that_does_not_exist_is_an_error(self):
        errors = self.repo.errors(self._parsed(_mutant(file="src/A/Missing.cs")))
        self.assertTrue(any("Missing.cs" in e for e in errors), errors)

    def test_a_file_with_an_uncommitted_change_is_an_error_because_a_restore_would_keep_that_change_unknown(self):
        self.repo.dirty.add("src/A/Thing.cs")
        errors = self.repo.errors(self._parsed(_mutant()))
        self.assertTrue(any("uncommitted" in e for e in errors), errors)

    def test_a_file_that_git_does_not_track_is_an_error_because_git_cannot_show_it_unchanged_at_the_end(self):
        self.repo.tracked.discard("src/A/Thing.cs")
        errors = self.repo.errors(self._parsed(_mutant()))
        self.assertTrue(any("track" in e for e in errors), errors)

    def test_a_file_outside_the_repository_is_an_error(self):
        errors = self.repo.errors(self._parsed(_mutant(file="../outside.cs")))
        self.assertTrue(errors)

    def test_a_test_project_folder_without_its_csproj_is_an_error(self):
        errors = self.repo.errors(self._parsed(_mutant(project="tests/B.Tests")))
        self.assertTrue(any("B.Tests" in e for e in errors), errors)

    def test_every_error_of_the_list_is_reported_not_only_the_first(self):
        errors = self.repo.errors(self._parsed(_mutant(line=1), _mutant(id="C02", file="src/A/Missing.cs")))
        self.assertEqual(len(errors), 2, errors)


class ApplyingAndRestoring(unittest.TestCase):
    """locate and applied: a mutant replaces the original text at its line, and the file gets its exact bytes back."""

    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        self.path = Path(self.dir.name) / "Thing.cs"
        # A byte-order mark, a CRLF line and a character outside ASCII, so that a restore through text would differ.
        self.original = "﻿int F(int x)\r\n    => x + 1; // é\n".encode("utf-8")
        self.path.write_bytes(self.original)

    def test_the_offset_of_the_original_is_where_its_bytes_start_on_its_line(self):
        self.assertEqual(mutate.locate(self.original, 2, "x + 1"), self.original.index(b"x + 1"))

    def test_an_original_not_on_its_line_has_no_offset(self):
        self.assertIsNone(mutate.locate(self.original, 1, "x + 1"))

    def test_inside_the_block_the_file_holds_the_mutant(self):
        with mutate.applied(self.path, 2, "x + 1", "x - 1"):
            self.assertEqual(self.path.read_bytes(), self.original.replace(b"x + 1", b"x - 1"))

    def test_after_the_block_the_file_holds_the_exact_original_bytes(self):
        with mutate.applied(self.path, 2, "x + 1", "x - 1"):
            pass
        self.assertEqual(self.path.read_bytes(), self.original)

    def test_an_exception_inside_the_block_still_restores_the_file(self):
        with self.assertRaises(RuntimeError):
            with mutate.applied(self.path, 2, "x + 1", "x - 1"):
                raise RuntimeError("the build crashed")
        self.assertEqual(self.path.read_bytes(), self.original)

    def test_the_restored_file_is_newer_than_the_mutant_so_that_the_next_build_compiles_it_again(self):
        with mutate.applied(self.path, 2, "x + 1", "x - 1"):
            mutated = self.path.stat().st_mtime_ns
            # The build outputs of the mutant are newer than the mutant file.
            time.sleep(0.01)
            output = Path(self.dir.name) / "Thing.dll"
            output.write_bytes(b"")
        self.assertGreater(self.path.stat().st_mtime_ns, mutated)
        self.assertGreater(self.path.stat().st_mtime_ns, output.stat().st_mtime_ns)

    def test_a_sigterm_inside_the_block_restores_the_file_before_the_process_ends(self):
        script = (
            "import importlib.util, os, signal, sys, time\n"
            f"spec = importlib.util.spec_from_file_location('mutate', {str(TOOLS / 'mutate.py')!r})\n"
            "mutate = importlib.util.module_from_spec(spec); spec.loader.exec_module(mutate)\n"
            "mutate.stop_on_signals()\n"
            "with mutate.applied(sys.argv[1], 2, 'x + 1', 'x - 1'):\n"
            "    print('applied', flush=True)\n"
            "    time.sleep(30)\n"
        )
        process = subprocess.Popen([sys.executable, "-I", "-c", script, str(self.path)], stdout=subprocess.PIPE, text=True)
        self.assertEqual(process.stdout.readline().strip(), "applied")
        self.assertNotEqual(self.path.read_bytes(), self.original)
        process.send_signal(signal.SIGTERM)
        process.wait(timeout=10)
        process.stdout.close()
        self.assertNotEqual(process.returncode, 0)
        self.assertEqual(self.path.read_bytes(), self.original)


class ClassifyingATestRun(unittest.TestCase):
    """classify: the exit code, the timeout and the output of one dotnet test run of a mutant give its result and up to
    three names of failed tests. The output lines are those of Microsoft.Testing.Platform, as tools/test-all.sh reads
    them."""

    @staticmethod
    def _output(total, failed, names=()):
        lines = [f"failed Tertius.Rendering.Tests.{n} (12ms)" for n in names]
        lines += ["Test run summary: Failed!" if failed else "Test run summary: Passed!", f"  total: {total}",
                  f"  failed: {failed}", f"  succeeded: {total - failed}", "  skipped: 0", "  duration: 3s 120ms"]
        return "\n".join(lines) + "\n"

    def test_a_run_whose_tests_all_pass_means_the_mutant_survived(self):
        self.assertEqual(mutate.classify(0, False, self._output(12, 0)), ("survived", []))

    def test_a_run_with_failed_tests_kills_the_mutant_and_names_them_without_their_namespace(self):
        output = self._output(12, 1, ["OwnRayListTests.HeaderHoldsTheCount"])
        self.assertEqual(mutate.classify(2, False, output), ("killed", ["OwnRayListTests.HeaderHoldsTheCount"]))

    def test_a_kill_names_at_most_three_failed_tests(self):
        output = self._output(12, 5, [f"T.Case{i}" for i in range(5)])
        result, names = mutate.classify(2, False, output)
        self.assertEqual((result, names), ("killed", ["T.Case0", "T.Case1", "T.Case2"]))

    def test_a_theory_case_keeps_its_arguments_after_the_class_and_method(self):
        output = self._output(3, 1, ["PyramidRenderTests.Level(size: 1.5, odd: True)"])
        self.assertEqual(mutate.classify(2, False, output)[1], ["PyramidRenderTests.Level(size: 1.5, odd: True)"])

    def test_a_run_that_the_timeout_stopped_is_a_timeout_whatever_it_printed(self):
        self.assertEqual(mutate.classify(-9, True, self._output(12, 0)), ("timeout", []))

    def test_a_run_that_ran_zero_tests_is_no_tests_and_never_survived(self):
        self.assertEqual(mutate.classify(8, False, self._output(0, 0))[0], "no-tests")

    def test_exit_code_eight_is_no_tests_even_without_a_summary(self):
        self.assertEqual(mutate.classify(8, False, "")[0], "no-tests")

    def test_a_test_host_that_crashed_before_its_summary_kills_the_mutant(self):
        self.assertEqual(mutate.classify(7, False, "Test host process exited\n"), ("killed", ["(test host exit code 7)"]))

    def test_a_run_with_exit_code_zero_and_no_summary_is_no_tests_because_nothing_shows_that_a_test_ran(self):
        self.assertEqual(mutate.classify(0, False, "")[0], "no-tests")

    def test_a_failing_exit_code_with_a_passing_summary_kills_the_mutant(self):
        # For example a leak check or a validation layer that fails the session after every test passed.
        self.assertEqual(mutate.classify(3, False, self._output(12, 0)), ("killed", ["(test run exit code 3)"]))


class TestFilters(unittest.TestCase):
    """filter_arguments: the classes of a mutant become the --filter-class options of dotnet test."""

    def test_a_bare_class_name_matches_that_class_in_any_namespace(self):
        self.assertEqual(mutate.filter_arguments(["OwnRayListTests"]), ["--filter-class", "*.OwnRayListTests"])

    def test_a_qualified_name_or_a_pattern_passes_unchanged(self):
        self.assertEqual(
            mutate.filter_arguments(["Tertius.Core.Tests.ResultTests", "*Pyramid*"]),
            ["--filter-class", "Tertius.Core.Tests.ResultTests", "--filter-class", "*Pyramid*"],
        )


class TheSharedTable(unittest.TestCase):
    """mutation_table: one Markdown row per mutant and one summary line with a count for every result, the same shape
    for tools/mutate.py and tools/stryker/changed.py."""

    @staticmethod
    def _row(**fields):
        row = {"id": "S01", "file": "src/A/x.comp", "line": 4, "change": "a || b -> a && b", "result": "killed",
               "killed_by": ["T.A", "T.B"], "seconds": 12.34}
        row.update(fields)
        return row

    def test_a_row_shows_the_place_the_change_the_result_the_killers_and_the_seconds(self):
        lines = table.format_rows([self._row()])
        self.assertEqual(lines[0], "| id | place | change | result | killed by | s |")
        self.assertEqual(lines[2], "| S01 | src/A/x.comp:4 | a \\|\\| b -> a && b | killed | T.A, T.B | 12.3 |")

    def test_a_row_without_seconds_shows_a_dash(self):
        self.assertEqual(table.format_rows([self._row(seconds=None)])[2].rsplit("|", 2)[1].strip(), "-")

    def test_a_long_change_is_shortened_on_one_line(self):
        cell = table.short_change("int a =\n        b * c + d * e + f * g + h * i + j * k + l * m + n * o + p;", "")
        self.assertLessEqual(len(cell), table.CHANGE_WIDTH)
        self.assertNotIn("\n", cell)
        self.assertTrue(cell.startswith("int a = b * c"), cell)

    def test_the_summary_counts_every_result_in_a_fixed_order_also_those_with_no_row(self):
        rows = [self._row(), self._row(result="survived"), self._row(result="killed")]
        self.assertEqual(
            table.summary(rows),
            "3 rows: killed 2, timeout 0, survived 1, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0",
        )

    def test_a_row_with_an_unknown_result_is_a_bug_of_the_caller(self):
        with self.assertRaises(ValueError):
            table.summary([self._row(result="Killed")])


if __name__ == "__main__":
    unittest.main()

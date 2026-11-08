#!/usr/bin/env python3
"""Tool code. The tests of the pure parts of tools/mutate.py, tools/mutation_table.py and tools/processes.py: reading
and checking the list of mutants, finding the project of a file, applying and restoring a mutant also on a signal,
classifying a test run, and the shared table.

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
from unittest.mock import patch
from pathlib import Path

TOOLS = Path(__file__).resolve().parent


def _load(name, file):
    spec = importlib.util.spec_from_file_location(name, TOOLS / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


mutate = _load("mutate", "mutate.py")
table = _load("mutation_table", "mutation_table.py")
processes = _load("processes", "processes.py")


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
    """A folder that looks like the repository to validate: src/A/Thing.cs with three lines in the project src/A/A.csproj,
    and a test project folder tests/A.Tests with its .csproj. The git state comes in as two sets, so the tests need no git."""

    def __init__(self):
        self.dir = tempfile.TemporaryDirectory()
        self.root = Path(self.dir.name)
        (self.root / "src/A").mkdir(parents=True)
        (self.root / "src/A/Thing.cs").write_text("int F(int x)\n    => x + 1;\n// x + 1 once more\n")
        (self.root / "src/A/A.csproj").write_text("<Project />\n")
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

    def test_a_file_that_belongs_to_no_project_is_an_error_because_no_test_build_holds_its_mutant(self):
        (self.repo.root / "global.json").write_text('{"sdk": {"version": "10.0.100"}}\n')
        self.repo.tracked.add("global.json")
        errors = self.repo.errors(self._parsed(_mutant(file="global.json", line=1, original="10.0", replacement="9.0")))
        self.assertTrue(any("no project" in e for e in errors), errors)


class StagedMergeInputs(unittest.TestCase):
    """A real pending merge retains its source bytes, staged prerequisites and merge identities."""

    def setUp(self):
        self.repo = _Repo()
        self.addCleanup(self.repo.close)
        self.root = self.repo.root
        self.path = self.root / "src/A/Thing.cs"
        self.git("init", "-b", "main")
        self.git("config", "user.name", "Mutation tests")
        self.git("config", "user.email", "mutation@example.invalid")
        self.git("config", "core.hooksPath", "/dev/null")
        self.git("add", ".")
        self.git("commit", "-qm", "base")
        self.git("checkout", "-qb", "prerequisite")
        (self.root / "prerequisite.txt").write_text("keep this import\n")
        self.git("add", "prerequisite.txt")
        self.git("commit", "-qm", "prerequisite")
        self.git("checkout", "main")
        (self.root / "local.txt").write_text("local branch\n")
        self.git("add", "local.txt")
        self.git("commit", "-qm", "local")
        self.git("merge", "--no-commit", "--no-ff", "prerequisite")
        self.path.write_bytes(b"int F(int x)\r\n    => x + 1; // staged\n")
        self.git("add", "src/A/Thing.cs")
        self.mutants, errors = mutate.parse_list(json.dumps([_mutant()]))
        self.assertEqual(errors, [])
        self.out = self.root / "evidence"
        self.out.mkdir()
        self.baseline = mutate.Inputs(self.root, self.mutants)

    def git(self, *args):
        return subprocess.check_output(["git", "-C", str(self.root), *args], stderr=subprocess.DEVNULL)

    def finish(self):
        with patch.object(mutate, "_build", return_value=True):
            return mutate._finish(self.root, self.mutants, self.out, [], None, self.baseline)

    def test_restored_staged_source_is_not_reported_as_corruption(self):
        before = self.path.read_bytes()
        index = self.git("ls-files", "--stage", "-z")
        identities = self.git("rev-parse", "HEAD", "MERGE_HEAD")
        with mutate.applied(self.path, 2, "x + 1", "x - 1"):
            self.assertIn(b"x - 1", self.path.read_bytes())
        self.assertIsNone(self.finish())
        self.assertEqual(self.path.read_bytes(), before)
        self.assertEqual(self.git("ls-files", "--stage", "-z"), index)
        self.assertEqual(self.git("rev-parse", "HEAD", "MERGE_HEAD"), identities)

    def test_an_unexpected_writer_is_not_overwritten_by_restoration(self):
        with self.assertRaisesRegex(RuntimeError, "changed|unexpected|restor"):
            with mutate.applied(self.path, 2, "x + 1", "x - 1"):
                self.path.write_bytes(b"another worker's edit\n")
        self.assertEqual(self.path.read_bytes(), b"another worker's edit\n")

    def admission(self, allow_staged):
        return mutate.validate(self.mutants, self.root, set(mutate._git_lines(self.root, "ls-files", "-z")),
                               mutate._dirty(self.root), allow_staged)

    def test_default_admission_rejects_staged_source(self):
        self.assertTrue(any("uncommitted" in error for error in self.admission(False)))

    def test_explicit_opt_in_admits_exact_stage_zero_source(self):
        self.assertEqual(self.admission(True), [])

    def test_a_sigkill_leftover_does_not_become_an_accepted_baseline(self):
        self.path.write_bytes(self.path.read_bytes().replace(b"x + 1", b"x - 1"))
        self.assertTrue(any("staged" in error for error in self.admission(True)))

    def test_untracked_source_is_rejected_also_with_opt_in(self):
        self.git("rm", "--cached", "src/A/Thing.cs")
        self.assertTrue(any("track" in error for error in self.admission(True)))

    def test_unmerged_source_is_rejected_also_with_opt_in(self):
        object_id = self.git("rev-parse", ":src/A/Thing.cs").decode().strip()
        self.git("update-index", "--force-remove", "src/A/Thing.cs")
        subprocess.run(["git", "-C", str(self.root), "update-index", "--index-info"],
                       input=f"100644 {object_id} 1\tsrc/A/Thing.cs\n".encode(), check=True)
        self.assertTrue(any("stage" in error for error in self.admission(True)))

    def test_unsupported_symlink_inputs_are_rejected(self):
        self.path.unlink()
        self.path.symlink_to("A.csproj")
        self.git("add", "src/A/Thing.cs")
        with self.assertRaisesRegex(mutate.InputChanged, "regular"):
            mutate.Inputs(self.root, self.mutants)

    def test_final_check_detects_byte_corruption_without_changing_the_dirty_path_set(self):
        self.path.write_bytes(b"later corruption\n")
        self.assertIn("bytes or mode changed", self.finish())
        self.assertEqual(self.path.read_bytes(), b"later corruption\n")

    def test_final_check_detects_a_new_staged_object_on_the_same_path(self):
        self.path.write_bytes(b"later stage\n")
        self.git("add", "src/A/Thing.cs")
        self.path.write_bytes(self.baseline.files["src/A/Thing.cs"][0])
        self.assertIn("staged entries changed", self.finish())
        self.assertEqual(self.git("show", ":src/A/Thing.cs"), b"later stage\n")

    def test_unrelated_staged_prerequisites_are_also_protected(self):
        (self.root / "prerequisite.txt").write_text("changed import\n")
        self.git("add", "prerequisite.txt")
        self.assertIn("staged entries changed", self.finish())

    def test_git_index_refresh_does_not_change_semantic_staged_entries(self):
        self.git("update-index", "--refresh")
        self.assertIsNone(self.finish())

    def test_final_check_detects_pending_merge_metadata_changes(self):
        merge = Path(self.git("rev-parse", "--git-path", "MERGE_HEAD").decode().strip())
        (self.root / merge).write_text("0" * 40 + "\n")
        self.assertIn("pending merge metadata changed", self.finish())

    def test_an_exception_restores_staged_source_and_preserves_git_state(self):
        with self.assertRaisesRegex(RuntimeError, "build crashed"):
            with mutate.applied(self.path, 2, "x + 1", "x - 1", self.baseline.files["src/A/Thing.cs"]):
                raise RuntimeError("build crashed")
        self.assertEqual(self.baseline.check(), [])

    def test_an_interruption_restores_staged_source_and_preserves_git_state(self):
        with self.assertRaises(KeyboardInterrupt):
            with mutate.applied(self.path, 2, "x + 1", "x - 1", self.baseline.files["src/A/Thing.cs"]):
                raise KeyboardInterrupt
        self.assertEqual(self.baseline.check(), [])

    def test_changed_source_is_not_mutated_from_a_new_baseline(self):
        self.path.write_bytes(b"later corruption\n")
        with self.assertRaisesRegex(mutate.InputChanged, "before mutation"):
            with mutate.applied(self.path, 2, "x + 1", "x - 1", self.baseline.files["src/A/Thing.cs"]):
                self.fail("unsafe mutation started")

    def test_a_real_termination_signal_restores_staged_bytes_without_changing_the_merge(self):
        script = f"""
import sys, time
from pathlib import Path
sys.path.insert(0, {str(TOOLS)!r})
import mutate
mutate.stop_on_signals()
with mutate.applied(Path({str(self.path)!r}), 2, 'x + 1', 'x - 1'):
    print('mutated', flush=True)
    time.sleep(20)
"""
        with subprocess.Popen([sys.executable, "-I", "-c", script], stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, text=True) as child:
            self.assertEqual(child.stdout.readline().strip(), "mutated")
            child.terminate()
            child.communicate(timeout=5)
        self.assertEqual(self.baseline.check(), [])

    def test_a_successful_mutation_run_restores_staged_source_and_all_prerequisites(self):
        assembly = self.root / "tests/A.Tests/bin/Checked/net11.0/A.dll"
        assembly.parent.mkdir(parents=True)

        def build(*args):
            assembly.write_bytes(b"test fixture assembly")
            timestamp = self.path.stat().st_mtime_ns + 1
            os.utime(assembly, ns=(timestamp, timestamp))
            return True

        def test(*args):
            self.assertIn(b"x - 1", self.path.read_bytes())
            self.assertEqual(self.git("show", ":src/A/Thing.cs"), self.baseline.files["src/A/Thing.cs"][0])
            return "killed", ["ThingTests.TheChangedOperationIsRejected"]

        rows = []
        with patch.object(mutate, "_build", side_effect=build), patch.object(mutate, "_test", side_effect=test):
            self.assertIsNone(mutate._run_mutants(self.root, self.mutants, self.out, rows, False, self.baseline))
        self.assertEqual(rows[0]["result"], "killed")
        self.assertEqual(self.baseline.check(), [])
        self.assertIsNone(self.finish())

    def test_final_check_detects_head_moving_without_overwriting_it(self):
        other = self.git("rev-parse", "prerequisite").decode().strip()
        self.git("update-ref", "HEAD", other)
        self.assertIn("HEAD", self.finish())
        self.assertEqual(self.git("rev-parse", "HEAD").decode().strip(), other)

    def test_final_check_refuses_to_build_corrupted_source(self):
        self.path.write_bytes(b"corruption\n")
        with patch.object(mutate, "_build") as build:
            self.assertIn("bytes or mode changed", mutate._finish(
                self.root, self.mutants, self.out, [], None, self.baseline))
            build.assert_not_called()


class FindingTheProjectOfAFile(unittest.TestCase):
    """project_of: the project whose assembly holds a mutated file, the nearest folder above it with a .csproj."""

    def setUp(self):
        self.repo = _Repo()
        self.addCleanup(self.repo.close)
        (self.repo.root / "src/A/Shaders").mkdir()
        (self.repo.root / "src/A/Shaders/blur.comp").write_text("void main() {}\n")

    def test_a_file_in_a_subfolder_belongs_to_the_project_of_the_nearest_folder_above_it(self):
        self.assertEqual(mutate.project_of(self.repo.root, "src/A/Shaders/blur.comp"), "A")

    def test_a_file_above_every_project_belongs_to_none_and_the_search_stops_at_the_repository_root(self):
        (self.repo.root / "global.json").write_text("{}\n")
        self.assertIsNone(mutate.project_of(self.repo.root, "global.json"))


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

    def test_a_signal_as_the_restore_starts_still_restores_the_file_and_then_ends_the_block(self):
        # A signal that arrives after the finally block starts and before the signals are blocked runs its handler
        # there. The fake raises KeyboardInterrupt at that place, once.
        real, raised = mutate._signals_blocked, []

        def signal_at_the_restore():
            if not raised:
                raised.append(True)
                raise KeyboardInterrupt
            return real()

        mutate._signals_blocked = signal_at_the_restore
        self.addCleanup(setattr, mutate, "_signals_blocked", real)
        with self.assertRaises(KeyboardInterrupt):
            with mutate.applied(self.path, 2, "x + 1", "x - 1"):
                pass
        self.assertEqual(self.path.read_bytes(), self.original)

    def test_a_sigterm_inside_the_block_restores_the_file_before_the_process_ends(self):
        script = (
            "import importlib.util, os, signal, sys, time\n"
            f"spec = importlib.util.spec_from_file_location('mutate', {str(TOOLS / 'mutate.py')!r})\n"
            "mutate = importlib.util.module_from_spec(spec); spec.loader.exec_module(mutate)\n"
            f"spec = importlib.util.spec_from_file_location('processes', {str(TOOLS / 'processes.py')!r})\n"
            "processes = importlib.util.module_from_spec(spec); spec.loader.exec_module(processes)\n"
            "processes.stop_on_signals()\n"
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

    def test_a_test_name_in_the_killed_by_column_is_its_class_and_method_with_the_arguments_of_a_theory_case(self):
        self.assertEqual(table.short_test_name("Tertius.Core.Tests.RTests.Two(x: 1, name: \"a.b\")"),
                         'RTests.Two(x: 1, name: "a.b")')

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

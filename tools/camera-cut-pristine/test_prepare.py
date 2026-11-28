"""Run the pinned preparation through the real handler, with the inert paid gate."""
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
SPEC = importlib.util.spec_from_file_location('handler', REPO / 'tools/runpod/handler.py')
HANDLER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HANDLER)


class PreparationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.work = Path(self.temporary.name)
        self.harness = self.work / 'harness/tools/camera-cut-pristine'
        shutil.copytree(HERE, self.harness, ignore=shutil.ignore_patterns('__pycache__'))
        for tree in ('before', 'after'):
            (self.work / tree).mkdir()
        self.manifest_sha = hashlib.sha256((HERE / 'payload-manifest.json').read_bytes()).hexdigest()

    def run_prepare(self):
        command = ('python3 -I harness/tools/camera-cut-pristine/prepare.py '
                   '--manifest-sha256 ' + self.manifest_sha)
        steps, log = [], bytearray()
        status = HANDLER.run_commands([command], self.work, ['bash', '-c'], 20, steps, log)
        return status, bytes(log).decode(), steps

    def test_verified_inputs_reach_the_existing_inert_gate_without_starting_measurement(self):
        status, log, steps = self.run_prepare()
        self.assertEqual('failed', status)
        self.assertIn('INERT: new Issue #4 paid approval', log)
        self.assertEqual(1, len(steps))
        for name, digest in json.loads((HERE / 'payload-manifest.json').read_text()).items():
            self.assertEqual(digest, hashlib.sha256((self.work / name).read_bytes()).hexdigest())
        self.assertFalse((self.work / 'linux-out').exists())

    def test_a_corrupted_input_is_rejected_before_any_copy(self):
        with (self.harness / 'fixture.py').open('a') as stream:
            stream.write('\n# corrupt input\n')
        status, log, _ = self.run_prepare()
        self.assertEqual('failed', status)
        self.assertIn('Payload hash differs: fixture.py', log)
        self.assertFalse((self.work / 'worker.py').exists())

    def test_a_missing_input_is_distinct_from_an_exec_size_failure(self):
        (self.harness / 'worker.py').unlink()
        status, log, _ = self.run_prepare()
        self.assertEqual('failed', status)
        self.assertIn('Payload file missing: worker.py', log)
        self.assertNotIn('Argument list too long', log)

    def test_manifest_changes_are_rejected_before_reading_payload(self):
        (self.harness / 'payload-manifest.json').write_text('{}')
        status, log, _ = self.run_prepare()
        self.assertEqual('failed', status)
        self.assertIn('Payload manifest hash differs', log)
        self.assertFalse((self.work / 'worker.py').exists())


if __name__ == '__main__':
    unittest.main()

"""Pin the offline camera-cut launch proposal in the normal tools test discovery."""
import importlib.util
import ast
import errno
import gzip
import hashlib
import platform
import shutil
import sys
import subprocess
from types import SimpleNamespace
import json
import shlex
from pathlib import Path
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[1]
PROPOSAL = REPO / 'docs/research/camera-cut-exposure/fsr-reference-investigation-2026-11-28/bounded-pristine-proposal'


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class LaunchProposalTests(unittest.TestCase):
    def test_the_final_command_carries_a_bound_external_approval_record(self):
        emitter = load('camera_cut_emitter', PROPOSAL / 'emit_proposal.py')
        with tempfile.TemporaryDirectory() as temporary:
            metadata = emitter.emit(PROPOSAL, Path(temporary) / 'proposal.ps1')
        record = metadata.get('external_approval_record')
        self.assertIsNotNone(record, 'The final launch still has no external authorization step')
        self.assertFalse(record['issue4_explicit_paid_approval'])
        self.assertFalse(record['coordinator_reviewed'])
        self.assertEqual(0, record['authorized_submissions'])
        self.assertEqual(record, json.loads(shlex.split(metadata['conditional_request_body']['input']['commands'][0])[-1]))


class ExternalLaunchTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.work = Path(self.temporary.name)
        self.here = REPO / 'tools/camera-cut-pristine'
        shutil.copytree(self.here, self.work / 'harness/tools/camera-cut-pristine',
                        ignore=shutil.ignore_patterns('__pycache__', 'proof-data'))
        self.handler = load('handler', REPO / 'tools/runpod/handler.py')
        emitter = load('emitter', PROPOSAL / 'emit_proposal.py')
        self.metadata = emitter.emit(PROPOSAL, self.work / 'proposal.ps1')
        self.command = self.metadata['conditional_request_body']['input']['commands'][0]
        self.record = self.metadata['external_approval_record'].copy()
        self.record.update(coordinator_reviewed=True, issue4_explicit_paid_approval=True,
                           duration_policy_approved=True, authorized_submissions=1)

    def command_with(self, record):
        argv = shlex.split(self.command)
        argv[-1] = json.dumps(record, sort_keys=True)
        return shlex.join(argv)

    def run_command(self, command, *, gate=False):
        shell = ([sys.executable, '-I', str(self.here / 'proof_gate.py')] if gate else ['bash', '-c'])
        log, steps = bytearray(), []
        status = self.handler.run_commands([command], self.work, shell, 20, steps, log)
        prefix = ('$ ' + command + '\n').encode()
        self.assertTrue(bytes(log).startswith(prefix))
        return status, bytes(log)[len(prefix):].decode(), steps

    def test_unapproved_final_command_stops_before_copying_or_measurement(self):
        status, log, steps = self.run_command(self.command)
        self.assertEqual('failed', status)
        self.assertIn('INERT: external approval', log)
        self.assertEqual(1, len(steps))
        self.assertFalse((self.work / 'worker.py').exists())

    def test_approved_scope_reaches_the_real_worker_with_only_three_runtime_changes(self):
        command = self.command_with(self.record)
        self.assertEqual(self.metadata['representative_approved_request_body']['input']['commands'][0], command)
        status, log, steps = self.run_command(command, gate=True)
        self.assertEqual('ok', status, log[-3000:])
        self.assertIn('REAL_WORKER_REACHED_HARMLESS_GATE', log)
        original = json.loads((self.here / 'config.json').read_text())
        expected = json.loads(json.dumps(original))
        expected['approval'].update(coordinator_reviewed=True, issue4_explicit_paid_approval=True,
                                    authorized_submissions=1)
        self.assertEqual(expected, json.loads((self.work / 'gate.json').read_text()))
        manifest = json.loads((self.here / 'payload-manifest.json').read_text())
        for name, digest in manifest.items():
            self.assertEqual(digest, hashlib.sha256((self.work / name).read_bytes()).hexdigest(), name)
        self.assertFalse((self.work / 'linux-out').exists())
        # Reusing this work folder refuses instead of replacing an earlier launch's inputs.
        status, log, _ = self.run_command(command, gate=True)
        self.assertEqual('failed', status)
        self.assertIn('Payload destination already exists', log)

    def test_mismatched_or_inconsistent_approval_fields_are_refused(self):
        changes = {'baseline_sha': '0' * 40, 'harness_sha': '0' * 40,
                   'manifest_sha256': '0' * 64, 'candidate_sha': '0' * 40, 'source_config_sha256': '0' * 64,
                   'authorized_submissions': 2, 'automatic_retry': True,
                   'duration_policy_approved': False, 'coordinator_reviewed': 1,
                   'command_seconds': 840, 'execution_seconds': 900,
                   'maximum_modeled_usd': 1, 'gpu_workers_max': 6,
                   'daily_cap_usd': 6, 'total_cap_usd': 31,
                   'output_globs': ['**/*']}
        for key, value in changes.items():
            with self.subTest(key=key):
                record = {**self.record, key: value}
                status, log, _ = self.run_command(self.command_with(record), gate=True)
                self.assertEqual('failed', status)
                self.assertIn('INERT: external approval', log)
                self.assertFalse((self.work / 'worker.py').exists())

    def test_corrupted_payload_is_refused_before_any_copy(self):
        path = self.work / 'harness/tools/camera-cut-pristine/fixture.py'
        path.chmod(0o644)
        path.write_bytes(path.read_bytes() + b'\n# altered\n')
        status, log, _ = self.run_command(self.command_with(self.record), gate=True)
        self.assertEqual('failed', status)
        self.assertIn('Payload hash differs: fixture.py', log)
        self.assertFalse((self.work / 'worker.py').exists())

    def test_consumed_attempt_refuses_before_policy_or_api_access(self):
        # Reuse the exact previous wrapper's entry function, without its API imports.
        source = (REPO / 'docs/research/camera-cut-exposure/issue37-linux-proof-2026-11-28/run_once.proposed.py.txt').read_text()
        main = next(node for node in ast.parse(source).body if isinstance(node, ast.FunctionDef) and node.name == 'main')
        namespace = {'RUN': self.work, 'sys': type('Args', (), {'argv': ['run_once.py']})}
        exec(compile(ast.Module(body=[main], type_ignores=[]), '<previous one-attempt wrapper>', 'exec'), namespace)
        (self.work / 'submission-consumed.json').write_text('{}')
        with self.assertRaisesRegex(AssertionError, 'already consumed'):
            namespace['main']()

    def test_unapproved_one_attempt_wrapper_stops_before_git_policy_or_api_access(self):
        source = (REPO / 'docs/research/camera-cut-exposure/issue37-linux-proof-2026-11-28/run_once.proposed.py.txt').read_text()
        main = next(node for node in ast.parse(source).body if isinstance(node, ast.FunctionDef) and node.name == 'main')
        namespace = {'RUN': self.work, 'sys': type('Args', (), {'argv': ['run_once.py']}),
                     'json': json, 'read': lambda name: self.metadata['external_approval_record']}
        exec(compile(ast.Module(body=[main], type_ignores=[]), '<proposed one-attempt wrapper>', 'exec'), namespace)
        with self.assertRaisesRegex(RuntimeError, 'INERT: fresh paid'):
            namespace['main']()

    def test_one_approved_attempt_consumes_its_marker_and_restores_policy(self):
        source = (REPO / 'docs/research/camera-cut-exposure/issue37-linux-proof-2026-11-28/run_once.proposed.py.txt').read_text()
        main = next(node for node in ast.parse(source).body if isinstance(node, ast.FunctionDef) and node.name == 'main')
        original = (REPO / 'tools/runpod/endpoint.json').read_bytes()
        proposed = original.replace(b'"job_seconds": 840,', b'"job_seconds": 1920,').replace(
            b'"endpoint_timeout_seconds": 900', b'"endpoint_timeout_seconds": 1980')
        config = self.work / 'endpoint.json'
        config.write_bytes(original)
        (self.work / 'endpoint.original.json').write_bytes(original)
        (self.work / 'endpoint.proposed.json').write_bytes(proposed)
        argv = self.metadata['canonical_argv_after_parent_approval']
        sha = lambda data: hashlib.sha256(data).hexdigest()
        approval = {**self.record, 'reviewed_head': 'reviewed-head',
            'canonical_argv_sha256': sha(json.dumps(argv, separators=(',', ':')).encode()),
            'temporary_config_sha256': sha(proposed),
            'full_command_sha256': self.metadata['representative_approved_command_sha256']}
        for name, value in [('approval.json', approval), ('launch-review.json', self.metadata),
                            ('canonical-argv.json', argv)]:
            (self.work / name).write_text(json.dumps(value))
        launches, restores = [], []

        def popen(command, **kwargs):
            self.assertEqual(proposed, config.read_bytes())
            self.assertTrue((self.work / 'submission-consumed.json').exists())
            self.assertEqual(argv, command)
            launches.append(command)
            return SimpleNamespace(pid=1, returncode=0, poll=lambda: 0)

        def restore():
            config.write_bytes(original)
            restores.append(True)

        namespace = {'RUN': self.work, 'REPO': REPO, 'CONFIG': config, 'json': json, 'sha': sha,
            'os': __import__('os'), 'sys': SimpleNamespace(argv=['run_once.py']),
            'signal': SimpleNamespace(SIGINT=2, SIGTERM=15, SIGHUP=1, signal=lambda *args: None),
            'subprocess': SimpleNamespace(Popen=popen), 'forward': lambda *args: None,
            'read': lambda name: json.loads((self.work / name).read_text()),
            'write': lambda name, value: (self.work / name).write_text(json.dumps(value)),
            'git': lambda *args: 'reviewed-head' if args[0] == 'rev-parse' else '',
            'now': lambda: 'offline-time', 'capture_receipt': lambda: [],
            'restore': restore, 'interrupted': [], 'child': None}
        exec(compile(ast.Module(body=[main], type_ignores=[]), '<proposed one-attempt wrapper>', 'exec'), namespace)
        self.assertEqual(0, namespace['main']())
        self.assertEqual(original, config.read_bytes())
        with self.assertRaisesRegex(AssertionError, 'already consumed'):
            namespace['main']()
        self.assertEqual(1, len(launches))
        self.assertEqual(1, len(restores))

    def test_reconstructed_payload_seals_and_returns_every_required_output(self):
        status, log, _ = self.run_command(self.command_with(self.record), gate=True)
        self.assertEqual('ok', status, log)
        program = """import importlib.util,runpy,sys,unittest
from pathlib import Path
spec=importlib.util.spec_from_file_location('output_budget',sys.argv[1])
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
module.WORKER=runpy.run_path(sys.argv[2]+'/worker.py')
module.ANALYZER=runpy.run_path(sys.argv[2]+'/analyze_runs.py')
original_seed=module.OutputBudgetTests.seed_complete
def writable_seed(self):
 original_seed(self)
 for path in self.out.rglob('*'):
  path.chmod(0o755 if path.is_dir() else 0o644)
module.OutputBudgetTests.seed_complete=writable_seed
case=module.OutputBudgetTests('test_all_evidence_round_trips_once_with_no_skipped_files')
result=unittest.TextTestRunner().run(unittest.TestSuite([case]))
if not result.wasSuccessful(): raise SystemExit(1)
"""
        result = subprocess.run([sys.executable, '-I', '-c', program,
            str(PROPOSAL / 'test_output_budget.py'), str(self.work)], capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(['linux-out/return/*'], self.metadata['conditional_request_body']['input']['outputs'])

    @unittest.skipUnless(platform.system() == 'Linux', 'Linux kernel argument boundary')
    def test_linux_rejects_the_exact_original_argument_before_a_child_starts(self):
        old = gzip.decompress((self.here / 'proof-data/old-command.gz').read_bytes())
        self.assertEqual(409197, len(old))
        self.assertEqual('18707842aed762dec041dae6b08802025bae8a975ce53ce34cabecdc4fe1a340',
                         hashlib.sha256(old).hexdigest())
        with self.assertRaises(OSError) as error:
            self.handler.run_commands([old.decode()], self.work,
                [sys.executable, '-c', 'raise SystemExit(77)'], 20, [], bytearray())
        self.assertEqual(errno.E2BIG, error.exception.errno)


if __name__ == '__main__':
    unittest.main()

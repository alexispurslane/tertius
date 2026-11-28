"""Offline tests of the camera-cut proposal's command transport. Never submit a job."""
import base64
import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
import re
import runpy
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[1]
PROPOSAL = REPO / 'docs/research/camera-cut-exposure/fsr-reference-investigation-2026-11-28/bounded-pristine-proposal'


def emitted_argv(path):
    command = path.read_text().split('\n& ', 1)[1]
    return [word.replace("''", "'") for word in re.findall(r"'((?:[^']|'')*)'", command)]


class TransportTests(unittest.TestCase):
    def test_emitted_command_stays_within_conservative_linux_argument_budget(self):
        # Embedding the payload in one argument caused the paid E2BIG failure.
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'proposal.ps1'
            runpy.run_path(str(PROPOSAL / 'emit_proposal.py'))['emit'](PROPOSAL, path)
            argv = emitted_argv(path)
            commands = [argv[i + 1] for i, word in enumerate(argv) if word == '--run']
            self.assertTrue(commands)
            for command in commands:
                self.assertLessEqual(len(command.encode('utf-8')) + 1, 32768)


    def test_proposal_uses_a_third_immutable_tree_without_repinning_measured_sources(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'proposal.ps1'
            metadata = runpy.run_path(str(PROPOSAL / 'emit_proposal.py'))['emit'](PROPOSAL, path)
            argv = emitted_argv(path)
            trees = dict(argv[i + 1].split('=', 1) for i, word in enumerate(argv) if word == '--tree')
            self.assertEqual('b002351eb48d33f008f8333e53e61345de3260cd', trees['before'])
            self.assertEqual('588b6d2747560c73aac542fb25ce80eec1ba8db7', trees['after'])
            self.assertEqual(json.loads((PROPOSAL / 'transport.json').read_text())['harness_sha'], trees['harness'])
            self.assertEqual({'before', 'after', 'harness'}, set(trees))
            self.assertFalse(metadata['approval']['issue4_explicit_paid_approval'])
            self.assertEqual(0, metadata['approval']['authorized_submissions'])

    def test_the_unapproved_short_command_refuses_before_preparation(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = root / 'proposal.ps1'
            runpy.run_path(str(PROPOSAL / 'emit_proposal.py'))['emit'](PROPOSAL, path)
            argv = emitted_argv(path)
            command = argv[argv.index('--run') + 1]
            folder = root / 'harness/tools/camera-cut-pristine'
            shutil.copytree(REPO / 'tools/camera-cut-pristine', folder)
            for name in ('before', 'after'):
                (root / name).mkdir()
            spec = importlib.util.spec_from_file_location('handler', REPO / 'tools/runpod/handler.py')
            handler = importlib.util.module_from_spec(spec); spec.loader.exec_module(handler)
            steps, log = [], bytearray()
            self.assertEqual('failed', handler.run_commands([command], root, ['bash', '-c'], 20, steps, log))
            self.assertIn('INERT: external approval', bytes(log).decode())
            self.assertFalse((root / 'linux-out').exists())
            self.assertFalse((root / 'worker.py').exists())
            self.assertEqual(({}, {}), handler.collect(['linux-out/return/*'], root, 8_000_000))

    def test_short_command_runs_inside_local_offline_nix_shell(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); path = root / 'proposal.ps1'
            runpy.run_path(str(PROPOSAL / 'emit_proposal.py'))['emit'](PROPOSAL, path)
            argv = emitted_argv(path); command = argv[argv.index('--run') + 1]
            shutil.copytree(REPO / 'tools/camera-cut-pristine', root / 'harness/tools/camera-cut-pristine')
            spec = importlib.util.spec_from_file_location('handler', REPO / 'tools/runpod/handler.py')
            handler = importlib.util.module_from_spec(spec); spec.loader.exec_module(handler)
            steps, log = [], bytearray()
            local_shell = ['nix', 'develop', str(REPO), '--offline', '--command', 'bash', '-c']
            self.assertEqual('failed', handler.run_commands([command], root, local_shell, 45, steps, log))
            self.assertIn('INERT: external approval', bytes(log).decode())
            self.assertFalse((root / 'linux-out').exists())


    def test_utf8_nul_and_environment_are_counted_before_emission(self):
        emitter = runpy.run_path(str(PROPOSAL / 'emit_proposal.py'))
        bounds = emitter['command_bounds'](['é'], {'A': 'b'})
        self.assertEqual([3], bounds['command_bytes_including_nul'])
        with self.assertRaisesRegex(ValueError, 'argument'):
            emitter['command_bounds'](['é' * 16384], {})
        with self.assertRaisesRegex(ValueError, 'environment'):
            emitter['command_bounds'](['true'], {'A': 'x' * 65536})
        with self.assertRaisesRegex(ValueError, 'Aggregate'):
            emitter['command_bounds'](['x' * 32767], {})

    def test_changed_prepare_is_rejected_before_payload_code_executes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); path = root / 'proposal.ps1'
            runpy.run_path(str(PROPOSAL / 'emit_proposal.py'))['emit'](PROPOSAL, path)
            argv = emitted_argv(path); command = argv[argv.index('--run') + 1]
            folder = root / 'harness/tools/camera-cut-pristine'
            shutil.copytree(REPO / 'tools/camera-cut-pristine', folder)
            (folder / 'prepare.py').write_text("raise RuntimeError('UNVERIFIED CODE RAN')")
            result = subprocess.run(['bash', '-c', command], cwd=root, capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('External launch source hash differs: prepare.py', result.stderr)
            self.assertNotIn('UNVERIFIED CODE RAN', result.stderr)
            self.assertFalse((root / 'worker.py').exists())

    def test_powershell_review_command_stops_before_canonical_submission(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'proposal.ps1'
            runpy.run_path(str(PROPOSAL / 'emit_proposal.py'))['emit'](PROPOSAL, path)
            result = subprocess.run(['pwsh', '-NoProfile', '-File', str(path)], cwd=temporary,
                                    capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('INERT:', result.stderr)
            self.assertFalse((Path(temporary) / 'linux-out').exists())


class NamedTreeProtocolTests(unittest.TestCase):
    def test_real_named_tree_checkout_keeps_inputs_and_reaches_inert_gate_in_job_root(self):
        # A local bare repository replaces only GitHub. The real protocol checker, sparse checkout,
        # shell compatibility check, command runner, Popen, payload preparation and collector all run.
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); source = root / 'source'; remote = root / 'remote.git'
            baked = root / 'baked'; work = root / 'work'
            def git(cwd, *args):
                return subprocess.check_output(['git', *args], cwd=cwd, stderr=subprocess.PIPE, text=True).strip()
            git(root, 'init', '-q', str(source)); git(root, 'init', '-q', '--bare', str(remote))
            git(remote, 'config', 'uploadpack.allowFilter', 'true')
            shell = {'flake.nix': b'{ outputs = _: {}; }', 'flake.lock': b'{}', 'native/pin': b'same shell'}
            for name, data in shell.items():
                p = baked / name; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(data)
            def commit(files):
                for p in source.iterdir():
                    if p.name != '.git':
                        shutil.rmtree(p) if p.is_dir() else p.unlink()
                for name, data in {**shell, **files}.items():
                    p = source / name; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(data)
                git(source, 'add', '-A')
                git(source, '-c', 'user.name=offline', '-c', 'user.email=offline@example.invalid', 'commit', '-qm', 'fixture')
                sha = git(source, 'rev-parse', 'HEAD')
                ref = 'refs/gpu-jobs/2026-11-28/offline/' + sha
                git(source, 'push', '-q', str(remote), sha + ':' + ref)
                return {'sha': sha, 'ref': ref}
            before = commit({'identity': b'baseline untouched'})
            after = commit({'identity': b'candidate untouched'})
            files = {str(p.relative_to(REPO)): p.read_bytes() for p in (REPO / 'tools/camera-cut-pristine').rglob('*')
                     if p.is_file() and '__pycache__' not in p.parts}
            harness = commit({**files, 'docs/excluded.txt': b'excluded by real sparse checkout'})
            proposal = root / 'proposal.ps1'
            metadata = runpy.run_path(str(PROPOSAL / 'emit_proposal.py'))['emit'](PROPOSAL, proposal)
            job = metadata['conditional_request_body']['input']
            job.update(repository=str(remote), trees={'before': before, 'after': after, 'harness': harness}, seconds=20)
            spec = importlib.util.spec_from_file_location('handler', REPO / 'tools/runpod/handler.py')
            handler = importlib.util.module_from_spec(spec); spec.loader.exec_module(handler)
            result = handler.handle(job, baked=baked, work=work, shell=['bash', '-c'])
            self.assertEqual('failed', result['status'])
            self.assertIn('INERT: external approval', result['log'])
            self.assertEqual(1, len(result['steps']))
            self.assertEqual({}, result['skipped']); self.assertEqual({}, result['files'])
            self.assertEqual(b'baseline untouched', (work / 'before/identity').read_bytes())
            self.assertEqual(b'candidate untouched', (work / 'after/identity').read_bytes())
            self.assertFalse((work / 'harness/docs/excluded.txt').exists())
            self.assertFalse((work / 'before/worker.py').exists())
            self.assertFalse((work / 'after/worker.py').exists())
            self.assertEqual((REPO / 'tools/camera-cut-pristine/worker.py').read_bytes(),
                             (work / 'harness/tools/camera-cut-pristine/worker.py').read_bytes())
            self.assertFalse((work / 'worker.py').exists())
            self.assertFalse((work / 'config.runtime.json').exists())
            self.assertFalse((work / 'linux-out').exists())
            # A mismatching harness shell must still be rejected by the unchanged handler.
            mismatch = commit({'flake.lock': b'{"different":true}'})
            bad = dict(job, trees={'harness': mismatch})
            refused = handler.handle(bad, baked=baked, work=root / 'bad-work', shell=['bash', '-c'])
            self.assertEqual('refused', refused['status'])
            self.assertIn('another dev shell', refused['error'])
            self.assertEqual([], refused['steps'])


if __name__ == '__main__':
    unittest.main()

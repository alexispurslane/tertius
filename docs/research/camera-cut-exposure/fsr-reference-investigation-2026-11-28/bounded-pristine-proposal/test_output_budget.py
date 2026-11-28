"""Offline evidence-return regressions. No renderer, sockets, keys or submissions."""
import base64
import contextlib
import gzip
import hashlib
import io
import json
from pathlib import Path
import random
import runpy
import shutil
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

PROPOSAL = Path(__file__).resolve().parent
REPO = PROPOSAL.parents[4]
WORKER = runpy.run_path(str(PROPOSAL / 'worker.py'))
HANDLER = runpy.run_path(str(REPO / 'tools/runpod/handler.py'))
ANALYZER = runpy.run_path(str(PROPOSAL / 'analyze_runs.py'))


def forbid_external_effects(event, args):
    if event.startswith('socket.') or event in {'subprocess.Popen', 'os.system', 'os.posix_spawn'}:
        raise RuntimeError('Offline tests forbid network and child processes: ' + event)


sys.addaudithook(forbid_external_effects)


class OutputBudgetTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.out = self.root / 'linux-out'
        self.out.mkdir()

    def seed_complete(self):
        with tarfile.open(PROPOSAL / 'local-harness-evidence.tar.gz') as archive:
            for visit, tree in WORKER['VISITS']:
                for mode in ('native', 'fsr'):
                    for lane in ('cut', 'steady'):
                        name = f'local-full-scope/{tree}/{mode}/{lane}/rows.json'
                        path = self.out / visit / mode / lane / 'rows.json'
                        path.parent.mkdir(parents=True, exist_ok=True)
                        path.write_bytes(archive.extractfile(name).read())
        shutil.copytree(PROPOSAL / 'retained-diagnostics', self.out / 'retained-diagnostics')
        for name in WORKER['REQUIRED_METADATA']:
            if not (self.out / name).exists():
                (self.out / name).write_text('{}\n')
        (self.out / 'restoration.json').write_text(json.dumps({'all_diagnostic_source_restored': True}))
        (self.out / 'phase.json').write_text(json.dumps({'phase': 'complete'}))
        (self.out / 'harness.json').write_text(json.dumps({'config': json.loads((PROPOSAL / 'config.json').read_text())}))

    def finish(self, complete=True, error=None):
        return WORKER['finish_output'](self.out, complete=complete, error=error)

    def collect(self):
        return HANDLER['collect'](WORKER['OUTPUT_GLOBS'], self.root, HANDLER['Limits']().output_bytes)

    def deliver(self):
        files, skipped = self.collect()
        returned = self.root / 'returned'
        for name, encoded in files.items():
            path = returned / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(base64.b64decode(encoded))
        (returned / 'result.json').write_text(json.dumps({'status': 'ok', 'skipped': skipped, 'files': sorted(files)}))
        return returned

    def test_oversized_evidence_reports_overflow_and_keeps_small_failure_metadata(self):
        self.seed_complete()
        (self.out / 'r1-before.log').write_bytes(random.Random(4).randbytes(6_100_000))
        report = self.finish()
        self.assertEqual('overflow', report['status'])
        self.assertFalse(report['required_evidence_returned'])
        files, skipped = self.collect()
        self.assertEqual({}, skipped)
        self.assertEqual(['linux-out/return/status.json'], sorted(files))
        self.assertTrue((self.out / 'evidence.tar.gz').exists())
        self.assertLess(len(files['linux-out/return/status.json']), WORKER['FINAL_METADATA_RESERVE'])
        self.assertTrue(json.loads(base64.b64decode(files['linux-out/return/status.json']))['restoration']['all_diagnostic_source_restored'])
        with self.assertRaisesRegex(ValueError, 'overflow'):
            ANALYZER['analyze_returned'](self.deliver(), require_sizes=False)

    def test_all_evidence_round_trips_once_with_no_skipped_files(self):
        self.seed_complete()
        (self.out / 'clocks.csv').write_text('timestamp,clock\n1,100\n')
        (self.out / 'assets.log').write_text('asset setup complete\n')
        raw = self.out / 'r1-before/native/cut/rows.json'
        raw.with_suffix('.json.gz').write_bytes(gzip.compress(raw.read_bytes(), mtime=0))
        retained = self.out / 'retained-diagnostics/diagnostic-before/native/cut/rows.json.gz'
        retained.with_suffix('').write_bytes(gzip.decompress(retained.read_bytes()))
        report = self.finish()
        self.assertEqual('complete', report['status'])
        files, skipped = self.collect()
        self.assertEqual({}, skipped)
        self.assertEqual(['linux-out/return/evidence.tar.gz', 'linux-out/return/status.json'], sorted(files))
        self.assertEqual(report['returned_base64_bytes'], sum(map(len, files.values())))
        with tarfile.open(self.out / 'return/evidence.tar.gz') as archive:
            names = archive.getnames()
            self.assertIn('restoration.json', names)
            self.assertIn('clocks.csv', names)
            self.assertIn('assets.log', names)
            self.assertNotIn('r1-before/native/cut/rows.json.gz', names)
            self.assertNotIn('retained-diagnostics/diagnostic-before/native/cut/rows.json', names)
            self.assertEqual(raw.read_bytes(), archive.extractfile('r1-before/native/cut/rows.json').read())
            self.assertEqual(retained.read_bytes(), archive.extractfile(str(retained.relative_to(self.out))).read())
        replay = ANALYZER['analyze_returned'](self.deliver(), require_sizes=False)
        self.assertEqual('all observation gates passed', replay['status'])
        self.assertEqual(24, len(replay['rows']))

    def test_missing_required_rows_cannot_advertise_success(self):
        self.seed_complete()
        (self.out / 'r2-before/fsr/cut/rows.json').unlink()
        report = self.finish()
        self.assertEqual('incomplete', report['status'])
        self.assertIn('r2-before/fsr/cut/rows.json', report['missing_required'])
        self.assertFalse(report['required_evidence_returned'])

    def test_failed_process_preserves_partial_rows_and_the_failure(self):
        path = self.out / 'r1-before/native/cut/rows.json'
        path.parent.mkdir(parents=True)
        path.write_text('{"Rows": []}')
        report = self.finish(complete=False, error='measurement timed out')
        self.assertEqual('failed', report['status'])
        self.assertEqual('measurement timed out', report['error'])
        self.assertFalse(report['required_evidence_returned'])
        self.assertEqual({}, self.collect()[1])
        with tarfile.open(self.out / 'return/evidence.tar.gz') as archive:
            self.assertEqual(path.read_bytes(), archive.extractfile(str(path.relative_to(self.out))).read())

    def test_restoration_failure_prevents_success(self):
        self.seed_complete()
        (self.out / 'restoration.json').write_text('{"all_diagnostic_source_restored": false}')
        self.assertEqual('incomplete', self.finish()['status'])

    def test_packaging_failure_retains_explicit_small_failure_metadata(self):
        self.seed_complete()
        with patch.object(tarfile, 'open', side_effect=OSError('archive disk failure')):
            report = self.finish()
        self.assertEqual('packaging-failed', report['status'])
        self.assertIn('archive disk failure', report['error'])
        files, skipped = self.collect()
        self.assertEqual({}, skipped)
        self.assertEqual(['linux-out/return/status.json'], sorted(files))

    def test_worker_seals_failed_phase_and_restoration_before_return(self):
        # Exercise finalization without crossing the paid gate on disk or starting any process.
        config_text = (PROPOSAL / 'config.json').read_text()
        config = json.loads(config_text)
        config['approval'] = {**config['approval'], 'coordinator_reviewed': True, 'issue4_explicit_paid_approval': True}
        decoder = json.loads
        offline_json = SimpleNamespace(dumps=json.dumps,
            loads=lambda text: config if text == config_text else decoder(text))
        namespace = WORKER['main'].__globals__
        self.out.rmdir()
        with contextlib.chdir(self.root), patch.object(sys, 'argv', ['worker.py', '--config', str(PROPOSAL / 'config.json')]), \
                patch.dict(namespace, json=offline_json, checked=lambda *args: ''), \
                patch.object(namespace['subprocess'], 'Popen', side_effect=RuntimeError('offline device failure')):
            with self.assertRaisesRegex(RuntimeError, 'Returned evidence is failed'):
                WORKER['main']()
        report = json.loads((self.out / 'return/status.json').read_text())
        self.assertIn('offline device failure', report['error'])
        self.assertEqual('failed', report['phase']['phase'])
        self.assertTrue(report['restoration']['all_diagnostic_source_restored'])
        with tarfile.open(self.out / 'return/evidence.tar.gz') as archive:
            self.assertEqual('failed', json.load(archive.extractfile('phase.json'))['phase'])
            self.assertTrue(json.load(archive.extractfile('restoration.json'))['all_diagnostic_source_restored'])

    def test_archive_at_reserved_boundary_is_accepted_and_one_byte_over_is_rejected(self):
        self.seed_complete()
        first = self.finish()
        archive_encoded = first['archive_base64_bytes']
        namespace = WORKER['finish_output'].__globals__
        with patch.dict(namespace, OUTPUT_LIMIT=archive_encoded + WORKER['FINAL_METADATA_RESERVE']):
            self.assertEqual('complete', self.finish()['status'])
        with patch.dict(namespace, OUTPUT_LIMIT=archive_encoded + WORKER['FINAL_METADATA_RESERVE'] - 1):
            self.assertEqual('overflow', self.finish()['status'])
        self.assertFalse((self.out / 'return/evidence.tar.gz').exists())

    def test_each_file_rounds_base64_independently_at_the_handler_boundary(self):
        for number in range(100):
            (self.out / f'{number}.bin').write_bytes(b'x')
        paths = list(self.out.glob('*.bin'))
        size = WORKER['encoded_bytes'](paths)
        self.assertEqual(400, size)
        files, skipped = HANDLER['collect'](['linux-out/*.bin'], self.root, size)
        self.assertEqual(100, len(files))
        self.assertEqual({}, skipped)
        self.assertEqual(1, len(HANDLER['collect'](['linux-out/*.bin'], self.root, size - 1)[1]))

    def test_analyzer_rejects_missing_archive_and_skipped_files(self):
        self.seed_complete()
        self.finish()
        returned = self.deliver()
        result_path = returned / 'result.json'
        result = json.loads(result_path.read_text())
        result['skipped'] = {'linux-out/return/evidence.tar.gz': 123}
        result_path.write_text(json.dumps(result))
        with self.assertRaisesRegex(ValueError, 'skipped'):
            ANALYZER['analyze_returned'](returned, require_sizes=False)
        result['skipped'] = {}
        result_path.write_text(json.dumps(result))
        (returned / 'linux-out/return/evidence.tar.gz').unlink()
        with self.assertRaisesRegex(ValueError, 'missing'):
            ANALYZER['analyze_returned'](returned, require_sizes=False)

    def test_analyzer_rejects_changed_archive_bytes(self):
        self.seed_complete()
        self.finish()
        returned = self.deliver()
        with (returned / 'linux-out/return/evidence.tar.gz').open('ab') as stream:
            stream.write(b'changed')
        with self.assertRaisesRegex(ValueError, 'hash'):
            ANALYZER['analyze_returned'](returned, require_sizes=False)

    def test_analyzer_requires_measurements_and_retained_diagnostics_even_if_manifest_omits_them(self):
        self.seed_complete()
        self.finish()
        for missing in ('r2-before/fsr/cut/rows.json', 'retained-diagnostics/diagnostic-before/native/cut/rows.json.gz'):
            with self.subTest(missing=missing):
                returned = self.deliver()
                archive_path = returned / 'linux-out/return/evidence.tar.gz'
                with tarfile.open(archive_path) as archive:
                    entries = {member.name: archive.extractfile(member).read() for member in archive.getmembers()}
                entries.pop(missing)
                manifest = json.loads(entries['evidence-manifest.json'])
                manifest['files'] = [entry for entry in manifest['files'] if entry['path'] != missing]
                entries['evidence-manifest.json'] = json.dumps(manifest).encode()
                with tarfile.open(archive_path, 'w:gz') as archive:
                    for name, data in entries.items():
                        header = tarfile.TarInfo(name)
                        header.size = len(data)
                        archive.addfile(header, io.BytesIO(data))
                status_path = returned / 'linux-out/return/status.json'
                status = json.loads(status_path.read_text())
                status['archive_sha256'] = hashlib.sha256(archive_path.read_bytes()).hexdigest()
                status_path.write_text(json.dumps(status))
                with self.assertRaisesRegex(ValueError, 'missing'):
                    ANALYZER['analyze_returned'](returned, require_sizes=False)

    def test_inert_emitter_embeds_every_input_and_the_exact_return_glob(self):
        emitter = runpy.run_path(str(PROPOSAL / 'emit_proposal.py'))
        command = self.root / 'proposal.ps1'
        metadata = emitter['emit'](PROPOSAL, command)
        self.assertEqual(WORKER['OUTPUT_GLOBS'], metadata['output_globs'])
        self.assertTrue(command.read_text().startswith("throw 'INERT:"))
        self.assertIn("'--output' 'linux-out/return/*'", command.read_text())
        self.assertFalse(metadata['approval']['issue4_explicit_paid_approval'])
        self.assertFalse(metadata['approval']['coordinator_reviewed'])
        self.assertEqual(0, metadata['approval']['authorized_submissions'])
        expected = {'worker.py', 'fixture.py', 'analyze_runs.py', 'config.json',
            *[str(path.relative_to(PROPOSAL)) for folder in ('retained-diagnostics', 'linux-scripts')
              for path in (PROPOSAL / folder).rglob('*') if path.is_file()]}
        self.assertEqual(expected, set(metadata['payload_sha256']))
        for name, digest in metadata['payload_sha256'].items():
            self.assertEqual(hashlib.sha256((PROPOSAL / name).read_bytes()).hexdigest(), digest)


if __name__ == '__main__':
    unittest.main()

"""Run offline launch regressions with the existing local Linux Nix builder. No provisioning."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

REPO = Path(__file__).resolve().parents[2]
PROPOSAL = Path('docs/research/camera-cut-exposure/fsr-reference-investigation-2026-11-28/bounded-pristine-proposal')
# These are the exact Linux dependencies already present for the original loopback proof.
PYTHON = '/nix/store/9zyabiswyisqky2gd1d3z1ig5rm7dd94-python3-3.14.10'
BASH = '/nix/store/brnhnyb8lq3sfkdsdq10i5w85b120imz-bash-interactive-5.3p20'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=False)
    with tempfile.TemporaryDirectory(prefix='issue37-linux-proof-') as temporary:
        root = Path(temporary)
        source = root / 'source'
        source.mkdir()
        files = [Path('tools/test_camera_cut_launch.py'), Path('tools/runpod/handler.py'), Path('tools/runpod/endpoint.json'),
                 *[PROPOSAL / name for name in ['emit_proposal.py', 'transport.json', 'config.json', 'test_output_budget.py', 'local-harness-evidence.tar.gz']]]
        files.append(Path('docs/research/camera-cut-exposure/issue37-linux-proof-2026-11-28/run_once.proposed.py.txt'))
        harness = REPO / 'tools/camera-cut-pristine'
        files += [path.relative_to(REPO) for path in harness.rglob('*')
                  if path.is_file() and '__pycache__' not in path.parts]
        files += [PROPOSAL / name for name in json.loads((harness / 'payload-manifest.json').read_text())]
        for name in set(files):
            target = source / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(REPO / name, target)
        # The derivation gets only the small source snapshot and already installed Linux dependencies.
        runner = '''import json,os,platform,runpy,sys,unittest
from pathlib import Path
sys.dont_write_bytecode=True
suite=runpy.run_path(os.environ['source']+'/tools/test_camera_cut_launch.py')
result=unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromModule(type('Suite',(),suite)))
report={'system':platform.system(),'machine':platform.machine(),'page_bytes':os.sysconf('SC_PAGE_SIZE'),'tests':result.testsRun,'failures':len(result.failures),'errors':len(result.errors),'skips':len(result.skipped)}
import tempfile,hashlib
with tempfile.TemporaryDirectory() as temporary:
 emitter=suite['load']('emitter',suite['PROPOSAL']/'emit_proposal.py')
 metadata=emitter.emit(suite['PROPOSAL'],Path(temporary)/'proposal.ps1')
report.update(handler_sha256=hashlib.sha256((suite['REPO']/'tools/runpod/handler.py').read_bytes()).hexdigest(),command_bounds=metadata['command_bounds'],inert_command_sha256=metadata['inert_command_sha256'],representative_approved_command_sha256=metadata['representative_approved_command_sha256'],request_body_bytes=metadata['request_body_bytes'],representative_approved_request_body_bytes=metadata['representative_approved_request_body_bytes'],payload_verified_files=len(metadata['payload_sha256']))
Path(os.environ['out']).write_text(json.dumps(report,sort_keys=True)+'\\n')
if not result.wasSuccessful() or result.skipped: raise SystemExit(1)
'''
        (root / 'runner.py').write_text(runner)
        expression = f'''let
 py = builtins.storePath {PYTHON};
 sh = builtins.storePath {BASH};
in derivation {{
 name = "tertius-issue37-external-launch-proof"; system = "aarch64-linux";
 builder = "${{py}}/bin/python3"; args = [ "-I" ./runner.py ];
 source = ./source; PATH = "${{py}}/bin:${{sh}}/bin";
}}
'''
        (root / 'proof.nix').write_text(expression)
        shutil.copyfile(root / 'proof.nix', args.out / 'proof.nix')
        shutil.copyfile(root / 'runner.py', args.out / 'runner.py')
        result = subprocess.run(['nix', 'build', '--offline', '--impure', '--file', str(root / 'proof.nix'),
            '--no-link', '--print-out-paths', '-L'], capture_output=True, text=True)
        (args.out / 'build.log').write_text(result.stderr)
        result.check_returncode()
        output = Path(result.stdout.strip())
        shutil.copyfile(output, args.out / 'result.json')
        (args.out / 'store-output').write_text(str(output) + '\n')
        print((args.out / 'result.json').read_text(), end='')


if __name__ == '__main__':
    main()

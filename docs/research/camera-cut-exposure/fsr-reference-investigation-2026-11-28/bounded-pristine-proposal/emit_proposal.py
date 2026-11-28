"""Emit an inert review copy of the existing canonical submit command. Never submit."""
import argparse
import base64
import hashlib
import json
from pathlib import Path


def emit(root, out):
    """Embed this proposal's exact inputs and output globs behind an unconditional PowerShell stop."""
    config = json.loads((root / 'config.json').read_text())
    if (config['approval']['coordinator_reviewed'] or config['approval']['issue4_explicit_paid_approval']
            or config['approval']['authorized_submissions'] != 0 or config['submitted']):
        raise ValueError('This emitter accepts only the inert proposal.')
    payload = {name: (root / name).read_bytes() for name in
               ['worker.py', 'fixture.py', 'analyze_runs.py', 'config.json']}
    for folder in ('linux-scripts', 'retained-diagnostics'):
        for path in sorted((root / folder).rglob('*')):
            if path.is_file():
                payload[str(path.relative_to(root))] = path.read_bytes()
    encoded = {name: base64.b64encode(data).decode() for name, data in payload.items()}
    bootstrap = ('import base64,json,pathlib,subprocess,sys\n'
        + 'payload=json.loads(' + repr(json.dumps(encoded)) + ')\n'
        + 'for name,data in payload.items():\n'
        + ' path=pathlib.Path(name);path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(base64.b64decode(data))\n'
        + 'subprocess.run([sys.executable,"-I","worker.py","--config","config.json"],check=True)\n')
    shell = "python3 -c 'import base64;exec(base64.b64decode(\"" + base64.b64encode(bootstrap.encode()).decode() + "\"))'"
    argv = ['python3', '-I', 'tools/runpod/gpu_job.py', 'submit', '--agent', 'issue4-exposure',
        '--tree', 'before=' + config['baseline_sha'], '--tree', 'after=' + config['candidate_sha'],
        '--run', shell, '--timeout', str(config['command_timeout_seconds']),
        '--out', config['result_folder'] or 'INERT-parent-must-select-result-folder']
    for pattern in config['output_globs']:
        argv += ['--output', pattern]
    command = ("throw 'INERT: duration policy and paid submission remain unapproved. This is a review copy only.'\n"
        + '& ' + ' '.join("'" + word.replace("'", "''") + "'" for word in argv) + '\n')
    out.write_text(command)
    metadata = {'submitted': False, 'proposal_only': True, 'approval': config['approval'],
        'output_globs': config['output_globs'], 'command_timeout_seconds': config['command_timeout_seconds'],
        'conditional_execution_seconds': config['canonical_execution_timeout_seconds'],
        'conditional_canonical_reservation_usd': config['worst_case_usd'],
        'launchable_under_checked_in_policy': False,
        'payload_sha256': {name: hashlib.sha256(data).hexdigest() for name, data in payload.items()},
        'submit_command_sha256': hashlib.sha256(command.encode()).hexdigest()}
    out.with_suffix('.json').write_text(json.dumps(metadata, indent=2) + '\n')
    return metadata


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    emit(Path(__file__).resolve().parent, args.out)

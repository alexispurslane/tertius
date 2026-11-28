"""Verify immutable named-tree inputs, then run the unchanged measurement worker."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys


def prepare(root, work, manifest_sha256):
    """Check every input before copying anything into the canonical job's work folder."""
    manifest = (root / 'payload-manifest.json').read_bytes()
    if hashlib.sha256(manifest).hexdigest() != manifest_sha256:
        raise ValueError('Payload manifest hash differs')
    payload = {}
    for name, digest in json.loads(manifest).items():
        source = root / name
        if Path(name).is_absolute() or '..' in Path(name).parts:
            raise ValueError('Payload path leaves its tree: ' + name)
        if not source.is_file():
            raise ValueError('Payload file missing: ' + name)
        data = source.read_bytes()
        if hashlib.sha256(data).hexdigest() != digest:
            raise ValueError('Payload hash differs: ' + name)
        if (work / name).exists():
            raise ValueError('Payload destination already exists: ' + name)
        payload[name] = data
    for name, data in payload.items():
        target = work / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest-sha256', required=True)
    args = parser.parse_args()
    prepare(Path(__file__).resolve().parent, Path.cwd(), args.manifest_sha256)
    subprocess.run([sys.executable, '-I', 'worker.py', '--config', 'config.json'], check=True)

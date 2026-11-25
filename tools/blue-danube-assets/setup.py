"""Tool code. Sets up the Blue Danube art of a checkout: fetches every file that a sidecar pins into the source cache,
checks its SHA-256, and converts each model into a .glb in engine axes with headless Blender (tools/README.md, "The
Blue Danube art"). Run it through setup.sh, which enters the assets dev shell:

    bash tools/blue-danube-assets/setup.sh [--staging <folder>] [--only <model>] [--force]

--staging names a folder that may hold copies of the files; a copy with the pinned hash is used instead of a download.
--only converts the one model whose path holds the text. --force converts even when the stamp says that the .glb is
current.

Exit codes: 0 when every model is current, 1 for a wrong command line, 2 for a fetch that failed, 3 for a conversion
that failed."""

import argparse
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
PACKAGE = REPO / "content" / "tertius.demo"
ART = PACKAGE / "assets" / "blue-danube"
CACHE = PACKAGE / "bin" / "sources" / "blue-danube"
COOKED = PACKAGE / "bin" / "assets" / "blue-danube"


def _load(name):
    spec = importlib.util.spec_from_file_location(name, HERE / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


fetch = _load("fetch")


def main(argv):
    parser = argparse.ArgumentParser(prog="setup.sh")
    parser.add_argument("--staging", type=Path)
    parser.add_argument("--only")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args(argv)
    if args.staging is not None and not args.staging.is_dir():
        print(f"--staging {args.staging} is not a folder.", file=sys.stderr)
        return 1

    sidecars = [s for s in fetch.load_sidecars(ART) if args.only is None or args.only in s.model]
    if not sidecars:
        print(f"No sidecar in {ART} matches {args.only!r}.", file=sys.stderr)
        return 1
    staging = fetch.Staging(args.staging)
    try:
        for sidecar in sidecars:
            fetch.check_approval(sidecar.source)
            for path, entry in sorted(sidecar.source["files"].items()):
                status = fetch.ensure_file(entry, sidecar.cached(path, CACHE), staging, fetch.http_download)
                if status != "ready":
                    print(f"{sidecar.model}: {path} {status}")
    except fetch.FetchError as e:
        print(f"The fetch failed: {e}", file=sys.stderr)
        return 2

    failed = []
    for sidecar in sidecars:
        if not _convert(sidecar, args.force):
            failed.append(sidecar.model)
    if failed:
        print(f"The conversion failed for {', '.join(failed)}. See convert.log in each cooked folder.", file=sys.stderr)
        return 3
    return 0


def _stamp(sidecar):
    """The SHA-256 of everything that the .glb depends on: the sidecar, the pinned hash of each source file, the
    converter's code, the overlay script and its pinned replacement maps, and the Blender version. The same stamp
    means the same .glb, so setup skips the conversion."""
    digest = hashlib.sha256()
    digest.update(sidecar.path.read_bytes())
    for path, entry in sorted(sidecar.source["files"].items()):
        digest.update(f"{path}={entry['sha256']}\n".encode())
    code = sorted(HERE.glob("*.py")) + [REPO / "tools" / "blender" / "tertius_blender" / "export.py"]
    overlay = sidecar.convert.get("overlay", {}).get("script")
    if overlay and (REPO / overlay).exists():
        code.append(REPO / overlay)
    for file in code:
        digest.update(file.name.encode() + b"\0" + file.read_bytes())
    digest.update(os.environ.get("TERTIUS_BLENDER_VERSION", "unknown").encode())
    return "sha256:" + digest.hexdigest()


def _convert(sidecar, force):
    glb = sidecar.cooked(COOKED)
    folder = glb.parent
    stamp_file = folder / "model.stamp"
    stamp = _stamp(sidecar)
    if not force and glb.exists() and stamp_file.exists() and stamp_file.read_text().strip() == stamp:
        print(f"{sidecar.model}: current")
        return True
    folder.mkdir(parents=True, exist_ok=True)
    stamp_file.unlink(missing_ok=True)
    report = folder / "report.json"
    blender = os.environ.get("TERTIUS_BLENDER", "blender")
    command = [blender, "--background", "--factory-startup", "--disable-autoexec", "--threads", "1",
               "--python-exit-code", "3", "--python", str(HERE / "convert.py"), "--",
               "--sidecar", str(sidecar.path), "--model", sidecar.model,
               "--sources", str(CACHE / sidecar.folder), "--out", str(glb), "--report", str(report)]
    started = time.monotonic()
    with open(folder / "convert.log", "w", encoding="utf-8") as log:
        result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, check=False)
    seconds = time.monotonic() - started
    if result.returncode != 0 or not glb.exists():
        return False
    stamp_file.write_text(stamp + "\n")
    summary = json.loads(report.read_text(encoding="utf-8"))
    triangles = sum(p["triangles"] for p in summary["pieces"].values())
    print(f"{sidecar.model}: converted in {seconds:.0f} s, {len(summary['pieces'])} pieces, {triangles} triangles, "
          f"{glb.stat().st_size / 1e6:.1f} MB")
    return True


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

"""Evidence script. Applies identical patches to plain archives, tests, and restores tracked source bytes.
Run from the ticket checkout inside its Nix dev shell. The two refs use the same flake and lock.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tarfile
import time

OUT = Path(__file__).resolve().parent
ROOT = Path.cwd()
REFS = {
    "baseline": "19e17c5ba364392ca6bf84b00c11a7d709771ded",
    "candidate": "468dc19d0f5e247819f24bfaea297919439c7b4f",
}
PRODUCTION = "src/Tertius.Rendering/Vulkan/DeviceAllocator.cs"
TESTS = "tests/Tertius.Rendering.Tests/DeviceAllocatorTests.cs"
PATCHES = [OUT / "allocator.patch", OUT / "regressions.patch"]

def sha(path):
    return hashlib.sha256(os.fsencode(os.readlink(path)) if path.is_symlink() else path.read_bytes()).hexdigest()

def git(*args, cwd=ROOT):
    return subprocess.check_output(["git", *args], cwd=cwd)

def manifest(tree, paths):
    return {p: sha(tree / p) for p in paths}

results = []
for name, ref in REFS.items():
    start = time.time()
    archive = OUT / (name + "-input.tar")
    tree = OUT / (name + "-verification-source")
    resumed_before_manifest = archive.exists() and tree.exists() and not (OUT / (name + "-original-manifest.json")).exists()
    if not resumed_before_manifest:
        if archive.exists() or tree.exists():
            raise SystemExit("Refusing to overwrite earlier verification: " + name)
        git("archive", "--format=tar", "--output=" + str(archive), ref)
        archive.chmod(0o444)
        tree.mkdir()
        with tarfile.open(archive) as stream:
            stream.extractall(tree, filter="data")
    objects = git("ls-tree", "-r", "-z", ref).decode().strip("\0").split("\0")
    for entry in objects:
        info, path = entry.split("\t", 1)
        expected_blob = info.split()[2]
        file = tree / path
        data = os.fsencode(os.readlink(file)) if file.is_symlink() else file.read_bytes()
        actual_blob = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
        assert actual_blob == expected_blob, path
    paths = git("ls-tree", "-r", "--name-only", "-z", ref).decode().strip("\0").split("\0")
    original = manifest(tree, paths)
    (OUT / (name + "-original-manifest.json")).write_text(json.dumps(original, indent=2) + "\n")
    row = dict(name=name, ref=ref, archive=str(archive), archive_sha256=sha(archive),
        git_metadata_present=(tree/".git").exists(), all_original_git_blobs_verified=True, resumed_before_manifest=resumed_before_manifest,
        patches={p.name:sha(p) for p in PATCHES},
        base_allocator_blob=git("rev-parse", ref+":"+PRODUCTION).decode().strip(),
        production_before_sha256=original[PRODUCTION],
        command=["dotnet", "test", "--project", "tests/Tertius.Rendering.Tests", "-c", "Checked", "--filter-class", "*.DeviceAllocatorTests"])
    applied = []
    try:
        for patch in PATCHES:
            subprocess.run(["git", "apply", "--check", str(patch)], cwd=tree, check=True)
            subprocess.run(["git", "apply", str(patch)], cwd=tree, check=True)
            applied.append(patch)
        fixed = manifest(tree, paths)
        changed = [p for p in paths if fixed[p] != original[p]]
        assert set(changed) == {PRODUCTION, TESTS}, changed
        row.update(apply_check=True, changed_paths=changed, production_after_sha256=fixed[PRODUCTION],
            final_allocator_blob=git("hash-object", str(tree/PRODUCTION)).decode().strip())
        with (OUT/(name+"-tests.log")).open("w") as log:
            run = subprocess.run(row["command"], cwd=tree, stdout=log, stderr=subprocess.STDOUT)
        row["test_exit"] = run.returncode
        row["test_log"] = str(OUT/(name+"-tests.log"))
        row["patched_source_unchanged_by_build"] = manifest(tree, paths) == fixed
        assert row["patched_source_unchanged_by_build"]
    finally:
        for patch in reversed(applied):
            subprocess.run(["git", "apply", "--reverse", str(patch)], cwd=tree, check=True)
        row["restored_original_source"] = manifest(tree, paths) == original
        row["elapsed_seconds"] = time.time()-start
        results.append(row)
        (OUT/"pinned-input-verification.json").write_text(json.dumps(results,indent=2)+"\n")
    assert row["test_exit"] == 0, row
    assert row["restored_original_source"], row
    print(json.dumps(row), flush=True)

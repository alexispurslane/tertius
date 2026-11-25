"""Tool code. The fetch of the Blue Danube art: the sidecars, their approval records, and the source cache that holds
exactly the pinned bytes of each fetched file (docs/plans/asset-pipeline.md, "Fetched sources").

The standard library only, so that the tests run in the default dev shell."""

import hashlib
import json
import os
import shutil
import urllib.request
from dataclasses import dataclass
from pathlib import Path

SIDECAR_SUFFIX = ".import.json"

# A generic client name: the requests carry nothing about the user or the machine.
USER_AGENT = "tertius-asset-setup/1"


class FetchError(Exception):
    """A fetch that cannot give the pinned bytes. The message starts with the code of asset-pipeline.md: AS9005 for a
    request that failed, AS9006 for bytes that differ from the pin, AS9007 for a record that differs from its
    approval."""


@dataclass(frozen=True)
class Sidecar:
    """One sidecar: the provenance and fetch record (`source`) and the conversion (`convert`) of one model.

    `model` is the path of the source file relative to the art folder, such as `lunar-shuttle/lunarshuttle.model`. It
    names the model's cooked folder too."""

    path: Path
    model: str
    source: dict
    convert: dict

    @property
    def folder(self):
        """The folder of the sidecar relative to the art folder."""
        return Path(self.model).parent

    def cooked(self, assets_out):
        """The .glb that the conversion writes, in the cooked folder named after the source file."""
        return assets_out / self.model / "model.glb"

    def cached(self, path, cache):
        """Where the fetched file `path` of the record lies in the source cache `cache`."""
        return cache / self.folder / path


def load_sidecars(art):
    """Every sidecar below the folder `art`, in the order of their model paths."""
    sidecars = []
    for path in art.rglob("*" + SIDECAR_SUFFIX):
        text = json.loads(path.read_text(encoding="utf-8"))
        model = path.relative_to(art).as_posix()[: -len(SIDECAR_SUFFIX)]
        sidecars.append(Sidecar(path=path, model=model, source=text["source"], convert=text["convert"]))
    return sorted(sidecars, key=lambda s: s.model)


def sha256_bytes(data):
    return "sha256:" + hashlib.sha256(data).hexdigest()


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            digest.update(block)
    return "sha256:" + digest.hexdigest()


def approval_record(source):
    """The SHA-256 of the canonical JSON of the fetch record: its files and archives, sorted keys, no spaces."""
    record = {"archives": source.get("archives", {}), "files": source["files"]}
    return sha256_bytes(json.dumps(record, sort_keys=True, separators=(",", ":")).encode())


def check_approval(source):
    """Raises AS9007 unless the fetch record is the one that the user approved."""
    approved = source.get("approved", {}).get("record")
    actual = approval_record(source)
    if approved != actual:
        raise FetchError(f"AS9007: the fetch record ({actual}) is not the approved one ({approved}). A changed file "
                         "needs a new approval.")


class Staging:
    """A local folder that may hold copies of fetched files at any path, such as the folder where a person first
    downloaded them. A copy counts only when its size and SHA-256 equal the pin."""

    def __init__(self, root):
        self._by_size = {}
        if root is None:
            return
        for path in sorted(Path(root).rglob("*")):
            if path.is_file():
                self._by_size.setdefault(path.stat().st_size, []).append(path)

    def find(self, size, sha256):
        for path in self._by_size.get(size, []):
            if sha256_file(path) == sha256:
                return path
        return None


def http_download(url, limit):
    """The bytes at `url`, at most `limit` + 1 of them, so that a file that grew cannot fill the disk."""
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": USER_AGENT}),
                                    timeout=60) as response:
            return response.read(limit + 1)
    except OSError as e:
        raise FetchError(f"AS9005: {url} did not answer: {e}") from e


def ensure_file(entry, dest, staging, download):
    """Puts the pinned bytes of the record entry `entry` (`url`, `bytes`, `sha256`) at `dest`, or raises FetchError and
    leaves no file there. Returns "ready" for a cached file with the pinned hash, "staged" for a copy from the staging
    folder, and "downloaded". A cached file with another hash counts as missing."""
    size, sha256 = entry["bytes"], entry["sha256"]
    if dest.exists():
        if dest.stat().st_size == size and sha256_file(dest) == sha256:
            return "ready"
        dest.unlink()
    dest.parent.mkdir(parents=True, exist_ok=True)
    temporary = dest.with_name(f"{dest.name}.tmp-{os.getpid()}")
    staged = staging.find(size, sha256)
    if staged is not None:
        shutil.copyfile(staged, temporary)
        os.replace(temporary, dest)
        return "staged"
    data = download(entry["url"], size)
    if len(data) != size or sha256_bytes(data) != sha256:
        raise FetchError(f"AS9006: {entry['url']} served {len(data)} bytes with {sha256_bytes(data)}, not the pinned "
                         f"{size} bytes with {sha256}.")
    temporary.write_bytes(data)
    os.replace(temporary, dest)
    return "downloaded"
